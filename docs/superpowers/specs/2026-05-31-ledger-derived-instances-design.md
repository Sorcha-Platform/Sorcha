# Ledger-Derived Workflow Instances — design

- **Status:** ✅ APPROVED — ready for `/speckit.specify`.
- **Date:** 2026-05-31
- **Author:** architecture session (Stuart + Claude)
- **Theme:** make a workflow instance a deterministic **projection of the sealed register**, identical on every node — eliminating the "origin vs mirror" duplication and the two-path submission model that have repeatedly caused cross-node friction.

---

## 1. Problem — why this exists

Three investigations into the live code (Blueprint, Validator, Register, Peer services) established that a workflow **instance is not a projection of the ledger** today. It is independently-stored mutable state, mutated imperatively, with the cross-node case bolted on. Concretely, three deviations from an event-sourced model:

1. **Instance control-state is authoritative mutable per-node store.** `CurrentActionIds`/`State` live in a Postgres row that `ActionExecutionService.ApplyInstanceStateChanges` edits in place right after a submit (`ActionExecutionService.cs:1762-1834`). The ledger does not drive it. (The *data* part, `AccumulatedData`, *is* a labelled cache of `StateReconstructionService`'s ledger projection — but control flow is not projected at all.)
2. **Routing is computed off-ledger and is not replayable.** The next-action decision runs in the Blueprint Engine against the payload; the validator "only knows the static route graph" (`DocketBuildTriggerService.cs:636-642`) and the answer is carried as a **singular** `MetaData.NextActionId` *hint* (parallel branches collapse to one).
3. **Instance creation is local-only with a node-local GUID.** `POST /instances` does `Id = Guid.NewGuid()` + a local store write, **no ledger transaction** (`Program.cs:2163-2185`). So "the same" workflow has a different id on every node, and a node that didn't run creation has no idea the instance exists until an action tx seals.

**Consequences (the recurring symptoms):**
- **Mirrors** (`InstanceMirrorReconstructor`, `IsReadOnlyMirror`) exist *only* to compensate for Deviation 3 — `ActionExecutionService` hard-requires a local instance row (`if (instance == null) throw "Instance not found"`, `:162-166`), which the owner lacks, so a synthetic mirror is reconstructed on `docket:confirmed`.
- **Two submission paths** are one submission with an optimistic shortcut forced by Deviation 1: both downstream calls (validator submit + peer fan-out) always fire; the `!LocallyOwned && AcceptedCount>0` branch only decides *wait-synchronously-and-advance-now* (owner) vs *return-202-advance-on-sync-back* (subscriber). The owner advances its row imperatively *now*; the subscriber advances from the replicated seal *later* → **two independent projections that drift** (single-vs-multi next action, self-keyed vs participant-keyed wallets, missing `AccumulatedData`, count drift).
- **Ownership is decided two ways**: the submit path uses a peer-topology heuristic (`TransactionDistributionService.ForwardSubmissionAsync` — "do I have seeds configured?"), bypassing the authoritative F108 `RegisterLocalRelationship` (roster-derived) which is computed but unused on this path.
- Live demo symptoms all trace here: `nextActions:[]`/"next: complete" on cross-node submit; the autonomous agent blind to the mirror's Action 2 via `/api/actions/pending`; `sync-state Indeterminate`; two disagreeing published-blueprint reads; subscribe/re-subscribe desyncs.

It is **one architectural choice expressed five ways**, not five bugs.

---

## 2. Target model

The register's **sealed transactions are the only source of truth.** A workflow instance is a **deterministic projection** of the sealed action transactions for that instance, computed identically on every node holding the register. There is no origin and no mirror — there is *the instance*, the same everywhere. Submission appends an action transaction; whichever node is on the register's validator roster seals it; the sealed docket replicates; **every node re-projects and advances identically.**

Two clean separations underpin it:
- **Projection** (pure, deterministic, every node) advances *state*.
- **Reactions** (at-least-once, idempotent, node-role-gated) perform *side effects*.

---

## 3. Routing as a carried, attested ledger fact

The submitter computes the **full `NextActions` set** — the `RoutingDecision` — and writes it onto the action transaction. Trust in that decision comes from a **pluggable `Attestation`** whose strength is an upgrade path, while the ledger shape and the projection stay invariant:

```
RoutingDecision {
  completedActionId : int
  nextActions       : ActionRef[]          // FULL set — fixes parallel-branch collapse
  attestation       : Attestation
}
Attestation =
  | SenderSigned        { sig }                         // v1 — ships now
  | ValidatorReEvaluated{ validatorSig, controlRef }    // v2 — control-plane disclosure
  | Proof               { scheme, proof, commitments }  // v3 — ZK / universally verifiable
```

- **v1 — `SenderSigned`.** The decision is signed by the authorised sender. The **sealing validator validates**: the `nextActions` are real successors of the current action in the blueprint's **static route graph** (structural), the sender is authorised for the action, and the chain is intact. The validator does **not** decrypt payload or re-evaluate the condition — consistent with how the system already trusts submitters for action data on encrypted registers. Reject codes: `VAL_ROUTING_001` (decision not a structural successor), `VAL_ROUTING_002` (attestation/sig invalid).
- **v2 — `ValidatorReEvaluated`.** A **control plane**: any field a route condition references is placed in a disclosure group wrapped to the validator roster (publish-time guard). The validator decrypts only those control fields and re-evaluates. Additive — same `Attestation` slot.
- **v3 — `Proof`.** The submitter attaches a succinct proof that `nextActions = evaluate(routeGraph, committedInputs)`; verifiable **without** seeing inputs. Route conditions are JSON-Logic (comparisons / ==, ranges, booleans) — a ZK-friendly subset; the encrypted payload already commits the field values. A *universally-verifiable* ("shared") proof lets **every** subscriber confirm routing without decrypting or trusting the roster — a fully trustless projection, strictly stronger than v2.

**Governance lever (ties to Register governance, F083/F086).** The required attestation strength is a **register governance policy**: the control record carries `routingAttestation: sender-signed | validator-reeval | proof` (default `sender-signed`). A register can *govern itself up* to stronger trust with no platform fork — the natural home for this decision.

**The projection consumes `decision.nextActions` regardless of attestation type.** Ship v1; v2/v3 are drop-ins.

---

## 4. Deterministic, ledger-anchored instance identity

"Start application" is a **local draft — no ledger write.** The instance is **born when its starting action seals**:

```
instanceId = H(registerId, blueprintId, startingActionTxHash)
```

derived identically on every node. The client submits the starting action with a local correlation id; the async response returns the canonical, ledger-derived `instanceId`. Every node, on observing the sealed starting action, derives the same id and creates the instance projection. Open-participant late-binding is unaffected — the id derives from the tx hash regardless of who is late-bound, and the binding is recorded by the projection from the sealed starting action.

This removes Deviation 3 → removes the reason mirrors exist.

---

## 5. The Instance Projection (pure, deterministic, every node)

A single **`InstanceProjector`** subscribes to `docket:confirmed` on **every** node. On each sealed **action** transaction for a register it holds, it folds the tx into the instance:

- advance `CurrentActionIds` from the validated `RoutingDecision.nextActions` (full set → parallel branches preserved),
- record the completed action + bump counts,
- update participant→wallet bindings from the tx (participant-id keyed, from the blueprint + tx sender/recipients),
- update the data view (disclosure-aware, what this node can decrypt).

The stored instance row is a **materialized view** — a cache of the projection, **rebuildable from the ledger** at any time via `Rebuild(instanceId)` (replay the instance's sealed txs). A periodic/CI **parity self-check** asserts `materialized == fresh replay`.

**Determinism invariants:** dockets folded in chain order (`PreviousHash`), transactions within a docket folded in a canonical order (by tx hash); re-folding an already-applied tx is a no-op (idempotent). Same sealed input → same state on every node, independent of arrival order or restarts.

**Replaces:** `ApplyInstanceStateChanges` (imperative mutation), `InstanceMirrorReconstructor` (mirror), the origin/mirror split. `IsReadOnlyMirror`, `CreateMirrorAsync`, `UpdateMirrorAsync` are deleted.

> This is what fixes the agent-discovery blocker: a node projects the next action as *current* from the same sealed docket every other node sees, so `/api/actions/pending` (fed by the projection, not a heuristic mirror) surfaces it naturally to the entitled participant.

---

## 6. Single async submission path

`ExecuteAction` becomes one path:

1. **Validate locally** — schema, sender authorisation, credential/presentation gates, then **compute the `RoutingDecision`** (full next-action set) and its `SenderSigned` attestation.
2. **Build + sign** the action transaction carrying the `RoutingDecision`.
3. **Submit to local mempool AND fan out to the roster's sealer** (both always — no ownership branch on the submit itself).
4. **Return `202 {txId, instanceId, accepted}`.** The submitter does **not** mutate instance state. The projection advances it when the seal is observed — identically on every node.

**Ownership is used only to route the fan-out to a sealer** — derived from the authoritative F108 `RegisterLocalRelationship` (validator roster), retiring the peer-topology heuristic. The `LocallyOwned` flow-branch is deleted.

**Bounded-wait convenience (one path, not a branch):** the endpoint may optionally wait a short bound (~2–3s) for the projection to advance and return `200` with the advanced state; otherwise `202`. Clients always handle `202` (subscribe to instance-updated events / poll). This keeps the single-node case ergonomic without reintroducing an architectural fork.

---

## 7. Reactions (idempotent, role-gated side effects)

A separate **`ReactionDispatcher`** subscribes to the same sealed-tx stream. On a sealed action that declares a side effect (`credentialIssuanceConfig`, notification, inbox write), it checks **"am I the entitled node?"** — i.e. *do I locally host the wallet responsible for this reaction* (issuer-wallet-host mints; recipient-wallet-host delivers/detects) — the same wallet-probe the current reconstructor uses. If entitled, it performs the side effect **once**, idempotent on `(sealedTxId, reactionKind)`.

- **Credential mint + deliver** becomes a reaction keyed on the issuing-action txId — **cannot double-issue** across nodes, restarts, or replay.
- **Inbound credential detection / wallet delivery** on the recipient node is a reaction on the same key.
- **Notifications / inbox writes** likewise.

Pure projection (state) and at-least-once side effects (reactions) are cleanly separated — credential issuance moves off the synchronous submit path entirely.

---

## 8. Presentation lifecycle reconciliation (F111 / F119)

Presentation-driven advancement (`PresentationOutcome`, `PresentationAbandoned`) is the one genuinely fiddly area: today it has its own seal-aware ordering coordinator (F119, `IPresentationSealCoordinator`) to avoid chain races. In the new model these transitions also carry a `RoutingDecision`, and the projection — being inherently **seal-ordered** — **subsumes** the coordinator's job (advance only on observed seal, in chain order). This phase migrates the presentation lifecycle onto the projection and retires the bespoke seal-ordering where the projection now guarantees it, keeping the `VAL_BP_003` carve-out semantics intact. Scoped as its own phase + risk, not hand-waved.

---

## 9. Clean break + CI gate

Pre-release, no migration. Delete outright and gate against reintroduction:

- `InstanceMirrorReconstructor`, `IsReadOnlyMirror`, `CreateMirrorAsync`/`UpdateMirrorAsync`.
- `ApplyInstanceStateChanges` imperative `CurrentActionIds` mutation.
- The `LocallyOwned` synchronous-vs-async branch in `ActionExecutionService`.
- The peer-topology ownership heuristic on the submit path (replaced by F108 roster).
- The singular `MetaData.NextActionId` hint (replaced by the full validated `RoutingDecision`).

`scripts/check-ledger-derived-clean-break.ps1` forbids their return (grep gate, like F135's clean-break gate).

---

## 10. Data flow — the AssuredIdentity loop in the new model

1. Citizen (n1) submits the starting action → tx carries `RoutingDecision{next:[2], SenderSigned}` → fan-out to tiny (roster sealer).
2. tiny's validator validates the decision (structural + sender-authz + chain) → seals. `instanceId = H(register, blueprint, startingTxHash)`.
3. Docket replicates to n1 **and** tiny → **both project** the instance to `CurrentActionIds:[2]` identically. No mirror.
4. Analyst (tiny) sees Action 2 as current — because tiny *projected* it (same projection n1 ran) — so `/api/actions/pending` surfaces it. Agent approves.
5. Analyst's Action 2 → `RoutingDecision{next:[3], SenderSigned}` → sealed → projection advances to `[3]`.
6. `ReactionDispatcher` on the **issuer-entitled** node mints the `AssuredIdentityCredential` (idempotent on the Action-2 txId) + the **recipient-entitled** node (n1) detects + delivers to the citizen wallet.
7. Citizen holds the credential. The autonomous agent worked end-to-end with no mirror and no manual approval.

---

## 11. Testing

- **Projection determinism** — same docket stream → identical instance state across nodes / arrival orders.
- **Rebuild-from-ledger parity** — materialized view == fresh `Rebuild()` replay (CI self-check).
- **Routing-decision validation** — validator rejects a structurally-invalid / mis-signed decision (`VAL_ROUTING_*`).
- **Full-set routing** — parallel-branch fan-out preserved end-to-end (regression for the singular-hint collapse).
- **Reaction idempotency** — duplicate / replayed seal → exactly one credential, one notification.
- **Cross-node E2E** — the AssuredIdentity loop with autonomous agent discovery working, no mirror, no manual approval.
- **Clean-break gate** — no references to mirror / `IsReadOnlyMirror` / `LocallyOwned` branch / singular hint.

---

## 12. Phasing (one feature, sequenced)

- **P1 — Routing decision as a carried, attested fact.** `RoutingDecision` + `Attestation` (v1 `SenderSigned`) model; submitter emits the full set; validator structural+authz validation (`VAL_ROUTING_*`); `routingAttestation` governance field (default `sender-signed`). Replaces the singular `NextActionId` hint.
- **P2 — Deterministic instance identity.** Born-at-first-action; `instanceId = H(...)`; creation = local draft; async response returns the ledger-derived id.
- **P3 — Instance projection + rebuild.** `InstanceProjector` (pure, every node), materialized view, `Rebuild()`, determinism invariants + parity check. Removes imperative mutation + the mirror.
- **P4 — Single async submission path.** One path, `202`, roster-based fan-out ownership (F108), bounded-wait convenience. Removes the `LocallyOwned` branch + the topology heuristic.
- **P5 — Reactions.** `ReactionDispatcher`; credential mint/deliver + notifications as idempotent role-gated reactions keyed on sealed txId. Removes inline issuance from the submit path.
- **P6 — Presentation-lifecycle migration.** Move F111/F119 advancement onto the projection; retire the bespoke seal-ordering where subsumed.
- **P7 — Clean-break removal + CI gate + caller migration.** Delete the dead paths; add the gate; migrate demo / walkthrough / UI callers to the eventual model; cross-node E2E green run.

---

## 13. Risks / constraints

- **Attestation evolution** is real work later (v2 control-plane disclosure, v3 ZK) but **out of scope for v1** — the seam + governance field are the only v1 obligations.
- **Presentation lifecycle (P6)** is the highest-complexity reconciliation; keep the `VAL_BP_003` carve-out and F119 idempotency semantics intact while the projection takes over ordering.
- **Determinism under encryption** — the projection's *state* needs only the validated decision (no payload); the projection's *data view* and *reactions* need disclosure access, so a node only materialises the data it can decrypt (consistent with today's disclosure model).
- **Caller migration** — UI / walkthroughs / the demo read the synchronous `nextActions`/`issuedCredential`; they move to subscribe-or-poll on instance-updated + credential events. Bounded-wait softens the single-node case.
- **Bounded-wait timeout tuning** — too long blocks HTTP; too short always 202s the owner case. ~2–3s default, configurable.

---

## 14. Out of scope (v1)

- v2/v3 attestations (validator re-evaluation, ZK proofs) — seam + governance field only.
- Register-governance UI for `routingAttestation` (the field exists; admin UX is later).
- Outbox-grade reaction delivery (at-least-once + idempotent is sufficient for now; a durable outbox/worker is a later hardening).
- Cross-installation anchor-set gossip / mesh routing (F143 deferred scope) — unchanged.
