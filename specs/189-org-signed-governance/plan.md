# Implementation Plan: Real register governance (Feature 189)

**Branch**: `189-org-signed-governance` | **Date**: 2026-08-06 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/189-org-signed-governance/spec.md`

## Summary

Governance is currently unenforceable *and* unusable at the same time: control transactions are
signed by the node rather than by an organisation on the register's roster (so every governance
operation is rejected once a register's genesis has sealed), while the one path that does reach the
ledger — `/propose` — evades the roster check altogether. Separately, the roster key comparison
cannot match even a correct key because the two sides use different base64 alphabets.

This plan fixes authority first (US1), then makes the platform's own governance blueprint actually
execute so multiple organisations can jointly authorise a change under the register's configured
rule (US2/US3), and finally exercises the whole model by transferring ownership of the system
register (US4).

The quorum arithmetic, roster reconstruction, approval rules including unanimity, and multi-owner
register creation **already exist and are reused**. What is built here is: correct signing, correct
enforcement, and the approval surface that was never written.

## Technical Context

**Language/Version**: C# 14 / .NET 10
**Primary Dependencies**: ASP.NET Core Minimal APIs, MongoDB (register/ledger), PostgreSQL + EF Core
(blueprint instances), Redis (validator mempool), Sorcha.Cryptography (ED25519)
**Storage**: Register ledger in MongoDB (`sorcha_register_{id}`: `transactions`, `dockets`);
blueprint instances in PostgreSQL
**Testing**: xUnit v3 + FluentAssertions 8.x + Moq 4.20.x; **plus mandatory live multi-node
verification on n1 + tiny (SC-009)**
**Target Platform**: Linux containers (n1 = seed/owner + sole validator; tiny = SyncOnly replica,
separate installation)
**Project Type**: Distributed microservices
**Performance Goals**: Not a performance feature. Governance changes are human-cadence; the binding
latency is docket seal time (seconds).
**Constraints**:
- Quorum evaluation MUST be deterministic from sealed ledger content on every node (F145 projection
  — see R-009). This forbids evaluating quorum from service-local state.
- The roster is immutable per register, so the signing-key change is a clean break (R-011).
- Governance transactions MUST carry `BlueprintId = register-governance-v1` — never empty (`TX_003`)
  and never `"genesis"` (misclassifies as the network genesis, R-005).
**Scale/Scope**: 4 user stories; touches Register Service, Validator Service, Blueprint Service,
CLI, and the walkthrough module. Existing registers become ungovernable by design.

## Constitution Check

| Principle | Assessment |
|---|---|
| **I. Microservices-First** | ⚠️ **Watch item.** US2/US3 put governance (Register Service's domain) onto the workflow engine (Blueprint Service's domain). Dependencies must flow downward only. Mitigation: Register Service remains the authority on roster and quorum; Blueprint Service executes the workflow and holds no governance rules. No new upward dependency — see Complexity Tracking. |
| **II. Security First** | Core purpose. Note R-006: service tokens can sign as any organisation. Not a regression, but it bounds what US2 can claim — recorded as a limitation, not hidden. |
| **III. API Documentation** | New endpoints require `.WithSummary()`/`.WithDescription()` + XML docs. The approval endpoints carry examples. |
| **IV. Testing** | >85% on new code, plus SC-009 live verification. Unit tests alone are explicitly insufficient here (R-012). |
| **V. Code Quality** | Nullable enabled, async I/O, no Release warnings. |
| **VI. Blueprint Standards** | `register-governance-v1` is authored as JSON, which is the primary path in the standard. |
| **VII. DDD** | Governance is a Register Service domain concept; the blueprint is its execution mechanism, not its owner. |
| **VIII. Observability** | New metrics on governance proposals, approvals, quorum outcomes and refusals; structured logging for every authorisation decision. |

**Gate result: PASS**, with the microservices boundary tracked below.

## Project Structure

### Documentation (this feature)

```
specs/189-org-signed-governance/
├── spec.md              # Feature specification (complete, checklist 16/16)
├── plan.md              # This file
├── research.md          # Phase 0 — R-001..R-012, live evidence
├── data-model.md        # Phase 1 — entities + state transitions
├── quickstart.md        # Phase 1 — how to exercise it end to end
├── contracts/           # Phase 1 — endpoint + payload contracts
└── checklists/
    └── requirements.md
```

### Source Code (repository root)

```
src/
├── Common/
│   ├── Sorcha.Wallet.Contracts/Constants/SorchaDerivationPaths.cs   # slot 100 documented as governance key
│   └── Sorcha.Register.Models/
│       ├── RegisterControlRecord.cs         # roster, quorum arithmetic (REUSE)
│       └── GovernanceModels.cs              # + CryptoPolicyUpdate operation type
├── Core/
│   └── Sorcha.Register.Core/Services/
│       ├── GovernanceRosterService.cs       # quorum evaluation (REUSE), + roster-snapshot identity
│       └── RegisterManager.cs
├── Services/
│   ├── Sorcha.Register.Service/
│   │   ├── Services/CryptoPolicyService.cs  # org-signed submission (US1)
│   │   ├── Services/GovernanceSigningService.cs   # NEW — resolve roster wallet + sign at slot 100
│   │   └── Endpoints/ (propose, approve, proposals)
│   ├── Sorcha.Validator.Service/Services/
│   │   ├── RightsEnforcementService.cs      # N-signature + byte comparison + detection fix
│   │   └── ControlDocketProcessor.cs        # crypto-policy op already validated
│   └── Sorcha.Blueprint.Service/            # governance instance execution (US3)
└── Apps/
    └── Sorcha.Cli/Commands/                 # RegisterCommands, SystemRegisterCommands (slot 100)

