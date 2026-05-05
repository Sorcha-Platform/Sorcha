# `ITenantHubClient` — TenantHub typed client contract

Service: `Sorcha.Tenant.Service`
Hub: `TenantHub` at `/hubs/tenant`
Groups: see `data-model.md` → `TenantHubGroups`

## Method contract

```csharp
namespace Sorcha.Tenant.Service.Hubs;

/// <summary>
/// Typed client interface for <see cref="TenantHub"/>.
/// Every method conforms to the thin-signal contract — opaque IDs only,
/// detail fetch via the linked authenticated REST endpoint.
/// </summary>
public interface ITenantHubClient
{
    /// <summary>
    /// A new inbox entry was written for the user.
    /// </summary>
    /// <param name="entryId">GUID of the entry. Caller should fetch detail via GET /api/me/inbox/{entryId}.</param>
    /// <param name="occurredAt">Server timestamp at which the entry was written.</param>
    /// <param name="traceId">W3C trace-id.</param>
    /// <see cref="MeInboxEndpoints.GetEntry" path="/api/me/inbox/{entryId}"/>
    Task InboxEntryAdded(string entryId, DateTimeOffset occurredAt, string traceId);

    /// <summary>
    /// The authenticated user's unread inbox count changed.
    /// </summary>
    /// <param name="unreadCount">New count. Authoritative; client should overwrite, not increment.</param>
    /// <param name="occurredAt">Server timestamp.</param>
    /// <param name="traceId">W3C trace-id.</param>
    /// <see cref="MeInboxEndpoints.GetUnreadCount" path="/api/me/inbox/unread-count"/>
    Task InboxUnreadCountUpdated(int unreadCount, DateTimeOffset occurredAt, string traceId);

    /// <summary>
    /// The user's membership in an org changed (added, removed, role changed).
    /// </summary>
    /// <see cref="OrganizationEndpoints.GetMyMembership" path="/api/me/organizations/{orgId}"/>
    Task MembershipChanged(string orgId, DateTimeOffset occurredAt, string traceId);

    /// <summary>
    /// A security alert occurred on the user's account (new device login, password reset, etc.).
    /// </summary>
    /// <see cref="MeSecurityEndpoints.GetAlert" path="/api/me/security-alerts/{alertId}"/>
    Task SecurityAlert(string alertId, DateTimeOffset occurredAt, string traceId);

    /// <summary>
    /// A platform-wide system announcement was published.
    /// </summary>
    /// <see cref="SystemEndpoints.GetAnnouncement" path="/api/system/announcements/{announcementId}"/>
    Task SystemAnnouncement(string announcementId, DateTimeOffset occurredAt, string traceId);
}
```

## Group emission rules

| Method | Group(s) | Emitter |
|---|---|---|
| `InboxEntryAdded` | `TenantHubGroups.User(platformUserId)` | `InboxService` after Postgres write + Redis ZADD |
| `InboxUnreadCountUpdated` | `TenantHubGroups.User(platformUserId)` | `InboxService` after every state transition (write, read, dismiss, mark-all-read) |
| `MembershipChanged` | `TenantHubGroups.User(platformUserId)`, `TenantHubGroups.Org(orgId)` | `OrganizationMembershipService` |
| `SecurityAlert` | `TenantHubGroups.User(platformUserId)` | `SecurityAlertService` |
| `SystemAnnouncement` | `TenantHubGroups.SystemAll` | `SystemAnnouncementService` (admin-only emit, gated) |

## Client-to-server methods

TenantHub has no client-to-server methods. The user is implicitly subscribed to their `user:{platformUserId:N}` group on `OnConnectedAsync` based on the JWT's `platform_user_id` claim. Org-scoped subscriptions are added/removed when the user joins/leaves an org via the existing membership endpoints.

## Auth

`[Authorize]` Bearer JWT with the `platform_user_id` claim required. Connections without the claim are aborted in `OnConnectedAsync`.
