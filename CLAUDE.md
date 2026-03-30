# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

This is a **meetup demo** project showcasing IoT telemetry processing on .NET 10 + Aspire. It contains two independent solutions that build on each other:

- **`1_IoT/`** — Basic IoT pipeline: MQTT ingestion → PostgreSQL storage → alarm evaluation
- **`2_IoT_AI/`** — Extends 1_IoT with Durable Functions orchestration, Azure OpenAI RAG analysis, and a Python process mining service

The simulated domain is a **smart fish tank** with 5 sensors (temperature, pH, water level, oxygen, turbidity) and 2 actuators.

## Commands

### Solution 1 — Basic IoT

```bash
# Run (starts all containers via Aspire)
cd 1_IoT/src/BasicIoTDemo.AppHost && dotnet run

# Build
cd 1_IoT/src/BasicIoTDemo.AppHost && dotnet build

# Test
cd 1_IoT && dotnet test tests/BasicIoTDemo.Tests/BasicIoTDemo.Tests.csproj
```

### Solution 2 — IoT + AI

```bash
# Run (starts all containers via Aspire)
cd 2_IoT_AI/src/IoT_AI_Demo.AppHost && dotnet run

# Build
cd 2_IoT_AI/src/IoT_AI_Demo.AppHost && dotnet build

# Test
cd 2_IoT_AI && dotnet test tests/IoT_AI_Demo.Tests/IoT_AI_Demo.Tests.csproj
```

### Python Process Mining Service (2_IoT_AI only)

```bash
cd 2_IoT_AI/src/IoT_AI_Demo.ProcessMining
pip install -r requirements.txt
uvicorn app.main:app --host 0.0.0.0 --port 8000
```

## Architecture

### Solution 1 Data Flow

```
DeviceSimulator --MQTT--> Mosquitto --> TelemetryFunction --> PostgreSQL
                                                         └--> ServiceBus(alarms) --> AlarmFunction --> PostgreSQL
```

Grafana reads directly from PostgreSQL for dashboards.

### Solution 2 Data Flow

Same as Solution 1, plus:

```
AlarmFunction --ServiceBus(alarm-analysis)--> AlarmAnalysisOrchestrator (Durable Functions)
    ├── FetchRecentTelemetry (PostgreSQL)
    ├── FetchDeviceContext
    ├── SearchSimilarAlarms (pgvector cosine similarity RAG)
    ├── CallAiAnalysis (Azure OpenAI GPT-4.1)
    ├── ValidateResult
    └── Fan-out: UpdatePostgreSql | StoreAlarmEmbedding | stubs (notifications, tickets, dashboard)
```

The **Process Mining service** (FastAPI/Python) receives OTLP traces from all .NET services and exposes endpoints for Petri net discovery, conformance checking, and performance analysis via pm4py.

### Infrastructure (Aspire-managed for local dev)

| Resource | Solution 1 | Solution 2 |
|---|---|---|
| PostgreSQL | `postgres` image | `pgvector/pgvector:pg17` |
| MQTT | Mosquitto (port 1883) | same |
| Service Bus | Emulator, `alarms` queue | Emulator, `alarms` + `alarm-analysis` queues |
| Storage | — | Azure Storage emulator (Durable Functions state) |
| Durable Task Scheduler | — | gRPC emulator (port 8080, dashboard 8082) |
| Process Mining | — | FastAPI container (port 8000) |
| Grafana | port 3000 | port 61364 (dev tunnel) |

### Key Files

| File | Purpose |
|---|---|
| `*/src/*.AppHost/AppHost.cs` | Aspire resource wiring — start here for infrastructure |
| `1_IoT/src/BasicIoTDemo.Shared/AlarmEvaluator.cs` | Core alarm evaluation logic (domain, no dependencies) |
| `1_IoT/src/BasicIoTDemo.Shared/DeviceDefinitions.cs` | Sensor/actuator definitions and thresholds |
| `2_IoT_AI/src/IoT_AI_Demo.Orchestrator/AlarmAnalysisOrchestrator.cs` | Durable orchestration definition |
| `2_IoT_AI/src/IoT_AI_Demo.Orchestrator/Activities.cs` | All durable activity implementations |
| `2_IoT_AI/src/IoT_AI_Demo.Orchestrator/AiAnalyzer.cs` | Azure OpenAI integration (with deterministic fallback) |
| `2_IoT_AI/src/IoT_AI_Demo.Orchestrator/AlarmEmbeddingService.cs` | pgvector RAG embedding service |
| `2_IoT_AI/src/IoT_AI_Demo.ProcessMining/app/main.py` | FastAPI endpoints for process mining |

### Database Schema (Solution 2)

```sql
telemetry (id, device_id, device_type, timestamp, value, unit)
alarms (id, device_id, alarm_level, value, threshold, timestamp, description)
alarm_analyses (id, device_id, alarm_level, adjusted_severity, value, root_cause, recommended_actions, summary, timestamp, analyzed_at)
alarm_embeddings (id, device_id, alarm_level, root_cause, summary, adjusted_severity, value, embedding vector(1536), timestamp, created_at)  -- HNSW index
```

### Azure OpenAI (Solution 2)

- Endpoint: `https://dotnet-mvp-meetup.openai.azure.com/`
- Chat model: `gpt-4.1`
- Embedding model: `text-embedding-3-small` (1536 dimensions)
- Auth: `DefaultAzureCredential` (managed identity in Azure, local credential chain locally)
- Fallback: deterministic mock analyzer when OpenAI is unavailable

## Coding Style

This is a **demo project** — optimize for clarity and minimal code, not enterprise patterns.

**Prefer:** Aspire integrations, plain DTOs, small service classes, plain SQL, straightforward DI, simple `if` statements.

**Avoid:** MediatR, CQRS, DDD layers, repository pattern, generic abstractions, custom frameworks, event sourcing, heavy configuration systems.

Each Azure Function owns one trigger type. Features communicate via Service Bus, not direct calls. The domain layer (`Shared` project) has zero Azure/DB dependencies.