blueprints/templates/register-governance-v1.json   # revised: rule-driven quorum, schemas, crypto-policy op
walkthroughs/modules/SorchaWalkthrough/            # New-SorchaRegister signs at slot 100
```

## Implementation Phases

### Phase A — Correct authority (US1, P1) — *independently shippable*

The whole of A is invisible to the mock-validator test path, so every item carries a live check.

1. **Slot 100 becomes the governance key.** Document it in `SorchaDerivationPaths` as previously
   unused. Move attestation signing to it at all four call sites: CLI `RegisterCommands`, CLI
   `SystemRegisterCommands` (ceremony), `SandboxRegisterProvider`, walkthrough `New-SorchaRegister`.
   *These must land together* — a partial move produces registers whose rosters disagree about which
   key is authoritative.
2. **`GovernanceSigningService`** (new, Register Service): resolve the register's roster, pick the
   caller's organisation attestation, parse the wallet address from `Subject`
   (`did:sorcha:w:{address}`), and sign the transaction via
   `IWalletServiceClient.SignTransactionAsync(address, hash, SorchaDerivationPaths.RegisterAttestation,
   isPreHashed: true)`. Replaces `ISystemWalletSigningService` for governance only.
3. **`CryptoPolicyService.SubmitPolicyUpdateAsync`** signs through it (US1's visible behaviour:
   DevMode promotion completes).
4. **`RightsEnforcementService`**: compare **decoded key bytes** (R-003) with a fixed-time
   comparison; verify **every** signature, not `Signatures[0]`; require the count of distinct
   roster-matched signers to satisfy the operation's requirement.
5. **Governance detection** keys on `Metadata["Type"] == "Control"` + governance `BlueprintId`
   (R-004), and `/propose` gets `BlueprintId = register-governance-v1` (R-005). **Together** —
   fixing TX_003 alone opens the bypass.
6. **Live gate:** on a register whose genesis has **sealed** (R-002), promote DevMode; assert the
   transaction id appears in a sealed docket's `TransactionIds` and the flag flips on n1 **and**
   tiny.

### Phase B — Approvals and quorum (US2, P2)

7. `GovernanceOperationType` gains `CryptoPolicyUpdate` (R-007).
8. **Approvals are ledger transactions** (R-009), not table rows — each carries the approving
   organisation's signature at slot 100 and references the proposal.
9. Proposal captures the **roster-snapshot identity** it was raised against (R-010:
   `LastControlTxId`); count time compares and invalidates on mismatch. Invalidation is recorded as
   an outcome (FR-011c), not a silent drop.
10. Quorum evaluated via existing `ValidateQuorumAsync` over sealed approval transactions.
11. Endpoints: raise proposal, approve, list/inspect proposals.
12. **Live gate:** three-organisation register under `Unanimous`; assert not enacted at 2 of 3,
    enacted at 3 of 3, and **SC-010** — removing the sole outstanding approver invalidates rather
    than enacts.

### Phase C — Blueprint execution and audit (US3, P3)

13. Revise `register-governance-v1`: quorum expressed from the register's configured rule rather
    than hardcoded `50.01`; add crypto-policy operation; add `dataSchemas` for proposal and
    approval payloads; make "Accept Role" conditional on operation type (R-008).
14. Execute it as a real instance; approvals become action submissions by each organisation.
15. Verify the recorded sequence matches the published definition (FR-018).

### Phase D — System register ownership transfer (US4, P4)

16. CLI ceremony signs system-register attestations at slot 100; re-genesis; re-provision AIAS.
17. Transfer ownership; confirm the former owner can no longer govern and the new owner can.

## Complexity Tracking

| Concern | Why it's needed | Why the simpler option was rejected |
|---|---|---|
| Governance executes on the workflow engine (crosses a service boundary) | The maintainer's requirement, and it is what produces US3's audit trail on the ledger for free rather than as bespoke logging | Keeping governance as bespoke code beside a decorative blueprint is the present state: the published definition already claims multi-sig quorum that nothing implements, which actively misleads. Deleting the blueprint instead would abandon the consortium model. |
| Approvals as ledger transactions rather than a service-side store | F145 makes instances a ledger projection; quorum must fold identically on every node (R-009) | A service-side approval store makes one node authoritative, hides approvals from other nodes, and breaks FR-019/FR-020 auditability. |
| Clean break on the roster key (existing registers become ungovernable) | The roster is immutable; a dual-key compatibility path is a permanent ambiguity in *which* key carries governance authority | A compatibility window was explicitly considered and rejected by the maintainer; pre-release, networks are recreated routinely. |

## Known limitations (recorded, not hidden)

- **Service principals can sign as any organisation** (R-006). US2's approvals therefore prove
  "this node was asked to use the organisation's key", not "the organisation approved". Acceptable
  within one installation; genuinely mutually-distrusting consortium members need each organisation
  to authorise from its own node or session. Out of scope; must not be described as solved.
- **Existing registers become permanently ungovernable** (R-011) and must be recreated.
- Delegation and rotation of governance authority are out of scope (spec Assumptions).
