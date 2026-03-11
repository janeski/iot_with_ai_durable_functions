# Agent Instructions - Basic IoT Demo (.NET 10 + Aspire)

You are working on a **meetup demo** project that implements a **simple IoT processing flow** in **C# with .NET 10**, orchestrated locally and deployed to Azure using **.NET Aspire**.

See [docs/requirements.md](../docs/requirements.md) for the full solution requirements (architecture, tech stack, Aspire usage, solution structure).

Your goal is to help build a solution that is:
- simple
- readable
- easy to explain live
- easy to run locally with `dotnet run` on the AppHost

Do **not** over-engineer the implementation.

---

## Implementation priorities

Always optimize for:
1. clarity
2. small amount of code
3. easy local execution via `dotnet run` on the AppHost
4. demo value

Do not optimize for:
- enterprise scale
- framework purity
- extensibility for many future scenarios
- advanced cross-cutting concerns

---

## Coding style

Prefer:
- Aspire integrations over manual connection string wiring
- plain DTOs
- small service classes
- plain SQL
- straightforward DI
- simple `if` statements

Avoid unless explicitly requested:
- MediatR
- CQRS
- DDD layers
- repositories for everything
- generic abstractions
- custom frameworks
- event sourcing
- heavy configuration systems
- manual ARM templates (prefer Bicep instead)
