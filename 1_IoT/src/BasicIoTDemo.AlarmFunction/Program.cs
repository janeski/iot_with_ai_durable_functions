using BasicIoTDemo.AlarmFunction;

var builder = Host.CreateApplicationBuilder(args);
builder.AddServiceDefaults();
builder.AddNpgsqlDataSource("telemetrydb");
builder.AddAzureServiceBusClient("messaging");
builder.Services.AddHostedService<AlarmWorker>();

var host = builder.Build();
host.Run();
