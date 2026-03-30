# Research: Register Subscription Sync Pipeline

**Feature Branch**: `076-register-subscription-sync`
**Date**: 2026-03-30

## R1: How to Represent Sync State on the Register Model

**Decision**: Add a nullable `SyncState` string field to the existing `Register` model rather than adding a new `RegisterStatus` enum value.

**Rationale**: The `RegisterStatus` enum (Offline, Online, Checking, Recovery) represents the operational state of the register. Sync state is orthogonal — a register can be Online and fully synced, or Online and still catching up. Mixing the two concepts in one enum creates ambiguity. A separate nullable field allows the existing status lifecycle to remain unchanged. When `SyncState` is null or empty, the register is locally-owned and not relevant for sync tracking.

**Alternatives considered**:
- Adding `Syncing` to `RegisterStatus` — rejected because it conflates operational state with replication state
- Using the existing `IRegisterRecoveryService` state — rejected because recovery is a different concept (repairing an existing local register), not onboarding a new remote one
- Separate `RegisterSyncInfo` entity — over-engineered for what is essentially one field on the register plus event-driven updates

## R2: How Tenant Service Notifies Register Service

**Decision**: Tenant Service calls an internal endpoint on Register Service via the existing `IRegisterServiceClient` (already registered in Tenant Service DI). Fire-and-forget pattern — subscription persists even if notification fails.

**Rationale**: The Tenant Service already has `IRegisterServiceClient` registered (via `AddServiceClients()`). The Register Service already has an `/api/internal/*` endpoint pattern (e.g., `/api/internal/registers`) used for service-to-service calls. This is the lowest-friction path.

**Alternatives considered**:
- Event bus / message queue — adds infrastructure dependency not currently in the project
- Peer Service as intermediary — adds unnecessary coupling; Peer Service is about network transport, not subscription lifecycle
- Polling from Register Service — higher latency, wasteful resource usage

## R3: Stub Register Creation

**Decision**: Use existing `RegisterManager.CreateRegisterAsync()` to create a stub register with `SyncState = "Subscribing"`, `IsFullReplica = false`, `Status = Offline`. As sync progresses, update `SyncState` and when complete, set `IsFullReplica = true`, `Status = Online`.

**Rationale**: `CreateRegisterAsync` already supports pre-set `registerId`, optional `description`, and `isFullReplica = false`. The register will immediately appear in `GetRegistersForOrgAsync()` because the subscription record already exists in the Tenant Service.

**Alternatives considered**:
- Separate "pending register" store — adds complexity, would need separate queries and merge logic
- Return subscription metadata from the UI subscription service without a real register record — the UI's `LoadRegistersAsync` intersects subscriptions with registers, so a real register record is needed

## R4: Peer Service Sync Triggering

**Decision**: Add a `SubscribeToRegisterAsync(registerId, mode)` method to `IPeerServiceClient` that calls `POST /api/registers/{registerId}/subscribe` on the Peer Service. The Peer Service endpoint already exists and creates a `RegisterSubscription` with sync state tracking.

**Rationale**: The Peer Service endpoint (`POST /api/registers/{registerId}/subscribe`) is already fully implemented with replication mode selection, duplicate detection, and network advertisement validation. We just need the client method to call it.

**Alternatives considered**:
- Direct gRPC call to Peer Service — the existing subscribe endpoint is HTTP REST, and the Register Service already uses HTTP for peer client calls (advertise, bulk-advertise)
- Having the Peer Service poll for new subscriptions — higher latency, wasteful

## R5: SignalR Sync State Events

**Decision**: Add a `RegisterSyncStateChanged(registerId, syncState)` event to the existing `IRegisterHubClient` interface and `RegisterEventBridgeService` pattern.

**Rationale**: The event bridge pattern is well-established. A `register:sync-state-changed` domain event published by `RegisterManager` will be picked up by `RegisterEventBridgeService` and broadcast via SignalR. The UI's `RegisterHubConnection` already handles similar events (e.g., `OnRegisterStatusChanged`).

**Alternatives considered**:
- Reuse `RegisterStatusChanged` for sync updates — rejected because status and sync state are separate concerns and the UI handles them differently
- Polling from the UI — defeats the purpose of real-time feedback

## R6: UI SubscribeDialog Name Passing

**Decision**: The `SubscribeDialog` already has access to `AvailableRegisterDto.Name` and `Description`. Modify the `SubscribeAsync` call to pass the register name so the Tenant Service can include it in the notification to Register Service, enabling a meaningful stub register name.

**Rationale**: Currently `SubscribeAsync` only passes `registerId`. The `AvailableRegisterDto` has `Name` and `Description` from peer advertisements. Passing these through to the Tenant Service (which already supports `RegisterName` on the subscription record) ensures the stub register has a displayable name from the start.

## R7: Unsubscribe Cleanup

**Decision**: When Tenant Service processes an unsubscribe, it notifies Register Service (same internal endpoint pattern). Register Service then tells Peer Service to stop replication and removes the local register record if it's not owned by this node.

**Rationale**: Mirrors the subscribe flow. The Peer Service already has `UnsubscribeFromRegisterAsync()` which stops live subscription tasks and removes the subscription record.

**Alternatives considered**:
- Leave orphaned register data — wastes storage and creates confusing UI state
- Only stop sync, keep data — could be useful for caching but adds complexity; simpler to clean up fully
