# Implementation Plan: Presentation Lifecycle Chain-Race Resolution via Seal-Aware Ordering

**Branch**: `119-presentation-seal-ordering` | **Date**: 2026-05-08 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/119-presentation-seal-ordering/spec.md`
**Design document**: `docs/superpowers/specs/2026-05-08-feature-111-chain-races-design.md`

## Summary

Close the two pre-existing chain-integrity races in Feature 111 (Timebound Presentation Lifecycle) that cause AssuredIdentity Phase 2 to fail roughly 50% of the time when the citizen presents quickly. Approach: introduce a single-purpose **seal-aware ordering** mechanism in Blueprint Service that holds chain-pointer-bearing transaction submissions, and workflow advancement triggers, until the predecessor they reference has been observed sealed via the existing `transaction:confirmed` Redis Streams channel. Three call sites (`HandleOutcomeAsync`, `HandleAbandonmentAsync`, FR-015 advancement) stop short-circuiting on `built.TxId`; one new `IPresentationSealCoordinator` plus `PresentationSealSubscriber : BackgroundService` plus a 5-second recovery sweep cover the new behaviour. Validator chain rules are unchanged.

## Technical Context

**Language/Version**: .NET 10 / C# 14 (per constitution).
**Primary Dependencies**: existing `IDistributedCache` (Redis), existing `IEventSubscriber` (Redis Streams from `Sorcha.Register.Core.Events`), existing `IPendingPresentationStore`, `IRegisterServiceClient`, `IValidatorServiceClient`, `ITransactionBuilderService`, `IWalletServiceClient`, `IActionExecutionService` (PR #583's `CompleteAfterPresentationAsync`). No new third-party packages.
**Storage**: Redis only — two new transient hashes (`sorcha:presentation:awaiting-seal:submit:{predecessorTxId}`, `sorcha:presentation:awaiting-seal:advance:{outcomeTxId}`). No PostgreSQL or MongoDB schema changes. No EF migration.
**Testing**: xUnit + FluentAssertions + Moq for unit tests; existing in-memory Redis test double (`Sorcha.Storage.InMemory.Redis`) for storage; integration via Blueprint Service's existing `WebApplicationFactory` test rig (Tests project: `tests/Sorcha.Blueprint.Service.Tests/`); walkthrough validation via `walkthroughs/AssuredIdentity/run.ps1 -Profile gateway`.
**Target Platform**: Server-side .NET 10 in Linux container under Docker Compose locally; same container under Azure Container Apps on n1; cross-platform Windows dev for Visual Studio.
**Project Type**: Single — modification to Blueprint Service only. No frontend or mobile component.
**Performance Goals**: P95 callback-to-next-action-ready latency ≤30 s for fast-citizen path (one-to-two docket-build cycles); ≤5 s recovery latency on a missed seal event (one sweeper tick); zero verifier-callback HTTP latency increase.
**Constraints**: MUST NOT block the verifier-facing HTTP callback waiting for predecessor seal (FR-119-013); MUST be idempotent under retries and Blueprint Service restart (FR-119-010, FR-119-011); MUST preserve VAL_CHAIN_FORK protection (FR-119-009); MUST stay scoped to presentation lifecycle (no general-purpose submit-after-seal primitive — see spec Out-of-scope).
**Scale/Scope**: Three call-site modifications + one new singleton service + one new background service + sentinel state machine extension. Estimated ~600 lines new code, ~500 lines new tests. Single-PR deliverable off `master`.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Compliance | Notes |
|---|---|---|
| I. Microservices-First | ✅ | Fix is fully internal to Blueprint Service. No new cross-service contracts. The `transaction:confirmed` channel is already consumed by three other services; Blueprint joins the consumers. No upward dependencies introduced. |
| II. Security First | ✅ | No new sensitive data. Queue payloads carry already-signed transaction submissions (signed before queueing). Redis is already inside the trust boundary. No new secrets, no new external boundaries. Input validation unchanged. |
| III. API Documentation | ✅ | No new public REST APIs. New internal interfaces (`IPresentationSealCoordinator`) get XML doc comments per coding standard. |
| IV. Testing Requirements | ✅ | Plan covers unit (each branch), integration (full Redis Streams loop), and end-to-end (walkthrough). Coverage target ≥85% for new code. xUnit, AAA pattern, deterministic via injected `IClock` and in-memory Redis double. |
| V. Code Quality | ✅ | async/await throughout. Constructor DI. Nullable enabled per project default. No compiler warnings target. |
| VI. Blueprint Creation Standards | N/A | No blueprint schema changes. |
| VII. Domain-Driven Design | ✅ | Uses existing vocabulary: presentation, lifecycle, outcome, abandonment, sentinel. New names (`SealCoordinator`, `seal-wait`) are consistent with the existing presentation lifecycle domain. |
| VIII. Observability by Default | ✅ | Explicit FR-119-008 in spec. New OTel meter `Sorcha.Blueprint.PresentationLifecycle` already exists; new metrics + spans + structured logs added per design doc. Health check `/health` impact: none (no new external dependency). |

**Result**: PASS — no violations, no justification required.

## Project Structure

### Documentation (this feature)

```text
specs/119-presentation-seal-ordering/
├── spec.md                          # Feature spec (already written)
├── plan.md                          # This file
├── research.md                      # Phase 0 output (this command)
├── data-model.md                    # Phase 1 output (this command)
├── quickstart.md                    # Phase 1 output (this command)
├── contracts/
│   └── presentation-seal-coordinator.cs.md   # Phase 1 internal interface contract
├── checklists/
│   └── requirements.md              # Spec-quality checklist (already written)
└── tasks.md                         # Phase 2 output (/speckit.tasks command)
```

### Source code (repository root) — files touched

```text
src/Services/Sorcha.Blueprint.Service/
├── Services/
│   ├── Interfaces/
│   │   └── IPresentationSealCoordinator.cs               # NEW
│   └── Implementation/
│       ├── PresentationLifecycleService.cs                # MODIFY: HandleOutcomeAsync, HandleAbandonmentAsync, FR-015 advancement
│       ├── RedisPresentationSealCoordinator.cs            # NEW
│       ├── PresentationSealSubscriber.cs                  # NEW (BackgroundService)
│       └── PresentationLifecycleMetrics.cs                # MODIFY: add seal-wait metrics
├── Storage/
│   └── Presentations/
│       └── IPendingPresentationStore.cs                   # MODIFY: extend sentinel state machine helpers
└── Program.cs                                             # MODIFY: register IPresentationSealCoordinator + PresentationSealSubscriber

