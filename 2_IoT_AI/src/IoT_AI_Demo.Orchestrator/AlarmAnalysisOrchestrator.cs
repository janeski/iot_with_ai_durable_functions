using IoT_AI_Demo.Shared;
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Microsoft.Extensions.Logging;

namespace IoT_AI_Demo.Orchestrator;

public static class AlarmAnalysisOrchestrator
{
    [Function(nameof(RunAlarmAnalysis))]
    public static async Task<AlarmAnalysisOutput> RunAlarmAnalysis(
        [OrchestrationTrigger] TaskOrchestrationContext context)
    {
        var logger = context.CreateReplaySafeLogger(nameof(AlarmAnalysisOrchestrator));
        var input = context.GetInput<AlarmAnalysisInput>()!;

        logger.LogInformation("Starting alarm analysis for {DeviceId} — {AlarmLevel}",
            input.Alarm.DeviceId, input.Alarm.AlarmLevel);

        var orchId = context.InstanceId;

        // Step 1: Fetch recent telemetry for trend context
        var recentTelemetry = await context.CallActivityAsync<List<TelemetryMessage>>(
            nameof(Activities.FetchRecentTelemetry), new DeviceActivityInput(orchId, input.Alarm.DeviceId));

        // Step 2: Fetch device / asset context
        var deviceContext = await context.CallActivityAsync<DeviceContext>(
            nameof(Activities.FetchDeviceContext), new DeviceActivityInput(orchId, input.Alarm.DeviceId));

        // Step 3: RAG — search for similar past alarms
        var similarAlarms = await context.CallActivityAsync<List<SimilarAlarmResult>>(
            nameof(Activities.SearchSimilarAlarms), new AlarmActivityInput(orchId, input.Alarm));

        logger.LogInformation("Found {Count} similar past alarms for {DeviceId}",
            similarAlarms.Count, input.Alarm.DeviceId);

        // Step 4: Call AI analysis (enriched with similar alarm context)
        var aiInput = new AiAnalysisInput(orchId, input.Alarm, recentTelemetry, deviceContext, similarAlarms);
        var aiResult = await context.CallActivityAsync<AiAnalysisResult?>(
            nameof(Activities.CallAiAnalysis), aiInput);

        // Step 5: Validate result — fall back if AI failed or returned invalid data
        var analysis = aiResult is not null
            ? await context.CallActivityAsync<AiAnalysisResult>(
                nameof(Activities.ValidateResult), aiResult)
            : FallbackAnalysis(input.Alarm);

        // Step 6: Fan-out — trigger downstream actions in parallel (includes embedding storage)
        var actionInput = new ActionInput(orchId, input.Alarm, analysis);
        await Task.WhenAll(
            context.CallActivityAsync(nameof(Activities.UpdatePostgreSql), actionInput),
            context.CallActivityAsync(nameof(Activities.StoreAlarmEmbedding), actionInput),
            context.CallActivityAsync(nameof(Activities.NotifyViaSendGrid), actionInput),
            context.CallActivityAsync(nameof(Activities.CreateMaintenanceTicket), actionInput),
            context.CallActivityAsync(nameof(Activities.UpdateDashboard), actionInput));

        logger.LogInformation("Alarm analysis completed for {DeviceId}", input.Alarm.DeviceId);
        return new AlarmAnalysisOutput(input.Alarm, analysis, context.CurrentUtcDateTime);
    }

    private static AiAnalysisResult FallbackAnalysis(AlarmMessage alarm) => new(
        RootCause: "AI analysis unavailable — using rule-based assessment",
        AdjustedSeverity: alarm.AlarmLevel is AlarmLevel.HH or AlarmLevel.LL ? "CRITICAL" : "WARNING",
        RecommendedActions: ["Review sensor readings", "Check device health"],
        Summary: $"Fallback: {alarm.Description}");
}
