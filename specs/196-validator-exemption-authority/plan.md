# Implementation Plan: Validator Exemption Authority

**Branch**: `196-validator-exemption-authority` | **Date**: 2026-08-28 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/196-validator-exemption-authority/spec.md`

## Summary

The validator waives six rules for administrative transactions on the basis of two fields the
submitter sets freely and no signature covers. This feature replaces the **claimed** discriminator
with a **proved** one: an exemption is granted only where the signer is demonstrably entitled to it.
Nothing about what an exemption waives changes — two of the six are load-bearing for governance
quorum.

The approach is deliberately *not* to move the discriminator into signed content. That route is
unavailable for two of the three values: a blueprint publication's signed payload **is** the
canonical definition (changing it moves every publication id on every register), and genesis's is a
pre-signed ceremony artefact (changing it forces a re-genesis). Authority is derivable from material
that is already signed — the signer's own key — so no ledger byte moves and no ceremony changes.

## Technical Context

**Language/Version**: C# 14 / .NET 10

**Primary Dependencies**: `Sorcha.Register.Core` (roster + repository seams, already referenced by
the Validator Service), `Sorcha.Cryptography` (`ICryptoModule`, fingerprint computation),
`Sorcha.Register.Models` (genesis constants, control record, validator roster)

**Storage**: No schema change. Read-only use of existing register/control-chain storage. **No EF
migration** — nothing persisted changes shape.

**Testing**: xUnit v3 4.x under Microsoft.Testing.Platform (per `global.json`), FluentAssertions 8.x,
Moq 4.20.x. Real hashing only — see the constraint in research R7.

**Target Platform**: Linux containers; Validator, Register and Peer services

**Project Type**: Backend services within the existing solution

**Performance Goals**: No measurable regression on the per-transaction validation path. Authority
resolution is per-register and cached against the register's last control transaction.

**Constraints**: No re-genesis *required* to adopt (FR-009). No change to canonical blueprint bytes
(FR-010). A wiped and re-genesised network must reach full function (FR-011). Sealed-docket
verification path untouched (FR-012).

**Revised 2026-08-28**: historical validity of already-sealed transactions is **no longer a
constraint** — the platform is pre-production and the estate may be wiped. This removes the largest
risk in the feature and unblocks US2, which was gated on what existing registers happened to contain.

**Scale/Scope**: Three services touched; one interface relocated into a shared library; four grant
routes closed. Administrative transactions are a small fraction of traffic — the hot path is
unaffected except by the field-agreement check (FR-006), which is a string comparison.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Assessment |
|---|---|
| **I. Microservices-First** — dependencies flow downward only | **PASS.** `INodeTrustAnchor` moves *down* into `Sorcha.Register.Core`, which the Validator Service already depends on. No new service-to-service edge; explicitly rejected calling the Register Service over HTTP for an authority decision (research R1). |
| **II. Security First** — zero trust, validation at external boundaries | **PASS — this feature is an instance of it.** It removes a trust-the-submitter decision from an external boundary. |
| **III. API Documentation** | **N/A.** No new or changed public API surface. FR-013 is a log/metric obligation, not an endpoint — serving refusal reasons is explicitly out of scope. |
| **IV. Testing** — >85% for new code, deterministic, isolated | **PASS with an added bar.** Beyond coverage, every guard must fail when its own check is removed (SC-002), and the hashing layer must not be stubbed. |
| **V. Code Quality** — nullable enabled, no warnings | **PASS.** |
| **VI. Blueprint Standards** | **N/A.** |
| **VII. Domain-Driven Design** — ubiquitous language | **PASS.** New vocabulary is *exemption*, *claim*, *authority*, *entitlement* — introduced deliberately and consistently, because the absence of a word for "entitled to an exemption" is part of why the defect was invisible. |
| **VIII. Observability by Default** — structured logging, metrics | **PASS.** FR-013 requires an attempted bypass be distinguishable; delivered as a distinct structured event plus a counter on the existing validator meter, following the `HasUncorroboratedLifecycleMetadata` precedent. |

**Post-Phase-1 re-check**: unchanged. No violation requires justification; the Complexity Tracking
table is omitted as it would be empty.

## Project Structure

### Documentation (this feature)

```text
specs/196-validator-exemption-authority/
├── plan.md              # This file
├── spec.md              # Feature specification
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/
│   └── authority-resolution.md   # Internal contract (no external API)
├── checklists/
│   └── requirements.md
└── tasks.md             # Phase 2 output (/speckit.tasks)
```

### Source Code (repository root)

```text
src/
├── Common/
│   └── Sorcha.Register.Models/
│       └── Genesis/                        # fingerprint helpers (read-only use)
├── Core/
│   └── Sorcha.Register.Core/
│       └── Provenance/
│           └── INodeTrustAnchor.cs         # RELOCATED here from Register.Service
└── Services/
    ├── Sorcha.Register.Service/
    │   └── Provenance/
    │       └── NodeTrustAnchor.cs          # implementation stays; namespace updated
    └── Sorcha.Validator.Service/
        └── Services/
            ├── TransactionTypeClassifier.cs      # grant routes → authority-derived
            ├── ExemptionAuthorityResolver.cs     # NEW — the single grant decision
            ├── ValidationEngine.cs               # consume resolved decision; field agreement
            └── RightsEnforcementService.cs       # couple governance grant to roster outcome

