# Peer NAT Traversal — Reverse-Stream Rendezvous (design)

- **Status:** ✅ Approved design — ready for speckit (`specify` → `plan` → `tasks`).
- **Date:** 2026-05-30
- **Unblocks:** [Assured Identity Demo Environment](2026-05-30-assured-identity-demo-environment-design.md) (parked).
- **Motivation in one line:** make a register-**owner** node behind NAT reachable
  by public subscribers, so the issuing authority can run on `tiny` (NAT'd) while
  testers/subscribers run on `n1` (public).

---

## Problem

Sorcha's peer protocol has the **subscriber initiate every cross-node
connection** — submit fan-out, docket pull, and live subscription are all
`subscriber → owner` (`TransactionDistributionService.cs:129`,
`RegisterReplicationService.cs:185` & `:526`, `PeerConnectionPool.cs:105`). The
owner's mempool is strictly local; **no path lets an owner pull an unsealed
transaction from a subscriber.** The practical rule:

> The owner/issuer node must be inbound-reachable by its subscribers.

That breaks the target topology: `tiny` (issuer/owner) is NAT'd (outbound-only:
public `81.111.103.112`, LAN `192.168.51.11`; no overlay), while `n1` is a public
Azure VM. `n1` cannot dial `tiny`, so it can neither submit to nor sync from a
`tiny`-owned register.

The only mechanism that ever addressed a NAT'd node — the central
`n0.sorcha.dev` **PeerRouter** relay — was **deliberately retired**
(`docker-compose.yml:531-533`) once "self-introduce via RegisterPeer" covered the
common `subscriber-dials-public-owner` direction (#353, #356). It was retired
because nobody needed a NAT'd *owner* — which is now genuinely required.

## Current state of the relay machinery (audit)

**HALF-WIRED:**

- ✅ **Client side LIVE** — a NAT'd peer already dials its seed and holds a
  persistent reverse duplex stream (`RegisterSyncBackgroundService` →
  `RelayCommunicationService.EstablishReverseStreamAsync`), with send paths,
  `CircuitBreaker`, a 20s safety-net poll (`RelayPollIntervalSeconds`), and unit
  tests. Routing of NAT'd peers via relay is already keyed off an empty `Address`
  in `CommunicationProtocolManager`.
- ❌ **Server side MISSING** — `PeerCommunicationServiceImpl` implements only the
  unary `SendMessage`, **not** the bidirectional `Stream` RPC defined in
  `peer_communication.proto:17`. Only the retired `Sorcha.PeerRouter`
  (`RouterCommunicationService`, 399 LOC, tested) ever implemented `Stream`. So
  today the topology is **spoke-only, no hub**: a NAT'd peer can dial out, but no
  peer-service can *accept* a reverse stream and broker over it.

The duplex transport + message vocabulary already exist: `PeerMessage` /
`Stream(stream PeerMessage) returns (stream PeerMessage)` with
`TRANSACTION_NOTIFICATION` (submit) and `REGISTER_SYNC_REQUEST/RESPONSE` +
`TRANSACTION_DATA_REQUEST/RESPONSE` (sync) message types.

## Architecture decisions (from the 2026-05-30 brainstorm)

1. **Rendezvous is a capability, folded into peer-service — not a separate node.**
   Any peer with a reachable public address is rendezvous-capable. The standalone
   `Sorcha.PeerRouter` is **retired for good** once parity lands. (Decided over
   reviving the dedicated node: avoids an always-on extra deployable; in our case
   `n1` is public *and* the subscriber, so it self-rendezvouses.)
2. **The NAT'd peer always dials out; the reverse stream is reused
   bidirectionally.** A rendezvous never dials a NAT'd peer — every
   rendezvous→NAT'd request travels back over the stream the NAT'd peer opened.
3. **Multi-anchor, propagated, latency-preferred — not seed-pinned.** A NAT'd peer
   holds reverse streams to *several* public peers; subscribers learn anchor sets
   from gossip (not static seed config); routing prefers the closest/fastest path.

## Core model

**Self-classification.** `PeerService:PublicAddress` set ⇒ *public /
rendezvous-capable*; empty ⇒ *NAT'd / spoke*. (Aligns with the existing
empty-`Address` relay keying. STUN-style auto-detection is deferred.)

**Spoke (NAT'd peer).** Dials out to the set of public peers it knows, opening a
persistent reverse duplex stream to each and registering "I am peer P, serving
registers […]" over it. Per-anchor reconnect/backoff/heartbeat (exists; extended
single→set).

**Hub (rendezvous-capable peer).** Implements server-side `Stream`; tracks active
reverse streams in a `ReverseStreamManager` (`peerId → live stream(s)`). When any
submit/sync request targets a peer reachable only via a reverse stream, the hub
forwards it over that stream and relays the correlated response back (correlation
framing already in `RelayCommunicationService` / `RelayMessageHandler`).

**Connection-direction matrix (the crux):**

| Flow | Who initiates the TCP/gRPC | Carried how |
|---|---|---|
| NAT'd peer establishes presence | **NAT'd peer → hub** (outbound) | persistent reverse `Stream` |
| Submit to NAT'd owner | subscriber → hub → owner | over owner's reverse stream |
| Sync from NAT'd owner | subscriber → hub → owner | over owner's reverse stream |
| Sealed-docket fan-out from NAT'd owner | owner → hub (already connected) | over reverse stream |

## Multi-anchor + latency-preferred routing

