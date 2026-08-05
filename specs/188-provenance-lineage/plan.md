# Implementation Plan: Provenance — trust-anchor and proof lineage

**Branch**: `188-provenance-lineage` | **Date**: 2026-08-05 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/188-provenance-lineage/spec.md`

## Summary

Two read-only views that let an administrator verify who signed off on what, walking evidence from a fact back to the trust anchor. Phase 1 delivers the verification engine plus register lineage: a docket spine from genesis with per-docket verification on demand. The evidence already exists — Feature 187 (merged `bed2f044`) made proposer, sealed Merkle root and consensus votes persistent. This feature reads and reports; it introduces no new evidence.

The load-bearing choice is a **dependency-free verification engine** separate from the service that feeds it. Everything else follows: it is the Phase-3 export path, it is testable against hand-built tampered evidence with no infrastructure, and it matches the `Sorcha.Verifier.Engine` / `Sorcha.Blueprint.Engine` / `Sorcha.Mdoc` precedent.

## Technical Context

**Language/Version**: C# 14 / .NET 10
**Primary Dependencies**: none new. The engine is deliberately dependency-free; the service side uses existing Register Service infrastructure.
**Storage**: read-only over existing MongoDB register storage (`DocketHeader`, `TransactionModel`) plus the published control record / validator roster.
**Testing**: xUnit v3 + FluentAssertions, adversarial fixtures, mutation-tested guards.
**Target Platform**: Register Service (Linux container) + Blazor WASM admin UI.
**Project Type**: web (backend service + frontend components).
**Performance Goals**: register history usable at ≥5,000 dockets (SC-007); verification cost paid per selected docket, never per listed docket.
**Constraints**: the engine must not reference `Sorcha.Cryptography` (libsodium P/Invoke, not WASM-loadable) nor `Sorcha.ServiceClients.Http`, or the Phase-3 export path is foreclosed.
**Scale/Scope**: Phase 1 = 5 checks over register history. Phases 2 and 3 named but not designed here.

## Constitution Check

| Principle | Assessment |
|---|---|
| I. Microservices-First | PASS — no new service. Endpoints live in Register Service, which already owns the evidence. Adding a service to read data another service owns would be worse. |
| II. Security First | PASS — read-only; `RequireAdministrator` composed with `RequirePlatformAudience` (pattern #13). No auth widening: the external-auditor path is Phase 3's export, not a loosened policy. |
| III. API Documentation | PASS — endpoints carry `.WithSummary()`/`.WithDescription()`; contract in `contracts/`. |
| IV. Testing (>85%) | PASS — the engine is pure and fully unit-testable; every check is mutation-tested. |
| V. Code Quality | PASS — follows existing engine/service/UI layering. |
| VI. Blueprint Standards | N/A — no blueprint surface. |
| VII. Domain-Driven Design | PASS — "provenance" is named as its own concept precisely because it is *not* the existing audit-logging concern. |
| VIII. Observability | PASS — check outcomes counted by layer and status (see Observability). |

**No violations to justify.** One judgement recorded in Complexity Tracking: hoisting a shared status enum.

## Key Design Decisions

### D1 — The engine is dependency-free, and that constrains two things

`Sorcha.Provenance.Engine` (new, `src/Common/`) takes assembled evidence and returns a verdict trail. No HTTP, no Mongo, no DI.

Two consequences found while planning, both of which would otherwise have surfaced late:

- **It cannot reference `Sorcha.Cryptography`.** That assembly P/Invokes libsodium and cannot load under browser-wasm — the documented reason `Sorcha.Mdoc` was extracted from it (F185). Phase 3 is an offline/portable auditor, so taking that dependency would foreclose the thing the engine exists for.
- **It cannot reference `Sorcha.Verifier.Engine`** to reuse `LayerStatus`, because that project depends on `Sorcha.ServiceClients.Http`, `Sorcha.Cryptography.Secp256k1` and BouncyCastle.

**Cost recorded honestly**: the engine cannot reach storage, so the service must assemble evidence objects — a data-shaping layer pure service code would not need.

### D2 — Hoist the tri-state status; keep each domain's layer enum local

`LayerStatus` (`Verified` / `Failed` / `Unverified`) is generic. `ValidationLayer` (LivePresentation / IssuerSignature / Revocation / RegisterAnchor) is credential-specific and stays where it is.

Declaring a second near-identical status enum is precisely the drift Feature 187 spent its length removing — `VoteDecision` existed twice with **incompatible values** (`Reject` = 2 versus 0), in two assemblies that referenced each other, told apart only by namespace qualification. That defect was silent.

**Decision**: hoist the tri-state into a zero-dependency leaf and have both engines reference it. `Sorcha.Provenance.Engine` declares its own `ProvenanceLayer` (Anchor / Chain / Seal / Signers / Proposer).

**Alternative rejected**: a parallel `ProvenanceStatus` enum. Cheaper today; it is the `VoteDecision` mistake with a different name, and the two would drift the first time either gained a member.

⚠ **This touches F155's verifier.** It is a namespace move of one enum, compiler-guided, with no behaviour change — but it is a real edit to a shipped feature and must be a **separate, self-contained commit** so a bisect can isolate it.

### D3 — Merkle recomputation is an injected seam, not a copy

`MerkleTree.ComputeMerkleRoot` already exists in `Sorcha.Cryptography.Utilities` and is used by five call sites (Register Service proof generation, `DocketBuilder`, `DocketConfirmer`, `GenesisManager`). The engine cannot reference it (D1), and reimplementing it would recreate the duplicate-projection defect Feature 187 existed to fix.

**Decision**: the engine declares `IMerkleRootCalculator`; the service implements it over the existing `MerkleTree`. One algorithm, one implementation, injected across the boundary — the pattern F135 already uses for `IRevocationChecker` and `IIssuerKeyResolver`.

### D4 — Relationship to Feature 187 US3 (#1372)

F187 US3 adds a sealed-vs-recomputed Merkle cross-check "where integrity is asserted". This feature's **Seal** check is that comparison, for the docket surface.

**Decision**: one computation, two surfaces. F188 delivers the comparison as an engine check consuming the D3 seam; **#1372's remaining scope narrows to the proof-generation and chain-integrity endpoints**, which should call the same seam rather than growing their own. Update #1372 to say so rather than letting both features implement it.

Verified during the F187 n1 deploy: the inclusion-proof endpoint already returns a `merkleRoot` byte-identical to the persisted `DocketHeader.MerkleRoot`, so this check asserts agreement today rather than starting red.

### D5 — Roster-as-of is the design's centre of gravity

FR-010 requires each signature be checked against the validator set **as it stood at that docket**, not as it stands now. This is the single most likely thing to get silently wrong, and it grows *more* likely as the network grows — the stated direction of travel.

**Decision**: evidence assembly resolves a roster *version* per docket by walking control transactions up to that docket's height, and the engine receives `RosterAsOf` alongside the docket. The engine never sees "the current roster" — it is not given the option of being wrong.

**Alternative rejected**: pass the current roster plus a change-log and let the engine interpret it. More flexible, and it puts the trap inside the code most likely to be copied.

### D6 — Verification is on-demand, and the API shape enforces it

The spine endpoint returns docket summaries and runs **no checks**; the trail endpoint verifies one docket. This is not an optimisation to add later — a spine that verified eagerly would be O(n·m) hashing on a list view, and SC-007 requires 5,000 dockets to stay usable.

Separate endpoints mean the expensive path cannot be entered by accident.

## Project Structure

### Documentation (this feature)

```
specs/188-provenance-lineage/
├── spec.md
├── plan.md              # this file
├── research.md          # decisions D1-D6 with alternatives
├── data-model.md        # engine types + evidence shapes
├── contracts/
│   └── provenance-api.yaml
├── quickstart.md
└── checklists/
    └── requirements.md
