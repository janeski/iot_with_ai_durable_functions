namespace IoT_AI_Demo.Shared;

/// <summary>Input to the alarm analysis orchestrator.</summary>
public record AlarmAnalysisInput(AlarmMessage Alarm, TelemetryMessage Telemetry);

/// <summary>Device metadata and asset context.</summary>
public record DeviceContext(
    string DeviceId,
    string DeviceType,
    string Unit,
    double NormalMin,
    double NormalMax,
    string? Location,
    string? AssetGroup);

/// <summary>Wraps a device ID with the orchestration instance ID for process event tracing.</summary>
public record DeviceActivityInput(string OrchestrationId, string DeviceId);

/// <summary>Combined input for the AI analysis activity.</summary>
public record AiAnalysisInput(
    string OrchestrationId,
    AlarmMessage Alarm,
    List<TelemetryMessage> RecentTelemetry,
    DeviceContext DeviceContext,
    List<SimilarAlarmResult>? SimilarAlarms = null);

/// <summary>A past alarm retrieved by similarity search.</summary>
public record SimilarAlarmResult(
    string DeviceId,
    string AlarmLevel,
    string RootCause,
    string Summary,
    double Similarity,
    DateTimeOffset Timestamp);

/// <summary>Result returned by the AI analysis.</summary>
public record AiAnalysisResult(
    string RootCause,
    string AdjustedSeverity,
    string[] RecommendedActions,
    string Summary);

/// <summary>Wraps an alarm with the orchestration instance ID for similarity search tracing.</summary>
public record AlarmActivityInput(string OrchestrationId, AlarmMessage Alarm);

/// <summary>Input to downstream action activities.</summary>
public record ActionInput(string OrchestrationId, AlarmMessage Alarm, AiAnalysisResult Analysis);

/// <summary>Final output of the alarm analysis orchestration.</summary>
public record AlarmAnalysisOutput(
    AlarmMessage Alarm,
    AiAnalysisResult Analysis,
    DateTimeOffset CompletedAt);
