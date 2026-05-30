# Implementation Plan: Peer NAT Traversal (Reverse-Stream Rendezvous)

**Branch**: `143-peer-nat-traversal` | **Date**: 2026-05-30 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/143-peer-nat-traversal/spec.md`
**Authoritative design**: `docs/superpowers/specs/2026-05-30-peer-nat-traversal-design.md`

## Summary

Make a register-**owner** node behind NAT reachable by public subscribers by
**folding the reverse-stream rendezvous capability into the peer service** and
**retiring the standalone `Sorcha.PeerRouter`**. A NAT'd node dials out and holds a
persistent reverse duplex `PeerCommunication.Stream` to one or more public peers;
a public peer (the rendezvous, e.g. n1) accepts that stream and brokers
submission + sync requests back over it. Reachability is propagated through
existing peer gossip as a per-NAT'd-node **anchor set**; senders route by
**self-anchor first, then lowest-RTT, with circuit-breaker failover.** The
work is dominated by porting the retired-but-tested `RouterCommunicationService`
(server-side `Stream` + `ReverseStreamManager`) into peer-service and extending
the already-live client side (`Communication/`) from single-seed to multi-anchor.

## Technical Context

**Language/Version**: C# 14 / .NET 10
**Primary Dependencies**: Grpc.Net 2.71 (existing `PeerCommunication` service + `peer_communication.proto`), existing `Sorcha.Peer.Service/Communication/` relay client layer, existing advert/heartbeat gossip, OpenTelemetry 1.12
**Storage**: None new — reverse-stream registry and routing table are in-memory (process-local). No EF migration. Not on the F113 storage-audit list.
**Testing**: xUnit + FluentAssertions + Moq; existing `RelayCommunicationServiceTests`, `RelayMessageHandlerTests`, `RouterCommunicationServiceRelayTests` (to migrate); a two-peer in-proc integration harness; a real tiny↔n1 E2E gate
**Target Platform**: Linux containers (peer-service), cross-node over real networks
**Project Type**: single (changes localised to `src/Services/Sorcha.Peer.Service` + removal of `src/Apps/Sorcha.PeerRouter` + tests)
**Performance Goals**: Cross-node submit/sync against a NAT'd owner within the same order-of-magnitude latency as against a public owner (SC-006); no pathological slowdown from the rendezvous hop
**Constraints**: NAT'd node has outbound-only connectivity; rendezvous never dials the NAT'd node; correctness (chain integrity, dedupe, replay protection) unchanged
**Scale/Scope**: v1 target topology — 1 NAT'd owner (tiny) + ≥1 public rendezvous/subscriber (n1); design supports multiple anchors and multiple subscribers

No `NEEDS CLARIFICATION` — the approved design resolved all open questions.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|---|---|---|
| I. Microservices-First | ✅ | Capability folded into the existing peer-service; **removes** a service (`Sorcha.PeerRouter`) → less coupling, fewer deployables. No upward deps. |
| II. Security First | ✅ | No new secrets. Integrity preserved by existing transaction/docket signatures (FR-013). Relay-payload re-encryption + rendezvous authz explicitly deferred (documented Out of Scope). |
| III. API Documentation | ✅ (n/a-ish) | No new public REST. gRPC `Stream` already in the proto; XML docs on new public types. No Scalar surface change. |
| IV. Testing | ✅ | Unit (ReverseStreamManager, selection, failover) + integration (two in-proc peers) + E2E gate. ≥85% on new code. Migrate existing relay tests. |
| V. Code Quality | ✅ | async/await, DI, nullable enabled, no Release warnings. Matches existing peer-service patterns. |
| VI. Blueprint Standards | ✅ n/a | No blueprint changes. |
| VII. DDD / ubiquitous language | ✅ | Reuses owner/subscriber/register/docket/peer. New terms (rendezvous, anchor, reverse stream) are transport-layer, documented. |
| VIII. Observability | ✅ | New `Sorcha.Peer` meter instruments (FR-012): active reverse streams, brokered latency by flow, path-selection outcome, failover/reconnect counts. Structured logging, no interpolation. |

**Gate: PASS.** No violations → Complexity Tracking section omitted.

## Project Structure

### Documentation (this feature)

```text
specs/143-peer-nat-traversal/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output (gRPC + internal interfaces)
└── tasks.md             # Phase 2 output (/speckit.tasks — NOT created here)
```

### Source Code (repository root)

```text
src/Services/Sorcha.Peer.Service/
├── GrpcServices/
│   └── PeerCommunicationServiceImpl.cs      # ADD server-side Stream override (port from RouterCommunicationService)
├── Communication/
│   ├── ReverseStreamManager.cs              # NEW (port from Sorcha.PeerRouter/Services/ReverseStreamManager.cs)
│   ├── RelayCommunicationService.cs         # EXTEND single-seed → multi-anchor (set of reverse streams)
│   ├── CommunicationProtocolManager.cs      # EXTEND path selection: self-anchor → lowest-RTT → failover
│   ├── RelayMessageHandler.cs               # reuse (incoming relayed submit/sync dispatch)
│   └── CircuitBreaker.cs                     # reuse (failover)
├── Replication/
│   ├── RegisterReplicationService.cs        # relay sync send paths (reuse/verify)
│   └── RegisterSyncBackgroundService.cs     # reverse-stream establishment (extend to anchor set)
├── Discovery|Advertisement/ (existing)
│   └── advert/heartbeat models              # EXTEND to carry per-node anchor set; ingest → routing table
├── Models/
│   └── ReverseStreamEntry.cs, RoutingPreference.cs   # NEW (port + selection model)
├── Protos/peer_communication.proto          # reuse Stream + message types (no wire change expected)
└── Program.cs / Extensions                   # DI for ReverseStreamManager; rendezvous-enable config; map Stream

tests/Sorcha.Peer.Service.Tests/
├── unit/        # ReverseStreamManager, path selection, failover, anchor advert ingest
├── integration/ # two in-proc peer-services: one "NAT'd" dials the other; submit+sync; anchor-drop→recover
└── (migrate)    # RouterCommunicationServiceRelayTests → peer-service-as-rendezvous

# REMOVED
src/Apps/Sorcha.PeerRouter/                    # delete project + sln ref + compose/wiring (US4 / FR-014)
docker-compose.yml                             # peer-service rendezvous-enable env; drop dead PeerRouter refs
```

**Structure Decision**: Single existing service. All new code lands in
`Sorcha.Peer.Service`; the standalone `Sorcha.PeerRouter` app is deleted once its
`Stream`/`ReverseStreamManager` logic reaches parity inside peer-service. No new
project, no new datastore.

## Complexity Tracking

No constitution violations — section intentionally omitted.
