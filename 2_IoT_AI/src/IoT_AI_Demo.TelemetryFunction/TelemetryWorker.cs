using System.Text.Json;
using Azure.Messaging.ServiceBus;
using IoT_AI_Demo.Shared;
using MQTTnet;
using MQTTnet.Formatter;
using Npgsql;

namespace IoT_AI_Demo.TelemetryFunction;

public sealed class TelemetryWorker(
    IConfiguration config,
    NpgsqlDataSource db,
    ServiceBusClient serviceBus,
    ILogger<TelemetryWorker> logger) : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await EnsureTableAsync(stoppingToken);

        var host = config["Mqtt:Host"] ?? "localhost";
        var port = int.Parse(config["Mqtt:Port"] ?? "1883");

        logger.LogInformation("Connecting to MQTT broker at {Host}:{Port}", host, port);

        var client = new MqttClientFactory().CreateMqttClient();

        client.ApplicationMessageReceivedAsync += async e =>
        {
            try
            {
                await HandleMessageAsync(e, stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error processing MQTT message");
            }
        };

        var options = new MqttClientOptionsBuilder()
            .WithTcpServer(host, port)
            .WithProtocolVersion(MqttProtocolVersion.V500)
            .WithClientId("telemetry-function")
            .Build();

        await client.ConnectAsync(options, stoppingToken);

        var subscribeOptions = new MqttClientSubscribeOptionsBuilder()
            .WithTopicFilter("fishtank/+/telemetry")
            .Build();

        await client.SubscribeAsync(subscribeOptions, stoppingToken);
        logger.LogInformation("Subscribed to fishtank/+/telemetry");

        // Keep running until cancelled
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    private async Task HandleMessageAsync(MqttApplicationMessageReceivedEventArgs e, CancellationToken ct)
    {
        var payload = e.ApplicationMessage.ConvertPayloadToString();
        var telemetry = JsonSerializer.Deserialize<TelemetryMessage>(payload, JsonOptions);
        if (telemetry is null) return;

        logger.LogInformation("[{DeviceId}] {Value} {Unit}", telemetry.DeviceId, telemetry.Value, telemetry.Unit);

        // Store in PostgreSQL
        await StoreTelemetryAsync(telemetry, ct);

        // Forward telemetry to AlarmFunction via Service Bus for alarm evaluation
        await SendTelemetryToServiceBusAsync(telemetry, ct);
    }

    private async Task StoreTelemetryAsync(TelemetryMessage t, CancellationToken ct)
    {
        const string sql = """
            INSERT INTO telemetry (device_id, device_type, timestamp, value, unit)
            VALUES ($1, $2, $3, $4, $5)
            """;

        await using var cmd = db.CreateCommand(sql);
        cmd.Parameters.AddWithValue(t.DeviceId);
        cmd.Parameters.AddWithValue(t.DeviceType);
        cmd.Parameters.AddWithValue(t.Timestamp);
        cmd.Parameters.AddWithValue(t.Value);
        cmd.Parameters.AddWithValue(t.Unit);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private async Task SendTelemetryToServiceBusAsync(TelemetryMessage telemetry, CancellationToken ct)
    {
        await using var sender = serviceBus.CreateSender("alarms");
        var json = JsonSerializer.Serialize(telemetry, JsonOptions);
        await sender.SendMessageAsync(new ServiceBusMessage(json), ct);
    }

    private async Task EnsureTableAsync(CancellationToken ct)
    {
        const string sql = """
            CREATE TABLE IF NOT EXISTS telemetry (
                id BIGSERIAL PRIMARY KEY,
                device_id TEXT NOT NULL,
                device_type TEXT NOT NULL,
                timestamp TIMESTAMPTZ NOT NULL,
                value DOUBLE PRECISION NOT NULL,
                unit TEXT NOT NULL
            )
            """;

        await using var cmd = db.CreateCommand(sql);
        await cmd.ExecuteNonQueryAsync(ct);
        logger.LogInformation("Telemetry table ensured");
    }
}
