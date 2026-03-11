namespace IoT_AI_Demo.Shared;

public record SensorConfig(
    string DeviceId,
    string DeviceType,
    string Unit,
    double NormalMin,
    double NormalMax,
    double? LL,
    double? L,
    double? H,
    double? HH,
    TimeSpan PublishInterval);

public record ActuatorConfig(
    string DeviceId,
    string DeviceType,
    string Unit,
    TimeSpan ToggleInterval);

public static class DeviceDefinitions
{
    public static readonly SensorConfig[] Sensors =
    [
        new("temp-01",      "TemperatureSensor", "°C",   24.0, 28.0, 20.0, 22.0, 30.0, 32.0, TimeSpan.FromSeconds(5)),
        new("ph-01",        "PhSensor",          "pH",   6.5,  7.5,  5.5,  6.0,  8.0,  8.5,  TimeSpan.FromSeconds(10)),
        new("level-01",     "WaterLevelSensor",  "%",    80,   100,  50,   70,   null, null,  TimeSpan.FromSeconds(15)),
        new("oxygen-01",    "OxygenSensor",      "mg/L", 6.0,  8.0,  4.0,  5.0,  10.0, 12.0, TimeSpan.FromSeconds(10)),
        new("turbidity-01", "TurbiditySensor",   "NTU",  0,    5,    null, null, 10,   15,    TimeSpan.FromSeconds(30)),
    ];

    public static readonly ActuatorConfig[] Actuators =
    [
        new("feeder-01", "FishFeeder",       "state", TimeSpan.FromSeconds(60)),
        new("light-01",  "LightController",  "state", TimeSpan.FromSeconds(120)),
    ];
}
