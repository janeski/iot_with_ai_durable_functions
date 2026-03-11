# IoT with AI & Durable Functions

A demo IoT platform built on Azure for a meetup presentation. It simulates a **smart fish tank ecosystem** that monitors sensor conditions, triggers rule-based alarms, and (in the AI variant) enriches them with AI-powered root-cause analysis using Azure Durable Functions and RAG.

The repository contains two self-contained solutions that build on each other:

| Solution | Description |
|----------|-------------|
| **1_IoT** | Foundational IoT pipeline — telemetry ingestion, rule-based alarms, Grafana dashboard |
| **2_IoT_AI** | Extends 1_IoT with Azure Durable Functions, Azure OpenAI analysis, and RAG via pgvector |

## Technology Stack

- .NET 10 / C# (latest stable packages)
- .NET Aspire — local dev orchestration and Azure deployment
- Azure Functions (isolated worker)
- Azure Service Bus — async messaging
- PostgreSQL + pgvector — relational data, time-series storage, vector embeddings
- Eclipse Mosquitto — MQTT broker
- Grafana — real-time dashboards
- Azure OpenAI (GPT-4.1 + text-embedding-3-small) — AI analysis & embeddings (2_IoT_AI only)
- Azure Durable Functions / Durable Task Scheduler — workflow orchestration (2_IoT_AI only)
- xUnit + FluentAssertions — testing

---

## Simulated Environment — Smart Fish Tank

Both solutions simulate a fish tank with 5 sensors and 2 actuators:

| Device | Type | Unit | Normal Range | LL | L | H | HH | Interval |
|--------|------|------|:---:|:---:|:---:|:---:|:---:|:---:|
| temp-01 | TemperatureSensor | °C | 24–28 | 20.0 | 22.0 | 30.0 | 32.0 | 5 s |
| ph-01 | PhSensor | pH | 6.5–7.5 | 5.5 | 6.0 | 8.0 | 8.5 | 10 s |
| level-01 | WaterLevelSensor | % | 80–100 | 50 | 70 | — | — | 15 s |
| oxygen-01 | OxygenSensor | mg/L | 6.0–8.0 | 4.0 | 5.0 | 10.0 | 12.0 | 10 s |
| turbidity-01 | TurbiditySensor | NTU | 0–5 | — | — | 10 | 15 | 30 s |
| feeder-01 | FishFeeder | state | 0/1 | — | — | — | — | 60 s toggle |
| light-01 | LightController | state | 0/1 | — | — | — | — | 120 s toggle |

The **Device Simulator** generates realistic telemetry using random-walk drift with a ~5 % chance of spiking into alarm zones (HH or LL) for 1–3 readings before drifting back.

### Alarm Evaluation

The `AlarmEvaluator` checks each reading against sensor thresholds:

1. **HH** / **LL** (critical) checked first — highest priority
2. **H** / **L** (warning) checked next
3. Strict comparison (`>` for high, `<` for low) — threshold values themselves do not trigger
4. Null thresholds are skipped (e.g. level-01 has no H/HH)

---

## Solution 1 — Basic IoT (`1_IoT/`)

### Architecture

```
Device Simulator
  │ MQTT: fishtank/{deviceId}/telemetry
  ▼
Mosquitto MQTT Broker
  │
  ▼
Telemetry Function
  ├─► PostgreSQL  telemetry  table
  └─► Service Bus  alarms  queue
        │
        ▼
Alarm Function
  ├─► PostgreSQL  alarms  table
  └─► Logs alarm (CRITICAL / WARNING)
        │
        ▼
Grafana Dashboard
  ├─ Time-series panels (sensor values over time)
  ├─ Alarm annotations (HH/LL = red, H/L = yellow)
  └─ Actuator state panel
```

### Projects

| Project | Role |
|---------|------|
| `BasicIoTDemo.AppHost` | .NET Aspire orchestrator — PostgreSQL, MQTT, Service Bus emulator, Grafana |
| `BasicIoTDemo.TelemetryFunction` | Subscribes to MQTT, stores telemetry in PostgreSQL, publishes to Service Bus |
| `BasicIoTDemo.AlarmFunction` | Consumes Service Bus queue, evaluates alarm rules, persists alarm events |
| `BasicIoTDemo.DeviceSimulator` | Publishes simulated sensor/actuator telemetry via MQTT |
| `BasicIoTDemo.Shared` | Shared models (`TelemetryMessage`, `AlarmMessage`, `AlarmEvaluator`, device definitions) |
| `BasicIoTDemo.ServiceDefaults` | OpenTelemetry, health checks, resilience, service discovery |

