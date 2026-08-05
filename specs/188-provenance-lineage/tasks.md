# Tasks: Provenance — trust-anchor and proof lineage

**Feature**: 188 | **Branch**: `188-provenance-lineage`
**Spec**: [spec.md](./spec.md) · **Plan**: [plan.md](./plan.md) · **Research**: [research.md](./research.md)

**Scope**: Plan Phase 1 only — the verification engine plus register lineage (User Story 1, P1). User Stories 2 and 3 are named as future work at the foot of this file and have no tasks here.

Legend: 📋 pending · 🚧 in progress · ✅ done · `[P]` parallelisable (different files, no incomplete dependency)

---

## Phase 1: Setup

- [X] T001 [P] Create `src/Common/Sorcha.Verification.Abstractions/Sorcha.Verification.Abstractions.csproj` — a zero-dependency leaf targeting `net10.0`. No `PackageReference`, no `ProjectReference`. Add to `Sorcha.sln`.
- [X] T002 [P] Create `src/Common/Sorcha.Provenance.Engine/Sorcha.Provenance.Engine.csproj` targeting `net10.0`, referencing **only** `Sorcha.Verification.Abstractions`. Add to `Sorcha.sln`.
- [X] T003 [P] Create `tests/Sorcha.Provenance.Engine.Tests/Sorcha.Provenance.Engine.Tests.csproj` — xUnit v3 + FluentAssertions, matching the conventions in `tests/Sorcha.Register.Models.Tests`. Add to `Sorcha.sln`.

---

## Phase 2: Foundational — blocks all of User Story 1

- [X] T004 **OWN COMMIT.** Hoist the tri-state status: move `LayerStatus` out of `src/Common/Sorcha.Verifier.Engine/Models/VerifierSession.cs` into `src/Common/Sorcha.Verification.Abstractions/VerificationStatus.cs` as `VerificationStatus` (`Verified` / `Failed` / `Unverified`). Add the project reference to `Sorcha.Verifier.Engine.csproj` and repoint every usage **compiler-guided** — do not blanket-rename, `LayerStatus` appears in doc comments and in `ValidationLayerResult`. `ValidationLayer` stays in the verifier: only the status is shared.
  - This edits shipped Feature 155 code with **no behaviour change**. Commit it alone so a bisect can isolate it. Verifier and Citizen.Verifier suites must be green before and after, with the counts recorded in the commit message.
- [X] T005 Assert the engine is dependency-free, in `tests/Sorcha.Provenance.Engine.Tests/EngineIsPortableTests.cs`: reflect over `typeof(ProvenanceCheck).Assembly.GetReferencedAssemblies()` and fail if any name starts with `Sorcha.Cryptography` or `Sorcha.ServiceClients`.
  - The entire Phase-3 export path rests on this, and a casual `using` would foreclose it silently. **Mutation-test it**: add a temporary reference to `Sorcha.Cryptography`, confirm the test fails naming the assembly, then remove it.
- [X] T006 [P] Create `src/Common/Sorcha.Provenance.Engine/ProvenanceLayer.cs` — `Anchor`, `Chain`, `Seal`, `Signers`, `Proposer`. Register-specific by design; see data-model.md.
- [X] T007 [P] Create `src/Common/Sorcha.Provenance.Engine/ProvenanceCheck.cs` — `Layer`, `Status`, `Headline`, `Detail?`, `CheckedAgainst` (**required**), `Reason?`. XML-doc `CheckedAgainst` with the FR-005 rule: for `Seal` it must read as *"recomputed from the docket's stored transaction ids"* and must not imply independent validation.
- [X] T008 [P] Create `src/Common/Sorcha.Provenance.Engine/Evidence/` — `DocketEvidence`, `RosterAsOf`, `AnchorEvidence` per data-model.md. Every nullable field carries an XML doc stating that absent ⇒ `Unverified`, **never** `Failed`.
- [X] T009 [P] Create `src/Common/Sorcha.Provenance.Engine/Seams/IMerkleRootCalculator.cs` — one method, transaction ids → root. XML-doc why it is a seam (R-003: the real implementation lives in `Sorcha.Cryptography`, which cannot load under browser-wasm).
- [X] T010 Create `src/Common/Sorcha.Provenance.Engine/DocketProvenanceVerifier.cs` — the orchestrator taking `DocketEvidence` + `RosterAsOf` + `AnchorEvidence` + `IMerkleRootCalculator`, returning an ordered `ProvenanceTrail`. Checks stubbed to `Unverified("not implemented")` so the ordering is testable before any check exists.

**Checkpoint**: solution builds; T005 green; no check yet claims `Verified`.

---

## Phase 3: User Story 1 — Prove a register's history and who signed each docket (P1)

**Goal**: an administrator opens a register, sees every docket from genesis with its proposer and signer set and roster changes in place, and can verify any docket.

**Independent test**: open a register with several dockets and a validator-set change; confirm history is complete and ordered, the change is visible where it happened, and selecting a docket yields per-check results naming what each compared.

### The checks — each test RED before its implementation

> A guard never shown to fail is not a guard. Record the RED output in the PR body for each.

