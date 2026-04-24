# Implementation Plan: Timebound Presentation Lifecycle

**Branch**: `111-presentation-lifecycle` | **Date**: 2026-04-23 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/111-presentation-lifecycle/spec.md`

## Summary

Introduce a three-event lifecycle (`presentation-initiated`, `presentation-outcome`, `presentation-abandoned`) as first-class transaction types on the Sorcha register, replacing today's single-shot write for presentation-required actions. Scoped initially to HAIP external-wallet credential presentations (the existing `HaipExternalWallet` targetAudience path in `ActionExecutionService`), but the lifecycle primitive itself is consumer-agnostic so future timebound-evidence flows can plug in without re-implementation. Transient pending-presentation state lives in the HAIP service's existing Redis (co-located with `PreAuthCodeStore` / `NonceStore` / `AccessTokenStore`). Abandonment is an opt-in per-blueprint behaviour driven by a background poller.

## Technical Context

**Language/Version**: C# 14 / .NET 10
**Primary Dependencies**: Sorcha.Blueprint.Service, Sorcha.Haip.Service, Sorcha.ServiceClients.Http (`HaipServiceClient`), StackExchange.Redis, MongoDB.Driver (via Sorcha.Register.Service), Microsoft.AspNetCore.SignalR, xUnit + FluentAssertions + Moq
**Storage**: MongoDB (per-register transactions via Sorcha.Register.Service), Redis (transient pending-presentation state), PostgreSQL (instance metadata via Sorcha.Blueprint.Service EF context)
**Testing**: xUnit v3.2.2 unit + integration, WebApplicationFactory for Blueprint/HAIP service integration tests, shared WalkThrough module for end-to-end verification against Docker
**Target Platform**: Linux containers (docker-compose.yml) and .NET Aspire AppHost for local debug
**Project Type**: single — existing microservices in the solution (no new service); most new code lives in `src/Services/Sorcha.Blueprint.Service` with lifecycle types in `src/Common/Sorcha.Blueprint.Models` and the consumer contract in a new `src/Common/Sorcha.PresentationLifecycle.Abstractions` project
**Performance Goals**: `presentation-initiated` transaction sealed within the normal action-submission budget (p95 < 2s) — adds one extra docket vs. today, no new external round-trip. Abandonment detection latency ≤ 60s after window expiry (SC-006).
**Constraints**: No regression on existing CSRF protection (OpenID4VP `state` parameter). No change to register replication, encryption pipeline, or disclosure model. Redis state must survive normal HTTP round-trips but not process restarts (by design — citizen resubmits).
**Scale/Scope**: Dominant cardinality = instances × actions-with-credentialRequirements. For the planning workload that drives the motivating SEC-014 backlog item, this is bounded (<300 Significant-Works presentations/year, <100k Minor-Works attempts/year). Three new transaction types × ~six test-matrix shapes (success, decline, abandon, late-outcome-after-abandon, retry after decline, rate-limit-exceeded) = ~18 integration-test scenarios + equivalent unit coverage.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design.*

| Principle | Status | Notes |
|---|---|---|
| I. Microservices-First Architecture | PASS | No new service. Lifecycle primitive lives in existing Blueprint Service; HAIP Service adds one abandonment-sweeper background task. Dependencies flow downward only. |
| II. Security First | PASS | Existing CSRF (`state` on verifier callback) preserved; attempt rate-limiting enforced at endpoint (FR-011); no credential data in attempt records (FR-002); success payloads use existing encryption pipeline (assumption validated). |
| III. API Documentation | PASS | New/changed endpoints documented via .NET 10 OpenAPI + Scalar; XML comments mandatory on all public types. |
| IV. Testing Requirements | PASS | xUnit unit + integration coverage for all 18 lifecycle scenarios; >85% coverage on new code; race/idempotency tests deterministic via injectable clock and test-only rate-limit bypass. |
| V. Code Quality | PASS | C# 14 / .NET 10, nullable enabled, async/await, DI; no compiler warnings. |
| VI. Blueprint Creation Standards | PASS | Three new blueprint configuration fields (`recordAbandonment`, `outcomeDetailLevel`, `presentationValidityWindowSeconds`) added to the JSON schema with JsonSchema.Net validation. No Fluent-API-only paths. |
| VII. Domain-Driven Design | PASS | Introduces a first-class domain term: **Presentation** (a citizen-asserted proof of credential possession). The lifecycle primitive names are ubiquitous across spec, code, logs, and docs. |
| VIII. Observability by Default | PASS | New OTel spans: `presentation.initiated`, `presentation.outcome`, `presentation.abandoned`; structured log events with correlation on `presentationRequestId`; metrics: attempts-per-outcome, abandonment rate, rate-limit rejections. |

**No violations. Complexity Tracking table empty.**

**Post-Phase-1 re-evaluation (2026-04-23)**: data-model, contracts, and quickstart introduced no new Constitution violations. `Sorcha.PresentationLifecycle.Abstractions` is a new project but is a thin abstractions package (interface + records only) justified by Principle I's "minimal coupling between services" — it prevents Blueprint Service from dragging HAIP-specific types into consumer-agnostic code. All other principles (Security, Testing, Observability, Blueprint Standards) remain GREEN as documented above.

## Project Structure

### Documentation (this feature)

```text
specs/111-presentation-lifecycle/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output — OpenAPI + transaction schemas
└── checklists/
    └── requirements.md  # /speckit.specify output (already green)
