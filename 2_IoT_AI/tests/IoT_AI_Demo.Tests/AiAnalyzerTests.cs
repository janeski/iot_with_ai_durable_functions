using IoT_AI_Demo.Orchestrator;
using IoT_AI_Demo.Shared;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace IoT_AI_Demo.Tests;

public class AiAnalyzerTests
{
    private static AiAnalyzer CreateAnalyzer(Dictionary<string, string?>? settings = null)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(settings ?? [])
            .Build();

        return new AiAnalyzer(config, NullLogger<AiAnalyzer>.Instance);
    }

    private static AlarmMessage MakeAlarm(
        AlarmLevel level = AlarmLevel.HH,
        string deviceId = "temp-01",
        double value = 33.0) => new(
            deviceId, "TemperatureSensor", DateTimeOffset.UtcNow, value, "°C", level,
            $"{level} alarm on {deviceId}");

    private static DeviceContext MakeContext(string deviceId = "temp-01") => new(
        deviceId, "TemperatureSensor", "°C", 24.0, 28.0, "Fish Tank A", "Aquaculture");

    // ── Mock analysis path (no OpenAI endpoint) ─────────────────────

    [Fact]
    public async Task AnalyzeAsync_NoEndpoint_ReturnsMockAnalysis()
    {
        var analyzer = CreateAnalyzer();
        var alarm = MakeAlarm();

        var result = await analyzer.AnalyzeAsync(alarm, [], MakeContext());

        Assert.NotNull(result);
        Assert.NotEmpty(result.RootCause);
        Assert.NotEmpty(result.Summary);
        Assert.NotEmpty(result.RecommendedActions);
    }

    [Theory]
    [InlineData(AlarmLevel.HH, "CRITICAL")]
    [InlineData(AlarmLevel.LL, "CRITICAL")]
    [InlineData(AlarmLevel.H, "WARNING")]
    [InlineData(AlarmLevel.L, "WARNING")]
    public async Task AnalyzeAsync_MockPath_SeverityMatchesAlarmLevel(AlarmLevel level, string expectedSeverity)
    {
        var analyzer = CreateAnalyzer();
        var alarm = MakeAlarm(level: level);

        var result = await analyzer.AnalyzeAsync(alarm, [], MakeContext());

        Assert.Equal(expectedSeverity, result.AdjustedSeverity);
    }

    [Fact]
    public async Task AnalyzeAsync_MockPath_IncludesDeviceIdInRootCause()
    {
        var analyzer = CreateAnalyzer();
        var alarm = MakeAlarm(deviceId: "ph-01");
        var ctx = MakeContext("ph-01") with { DeviceType = "PhSensor" };

        var result = await analyzer.AnalyzeAsync(alarm, [], ctx);

        Assert.Contains("ph-01", result.RootCause);
    }

    [Fact]
    public async Task AnalyzeAsync_MockPath_RecommendedActionsIncludeDeviceType()
    {
        var analyzer = CreateAnalyzer();
        var alarm = MakeAlarm();

        var result = await analyzer.AnalyzeAsync(alarm, [], MakeContext());

        Assert.Contains(result.RecommendedActions, a => a.Contains("TemperatureSensor"));
    }

    [Fact]
    public async Task AnalyzeAsync_MockPath_CriticalAlarm_RecommendsMaintenance()
    {
        var analyzer = CreateAnalyzer();
        var alarm = MakeAlarm(level: AlarmLevel.HH);

        var result = await analyzer.AnalyzeAsync(alarm, [], MakeContext());

        Assert.Contains(result.RecommendedActions, a => a.Contains("maintenance", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task AnalyzeAsync_MockPath_WarningAlarm_RecommendsRoutineCheck()
    {
        var analyzer = CreateAnalyzer();
        var alarm = MakeAlarm(level: AlarmLevel.H);

        var result = await analyzer.AnalyzeAsync(alarm, [], MakeContext());

        Assert.Contains(result.RecommendedActions, a => a.Contains("routine", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task AnalyzeAsync_EmptyEndpoint_FallsBackToMock()
    {
        var analyzer = CreateAnalyzer(new() { ["AzureOpenAI:Endpoint"] = "" });
        var alarm = MakeAlarm();

        var result = await analyzer.AnalyzeAsync(alarm, [], MakeContext());

        Assert.NotNull(result);
        Assert.Contains("[Mock]", result.Summary);
    }
}
