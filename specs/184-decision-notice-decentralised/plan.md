# Implementation Plan: Decentralised decision notice + reason codification

**Branch**: `fix/183-decision-notice-citizen-recipient` | **Date**: 2026-07-13 | **Spec**: [spec.md](./spec.md)

**Design (authoritative)**: `docs/superpowers/specs/2026-07-13-aias-decision-notice-decentralised-design.md`

## Summary

Move the F183 `x-decision-notice` reject notice off the inline `ActionExecutionService` hook (which runs
only on the deciding agent's node) and onto the entitlement-gated `ReactionDispatcher`, so it fires on the
**citizen's own node** as that node folds the inbound sealed transaction. Carry the taken route's id and a
non-sensitive reason **code** on the existing sender-signed `RoutingDecision` (they fall inside
`ComputeSignableBytes()`, so `VAL_ROUTING_002` verifies them for free), and resolve the code to
citizen-facing text from the **replicated blueprint**. Clean break: delete `DecisionNoticeDispatcher` and
`DecisionNotice.ReasonField` — no free-text reason ever reaches clear metadata.

## Technical Context

**Language/Version**: C# 14 / .NET 10

**Primary Dependencies**: `Sorcha.Register.Models` (RoutingDecision), `Sorcha.Blueprint.Models` (Route /
DecisionNotice), `Sorcha.Blueprint.Engine` (RoutingEngine), `Sorcha.Blueprint.Service`
(ActionExecutionService, InstanceProjector, ReactionDispatcher, BlueprintInboxWriter, NotificationService),
`Sorcha.AtomicCache` (`TrySetIfAbsentAsync` idempotency), `IWalletServiceClient` (entitlement probe)

**Storage**: No schema change. The notice lands in the existing Tenant `InboxEntries` table via the
existing `IPlatformInboxClient`. Idempotency claims use the existing `IAtomicDistributedCache` (Redis).

**Testing**: xUnit v3 + FluentAssertions 8.x + Moq 4.20.x. Unit-first (TDD). Live acceptance on n1 via
Chrome-DevTools + `demos/AIAS/rehearse.ps1`.

**Target Platform**: Linux containers (Docker Compose / n1)

**Project Type**: Distributed service platform (.NET Aspire)

**Performance Goals**: No new hot path. One extra blueprint read (already cached by
`IActionResolverService`) and one extra wallet probe per folded decision transaction that names a route
carrying a notice.

**Constraints**: The reaction runs in a background fold with **no delegation token** and MUST NOT decrypt
payload. Every failure path is skip-quiet — a notice failure must never disturb sealing, routing, or the
committed projection.

**Scale/Scope**: ~7 source files changed, 2 deleted, 2 demo artefacts updated. No new service, no new
endpoint, no migration.

## Constitution Check

*GATE: passes.*

| Principle | Assessment |
|---|---|
| Microservices boundaries | No new service, no new cross-service call shape. The notice reuses the existing Blueprint → Tenant inbox client. |
| Storage abstraction (#5 / #10) | No new storage interface; no `IStorageRegistrationLog` change. |
| Documentation policy | `sorcha-architecture` skill (F183 entry) and `blueprint-builder` skill (`x-decision-notice` section) updated in the same PR; XML docs on every touched public member. |
| Test coverage (>85%) | Every new branch is unit-covered; see Testing below. |
| Clean break, no shims (pre-release) | `DecisionNoticeDispatcher` and `DecisionNotice.ReasonField` are deleted outright, not deprecated. |
| DAD model | Reinforces it: the decision is delivered from the replicated ledger on the recipient's own node, not pushed from the issuer's node. |

**No violations. Complexity Tracking section omitted.**

## Project Structure

### Documentation (this feature)

```text
specs/184-decision-notice-decentralised/
├── spec.md
├── plan.md                  # this file
├── data-model.md            # the two carried fields + the annotation shape
├── contracts/
│   └── x-decision-notice-extension.md   # blueprint-author contract
├── quickstart.md            # how to author + verify a decision notice
├── checklists/requirements.md
└── tasks.md                 # /speckit.tasks output
```

No `research.md`: there are no unknowns. The two open decisions (carrier, codification) were settled with
the user during brainstorming and are recorded in the design doc.

### Source Code (repository root)

```text
src/Common/Sorcha.Register.Models/Transactions/
└── RoutingDecision.cs                     # + RouteId, + ReasonCode (and into ComputeSignableBytes)

src/Common/Sorcha.Blueprint.Models/
└── Route.cs                               # DecisionNotice: - ReasonField, + ReasonCodeField,
                                           #   + Reasons, + FallbackMessage

src/Core/Sorcha.Blueprint.Engine/
├── Models/RoutingResult.cs                # + MatchedRouteId (top-level)
└── Implementation/RoutingEngine.cs        # set it on every matched-route path incl. terminal

src/Services/Sorcha.Blueprint.Service/Services/Implementation/
├── ActionExecutionService.cs              # producer: stamp RouteId + ReasonCode; DELETE 9-notice hook
├── DecisionNoticeDispatcher.cs            # DELETED
├── ReactionDispatcher.cs                  # consumer: decision-notice reaction; takes TransactionModel
├── InstanceProjector.cs                   # pass the sealed tx it already holds
├── ReactionDispatcherMetrics.cs           # (no change — "decision-notice" is just a kind tag)
└── BlueprintInboxWriter.cs                # (no change — resolver already landed in dedb339c)

tests/Sorcha.Blueprint.Service.Tests/Services/
├── DecisionNoticeDispatcherTests.cs       # DELETED
├── ReactionDispatcherTests.cs             # + decision-notice cases
└── ActionExecutionServiceTests.cs (or new RoutingDecisionProducerTests.cs)

tests/Sorcha.Register.Models.Tests/  &  tests/Sorcha.Blueprint.Engine.Tests/
└── carrier + matched-route-id coverage

demos/AIAS/
├── blueprints/aias-assured-identity.template.json   # reasonCode schema property + codified notice
└── agent/assure-id.rules.json                       # emit reasonCode per reject rule
```

**Structure Decision**: No new projects. Every change lands in an existing assembly, on the existing
F145 projector → reaction seam.

## Phase 1 — the mechanism (in dependency order)

1. **Carrier** (`RoutingDecision`): add `RouteId` + `ReasonCode`, and **copy both into the object
   `ComputeSignableBytes()` constructs** — it rebuilds a field-by-field copy, so a field omitted there is
   silently unsigned. This is the single most important detail in the change.
2. **Annotation** (`DecisionNotice`): remove `ReasonField`; add `ReasonCodeField`, `Reasons`,
   `FallbackMessage`.
3. **Engine**: lift `MatchedRouteId` onto the top-level `RoutingResult` and set it on the conditional,
   default, **and terminal** paths (`BuildRoutingResult` currently returns `RoutingResult.Complete()` for
   an empty next-action set, discarding `route.Id` — that is precisely the reject route).
4. **Producer** (`ActionExecutionService`): carry `MatchedRouteId` through the service-local
   `RoutingResult`; at the step-10d decision build, set `RouteId`, and when the taken route declares a
   notice with a `ReasonCodeField`, resolve that pointer from the submitted payload into `ReasonCode`.
   Delete the inline 9-notice block and `DecisionNoticeDispatcher` (+ its tests).
5. **Consumer** (`ReactionDispatcher`): `DispatchAsync(Instance, TransactionModel, ct)`; new
   `IActionResolverService` dependency; decision-notice reaction runs **before** the terminal/active
   branching, gated by the existing `ShouldFireAsync(kind: "decision-notice")`.
6. **Projector**: pass the sealed `tx` it already holds at both `DispatchAsync` call sites.
7. **Demo artefacts + docs**: AIAS blueprint template, agent rules, `blueprint-builder` skill,
   `sorcha-architecture` skill F183 entry.

## Testing strategy

TDD throughout — each unit below is written red-first.

| Layer | Cases |
|---|---|
| `RoutingDecision` | Canonical round-trip carries both fields; both are **inside** `ComputeSignableBytes()` (a decision differing only in `ReasonCode` produces different signable bytes); absent fields deserialize null (pre-feature txs). |
| `RoutingEngine` | Terminal route yields `MatchedRouteId`; conditional route yields it; default route yields it; no matching route yields null. |
| Producer | Taken route id stamped; `ReasonCode` resolved from `ReasonCodeField`; route without a notice ⇒ null code; unresolvable pointer ⇒ null code. |
| `ReactionDispatcher` | Fires for the entitled recipient; **skips when the recipient's wallet is not local** (the cross-node case); idempotent on replay; unknown/absent code ⇒ `FallbackMessage`; no notice on the route ⇒ no-op; unknown route id ⇒ no-op; unbound recipient ⇒ no-op; fires on a **non-terminal** route. |
| `BlueprintInboxWriter` | Existing tests from `dedb339c` (participant-first, wallet-owner fallback) — unchanged, must stay green. |
| Live (n1) | Reject via the web path and via `demos/AIAS/rehearse.ps1`; Chrome-DevTools confirms a durable bell/inbox entry with the codified reason, surviving reload + re-login. Approve ⇒ no notice. |

## Risks

- **Unsigned-field trap** — omitting a new field from `ComputeSignableBytes()`'s copy leaves it
  unauthenticated while *looking* signed. Covered by a dedicated test (above).
- **Blueprint version drift** — the citizen's node resolves the route id against the blueprint it holds. A
  route renamed between publish and fold produces no notice (skip-quiet, logged). Acceptable: the blueprint
  is immutable per published version and the instance pins its version.
- **DevMode masking** — n1 is DevMode (plaintext payloads), so a mistakenly payload-reading implementation
  would still pass the live test. Mitigated by the unit tests, which give the dispatcher no payload access
  at all (it takes only the instance, the tx metadata, and the blueprint).
