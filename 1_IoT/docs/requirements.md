# Solution Requirements — Basic IoT Demo

## Main architecture

Implement this flow:

1. **Device simulator** publishes telemetry over **MQTT**
   - See [data-simulator-requirements.md](data-simulator-requirements.md) for the full list of devices, telemetry schemas, and simulation behaviour
   - The simulator must generate all telemetry data based on the device definitions in that file
2. **Event Grid** forwards telemetry to **Telemetry Function**
3. **Telemetry Function**
   - parses telemetry
   - stores telemetry in **TimescaleDB**
   - sends telemetry to **Alarm Function** for alarm evaluation

4. **Alarm Function**
   - reads telemetry from Service Bus
   - evaluates against alarm rules
   - if alarm condition met, sends notification through **SendGrid**

---

## .NET Aspire usage

### Local development
- Use an **AppHost** project to orchestrate all services locally.
- Use Aspire to spin up **PostgreSQL** (with TimescaleDB) and **Azure Service Bus emulator** (or Azurite where applicable) as container resources.
- Use Aspire **service discovery** so projects reference each other by name, not hardcoded URLs.
- Use a **ServiceDefaults** project for shared configuration: OpenTelemetry, health checks, resilience.
- The DeviceSimulator should be added as a project resource in the AppHost so it starts automatically.

### Deployment to Azure
- Use **Azure Developer CLI (`azd`)** with Aspire's manifest to deploy.
- Aspire maps resources to Azure services:
  - PostgreSQL -> **Azure Database for PostgreSQL Flexible Server**
  - Service Bus -> **Azure Service Bus**
  - Azure Functions -> **Azure Functions (isolated worker)**
- Use `azd init` and `azd up` to provision and deploy.
- Use **Bicep** for Azure infrastructure definitions.
- Aspire's `azd` integration can generate a starting Bicep scaffold; extend it with custom Bicep as needed.

---

## Tech constraints

Use:
- **C#**
- **.NET 10**
- **.NET Aspire** (AppHost + ServiceDefaults)
- **Azure Functions isolated worker**
- **MQTTnet**
- **Npgsql** (via Aspire PostgreSQL integration)
- **Azure.Messaging.ServiceBus** (via Aspire Service Bus integration)
- **SendGrid**
- **HttpClient**
- **Azure Developer CLI (`azd`)** for deployment

---

## Grafana dashboard

### Overview
Add a **Grafana** instance as an Aspire container resource to visualize telemetry time-series and alarm events from the PostgreSQL database.

### Requirements

- Aspire AppHost adds Grafana as a container resource (`grafana/grafana`) with a pre-provisioned **PostgreSQL datasource** pointing to the `telemetrydb` database.
- A **pre-built dashboard** (JSON provisioning) is mounted into the Grafana container, containing:
  1. **Time-series panels** — one panel per sensor device (`temp-01`, `ph-01`, `level-01`, `oxygen-01`, `turbidity-01`) showing `value` over time.
  2. **Alarm annotations** — alarm events from the database displayed as **annotations** on the time-series panels, so the user can see *when* alarms occurred and at which severity level (L, H, LL, HH).
  3. **Actuator state panel** — a panel showing actuator state changes (`feeder-01`, `light-01`) over time.
- The dashboard is **automatically provisioned** on startup via Grafana's file-based provisioning (datasources and dashboards YAML + JSON).
- Grafana runs with **anonymous access enabled** (no login required) for demo simplicity.
- The dashboard auto-refreshes every **5 seconds**.

### Data sources
- **Telemetry table** in PostgreSQL — used for time-series panels (queried by `device_id`, ordered by `timestamp`).
- **Alarms table** in PostgreSQL — used for annotations (the alarm function should persist alarm events to the database in addition to sending notifications).

### Alarm persistence (new requirement)
- The **Alarm Function** must write alarm events to a PostgreSQL `alarms` table with at least: `id`, `device_id`, `alarm_level`, `value`, `threshold`, `timestamp`.
- This table is the data source for Grafana alarm annotations.
- The Alarm Function needs a `WithReference(postgres)` and `WaitFor(postgres)` in the AppHost.

### Aspire wiring
- Grafana container gets a `WithEnvironment` or bind-mount for:
  - Datasource provisioning YAML (pointing to the Aspire-managed PostgreSQL connection)
  - Dashboard provisioning YAML + dashboard JSON file
- Grafana `WaitFor(postgres)` to ensure the database is available.

### Deployment note
- Grafana is for **local development only**. It does not need to deploy to Azure.

---

## Solution structure

```text
BasicIoTDemo.sln

src/
  BasicIoTDemo.AppHost/           # Aspire orchestrator
    grafana/                      # Grafana provisioning files
      dashboards/
        dashboard.json            # Pre-built IoT dashboard
      provisioning/
        datasources.yaml          # PostgreSQL datasource config
        dashboards.yaml           # Dashboard provisioning config
  BasicIoTDemo.ServiceDefaults/   # Shared config (telemetry, health, resilience)
  Shared/                         # Shared DTOs and constants
  DeviceSimulator/                # MQTT telemetry publisher
  TelemetryFunction/              # Azure Function: ingest + store + alarm
  AlarmFunction/                  # Azure Function: notify on alarm
```
