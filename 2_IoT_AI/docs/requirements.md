# Solution Requirements — IoT AI Demo

## Main architecture

### Phase 1 — Telemetry & Alarm Processing

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

### Phase 2 — AI-Powered Alarm Analysis (Durable Functions)

When the Alarm Function detects a rule-based alarm, it kicks off an **Azure Durable Functions orchestrator** that enriches the alarm with context and AI analysis, then triggers downstream actions.

```
Telemetry
   ↓
Rule Alarm
   ↓
Durable Orchestrator
   ├─ Fetch Recent Telemetry
   ├─ Fetch Device / Asset Context
   ├─ Call AI Analysis
   ├─ Validate Result
   └─ Trigger Actions
          ├─ Update PostgreSQL
          ├─ Notify via SendGrid
          ├─ Create Maintenance Ticket
          └─ Update Dashboard / Twin
```

#### Orchestrator flow

1. **Alarm Function** detects an alarm condition and starts a new **Durable Orchestrator** instance, passing the alarm details as input.
2. **Fetch Recent Telemetry** — activity function queries TimescaleDB for the last N minutes of telemetry for the affected device, providing trend context.
3. **Fetch Device / Asset Context** — activity function retrieves device metadata, maintenance history, and asset information from PostgreSQL.
4. **Call AI Analysis** — activity function sends the alarm, recent telemetry, and device context to an AI model (e.g. Azure OpenAI) to get:
   - Root-cause hypothesis
   - Severity assessment (confirm, escalate, or downgrade the alarm level)
   - Recommended actions
5. **Validate Result** — activity function checks the AI response for completeness and sanity (e.g. required fields present, severity is a known value). If validation fails, the orchestrator falls back to the original alarm data.
6. **Trigger Actions** — the orchestrator fans out to execute downstream actions in parallel:
   - **Update PostgreSQL** — persist the enriched alarm analysis (AI summary, adjusted severity) to an `alarm_analyses` table.
   - **Notify via SendGrid** — send an enriched email notification containing the AI analysis and recommended actions.
   - **Create Maintenance Ticket** — call an external API (or stub) to create a maintenance work order when the AI recommends intervention.
   - **Update Dashboard / Twin** — push the enriched alarm state to the dashboard data source (PostgreSQL) so Grafana reflects AI insights.

### Phase 3 — Alarm RAG with Embeddings

Every alarm analysis is stored as a vector embedding in PostgreSQL (via **pgvector**), building a searchable knowledge base of past alarms. Before the AI analysis step, the orchestrator retrieves similar past alarms to augment the prompt with historical context — a classic **Retrieval-Augmented Generation (RAG)** pattern.

```
New Alarm
   ↓
Search Similar Alarms  ← pgvector cosine similarity
   ↓
AI Analysis (enriched with past alarm context)
   ↓
Store Alarm Embedding  → pgvector for future retrieval
```

#### Components

1. **pgvector on PostgreSQL** — The AppHost uses the `pgvector/pgvector:pg17` Docker image. A dedicated `alarm_embeddings` table stores alarm metadata alongside a `vector(1536)` column with an HNSW index for fast cosine similarity search.
2. **AlarmEmbeddingService** — Generates text embeddings via Azure OpenAI (`text-embedding-3-small`) or a deterministic mock when no endpoint is configured. Handles storage and similarity search against pgvector.
3. **SearchSimilarAlarms** activity — Runs before AI analysis. Builds a text representation of the incoming alarm, generates an embedding, and queries for the top-5 most similar past alarms.
4. **StoreAlarmEmbedding** activity — Runs in the fan-out step after AI analysis. Stores the enriched alarm text (including root cause and summary) as an embedding for future retrieval.
5. **Enhanced AI prompt** — The AI analysis prompt now includes a "Similar past alarms" section listing relevant historical context (device, level, root cause, summary, similarity score).

#### Updated orchestrator flow

```
Durable Orchestrator
   ├─ Fetch Recent Telemetry
   ├─ Fetch Device / Asset Context
   ├─ Search Similar Alarms (RAG retrieval)
   ├─ Call AI Analysis (enriched with similar alarms)
   ├─ Validate Result
   └─ Trigger Actions
          ├─ Update PostgreSQL
          ├─ Store Alarm Embedding (RAG ingestion)
          ├─ Notify via SendGrid
          ├─ Create Maintenance Ticket
          └─ Update Dashboard / Twin
```

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
- **Azure Durable Functions** (orchestrator + activity functions)
- **MQTTnet**
- **Npgsql** (via Aspire PostgreSQL integration)
- **pgvector** (vector similarity search on PostgreSQL, `pgvector/pgvector:pg17` image)
- **Azure.Messaging.ServiceBus** (via Aspire Service Bus integration)
- **Azure OpenAI** (for AI-powered alarm analysis)
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
IoT_AI_Demo.sln

src/
  IoT_AI_Demo.AppHost/            # Aspire orchestrator
    grafana/                      # Grafana provisioning files
      dashboards/
        dashboard.json            # Pre-built IoT dashboard
      provisioning/
        datasources.yaml          # PostgreSQL datasource config
        dashboards.yaml           # Dashboard provisioning config
  IoT_AI_Demo.ServiceDefaults/    # Shared config (telemetry, health, resilience)
  IoT_AI_Demo.Shared/             # Shared DTOs and constants
  IoT_AI_Demo.DeviceSimulator/    # MQTT telemetry publisher
  IoT_AI_Demo.TelemetryFunction/  # Azure Function: ingest + store + forward
  IoT_AI_Demo.AlarmFunction/      # Azure Function: rule evaluation + start orchestrator
  IoT_AI_Demo.Orchestrator/       # Durable Functions: AI analysis orchestrator

tests/
  IoT_AI_Demo.Tests/              # Unit & integration tests
```
