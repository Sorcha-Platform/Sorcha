# Phase 0 Research — Register State Aggregation & Local Relationship

**Feature**: `108-register-local-relationship`
**Date**: 2026-04-21

This document records decisions made for each technical unknown surfaced by the plan. Every decision records what was chosen, the rationale, and the alternatives evaluated. There were no `[NEEDS CLARIFICATION]` markers carried forward from the spec — the assumptions section in the spec settled the outstanding policy questions (staleness window, quorum rule, legacy fallback, multi-owner treatment, out-of-scope tunnel optimisation). This Phase 0 resolves the remaining *technical* unknowns.

---

## D1. Where is the `RegisterLocalRelationship` cache kept?

**Decision**: In-memory inside `Sorcha.Register.Service`, keyed by `registerId`. Invalidated when a docket containing a control transaction is sealed locally. Rebuilt lazily on next read. Not persisted.

**Rationale**: Relationship is derived entirely from the latest control record (read from Mongo) and local identity (static for the process lifetime). Writing it to Mongo introduces a staleness class we'd then have to invalidate; keeping it in-process means the cache and invalidation path are trivially consistent. Recomputation cost is small (scan attestations + roster, match against local wallet/key).

**Alternatives considered**:
- *Mongo-persisted derived field on the `Register` document.* Rejected: creates a denormalised copy that can drift if the invalidation hook ever misses a seal event. Provides no scale benefit at Sorcha's register cardinality.
- *Redis-cached per pod, shared across replicas.* Rejected: would only matter if Register.Service scaled to multiple replicas per node; even then, each replica derives deterministically from the same inputs, so shared cache is an optimisation for a scale point we haven't reached.

---

## D2. How does `Validator.Service` identify its own public key for roster matching?

