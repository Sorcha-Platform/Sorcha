# Implementation Plan: Agent Persona Mode

**Branch**: `110-agent-persona-mode` | **Date**: 2026-04-22 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/110-agent-persona-mode/spec.md`

## Summary

Add an additive "persona loop" to `Sorcha.Agent` that runs alongside the existing reactive inbox loop and submits workflow-starting actions on a trigger. A persona is a JSON file referenced by the actor configuration via a new optional `personaFile` field. Triggers in v1 are `once` (one-shot) and `interval` (recurring, bounded by `maxIterations` and/or `until`). Payloads are JSON templates with a small fixed vocabulary of substitution tokens (`${now}`, `${uuid}`, `${counter}`, `${random.int|decimal|choice}`). The persona reuses the agent's existing authentication, `HttpClient`, and `ActionExecutor`; there is no new auth surface and no separate binary.

This unblocks the TradeFinance and ConstructionPermit walkthroughs (which currently hang because starting actions have nothing in any inbox) and enables scenario-register data generation without new runtime infrastructure.

## Technical Context

**Language/Version**: C# 14, .NET 10
**Primary Dependencies**: `System.CommandLine` (already in Sorcha.Agent), `System.Text.Json` / `JsonNode` (already in Sorcha.Agent), existing `Sorcha.Agent.Auth.AgentAuthService`, existing `Sorcha.Agent.Execution.ActionExecutor`, existing Blueprint Service REST API
**Storage**: In-memory only for v1 (persona iteration counter resets on process restart — deliberate trade-off documented in spec Assumptions)
**Testing**: xUnit + FluentAssertions + Moq (matches existing `tests/Sorcha.Agent.Tests` project)
**Target Platform**: Any OS where `dotnet` runs (agent already runs cross-platform)
**Project Type**: Single project addition inside the existing `src/Apps/Sorcha.Agent` console app
**Performance Goals**: Persona tick overhead negligible compared to HTTP submission cost; reactive inbox latency MUST NOT regress by more than 25% when a persona is present (per spec SC-004)
**Constraints**: Strictly additive — no opt-in, no regression (FR-016); no new service clients, no new auth paths (FR-013)
**Scale/Scope**: One persona per agent; walkthrough scenarios in the low tens of agents; recurring personas capped at `maxIterations` — no need for back-pressure architecture beyond respecting the target service's existing rate limits

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Compliance | Notes |
|-----------|------------|-------|
| I. Microservices-First | ✅ | Additive change inside one existing service app. No new services, no new cross-service dependencies. |
| II. Security First | ✅ | No new secrets, no new external boundary. Persona submissions reuse the agent's existing JWT-bearing `HttpClient` and `AgentAuthService` (FR-013). Persona files are author-controlled scenario config, not user input. Input validation handled by the payload-token schema parser (FR-010). |
| III. API Documentation | ✅ (N/A) | No new HTTP endpoints. Persona file schema is JSON + documented in quickstart. No OpenAPI surface changes. |
| IV. Testing Requirements | ✅ | All new classes covered by xUnit unit tests; integration test for one-shot kickoff against a mocked Blueprint Service; target ≥85% coverage for new code. Tests deterministic (fixed seed for `random.*` tokens in unit tests). |
| V. Code Quality | ✅ | `async`/`await` throughout; DI via constructor injection matching existing agent patterns; nullable reference types enabled; no warnings. |
| VI. Blueprint Standards | ✅ (N/A) | No new blueprints. Personas reference existing blueprints by ID. |
| VII. Domain-Driven Design | ✅ | New terms added: **Persona**, **Trigger**, **PayloadTemplate**. These extend — not replace — the existing Blueprint/Action/Participant vocabulary. |
| VIII. Observability | ✅ | Persona loop emits structured logs via existing `ILogger<T>` pattern. Each fire logged with iteration number, target blueprint/action, outcome. Audit entries routed through existing `AuditLogger`. |

**Result**: PASS. No violations, no Complexity Tracking entries required.

## Project Structure

### Documentation (this feature)

```text
specs/110-agent-persona-mode/
├── plan.md              # This file
├── spec.md              # Feature specification
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/
│   └── persona-schema.json   # JSON Schema for persona files
└── checklists/
    └── requirements.md  # Spec quality checklist (existing)
```

### Source Code (repository root)

```text
src/Apps/Sorcha.Agent/
├── Configuration/
│   ├── ActorDefinition.cs            # EXISTING — add optional PersonaFile property
│   ├── ActorDefinitionLoader.cs      # EXISTING — resolve persona file path alongside actor file
│   └── VariableResolver.cs           # EXISTING — reused for ${...} state placeholders; persona tokens are separate
├── Persona/                           # NEW folder
│   ├── PersonaDefinition.cs          # NEW — root persona record + Trigger / StopConditions / PayloadTemplate sub-records
│   ├── PersonaDefinitionLoader.cs    # NEW — load + validate persona JSON file
│   ├── PersonaSchemaValidator.cs     # NEW — fail-fast token validation (FR-010)
│   ├── IPayloadTokenResolver.cs      # NEW — interface for token evaluation
│   ├── PayloadTokenResolver.cs       # NEW — evaluates ${now}, ${uuid}, ${counter}, ${random.*}
│   ├── IPersonaSubmitter.cs          # NEW — submits a starting action for a persona fire
│   ├── PersonaSubmitter.cs           # NEW — thin wrapper over existing Blueprint Service submission path
│   ├── IPersonaLoop.cs               # NEW — runs the persona's trigger loop
│   ├── OnceTriggerLoop.cs            # NEW — fires once then terminates
│   ├── IntervalTriggerLoop.cs        # NEW — fires on interval subject to stop conditions
│   └── PersonaHost.cs                # NEW — composes loader + loop + submitter; launched from RunCommand
├── Commands/
│   └── RunCommand.cs                 # EXISTING — wire in PersonaHost alongside inbox loop (additive)
└── Program.cs                        # EXISTING — no change

tests/Sorcha.Agent.Tests/
└── Persona/                           # NEW test folder
    ├── PersonaDefinitionLoaderTests.cs
    ├── PayloadTokenResolverTests.cs
    ├── OnceTriggerLoopTests.cs
    ├── IntervalTriggerLoopTests.cs
    ├── PersonaSchemaValidatorTests.cs
    └── PersonaHostIntegrationTests.cs  # exercises RunCommand wiring with a mocked Blueprint Service

walkthroughs/TradeFinance/
├── actors/
│   └── procurement-mgr.json           # EXISTING — add "personaFile" field pointing to persona file
├── personas/                          # NEW folder
│   └── procurement-mgr-kickoff.persona.json  # NEW — one-shot Raise PO persona
└── run-agents.ps1                     # EXISTING — no change needed (persona loads automatically from actor config)

walkthroughs/ConstructionPermit/
├── actors/…                            # EXISTING — add personaFile to the first-action agent
├── personas/                          # NEW folder
│   └── <first-action-actor>-kickoff.persona.json   # NEW
└── run-agents.ps1                     # EXISTING — no change
```

**Structure Decision**: Single-project addition to `src/Apps/Sorcha.Agent`. A new `Persona/` folder isolates all persona logic; actor-config changes are additive (one optional field). Walkthrough changes are data-only: one new persona file per walkthrough plus a one-line reference in the existing actor config. No changes to `run-agents.ps1`, no new Docker images, no new services.

## Complexity Tracking

*None required — Constitution Check passes without justified violations.*