tests/Sorcha.Blueprint.Service.Tests/
└── Services/
    ├── PresentationSealCoordinatorTests.cs                # NEW (unit)
    ├── PresentationSealSubscriberTests.cs                 # NEW (unit)
    ├── PresentationLifecycleServiceOutcomeTests.cs        # MODIFY: add predecessor-sealed/pending branches
    ├── PresentationLifecycleServiceAbandonmentTests.cs    # MODIFY: same shape
    └── PresentationSealCoordinatorIntegrationTests.cs     # NEW (integration)

.claude/skills/sorcha-architecture/SKILL.md                # MODIFY: Cross-Cutting Pattern: Timebound Presentation Lifecycle section
specs/111-presentation-lifecycle/research.md               # MODIFY: add R10 Seal-aware ordering
specs/111-presentation-lifecycle/data-model.md             # MODIFY: §1.4 chain linkage clarification
walkthroughs/AssuredIdentity/run.ps1                       # NO CHANGE — Phase 2 must succeed unmodified
```

**Structure Decision**: Single-project modification within an existing microservice. All new code lives under `src/Services/Sorcha.Blueprint.Service/`. No new projects, no new test projects. Documentation propagation goes to two existing locations (`sorcha-architecture` skill, `specs/111-presentation-lifecycle/`).

## Complexity Tracking

> Constitution Check passed without violations. Section intentionally empty.

No deviations from constitution principles. The feature is a localised bug-fix with a clearly bounded surface.