- [X] T011 [P] [US1] **RED**: `AnchorCheckTests` in `tests/Sorcha.Provenance.Engine.Tests/Checks/` — genesis record verifying against the anchor ⇒ `Verified`; a mismatched anchor ⇒ `Failed`; `IsAnchorKnown == false` ⇒ `Unverified` with a reason (the correct outcome for a node whose anchor does not match the network — issue #1374).
- [X] T012 [US1] Implement the Anchor check in `DocketProvenanceVerifier`. T011 green.
- [X] T013 [P] [US1] **RED**: `ChainCheckTests` — `PreviousHash` matching the predecessor ⇒ `Verified`; mismatch ⇒ `Failed`; **predecessor not held ⇒ `Unverified`, not `Failed`**. A partial replica must not read as compromised.
- [X] T014 [US1] Implement the Chain check. T013 green.
- [X] T015 [P] [US1] **RED**: `SealCheckTests` — recomputed root equal to `SealedMerkleRoot` ⇒ `Verified`; **a tampered transaction id ⇒ `Failed`**; **`SealedMerkleRoot == null` (pre-F187 docket) ⇒ `Unverified` with a reason, not `Failed`**. Assert `CheckedAgainst` states recomputation-from-stored-ids rather than implying independent validation.
- [X] T016 [US1] Implement the Seal check over `IMerkleRootCalculator`. T015 green.
- [X] T017 [P] [US1] **RED — the highest-value test in the feature.** `RosterAsOfTests`: build a register where a validator is removed at docket 12. A docket-**10** signature from that validator ⇒ `Verified` (it held authority then); a docket-**14** signature from the same key ⇒ `Failed`.
  - This is the only test that fails against an implementation that looks entirely correct. Verifying against the *current* roster passes every other test in this file and starts reporting false tampering the moment the network grows.
- [X] T018 [P] [US1] **RED**: `SignersCheckTests` — every signature valid against `RosterAsOf` ⇒ `Verified`; an invalid signature ⇒ `Failed`; **an empty vote set ⇒ `Unverified` with a reason, never `Verified`**. Single-validator deployments are the common case, not an edge.
- [X] T019 [US1] Implement the Signers check. T017 and T018 green. The verifier must consume `RosterAsOf` only — it is never handed a current roster (D5).
- [X] T020 [P] [US1] **RED**: `ProposerCheckTests` — proposer present in `RosterAsOf` ⇒ `Verified`; absent ⇒ `Failed`; roster version unresolvable ⇒ `Unverified`.
- [X] T021 [US1] Implement the Proposer check. T020 green.
- [X] T022 [US1] Coverage guard in `ProvenanceLayerCoverageTests`: reflect over `ProvenanceLayer` and fail if any member is not exercised by at least one test, so a layer cannot be silently left unchecked.
- [X] T023 [US1] **Mutation sweep.** For each of the five checks, force it to return `Verified` unconditionally and confirm at least one test fails naming that layer. Record the results in the PR body. This is the only evidence the guards are real.

### Register Service — evidence assembly and endpoints

- [X] T024 [US1] Implement roster-as-of resolution in `src/Services/Sorcha.Register.Service/Provenance/RosterAsOfResolver.cs` — walk control transactions up to a docket's height and return the roster version applying **at that docket**, with `ResolvedFrom` naming the control transaction that established it.
  - The resolver is the only component that may see the full roster history. Nothing downstream receives "the current roster" (D5).
- [X] T025 [US1] Implement `src/Services/Sorcha.Register.Service/Provenance/DocketEvidenceAssembler.cs` — read `DocketHeader` + predecessor + anchor, produce `DocketEvidence` / `AnchorEvidence`. Absent inputs map to nulls that the engine renders `Unverified`; the assembler must not throw for missing evidence.
- [X] T026 [US1] Implement `MerkleRootCalculator : IMerkleRootCalculator` in the same folder, delegating to the existing `MerkleTree.ComputeMerkleRoot` (`src/Common/Sorcha.Cryptography/Utilities/MerkleTree.cs:32`). **Delegate — do not reimplement** (R-003/D4).
- [X] T027 [US1] Add `GET /api/provenance/registers/{registerId}` in `src/Services/Sorcha.Register.Service/Endpoints/ProvenanceEndpoints.cs` — paged spine per `contracts/provenance-api.yaml`. **Runs no checks** (D6). `RequireAdministrator` + `RequirePlatformAudience`. `.WithSummary()` / `.WithDescription()`.
- [X] T028 [US1] Add `GET /api/provenance/registers/{registerId}/dockets/{docketNumber}` — assemble, delegate to the engine, return the trail. Same authorization.
- [X] T029 [US1] Endpoint tests in `tests/Sorcha.Register.Service.Tests/Endpoints/ProvenanceEndpointTests.cs`: authorization (401 unauthenticated, 403 non-admin, 403 consumer-tier), unknown register ⇒ 404, and — **the important one** — a docket whose evidence cannot be assembled returns **200 with `Unverified` rows carrying reasons, never a 5xx**.
- [X] T030 [US1] Assert the spine runs no verification: a spine response over a register with many dockets must contain no check results, and `DocketSpineEntry` must have no status field to populate (D6, SC-007).

### UI — `Sorcha.UI.Core`, not `Components.User`

- [X] T031 [US1] [P] Add the typed client in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/Admin/ProvenanceService.cs` + interface, registered with the authenticated handler chain. **Not** an ambient `HttpClient` — that is anonymous by design and 401s silently.
- [X] T032 [US1] Build `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Provenance/RegisterLineage.razor` — the docket spine: chronological from genesis, each docket carrying proposer and signer count, **roster changes rendered as events on the spine** so network growth is visible as history. Paged.
- [X] T033 [US1] Build `DocketProvenanceTrail.razor` + `ProvenanceCheckRow.razor` in the same folder — the layered evidence trail, reusing the stacked-expandable idiom of F155's `VerdictTrailPanel`. **Do not invent a second idiom.** Each row shows status, headline, `CheckedAgainst`, and expands to `Detail`. `Unverified` must be visually distinct from `Failed` — amber-neutral versus red — because conflating them is the feature's core misreading.
- [X] T034 [US1] bUnit tests in `tests/Sorcha.UI.Core.Tests/Provenance/`: a trail with a `Failed` row renders it distinctly from `Unverified`; a spine with a roster change renders the marker; an empty signer set renders as "not verifiable" rather than a tick or an error.
- [X] T035 [US1] Wire the entry point from the existing register explorer (`Components/Explorer/`) so lineage is reachable from a register without a new top-level nav item.

### Observability

- [X] T036 [US1] [P] Add `src/Services/Sorcha.Register.Service/Provenance/ProvenanceMetrics.cs` on a `Sorcha.Provenance` meter: `sorcha_provenance_check_total{layer,status}` and `sorcha_provenance_trail_duration_seconds{surface}`. No subject data on any dimension. Add the meter to the ServiceDefaults export allowlist.

### Live verification

- [ ] T037 [US1] Verify on n1 against the real AIAS registers (identity `b388e51816e34d4ea7ce275ca7e8219c`, cyber `06658347d724454e89a6655d8852d6ac`) — both hold sealed dockets from the 2026-08-05 re-genesis.
  - **Expected**: `anchor`, `chain`, `seal`, `proposer` ⇒ `Verified`; **`signers` ⇒ `Unverified`**, because n1 runs single-validator mode. A green tick on `signers` is a defect, not good news.
  - Confirm the spine loads and pages; record timings against SC-007.
  - A green suite does not prove the fold — Feature 187's whole history says so.

**Checkpoint**: User Story 1 complete and independently demonstrable.

---

## Phase 4: Polish & close-out

- [ ] T038 [P] Update `.claude/skills/sorcha-architecture/SKILL.md` with a Provenance section: the two surfaces, the tri-state, and the three traps (roster-as-of, empty votes, tamper-evidence-not-correctness).
- [ ] T039 [P] Update `docs/reference/API-DOCUMENTATION.md` with the two endpoints.
- [ ] T040 [P] Update `.specify/MASTER-TASKS.md` with the shipped entry.
- [ ] T041 **Narrow issue #1372** per plan decision D4: F188's Seal check is that cross-check for the docket surface; #1372's remaining scope is the proof-generation and chain-integrity endpoints calling the same `IMerkleRootCalculator` seam. Left as-is, both features implement one comparison and drift starts immediately.
- [ ] T042 Add `Sorcha.Provenance.Engine` to the WASM-safety gate (`scripts/check-wasm-safe.ps1`) if that gate enumerates projects, so the Phase-3 property is defended by CI rather than by T005 alone.

---

## Dependencies

```
Setup (T001-T003)
      │
Foundational (T004-T010)          T004 is its own commit
      │
User Story 1 (T011-T037)
      ├── checks   T011-T023   (tests RED before each impl; T017 before T019)
      ├── service  T024-T030   (T024 before T025; T026 before T028)
      ├── UI       T031-T035   (T031 before T032/T033)
      ├── metrics  T036
      └── live     T037        (after service + UI)
      │
Polish (T038-T042)
```

**Parallel opportunities**: T001-T003; T006-T009; the RED tests T011/T013/T015/T017/T018/T020 (separate files, no shared state); T038-T040.

**Critical ordering**: T017 (roster-as-of) **must** exist and be RED before T019. Writing the Signers check first and the test after is how the naive current-roster implementation ships looking correct.

## MVP

**T001-T023** — the engine with all five checks, adversarially tested and mutation-verified. At that point the verification logic is proven without any service or UI, and is already the Phase-3 export core.

Adding T024-T030 makes it reachable; T031-T035 makes it usable.

## Future work (no tasks here)

| Phase | Scope | Depends on |
|---|---|---|
| 2 | **User Story 2** — application lineage: instance narrative, five authority checks (sender authority, routing attestation, decision reason, inclusion, issuance), and **User Story 3** cross-links | Phase 1 engine; F145/F184 attestation |
| 3 | Portable export bundle for external auditors | Phase 1 engine being genuinely dependency-free (T005) |

Phase 3 is why T005 is not optional: if the engine acquires a service dependency, the export becomes a rewrite rather than an addition.
