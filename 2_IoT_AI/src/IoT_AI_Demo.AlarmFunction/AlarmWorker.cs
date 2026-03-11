using System.Text.Json;
using Azure.Messaging.ServiceBus;
using IoT_AI_Demo.Shared;
using Npgsql;

namespace IoT_AI_Demo.AlarmFunction;

public sealed class AlarmWorker(
    ServiceBusClient serviceBus,
    NpgsqlDataSource db,
    ILogger<AlarmWorker> logger) : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await EnsureTableAsync(stoppingToken);

        var processor = serviceBus.CreateProcessor("alarms");

        processor.ProcessMessageAsync += async args =>
        {
            var body = args.Message.Body.ToString();
            var telemetry = JsonSerializer.Deserialize<TelemetryMessage>(body, JsonOptions);
            if (telemetry is null) return;

            // Evaluate alarm rules
            var sensor = AlarmEvaluator.FindSensor(telemetry.DeviceId);
            if (sensor is null)
            {
                await args.CompleteMessageAsync(args.Message, args.CancellationToken);
                return; // actuator or unknown device — skip
            }

            var alarm = AlarmEvaluator.Evaluate(telemetry, sensor);
            if (alarm is not null)
            {
                // Persist alarm to database for Grafana annotations
                await StoreAlarmAsync(alarm, args.CancellationToken);

                var level = alarm.AlarmLevel switch
                {
                    AlarmLevel.HH => "CRITICAL",
                    AlarmLevel.LL => "CRITICAL",
                    AlarmLevel.H => "WARNING",
                    AlarmLevel.L => "WARNING",
                    _ => "INFO"
                };

                logger.LogWarning(
                    "[{Level}] {AlarmLevel} alarm on {DeviceId}: {Value} {Unit} — {Description}",
                    level, alarm.AlarmLevel, alarm.DeviceId, alarm.Value, alarm.Unit, alarm.Description);

                // Start AI analysis orchestration via Service Bus
                await SendToAnalysisAsync(alarm, telemetry, args.CancellationToken);
            }

            await args.CompleteMessageAsync(args.Message, args.CancellationToken);
        };

        processor.ProcessErrorAsync += args =>
        {
            logger.LogError(args.Exception, "Service Bus processor error");
            return Task.CompletedTask;
        };

        await processor.StartProcessingAsync(stoppingToken);
        logger.LogInformation("AlarmWorker started processing Service Bus queue 'alarms'");

        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    private async Task StoreAlarmAsync(AlarmMessage alarm, CancellationToken ct)
    {
        const string sql = """
            INSERT INTO alarms (device_id, alarm_level, value, threshold, timestamp, description)
            VALUES ($1, $2, $3, $4, $5, $6)
            """;

        var threshold = AlarmEvaluator.FindSensor(alarm.DeviceId) switch
        {
            { } s => alarm.AlarmLevel switch
            {
                AlarmLevel.HH => s.HH ?? 0,
                AlarmLevel.H => s.H ?? 0,
                AlarmLevel.L => s.L ?? 0,
                AlarmLevel.LL => s.LL ?? 0,
                _ => 0
            },
            _ => 0
        };

        await using var cmd = db.CreateCommand(sql);
        cmd.Parameters.AddWithValue(alarm.DeviceId);
        cmd.Parameters.AddWithValue(alarm.AlarmLevel.ToString());
        cmd.Parameters.AddWithValue(alarm.Value);
        cmd.Parameters.AddWithValue(threshold);
        cmd.Parameters.AddWithValue(alarm.Timestamp);
        cmd.Parameters.AddWithValue(alarm.Description);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private async Task SendToAnalysisAsync(AlarmMessage alarm, TelemetryMessage telemetry, CancellationToken ct)
    {
        var input = new AlarmAnalysisInput(alarm, telemetry);
        await using var sender = serviceBus.CreateSender("alarm-analysis");
        var json = JsonSerializer.Serialize(input, JsonOptions);
        await sender.SendMessageAsync(new Azure.Messaging.ServiceBus.ServiceBusMessage(json), ct);

        logger.LogInformation("Sent alarm for AI analysis: {DeviceId} — {AlarmLevel}",
            alarm.DeviceId, alarm.AlarmLevel);
    }

    private async Task EnsureTableAsync(CancellationToken ct)
    {
        const string sql = """
            CREATE TABLE IF NOT EXISTS alarms (
                id BIGSERIAL PRIMARY KEY,
                device_id TEXT NOT NULL,
                alarm_level TEXT NOT NULL,
                value DOUBLE PRECISION NOT NULL,
                threshold DOUBLE PRECISION NOT NULL,
                timestamp TIMESTAMPTZ NOT NULL,
                description TEXT NOT NULL
            )
            """;

        await using var cmd = db.CreateCommand(sql);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
