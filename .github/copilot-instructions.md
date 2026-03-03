# Copilot / AI Agent Instructions for Sorcha

Purpose: Give AI coding agents the minimal, actionable knowledge to be productive in this repo.

- **Project Type**: .NET 10 microservice platform using .NET Aspire for local orchestration. Key layers: `src/Apps`, `src/Common`, `src/Core`, `src/Services`.
- **Run locally (recommended)**: use Aspire AppHost to start all services and Redis integration.

  - Command: `dotnet run --project src/Apps/Sorcha.AppHost`
  - Aspire dashboard: `http://localhost:15888`
  - Common endpoints: API Gateway `https://localhost:7082`, Designer `https://localhost:7083`, Engine (varies)

- **If running services individually**: prefer the AppHost for correctness, but individual services can be started with:
  - `dotnet run --project src/Services/Sorcha.ApiGateway`
  - `dotnet run --project src/Services/Sorcha.Blueprint.Service`
  - `dotnet run --project src/Services/Sorcha.Peer.Service`

- **Health endpoints**: services expose `/health` and `/alive`. Use these for liveness checks and Aspire health aggregation.

- **Shared configuration / conventions**:
  - Use the `Sorcha.ServiceDefaults` extension methods. See `src/Common/Sorcha.ServiceDefaults/Extensions.cs` for how services are wired (OpenTelemetry, health, resilience).
  - Map service endpoints with `MapDefaultEndpoints(WebApplication)` where appropriate.
  - OpenTelemetry and resilience policies are configured centrally; avoid duplicating configuration.

- **Blueprint domain & code patterns**:
  - Domain models live in `src/Common/Sorcha.Blueprint.Models/` (Blueprint, Action, Participant, Disclosure).
  - Fluent builders are in `src/Core/Sorcha.Blueprint.Fluent/` — prefer using builders for generating blueprints in tests and examples. Example:

    ```csharp
    var blueprint = BlueprintBuilder.Create()
      .WithTitle("Purchase Order")
      .AddParticipant("buyer", p => p.Named("Buyer"))
      .AddAction(0, a => a
        .WithTitle("Submit Order")
        .SentBy("buyer")
        .RequiresData(d => d.AddProperty("itemName","string"))
      )
      .Build();
    ```

- **Schema handling**:
  - Schema services are under `src/Core/Sorcha.Blueprint.Schemas/`.
  - Use `SchemaLibraryService` and `ISchemaRepository` for schema lookups; client caches schemas in local storage (`LocalStorageSchemaCacheService`) in the Designer.

- **Tests & CI**:
  - Tests live under `tests/` (unit, integration, E2E, performance). Run `dotnet test` at solution root or target a specific test project.
  - Integration tests require Docker (Redis). Ensure Docker Desktop is running for `Sorcha.Gateway.Integration.Tests` and any tests that rely on Redis.
  - Formatting and checks: `dotnet format`, `dotnet list package --vulnerable`, `dotnet list package --outdated`.

- **Conventions for changes**:
  - Keep cross-cutting changes confined to `Sorcha.ServiceDefaults` when possible.
  - Prefer extension methods and DI registration over scattering configuration across projects.
  - Add health checks to new services at `/health` and `/alive` for Aspire to aggregate.
  - When adding HTTP endpoints follow minimal APIs style and register OpenAPI metadata.

- **Where to look for common tasks**:
  - Service orchestration: `src/Apps/Sorcha.AppHost/AppHost.cs`
  - Service defaults & telemetry: `src/Common/Sorcha.ServiceDefaults/` (extension methods, MapDefaultEndpoints)
  - Blueprint models: `src/Common/Sorcha.Blueprint.Models/`
  - Fluent builders: `src/Core/Sorcha.Blueprint.Fluent/`
  - Schema library: `src/Core/Sorcha.Blueprint.Schemas/`
  - API Gateway (YARP): `src/Services/Sorcha.ApiGateway/`

- **Test naming and patterns**:
  - Unit tests follow `MethodName_Scenario_ExpectedBehavior` (see `docs/reference/architecture.md` Test Naming Convention).
  - Use `xUnit`, `Moq`, and `FluentAssertions`.

- **Integration & E2E specifics**:
  - E2E Playwright tests live in `tests/Sorcha.UI.E2E.Tests/`; follow Playwright setup in the README for headless/headed runs.
  - Performance tests (NBomber) are in `tests/Sorcha.Performance.Tests/` and produce reports in `performance-reports/`.

- **Common pitfalls** (discoverable from repo):
  - Don't assume ports are fixed — AppHost or project launchSettings may override ports. Verify console output or Aspire dashboard URLs.
  - Redis-based features require Docker; skip or mock when Docker is unavailable.
  - Peer service is work-in-progress; changes may be incomplete — check `peer-service-design.md` and `peer-service-implementation-plan.md`.

- **PR guidance for AI agents**:
  - Keep PRs focused and limited to one area (engine, schemas, designer). Update `docs/` for behavioral changes.
  - Run `dotnet test` and verify no regressions in unit tests for modified projects.
  - Run `dotnet format` before committing.

If anything here is unclear or you'd like more examples (e.g., exact DI registration snippets or test harness examples), tell me which areas to expand and I will update this file accordingly.