**Decision**: Read via the existing `IValidatorWalletProvider` (or equivalent `SystemWalletProvider`) on startup and cache for the process lifetime. Key is derived under the existing `sorcha:docket-signing` purpose (established in Feature 086 / PR #311).

**Rationale**: This is the exact key the validator uses to sign dockets. Roster membership is a function of that key's public half matching a `ValidatorRosterEntry.PublicKey`. No alternative identifier is in play.

**Alternatives considered**:
- *Configuration-file-based validator ID string.* Rejected: would introduce a second source of truth that could diverge from the key actually used to sign. Matching by key is the only tamper-resistant rule.
- *Ask Wallet.Service over HTTP on every relationship query.* Rejected: hot-path cost, and the key doesn't change for the process lifetime — a cold cache miss on startup is sufficient.

---

## D3. How does `Register.Service` notify `Validator.Service` of a relationship change?

**Decision**: Redis pub/sub on a new channel `register:relationship-changed` with payload `{ registerId, changeVersion, addedRoles[], removedRoles[] }`. Validator.Service subscribes at startup. Reuses the same Redis infrastructure as the existing `RegisterEventBridgeService`.

**Rationale**: Redis pub/sub is already part of the platform (see `register:height-updated`, `docket:confirmed`, `transaction:confirmed` channels). Notifications must be fire-and-forget — Register.Service should not block on Validator.Service acknowledgement. At-most-once delivery is acceptable because Validator.Service also rebuilds its enrolment view from a direct `GetMyValidatedRegistersAsync` query on startup and can be polled on a slow safety loop (e.g. every 5 minutes) to heal any missed events.

**Alternatives considered**:
- *HTTP callback from Register to Validator.* Rejected: creates a reverse dependency (Register needs to know Validator's URL) that conflicts with the plan's "Register is authoritative, services query it" direction.
- *gRPC server-streaming from Register to Validator.* Rejected: adds a long-lived connection to manage for a rare event. Redis is simpler and already load-bearing for analogous signals.
- *Validator polling Register on a tick.* Rejected alone (too laggy); adopted as a secondary safety loop alongside pub/sub.

---

## D4. How are `PeerHeightObservation` records retained, and how many?

**Decision**: Per-register bounded ring buffer of the last 16 observations, in-memory inside Register.Service, keyed by `(registerId, sourcePeerId)` so each distinct peer occupies one slot (not a history). On advert ingest, upsert the entry for that peer. When the quorum rule evaluates, it looks at all entries whose `observedAt` is within the staleness window (default 60s).

**Rationale**: We never need historical peer heights — the state machine only cares about the *current* high-water-mark claim from each peer that has spoken recently. A per-peer-entry design is O(peers) in memory per register, cleaner to reason about for quorum ("distinct peers agreeing on a height"), and bounded by the number of peers a node talks to. 16 is a safe cap — multi-peer deployments typically converge on 3–5 source peers.

**Alternatives considered**:
- *Time-series ring per peer.* Rejected: we don't need the history.
- *Persist to Mongo alongside register.* Rejected: these are ephemeral operational signals, not authoritative state. Persistence would outlive their usefulness.
- *Publish adverts directly on Redis for anyone to consume.* Rejected: spreads the quorum logic across services. Centralising it in Register.Service means one code path owns the sync-state truth.

---

## D5. How are `ValidatorSealingObservation` records used?

**Decision**: Single overwriting slot per register (not a history). Records `{ lastSealedHeight, mempoolDepth, observedAt }`. Register.Service uses it to refine the `Caught-up` predicate: if local is the validator and mempool depth > 0 while local height matches network HWM, report `Caught-up` but with a `pendingSeal` soft flag so operators can see work is in progress.

**Rationale**: Pure-subscriber nodes never get a validator observation (nothing to report), and `Caught-up` for them means "height matches HWM". For owner-validator nodes, it matters for operators to see that the node is current but actively producing.

**Alternatives considered**:
- *Merge into `PeerHeightObservation` as self-observation.* Rejected: conflates remote claims with local authoritative progress.
- *Require validator to push every mempool-size change.* Rejected as too chatty; push on docket seal + a throttled mempool-count push (1 Hz max) is enough.

---

## D6. How does `Peer.Service` decide where to forward a submission for a register the local node doesn't own?

**Decision**: Target all peers in `RegisterSubscription.SourcePeerIds` for that register. If the local node is a subscriber, at least one of those peers is by construction a holder of the authoritative chain — and therefore either the owner's validator or a validator peer. If multiple source peers are known, fan out to all (idempotent: duplicate submissions are deduped downstream by sequence number and signature match).

**Rationale**: Source peers are the peers we pull dockets from — by definition they have the register's authoritative state and are reachable. We already maintain this list in Peer.Service SQL. No new discovery needed.

**Alternatives considered**:
- *Single "canonical owner" lookup that parses the control record.* Rejected: couples peer routing to control-record parsing, introduces a cross-service call on the hot path, and "owner DID → peer address" is not a mapping we maintain (ownership is a cryptographic assertion, not a routing identity).
- *Broadcast to all known peers (full gossip).* Rejected: wasteful and noisy for the targeted "subscriber → owner" case. Keep generic gossip as a future enhancement if needed.
- *gRPC-tunnel through the heartbeat reverse-stream.* Rejected for v1 (explicit non-goal in spec). Revisit if HTTP-path latency is insufficient.

---

## D7. What wire format for submission forwarding — HTTP POST to API gateway, or gRPC to Peer.Service?

**Decision**: gRPC to the target peer's existing `TransactionDistributionGrpcService`. The service already accepts `TransactionNotification` messages; we extend it with a `SubmitTransaction` unary RPC that carries the full `TransactionSubmission` payload (the existing distribution flow only notifies hash + metadata; for owner-forwarding we need the whole thing because the receiver has never seen it before). Uses the existing peer-to-peer mTLS channel — same auth story as existing pulls.

**Rationale**: We already have an open, authenticated, NAT-traversing outbound gRPC channel from every subscriber to every source peer (used by `RegisterReplicationService` and `PeerHeartbeatService`). Reusing it for forward-submission costs no new connectivity. HTTP through the API gateway would require the subscriber to know the owner's public HTTPS URL (we don't track that separately from the gRPC host:port) and would bounce auth through the service-to-service JWT with a shared signing key assumption — strictly more surface for the same outcome.

**Alternatives considered**:
- *HTTP POST to `https://{ownerHost}/api/validator/v1/transactions/validate` via API gateway.* Rejected per above — extra auth surface, extra URL-convention assumption (port 443 API gateway with same hostname as gRPC). Spec's original sketch called this out as an option; deeper exploration showed gRPC-channel-reuse is simpler for the same delivery guarantee.
- *Enqueue into local peer-service queue and let the existing relay fallback carry it.* Rejected: relay was retired (`n0.sorcha.dev` gone) and the existing `TransactionDistributionService.SendToPeerAsync` only delivers `TransactionNotification` (hash + metadata), not the full payload.

---

## D8. What happens to the existing (currently-unwired) `TransactionDistributionService` gossip hash/fetch pattern?

**Decision**: Leave it in place but unused by this feature. Add the new `SubmitTransaction` gRPC method alongside the existing `NotifyTransaction`. The new method is the owner-forward path; the old notification/hash+fetch flow is preserved for a future gossip-at-scale design where bandwidth matters more than latency.

**Rationale**: Removing the existing service now would be scope creep. The two paths serve different goals — notify-and-pull is good for broadcast-to-many; direct-submit is good for subscriber-to-owner. Both can coexist.

**Alternatives considered**:
- *Delete `TransactionDistributionService`.* Rejected: documented non-goal; preserves optionality.
- *Repurpose `NotifyTransaction` to carry full payload.* Rejected: changes semantics of an existing contract that has partial integration in `GossipProtocolEngine`.

---

## D9. How does `ActionExecutionService` actually "call both"?

**Decision**: After signing, issue the two calls concurrently with `Task.WhenAll`:
1. `_validatorClient.SubmitTransactionAsync(submission, ct)` — goes into local mempool. Local validator seals iff it's on the roster (from D2 + `IRegisterMonitoringRegistry` populated by bootstrap).
2. `_peerClient.DistributeTransactionAsync(registerId, submission, ct)` — peer service fans out to source peers per D6; no-op for a locally-owned register because `SourcePeerIds` is empty.

Await both; report success if either returned success. The local validator returns fast if it can't seal (it accepts the tx into the pool but won't produce a docket — no error). The peer distribution returns fast if there are no targets.