### Event Flow

1. **Device Simulator** publishes telemetry JSON to `fishtank/{deviceId}/telemetry` over MQTT.
2. **Telemetry Function** subscribes to `fishtank/+/telemetry`, parses the message, appends a row to the `telemetry` table, and forwards the message to the `alarms` Service Bus queue.
3. **Alarm Function** receives the message, runs `AlarmEvaluator`, and if an alarm fires, inserts into the `alarms` table (for Grafana annotations) and logs it.
4. **Grafana** queries PostgreSQL on a 5-second refresh — time-series panels show sensor values, alarm annotations overlay threshold breaches.

### Data Models

```csharp
record TelemetryMessage(string DeviceId, string DeviceType,
    DateTimeOffset Timestamp, double Value, string Unit);

enum AlarmLevel { L, LL, H, HH }

record AlarmMessage(string DeviceId, string DeviceType,
    DateTimeOffset Timestamp, double Value, string Unit,
    AlarmLevel AlarmLevel, string Description);
```

### Database Tables

| Table | Columns |
|-------|---------|
| `telemetry` | id, device_id, device_type, timestamp, value, unit |
| `alarms` | id, device_id, alarm_level, value, threshold, timestamp, description |

### Tests

- **AlarmEvaluatorTests** (21 tests) — normal values, all alarm levels, threshold boundary behavior, priority rules, null thresholds, message correctness
- **DeviceDefinitionsTests** (6 tests) — sensor/actuator counts, unique IDs, range validity, threshold ordering

---

## Solution 2 — IoT + AI (`2_IoT_AI/`)

Extends Solution 1 with **Azure Durable Functions orchestration**, **Azure OpenAI analysis**, and **RAG via pgvector**.

### Architecture

```
Device Simulator
  │ MQTT: fishtank/{deviceId}/telemetry
  ▼
Mosquitto MQTT Broker
  │
  ▼
Telemetry Function
  ├─► PostgreSQL  telemetry  table
  └─► Service Bus  alarms  queue
        │
        ▼
Alarm Function
  ├─► PostgreSQL  alarms  table
  └─► Service Bus  alarm-analysis  queue  (if alarm fires)
        │
        ▼
Durable Orchestrator  ◄── Durable Task Scheduler (gRPC)
  │
  ├─ 1. FetchRecentTelemetry    ── PostgreSQL (last 50 readings)
  ├─ 2. FetchDeviceContext       ── Device definitions + metadata
  ├─ 3. SearchSimilarAlarms     ── pgvector cosine similarity (top-5)
  ├─ 4. CallAiAnalysis          ── Azure OpenAI GPT-4.1
  ├─ 5. ValidateResult          ── Sanitize AI output
  │
  └─ Fan-out (parallel):
     ├─► UpdatePostgreSql        ── alarm_analyses table
     ├─► StoreAlarmEmbedding     ── pgvector (for future RAG)
     ├─► NotifyViaSendGrid       ── (stub)
     ├─► CreateMaintenanceTicket ── (stub)
     └─► UpdateDashboard         ── (stub)

Grafana Dashboard
  ├─ Time-series panels
  ├─ Alarm annotations
  └─ AI-enriched analysis data
```

### Projects

| Project | Role |
|---------|------|
| `IoT_AI_Demo.AppHost` | Aspire orchestrator — PostgreSQL (pgvector), MQTT, Service Bus, Azure Storage, DTS emulator, Grafana |
| `IoT_AI_Demo.TelemetryFunction` | MQTT subscriber → PostgreSQL + Service Bus |
| `IoT_AI_Demo.AlarmFunction` | Alarm evaluation → PostgreSQL + forwards to `alarm-analysis` queue |
| `IoT_AI_Demo.Orchestrator` | **Durable Functions hub** — orchestration, AI analysis, RAG, downstream actions |
| `IoT_AI_Demo.DeviceSimulator` | MQTT telemetry publisher (same behavior as 1_IoT) |
| `IoT_AI_Demo.Shared` | Extended models — includes `AlarmAnalysisModels` for AI input/output, plus all base models |
| `IoT_AI_Demo.ServiceDefaults` | Shared observability and resilience configuration |

### Durable Orchestration Workflow

