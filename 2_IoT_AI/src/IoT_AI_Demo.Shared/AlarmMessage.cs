namespace IoT_AI_Demo.Shared;

public enum AlarmLevel { L, LL, H, HH }

public record AlarmMessage(
    string DeviceId,
    string DeviceType,
    DateTimeOffset Timestamp,
    double Value,
    string Unit,
    AlarmLevel AlarmLevel,
    string Description);
