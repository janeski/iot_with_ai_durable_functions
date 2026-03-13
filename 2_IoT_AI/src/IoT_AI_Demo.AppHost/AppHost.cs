var builder = DistributedApplication.CreateBuilder(args);

// Infrastructure
var postgresServer = builder.AddPostgres("postgres")
    .WithImage("pgvector/pgvector", "pg17");
var telemetrydb = postgresServer.AddDatabase("telemetrydb");

var serviceBus = builder.AddAzureServiceBus("messaging")
    .RunAsEmulator();
serviceBus.AddServiceBusQueue("alarms");
serviceBus.AddServiceBusQueue("alarm-analysis");

var storage = builder.AddAzureStorage("storage")
    .RunAsEmulator();
var blobs = storage.AddBlobs("funcstorage");

var dtsEmulator = builder.AddContainer("dts-emulator", "mcr.microsoft.com/dts/dts-emulator", "latest")
    .WithEndpoint(targetPort: 8080, name: "grpc")
    .WithEndpoint(targetPort: 8082, name: "dashboard", scheme: "http")
    .ExcludeFromManifest();

var dtsGrpcEndpoint = dtsEmulator.GetEndpoint("grpc");

var mqtt = builder.AddContainer("mqtt", "eclipse-mosquitto", "2")
    .WithEndpoint(targetPort: 1883, name: "mqtt")
    .WithArgs("/usr/sbin/mosquitto", "-c", "/mosquitto-no-auth.conf")
    .ExcludeFromManifest();

var mqttEndpoint = mqtt.GetEndpoint("mqtt");

// Grafana (local dev only)
var grafanaDataPath = Path.Combine(builder.AppHostDirectory, "grafana");
var pgEndpoint = postgresServer.GetEndpoint("tcp");

var grafana = builder.AddContainer("grafana", "grafana/grafana", "latest")
    .WithEndpoint(targetPort: 3000, port: 61364, name: "http", scheme: "http")
    .WithEnvironment("GF_SERVER_ROOT_URL", "http://localhost:61364/")
    .WithEnvironment("GF_AUTH_ANONYMOUS_ENABLED", "true")
    .WithEnvironment("GF_AUTH_ANONYMOUS_ORG_ROLE", "Admin")
    .WithEnvironment("GF_AUTH_DISABLE_LOGIN_FORM", "true")
    .WithEnvironment("POSTGRES_HOST", pgEndpoint.Property(EndpointProperty.Host))
    .WithEnvironment("POSTGRES_PORT", pgEndpoint.Property(EndpointProperty.Port))
    .WithEnvironment(ctx =>
    {
        if (postgresServer.Resource.PasswordParameter is { } pwd)
            ctx.EnvironmentVariables["POSTGRES_PASSWORD"] = pwd;
    })
    .WithBindMount(Path.Combine(grafanaDataPath, "provisioning"), "/etc/grafana/provisioning", isReadOnly: true)
    .WithBindMount(Path.Combine(grafanaDataPath, "dashboards"), "/var/lib/grafana/dashboards", isReadOnly: true)
    .WithUrl("http://localhost:61364/d/fishtank-iot/fish-tank-iot-dashboard", "Fish Tank Dashboard")
    .WaitFor(postgresServer)
    .ExcludeFromManifest();

var enableGrafanaTunnel = string.Equals(
    Environment.GetEnvironmentVariable("ENABLE_GRAFANA_TUNNEL"),
    "true",
    StringComparison.OrdinalIgnoreCase);

if (enableGrafanaTunnel)
{
    builder.AddDevTunnel("grafana-tunnel")
        .WithReference(grafana)
        .ExcludeFromManifest();
}

// Services
builder.AddProject<Projects.IoT_AI_Demo_TelemetryFunction>("telemetry-function")
    .WithReference(telemetrydb)
    .WithReference(serviceBus)
    .WithEnvironment("Mqtt__Host", mqttEndpoint.Property(EndpointProperty.Host))
    .WithEnvironment("Mqtt__Port", mqttEndpoint.Property(EndpointProperty.Port))
    .WaitFor(telemetrydb)
    .WaitFor(mqtt)
    .WaitFor(serviceBus);

builder.AddProject<Projects.IoT_AI_Demo_AlarmFunction>("alarm-function")
    .WithReference(serviceBus)
    .WithReference(telemetrydb)
    .WaitFor(serviceBus)
    .WaitFor(telemetrydb);

builder.AddProject<Projects.IoT_AI_Demo_DeviceSimulator>("device-simulator")
    .WithEnvironment("Mqtt__Host", mqttEndpoint.Property(EndpointProperty.Host))
    .WithEnvironment("Mqtt__Port", mqttEndpoint.Property(EndpointProperty.Port))
    .WaitFor(mqtt)
    .WaitFor(serviceBus);

builder.AddAzureFunctionsProject<Projects.IoT_AI_Demo_Orchestrator>("orchestrator")
    .WithHostStorage(storage)
    .WithReference(serviceBus)
    .WithReference(telemetrydb)
    .WithEnvironment(ctx =>
    {
        ctx.EnvironmentVariables["DURABLE_TASK_SCHEDULER_CONNECTION_STRING"] =
            ReferenceExpression.Create(
                $"Endpoint=http://{dtsGrpcEndpoint.Property(EndpointProperty.Host)}:{dtsGrpcEndpoint.Property(EndpointProperty.Port)};Authentication=None");
    })
    .WithEnvironment("TASKHUB_NAME", "default")
    .WithEnvironment("AzureOpenAI__Endpoint", "https://dotnet-mvp-meetup.openai.azure.com/")
    .WithEnvironment("AzureOpenAI__Deployment", "gpt-4.1")
    .WithEnvironment("AzureOpenAI__EmbeddingDeployment", "text-embedding-3-small")
    .WaitFor(serviceBus)
    .WaitFor(telemetrydb)
    .WaitFor(storage)
    .WaitFor(dtsEmulator);

builder.Build().Run();
