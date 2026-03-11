using BasicIoTDemo.Shared;

namespace BasicIoTDemo.Tests;

public class AlarmEvaluatorTests
{
    private static readonly SensorConfig TempSensor = new(
        "temp-01", "TemperatureSensor", "°C",
        NormalMin: 24.0, NormalMax: 28.0,
        LL: 20.0, L: 22.0, H: 30.0, HH: 32.0,
        PublishInterval: TimeSpan.FromSeconds(5));

    private static readonly SensorConfig LevelSensor = new(
        "level-01", "WaterLevelSensor", "%",
        NormalMin: 80, NormalMax: 100,
        LL: 50, L: 70, H: null, HH: null,
        PublishInterval: TimeSpan.FromSeconds(15));

    private static TelemetryMessage MakeTelemetry(SensorConfig sensor, double value)
        => new(sensor.DeviceId, sensor.DeviceType, DateTimeOffset.UtcNow, value, sensor.Unit);

    // --- Evaluate: normal values ---

    [Fact]
    public void Evaluate_NormalValue_ReturnsNull()
    {
        var result = AlarmEvaluator.Evaluate(MakeTelemetry(TempSensor, 26.0), TempSensor);
        Assert.Null(result);
    }

    [Fact]
    public void Evaluate_ValueAtHThreshold_ReturnsNull()
    {
        // At the threshold (not above), no alarm
        var result = AlarmEvaluator.Evaluate(MakeTelemetry(TempSensor, 30.0), TempSensor);
        Assert.Null(result);
    }

    [Fact]
    public void Evaluate_ValueAtLThreshold_ReturnsNull()
    {
        var result = AlarmEvaluator.Evaluate(MakeTelemetry(TempSensor, 22.0), TempSensor);
        Assert.Null(result);
    }

    // --- Evaluate: High-High ---

    [Fact]
    public void Evaluate_AboveHH_ReturnsHHAlarm()
    {
        var result = AlarmEvaluator.Evaluate(MakeTelemetry(TempSensor, 33.0), TempSensor);

        Assert.NotNull(result);
        Assert.Equal(AlarmLevel.HH, result.AlarmLevel);
        Assert.Contains("High-High", result.Description);
        Assert.Equal(TempSensor.DeviceId, result.DeviceId);
        Assert.Equal(33.0, result.Value);
    }

    [Fact]
    public void Evaluate_AtHHThreshold_ReturnsHAlarm()
    {
        // 32.0 is not > 32.0 (HH), but 32.0 > 30.0 (H) — so H alarm fires
        var result = AlarmEvaluator.Evaluate(MakeTelemetry(TempSensor, 32.0), TempSensor);

        Assert.NotNull(result);
        Assert.Equal(AlarmLevel.H, result.AlarmLevel);
    }

    // --- Evaluate: Low-Low ---

    [Fact]
    public void Evaluate_BelowLL_ReturnsLLAlarm()
    {
        var result = AlarmEvaluator.Evaluate(MakeTelemetry(TempSensor, 19.0), TempSensor);

        Assert.NotNull(result);
        Assert.Equal(AlarmLevel.LL, result.AlarmLevel);
        Assert.Contains("Low-Low", result.Description);
    }

    [Fact]
    public void Evaluate_AtLLThreshold_ReturnsLAlarm()
    {
        // 20.0 is not < 20.0 (LL), but 20.0 < 22.0 (L) — so L alarm fires
        var result = AlarmEvaluator.Evaluate(MakeTelemetry(TempSensor, 20.0), TempSensor);

        Assert.NotNull(result);
        Assert.Equal(AlarmLevel.L, result.AlarmLevel);
    }

    // --- Evaluate: High ---

    [Fact]
    public void Evaluate_AboveH_BelowHH_ReturnsHAlarm()
    {
        var result = AlarmEvaluator.Evaluate(MakeTelemetry(TempSensor, 31.0), TempSensor);

        Assert.NotNull(result);
        Assert.Equal(AlarmLevel.H, result.AlarmLevel);
        Assert.Contains("High", result.Description);
    }

    // --- Evaluate: Low ---

    [Fact]
    public void Evaluate_BelowL_AboveLL_ReturnsLAlarm()
    {
        var result = AlarmEvaluator.Evaluate(MakeTelemetry(TempSensor, 21.0), TempSensor);

        Assert.NotNull(result);
        Assert.Equal(AlarmLevel.L, result.AlarmLevel);
        Assert.Contains("Low", result.Description);
    }

    // --- Evaluate: HH takes priority over H ---

    [Fact]
    public void Evaluate_WayAboveHH_ReturnsHHNotH()
    {
        var result = AlarmEvaluator.Evaluate(MakeTelemetry(TempSensor, 50.0), TempSensor);

        Assert.NotNull(result);
        Assert.Equal(AlarmLevel.HH, result.AlarmLevel);
    }

