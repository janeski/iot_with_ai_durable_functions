using Azure.AI.OpenAI;
using Azure.Identity;
using IoT_AI_Demo.Orchestrator;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = FunctionsApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddNpgsqlDataSource("telemetrydb");

// Register OpenAI embedding generator (text-embedding-3-small)
var openAiEndpoint = builder.Configuration["AzureOpenAI:Endpoint"];
var embeddingDeployment = builder.Configuration["AzureOpenAI:EmbeddingDeployment"] ?? "text-embedding-3-small";

if (!string.IsNullOrEmpty(openAiEndpoint))
{
    builder.Services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(
        new AzureOpenAIClient(new Uri(openAiEndpoint), new DefaultAzureCredential())
            .GetEmbeddingClient(embeddingDeployment)
            .AsIEmbeddingGenerator());
}
else
{
    builder.Services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(new MockEmbeddingGenerator());
}

builder.Services.AddSingleton<AiAnalyzer>();
builder.Services.AddSingleton<AlarmEmbeddingService>();

builder.Build().Run();
