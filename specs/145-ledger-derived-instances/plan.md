# Implementation Plan: Ledger-Derived Workflow Instances

**Branch**: `145-ledger-derived-instances` | **Date**: 2026-05-31 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/145-ledger-derived-instances/spec.md`
**Design**: `docs/superpowers/specs/2026-05-31-ledger-derived-instances-design.md`

## Summary

Make a workflow instance a **deterministic projection of the sealed register**: every node holding the register folds the same sealed action transactions into the same instance state. Replace the imperative per-node instance mutation + the reconstructed cross-node mirror + the dual synchronous/async submission split with: (1) routing decisions **carried on the transaction in the clear and validated at seal** under a pluggable attestation, (2) a **single `InstanceProjector`** that advances state on every node from `docket:confirmed`, (3) a **single async submission path** with roster-derived sealer selection, and (4) **idempotent role-gated reactions** for side effects (credential mint/deliver, notifications). Clean break, CI-gated. Anchored by three code investigations (file:line cited in Technical Context).

## Technical Context

**Language/Version**: C# 14 / .NET 10 (existing services).
**Primary Dependencies**: Blueprint Engine (`Sorcha.Blueprint.Engine`); the Validator (`ValidationEngine`, `DocketBuildTriggerService`); Register Service (docket write + `docket:confirmed` Redis Streams event, `RegisterEventChannels.DocketConfirmed`); Peer Service (`TransactionDistributionService` fan-out, F143 relay); `IRegisterLocalRelationshipService` (F108 roster relationship); `IEventSubscriber`/`RedisStreamEventPublisher`; EF Core instance store; `Sorcha.AtomicCache` (idempotency).
**Storage**: ledger = MongoDB dockets/transactions (canonical); instance materialized view = Postgres via `IInstanceStore`/`EfCoreInstanceStore`; Redis = event streams (`docket:confirmed`) + reaction idempotency (SET-NX) + bounded-wait signalling.
**Testing**: xUnit + FluentAssertions + Moq (Constitution IV); plus deterministic projection tests, rebuild-parity tests, reaction-idempotency tests, and the cross-node E2E (the standing two-node Assured Identity demo, `demos/AssuredIdentity/`).
**Target Platform**: Linux containers; federated installations (1 owner + N subscribers).
**Project Type**: distributed microservices (existing) — changes span Register.Models, Blueprint Engine, Blueprint Service, Validator Service, Peer Service.
**Performance Goals**: projection advances within ~1–2s of observing a sealed docket; bounded-wait convenience default ~2–3s; reaction dispatch idempotent under replay/restart.
**Constraints**: deterministic, idempotent, order-independent fold; routing control state derivable **without** payload decryption; pre-release clean break (no instance migration); CI gate forbids the removed constructs.
**Scale/Scope**: cross-installation; the projection + reactions run on every node holding the register.

### Grounded current-state anchors (from the investigations)

| Concern | Current code | Change |
|---|---|---|
| Imperative state mutation | `ActionExecutionService.ApplyInstanceStateChanges` (`:1762-1834`) | **Remove**; state advances only via projection |
| Mirror reconstruction | `InstanceMirrorReconstructor` (subscribes `docket:confirmed`, `:50-67,288-322`); `IsReadOnlyMirror`; `Create/UpdateMirrorAsync` | **Replace** with the single `InstanceProjector` (runs on every node, not just owner) |
| Singular routing hint | `MetaData.NextActionId` set at `ActionExecutionService.cs:984-993`, projected at `DocketBuildTriggerService.cs:636-642` | **Replace** with full `RoutingDecision` (carried + attested) |
| Local GUID identity | `Program.cs:2163-2185` (`Guid.NewGuid()`, local store only) | Derive id from starting-action tx; creation = local draft |
| Dual submit branch | `ActionExecutionService.cs:1060-1075` (`!LocallyOwned && AcceptedCount>0` → 202; else sync wait) | **One path**, always async-ack (+ bounded wait) |
| Ownership heuristic | `TransactionDistributionService.ForwardSubmissionAsync` (seeds-configured topology, `:62-218`) | Use `IRegisterLocalRelationshipService` roster relationship |
| Pending discovery | `EfCoreInstanceStore.GetPendingActionsByWalletAsync` (`:256-372`) reads the instance row | Works once the projection is the single writer + participant-id keyed bindings |
| Inline issuance | credential mint inside the sync submit path (`ActionExecutionService.cs:~510-685`) | **Move** to `ReactionDispatcher` (role-gated, idempotent) |
| Seal projection (validator) | `DocketBuildTriggerService.ResolveNextActionId` (`:531,636-642`) | Carry/validate the full `RoutingDecision`; validator adds `VAL_ROUTING_*` |
| Presentation ordering | `IPresentationSealCoordinator` (F119) | Subsumed by the seal-ordered projection; keep `VAL_BP_003` carve-out |

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Assessment |
|---|---|
| I. Microservices-First | **Honoured.** Each piece lands in its owning service: `RoutingDecision`/`Attestation` model in `Sorcha.Register.Models`; routing computation in `Sorcha.Blueprint.Engine`; validation in `Sorcha.Validator.Service`; `InstanceProjector` + `ReactionDispatcher` in `Sorcha.Blueprint.Service`; fan-out ownership in `Sorcha.Peer.Service`. No new upward dependencies; Core/Engine stay free of service refs (the Engine consumes seams, mirroring the existing `IRevocationChecker`/trust pattern). Cross-service span is inherent to the data model (a tx field validated by the validator, projected by Blueprint) — justified, not gratuitous. |
| II. Security First | **Honoured.** Routing decision is sender-signed (authenticity) + validator-validated (structural). No new secrets. Disclosure model unchanged — control-state projection needs no decryption; reactions only act where entitled. Governance attestation policy is additive. |
| III. API Documentation | **Required.** Changed/added endpoints (submission response contract, instance read, pending-actions) get `.WithSummary()/.WithDescription()` + XML + Scalar. No Swagger. |
| IV. Testing | **Strong fit.** Determinism, idempotency, and parity are naturally unit-testable; ≥85% on new core (Engine routing-decision, projector fold, reaction idempotency). Cross-node E2E via the standing demo. |
| V. Code Quality | **Honoured.** C# 14, nullable, async; no Release warnings. |
| VI. Blueprint Standards | **Honoured.** Blueprints stay JSON; the route graph stays in the blueprint. The `RoutingDecision` is a **runtime transaction artifact**, not a blueprint-format change. |
| VII. Domain-Driven Design | **Honoured.** Uses Register/Docket/Transaction/Action/Participant/Publish. New ubiquitous terms: **Instance Projection**, **Routing Decision**, **Attestation**, **Reaction** — defined in data-model.md. |
| VIII. Observability | **Required.** New OTel: projection lag, fold idempotency hits, `VAL_ROUTING_*` rejects, reaction dispatch/idempotency, bounded-wait outcomes. Health: projection-up-to-head. |

**Verdict**: PASS. The only notable item is the **cross-service breadth**, which is intrinsic to the architectural fix (one model change expressed in the tx contract + validator + projector + reactions). It is the *simplest* shape that delivers a single consistent state machine — the alternative (keeping per-node imperative state) is precisely the complexity being removed. No Complexity Tracking entries required.

## Project Structure

### Documentation (this feature)

```text
specs/145-ledger-derived-instances/
├── plan.md              # This file
├── research.md          # Phase 0 — resolved unknowns
├── data-model.md        # Phase 1 — entities, tx-carried RoutingDecision, projection state machine
├── quickstart.md        # Phase 1 — how to verify (determinism, parity, idempotency, cross-node E2E)
├── contracts/           # Phase 1
│   ├── routing-decision.md       # tx-carried RoutingDecision + Attestation shape + validation rules
│   ├── submission-response.md    # the unified async submission contract (202 + bounded-wait 200)
│   ├── instance-read.md          # instance state + pending-actions read contract
│   └── reactions.md              # reaction entitlement + idempotency contract
└── checklists/requirements.md    # spec quality checklist (passing)
```

### Source Code (repository root) — touched areas

```text
src/Common/Sorcha.Register.Models/
└── Transactions/            # RoutingDecision + Attestation carried on TransactionMetaData (clear);
                             #   retire NextActionId. Governance: RoutingAttestationPolicy on the control record.

