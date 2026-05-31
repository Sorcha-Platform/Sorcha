# Phase 0 Research: Ledger-Derived Workflow Instances

The architectural decisions were resolved in the design session; the mechanics were established by three code investigations. This consolidates them as implementation decisions.

---

## R1 — Where the RoutingDecision rides on the transaction (clear, not encrypted)

**Decision**: Carry the `RoutingDecision` (the full next-action set + attestation) on the transaction's **clear metadata** (`TransactionMetaData`), replacing the singular `NextActionId`. It is NOT inside the encrypted payload.

**Rationale**: FR-010 requires every node to advance control state **without decrypting** payload. Metadata is already the clear, all-nodes-readable part of a tx (the current singular hint lives there). The validated decision must be projection-readable on subscribers that can't decrypt the payload. `TransactionMetaData` needs a `[JsonPropertyName]`-stable, canonical serialization (the #881 lesson: PascalCase silently dropped on relay) — use `RegisterSerializationOptions.Canonical`.

**Alternatives**: in the encrypted payload (rejected — breaks subscriber projection); a separate sidecar tx (rejected — extra seal round-trip, ordering complexity).

---

## R2 — Routing decision computation + the SenderSigned attestation (v1)

**Decision**: The Blueprint Engine's routing evaluation emits the **full** `NextActions` set (today `RoutingResult.NextActions` already computes it; the loss is downstream at `ActionExecutionService.cs:984-993` collapsing to a singular hint). `ActionExecutionService` builds a `RoutingDecision{completedActionId, nextActions[], attestation}` and signs it with the sender wallet (the same signer already used for the tx). v1 attestation = `SenderSigned{sig over canonical(decision)}`.

**Rationale**: FR-007 (full set, parallel branches), FR-009 (sender-signed default). Reuses the existing routing engine + the sender's signing key; no new crypto.

