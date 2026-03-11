using IoT_AI_Demo.Orchestrator;
using IoT_AI_Demo.Shared;
using Microsoft.Extensions.Logging.Abstractions;

namespace IoT_AI_Demo.Tests;

public class ActivitiesTests
{
    private static readonly Activities Sut = new(
        db: null!,               // not needed by the methods under test
        aiAnalyzer: null!,       // not needed by the methods under test
        embeddingService: null!, // not needed by the methods under test
        logger: NullLogger<Activities>.Instance);

    private static AlarmMessage MakeAlarm(
        AlarmLevel level = AlarmLevel.HH,
        string deviceId = "temp-01",
        double value = 33.0) => new(
            deviceId, "TemperatureSensor", DateTimeOffset.UtcNow, value, "°C", level,
            $"{level} alarm on {deviceId}");

    // ── FetchDeviceContext ───────────────────────────────────────────

    [Fact]
    public async Task FetchDeviceContext_KnownDevice_ReturnsSensorInfo()
    {
        var result = await Sut.FetchDeviceContext("temp-01");

        Assert.Equal("temp-01", result.DeviceId);
        Assert.Equal("TemperatureSensor", result.DeviceType);
        Assert.Equal("°C", result.Unit);
        Assert.Equal(24.0, result.NormalMin);
        Assert.Equal(28.0, result.NormalMax);
    }

    [Fact]
    public async Task FetchDeviceContext_UnknownDevice_ReturnsDefault()
    {
        var result = await Sut.FetchDeviceContext("nonexistent-99");

        Assert.Equal("nonexistent-99", result.DeviceId);
        Assert.Equal("Unknown", result.DeviceType);
        Assert.Equal(0, result.NormalMin);
        Assert.Equal(0, result.NormalMax);
    }

    [Theory]
    [InlineData("ph-01", "PhSensor")]
    [InlineData("oxygen-01", "OxygenSensor")]
    [InlineData("level-01", "WaterLevelSensor")]
    public async Task FetchDeviceContext_EachSensor_ReturnsCorrectType(string deviceId, string expectedType)
    {
        var result = await Sut.FetchDeviceContext(deviceId);
        Assert.Equal(expectedType, result.DeviceType);
    }

    // ── ValidateResult ──────────────────────────────────────────────

    [Fact]
    public async Task ValidateResult_ValidResult_PassesThrough()
    {
        var input = new AiAnalysisResult(
            RootCause: "Sensor drift detected",
            AdjustedSeverity: "CRITICAL",
            RecommendedActions: ["Recalibrate sensor"],
            Summary: "Temperature sensor exceeding threshold");

        var result = await Sut.ValidateResult(input);

        Assert.Equal(input, result);
    }

    [Theory]
    [InlineData("CRITICAL")]
    [InlineData("WARNING")]
    [InlineData("INFO")]
    public async Task ValidateResult_AllValidSeverities_PassThrough(string severity)
    {
        var input = new AiAnalysisResult("Root cause", severity, ["Action"], "Summary");

        var result = await Sut.ValidateResult(input);

        Assert.Equal(severity, result.AdjustedSeverity);
    }

    [Theory]
    [InlineData("HIGH")]
    [InlineData("LOW")]
    [InlineData("")]
    [InlineData("critical")]
    public async Task ValidateResult_InvalidSeverity_DefaultsToWarning(string badSeverity)
    {
        var input = new AiAnalysisResult("Root cause", badSeverity, ["Action"], "Summary");

        var result = await Sut.ValidateResult(input);

        Assert.Equal("WARNING", result.AdjustedSeverity);
    }

    [Fact]
    public async Task ValidateResult_EmptyRootCause_DefaultsToUndetermined()
    {
        var input = new AiAnalysisResult("", "WARNING", ["Action"], "Summary");

        var result = await Sut.ValidateResult(input);

        Assert.Equal("Undetermined", result.RootCause);
    }

    [Fact]
    public async Task ValidateResult_WhitespaceRootCause_DefaultsToUndetermined()
    {
        var input = new AiAnalysisResult("  ", "WARNING", ["Action"], "Summary");

        var result = await Sut.ValidateResult(input);

        Assert.Equal("Undetermined", result.RootCause);
    }

    [Fact]
    public async Task ValidateResult_EmptySummary_DefaultsToFallback()
    {
        var input = new AiAnalysisResult("Root cause", "CRITICAL", ["Action"], "");

        var result = await Sut.ValidateResult(input);

        Assert.Equal("Analysis completed with limited data", result.Summary);
    }

    [Fact]
    public async Task ValidateResult_MultipleInvalidFields_FixesAll()
    {
        var input = new AiAnalysisResult("", "INVALID", ["Action"], "  ");

        var result = await Sut.ValidateResult(input);

        Assert.Equal("Undetermined", result.RootCause);
        Assert.Equal("WARNING", result.AdjustedSeverity);
        Assert.Equal("Analysis completed with limited data", result.Summary);
        // RecommendedActions should be preserved
        Assert.Equal(["Action"], result.RecommendedActions);
    }

    // ── Stub activities ─────────────────────────────────────────────

    [Fact]
    public async Task NotifyViaSendGrid_CompletesWithoutError()
    {
        var input = new ActionInput(MakeAlarm(), MakeAnalysisResult());
        await Sut.NotifyViaSendGrid(input);
    }

    [Fact]
    public async Task CreateMaintenanceTicket_CompletesWithoutError()
    {
        var input = new ActionInput(MakeAlarm(), MakeAnalysisResult("CRITICAL"));
        await Sut.CreateMaintenanceTicket(input);
    }

    [Fact]
    public async Task CreateMaintenanceTicket_NonCritical_CompletesWithoutError()
    {
        var input = new ActionInput(MakeAlarm(AlarmLevel.H), MakeAnalysisResult("WARNING"));
        await Sut.CreateMaintenanceTicket(input);
    }

    [Fact]
    public async Task UpdateDashboard_CompletesWithoutError()
    {
        var input = new ActionInput(MakeAlarm(), MakeAnalysisResult());
        await Sut.UpdateDashboard(input);
    }

    private static AiAnalysisResult MakeAnalysisResult(string severity = "CRITICAL") => new(
        RootCause: "Test root cause",
        AdjustedSeverity: severity,
        RecommendedActions: ["Action 1"],
        Summary: "Test summary");
}
