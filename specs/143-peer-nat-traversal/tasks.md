---
description: "Task list for Peer NAT Traversal (Reverse-Stream Rendezvous)"
---

# Tasks: Peer NAT Traversal (Reverse-Stream Rendezvous)

**Input**: Design documents from `/specs/143-peer-nat-traversal/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/

**Tests**: INCLUDED — the constitution mandates ≥85% coverage on new code, and cross-node correctness is the whole point of the feature.

**Organization**: Tasks grouped by user story. US1 is the MVP; US2–US4 layer on US1's rendezvous core (noted in Dependencies).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on incomplete tasks)
- All paths are repository-relative.

## Path Conventions

- Service: `src/Services/Sorcha.Peer.Service/`
- Tests: `tests/Sorcha.Peer.Service.Tests/`
- Retired app: `src/Apps/Sorcha.PeerRouter/`

---

## Phase 1: Setup

- [X] T001 Establish baseline: build `src/Services/Sorcha.Peer.Service` and run `tests/Sorcha.Peer.Service.Tests` green before any change (capture current pass count).
- [X] T002 [P] Add NAT-traversal config surface in `src/Services/Sorcha.Peer.Service` options: reachability self-classification (`PeerService:PublicAddress` empty ⇒ NAT'd/spoke; set ⇒ public/rendezvous-capable) and `PeerService:Relay:RendezvousEnabled` (default derived from `PublicAddress`). See contracts/internal-interfaces.md → Configuration.

---

## Phase 2: Foundational (Blocking Prerequisites)

**⚠️ CRITICAL**: All user stories depend on the reverse-stream registry + metrics. No story work begins until this phase is complete.

- [X] T003 Port `ReverseStreamEntry` into `src/Services/Sorcha.Peer.Service/Communication/ReverseStreamEntry.cs` (from `src/Apps/Sorcha.PeerRouter/Models/ReverseStreamEntry.cs`; adjust namespace; proto types already `Sorcha.Peer.Service.Protos`).
- [X] T004 Port + extend `ReverseStreamManager` into `src/Services/Sorcha.Peer.Service/Communication/ReverseStreamManager.cs` (from `src/Apps/Sorcha.PeerRouter/Services/ReverseStreamManager.cs`): `RegisterStream`/`TryGetStream`/`RemoveStream` + NEW `DispatchAsync(peerId, msg, ct)` (throws `RpcException(Unavailable)` on no live stream) + NEW `ActiveCount`.
- [X] T005 Register `ReverseStreamManager` as a singleton in `src/Services/Sorcha.Peer.Service/Program.cs` DI.
- [X] T006 [P] Add `Sorcha.Peer` meter instruments in a metrics type under `src/Services/Sorcha.Peer.Service/`: `peer_reverse_streams_active` (gauge), `peer_relay_forward_duration` (histogram, tag `flow`), `peer_path_selection_total` (counter, tag `path`), `peer_anchor_failover_total`, `peer_anchor_reconnect_total`. (Emission wired per story.)
- [X] T007 [P] Unit tests for `ReverseStreamManager` in `tests/Sorcha.Peer.Service.Tests/Communication/ReverseStreamManagerTests.cs`: register, replace/supersede (old CTS cancelled), remove, `DispatchAsync` on missing stream → `Unavailable`, `ActiveCount`.

**Checkpoint**: registry + config + metrics ready.

---

## Phase 3: User Story 1 - Run a register-owning node behind NAT (Priority: P1) 🎯 MVP

**Goal**: A NAT'd owner dials out, holds a reverse stream to a public rendezvous, and the rendezvous brokers submit + docket-sync to it so a public subscriber can use the register.

**Independent Test**: Two peers — `pub` (PublicAddress set) and `natd` (PublicAddress empty, seed=pub). Submit an action on `pub` against `natd`'s register → it seals on `natd` and the docket replicates back to `pub`.

### Tests for User Story 1 ⚠️ (write first, ensure they fail)

- [X] T008 [P] [US1] Port/adapt `Stream` server tests → `tests/Sorcha.Peer.Service.Tests/GrpcServices/PeerCommunicationStreamTests.cs` (from `RouterCommunicationServiceRelayTests`): first-message registers stream; missing `sender_peer_id` ⇒ `InvalidArgument`; reconnect supersedes; inbound forward to recipient stream.
- [ ] T009 [P] [US1] Integration test `tests/Sorcha.Peer.Service.Tests/Integration/NatTraversalSubmitTests.cs`: `natd` (no inbound) establishes reverse stream to `pub`; submit on `pub` for `natd`'s register → brokered → sealed on `natd`.
- [ ] T010 [P] [US1] Integration test `tests/Sorcha.Peer.Service.Tests/Integration/NatTraversalSyncTests.cs`: sealed docket on `natd` replicates back to `pub` over the reverse stream (docket chain + tx data).

### Implementation for User Story 1

- [X] T011 [US1] Implement server-side `Stream` override in `src/Services/Sorcha.Peer.Service/GrpcServices/PeerCommunicationServiceImpl.cs` (port `RouterCommunicationService.Stream`): accept reverse stream, register on first message via `ReverseStreamManager`, update `LastActivityAt`, clean teardown on disconnect/cancel. Gate on rendezvous-enabled.
- [ ] T012 [US1] Implement inbound forwarding (port `ForwardStreamMessageAsync`) in `PeerCommunicationServiceImpl`: messages from the NAT'd peer carrying a `recipient_peer_id` → recipient's reverse stream, else direct channel, else log+drop.
- [X] T013 [US1] Wire SUBMIT brokering in `src/Services/Sorcha.Peer.Service/Communication/CommunicationProtocolManager.cs` + `Replication/TransactionDistributionService.cs`: a `TRANSACTION_NOTIFICATION` targeting a reverse-stream-only peer is sent via `ReverseStreamManager.DispatchAsync` instead of dialing.
- [X] T014 [US1] Wire SYNC brokering in `src/Services/Sorcha.Peer.Service/Replication/RegisterReplicationService.cs`: `REGISTER_SYNC_REQUEST/RESPONSE` + `TRANSACTION_DATA_REQUEST/RESPONSE` carried over the reverse stream via `DispatchAsync`, correlation-matched (reuse `RelayMessageHandler`).
- [X] T015 [US1] Map the `Stream` gRPC endpoint and enable rendezvous when `PublicAddress` is set, in `src/Services/Sorcha.Peer.Service/Program.cs`.
- [X] T016 [US1] Emit `peer_reverse_streams_active` (on register/remove) and `peer_relay_forward_duration{flow=submit|sync}` (around `DispatchAsync`) with an OTel `peer.relay.forward` span.

**Checkpoint**: reverse-stream server + rendezvous send-path routing (notification + sync) work.
SC-001 (sealing through a NAT'd owner) is **NOT** yet reachable — see Phase 3b.

> ⚠️ **Discovered scope (during T013/T014 implementation).** The send-path routing brokers the
> *pull/notify* relay messages over a held reverse stream (sync-back from a NAT'd owner works).
> But the **submission-for-sealing** path — `TransactionDistributionService.ForwardSubmissionAsync`
> → `TransactionDistribution.SubmitTransaction` (full signed tx → owner's local validator) — uses
> **direct gRPC channels only**; it has no relay message type and never traverses a reverse stream.
> On a node with no seeds it returns `LocallyOwned: true` and skips fan-out. So a transaction
> submitted on a public subscriber currently **cannot reach a NAT'd owner's validator to be sealed**.
> The owner-side ingest logic exists (`TransactionDistributionGrpcService.SubmitTransaction:248-310`,
> hands the tx to `IValidatorServiceClient`); only the relay **transport** for it is missing.
> Closing this is **Phase 3b (US1b)** below and is a prerequisite for SC-001.

---

## Phase 3b: User Story 1b - Submit-for-sealing to a NAT'd owner over relay (Priority: P1) 🎯 MVP-completing

**Goal**: A signed transaction submitted on a public subscriber reaches a NAT'd owner's validator
and seals — the sealing transport over the reverse stream that the pull/notify path already has.

**Independent Test**: in-proc `pub` + `natd`-owner; submit on `pub` → `natd`'s validator-submit
handler is invoked with the forwarded submission and returns an ack over the reverse stream.

- [X] T038 [P] [US1b] Add relay message types `SUBMIT_TRANSACTION_REQUEST` (=12) and `SUBMIT_TRANSACTION_RESPONSE` (=13) to `Protos/peer_communication.proto`; add matching DTOs in `Communication/Models/RelayMessages.cs` (RegisterId, SubmissionJson, OriginPeerId, CorrelationId; response: Accepted, RejectReason, CorrelationId).
- [X] T039 [US1b] Owner-side handler in `RelayMessageHandler`: on `SUBMIT_TRANSACTION_REQUEST`, resolve `IValidatorServiceClient` via scope and submit (mirror `TransactionDistributionGrpcService.SubmitTransaction`), then send `SUBMIT_TRANSACTION_RESPONSE` back via `SendViaRelayAsync`; on `SUBMIT_TRANSACTION_RESPONSE`, complete the correlation.
- [X] T040 [US1b] Rendezvous-side routing in `TransactionDistributionService.ForwardSubmissionAsync`: when the register's owner is reachable only via a held reverse stream (no direct channel/seed), broker the submission via `RelayCommunicationService.SendAndWaitAsync<SubmitResponse>(ownerPeerId, SUBMIT_TRANSACTION_REQUEST, …)` instead of returning `LocallyOwned: true`. Needs register→owner→reverse-stream resolution (a `GetReverseStreamOwnerForRegister` analog of `GetChannelsForRegister`, fed by adverts).
- [X] T041 [US1b] Integration test: submit on `pub` for `natd`-owned register → brokered `SUBMIT_TRANSACTION_REQUEST` reaches `natd`'s validator-submit path and acks. (Then T009/T010 can prove the full SC-001 loop.)

**Checkpoint**: SC-001 reachable on the in-proc harness.

---

## Phase 4: User Story 2 - Stay connected when a path drops (Priority: P2)

**Goal**: A NAT'd node holds reverse streams to multiple public anchors and auto-recovers a dropped path with no operator action.

**Independent Test**: With `natd` anchored to two public peers, sever one anchor → submit/sync continue over the survivor; the severed anchor auto-restores.

### Tests for User Story 2 ⚠️

- [ ] T017 [P] [US2] Integration test `tests/Sorcha.Peer.Service.Tests/Integration/MultiAnchorResilienceTests.cs`: two anchors; sever one → submit + sync continue on survivor; severed anchor reconnects automatically.
- [ ] T018 [P] [US2] Integration test `tests/Sorcha.Peer.Service.Tests/Integration/AnchorLossRecoveryTests.cs`: lose all anchors → sends fail explicitly (no hang) → reconnect → resume (FR-010).

### Implementation for User Story 2

- [ ] T019 [US2] Extend `src/Services/Sorcha.Peer.Service/Communication/RelayCommunicationService.cs`: single reverse stream → a keyed set of `Anchor`s (per-anchor connection state + last-heartbeat RTT). Expose `Anchors`.
- [ ] T020 [US2] Extend `src/Services/Sorcha.Peer.Service/Replication/RegisterSyncBackgroundService.cs` to establish reverse streams to the full anchor set (not just one seed).
- [ ] T021 [US2] Per-anchor reconnect/backoff + heartbeat keep-alive; recover a dropped anchor without operator action; emit `peer_anchor_reconnect_total`. (extend existing reconnect logic in `RelayCommunicationService`.)
- [ ] T022 [US2] Explicit-fail send path when no anchor is available (surface error, do not hang) and resume on reconnect, in `RelayCommunicationService` / `CommunicationProtocolManager`.

**Checkpoint**: SC-002 + SC-003 — resilience with zero operator action.

---

## Phase 5: User Story 3 - Use the closest/fastest path (Priority: P3)

**Goal**: Anchor sets are gossiped; senders route self-anchor → lowest-RTT → failover, not seed-pinned.

**Independent Test**: `natd` reachable via two public anchors of differing RTT (one being the requester); routing/metrics show self-anchor used when the requester is an anchor, otherwise lowest-RTT, with failover.

### Tests for User Story 3 ⚠️

- [ ] T023 [P] [US3] Unit test `tests/Sorcha.Peer.Service.Tests/Communication/PathSelectionTests.cs`: prefers self-anchor; else lowest-RTT; fails over to next-best on circuit-breaker open; never selects a NAT'd target via direct address.
- [ ] T024 [P] [US3] Test `tests/Sorcha.Peer.Service.Tests/Integration/AnchorAdvertConvergenceTests.cs`: anchor-set advert ingested into routing table; stale anchor pruned within one advert/heartbeat cycle.

### Implementation for User Story 3

- [ ] T025 [US3] Extend the advert/heartbeat payload to carry a NAT'd node's live anchor set (`Protos/peer_heartbeat.proto` and/or the advert model) and re-advertise on anchor-set change.
- [ ] T026 [US3] Ingest `AnchorAdvertisement` into a `NodeRoutingTable` (`peerId → DirectAddress? + Anchors[] + IsSelfAnchor`) in the advertisement/discovery ingest path; prune stale anchors within one cycle.
- [ ] T027 [US3] Implement `RoutingPreference SelectPath(targetPeerId)` in `src/Services/Sorcha.Peer.Service/Communication/CommunicationProtocolManager.cs`: self-anchor (direct write over local reverse stream) → lowest measured-RTT remote anchor → `CircuitBreaker` failover; re-evaluated per request.
- [ ] T028 [US3] Emit `peer_path_selection_total{path=self|remote}` and `peer_anchor_failover_total` from the selection/failover path.

**Checkpoint**: SC-004 — routing is latency-preferred and verifiable from metrics.

---

## Phase 6: User Story 4 - Remove the standalone relay infrastructure (Priority: P3)

**Goal**: `Sorcha.PeerRouter` is gone; all NAT-traversal works on peer-service alone.

**Independent Test**: Solution builds with no `Sorcha.PeerRouter`; US1–US3 scenarios pass with no separate relay deployed.

### Implementation for User Story 4

- [ ] T029 [US4] Delete `src/Apps/Sorcha.PeerRouter/` project and its solution reference.
- [ ] T030 [US4] Remove all `Sorcha.PeerRouter` / `peer-router` compose + env wiring in `docker-compose.yml` (and any `.env`/deploy refs); ensure peer-service rendezvous config is present where a public node runs.
- [ ] T031 [US4] Migrate any unique coverage from `RouterCommunicationServiceRelayTests` into `tests/Sorcha.Peer.Service.Tests` (already covered by T008/T009 where possible); delete the PeerRouter test project.
- [ ] T032 [US4] Verify the solution builds with no warnings and US1–US3 in-proc scenarios pass with no separate relay component (SC-005).

**Checkpoint**: SC-005 — standalone relay removed, no capability lost.

---

## Phase 7: Polish & Cross-Cutting Concerns

- [ ] T033 [P] Doc updates: `src/Services/Sorcha.Peer.Service/README.md` (rendezvous capability), `.claude/skills/sorcha-architecture/SKILL.md` (NAT-traversal section), and CLAUDE.md peer-status note.
- [ ] T034 [P] Run quickstart §A (in-proc) end-to-end and confirm green.
- [ ] T035 Real-network E2E on tiny↔n1 per quickstart §B: SC-001 gate (submit on n1 → seal on tiny → docket back), SC-002/SC-003 (sever/restart → recover), SC-004 (path selection from metrics). **This is the demo un-park trigger.**
- [ ] T036 SC-006 latency comparison: cross-node submit/sync against the NAT'd owner vs a public-owner baseline; confirm same order of magnitude.
- [ ] T037 [P] Coverage ≥85% on new code; zero Release warnings; `dotnet test` full peer-service suite green.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (P1)** → no deps.
- **Foundational (P2)** → after Setup. **Blocks all stories** (registry, config, metrics).
- **US1 (P3)** → after Foundational. The MVP and the rendezvous core.
- **US2, US3, US4** → after **US1** (they extend US1's rendezvous; not independent of it). US2 and US3 are independent of each other and may run in parallel once US1 lands. US4 should run after US1–US3 reach parity.
- **Polish (P7)** → after the targeted stories; T035 (real E2E) needs US1 (min) and ideally US2/US3.

### Within Each Story

- Tests written first and failing → implementation → integration → metrics.
- Models before services before endpoints.

### Parallel Opportunities

- T002 ∥ (setup). T006 ∥ T007 (foundational, different files).
- US1 tests T008 ∥ T009 ∥ T010 (different files) before implementation.
- US2 tests T017 ∥ T018; US3 tests T023 ∥ T024.
- After US1: a dev can take US2 while another takes US3.
- Polish: T033 ∥ T034 ∥ T037.

---

## Parallel Example: User Story 1

```text
# Write US1 tests together first (they must fail):
T008  PeerCommunicationStreamTests.cs
T009  Integration/NatTraversalSubmitTests.cs
T010  Integration/NatTraversalSyncTests.cs
# Then implement T011→T016 (mostly same files → sequential).
```

---

## Implementation Strategy

### MVP First (User Story 1 only)

1. Phase 1 Setup → 2. Phase 2 Foundational → 3. Phase 3 US1.
4. **STOP & VALIDATE**: in-proc submit+sync against a NAT'd owner (SC-001 on the harness).
5. Run quickstart §B step B.5 on tiny↔n1 — if green, the demo is unblocked even before US2–US4.

### Incremental Delivery

US1 (MVP, reachability) → US2 (resilience) → US3 (latency routing) → US4 (retire PeerRouter) → Polish (real-network E2E + docs). Each story is a deployable increment; US2/US3 add robustness without breaking US1.

---

## Notes

- The bulk of US1 is porting tested code (`RouterCommunicationService.Stream` + `ReverseStreamManager`) into peer-service — low novelty, high confidence.
- No `.proto` change for submit/sync (message types exist); US3's anchor-set advert may extend `peer_heartbeat.proto`/advert model only.
- No datastore, no EF migration — all new state is in-memory/process-local.
- Commit after each task or logical group; keep `Sorcha.PeerRouter` until US4 so US1–US3 can cross-reference the reference implementation.