When an alarm fires, the **AlarmFunction** publishes to the `alarm-analysis` Service Bus queue, which triggers the Durable Functions orchestrator:

1. **FetchRecentTelemetry** — queries the last 50 readings from PostgreSQL for the device (trend context).
2. **FetchDeviceContext** — looks up sensor metadata, location, and asset group.
3. **SearchSimilarAlarms (RAG)** — builds a text representation of the alarm, generates an embedding via Azure OpenAI `text-embedding-3-small`, and runs a cosine similarity search against `alarm_embeddings` in pgvector. Returns the top-5 most similar past alarms.
4. **CallAiAnalysis** — sends the alarm details, recent telemetry, device context, and similar past alarms to Azure OpenAI GPT-4.1 for root-cause analysis.
5. **ValidateResult** — sanitizes the AI response (fixes missing/invalid severity, root cause, summary).
6. **Parallel fan-out** — five downstream actions execute concurrently:
   - Persist analysis to `alarm_analyses` table
   - Generate & store embedding in pgvector (feeds future RAG)
   - Send notification (stub)
   - Create maintenance ticket if critical (stub)
   - Update dashboard (stub)

### AI Integration

**Azure OpenAI Models:**
- **GPT-4.1** — chat completions for alarm root-cause analysis
- **text-embedding-3-small** — 1536-dimension embeddings for semantic similarity

**System Prompt:** Acts as a control system operator assistant — analyzes alarms objectively, identifies primary vs. consequential alarms during alarm floods, assesses severity based on process risk, and provides concise actionable operator guidance.

**AI Response Format:**
```json
{
  "rootCause": "...",
  "adjustedSeverity": "CRITICAL | WARNING | INFO",
  "recommendedActions": ["..."],
  "summary": "..."
}
```

**Graceful Fallback:** If Azure OpenAI is unavailable, a deterministic mock analyzer provides reasonable suggestions based on alarm level (HH/LL → CRITICAL, H/L → WARNING).

### RAG (Retrieval-Augmented Generation)

Every analyzed alarm is indexed for future retrieval:

1. **Indexing** — after AI analysis, the enriched alarm text (device + level + value + root cause + summary) is embedded and stored in the `alarm_embeddings` pgvector table with an HNSW index.
2. **Retrieval** — when a new alarm fires, similar past alarms are found via `1 - (embedding <=> query::vector)` cosine similarity.
3. **Enrichment** — similar alarms are included in the AI prompt so the model can spot recurring patterns and known failure modes.

The system learns over time — each analyzed alarm improves future analysis.

### Additional Data Models

```csharp
record AlarmAnalysisInput(AlarmMessage Alarm, TelemetryMessage Telemetry);

record AiAnalysisInput(AlarmMessage Alarm, List<TelemetryMessage> RecentTelemetry,
    DeviceContext DeviceContext, List<SimilarAlarmResult> SimilarAlarms);

record AiAnalysisResult(string RootCause, string AdjustedSeverity,
    string[] RecommendedActions, string Summary);

record SimilarAlarmResult(string DeviceId, string AlarmLevel, string RootCause,
    string Summary, double Similarity, DateTimeOffset Timestamp);
```

### Additional Database Tables

| Table | Columns |
|-------|---------|
| `alarm_analyses` | id, device_id, alarm_level, adjusted_severity, value, root_cause, recommended_actions, summary, timestamp, analyzed_at |
| `alarm_embeddings` | id, device_id, alarm_level, root_cause, summary, adjusted_severity, value, embedding vector(1536), timestamp, created_at — HNSW index |

### Tests

All tests from 1_IoT plus:
- **AiAnalyzerTests** — mock fallback behavior, severity mapping, device-specific recommendations, critical vs. warning actions
- **AlarmEmbeddingServiceTests** — text building with/without analysis, deterministic embedding consistency
- **ActivitiesTests** — device context lookup, result validation/sanitization, default values for invalid AI output, stub activity completion

---

## Key Differences Between Solutions

