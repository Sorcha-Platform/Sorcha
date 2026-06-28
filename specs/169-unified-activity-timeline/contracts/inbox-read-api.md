# Contract: Inbox Read API (changes for Feature 169)

Service: **Sorcha.Tenant.Service** · Endpoints: `src/Services/Sorcha.Tenant.Service/Endpoints/MeInboxEndpoints.cs`
All endpoints are per-user scoped by the caller's `platform_user_id` claim (`.RequireAuthorization()`). No tier change.

---

## GET `/api/me/inbox` — list entries (CHANGED: + `actionableOnly`)

**Query parameters**

| Name | Type | Default | Meaning |
|------|------|---------|---------|
| `page` | int (1..) | 1 | Page number. |
| `pageSize` | int (1..100) | 20 | Page size (clamped). |
| `category` | string? | null | Optional single-category filter (existing). |
| `unreadOnly` | bool | false | Existing. |
| `includeDismissed` | bool | false | Existing. |
| **`actionableOnly`** | **bool** | **false** | **NEW.** When true, return only entries where `Category == Action OR Severity >= ActionRequired`. |

**Response** `200` — `InboxPage { Entries: InboxEntry[], Page, PageSize, TotalCount }`, newest-first.
`TotalCount` reflects the **same filter set** (so "more available" = `loaded < TotalCount`).

**Consumers**
- Bell drawer (`InboxPanel`) → `actionableOnly: true`.
- Activity timeline (`ActivityFeed`) → `actionableOnly: false` (everything).

---

## GET `/api/me/inbox/unread-count` — badge count (CHANGED: semantics)

**Response** `200` — `{ "unread": <int> }`

**Change**: counts only **unread, non-dismissed, Actionable** entries
(`ReadAt == null AND DismissedAt == null AND (Category == Action OR Severity >= ActionRequired)`).
Backed by `IInboxStore.GetUnreadCountAsync(..., actionableOnly: true)`. The TenantHub `InboxUnreadCountUpdated` signal carries this same Actionable-scoped count.

---

## Unchanged endpoints (listed for completeness)

| Method | Route | Notes |
|--------|-------|-------|
| GET | `/api/me/inbox/{id:guid}` | Single entry (per-user). |
| POST | `/api/me/inbox/{id}/read` | Idempotent. |
| POST | `/api/me/inbox/{id}/dismiss` | Idempotent. |
| POST | `/api/me/inbox/mark-all-read` | Returns `{ marked }`. |

---

## Internal write channel (existing — reused by reroutes, no contract change)

`POST /api/internal/inbox` (`RequireService`) — idempotent on `(PlatformUserId, SourceEventId)`; `201` first write, `200` on retry; body `InboxEntryResponseDto { Entry, Idempotent }`. Used by `EncryptionInboxWriter` via `IPlatformInboxClient`. `PersonaInboxWriter` writes in-process via `IInboxService` (same store).

---

## Store interface delta

```csharp
// IInboxStore — add actionableOnly to the two read methods
Task<InboxPageResult> GetPageAsync(Guid platformUserId, int page, int pageSize,
    InboxCategory? category, bool unreadOnly, bool includeDismissed,
    bool actionableOnly,                              // NEW
    CancellationToken ct = default);

Task<int> GetUnreadCountAsync(Guid platformUserId,
    bool actionableOnly,                              // NEW (unread-count path passes true)
    CancellationToken ct = default);
```

EF predicate (translatable to SQL): `e.Category == InboxCategory.Action || e.Severity >= InboxSeverity.ActionRequired`.

## Documentation obligations (Constitution III)

- `.WithSummary()` / `.WithDescription()` updated on both changed endpoints to describe `actionableOnly` and the Actionable-only unread semantics.
- XML `<summary>` on the new/changed store + service members.
