# UX-005 — Dashboard org-scoping (design)

**Date:** 2026-05-18
**Spec:** `specs/131-dashboard-org-scoping/`
**Roadmap line:** M4 · "Dashboard org-scoped stats"
**Driver:** an Administrator of org A sees totals across every org on the platform (their own + everyone else's) on Home.razor's stats cards. Same data leaks via the anonymous `GET /api/dashboard` endpoint on the gateway — anyone with HTTP access to the platform reads `TotalWallets` / `TotalOrganizations` / `TotalTransactions` without authentication.

## Current state

| Surface | Today |
|---|---|
| `Sorcha.ApiGateway/Program.cs:151` `GET /api/dashboard` | Anonymous. Returns `DashboardStatistics` aggregated from each backend `/api/stats`. |
| Each backend `/api/stats` (Wallet, Blueprint, Register, Tenant `/api/organizations/stats`) | Anonymous. Returns platform-wide counts only. |
| `Sorcha.ApiGateway/Services/DashboardStatisticsService.cs` | No org context anywhere in the call chain. |
| `Sorcha.UI.Components.User/Services/User/DashboardService.cs:28` | Calls `/api/dashboard`. No org context. |
| `Sorcha.UI.Web.Client/Pages/Home.razor:45` | Stats card grid gated to `Administrator,SystemAdmin,Auditor`. All cards render whatever the gateway returned. |

## Decisions

| # | Decision | Why |
|---|---|---|
| **D1** | Path A — push org filter all the way through the call chain. | Backend filter is canonical; UI-only filter (Path B) wouldn't fix the gateway data-leak. |
| **D2** | Org-scoped is the default for **every** role, including SystemAdmin. | Matches the org-switcher pattern: every other page in the UI honours `UserContext.ActiveContextOrgId`. Consistency wins. |
| **D3** | SystemAdmin sees a `View: Org · Platform` toggle on the dashboard. Other roles never see it. | Platform-view is the SystemAdmin power-user case. Showing the toggle to org admins would imply they have a view they cannot access — bad affordance. |
| **D4** | `ConnectedPeers` + `TotalOrganizations` only render in **platform view**. In org view they are hidden, not zeroed. | Both are infrastructure-wide signals an org admin has no agency over. Per Home.razor:174 comment, "Citizens have no agency over infrastructure health" — extend that principle to org admins. |
| **D5** | Auth at the gateway endpoint, not at each backend `/api/stats`. | Backend stats stay anonymous-callable-with-`?orgId=`; the security boundary is the gateway. Avoids changing the s2s pattern for an internal-only metric surface. |
| **D6** | Org id passes as `?orgId={guid}` query param on every backend `/api/stats`. Omitting the param = platform-wide (SystemAdmin platform view only). | Query param is more cache-friendly than headers and trivial to filter in EF. |
| **D7** | `RecentTransactions` in org view = "transactions across registers the org is subscribed to". | Register-level data is org-scoped via `OrganizationRegisterSubscriptions`. Subset is correct and cheap to compute (Tenant already exposes the join). |
| **D8** | `ActiveRegisters` in org view = `OrganizationRegisterSubscriptions` count with `Status=Active`. | Already the visible truth on the Registers page; same number on the dashboard avoids divergence. |
| **D9** | `ActiveBlueprints` in org view = blueprints published by the org's own participants. | Blueprint publish is org-scoped via the publishing participant's org. |
| **D10** | `TotalWallets` in org view = wallets with `Tenant = orgId`. | `wallet.Wallets.Tenant` already carries org id. |

## Wire shape (after the change)

```jsonc
// org view (default)
GET /api/dashboard
Authorization: Bearer <user-jwt-with-org_id>

200 OK
{
  "scope": "org",
  "orgId": "0ce531bf-...",
  "activeBlueprints": 3,
  "totalWallets": 7,
  "recentTransactions": 142,
  "activeRegisters": 2,
  // connectedPeers + totalOrganizations omitted
  "timestamp": "2026-05-18T..."
}

// platform view (SystemAdmin only)
GET /api/dashboard?scope=platform
Authorization: Bearer <user-jwt-with-SystemAdmin-role>

200 OK
{
  "scope": "platform",
  "orgId": null,
  "activeBlueprints": 41,
  "totalWallets": 89,
  "recentTransactions": 2,108,
  "activeRegisters": 5,
  "connectedPeers": 3,
  "totalOrganizations": 5,
  "timestamp": "..."
}
```

`scope=platform` is honoured ONLY when the caller's JWT carries `SystemAdmin`. Other callers requesting it get an org-scoped response anyway (silent ignore — safer than 403).

## Backend cost

| Service | Change |
|---|---|
| Wallet | `IWalletRepository.CountAsync(string? tenantId)` overload; endpoint forwards param. |
| Blueprint | Existing `/api/stats` already returns `blueprintCount`/`instanceCount` — add `?orgId=` filter via the publisher-participant→org join. |
| Register | `/api/stats` filter: when `orgId` set, the count is "registers in `subscribedRegisterIds` for this org" — Register Service has no Tenant table so this needs a cross-service call to `Tenant.Service` for the subscription list. Cleaner: move the org-scoped read to Tenant Service, which already has `OrganizationRegisterSubscriptions`. Gateway picks the source per `scope`. |
| Tenant | `/api/organizations/stats` already accepts an org context indirectly via the `/dashboard/{orgId}` endpoint (`DashboardService.GetDashboardAsync(orgId)`). New: a single composite stats endpoint that returns subscribed-register count + transaction roll-up for the org. |

## Non-goals

- No new metric surfaces. Same six cards, with scope discipline.
- No caching strategy (current `/api/dashboard` is a fan-out per request; ~5 backend calls in parallel; fine for v1).
- No realtime updates. Snapshot on page load + a Refresh button — current behaviour.
- No history chart. Same point-in-time numbers.

## Risk

| Risk | Mitigation |
|---|---|
| Auditor role currently sees the same cards as Administrator; we're now scoping by org. Their view will change. | Acceptable — Auditors are org-scoped by definition. Org admins already expect this. |
| Tests against `/api/dashboard` that don't carry a JWT will start 401-ing. | Inspect existing test fixtures; update any that exercise this surface. |
| `?scope=platform` is JWT-claim-checked at the gateway; if claim extraction is wrong, SystemAdmin loses the platform view. | Existing `AuthorizationPolicies` already extract role claims; reuse them. |
| Subscribed-register transaction count is a cross-service read (Register asks Tenant). Latency. | Acceptable — already five backend calls in parallel; one more isn't load-bearing. Cap timeout at 5 s (matches existing). |

## Toggle UI

`MudButtonGroup` with two buttons (Org / Platform), top-right of the stats grid, visible only when `IsInRole("SystemAdmin")`. Defaults to Org. Selection persists in `localStorage` keyed by user id so it survives reloads but doesn't bleed across users on a shared device.