```

### Source Code (repository root)

```
src/Common/Sorcha.Provenance.Engine/          # NEW — dependency-free
├── ProvenanceCheck.cs                        # Layer, Status, Headline, Detail, CheckedAgainst
├── ProvenanceLayer.cs                        # Anchor | Chain | Seal | Signers | Proposer
├── DocketProvenanceVerifier.cs               # the five checks
├── Evidence/                                 # DocketEvidence, RosterAsOf, AnchorEvidence
└── Seams/IMerkleRootCalculator.cs

src/Common/Sorcha.Verification.Abstractions/  # NEW leaf — hoisted tri-state (D2)
└── VerificationStatus.cs

src/Services/Sorcha.Register.Service/
├── Endpoints/ProvenanceEndpoints.cs          # thin: assemble, delegate, return
└── Provenance/                               # evidence assembly + IMerkleRootCalculator impl

src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Provenance/
├── RegisterLineage.razor                     # the docket spine
├── DocketProvenanceTrail.razor               # layered evidence trail
└── ProvenanceCheckRow.razor

tests/Sorcha.Provenance.Engine.Tests/         # NEW — adversarial fixtures, no infrastructure
tests/Sorcha.Register.Service.Tests/          # endpoint + evidence-assembly tests
```

**UI placement**: `Sorcha.UI.Core`, **not** `Sorcha.UI.Components.User` — admin/explorer-facing, must not reach the wallet PWA bundle (F123 audience convention).

**UI shape**: a docket spine, with roster changes rendered as events *on* the spine so network enlargement is visible as the signer set grows; clicking a docket opens a layered trail reusing the stacked-expandable idiom of F155's `VerdictTrailPanel`. Do not invent a second idiom for the same job.

## API Surface

| Method | Route | Verifies? | Notes |
|---|---|---|---|
| GET | `/api/provenance/registers/{registerId}` | No | Paged spine: docket number, proposer, signer count, roster-change marker |
| GET | `/api/provenance/registers/{registerId}/dockets/{number}` | Yes | Full trail for one docket |
| GET | `/api/provenance/instances/{instanceId}` | Yes | **Phase 2** — not implemented in this phase |

Authorization: `RequireAdministrator` + `RequirePlatformAudience`.

**Error handling**: an endpoint that cannot assemble evidence returns **200 with a trail whose affected rows are `Unverified` and carry a reason** — never a 500. An auditor needs to know *which* link could not be established; a 500 tells them nothing. Genuine faults (malformed route, unknown register) keep their normal status codes.

## Testing Strategy

Adversarial by construction. Every guard is mutation-tested — a guard never shown to fail is not a guard, and that discipline caught three real defects during Feature 187.

| Test | Asserts | Catches |
|---|---|---|
| Tamper a transaction id | Seal → `Failed` | A Seal check that does not compare |
| Roster-as-of, both directions | docket-10 signature from a validator removed at 12 → `Verified`; a docket-14 signature from that key → `Failed` | **The naive verify-against-current-roster implementation** |
| Empty vote set | Signers → `Unverified`, never `Verified` | The feature lying on single-validator deployments |
| Absent predecessor | Chain → `Unverified`, not `Failed` | A partial replica reading as compromised |
| Pre-F187 docket | Seal → `Unverified` with reason | Treating "no stored root" as tampering |
| Reflection over `ProvenanceLayer` | Every layer is exercised by at least one test | A layer silently never checked |

The roster-as-of pair is the highest-value test here: it is the only one that fails against an implementation that looks entirely correct.

## Observability

`Sorcha.Provenance` meter:

- `sorcha_provenance_check_total{layer,status}` — counter. Rising `status=failed` is an integrity signal worth alerting on; rising `unverified` usually means missing evidence rather than tampering, and the two must stay distinguishable.
- `sorcha_provenance_trail_duration_seconds{surface}` — histogram, guarding SC-007.

No subject data on any dimension.

## Phasing

| Phase | Scope | Depends on |
|---|---|---|
| **1 (this plan)** | Engine + register lineage: spine, per-docket trail, 5 checks | F187 evidence (merged) |
| 2 | Application lineage: instance narrative, 5 authority checks, cross-links | Phase 1 engine; F145/F184 attestation |
| 3 | Portable export bundle for external auditors | Phase 1 engine being genuinely dependency-free |

Phase 3 is why D1 is non-negotiable: if the engine acquires a service dependency, the export becomes a rewrite.

## Complexity Tracking

| Decision | Why it is worth it | Simpler alternative rejected because |
|---|---|---|
| New `Sorcha.Verification.Abstractions` leaf for one enum (D2) | A near-duplicate status enum across two engines is the exact `VoteDecision` defect — two declarations with incompatible values, silent, in mutually-referencing assemblies | Declaring `ProvenanceStatus` locally is cheaper today and drifts the first time either gains a member |
| Engine/service split with an evidence-assembly layer (D1) | It is the Phase-3 export path, and it makes verification testable without infrastructure | Service-embedded verification is faster to build and makes Phase 3 a rewrite |
| Injected `IMerkleRootCalculator` (D3) | Keeps one Merkle implementation while keeping the engine WASM-capable | Referencing `Sorcha.Cryptography` forecloses Phase 3; reimplementing recreates F187's duplicate-projection defect |

## Open Items

- **#1372 scope needs narrowing** per D4, so F187 US3 and F188's Seal check do not both implement the comparison.
- **Roster-as-of resolution cost** is unmeasured. Walking control transactions per docket may need a per-register cache; deferred until the spine is measured against SC-007's 5,000 dockets rather than optimised speculatively.
