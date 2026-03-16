# Implementation Plan: Relay-Aware Peer Communication

**Branch**: `060-relay-aware-communication` | **Date**: 2026-03-16 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/060-relay-aware-communication/spec.md`
**Design**: [design spec](../../docs/superpowers/specs/2026-03-16-relay-aware-communication-design.md)

## Summary

Add relay fallback to the Peer Service so NAT'd peers (empty `peer.Address`) can communicate through the seed node's existing `PeerCommunication.SendMessage` relay endpoint. Four new `MessageType` enum values enable register sync via batch request/response over the relay. Zero PeerRouter changes required — all new logic lives in the Peer Service.

## Technical Context

**Language/Version**: C# 13 / .NET 10
**Primary Dependencies**: Grpc.Net 2.71.0, Google.Protobuf, System.Text.Json
**Storage**: N/A (uses existing RegisterCache in-memory + Redis)
**Testing**: xUnit + FluentAssertions + Moq
**Target Platform**: Linux container / Windows (cross-platform .NET)
**Project Type**: Microservice (Sorcha.Peer.Service)
**Performance Goals**: Message relay within 5 seconds; register sync within 2 poll intervals (2 min default)
**Constraints**: 4MB protobuf message size limit (16MB configured); relay adds ~2x latency vs direct; all traffic through single seed node
**Scale/Scope**: Test network (<10 peers), all potentially NAT'd

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Gate | Status | Notes |
|------|--------|-------|
| I. Microservices-First | PASS | All changes in Peer Service only. Zero PeerRouter changes. |
| II. Security First | PASS | SenderPeerId populated on all relay messages. PeerRouter validates non-empty sender. |
| III. API Documentation | PASS | No new REST endpoints. Proto changes self-documenting. XML docs on all public methods. |
| IV. Testing Requirements | PASS | Unit tests for all new classes. Integration test for relay round-trip. |
| V. Code Quality | PASS | Async/await throughout. DI for all dependencies. Nullable enabled. |
| VI. Blueprint Standards | N/A | No blueprint changes. |
| VII. Domain-Driven Design | PASS | Uses existing domain terms (Register, Docket, Transaction). |
| VIII. Observability | PASS | Structured logging on relay send/receive. Existing metrics infrastructure. |

No violations. No complexity tracking needed.

## Project Structure

### Documentation (this feature)

```text
specs/060-relay-aware-communication/
├── plan.md              # This file
├── spec.md              # Feature specification
├── research.md          # Phase 0: technical research
├── data-model.md        # Phase 1: data models
├── quickstart.md        # Phase 1: implementation guide
├── contracts/
│   └── proto-changes.md # Phase 1: proto contract changes
└── checklists/
    └── requirements.md  # Spec quality checklist
```

### Source Code (repository root)

```text
src/Services/Sorcha.Peer.Service/
├── Communication/
│   ├── CommunicationProtocolManager.cs    # MODIFIED - relay fallback check
│   ├── RelayCommunicationService.cs       # NEW - core relay primitive
│   ├── RelayMessageHandler.cs             # NEW - incoming relay message dispatch
│   └── Models/
│       └── RelayMessages.cs               # NEW - request/response POCOs
├── Distribution/
│   └── TransactionDistributionService.cs  # MODIFIED - relay fallback
├── Replication/
│   ├── RegisterReplicationService.cs      # MODIFIED - relay batch sync
│   └── RegisterSyncBackgroundService.cs   # MODIFIED - periodic poll + semaphores
├── GrpcServices/
│   └── PeerCommunicationServiceImpl.cs    # NEW - gRPC service for incoming relay
├── Core/
│   └── PeerServiceConfiguration.cs        # MODIFIED - RelayPollIntervalSeconds
├── Protos/
│   └── peer_communication.proto           # MODIFIED - 4 new MessageType values
└── Program.cs                             # MODIFIED - DI + MapGrpcService

tests/Sorcha.Peer.Service.Tests/
├── Communication/
│   ├── RelayCommunicationServiceTests.cs  # NEW
│   └── RelayMessageHandlerTests.cs        # NEW
├── Distribution/
│   └── TransactionDistributionServiceTests.cs  # MODIFIED
└── Replication/
    └── RegisterReplicationServiceTests.cs      # MODIFIED
```

**Structure Decision**: All changes are within the existing `Sorcha.Peer.Service` project and its test project. No new projects needed — relay is a communication concern within the peer service.
