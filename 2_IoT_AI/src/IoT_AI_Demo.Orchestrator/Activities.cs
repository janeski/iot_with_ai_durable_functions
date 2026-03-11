using System.Text.Json;
using IoT_AI_Demo.Shared;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace IoT_AI_Demo.Orchestrator;

public sealed class Activities(
    NpgsqlDataSource db,
    AiAnalyzer aiAnalyzer,
    AlarmEmbeddingService embeddingService,
    ILogger<Activities> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    // ── Step 1: Fetch recent telemetry ──────────────────────────────────

    [Function(nameof(FetchRecentTelemetry))]
    public async Task<List<TelemetryMessage>> FetchRecentTelemetry(
        [ActivityTrigger] string deviceId)
    {
        const string sql = """
            SELECT device_id, device_type, timestamp, value, unit
            FROM telemetry
            WHERE device_id = $1 AND timestamp > $2
            ORDER BY timestamp DESC
            LIMIT 50
            """;

        var results = new List<TelemetryMessage>();
        await using var cmd = db.CreateCommand(sql);
        cmd.Parameters.AddWithValue(deviceId);
        cmd.Parameters.AddWithValue(DateTimeOffset.UtcNow.AddMinutes(-10));

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(new TelemetryMessage(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetFieldValue<DateTimeOffset>(2),
                reader.GetDouble(3),
                reader.GetString(4)));
        }

        logger.LogInformation("Fetched {Count} telemetry records for {DeviceId}", results.Count, deviceId);
        return results;
    }

    // ── Step 2: Fetch device / asset context ────────────────────────────

    [Function(nameof(FetchDeviceContext))]
    public Task<DeviceContext> FetchDeviceContext(
        [ActivityTrigger] string deviceId)
    {
        var sensor = DeviceDefinitions.Sensors.FirstOrDefault(s => s.DeviceId == deviceId);
        if (sensor is null)
            return Task.FromResult(new DeviceContext(deviceId, "Unknown", "", 0, 0, null, null));

        return Task.FromResult(new DeviceContext(
            sensor.DeviceId,
            sensor.DeviceType,
            sensor.Unit,
            sensor.NormalMin,
            sensor.NormalMax,
            Location: "Fish Tank A",
            AssetGroup: "Aquaculture"));
    }

    // ── Step 3a: Search similar past alarms (RAG retrieval) ─────────

    [Function(nameof(SearchSimilarAlarms))]
    public async Task<List<SimilarAlarmResult>> SearchSimilarAlarms(
        [ActivityTrigger] AlarmMessage alarm)
    {
        try
        {
            await embeddingService.EnsureSchemaAsync();
            var text = AlarmEmbeddingService.BuildAlarmText(alarm);
            var embedding = await embeddingService.GenerateEmbeddingAsync(text);
            return await embeddingService.SearchSimilarAsync(embedding);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Similar alarm search failed for {DeviceId} — continuing without RAG context", alarm.DeviceId);
            return [];
        }
    }

    // ── Step 3b: Call AI analysis ───────────────────────────────────────

    [Function(nameof(CallAiAnalysis))]
    public async Task<AiAnalysisResult?> CallAiAnalysis(
        [ActivityTrigger] AiAnalysisInput input)
    {
        try
        {
            return await aiAnalyzer.AnalyzeAsync(
                input.Alarm, input.RecentTelemetry, input.DeviceContext, input.SimilarAlarms);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "AI analysis failed for {DeviceId}", input.Alarm.DeviceId);
            return null;
        }
    }

    // ── Step 4: Validate AI result ──────────────────────────────────────

    [Function(nameof(ValidateResult))]
    public Task<AiAnalysisResult> ValidateResult(
        [ActivityTrigger] AiAnalysisResult result)
    {
        var validSeverities = new[] { "CRITICAL", "WARNING", "INFO" };

        if (string.IsNullOrWhiteSpace(result.RootCause) ||
            string.IsNullOrWhiteSpace(result.Summary) ||
            !validSeverities.Contains(result.AdjustedSeverity))
        {
            logger.LogWarning("AI result validation failed — adjusting fields");
            return Task.FromResult(result with
            {
                AdjustedSeverity = validSeverities.Contains(result.AdjustedSeverity)
                    ? result.AdjustedSeverity : "WARNING",
                RootCause = string.IsNullOrWhiteSpace(result.RootCause)
                    ? "Undetermined" : result.RootCause,
                Summary = string.IsNullOrWhiteSpace(result.Summary)
                    ? "Analysis completed with limited data" : result.Summary,
            });
        }

        logger.LogInformation("AI result validated successfully");
        return Task.FromResult(result);
    }

    // ── Step 5a: Update PostgreSQL ──────────────────────────────────────

    [Function(nameof(UpdatePostgreSql))]
    public async Task UpdatePostgreSql([ActivityTrigger] ActionInput input)
    {
        await EnsureAnalysisTableAsync();

        const string sql = """
            INSERT INTO alarm_analyses
                (device_id, alarm_level, adjusted_severity, value, root_cause, recommended_actions, summary, timestamp)
            VALUES ($1, $2, $3, $4, $5, $6, $7, $8)
            """;

        await using var cmd = db.CreateCommand(sql);
        cmd.Parameters.AddWithValue(input.Alarm.DeviceId);
        cmd.Parameters.AddWithValue(input.Alarm.AlarmLevel.ToString());
        cmd.Parameters.AddWithValue(input.Analysis.AdjustedSeverity);
        cmd.Parameters.AddWithValue(input.Alarm.Value);
        cmd.Parameters.AddWithValue(input.Analysis.RootCause);
        cmd.Parameters.AddWithValue(JsonSerializer.Serialize(input.Analysis.RecommendedActions, JsonOptions));
        cmd.Parameters.AddWithValue(input.Analysis.Summary);
        cmd.Parameters.AddWithValue(input.Alarm.Timestamp);
        await cmd.ExecuteNonQueryAsync();

        logger.LogInformation("Stored alarm analysis for {DeviceId}", input.Alarm.DeviceId);
    }

    // ── Step 5b: Notify via SendGrid ────────────────────────────────────

    [Function(nameof(NotifyViaSendGrid))]
    public Task NotifyViaSendGrid([ActivityTrigger] ActionInput input)
    {
        // Stub — in production, send enriched email via SendGrid
        logger.LogInformation(
            "[SendGrid Stub] Would notify for {DeviceId}: {Summary}",
            input.Alarm.DeviceId, input.Analysis.Summary);
        return Task.CompletedTask;
    }

    // ── Step 5c: Create maintenance ticket ──────────────────────────────

    [Function(nameof(CreateMaintenanceTicket))]
    public Task CreateMaintenanceTicket([ActivityTrigger] ActionInput input)
    {
        // Stub — in production, call external API to create work order
        if (input.Analysis.AdjustedSeverity == "CRITICAL")
        {
            logger.LogInformation(
                "[Maintenance Stub] Would create ticket for {DeviceId}: {RootCause}",
                input.Alarm.DeviceId, input.Analysis.RootCause);
        }
        return Task.CompletedTask;
    }

    // ── Step 5d: Update dashboard / twin ────────────────────────────────

    [Function(nameof(UpdateDashboard))]
    public Task UpdateDashboard([ActivityTrigger] ActionInput input)
    {
        // The alarm analysis is already persisted to PostgreSQL by UpdatePostgreSql.
        // Grafana picks it up automatically on refresh.
        // In production, this could update a device twin or push to a real-time cache.
        logger.LogInformation(
            "[Dashboard] Analysis available for {DeviceId} — Grafana will refresh",
            input.Alarm.DeviceId);
        return Task.CompletedTask;
    }

    // ── Step 6: Store alarm embedding (RAG ingestion) ───────────────

    [Function(nameof(StoreAlarmEmbedding))]
    public async Task StoreAlarmEmbedding([ActivityTrigger] ActionInput input)
    {
        try
        {
            await embeddingService.EnsureSchemaAsync();
            var text = AlarmEmbeddingService.BuildAlarmText(input.Alarm, input.Analysis);
            var embedding = await embeddingService.GenerateEmbeddingAsync(text);
            await embeddingService.StoreAsync(input.Alarm, input.Analysis, embedding);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to store alarm embedding for {DeviceId} — non-critical", input.Alarm.DeviceId);
        }
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    private async Task EnsureAnalysisTableAsync()
    {
        const string sql = """
            CREATE TABLE IF NOT EXISTS alarm_analyses (
                id BIGSERIAL PRIMARY KEY,
                device_id TEXT NOT NULL,
                alarm_level TEXT NOT NULL,
                adjusted_severity TEXT NOT NULL,
                value DOUBLE PRECISION NOT NULL,
                root_cause TEXT NOT NULL,
                recommended_actions TEXT NOT NULL,
                summary TEXT NOT NULL,
                timestamp TIMESTAMPTZ NOT NULL,
                analyzed_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
            )
            """;

        await using var cmd = db.CreateCommand(sql);
        await cmd.ExecuteNonQueryAsync();
    }
}
