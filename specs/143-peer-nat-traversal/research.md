# Phase 0 Research: Peer NAT Traversal

All decisions below were resolved during the brainstorm + two code-investigation
passes (peer protocol direction; relay-machinery maturity/wiring). No open
`NEEDS CLARIFICATION` remain. Citations are to current `master`.

## R-001 — Connection-direction reality (the core constraint)

- **Decision**: Treat "the subscriber initiates every cross-node connection" as a
  fixed invariant of the current protocol, and make NAT traversal work *within* it
  by having the NAT'd node dial out and the rendezvous reuse that connection.
- **Rationale**: Verified that submit fan-out, docket pull, and live subscription
  are all subscriber→owner outbound: `TransactionDistributionService.cs:129`
  (`TransactionDistribution.SubmitTransaction`), `RegisterReplicationService.cs:185`
  (`PullDocketChain`) & `:526` (`SubscribeToRegister`), `PeerConnectionPool.cs:105`
  (bootstrap dial). The owner's mempool is strictly local — **no owner-pull of an
  unsealed transaction exists**. So a NAT'd owner is unreachable unless it opens
  the connection itself. NAT blocks only the *initiating* direction; a duplex
  stream opened by the NAT'd node carries traffic both ways.
- **Alternatives rejected**: (a) invert topology so the NAT'd node is the
  subscriber — works today but forces testers onto the NAT'd box, rejected by the
  operator; (b) Tailscale/WireGuard overlay — works, but an external infra
  dependency the operator wanted to avoid in-protocol; (c) router port-forwarding
  — fragile, requires router access, breaks on IP change.

## R-002 — Rendezvous home: fold into peer-service (not a dedicated node)

- **Decision**: Any peer with a public address is rendezvous-capable; the
  rendezvous logic lives in peer-service. Retire the standalone `Sorcha.PeerRouter`.
- **Rationale**: The old central `n0.sorcha.dev` PeerRouter was **deliberately
  retired** (`docker-compose.yml:531-533`, #353) because "self-introduce via
  RegisterPeer" covered the common subscriber-dials-public-owner case. Reviving a
  separate always-on node re-adds the very dependency that was removed. In our
  topology n1 is both public and the subscriber, so it self-rendezvouses with no
  third node. Folding in also reuses the existing in-peer-service `Communication/`
  client layer.
- **Alternatives rejected**: revive dedicated PeerRouter (extra deployable);
  hybrid peer + optional standalone (more surface area for no v1 benefit).

## R-003 — Current relay-machinery state: HALF-WIRED

- **Decision**: Build the **missing server side**; reuse/extend the **live client
  side**; port the retired `RouterCommunicationService` logic.
- **Rationale (audit)**:
  - ✅ Client LIVE — `RegisterSyncBackgroundService` starts
    `RelayCommunicationService.EstablishReverseStreamAsync` when the relay service
    is injected (it is, as a required singleton: `Program.cs:141,155-157`); send
    paths, `CircuitBreaker`, 20s `RelayPollIntervalSeconds` safety poll, and unit
    tests exist. NAT'd routing keys off empty `Address` in
    `CommunicationProtocolManager`.
  - ❌ Server MISSING — `PeerCommunicationServiceImpl` (59 LOC) implements only
    unary `SendMessage`; it does **not** override the bidirectional `Stream` RPC
    (`peer_communication.proto:17`). Only the retired
    `RouterCommunicationService.Stream` (399 LOC) ever implemented it, backed by
    `ReverseStreamManager` (115 LOC) + `RoutingTable` + `RouterChannelPool`.
- **Port targets**: `RouterCommunicationService.Stream` + `RelayViaReverseStreamAsync`
  + `ForwardStreamMessageAsync` → into `PeerCommunicationServiceImpl`;
  `Sorcha.PeerRouter/Services/ReverseStreamManager.cs` + `Models/ReverseStreamEntry.cs`
  → into `Sorcha.Peer.Service/Communication/` (adjust namespaces; the proto types
  are already `Sorcha.Peer.Service.Protos`).

## R-004 — Reachability self-classification

- **Decision**: `PeerService:PublicAddress` set ⇒ public / rendezvous-capable;
  empty ⇒ NAT'd / spoke. Explicit, not auto-detected.
- **Rationale**: Aligns with the existing empty-`Address` relay keying
  (`CommunicationProtocolManager`) and `PeerService__PublicAddress` env
  (`docker-compose.yml:528`). STUN-style auto-detection adds complexity for no v1
  value and is listed Out of Scope.
- **Alternatives rejected**: STUN/auto-probe; inferring from connection acceptance.

## R-005 — Multi-anchor + propagation + latency-preferred routing

- **Decision**: A NAT'd node holds reverse streams to a *set* of public peers;
  the live anchor set is propagated via existing advert/heartbeat gossip into a
  per-node routing table; senders select **self-anchor → lowest-RTT remote anchor
  → failover** (reusing heartbeat RTT and the existing `CircuitBreaker`).
- **Rationale**: Operator requirement — most resilient, multi-seed-capable, not
  seed-pinned, "closest/fastest wins." Self-anchor (sender is itself an anchor of
  the target) is a zero-extra-hop direct write over the local reverse stream — our
  n1→tiny case. RTT is already measured by heartbeats; no new probe needed.
- **Alternatives rejected**: single fixed seed (not resilient); static priority
  (not adaptive); geographic/hop-count (no data, no benefit over RTT).

## R-006 — Correctness under reroute

- **Decision**: Rely on existing chain-integrity + dedupe + replay protection;
  add no new sealing semantics. Failover/retry must be idempotent at the transport
  layer (a re-sent submission dedupes downstream; `VAL_CHAIN_FORK` already dedupes
  duplicate transactions).
- **Rationale**: This feature changes transport reachability only (FR-011). The
  validator already tolerates duplicate submissions; the relay must not invent new
  duplication beyond what's already idempotent.
- **Alternatives rejected**: transport-level exactly-once (unnecessary given
  downstream idempotency; high complexity).

## R-007 — Observability

- **Decision**: Extend the `Sorcha.Peer` meter: `active reverse streams` (gauge,
  per rendezvous), `relay forward latency` (histogram, tag `flow=submit|sync`),
  `path selection outcome` (counter, tag `path=self|remote`), `failover count`,
  `reconnect count`. OTel span around brokered forward.
- **Rationale**: FR-012 + SC-004 require routing/health be verifiable from metrics
  without code inspection. Reuses the existing meter + export allowlist.

## R-008 — Trust boundary (v1)

- **Decision**: Rendezvous is trusted transport within one federation trust domain;
  integrity from signatures (FR-013). Defer payload re-encryption, rendezvous
  authz/quotas, malicious-rendezvous threat model.
- **Rationale**: Matches the spec's Out of Scope; signatures already make tampering
  tamper-evident; v1's goal is reachability, not adversarial-relay hardening.