src/Core/Sorcha.Blueprint.Engine/
└── Routing/                 # emit the FULL NextActions set + build the SenderSigned attestation
                             #   (extends the existing routing evaluation; engine-local, WASM-safe)

src/Services/Sorcha.Blueprint.Service/
├── Services/Implementation/
│   ├── InstanceProjector.cs        # NEW — subscribes docket:confirmed on EVERY node; pure fold → IInstanceStore
│   │                               #   (replaces InstanceMirrorReconstructor)
│   ├── ReactionDispatcher.cs       # NEW — role-gated idempotent side effects (credential mint/deliver, notify)
│   ├── ActionExecutionService.cs   # single async path; remove ApplyInstanceStateChanges + LocallyOwned branch + inline issuance
│   └── InstanceIdentity.cs         # NEW — deterministic id from starting-action tx
├── Storage/
│   └── EfCoreInstanceStore.cs      # materialized view + Rebuild(instanceId) + parity check; drop IsReadOnlyMirror/mirror writes
└── Program.cs                      # POST /instances = local draft; submission response contract; instance/pending reads

src/Services/Sorcha.Validator.Service/
├── Services/ValidationEngine.cs    # VAL_ROUTING_001/002 (structural successor + attestation); enforce register attestation policy
└── Services/DocketBuildTriggerService.cs # carry the validated RoutingDecision through the seal (replace ResolveNextActionId hint)

src/Services/Sorcha.Peer.Service/
└── Distribution/TransactionDistributionService.cs  # sealer selection via IRegisterLocalRelationshipService (retire topology heuristic)

src/Core/Sorcha.Register.Core/
└── Governance/                     # RoutingAttestationPolicy read/derive (sibling of the crypto policy, F083/F086)

scripts/check-ledger-derived-clean-break.ps1   # NEW CI gate (forbids mirror/dual-path/imperative/heuristic/singular-hint)

# Callers migrated (P7): demos/AssuredIdentity/ (toolkit + agent discovery), walkthroughs/*, Sorcha.UI / Wallet PWA submit surfaces.
```

**Structure Decision**: Existing distributed-microservices layout. The new behavioural core is two Blueprint-Service services (`InstanceProjector`, `ReactionDispatcher`) plus a carried+validated `RoutingDecision` on the transaction model and a roster-based sealer selection in Peer. Engine stays dependency-free (routing emission behind the existing seam pattern). The clean break deletes the mirror/dual-path/imperative constructs in the same change.

## Complexity Tracking

> No constitution violations. Cross-service breadth is intrinsic to the model change (justified inline above). Table intentionally empty.