**Rationale**: Fan-out is fire-and-forget-ish — the caller primarily cares that *somebody* will seal it. The downstream `WaitForTransactionConfirmationAsync` polls the local Register.Service for tx-with-docket-number; once the owner seals and replication pulls the docket back (PR #357 path), the local register shows it confirmed. One success suffices.

**Alternatives considered**:
- *Sequential calls (validator first, then peer).* Rejected: adds latency on the common case.
- *Only call peer if `RegisterLocalRelationship.IsOwner == false`.* Rejected: reintroduces ownership branching in Blueprint.Service. The peer client is cheap; let it decide based on `SourcePeerIds`.
- *Single "submit-anywhere" orchestrator endpoint.* Rejected as overkill for two concurrent calls; would be worth it if a third channel appeared (e.g. a background queue for offline mode).

---

## D10. How is the `Register.SyncState` string-to-enum migration handled for existing data?

**Decision**: The current `SyncState` field is `string?` with small known value set (`null`, `"Subscribing"`, `"Syncing"`, `"Synced"`, `"Error"`). At read time, map via a converter to the new enum:
- `null` → `Indeterminate` (no remote sync info yet — a locally-created register is `Caught-up` as soon as it has peer observations or at-startup with `lastAdvertSource == self`; but starting from `Indeterminate` and letting the resolver promote it is safe).
- `"Subscribing"` / `"Syncing"` → `Syncing`.
- `"Synced"` → `Caught-up`.
- `"Error"` → `Error`.
Write path stores the new enum. Existing docs are migrated opportunistically on next write of the register document — no big-bang migration. Admin UI consumers of the old string are updated to read the new enum property name.

**Rationale**: In-place migration on write avoids a Mongo bulk job and works correctly for a read-heavy dataset. The new enum value is the authoritative field going forward; the old string lingers in older docs until they're touched, but all code paths read through the converter.

**Alternatives considered**:
- *Bulk Mongo migration script on service startup.* Rejected: a migration that has to re-run safely on crash-restart is more operational surface than the lazy approach warrants, given the value set is enumerable.
- *Keep both the string and a new enum column and dual-write.* Rejected: guaranteed to drift; enums are the canonical, string is deprecated and dropped once all reads have moved.

---

## D11. What's the shape of the `my-validated-registers` query?

**Decision**: `GET /api/internal/my-validated-registers` on Register.Service, service-to-service authenticated, returns `{ registerIds: string[] }`. Implementation scans local `Register` collection, for each register derives `RegisterLocalRelationship` for the caller-provided validator public key (passed via a header `X-Validator-Public-Key`, matching the way Validator.Service identifies itself elsewhere in this plan). Returns registers where `IsValidator == true`.

**Rationale**: Validator.Service identifies itself with its public key; Register.Service matches that key against the roster in each register's control record. Centralises the matching logic in one place (D2).

**Alternatives considered**:
- *Broadcast the roster of every register to every service.* Rejected: unnecessary data fan-out; the question "which of my registers is this key on?" is naturally a Register.Service query.
- *Validator.Service pre-scans registers on startup by reading Mongo directly.* Rejected: breaks the microservices boundary — Validator shouldn't talk to Register's store.

---

## D12. Ordering guarantees on relationship-change events vs docket seals

**Decision**: Register.Service's docket-seal event handler first writes the docket (existing path), then — if the docket contained a control transaction — invalidates the cached relationship and publishes the `register:relationship-changed` event. This ordering guarantees that any service that consumes the event and subsequently queries `/api/registers/{id}/local-relationship` gets the new state.

**Rationale**: Write-then-notify is the standard pattern for "thing changed" pub/sub. Doing it in the other order creates a race where the consumer fetches stale state.

**Alternatives considered**:
- *Notify-then-write.* Rejected for the race above.
- *Transaction / outbox pattern across Mongo + Redis.* Rejected as over-engineering at the current scale; a best-effort in-process hook is sufficient because consumers also poll on a safety loop (D3).

---

## Summary

All Phase-0 decisions resolved. No open `NEEDS CLARIFICATION` items. Ready for Phase 1 design (data-model, contracts, quickstart).
