# Phase 1 Data Model: Ledger-Derived Workflow Instances

The canonical state lives in the **sealed register** (dockets + transactions, MongoDB). Everything below is either a **fact carried on a transaction** or a **derived projection** of those facts. The instance row is a materialized view, not a source of truth.

---

## Entity 1 — RoutingDecision (carried on the transaction, clear)

Attached to every action transaction's clear metadata. Replaces the singular `TransactionMetaData.NextActionId`.

| Field | Type | Notes |
|---|---|---|
| `completedActionId` | int | the action this transaction completes |
| `nextActions` | `ActionRef[]` | the **full** set of next actions (preserves parallel branches). Empty ⇒ this branch terminates. |
| `attestation` | `Attestation` | the trust mechanism (Entity 2) |

`ActionRef` = `{ actionId: int, branchKey?: string }` (branchKey distinguishes parallel branches where needed).

**Validation (at seal, `ValidationEngine`):**
- `VAL_ROUTING_001` — every `nextActions[i]` is a valid successor of `completedActionId` in the published blueprint's static route graph; a terminal (`[]`) is valid only where the route graph allows termination.
- `VAL_ROUTING_002` — `attestation` verifies and satisfies the register's required `routingAttestation` strength.

**Serialization:** canonical (`RegisterSerializationOptions.Canonical`), `[JsonPropertyName]`-stable (the #881 relay lesson). The decision is signed over its canonical bytes.

---

## Entity 2 — Attestation (pluggable trust)

A discriminated union; v1 ships only the first.

| Variant | Fields | Status |
|---|---|---|
| `SenderSigned` | `signature` (sender wallet sig over canonical `RoutingDecision` minus the attestation) | **v1 — implemented** |
| `ValidatorReEvaluated` | `validatorSignature`, `controlDisclosureRef` | reserved (v2 — control-plane disclosure) |
| `Proof` | `scheme`, `proof`, `commitments[]` | reserved (v3 — ZK / universally verifiable) |

The projection reads `RoutingDecision.nextActions` regardless of variant. Only the **validation** step branches on variant; v2/v3 validation throws "unsupported attestation strength" until implemented.

---

## Entity 3 — Instance Projection (materialized view)

The per-node cache of the deterministic fold. Reconstructable from the ledger.

| Field | Type | Source |
|---|---|---|
| `instanceId` | string | `H(registerId, blueprintId, startingActionTxHash)` (Entity 4) |
| `registerId`, `blueprintId`, `blueprintVersion` | — | from the starting action tx |
| `currentActionIds` | int[] | folded from each `RoutingDecision.nextActions` (full set; multiple ⇒ parallel) |
| `completedActionIds` / `completedActionCount` | — | folded |
| `participantWallets` | map participant-id → wallet | folded from blueprint + tx sender/recipients (participant-id keyed, never self-keyed) |
| `dataView` | map | disclosure-scoped: only fields this node is entitled to decrypt |
| `state` | enum `Active` \| `Completed` \| `Rejected` | derived (no current actions + terminal route ⇒ Completed) |
| `lastAppliedTxId` / `lastAppliedDocketHash` | — | idempotency watermark (fold is a no-op at/below it) |

**Invariants:**
- No `IsReadOnlyMirror` — there is no mirror.
- Identical across nodes for the same sealed input (FR-001), modulo `dataView` (disclosure-scoped) which is the only legitimately node-varying field.
- `materialized == RebuildAsync(instanceId)` (parity, FR-003).

**State transitions (all via fold on a sealed action tx):**
`(absent) → Active` on the sealed starting action; `Active → Active` advancing `currentActionIds` per decision; `Active → Completed` when a decision yields no next actions on the last branch; `Active → Rejected` on a sealed rejection. No transition is ever written by a submit path.

---

## Entity 4 — Instance identity derivation

`instanceId = encode( SHA256( registerId || blueprintId || startingActionTxHash ) )`. Deterministic, node-independent, unique per application, stable under late-binding. A **local draft** correlation id exists only client-side until the starting action seals and the canonical id is returned.

---

## Entity 5 — Reaction (idempotent side effect)

Not stored as domain data; an at-least-once handler keyed for idempotency.

| Field | Type | Notes |
|---|---|---|
| `sealedTxId` | string | the action tx that triggers the effect |
| `reactionKind` | enum | `CredentialMint` \| `CredentialDeliver` \| `Notification` \| `InboxWrite` |
| idempotency key | `(sealedTxId, reactionKind)` | Redis SET-NX (`Sorcha.AtomicCache`); first claim wins |
| entitlement | derived | node hosts the responsible wallet (issuer → mint; recipient → deliver/detect) |

A reaction fires at-most-once-effectively (SET-NX claim) and is safe under replay/restart. Non-entitled nodes no-op.

---

## Entity 6 — Register routing-attestation policy (governance)

On the register control record (sibling of crypto policy / validator roster).

| Field | Type | Notes |
|---|---|---|
| `routingAttestation` | enum `sender-signed` \| `validator-reeval` \| `proof` | default `sender-signed`; enforced at seal by `VAL_ROUTING_002` |

Changing it is a governance action; v1 supports only `sender-signed` (others reserved).

---

## Removed (clean break)

- `Instance.IsReadOnlyMirror`; `InstanceMirrorReconstructor`; `CreateMirrorAsync`/`UpdateMirrorAsync`.
- `TransactionMetaData.NextActionId` (singular hint) → replaced by `RoutingDecision`.
- `ActionSubmissionResponse` synchronous `NextActions`/`IssuedCredential` semantics on the brokered path (unified — see contracts).
- The imperative `AccumulatedData` write-on-submit as authoritative (becomes part of the disclosure-scoped projection `dataView`).