**Alternatives**: validator computes routing (rejected — validator "only knows the static route graph", `DocketBuildTriggerService.cs:636-642`, can't evaluate conditions).

---

## R3 — Validator validation of the decision (VAL_ROUTING_*)

**Decision**: `ValidationEngine` adds, before seal: **VAL_ROUTING_001** — the decision's `nextActions` are valid successors of `completedActionId` in the published blueprint's static route graph (structural); **VAL_ROUTING_002** — the attestation verifies (sender signature over the canonical decision) and meets the register's required attestation strength. v1 does NOT decrypt payload or re-evaluate the condition. `DocketBuildTriggerService` carries the validated decision through the seal (replacing `ResolveNextActionId`).

**Rationale**: FR-008. Consistent with the current trust model (validator does structure/signature/chain, submitter is trusted for data on encrypted registers). The route-graph is available to the validator (it already resolves the published blueprint for VAL_BP_002).

**Alternatives**: full re-evaluation now (deferred to v2 control-disclosure — out of scope); no validation (rejected — the decision must be a trustworthy fact).

---

## R4 — Deterministic instance identity

**Decision**: `instanceId = base32/hex( H( registerId || blueprintId || startingActionTxHash ) )`, computed by `InstanceIdentity` on both the submit side (returned in the ack once sealed) and the projector (on observing the sealed starting action). `POST /instances` becomes a **local draft** (no ledger write, no store row that other nodes need). The starting action is submitted against the draft; the canonical id is derived from its sealed tx.

**Rationale**: FR-005/FR-006. Removes the node-local GUID (`Program.cs:2166`) that makes instances unshareable. Deterministic from on-chain data → identical on every node. Hash over the starting tx hash guarantees uniqueness per application and stability under late-binding.

**Open detail for planning**: the client submits the starting action before it has the canonical id; the submit ack returns it. Draft-state correlation is a client/local concern (a local draft id mapped to the canonical id on ack).

---

## R5 — The InstanceProjector (pure fold, every node)

**Decision**: One `InstanceProjector : BackgroundService` subscribes to `docket:confirmed` (Redis Streams, `RegisterEventChannels.DocketConfirmed`) on **every** node — generalizing `InstanceMirrorReconstructor` (which already subscribes to exactly this event and is owner-only). On each sealed **action** tx for a held register it folds: derive/confirm instanceId, advance `CurrentActionIds` from the validated `RoutingDecision.nextActions`, mark completed + counts, update participant→wallet bindings (participant-id keyed, from blueprint + tx), update the disclosure-scoped data view. Writes the materialized view via `IInstanceStore`. **Pure** (no side effects). **Idempotent** (re-folding an applied tx is a no-op, tracked by last-applied tx/docket per instance). **Order-independent** for the same sealed set (fold dockets in `PreviousHash` chain order; txns within a docket by canonical tx-hash order).

**Rationale**: FR-001/002/004. The mechanism already exists for the owner case; making it run everywhere + reading the full validated decision (not the singular hint) + dropping the mirror flag yields the single shared state machine. `EfCoreInstanceStore.GetPendingActionsByWalletAsync` then surfaces the current action to the right participant on every node (fixes the discovery blocker).

**Alternatives**: keep imperative mutation on the owner + projection on subscribers (rejected — that IS the origin/mirror drift).

---

## R6 — Materialized view + Rebuild + parity

**Decision**: The instance row is a cache. `IInstanceStore` gains `RebuildAsync(instanceId)` (replay the instance's sealed txs via the register tx stream — the existing `StateReconstructionService` data path generalized to also fold control state) and a parity check `materialized == RebuildAsync`. A periodic/CI parity self-check + an operator-triggered rebuild for recovery. `IsReadOnlyMirror`, `CreateMirrorAsync`, `UpdateMirrorAsync` are deleted.

**Rationale**: FR-003. The ledger is canonical; the view is reconstructable. Gives recovery + an integrity invariant (SC-003).

---

## R7 — Single async submission path + roster sealer selection

**Decision**: `ActionExecutionService` collapses to one path: validate → compute `RoutingDecision` → build+sign tx (carrying the decision) → submit to local mempool AND fan out → return **`202 {txId, instanceId, accepted}`**. Remove `ApplyInstanceStateChanges` (no imperative mutation) and the `!LocallyOwned` branch. The projection advances state on seal. **Bounded-wait convenience**: optionally await the projection up to ~2–3s (signalled via a Redis key on `instance-advanced:{instanceId}`) and return `200` with advanced state if it lands, else `202`. **Sealer selection** for fan-out uses `IRegisterLocalRelationshipService` (F108 roster) — retiring the `TransactionDistributionService` seeds-topology heuristic; the F143 relay transport is unchanged.

**Rationale**: FR-011/012/013, SC-006. One path = no owner/subscriber divergence. Bounded-wait keeps the single-node case ergonomic without a second architectural path.

**Alternatives**: pure 202 always (viable; bounded-wait chosen for caller ergonomics during migration); keep the topology heuristic (rejected — two ownership mechanisms).

---

## R8 — Reactions (idempotent, role-gated)

**Decision**: One `ReactionDispatcher : BackgroundService` subscribes to `docket:confirmed`. For a sealed action declaring a side effect (`credentialIssuanceConfig`, notification, inbox), it checks entitlement = **does this node locally host the responsible wallet** (issuer-wallet-host mints; recipient-wallet-host delivers/detects) — the same `IWalletServiceClient.GetWalletAsync` probe the reconstructor uses — and if entitled performs it **once**, idempotent on `(sealedTxId, reactionKind)` via `Sorcha.AtomicCache` SET-NX (the F114/F128 single-use pattern). Credential mint moves out of `ActionExecutionService`'s inline path into a reaction. Inbound credential detection/delivery on the recipient node is a reaction on the same key.

**Rationale**: FR-015/016/017, SC-004. Pure projection (state) vs at-least-once idempotent side effects (reactions) — the textbook split, and it prevents double-issue across nodes/replay/restart.

**Alternatives**: issuer-acts-inline (rejected — keeps a sliver of inline coupling + ties issuance to the submitter not the entitled node); durable outbox (deferred — at-least-once+idempotent suffices now).

---

## R9 — Register governance: routing-attestation policy

**Decision**: Add `routingAttestation: sender-signed | validator-reeval | proof` to the register control record (default `sender-signed`), read via a `Sorcha.Register.Core/Governance` policy service (sibling of the F083 crypto policy / F086 roster). The validator enforces the declared strength at seal. v1 implements only `sender-signed`; the other values are reserved (the seam) and rejected-if-required until v2/v3 land.

**Rationale**: FR-009. Makes the trust-level a per-register governance lever (where the user wants it), so v2/v3 attestations are additive, not a fork.

---

## R10 — Presentation lifecycle on the projection (the hard slice)

**Decision**: `PresentationOutcome` / `PresentationAbandoned` transitions carry a `RoutingDecision` like any action; the `InstanceProjector` advances on their seal. The F119 `IPresentationSealCoordinator`'s job (don't advance until the predecessor seals) is **subsumed** by the seal-ordered projection (the projector only ever folds sealed txns, in chain order). Keep the `ValidationEngine` `VAL_BP_003` carve-out for these intra-action terminals and the F119 idempotency sentinels where they still apply. Migrate carefully as its own phase; verify no chain-ordering regression.

**Rationale**: FR-018. The projection's seal-ordering naturally provides what F119 added bespoke machinery for. Highest-complexity reconciliation — isolated to its own phase.

**Alternatives**: leave presentation on its own path (rejected — reintroduces a parallel advancement mechanism, the smell).

---

## R11 — Clean-break gate

**Decision**: `scripts/check-ledger-derived-clean-break.ps1` (modeled on F135's `check-trust-clean-break.ps1`) greps for and fails on: `InstanceMirrorReconstructor`, `IsReadOnlyMirror`, `CreateMirrorAsync`/`UpdateMirrorAsync`, `ApplyInstanceStateChanges`, the `LocallyOwned` submit branch, the `NextActionId` singular hint, and the topology ownership heuristic. Wired into CI.

**Rationale**: FR-019/020. Locks in the deletion.

---

## Resolved-unknowns summary

| Unknown | Resolution |
|---|---|
| Where the decision rides | clear `TransactionMetaData`, canonical serialization (R1) |
| Who computes / signs it | Blueprint Engine emits full set; sender signs (R2) |
| Validator role | structural successor + attestation check, `VAL_ROUTING_*` (R3) |
| Instance identity | `H(registerId,blueprintId,startingTxHash)`, draft-until-first-action (R4) |
| Projection mechanism | generalize the `docket:confirmed` subscriber to every node, full-decision fold (R5) |
| Recovery/integrity | Rebuild + parity; delete mirror writes (R6) |
| Submission | one async path + bounded-wait; roster sealer selection (R7) |
| Side effects | idempotent role-gated reactions, SET-NX on (txId,kind) (R8) |
| Trust governance | register `routingAttestation` policy (R9) |
| Presentation lifecycle | carry decisions; projection subsumes seal-ordering (R10) |
| Cleanup enforcement | clean-break CI gate (R11) |
