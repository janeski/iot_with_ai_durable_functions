namespace BasicIoTDemo.Shared;

public record TelemetryMessage(
    string DeviceId,
    string DeviceType,
    DateTimeOffset Timestamp,
    double Value,
    string Unit);
