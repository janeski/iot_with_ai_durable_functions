using System.Text.Json;
using IoT_AI_Demo.Shared;
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.Logging;

namespace IoT_AI_Demo.Orchestrator;

public sealed class AlarmAnalysisTrigger(ILogger<AlarmAnalysisTrigger> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    [Function(nameof(StartAlarmAnalysis))]
    public async Task StartAlarmAnalysis(
        [ServiceBusTrigger("alarm-analysis", Connection = "messaging")] string message,
        [DurableClient] DurableTaskClient durableClient)
    {
        var input = JsonSerializer.Deserialize<AlarmAnalysisInput>(message, JsonOptions);
        if (input is null)
        {
            logger.LogWarning("Failed to deserialize alarm analysis input");
            return;
        }

        var instanceId = await durableClient.ScheduleNewOrchestrationInstanceAsync(
            nameof(AlarmAnalysisOrchestrator.RunAlarmAnalysis), input);

        logger.LogInformation("Started alarm analysis orchestration {InstanceId} for {DeviceId}",
            instanceId, input.Alarm.DeviceId);
    }
}
