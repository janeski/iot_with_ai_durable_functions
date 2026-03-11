using BasicIoTDemo.TelemetryFunction;

var builder = Host.CreateApplicationBuilder(args);
builder.AddServiceDefaults();
builder.AddNpgsqlDataSource("telemetrydb");
builder.AddAzureServiceBusClient("messaging");
builder.Services.AddHostedService<TelemetryWorker>();

var host = builder.Build();
host.Run();
