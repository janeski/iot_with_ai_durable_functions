using System.Text.Json;
using IoT_AI_Demo.Shared;
using MQTTnet;
using MQTTnet.Formatter;

namespace IoT_AI_Demo.DeviceSimulator;

public sealed class SimulatorWorker(IConfiguration config, ILogger<SimulatorWorker> logger) : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private readonly Random _random = new();
    private readonly Dictionary<string, double> _currentValues = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var host = config["Mqtt:Host"] ?? "localhost";
        var port = int.Parse(config["Mqtt:Port"] ?? "1883");

        logger.LogInformation("Connecting to MQTT broker at {Host}:{Port}", host, port);

        var client = new MqttClientFactory().CreateMqttClient();
        var options = new MqttClientOptionsBuilder()
            .WithTcpServer(host, port)
            .WithProtocolVersion(MqttProtocolVersion.V500)
            .WithClientId("device-simulator")
            .Build();

        await client.ConnectAsync(options, stoppingToken);
        logger.LogInformation("Connected to MQTT broker");

        // Initialize sensor values at midpoint of normal range
        foreach (var sensor in DeviceDefinitions.Sensors)
            _currentValues[sensor.DeviceId] = (sensor.NormalMin + sensor.NormalMax) / 2.0;

        // Track next publish time per sensor
        var nextPublish = DeviceDefinitions.Sensors.ToDictionary(s => s.DeviceId, _ => DateTimeOffset.UtcNow);
        var actuatorState = DeviceDefinitions.Actuators.ToDictionary(a => a.DeviceId, _ => 0.0);
        var nextToggle = DeviceDefinitions.Actuators.ToDictionary(a => a.DeviceId, _ => DateTimeOffset.UtcNow);

        while (!stoppingToken.IsCancellationRequested)
        {
            var now = DateTimeOffset.UtcNow;

            // Publish sensor telemetry
            foreach (var sensor in DeviceDefinitions.Sensors)
            {
                if (now < nextPublish[sensor.DeviceId]) continue;

                var value = GenerateSensorValue(sensor);
                _currentValues[sensor.DeviceId] = value;

                var msg = new TelemetryMessage(sensor.DeviceId, sensor.DeviceType, now, Math.Round(value, 2), sensor.Unit);
                await PublishAsync(client, msg, stoppingToken);

                nextPublish[sensor.DeviceId] = now + sensor.PublishInterval;
            }

            // Publish actuator telemetry on state change
            foreach (var actuator in DeviceDefinitions.Actuators)
            {
                if (now < nextToggle[actuator.DeviceId]) continue;

                actuatorState[actuator.DeviceId] = actuatorState[actuator.DeviceId] == 0 ? 1 : 0;
                var msg = new TelemetryMessage(actuator.DeviceId, actuator.DeviceType, now, actuatorState[actuator.DeviceId], actuator.Unit);
                await PublishAsync(client, msg, stoppingToken);

                nextToggle[actuator.DeviceId] = now + actuator.ToggleInterval;
            }

            await Task.Delay(500, stoppingToken);
        }

        await client.DisconnectAsync(new MqttClientDisconnectOptionsBuilder().Build(), stoppingToken);
    }

    private double GenerateSensorValue(SensorConfig sensor)
    {
        var current = _currentValues[sensor.DeviceId];
        var range = sensor.NormalMax - sensor.NormalMin;
        var drift = (_random.NextDouble() - 0.5) * range * 0.01; // ±0.5% of range

        // ~5% chance to spike into alarm zone
        if (_random.NextDouble() < 0.05)
        {
            var spikeHigh = _random.NextDouble() > 0.5;
            if (spikeHigh && sensor.HH.HasValue)
                return sensor.HH.Value + _random.NextDouble() * (sensor.HH.Value - sensor.NormalMax);
            if (!spikeHigh && sensor.LL.HasValue)
                return sensor.LL.Value - _random.NextDouble() * (sensor.NormalMin - sensor.LL.Value);
        }

        // Normal drift, clamped to a reasonable range
        var next = current + drift;
        return Math.Clamp(next, sensor.NormalMin - range * 0.1, sensor.NormalMax + range * 0.1);
    }

    private async Task PublishAsync(IMqttClient client, TelemetryMessage msg, CancellationToken ct)
    {
        var topic = $"fishtank/{msg.DeviceId}/telemetry";
        var payload = JsonSerializer.Serialize(msg, JsonOptions);

        var mqttMsg = new MqttApplicationMessageBuilder()
            .WithTopic(topic)
            .WithPayload(payload)
            .Build();

        await client.PublishAsync(mqttMsg, ct);
        logger.LogInformation("[{DeviceId}] {Value} {Unit}", msg.DeviceId, msg.Value, msg.Unit);
    }
}