- **Multi-anchor resilience.** A NAT'd peer maintains reverse streams to multiple
  public peers; losing one anchor leaves the others. Reconnect/backoff per anchor.
- **Propagation, not seed-reliance.** The existing advert/heartbeat gossip is
  extended so a NAT'd peer advertises its **live anchor set**
  (`owner P reachable via [A, B, C]`). Subscribers build a routing table
  `peerId → [anchors]` from gossip; reaching P depends on whoever currently
  anchors P, not on static seed config.
- **Closest/fastest precedence.** `CommunicationProtocolManager` selects, in order:
  1. **Self-anchor (zero extra hop)** — if this subscriber is itself one of P's
     anchors (P dials it), reach P directly over the local reverse stream. *(Our
     demo case: `n1` anchors `tiny`, so `n1 → tiny` is one hop, no third party.)*
  2. **Lowest-latency remote anchor** — else pick the anchor with the best
     measured RTT (reuse heartbeat RTT), with `CircuitBreaker` failover to the
     next-best on failure.

## Component breakdown

Mostly porting retired-but-tested `RouterCommunicationService` logic into
peer-service, plus extending the live client side.

| # | Component | New / Extend | Reference to port |
|---|---|---|---|
| 1 | `Stream` server impl in `PeerCommunicationServiceImpl` — accept reverse streams, pump, handle disconnect | New | `RouterCommunicationService.Stream` |
| 2 | `ReverseStreamManager` (peer-service singleton) — `peerId → live stream(s)`, liveness, `DispatchAsync(peerId, msg)` + correlated response | New | PeerRouter `ReverseStreamEntry` / `RoutingEntry` |
| 3 | Rendezvous forwarding — route submit/sync targeting a reverse-stream-only peer via `ReverseStreamManager` instead of dialing | New wiring into `SendMessage` / distribute / sync-receive paths | PeerRouter `ForwardStreamMessageAsync` |
| 4 | Multi-anchor client — maintain reverse streams to the *set* of public peers; per-anchor reconnect/backoff/heartbeat | Extend `RelayCommunicationService` (single→set) | exists |
| 5 | Anchor advertisement — gossip live anchor set; subscribers build `peerId → [anchors]` | Extend advert/heartbeat payload + ingest | exists (advert pipeline) |
| 6 | Latency-preferred selection — self-anchor-first, else lowest-RTT, circuit-breaker failover | Extend `CommunicationProtocolManager` | exists (`CircuitBreaker`) |
| 7 | Rendezvous-enable config + reachability self-classification (`PublicAddress` ⇒ rendezvous) | New small config | exists (`PublicAddress`) |
| 8 | Retire `Sorcha.PeerRouter` — drop project, compose/wiring; migrate its tests | Remove | — |

## Trust & security (v1 boundary)

A rendezvous forwards **signed** transactions + dockets — integrity is
tamper-evident via signatures, and payloads are already field-encrypted, so a
rendezvous gains no plaintext it couldn't see as an ordinary peer. v1 assumes
peers sit in one federation trust domain.

**Explicitly out of scope (follow-ups):** relay-payload re-encryption; rendezvous
authorization / quotas; a malicious-rendezvous withholding/DoS threat model;
STUN-style NAT auto-detection.

## Observability

`Sorcha.Peer` meter: active reverse-stream count per rendezvous; relay-forward
latency histogram (tagged `flow=submit|sync`); anchor-selection outcomes
(`self|remote`); failover count; reconnect count.

## Testing & success criteria

**Unit** — `ReverseStreamManager` dispatch/correlation/liveness; selection prefers
self-anchor then lowest-RTT; failover on anchor drop. Adapt
`RouterCommunicationServiceRelayTests` → peer-service-as-rendezvous.

**Integration** — two in-proc peer-services, one "NAT'd" (no inbound) dials the
other; submit + sync brokered over the reverse stream; anchor-drop → reconnect →
recovery.

**E2E (gating)** — on the real `tiny`↔`n1` network: a transaction submitted on
`n1` reaches `tiny`'s validator and seals; the sealed docket syncs back to `n1`;
killing `tiny`'s reverse stream recovers automatically.

**Success criteria (verbatim for the spec):**

1. A NAT'd owner node receives forwarded action submissions and serves docket sync
   to a public subscriber, proven E2E across the real `tiny`↔`n1` network.
2. A NAT'd peer survives losing an anchor (reconnect + reroute) with no operator
   action.
3. With multiple anchors available, traffic prefers self-anchor, then lowest-RTT —
   verifiable from metrics.
4. `Sorcha.PeerRouter` is removed with no loss of capability.

## Key references

- Retired relay to port: `src/Apps/Sorcha.PeerRouter/GrpcServices/RouterCommunicationService.cs`,
  `Models/{ReverseStreamEntry,RoutingEntry}.cs`.
- Live client side to extend: `src/Services/Sorcha.Peer.Service/Communication/`
  (`RelayCommunicationService.cs`, `RelayMessageHandler.cs`,
  `CommunicationProtocolManager.cs`, `CircuitBreaker.cs`).
- Server side to add: `src/Services/Sorcha.Peer.Service/.../PeerCommunicationServiceImpl.cs`.
- Proto: `src/Services/Sorcha.Peer.Service/Protos/peer_communication.proto`.
- Submit/sync call sites: `TransactionDistributionService.cs`,
  `RegisterReplicationService.cs`, `RegisterSyncBackgroundService.cs`.
- Prior art / context: #353 (self-introduce), #356 (live-fallback),
  `specs/108-register-local-relationship/`, `specs/137-cross-node-submission/`.
