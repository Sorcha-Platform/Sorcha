# Data Model: Register Subscription Sync Pipeline

**Feature Branch**: `076-register-subscription-sync`
**Date**: 2026-03-30

## Modified Entities

### Register (existing — modified)

Adds a sync state field to track replication progress for remotely-subscribed registers.

| Field | Type | New? | Description |
|-------|------|------|-------------|
| Id | string | No | 32-char hex register identifier |
| Name | string | No | Human-readable name (1-38 chars) |
| Description | string? | No | Optional description (0-2048 chars) |
| Height | uint | No | Current docket height |
| Status | RegisterStatus | No | Operational status (Offline, Online, Checking, Recovery) |
| Advertise | bool | No | Whether advertised to peer network |
| IsFullReplica | bool | No | Whether node has full transaction history |
| **SyncState** | **string?** | **Yes** | **Replication state: null (local), "Subscribing", "Syncing", "Synced", "Error"** |
| Purpose | RegisterPurpose | No | General or System |
| CreatedAt | DateTime | No | UTC creation timestamp |
| UpdatedAt | DateTime | No | UTC last update timestamp |
| DevMode | bool | No | Controls payload encryption behaviour |

**State transitions for SyncState**:

```
null ──────────────────────────── (locally created registers, no sync needed)

"Subscribing" → "Syncing" → "Synced" → null (remote subscription lifecycle)
                    │                      ↑
                    └── "Error" ──────────┘ (retry returns to "Syncing")
```

- **null**: Register is locally owned/created. No sync tracking.
- **Subscribing**: Stub created, waiting for Peer Service to begin replication.
- **Syncing**: Peer Service is actively replicating dockets and transactions.
- **Synced**: Full replication complete. Status transitions to Online, IsFullReplica to true.
- **Error**: Sync failed (no peers, network error). Retryable.

### SubscriptionNotification (new — request/response DTO only, not persisted)

Message from Tenant Service to Register Service when a subscription is created or removed.

| Field | Type | Description |
|-------|------|-------------|
| OrganizationId | Guid | Organisation that subscribed |
| RegisterId | string | Target register ID (32-char hex) |
| RegisterName | string? | Display name from peer advertisement |
| Description | string? | Description from peer advertisement |
| Action | string | "subscribe" or "unsubscribe" |

### RegisterSyncStateChangedEvent (new — domain event)

Published when a register's sync state changes, bridged to SignalR.

| Field | Type | Description |
|-------|------|-------------|
| RegisterId | string | Register whose sync state changed |
| SyncState | string | New sync state value |
| PreviousSyncState | string? | Previous sync state (for logging/debugging) |

## Modified Entities (UI)

### RegisterViewModel (existing — modified)

| Field | Type | New? | Description |
|-------|------|------|-------------|
| SyncState | string? | Yes | Mirrors backend Register.SyncState |
| IsSyncing | bool (computed) | Yes | True when SyncState is "Subscribing" or "Syncing" |
| SyncStateText | string (computed) | Yes | Human-readable display text for sync state |

## Relationships

```
Tenant Service                     Register Service                    Peer Service
┌─────────────────────┐           ┌──────────────────────┐           ┌──────────────────┐
│ OrganizationRegister │──notify──▶│ Register (+ SyncState)│──subscribe─▶│ RegisterSubscription│
│ Subscription         │           │                      │           │ (sync tracking)   │
│ (PostgreSQL)         │           │ (MongoDB)            │◀──progress──│                  │
└─────────────────────┘           └──────────────────────┘           └──────────────────┘
                                          │
                                          │ SignalR
                                          ▼
                                  ┌──────────────────┐
                                  │ UI (Blazor WASM)  │
                                  │ RegisterViewModel  │
                                  └──────────────────┘
```
