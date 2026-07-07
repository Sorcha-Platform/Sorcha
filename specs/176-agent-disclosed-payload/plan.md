# Implementation Plan: Autonomous agent decides on disclosed application data

**Branch**: `176-agent-disclosed-payload` | **Date**: 2026-07-07 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/176-agent-disclosed-payload/spec.md`

## Summary

The autonomous agent currently decides on an empty payload because the disclosed prior-action data is not
available on the surface it reads (`/api/actions/pending`). This plan implements the already-contracted
**disclosed-data query endpoint** in the blueprint-service (`GET /api/workflows/{instanceId}/actions/{actionId}/disclosures`,
already targeted by `IBlueprintServiceClient.GetDisclosedDataAsync()` and the MCP `DisclosedDataTool`),
backed by a shared disclosure resolver extracted from `ActionExecutionService.ApplyDisclosuresAsync`. The
agent's inbox/decision layer fetches the disclosed payload per pending action, feeds it to the external-check
runner, and **holds** (fail-closed, #1077 pattern) when the disclosed data is unavailable. The evaluated
check facts are logged for explainability. An end-to-end AIAS regression (valid→approved, invalid→rejected)
plus unit coverage guard the behaviour. Design decision and evidence: see [research.md](./research.md).

## Technical Context

**Language/Version**: .NET 10 / C# 14 (nullable enabled, no Release warnings)
**Primary Dependencies**: ASP.NET Core Minimal APIs + Scalar (blueprint-service); `Sorcha.ServiceClients.Http`
(`IBlueprintServiceClient`); `Sorcha.Blueprint.Engine` (`ApplyDisclosures`); `Sorcha.Agent` inbox/decision
(`PollingInboxListener`, `RulesDecisionEngine`, `ExternalCheckRunner`); System.Text.Json.
**Storage**: Existing — sealed transactions in the register (source of the instance's accumulated data);
no new persistence.
**Testing**: xUnit + FluentAssertions + Moq (unit/integration); the AIAS `demos/AIAS/rehearse.ps1` end-to-end
harness (valid + invalid application) as the regression witness.
**Target Platform**: Linux containers (blueprint-service, agent); the agent also runs as a CLI/dotnet tool.
**Project Type**: Web service (blueprint-service endpoint) + client application (`Sorcha.Agent`).
**Performance Goals**: The disclosed-data fetch is an on-demand per-decision read; it must not be added to the
high-frequency `/api/actions/pending` poll. One fetch per pending action the agent decides.
**Constraints**: MUST respect the DAD disclosure model (agent receives only fields disclosed to its
participant — FR-006/FR-010); MUST fail closed on unavailable/partial data (FR-005). No new JWT claim; reuse
the wallet-service caller-wallet resolution `ActionEndpoints` already uses.
**Scale/Scope**: Small, bounded change — one new endpoint (+ shared resolver extraction) in blueprint-service,
one fetch-and-populate change + a fail-closed guard in the agent. No schema/disclosure-model changes.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Assessment |
|---|---|
| **I. Microservices-First** | PASS. Change is confined to blueprint-service (endpoint + resolver) and the agent client. Dependencies flow downward (agent → blueprint-service via `IBlueprintServiceClient`). No upward/core→app deps introduced. |
| **II. Security First** | PASS — **security-positive.** Enforces the disclosure model (only disclosed fields reach the agent) and closes a real fail-open hole (decisions on blank data). Endpoint is authenticated; disclosure applied per recipient wallet. |
| **III. API Documentation** | REQUIRED. The new endpoint MUST have `.WithSummary()`/`.WithDescription()` and XML docs on the response model; surfaces on `/openapi/v1.json` via Scalar. Captured as tasks. |
| **IV. Testing (>85% new code)** | PASS by plan. Unit tests for the disclosure resolver + the endpoint (disclosed vs. undisclosed fields; caller-wallet resolution) and the agent's fetch-and-fail-closed path; integration test on the endpoint; the AIAS E2E regression (SC-006). |
| **V. Code Quality** | PASS. async/await, DI (shared resolver injected), nullable, no new warnings; follows service folder structure. |
| **VI. Blueprint Standards** | N/A — no blueprint authoring changes. |
| **VII. DDD** | PASS. Disclosure is a domain concept; extracting the resolver puts one authority behind it (removes the private fork in `ActionExecutionService`). |
| **VIII. Observability** | PASS. Structured logging of evaluated check facts (Story 3 / FR-008), no string interpolation; endpoint under existing telemetry. |

**Result: PASS (no violations).** Re-checked after Phase 1 design — still PASS; the design adds no new
service, no upward dependency, and strengthens the security posture. No Complexity Tracking entries required.

## Project Structure

### Documentation (this feature)

```
specs/176-agent-disclosed-payload/
├── spec.md              # Feature specification (/speckit.specify)
├── plan.md              # This file (/speckit.plan)
├── research.md          # Phase 0 — design decision (Design A) + evidence
├── data-model.md        # Phase 1 — entities & response shapes
├── quickstart.md        # Phase 1 — how to validate (AIAS E2E)
├── contracts/           # Phase 1 — endpoint + agent-side contract
│   └── disclosed-data-endpoint.md
├── checklists/
│   └── requirements.md  # Spec quality checklist (all pass)
└── tasks.md             # Phase 2 — task breakdown (/speckit.tasks)
```

### Source Code (repository root — files touched)

```
src/Services/Sorcha.Blueprint.Service/
├── Endpoints/ActionEndpoints.cs                 # + GET disclosures route (or a new WorkflowDisclosureEndpoints.cs)
├── Services/
│   ├── Interfaces/IActionDisclosureResolver.cs  # NEW — shared disclosure resolver seam
│   └── Implementation/
│       ├── ActionDisclosureResolver.cs          # NEW — extracted from ApplyDisclosuresAsync
│       └── ActionExecutionService.cs            # refactor to use the shared resolver
└── Models/DisclosedActionData*.cs               # response model (align to IBlueprintServiceClient contract)

src/Common/Sorcha.ServiceClients.Http/Blueprint/
├── IBlueprintServiceClient.cs                   # confirm GetDisclosedDataAsync signature/shape
└── BlueprintServiceClient.cs                    # confirm route + deserialization

src/Apps/Sorcha.Agent/
├── Inbox/PollingInboxListener.cs                # finalise dataSchema mapping; wire disclosed-data fetch
├── Inbox/(fetch)                                # per-pending-action disclosed-data fetch → PreviousPayload
└── Decision/RulesDecisionEngine.cs              # fail-closed hold when disclosed payload required but empty; keep check-facts log

tests/
├── Sorcha.Blueprint.Service.Tests/…             # resolver + endpoint unit/integration tests
└── Sorcha.Agent.Tests/…                         # fetch + fail-closed + decision-on-real-data tests
demos/AIAS/rehearse.ps1                          # E2E regression witness (already exists)
```

**Structure decision**: Web-service + client. Endpoint work lives in blueprint-service (with the resolver
extracted to a service seam); consumption + fail-closed lives in `Sorcha.Agent`; the shared HTTP client
(`IBlueprintServiceClient`) is the seam between them.

## Complexity Tracking

No constitution violations — table intentionally omitted. The one refactor (extracting the disclosure
resolver from `ActionExecutionService`) reduces complexity by removing a private, un-reusable code path and
is covered by tests to prevent behavioural drift.
