# Data Model: Provenance — trust-anchor and proof lineage

**Date**: 2026-08-05 | **Feature**: 188 | **Plan**: [plan.md](./plan.md)

Phase 1 introduces **no persisted entities**. Every type here is either an in-memory evidence shape assembled by the service, or a result shape returned by the engine. The register is read, never written.

---

## Engine result types (`Sorcha.Provenance.Engine`)

### `VerificationStatus` — hoisted, shared (see R-002)

Lives in the new zero-dependency leaf `Sorcha.Verification.Abstractions`, referenced by both this engine and `Sorcha.Verifier.Engine`.

| Member | Meaning |
|---|---|
| `Verified` | The check ran and passed. |
| `Failed` | The check ran and did not pass. |
| `Unverified` | The check could not run. **Not** a failure, and never a veto. |

`Unverified` is load-bearing: on a single-validator deployment several checks genuinely cannot run, and reporting `Verified` there would be the feature's most serious possible defect.

### `ProvenanceLayer`

| Member | Question it answers |
|---|---|
| `Anchor` | Does this register's origin trace to its trust anchor? |
| `Chain` | Does this docket link to its predecessor? |
| `Seal` | Do the docket's recorded contents still match its sealed commitment? |
| `Signers` | Did the validators who signed hold authority *at that point in history*? |
| `Proposer` | Was the proposing validator a member of the set applying then? |

Register-specific by design. `Sorcha.Verifier.Engine` keeps its own credential-specific `ValidationLayer`; only the status is shared.

### `ProvenanceCheck`

| Field | Type | Notes |
|---|---|---|
| `Layer` | `ProvenanceLayer` | Which question this answers |
| `Status` | `VerificationStatus` | Verified / Failed / Unverified |
| `Headline` | `string` | One line a reader can act on |
| `Detail` | `string?` | The values behind the verdict |
| `CheckedAgainst` | `string` | **Required.** What the comparison was made against |

`CheckedAgainst` is not optional and not decorative — it is how FR-002 and FR-005 are met. For the Seal check it must read as *"recomputed from the docket's stored transaction ids"* rather than implying independent validation: recomputing with the same algorithm that sealed proves the stored data is unchanged, not that the algorithm is correct. Overstating this makes the feature worse than none.

### `ProvenanceTrail`

Ordered `ProvenanceCheck` list for one subject, plus the subject's identity. Order is presentation order: anchor first (broadest), then chain, seal, signers, proposer.

---

## Evidence shapes (assembled by the service, consumed by the engine)

The engine cannot reach storage (R-001/R-003), so the service assembles these. This layer is the honest cost of the engine boundary.

### `DocketEvidence`

| Field | Source |
|---|---|
| `DocketNumber`, `Hash`, `PreviousHash` | `DocketHeader` |
| `TransactionIds` | `DocketHeader.TransactionIds` |
| `SealedMerkleRoot` | `DocketHeader.MerkleRoot` — **null for pre-F187 dockets** ⇒ Seal is `Unverified`, not `Failed` |
| `ProposerValidatorId` | `DocketHeader.ProposerValidatorId` |
| `Votes` | `DocketHeader.Votes` — **empty is valid** on single-validator deployments ⇒ Signers is `Unverified` |
| `PredecessorHash` | prior `DocketHeader.Hash`, or null when not held ⇒ Chain is `Unverified` |

Three of these six fields carry an explicit "absent means Unverified, not Failed" rule. That is the shape of the whole feature: absence of evidence is never evidence of tampering.

### `RosterAsOf` (see R-005)

| Field | Notes |
|---|---|
| `RosterVersion` | Which version applied at this docket |
| `Entries` | Validator id + public key + status, **as they stood then** |
| `ResolvedFrom` | The control transaction establishing this version — feeds `CheckedAgainst` |

**The engine is never given the current roster.** Withholding it removes the possibility of the naive implementation rather than relying on the author's discipline.

### `AnchorEvidence`

| Field | Notes |
|---|---|
| `Fingerprint` | The anchor this node holds |
| `GenesisControlRecord` | Origin record and its signature |
| `IsAnchorKnown` | False ⇒ Anchor is `Unverified` with a reason — the correct outcome for a node whose anchor does not match the network (see issue #1374) |

---

## Spine projection (list surface — no verification)

`DocketSpineEntry` per docket: number, timestamp, proposer, signer count, and `RosterChanged` marking a docket where the validator set changed.

Deliberately carries **no `ProvenanceCheck`**. Verification is the trail endpoint's job (R-006, D6); a spine entry that could hold a status would invite someone to populate it, which is the O(n·m) list-path cost the design exists to avoid.

---

## Relationships

```
Register ──1:N── DocketSpineEntry            (list, unverified)
                      │ select
                      ▼
                 DocketEvidence ──┐
                 RosterAsOf ──────┼──► DocketProvenanceVerifier ──► ProvenanceTrail
                 AnchorEvidence ──┘            (pure)                (N × ProvenanceCheck)
```

No persistence. No writes. No new collections, indexes or migrations.
