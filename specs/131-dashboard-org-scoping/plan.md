# Plan — Dashboard org-scoping (UX-005, Feature 131, v2)

**Spec:** `spec.md`
**Design:** `docs/superpowers/specs/2026-05-18-ux-005-dashboard-org-scoping-design.md`

## Strategy

Three layers, ordered. Two PRs.

- **PR-A** — Backend support:
  1. Register Service `/api/stats` accepts `?registerIds=`.
  2. Tenant Service grows an org-summary surface (returns 4 org-scoped fields including `recentTransactions` via Tenant→Register cross-service call).
  Behaviour without the new param/endpoint is unchanged. Shippable independently.
- **PR-B** — Gateway + UI:
  1. Gateway `/api/dashboard` requires auth; resolves scope; routes org→Tenant, platform→existing fan-out.
  2. UI scope toggle, conditional card grid, localStorage persistence.
  Depends on PR-A live.

## Files

### PR-A — backend

| File | Change |
|---|---|
| `src/Services/Sorcha.Register.Service/Program.cs:3105` | Accept `[FromQuery] string? registerIds`. When non-empty (comma-split, validated as ids), return `{ registerCount = listed.Count, transactionCount = sumAcrossListed }`. When null, retain platform-wide shape. |
| `src/Services/Sorcha.Tenant.Service/Services/IDashboardService.cs` | Add `Task<OrgSummaryResponse> GetOrgSummaryAsync(Guid orgId, CancellationToken ct)`. New DTO `OrgSummaryResponse { Guid OrgId, int ActiveUsers, int PendingInvitations, int SubscribedRegisters, int RecentTransactions, DateTimeOffset Timestamp }`. |
| `src/Services/Sorcha.Tenant.Service/Services/DashboardService.cs` | Implement `GetOrgSummaryAsync`. Reuse existing user count + pending invitation count. Add `OrganizationRegisterSubscriptions` count query (Status=Active). Call `IRegisterServiceClient` with the subscribed register ids; sum `transactionCount`. |
| `src/Services/Sorcha.Tenant.Service/Endpoints/DashboardEndpoints.cs` | Map `GET /api/organizations/{orgId:guid}/dashboard-summary` → `GetOrgSummaryAsync`. Policy `RequireAuthenticated` (any signed-in org member). |
| `src/Common/Sorcha.ServiceClients.Http/Register/IRegisterServiceClient.cs` + impl | Add `Task<RegisterStatsResponse> GetStatsAsync(IReadOnlyList<string>? registerIds = null, CancellationToken ct = default)`. URL builder joins ids with `,`. |
| `tests/Sorcha.Register.Service.Tests/...` | Two tests: `?registerIds=` filters to sum; unset returns platform shape. |
| `tests/Sorcha.Tenant.Service.Tests/Services/DashboardServiceOrgSummaryTests.cs` | Cases: user count, invitations, subscriptions, mocked Register call for transactions, error path (Register down → recentTransactions = 0). |

### PR-B — gateway + UI

| File | Change |
|---|---|
| `src/Services/Sorcha.ApiGateway/Program.cs:151` | `.RequireAuthorization()`. Accept `[FromQuery] string? scope` and `HttpContext`. Resolve effective scope. Org → call Tenant `/api/organizations/{orgId}/dashboard-summary`; map result to `DashboardResponse { scope:"org", orgId, activeUsers, pendingInvitations, subscribedRegisters, recentTransactions, timestamp }`. Platform → existing fan-out, decorate with `scope:"platform", orgId:null`. |
| `src/Services/Sorcha.ApiGateway/Services/DashboardStatisticsService.cs` | Add `Scope` + `OrgId` to `DashboardStatistics`. Add `GetOrgSummaryAsync(Guid orgId, string? bearerToken)` that calls Tenant. |
| `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Services/User/DashboardService.cs:28` | Accept `string? scope = null`; forward `?scope=platform` when provided. |
| `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Models/User/Dashboard/DashboardStatsViewModel.cs` | Add `Scope` (`"org"`\|`"platform"`), `OrgId` (Guid?), org-only fields (`ActiveUsers`, `PendingInvitations`, `SubscribedRegisters`, `RecentTransactions`) — all nullable so platform shape doesn't fill them. |
| `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/Home.razor` | (a) Add scope-toggle `MudButtonGroup` inside `<AuthorizeView Roles="SystemAdmin">`, top-right of the stats grid. (b) Bind to `_scope`, reload stats on change, persist in `localStorage`. (c) Replace single card grid with two `@if (_stats.Scope == "org")` / `@else` branches. Org branch: 4 cards. Platform branch: existing 6 cards. |
| `tests/Sorcha.ApiGateway.Tests/...` | Anonymous→401; non-admin→org; admin no-toggle→org; admin scope=platform→platform; non-admin scope=platform→org (silent ignore). |
| `tests/Sorcha.UI.E2E.Tests/Docker/DashboardScopeTests.cs` (Playwright) | Smoke: SystemAdmin sees toggle, can flip; Administrator (non-system) doesn't see toggle; both render expected card counts. |

## PR ordering

```
PR-A backend (Register filter + Tenant summary endpoint)
        │  shippable independently — no caller uses it yet
        ▼
PR-B gateway + UI
```

## Effort estimate

- PR-A: ~4h (Register query-param parse, Tenant new method + endpoint + cross-service call + tests).
- PR-B: ~5h (Gateway routing + UI toggle + localStorage + Playwright smoke).
- Total: ~9h. Tracks prior estimate.

## Verification

- All target backends' unit tests pass with new param/endpoint and the unchanged unscoped path.
- Gateway integration tests pass for the five scope-resolution cases.
- Playwright smoke + manual run on n1: `admin@sorcha.local` (SystemAdmin) flips toggle and sees both views; `verification-admin@assured-identity.local` (Administrator of Acme Verification Co.) sees org view only.
- SC-001 through SC-008 from `spec.md` validated on n1.
