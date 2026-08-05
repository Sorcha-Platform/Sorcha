# Research: Provenance — trust-anchor and proof lineage

**Date**: 2026-08-05 | **Feature**: 188 | **Plan**: [plan.md](./plan.md)

Findings established against source before planning, not assumed. Each records what was checked, so a later reader can re-verify rather than trust.

---

## R-001 — `Sorcha.Verifier.Engine` cannot be referenced by a dependency-free engine

**Checked**: `src/Common/Sorcha.Verifier.Engine/Sorcha.Verifier.Engine.csproj`

```
PackageReference  Microsoft.Extensions.Logging.Abstractions
PackageReference  BouncyCastle.Cryptography
ProjectReference  Sorcha.ServiceClients.Http
ProjectReference  Sorcha.Cryptography.Secp256k1
```

**Decision**: do not reference it. Reusing `LayerStatus` in place would drag `ServiceClients.Http` into the provenance engine and defeat the property the engine exists for.

**Rationale**: the engine's whole justification is that it can run where the service cannot (Phase 3 export, potentially WASM). A reference that reintroduces HTTP clients makes the boundary decorative.

**Alternatives considered**: reference it and accept the weight — rejected, it silently forecloses Phase 3, and the failure would only appear when someone tried to build the export.

---

## R-002 — The tri-state status already exists and must not be duplicated

**Checked**: `src/Common/Sorcha.Verifier.Engine/Models/VerifierSession.cs:89` declares `enum LayerStatus`; `:105` declares `record ValidationLayerResult`.

**Decision**: hoist the tri-state (`Verified` / `Failed` / `Unverified`) into a zero-dependency leaf that both engines reference. Each domain keeps its own *layer* enum — `ValidationLayer` is credential-specific (LivePresentation / IssuerSignature / Revocation / RegisterAnchor); `ProvenanceLayer` is register-specific (Anchor / Chain / Seal / Signers / Proposer).

**Rationale**: the status is a genuinely shared concept; the layer is not. Feature 187 has just finished removing a defect of exactly this shape — `VoteDecision` declared twice with **incompatible numeric values** (`Reject` = 2 in one, 0 in the other), in two assemblies that reference each other, distinguishable only by namespace qualification, with nothing in the type system objecting. Declaring a second status enum now would be that mistake with a different name.

**Alternatives considered**:
- Local `ProvenanceStatus` — cheapest today; drifts the moment either gains a member, and the drift is silent.
- Reuse `ValidationLayerResult` wholesale — wrong shape: it carries credential-verification layers and no `CheckedAgainst`.

**Cost**: a namespace move touching a shipped feature (F155). Compiler-guided, no behaviour change, but it must be its own commit so a bisect can isolate it.

---

## R-003 — A Merkle recomputation primitive already exists, in an assembly the engine cannot use

**Checked**: `MerkleTree.ComputeMerkleRoot` lives in `src/Common/Sorcha.Cryptography/Utilities/MerkleTree.cs:32`, with call sites in `Register.Service/Program.cs:2981`, `Validator.Service/DocketBuilder.cs:156,164`, `DocketConfirmer.cs:422,430`, `GenesisManager.cs:65`.

**Constraint**: `Sorcha.Cryptography` P/Invokes libsodium and cannot load under browser-wasm — the documented reason `Sorcha.Mdoc` was extracted from it during F185.

**Decision**: the engine declares `IMerkleRootCalculator`; the service implements it over the existing `MerkleTree`.

**Rationale**: keeps one algorithm with one implementation while keeping the engine portable. Mirrors the established seam pattern (`IRevocationChecker`, `IIssuerKeyResolver`, `ITenantTrustAnchorProvider` in F135).

**Alternatives considered**:
- Reference `Sorcha.Cryptography` — forecloses Phase 3.
- Reimplement inside the engine — a second Merkle implementation is the duplicate-projection defect F187 existed to fix, and the two would agree right up until they didn't.

---

## R-004 — The Seal check and F187 US3 are the same computation

**Checked**: issue #1372 remains open; during the F187 n1 deploy the inclusion-proof endpoint returned a `merkleRoot` byte-identical to the persisted `DocketHeader.MerkleRoot` for the same docket.

**Decision**: one computation, two surfaces. F188 implements the comparison as an engine check over the R-003 seam; #1372 narrows to the proof-generation and chain-integrity endpoints calling that same seam.

**Rationale**: two features independently implementing one comparison is precisely how the docket projection came to exist twice.

**Consequence**: #1372 must be updated. Left as-is, both features will implement it and the drift starts immediately.

---

## R-005 — Roster-as-of, not roster-now

**Context**: F086 governance changes the validator set over the life of a register (`AddValidator`, `RemoveValidator`, `RotateValidatorKey`). Network enlargement is precisely the act of changing it.

**Decision**: evidence assembly resolves the roster version applying at each docket by walking control transactions up to that docket's height, and hands the engine `RosterAsOf`. The engine is never given the current roster.

**Rationale**: a signature valid when made must stay valid. Judging history against the present set produces a *false failure*, which is worse than no check — it would report tampering where there is none, and it would do so more often the more the network grows. Withholding the current roster from the engine removes the possibility rather than relying on discipline.

**Alternatives considered**: give the engine the current roster plus a change-log and let it work backwards — more flexible, and it puts the trap inside the most-copied code.

**Open**: the cost of walking control transactions per docket is unmeasured. Deferred to measurement against SC-007 rather than pre-optimised.

---

## R-006 — Verification must not run on the list path

**Context**: SC-007 requires a 5,000-docket register to stay usable. Each Seal check is O(n) hashing over a docket's transactions.

**Decision**: two endpoints. The spine returns summaries and runs no checks; the trail verifies exactly one docket.

**Rationale**: same conclusion F187 US3 reached for the cross-check — verify where integrity is *asserted*, not on every read. Splitting the endpoints makes the expensive path impossible to enter by accident, rather than relying on a caller remembering.

---

## R-007 — "Audit" is already taken, and means the opposite

**Checked**: `src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/Admin/IAuditService.cs` — *"Client-side audit service for **logging** administrative actions"*, with `LogEventAsync`-style members.

**Decision**: name the feature **Provenance** throughout — types, routes, components.

**Rationale**: one word covering both a write-side action logger and a read-side evidence viewer is the collision class this codebase has just spent a day untangling (`Docket` ×2, `ValidatorSignature` ×2, `VoteDecision` ×2). Cheaper to avoid than to unpick.
