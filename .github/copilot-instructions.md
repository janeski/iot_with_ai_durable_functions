# copilot-instructions.md

## Project Overview
A demo IoT platform built on Azure for a meetup presentation. Organized into numbered feature folders (e.g. `1_IoT/`). Keep things simple, readable, and easy to follow live.

### Platform pillars
- **Azure Event Grid** — event ingestion
- **Azure Functions (isolated worker)** — serverless processing
- **Azure Service Bus** — async messaging between features
- **TimescaleDB** — time-series storage
- **PostgreSQL** — relational / operational data
- **.NET Aspire** — local dev orchestration and Azure deployment

### Current features

#### 1_IoT — Telemetry & Alarm Processing
1. Devices publish telemetry events to Event Grid.
2. Telemetry Function validates, normalizes, and stores telemetry in TimescaleDB.
3. Telemetry Function publishes a message to Service Bus.
4. Alarm Function consumes the message, evaluates alarm rules, and persists alarm state in PostgreSQL.

> When adding a new feature, document its purpose and event flow here.

---

## Development Goals
- Simple, readable code — this is a demo, not an enterprise codebase
- Clear separation of responsibilities
- Thin Azure Function entry points that delegate to services
- Async/await end-to-end
- Structured logging with context (`DeviceId`, `Metric`, etc.)
- Configuration via strongly typed options — no hardcoded values
- No unnecessary frameworks or abstractions

---

## Technology Stack
- .NET 10 / C# (latest stable versions of all packages)
- .NET Aspire for local orchestration (`AppHost`) and publishing to Azure
- Azure Functions isolated worker
- Azure.Messaging.EventGrid / Azure.Messaging.ServiceBus
- Npgsql + Dapper for database access
- xUnit + FluentAssertions for tests

---

## Solution Structure
Each feature lives in its own numbered folder:

```
<feature_folder>/
  src/
    Functions.<FeatureName>/   # Azure Function app (thin triggers)
    Application/               # use cases, DTOs, service interfaces
    Domain/                    # entities, value objects, domain rules
    Infrastructure/            # repositories, messaging, DB access
    Shared/                    # cross-cutting utilities
  tests/
    UnitTests/
    IntegrationTests/
  docs/
    requirements.md
```

An `AppHost/` project at the repo root wires up all features, databases, and messaging for local dev via Aspire and handles publishing to Azure.

New features replicate this layout (e.g. `2_Analytics/`) and register their Function projects in the AppHost.

### Layer rules
- **Domain** — no Azure or DB SDK dependencies
- **Application** — orchestration and interfaces, no infrastructure details
- **Infrastructure** — repository implementations, Service Bus adapters, DB access
- **Function projects** — triggers only; deserialize, call a service, done

---

## Architecture Rules

### General
- Each Function project owns one trigger type and delegates to application services.
- Features communicate through Service Bus, not direct calls.
- Each feature owns its own storage.

### 1_IoT — Telemetry Function
- Receives Event Grid events → validates → stores in TimescaleDB → publishes to Service Bus.
- Must not contain alarm logic.

### 1_IoT — Alarm Function
- Receives Service Bus messages → evaluates alarm rules → persists state in PostgreSQL.
- Must not write to TimescaleDB.

> Add similar boundary rules here when creating new features.

### Storage
- **TimescaleDB** — append-heavy writes, time-series queries, hypertables
- **PostgreSQL** — relational data, alarm rules/state, lookups
- New features may use other stores if justified

---

## Data Contracts

Use explicit C# models per stage. Don't pass raw JSON between layers.

### Telemetry fields
`DeviceId`, `Timestamp`, `Metric`, `Value`, `Unit`, `Quality`, `Metadata`

### Alarm fields
`AlarmId`, `DeviceId`, `Metric`, `Severity`, `Threshold`, `CurrentValue`, `State`, `TriggeredAt`, `ClearedAt`

---

## Coding Style
- Clear, boring, readable code
- Meaningful names: `TelemetryEvent`, `AlarmRule`, `ProcessTelemetryUseCase`, `ITelemetryRepository`
- Avoid vague names like `Helper`, `Manager`
- Use nullable reference types
- Use constructor injection
- Keep it simple — no over-engineering for a demo

---

## Error Handling
- Log and reject invalid payloads
- Let transient failures retry via Service Bus / Functions retry policies
- Send non-recoverable failures to dead-letter
- Don't swallow exceptions

---

## Testing
- Arrange / Act / Assert
- Meaningful test names: `Should_store_telemetry_when_event_is_valid`
- Focus on: validation, alarm evaluation, state transitions
- Don't generate shallow tests that only assert non-null

---

## Security
- No hardcoded secrets
- Prefer Managed Identity
- Don't log connection strings or secrets

---

## What Copilot Should Avoid
- Business logic inside Function trigger methods
- Monolithic classes
- Magic strings everywhere
- Over-engineered patterns (CQRS, MediatR, generic repositories) unless asked
- Silent catch blocks