```

### Source Code (repository root)

```text
src/
├── Common/
│   ├── Sorcha.PresentationLifecycle.Abstractions/   # NEW — consumer-agnostic primitive
│   │   ├── IPresentationConsumer.cs                 # contract external consumers implement
│   │   ├── PresentationLifecycleEvents.cs           # event records (initiated/outcome/abandoned)
│   │   ├── PresentationOutcomeKind.cs               # success | decline
│   │   └── PresentationDeclineReason.cs             # reason-code enum
│   ├── Sorcha.Blueprint.Models/
│   │   ├── Credentials/
│   │   │   └── CredentialRequirement.cs             # EXISTS — add optional lifecycle config
│   │   └── BlueprintPresentationConfig.cs           # NEW — per-blueprint record
│   └── Sorcha.Register.Models/
│       └── Enums/TransactionType.cs                 # EXISTS — add PresentationInitiated/Outcome/Abandoned
├── Services/
│   ├── Sorcha.Blueprint.Service/
│   │   ├── Services/
│   │   │   ├── Implementation/
│   │   │   │   ├── ActionExecutionService.cs       # EXISTS — route presentation actions through lifecycle
│   │   │   │   ├── PresentationLifecycleService.cs # NEW — orchestrates the 3 events
│   │   │   │   └── AbandonmentSweeper.cs            # NEW — background hosted service
│   │   │   └── Interfaces/
│   │   │       ├── IPresentationLifecycleService.cs # NEW
│   │   │       └── ITransactionBuilderService.cs    # EXISTS — add BuildPresentation*Async methods
│   │   ├── Endpoints/
│   │   │   └── PresentationEndpoints.cs             # NEW — verifier callback entry, polling GET
│   │   ├── Configuration/
│   │   │   └── PresentationLifecycleOptions.cs      # NEW — deployment knobs
│   │   └── Program.cs                               # EXISTS — register new services, background task
│   └── Sorcha.Haip.Service/
│       ├── Services/
│       │   └── PresentationCallbackRelay.cs         # NEW — sends verifier outcome to Blueprint Service
│       └── Endpoints/
│           └── VerifierEndpoints.cs                 # EXISTS — callback now relays, doesn't complete action directly

tests/
├── Sorcha.Blueprint.Service.Tests/
│   ├── Services/
│   │   ├── PresentationLifecycleServiceTests.cs    # unit — state transitions, idempotency
│   │   └── AbandonmentSweeperTests.cs              # unit — timer/clock-injected
│   ├── Integration/
│   │   ├── PresentationLifecycleIntegrationTests.cs # full 18-scenario matrix
│   │   └── PresentationRateLimitIntegrationTests.cs
│   └── Endpoints/
│       └── PresentationEndpointsTests.cs
└── Sorcha.Haip.Service.Tests/
    └── PresentationCallbackRelayTests.cs

walkthroughs/
└── AssuredIdentity/
    └── run.ps1                                      # EXISTS — verify lifecycle flow works end-to-end

docs/
└── reference/
    └── presentation-lifecycle.md                    # NEW — developer + auditor guide
```

**Structure Decision**: Extend the existing Blueprint Service (owns orchestration, routing, transaction building) and HAIP Service (owns the OpenID4VP verifier) rather than introducing a new service — keeps the service surface small and aligns with the Constitution's microservices-first-but-don't-multiply-services principle. A new lightweight `Sorcha.PresentationLifecycle.Abstractions` package carries the consumer-agnostic primitive so that future non-HAIP consumers (file-upload-by-deadline, step-up MFA, etc.) can depend on it without pulling in Blueprint Service internals.

## Complexity Tracking

> No Constitution violations — table intentionally empty.

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| — | — | — |
