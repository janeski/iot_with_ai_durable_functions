using IoT_AI_Demo.Orchestrator;
using IoT_AI_Demo.Shared;

namespace IoT_AI_Demo.Tests;

public class AlarmEmbeddingServiceTests
{
    private static AlarmMessage MakeAlarm(
        AlarmLevel level = AlarmLevel.HH,
        string deviceId = "temp-01",
        double value = 33.0) => new(
            deviceId, "TemperatureSensor", DateTimeOffset.UtcNow, value, "°C", level,
            $"{level} alarm on {deviceId}");

    private static AiAnalysisResult MakeAnalysis() => new(
        RootCause: "Sensor drift detected",
        AdjustedSeverity: "CRITICAL",
        RecommendedActions: ["Recalibrate sensor"],
        Summary: "Temperature exceeded threshold due to drift");

    // ── BuildAlarmText ──────────────────────────────────────────────

    [Fact]
    public void BuildAlarmText_AlarmOnly_ContainsDeviceInfo()
    {
        var alarm = MakeAlarm();
        var text = AlarmEmbeddingService.BuildAlarmText(alarm);

        Assert.Contains("temp-01", text);
        Assert.Contains("TemperatureSensor", text);
        Assert.Contains("HH", text);
    }

    [Fact]
    public void BuildAlarmText_AlarmOnly_ContainsValue()
    {
        var alarm = MakeAlarm(value: 33.5);
        var text = AlarmEmbeddingService.BuildAlarmText(alarm);

        Assert.Contains("33.5", text);
        Assert.Contains("°C", text);
    }

    [Fact]
    public void BuildAlarmText_WithAnalysis_ContainsRootCause()
    {
        var alarm = MakeAlarm();
        var analysis = MakeAnalysis();
        var text = AlarmEmbeddingService.BuildAlarmText(alarm, analysis);

        Assert.Contains("Sensor drift detected", text);
        Assert.Contains("Temperature exceeded threshold", text);
    }

    [Fact]
    public void BuildAlarmText_WithNullAnalysis_DoesNotThrow()
    {
        var alarm = MakeAlarm();
        var text = AlarmEmbeddingService.BuildAlarmText(alarm, null);

        Assert.NotEmpty(text);
        Assert.DoesNotContain("Root cause:", text);
    }

    [Fact]
    public void BuildAlarmText_DifferentDevices_ProduceDifferentText()
    {
        var alarm1 = MakeAlarm(deviceId: "temp-01");
        var alarm2 = MakeAlarm(deviceId: "ph-01");

        var text1 = AlarmEmbeddingService.BuildAlarmText(alarm1);
        var text2 = AlarmEmbeddingService.BuildAlarmText(alarm2);

        Assert.NotEqual(text1, text2);
    }

    [Fact]
    public void BuildAlarmText_DifferentLevels_ProduceDifferentText()
    {
        var alarm1 = MakeAlarm(level: AlarmLevel.H);
        var alarm2 = MakeAlarm(level: AlarmLevel.HH);

        var text1 = AlarmEmbeddingService.BuildAlarmText(alarm1);
        var text2 = AlarmEmbeddingService.BuildAlarmText(alarm2);

        Assert.NotEqual(text1, text2);
    }

    [Theory]
    [InlineData(AlarmLevel.H)]
    [InlineData(AlarmLevel.HH)]
    [InlineData(AlarmLevel.L)]
    [InlineData(AlarmLevel.LL)]
    public void BuildAlarmText_AllLevels_ContainsLevelString(AlarmLevel level)
    {
        var alarm = MakeAlarm(level: level);
        var text = AlarmEmbeddingService.BuildAlarmText(alarm);

        Assert.Contains(level.ToString(), text);
    }
}
