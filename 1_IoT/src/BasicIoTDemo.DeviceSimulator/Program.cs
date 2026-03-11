using BasicIoTDemo.DeviceSimulator;

var builder = Host.CreateApplicationBuilder(args);
builder.AddServiceDefaults();
builder.Services.AddHostedService<SimulatorWorker>();

var host = builder.Build();
host.Run();
