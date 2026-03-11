namespace IoT_AI_Demo.Shared;

public record TelemetryMessage(
    string DeviceId,
    string DeviceType,
    DateTimeOffset Timestamp,
    double Value,
    string Unit);
