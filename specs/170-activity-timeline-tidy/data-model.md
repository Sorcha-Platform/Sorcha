# Phase 1 Data Model: Activity Timeline Tidy

This feature **removes** a data entity. The "model" here documents what is deleted, the schema/index footprint to squash out of the initial migration, and the retained boundary (the Inbox spine) that this feature must not touch.

## Removed entity — `ActivityEvent`

**Source**: `src/Services/Sorcha.Tenant.Service/Models/ActivityEvent.cs` (class `ActivityEvent` + enum `EventSeverity` → `Info | Success | Warning | Error`).

| Field | Type | Notes (from EF config) |
|-------|------|------------------------|
| Id | Guid | PK (`PK_ActivityEvents`) |
| OrganizationId | Guid | part of org index |
| UserId | Guid | part of user indexes |
| EventType | string(100) | required |
| Severity | EventSeverity→string(20) | required, stored as string |
| Title | string(200) | required |
| Message | string(2000) | required |
| SourceService | string(50) | required |
| EntityId | string(200)? | optional |
| EntityType | string(50)? | optional |
| IsRead | bool | filtered-index target |
| CreatedAt | DateTime | sort key (desc) |
| ExpiresAt | DateTime | retention key |

**EF mapping to remove** (`TenantDbContext.cs`): `DbSet<ActivityEvent> ActivityEvents` (l.71); `ConfigureActivityEvent(modelBuilder)` call (l.150) and the `ConfigureActivityEvent` method (l.965-1009).

## Indexes to squash out (initial migration + snapshot)

| Index | Columns | Notes |
|-------|---------|-------|
| `IX_ActivityEvent_UserId_CreatedAt` | (UserId, CreatedAt↓) | composite |
| `IX_ActivityEvent_OrgId_CreatedAt` | (OrganizationId, CreatedAt↓) | composite |
| `IX_ActivityEvent_ExpiresAt` | (ExpiresAt) | retention scan |
| `IX_ActivityEvent_UserId_IsRead` | (UserId, IsRead) | filtered: `"IsRead" = false` (PostgreSQL) |

**Migration edits**: `20260513152714_InitialCreate.cs` — remove `CreateTable("ActivityEvents", schema "public")` (Up, l.20-42), the four `CreateIndex` calls (Up, l.802-826), and `DropTable("ActivityEvents")` (Down, l.1262-1263). `TenantDbContextModelSnapshot.cs` — remove the `Sorcha.Tenant.Service.Models.ActivityEvent` entity block (l.28-100). State after edit: snapshot ≡ model (`has-pending-model-changes` → none).

## Removed transport/DTO models

| Type | File | Reason |
|------|------|--------|
| `CreateActivityEventRequest` | `Sorcha.ServiceClients.Http/Events/Models/CreateActivityEventRequest.cs` | request body for deleted POST `/api/events` |
| `MarkReadRequest`, `CreateEventRequest` | inline in `EventEndpoints.cs` | deleted with endpoints |
| `ActivityEventDto`, `EventsPagedResponse`, `UnreadCountResponse`, `MarkReadResponse` | `Sorcha.UI.Components.User/Models/Shared/ActivityEventDto.cs` | UI projections of removed surface; orphaned |
| `SystemEventViewModel`, `EventFilterModel`, `EventListResponse` | `Sorcha.UI.Core/Models/Admin/EventAdminModels.cs` | admin UI projections; prune if no other consumer |

## Retained boundary — Inbox spine (do NOT modify)

The single source of truth after this feature. This tidy **reads the consequence of** F169 but changes none of it.

| Element | Location | Role |
|---------|----------|------|
| Inbox writers | `*InboxWriter` across Tenant/Wallet/Blueprint services | Emit inbox entries (thin-signal) |
| `PersonaInboxWriter` (F169) | `Sorcha.Tenant.Service/Services/PersonaInboxWriter.cs` | Covers persona.replaced / persona.deleted |
| `EncryptionInboxWriter` (F169) | `Sorcha.Blueprint.Service/Services/Implementation/EncryptionInboxWriter.cs` | Covers EncryptionComplete / EncryptionFailed |
| `InboxPanel` (F118) | `Sorcha.UI.Components.User/Components/Inbox/InboxPanel.razor` | Durable bell-drawer UI |
| `ActivityFeed` / read surface (F169) | added by F169 | Unified timeline read path |

## Event-class coverage map (FR-001 / SC-001)

| Event class | Legacy writer (removed) | Inbox equivalent (retained) |
|-------------|-------------------------|------------------------------|
| persona.replaced | `PersonaService.ReplaceAsync` l.268-280 | `PersonaInboxWriter.WritePersonaSavedAsync` |
| persona.deleted | `PersonaService.DeleteAsync` l.306-318 | `PersonaInboxWriter.WritePersonaDeletedAsync` |
| EncryptionComplete | `EncryptionBackgroundService.StoreActivityEventAsync` (success, l.278) | `EncryptionInboxWriter.WriteEncryptionCompleteAsync` |
| EncryptionFailed | `EncryptionBackgroundService.StoreActivityEventAsync` (failure, l.378) | `EncryptionInboxWriter.WriteEncryptionFailedAsync` |

Every legacy event class maps to a retained Inbox writer → zero visible-event-class regression, **provided T-PREP (F169 merge) is done first**.
