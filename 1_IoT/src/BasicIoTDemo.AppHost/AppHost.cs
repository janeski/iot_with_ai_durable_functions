var builder = DistributedApplication.CreateBuilder(args);

// Infrastructure
var postgresServer = builder.AddPostgres("postgres");
var telemetrydb = postgresServer.AddDatabase("telemetrydb");

var serviceBus = builder.AddAzureServiceBus("messaging")
    .RunAsEmulator();
serviceBus.AddServiceBusQueue("alarms");

var mqtt = builder.AddContainer("mqtt", "eclipse-mosquitto", "2")
    .WithEndpoint(targetPort: 1883, name: "mqtt")
    .WithArgs("/usr/sbin/mosquitto", "-c", "/mosquitto-no-auth.conf")
    .ExcludeFromManifest();

var mqttEndpoint = mqtt.GetEndpoint("mqtt");

// Grafana (local dev only)
var grafanaDataPath = Path.Combine(builder.AppHostDirectory, "grafana");
var pgEndpoint = postgresServer.GetEndpoint("tcp");

var grafana = builder.AddContainer("grafana", "grafana/grafana", "latest")
    .WithEndpoint(targetPort: 3000, name: "http", scheme: "http")
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
    .WaitFor(postgresServer)
    .ExcludeFromManifest();

// Services
builder.AddProject<Projects.BasicIoTDemo_TelemetryFunction>("telemetry-function")
    .WithReference(telemetrydb)
    .WithReference(serviceBus)
    .WithEnvironment("Mqtt__Host", mqttEndpoint.Property(EndpointProperty.Host))
    .WithEnvironment("Mqtt__Port", mqttEndpoint.Property(EndpointProperty.Port))
    .WaitFor(telemetrydb)
    .WaitFor(mqtt)
    .WaitFor(serviceBus);

builder.AddProject<Projects.BasicIoTDemo_AlarmFunction>("alarm-function")
    .WithReference(serviceBus)
    .WithReference(telemetrydb)
    .WaitFor(serviceBus)
    .WaitFor(telemetrydb);

builder.AddProject<Projects.BasicIoTDemo_DeviceSimulator>("device-simulator")
    .WithEnvironment("Mqtt__Host", mqttEndpoint.Property(EndpointProperty.Host))
    .WithEnvironment("Mqtt__Port", mqttEndpoint.Property(EndpointProperty.Port))
    .WaitFor(mqtt)
    .WaitFor(serviceBus);

builder.Build().Run();
