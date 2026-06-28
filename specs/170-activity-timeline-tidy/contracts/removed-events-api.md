# Removed Contract: Legacy Activity-Event REST surface (`/api/events*`)

This is a **negative contract** — it records the interface surface this feature deletes, so reviewers and any out-of-process consumer can confirm nothing depends on it. After this feature, every route below returns **404** and the corresponding HTTP client is gone.

**Source**: `src/Services/Sorcha.Tenant.Service/Endpoints/EventEndpoints.cs` (group `app.MapGroup("/api/events")`, tag `Events`, `RequireAuthorization()`).

## Routes being removed

| Method | Route | Name | Was used by |
|--------|-------|------|-------------|
| GET | `/api/events` | GetEvents | (no remaining consumer) |
| GET | `/api/events/unread-count` | GetUnreadCount | (no remaining consumer) |
| POST | `/api/events/mark-read` | MarkRead | (no remaining consumer) |
| POST | `/api/events` | CreateEvent (service-to-service) | `IEventServiceClient` (Blueprint encryption pipeline) — removed |
| GET | `/api/events/admin` | GetAdminEvents (admin only) | orphaned `IActivityLogService` UI client — removed |
| DELETE | `/api/events/{id:guid}` | DeleteEvent | orphaned `IActivityLogService` UI client — removed |

## HTTP client being removed

`IEventServiceClient` / `EventServiceClient` (`src/Common/Sorcha.ServiceClients.Http/Events/`):

```csharp
// REMOVED — do not reintroduce. Activity now flows through the Inbox spine (F169).
Task<bool> CreateEventAsync(CreateActivityEventRequest request, CancellationToken ct = default);
// POST {TenantService}/api/events  (best-effort; returned false on failure)
```

Registration removed from `HttpServiceCollectionExtensions.AddHttpServiceClients` (l.72-73).

## Replacement contract

There is **no REST replacement** for these routes. The activity timeline is served by the Inbox spine (Features 118 + 169):

- Server: `*InboxWriter` classes emit inbox entries; the Inbox read endpoints (e.g. `/me/inbox`) and `TenantHub` deliver them.
- Client: `IInboxApiService` + `InboxPanel` (durable drawer) and the F169 unified `ActivityFeed` read path.

## Verification (no-consumer assertion — see quickstart.md)

```bash
# Expect ZERO matches outside deleted/spec files after the change:
grep -rn "/api/events" src/ tests/
grep -rn "IEventServiceClient\|EventServiceClient\|CreateActivityEventRequest" src/ tests/
grep -rn "IActivityLogService\|ActivityLogService" src/
```
