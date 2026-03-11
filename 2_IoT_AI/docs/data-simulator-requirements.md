# Fish Tank IoT Devices – Demo Setup

This document defines a set of **IoT devices for a smart fish tank** used in a meetup demo.  
Each device produces **simple telemetry messages** that can be processed by the IoT system.

The goal is to simulate a **small ecosystem of sensors and actuators** that monitor the aquarium environment and trigger alarms when conditions become unsafe for fish.

---

# Overview

The fish tank contains several IoT devices:

| Device | Type | Purpose |
|------|------|------|
| Temperature Sensor | Sensor | Monitor water temperature |
| pH Sensor | Sensor | Measure water acidity |
| Water Level Sensor | Sensor | Detect evaporation or leaks |
| Oxygen Sensor | Sensor | Measure dissolved oxygen |
| Turbidity Sensor | Sensor | Measure water cleanliness |
| Fish Feeder | Actuator | Automatically feed fish |
| Light Controller | Actuator | Control aquarium lighting |

These devices publish telemetry messages that are processed by the IoT backend.

---

## MQTT topic structure

All devices publish to:

```
fishtank/{deviceId}/telemetry
```

Example: `fishtank/temp-01/telemetry`

---

## Telemetry message format

Every message uses this common envelope:

```json
{
  "deviceId": "temp-01",
  "deviceType": "TemperatureSensor",
  "timestamp": "2026-03-08T14:30:00Z",
  "value": 25.3,
  "unit": "°C"
}
```

| Field | Type | Description |
|-------|------|-------------|
| deviceId | string | Unique device identifier |
| deviceType | string | One of the device types below |
| timestamp | ISO 8601 | UTC time of the reading |
| value | double | The sensor reading or actuator state |
| unit | string | Unit of measurement |

---

## Device definitions

### Sensors

| Device ID | Device Type | Unit | Normal Range | LL | L | H | HH | Publish Interval |
|-----------|-------------|------|-------------|-----|------|------|-----|------------------|
| temp-01 | TemperatureSensor | °C | 24.0 – 28.0 | 20.0 | 22.0 | 30.0 | 32.0 | 5 seconds |
| ph-01 | PhSensor | pH | 6.5 – 7.5 | 5.5 | 6.0 | 8.0 | 8.5 | 10 seconds |
| level-01 | WaterLevelSensor | % | 80 – 100 | 50 | 70 | — | — | 15 seconds |
| oxygen-01 | OxygenSensor | mg/L | 6.0 – 8.0 | 4.0 | 5.0 | 10.0 | 12.0 | 10 seconds |
| turbidity-01 | TurbiditySensor | NTU | 0 – 5 | — | — | 10 | 15 | 30 seconds |

### Alarm levels

Industrial alarm levels are used to manage process safety:

| Level | Name | Meaning | Action |
|-------|------|---------|--------|
| HH | High-High | Critical — dangerous condition | Automatic shutdown / immediate action |
| H | High | Warning — approaching upper limit | Operator attention required |
| L | Low | Warning — approaching lower limit | Operator attention required |
| LL | Low-Low | Critical — dangerous condition | Automatic shutdown / immediate action |

### Actuators

Actuators report their current state as a numeric value:

| Device ID | Device Type | Unit | Values | Publish Interval |
|-----------|-------------|------|--------|-----------------|
| feeder-01 | FishFeeder | state | 0 = idle, 1 = feeding | On state change |
| light-01 | LightController | state | 0 = off, 1 = on | On state change |

---

## Simulation behaviour

The simulator should:

1. **Loop continuously**, publishing telemetry for each sensor at its configured interval.
2. **Generate realistic values** — most readings stay within the normal range with small random drift.
3. **Occasionally produce alarm values** — roughly **5–10% of readings** should drift outside the normal range to trigger alarms.
4. **Actuators change state periodically** — the feeder triggers every ~60 seconds, the light toggles every ~120 seconds.
5. **Use a single MQTT client** connected to the broker, publishing on the per-device topics.

### Value generation strategy

For each sensor reading:
- Start from the midpoint of the normal range.
- Apply a small random walk (±0.5% of range per tick).
- With ~5% probability, spike the value into the alarm zone for 1–3 consecutive readings, then drift back to normal.

---

## Alarm rules

The **TelemetryFunction** should evaluate each reading against the sensor's four alarm thresholds and raise an alarm at the appropriate level:

| Device Type | LL | L | H | HH |
|-------------|-----|------|------|-----|
| TemperatureSensor | < 20.0 | < 22.0 | > 30.0 | > 32.0 |
| PhSensor | < 5.5 | < 6.0 | > 8.0 | > 8.5 |
| WaterLevelSensor | < 50 | < 70 | — | — |
| OxygenSensor | < 4.0 | < 5.0 | > 10.0 | > 12.0 |
| TurbiditySensor | — | — | > 10 | > 15 |

Evaluation priority: check HH/LL first (most severe), then H/L. Only the highest-severity alarm fires per reading.

Alarm message published to Service Bus:

**HH example** — water temperature critically high:
```json
{
  "deviceId": "temp-01",
  "deviceType": "TemperatureSensor",
  "timestamp": "2026-03-08T14:30:00Z",
  "value": 33.1,
  "unit": "°C",
  "alarmLevel": "HH",
  "description": "High-High temperature alarm"
}
```

**L example** — water level dropping:
```json
{
  "deviceId": "level-01",
  "deviceType": "WaterLevelSensor",
  "timestamp": "2026-03-08T14:30:05Z",
  "value": 65.2,
  "unit": "%",
  "alarmLevel": "L",
  "description": "Low water level alarm"
}
```

**LL example** — dissolved oxygen critically low:
```json
{
  "deviceId": "oxygen-01",
  "deviceType": "OxygenSensor",
  "timestamp": "2026-03-08T14:30:10Z",
  "value": 3.8,
  "unit": "mg/L",
  "alarmLevel": "LL",
  "description": "Low-Low oxygen alarm"
}
```

**H example** — turbidity rising:
```json
{
  "deviceId": "turbidity-01",
  "deviceType": "TurbiditySensor",
  "timestamp": "2026-03-08T14:30:30Z",
  "value": 12.4,
  "unit": "NTU",
  "alarmLevel": "H",
  "description": "High turbidity alarm"
}
```

| Field | Type | Description |
|-------|------|-------------|
| alarmLevel | string | One of: `LL`, `L`, `H`, `HH` |
| description | string | Human-readable alarm description |