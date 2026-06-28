# Phase 1 Data Model: Unified Activity Timeline Read-Path

This feature is **read-path + derived classification + two reroute writers**. It introduces **no new persisted entity and no schema migration**. The model below documents the entities it reads/writes and the one new *derived* concept.

---

## 1. Inbox entry — the spine (existing, unchanged)

**Type**: `Sorcha.Tenant.Service.Models.InboxEntry` · **Table**: `public.InboxEntries`

| Field | Type | Role in this feature |
|-------|------|----------------------|
| `Id` | `Guid` | Identity. |
| `PlatformUserId` | `Guid` | Per-user scope (read + write). |
| `Category` | `InboxCategory` | **Input to classification** + timeline icon. |
| `Severity` | `InboxSeverity` | **Input to classification** + emphasis. |
| `CorrelationKey` | `string` | Grouping (unchanged). |
| `DetailHref` | `string` | Timeline entry → detail navigation (FR-003). |
| `SourceEventId` | `Guid` | **Idempotency** key with `PlatformUserId` (FR-018). |
| `OccurredAt` | `DateTimeOffset` | Reverse-chronological ordering (FR-001). |
| `ReadAt` | `DateTimeOffset?` | Unread = null. |
| `DismissedAt` | `DateTimeOffset?` | Active = null. |
| `Title` | `string` | Timeline title (FR-003). |
| `Summary` | `string?` | Timeline summary (FR-003). |
| `IconKey` | `string?` | Optional icon. |
| `ChannelHints` | `ChannelHints` | Delivery hints (unchanged). |

**Enums (existing, reused — no new members):**

- `InboxCategory`: `Action(0)`, `Credential(1)`, `Membership(2)`, `Security(3)`, `System(4)`, `Workflow(5)`, `Custom(99)`
- `InboxSeverity`: `Info(0)`, `Warning(1)`, `ActionRequired(2)`, `Critical(3)`
- `ChannelHints` `[Flags]`: `None(0)`, `Inbox(1)`, `Push(2)`, `Email(4)`, `Digest(8)`

**Validation rules (existing, honoured):** `Title` ≤200; `Summary` ≤1000; `DetailHref` starts with `/api/`; `(PlatformUserId, SourceEventId)` unique.

---

## 2. Activity classification — NEW (derived view, not persisted)

A pure, total function over the spine entry. **Two values:** `Actionable`, `Informational`.

```
Classify(category, severity) =
    Actionable      if category == Action  OR  severity >= ActionRequired
    Informational   otherwise   (includes the unknown/unset fallback)
```

- **Server home**: `Sorcha.Tenant.Service.Services.InboxClassification` (static helper + an `actionableOnly` SQL predicate over `Category`/`Severity`).
- **Client mirror**: `Sorcha.UI.Components.User.Services.Shared.ActivityClassification` (same rule over the DTO's string `Category`/`Severity`) — used only for timeline emphasis; the bell's count remains server-authoritative.
- **Invariant**: the two implementations MUST agree (covered by a shared truth-table test). Default fallback = `Informational` (FR-011).

State: stateless/derived — no transitions, no storage.

---

## 3. Legacy ActivityEvent — RETAINED unchanged (read-reference only)

**Type**: `Sorcha.Tenant.Service.Models.ActivityEvent` · **Table**: `public.ActivityEvents`

Kept verbatim (entity, `EventService`, DbContext config, migrations) per FR-019. The two producers below **additionally** write the spine; their existing `ActivityEvent` emit is **not removed** this run. Field correspondence used by the reroute writers:

| ActivityEvent field | → Inbox spine field |
|---------------------|---------------------|
| `UserId` | `PlatformUserId` |
| `EventType` | encoded into `SourceEventId` key + drives `Category` |
| `Severity` (`EventSeverity`) | mapped to `InboxSeverity` (see research R4) |
| `Title` / `Message` | `Title` / `Summary` |
| `EntityType` / `EntityId` | informs `DetailHref` (where a detail destination exists) |

---

## 4. Reroute write records (transient inputs, not entities)

### 4a. Persona → spine (Tenant Service, in-process via `IInboxService`)

| Event | PlatformUserId | Category | Severity | Title | SourceEventId key |
|-------|----------------|----------|----------|-------|-------------------|
| save | profile owner | `System` | `Info` | "Profile saved" | `sorcha.inbox.persona.replaced:{userId:N}:{unixSecs}` |
| delete | profile owner | `System` | `Warning` | "Profile deleted" | `sorcha.inbox.persona.deleted:{userId:N}:{unixSecs}` |

### 4b. Encryption → spine (Blueprint Service, via `IPlatformInboxClient`)

| Event | PlatformUserId | Category | Severity | Title | SourceEventId key |
|-------|----------------|----------|----------|-------|-------------------|
| complete | `workItem.UserId` | `Workflow` | `Info` | "Encryption completed…" | `sorcha.inbox.encryption.complete:{operationId}` |
| fail | `workItem.UserId` | `Workflow` | `ActionRequired` | "Encryption failed…" | `sorcha.inbox.encryption.fail:{operationId}` |

Both write paths are **best-effort** (try / log / swallow; never roll back — FR-015) and **idempotent** (FR-018). If `PlatformUserId`/`UserId` is empty, the writer logs and skips.

---

## Relationships

```
PlatformUser 1──* InboxEntry  (existing cascade FK)
InboxEntry  ──derive──> Activity classification (Actionable | Informational)   [no storage]
PersonaService          ──best-effort write──> InboxEntry  (+ keeps legacy ActivityEvent)
EncryptionBackgroundSvc ──best-effort write──> InboxEntry  (+ keeps legacy ActivityEvent)
ActivityFeed (shared)   ──reads (all)──>        /api/me/inbox
InboxPanel (bell)       ──reads (Actionable)──> /api/me/inbox + /unread-count
```