    // --- Evaluate: LL takes priority over L ---

    [Fact]
    public void Evaluate_WayBelowLL_ReturnsLLNotL()
    {
        var result = AlarmEvaluator.Evaluate(MakeTelemetry(TempSensor, 5.0), TempSensor);

        Assert.NotNull(result);
        Assert.Equal(AlarmLevel.LL, result.AlarmLevel);
    }

    // --- Evaluate: null thresholds (no H/HH on level sensor) ---

    [Fact]
    public void Evaluate_NoHighThresholds_HighValueReturnsNull()
    {
        var result = AlarmEvaluator.Evaluate(MakeTelemetry(LevelSensor, 999.0), LevelSensor);
        Assert.Null(result);
    }

    [Fact]
    public void Evaluate_LowThresholdsStillWork_WhenHighIsNull()
    {
        var result = AlarmEvaluator.Evaluate(MakeTelemetry(LevelSensor, 60.0), LevelSensor);

        Assert.NotNull(result);
        Assert.Equal(AlarmLevel.L, result.AlarmLevel);
    }

    // --- Evaluate: alarm message fields ---

    [Fact]
    public void Evaluate_AlarmMessage_HasCorrectFields()
    {
        var now = DateTimeOffset.UtcNow;
        var telemetry = new TelemetryMessage("temp-01", "TemperatureSensor", now, 33.0, "°C");
        var result = AlarmEvaluator.Evaluate(telemetry, TempSensor);

        Assert.NotNull(result);
        Assert.Equal("temp-01", result.DeviceId);
        Assert.Equal("TemperatureSensor", result.DeviceType);
        Assert.Equal(now, result.Timestamp);
        Assert.Equal(33.0, result.Value);
        Assert.Equal("°C", result.Unit);
    }

    // --- FindSensor ---

    [Theory]
    [InlineData("temp-01", "TemperatureSensor")]
    [InlineData("ph-01", "PhSensor")]
    [InlineData("level-01", "WaterLevelSensor")]
    [InlineData("oxygen-01", "OxygenSensor")]
    [InlineData("turbidity-01", "TurbiditySensor")]
    public void FindSensor_KnownDeviceId_ReturnsSensor(string deviceId, string expectedType)
    {
        var sensor = AlarmEvaluator.FindSensor(deviceId);

        Assert.NotNull(sensor);
        Assert.Equal(deviceId, sensor.DeviceId);
        Assert.Equal(expectedType, sensor.DeviceType);
    }

    [Fact]
    public void FindSensor_UnknownDeviceId_ReturnsNull()
    {
        Assert.Null(AlarmEvaluator.FindSensor("nonexistent-99"));
    }

    [Fact]
    public void FindSensor_ActuatorDeviceId_ReturnsNull()
    {
        // Actuators are not in the Sensors list
        Assert.Null(AlarmEvaluator.FindSensor("feeder-01"));
    }
}

public class DeviceDefinitionsTests
{
    [Fact]
    public void Sensors_HasExpectedCount()
    {
        Assert.Equal(5, DeviceDefinitions.Sensors.Length);
    }

    [Fact]
    public void Actuators_HasExpectedCount()
    {
        Assert.Equal(2, DeviceDefinitions.Actuators.Length);
    }

    [Fact]
    public void Sensors_HaveUniqueDeviceIds()
    {
        var ids = DeviceDefinitions.Sensors.Select(s => s.DeviceId).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public void Actuators_HaveUniqueDeviceIds()
    {
        var ids = DeviceDefinitions.Actuators.Select(a => a.DeviceId).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public void Sensors_NormalRangeIsValid()
    {
        foreach (var sensor in DeviceDefinitions.Sensors)
            Assert.True(sensor.NormalMin < sensor.NormalMax,
                $"{sensor.DeviceId}: NormalMin ({sensor.NormalMin}) should be less than NormalMax ({sensor.NormalMax})");
    }

    [Fact]
    public void Sensors_ThresholdsAreOrdered()
    {
        // Expected order: LL < L < H < HH (when defined)
        foreach (var s in DeviceDefinitions.Sensors)
        {
            if (s.LL.HasValue && s.L.HasValue)
                Assert.True(s.LL.Value < s.L.Value, $"{s.DeviceId}: LL should be < L");
            if (s.L.HasValue && s.H.HasValue)
                Assert.True(s.L.Value < s.H.Value, $"{s.DeviceId}: L should be < H");
            if (s.H.HasValue && s.HH.HasValue)
                Assert.True(s.H.Value < s.HH.Value, $"{s.DeviceId}: H should be < HH");
        }
    }
}
