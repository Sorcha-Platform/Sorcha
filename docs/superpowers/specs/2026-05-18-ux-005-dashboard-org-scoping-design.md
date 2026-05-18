# UX-005 — Dashboard org-scoping (design, v2)

**Date:** 2026-05-18
**Spec:** `specs/131-dashboard-org-scoping/`
**Roadmap line:** M4 · "Dashboard org-scoped stats"
**Driver:** an Administrator of org A sees totals across every org on the platform on Home.razor's stats cards. Same data leaks via the anonymous `GET /api/dashboard`.

## v2 history note

v1 of this design assumed Wallet and Blueprint Service stats could be filtered by `?orgId=`. **They can't** — neither schema carries an org id. `wallet.Wallets.Tenant` is per-installation (`system`/`default`); `Wallet.Owner` is a PlatformUserId. Blueprints carry an owner wallet address. Org-scoping wallets/blueprints would require cross-service joins through Tenant Service (org → users → wallets, org → participants → blueprints), which doubles the new HTTP surface and the implementation cost.

v2 (this doc) **redefines the org-view card set** to use only data Tenant Service already has direct access to. SystemAdmin's platform view is unchanged.

## Current state

| Surface | Today |
|---|---|
| `Sorcha.ApiGateway/Program.cs:151` `GET /api/dashboard` | Anonymous. Returns `DashboardStatistics` (6 platform-wide fields) aggregated from each backend `/api/stats`. |
| Each backend `/api/stats` | Anonymous, platform-wide. |
| `Sorcha.UI.Web.Client/Pages/Home.razor:45` | Stats card grid gated to `Administrator,SystemAdmin,Auditor`. All cards render the gateway's response. |

## Decisions

| # | Decision | Why |
|---|---|---|
| **D1** | Two distinct response shapes — **org-scope** and **platform-scope** — keyed by a `scope` discriminator. | Org and platform are different mental models, not the same set of cards filtered differently. Forcing one shape muddles labels (e.g. "Total Organizations: 1" is wrong for an org admin). |
| **D2** | Org-scoped is the default for **every** role, including SystemAdmin. | Matches the org-switcher pattern: every other page in the UI honours `UserContext.ActiveContextOrgId`. |
| **D3** | SystemAdmin sees a `View: Org · Platform` toggle on the dashboard. Other roles never see it. | Platform view is the SystemAdmin power-user case. Showing the toggle to org admins would imply a view they cannot access. |
| **D4** | Auth at the gateway endpoint, not at each backend `/api/stats`. | Backend stats stay anonymous-callable; security boundary is the gateway. Closes the latent data-leak (anyone hitting `/api/dashboard` reads platform totals today). |
| **D5** | Gateway computes scope = `platform` iff `scope=="platform" && role==SystemAdmin`; else `org` with the JWT's `org_id`. | Single auth-decision site. `?scope=platform` from a non-admin is silently downgraded to org-scope. |
| **D6** | Org-scope source: **Tenant Service** owns the aggregate. Gateway delegates the whole org-view fetch to one Tenant endpoint. | Tenant already holds users-per-org, invitations-per-org, subscriptions-per-org. Adding a transactions-across-subscribed-registers field there is one cross-service call (Tenant → Register). Cleaner than gateway orchestration of 3 backends. |
| **D7** | Platform-scope source: existing 5-way fan-out from `DashboardStatisticsService`. Unchanged shape. | SystemAdmin behaviour preserved; existing tests still apply. |
| **D8** | Org-view cards: **Active users in org · Pending invitations · Subscribed registers · Recent transactions across subscribed registers**. | All four already-available without schema changes. Wallets and blueprints stay platform-only. |
| **D9** | Register Service accepts `?registerIds=a,b,c` (comma-sep) on `/api/stats`. Tenant Service uses this when building org-scope response. | One new query param on Register, no new endpoint. Sum scoped to the listed registers. |
| **D10** | Toggle persists in `localStorage` keyed by `platform_user_id`. SystemAdmin selecting Platform stays Platform across reloads. | Matches the SystemAdmin's intent. Scoped to user so a shared device doesn't bleed selection. |

## Wire shape

```jsonc
// org scope (default for everyone)
GET /api/dashboard
Authorization: Bearer <user-jwt>

200 OK
{
  "scope": "org",
  "orgId": "0ce531bf-...",
  "activeUsers": 5,
  "pendingInvitations": 2,
  "subscribedRegisters": 3,
  "recentTransactions": 142,
  "timestamp": "..."
}

// platform scope (SystemAdmin + ?scope=platform only)
GET /api/dashboard?scope=platform
Authorization: Bearer <systemadmin-jwt>

200 OK
{
  "scope": "platform",
  "orgId": null,
  "totalBlueprints": 41,
  "totalBlueprintInstances": 0,
  "activeBlueprintInstances": 0,
  "totalWallets": 89,
  "totalRegisters": 5,
  "totalTransactions": 2108,
  "totalTenants": 5,
  "connectedPeers": 3,
  "timestamp": "..."
}
```

The UI knows by `scope` which card set to render; org-only fields are omitted in platform shape and vice versa.

## Backend cost

| Service | Change |
|---|---|
| Tenant | Extend `DashboardService.GetDashboardAsync(orgId)` (or add a sibling method) to return: active-user count (already), pending-invitations (already), subscribed-registers count (new — count `OrganizationRegisterSubscriptions` with `Status=Active`), recent-transactions (new — call Register `/api/stats?registerIds=...` and sum). New endpoint or extended response on existing `/api/organizations/{orgId}/dashboard`. |
| Register | Accept `?registerIds=a,b,c` on `/api/stats`. When set, `transactionCount` is the sum across the listed registers. `registerCount` becomes the listed count (defensive — Tenant already knows it, but consistent). Unset = current platform-wide shape. |
| Wallet, Blueprint | **No change.** Wallets/blueprints not in the org view; platform view uses the existing endpoints. |
| Gateway | `/api/dashboard`: `.RequireAuthorization()`. Extract `org_id`, `platform_user_id`, role. Resolve effective scope. Org → call Tenant `/api/organizations/{orgId}/dashboard-summary`, return as `scope=org` response. Platform → existing fan-out, return as `scope=platform`. |

## UI

`MudButtonGroup` toggle (`Org` / `Platform`), top-right of stats grid, wrapped in `<AuthorizeView Roles="SystemAdmin">`. Defaults to `Org`. Selection persists in `localStorage["dashboard-scope-{platform_user_id}"]`.

Card grid is conditional on `_stats.Scope`:

- **Org** — Active users (org icon), Pending invitations (mail icon), Subscribed registers (storage icon), Recent transactions (receipt icon). Four cards.
- **Platform** — current six cards unchanged.

## Risk

| Risk | Mitigation |
|---|---|
| Tests against `/api/dashboard` that don't carry a JWT will start 401-ing. | Inspect existing test fixtures; update any that exercise this surface. |
| Tenant→Register call adds latency to the org view path. | Acceptable — single call with N register ids; capped at 5s per existing fan-out timeout. Cache-friendly if a near-future iteration adds it. |
| Auditor's view changes from platform-totals (current) to org-scoped (new). | Acceptable — Auditor scope is the org they audit. Confirm with operator in spec acceptance. |

## Non-goals

- No caching strategy (snapshot-per-request preserved).
- No realtime SignalR fan-out.
- No history / time-series.
- No new metric surfaces (e.g. credential issuance counts).
- No Wallet/Blueprint org-scoping. Deferred until either schema grows an `OrganizationId` column or we decide cross-service joins are worth the new HTTP surface.
