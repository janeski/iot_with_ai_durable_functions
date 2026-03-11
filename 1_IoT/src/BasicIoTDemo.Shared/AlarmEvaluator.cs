namespace BasicIoTDemo.Shared;

public static class AlarmEvaluator
{
    /// <summary>
    /// Evaluates a telemetry reading against the sensor's alarm thresholds.
    /// Returns the alarm message if a threshold is breached, or null if the value is normal.
    /// Checks HH/LL first (most severe), then H/L.
    /// </summary>
    public static AlarmMessage? Evaluate(TelemetryMessage telemetry, SensorConfig sensor)
    {
        // HH — High-High (critical)
        if (sensor.HH.HasValue && telemetry.Value > sensor.HH.Value)
            return MakeAlarm(telemetry, AlarmLevel.HH, $"High-High {sensor.DeviceType} alarm");

        // LL — Low-Low (critical)
        if (sensor.LL.HasValue && telemetry.Value < sensor.LL.Value)
            return MakeAlarm(telemetry, AlarmLevel.LL, $"Low-Low {sensor.DeviceType} alarm");

        // H — High (warning)
        if (sensor.H.HasValue && telemetry.Value > sensor.H.Value)
            return MakeAlarm(telemetry, AlarmLevel.H, $"High {sensor.DeviceType} alarm");

        // L — Low (warning)
        if (sensor.L.HasValue && telemetry.Value < sensor.L.Value)
            return MakeAlarm(telemetry, AlarmLevel.L, $"Low {sensor.DeviceType} alarm");

        return null;
    }

    public static SensorConfig? FindSensor(string deviceId)
        => DeviceDefinitions.Sensors.FirstOrDefault(s => s.DeviceId == deviceId);

    private static AlarmMessage MakeAlarm(TelemetryMessage t, AlarmLevel level, string description)
        => new(t.DeviceId, t.DeviceType, t.Timestamp, t.Value, t.Unit, level, description);
}
