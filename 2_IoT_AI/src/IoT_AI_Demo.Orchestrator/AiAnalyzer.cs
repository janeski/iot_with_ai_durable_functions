using System.Text.Json;
using Azure.AI.OpenAI;
using Azure.Identity;
using IoT_AI_Demo.Shared;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace IoT_AI_Demo.Orchestrator;

public sealed class AiAnalyzer(IConfiguration config, ILogger<AiAnalyzer> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public async Task<AiAnalysisResult> AnalyzeAsync(
        AlarmMessage alarm,
        List<TelemetryMessage> recentTelemetry,
        DeviceContext deviceContext,
        List<SimilarAlarmResult>? similarAlarms = null,
        CancellationToken ct = default)
    {
        var endpoint = config["AzureOpenAI:Endpoint"];
        if (string.IsNullOrEmpty(endpoint))
        {
            logger.LogInformation("No Azure OpenAI endpoint configured — returning mock analysis");
            await Task.Delay(500, ct); // simulate latency for demo
            return MockAnalysis(alarm, deviceContext);
        }

        return await CallAzureOpenAiAsync(endpoint, alarm, recentTelemetry, deviceContext, similarAlarms, ct);
    }

    private async Task<AiAnalysisResult> CallAzureOpenAiAsync(
        string endpoint,
        AlarmMessage alarm,
        List<TelemetryMessage> recentTelemetry,
        DeviceContext deviceContext,
        List<SimilarAlarmResult>? similarAlarms,
        CancellationToken ct)
    {
        var deployment = config["AzureOpenAI:Deployment"] ?? "gpt-4.1";
        var client = new AzureOpenAIClient(new Uri(endpoint), new DefaultAzureCredential());
        IChatClient chatClient = client.GetChatClient(deployment).AsIChatClient();

        var prompt = BuildPrompt(alarm, recentTelemetry, deviceContext, similarAlarms);

        const string systemMessage = """
            You are a control system operator assistant for an industrial IoT platform.
            Your role is to help operators make sense of alarm data objectively and efficiently.

            Guidelines:
            - Analyze telemetry trends and alarm patterns to identify root causes, not just symptoms.
            - When multiple alarms fire in a short window (alarm flooding), identify whether they share a common cause and highlight the primary alarm vs. consequential alarms.
            - Assess severity based on actual process risk — downgrade nuisance alarms, escalate genuinely dangerous conditions.
            - Recommend concrete operator actions prioritized by urgency.
            - Be concise and factual. Operators need clarity under pressure, not lengthy explanations.
            - If similar past alarms are provided, use them to spot recurring patterns or confirm known failure modes.

            Always respond with a single JSON object only — no markdown, no extra text.
            """;

        var response = await chatClient.GetResponseAsync(
            [new ChatMessage(ChatRole.System, systemMessage),
             new ChatMessage(ChatRole.User, prompt)],
            cancellationToken: ct);

        var text = response.Text ?? "";

        try
        {
            return JsonSerializer.Deserialize<AiAnalysisResult>(text, JsonOptions)
                   ?? MockAnalysis(alarm, deviceContext);
        }
        catch (JsonException)
        {
            logger.LogWarning("Failed to parse AI response as JSON, using mock analysis");
            return MockAnalysis(alarm, deviceContext);
        }
    }

    private static string BuildPrompt(
        AlarmMessage alarm,
        List<TelemetryMessage> telemetry,
        DeviceContext ctx,
        List<SimilarAlarmResult>? similarAlarms)
    {
        var recentJson = JsonSerializer.Serialize(
            telemetry.Take(10),
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        var similarSection = "";
        if (similarAlarms is { Count: > 0 })
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("\nSimilar past alarms (for context):");
            foreach (var (sa, i) in similarAlarms.Select((a, i) => (a, i + 1)))
            {
                sb.AppendLine($"{i}. [{sa.DeviceId}, {sa.AlarmLevel}, {sa.Timestamp:g}, similarity: {sa.Similarity:P0}]");
                sb.AppendLine($"   Root cause: {sa.RootCause}");
                sb.AppendLine($"   Summary: {sa.Summary}");
            }
            similarSection = sb.ToString();
        }

        return $"""
            Analyze this IoT alarm and respond with a JSON object containing:
            - rootCause: string (hypothesis for the alarm)
            - adjustedSeverity: "CRITICAL" | "WARNING" | "INFO"
            - recommendedActions: string[] (list of recommended actions)
            - summary: string (brief summary of the analysis)

            Alarm: {alarm.AlarmLevel} on device {alarm.DeviceId} ({ctx.DeviceType})
            Value: {alarm.Value} {alarm.Unit} at {alarm.Timestamp}
            Normal range: {ctx.NormalMin}–{ctx.NormalMax} {ctx.Unit}
            Location: {ctx.Location ?? "Unknown"}
            Asset group: {ctx.AssetGroup ?? "Unknown"}

            Recent telemetry (last 10 readings):
            {recentJson}
            {similarSection}
            """;
    }

    private static AiAnalysisResult MockAnalysis(AlarmMessage alarm, DeviceContext ctx) => new(
        RootCause: $"Sensor {alarm.DeviceId} ({ctx.DeviceType}) reading {alarm.Value} {alarm.Unit} exceeded threshold. " +
                   $"Possible causes: sensor drift, environmental change, or equipment malfunction.",
        AdjustedSeverity: alarm.AlarmLevel is AlarmLevel.HH or AlarmLevel.LL ? "CRITICAL" : "WARNING",
        RecommendedActions:
        [
            $"Inspect {ctx.DeviceType} sensor {alarm.DeviceId}",
            "Check sensor calibration",
            "Review environmental conditions",
            alarm.AlarmLevel is AlarmLevel.HH or AlarmLevel.LL
                ? "Escalate to maintenance team immediately"
                : "Schedule routine maintenance check"
        ],
        Summary: $"[Mock] {alarm.AlarmLevel} alarm on {alarm.DeviceId}: value {alarm.Value} {alarm.Unit} " +
                 $"is outside normal range ({ctx.NormalMin}–{ctx.NormalMax}). " +
                 $"AI analysis suggests sensor inspection and calibration check.");
}