| Aspect | 1_IoT | 2_IoT_AI |
|--------|-------|----------|
| Alarm handling | Rule-based only | Rule-based + AI root-cause analysis |
| Orchestration | None | Azure Durable Functions (chained + fan-out) |
| AI / ML | None | Azure OpenAI GPT-4.1 + embeddings |
| RAG / Vector DB | None | pgvector with HNSW cosine similarity |
| Learning | None | Each alarm improves future analysis via RAG |
| Service Bus queues | `alarms` | `alarms` + `alarm-analysis` |
| DB tables | 2 (telemetry, alarms) | 4 (+ alarm_analyses, alarm_embeddings) |
| Services | 4 | 5 (+ Orchestrator) |
| Failure mode | Immediate, synchronous | Graceful degradation (mock AI, skip RAG, validate results) |
| Infrastructure | PostgreSQL, MQTT, Service Bus, Grafana | + pgvector, Azure Storage, DTS emulator, Azure OpenAI |

---

## Infrastructure

### Local Development (via .NET Aspire)

Both solutions use Aspire to orchestrate all infrastructure locally:

- **PostgreSQL** — `pgvector/pgvector:pg17` image (2_IoT_AI) or standard postgres (1_IoT)
- **Azure Service Bus Emulator** — local messaging
- **Eclipse Mosquitto** — MQTT broker on port 1883
- **Grafana** — pre-provisioned datasource + dashboard, anonymous access, 5-second auto-refresh
- **Azure Storage Emulator** — Durable Functions state (2_IoT_AI only)
- **Durable Task Scheduler gRPC Emulator** — orchestration runtime (2_IoT_AI only)

### Azure Deployment

Aspire publishes to Azure via `azd`:
- PostgreSQL → Azure Database for PostgreSQL Flexible Server
- Service Bus → Azure Service Bus
- Functions → Azure Functions (isolated worker)
- OpenAI → Azure OpenAI (GPT-4.1 + text-embedding-3-small)

---

## Getting Started

### Prerequisites

- .NET 10 SDK
- Docker Desktop (for Aspire containers)
- Azure CLI (for Azure deployment / OpenAI access)

### Run Locally

```bash
# Solution 1 — Basic IoT
cd 1_IoT/src/BasicIoTDemo.AppHost
dotnet run

# Solution 2 — IoT + AI
cd 2_IoT_AI/src/IoT_AI_Demo.AppHost
dotnet run
```

The Aspire dashboard URL will appear in the console output. From there you can access Grafana, view logs, and monitor all services.

### Azure OpenAI (2_IoT_AI only)

To use real AI instead of mock fallbacks, set the following environment variables in the AppHost (already configured in the code):

- `AzureOpenAI__Endpoint` — your Azure OpenAI endpoint URL
- `AzureOpenAI__Deployment` — chat model deployment name (e.g. `gpt-4.1`)
- `AzureOpenAI__EmbeddingDeployment` — embedding model deployment name (e.g. `text-embedding-3-small`)

Authentication uses `DefaultAzureCredential` — log in with `az login` and ensure your account has the **Cognitive Services OpenAI User** role on the OpenAI resource.

---

## Solution Structure

```
├── 1_IoT/
│   ├── src/
│   │   ├── BasicIoTDemo.AppHost/          # Aspire orchestrator
│   │   ├── BasicIoTDemo.TelemetryFunction/ # MQTT → PostgreSQL + Service Bus
│   │   ├── BasicIoTDemo.AlarmFunction/     # Alarm evaluation + persistence
│   │   ├── BasicIoTDemo.DeviceSimulator/   # Fish tank simulator
│   │   ├── BasicIoTDemo.Shared/            # Models, alarm evaluator, device defs
│   │   └── BasicIoTDemo.ServiceDefaults/   # OpenTelemetry, health checks
│   ├── tests/
│   │   └── BasicIoTDemo.Tests/
│   └── docs/
│
├── 2_IoT_AI/
│   ├── src/
│   │   ├── IoT_AI_Demo.AppHost/            # Aspire orchestrator (extended)
│   │   ├── IoT_AI_Demo.TelemetryFunction/  # MQTT → PostgreSQL + Service Bus
│   │   ├── IoT_AI_Demo.AlarmFunction/      # Alarm eval + alarm-analysis queue
│   │   ├── IoT_AI_Demo.Orchestrator/       # Durable Functions, AI, RAG
│   │   ├── IoT_AI_Demo.DeviceSimulator/    # Fish tank simulator
│   │   ├── IoT_AI_Demo.Shared/             # Extended models + AI contracts
│   │   └── IoT_AI_Demo.ServiceDefaults/    # OpenTelemetry, health checks
│   ├── tests/
│   │   └── IoT_AI_Demo.Tests/
│   └── docs/
│
└── README.md
```