tests/
├── Sorcha.Validator.Service.Tests/
│   ├── ExemptionAuthorityTests.cs          # NEW — per-route refusal + counterfactuals
│   └── ValidationEngineChainBindingTests.cs # existing fixture the probe used
└── Sorcha.Register.Service.Tests/          # anchor relocation regression
```

**Structure Decision**: The change is concentrated in the Validator Service, with one interface
relocated into `Sorcha.Register.Core` and no change to the Peer Service beyond it being the reachable
surface (whose authentication is explicitly out of scope). A new `ExemptionAuthorityResolver` gives
the grant decision **one home**, mirroring the pattern the codebase already applies to derivation
contexts, validation codes, service addresses and publication ids: a value that must be consistent
gets exactly one producer.

## Phase Ordering and Risk

Ordered so the highest-severity, best-evidenced routes close first and the riskiest work is not
blocking:

1. **Genesis (US1)** — highest severity, cheapest proof. The valid genesis transaction id is a
   compile-time constant and the anchor already exists; only its relocation is new. Closes both
   genesis routes together, since closing one alone closes nothing.
2. **Field agreement (US4)** — independent of authority resolution, small, and closes the general
   form of the class.
3. **Governance coupling (US3)** — no behaviour change, so it is the safest to land and reduces the
   surface before the riskiest item.
4. **Blueprint publication (US2)** — **last, and gated on the R2 decision.** It is the only part that
   can lock out legitimate traffic if the authority source is chosen wrongly, and the authority
   source is not yet settled.

## Risks

| Risk | Consequence | Mitigation |
|---|---|---|
| ~~R2 unresolved~~ **RESOLVED** — validator roster on the existing register-control context | — | Decided 2026-08-28. US2 unblocked. |
| **Fail-closed converts an outage into refused administrative traffic** | A node that cannot resolve authority stops accepting publishes/governance | Deliberate (FR-007), documented in Assumptions, flagged for confirmation. Distinct log + metric so the cause is diagnosable rather than silent. |
| ~~An unknown path re-validates sealed transactions~~ **NO LONGER A RISK** | — | Wipe-and-re-genesis is permitted, so historical validity is not owed. T004 is retained as informational only. |
| **A guard passes vacuously** | The feature ships without closing anything — the exact failure mode of the tests that hid #1587 | SC-002 mutation requirement; counterfactual in every guard; no stubbed hashing. |
| **Live network divergence** | A node validating differently from its peers partitions the register | Live verification on both n1 and tiny is part of completion, including a replica pull. |
