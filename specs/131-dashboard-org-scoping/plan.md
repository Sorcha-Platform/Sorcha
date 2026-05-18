# Plan — Dashboard org-scoping (UX-005, Feature 131)

**Spec:** `spec.md`
**Design:** `docs/superpowers/specs/2026-05-18-ux-005-dashboard-org-scoping-design.md`

## Strategy

Five layers, top-down. Land as **two PRs** to keep diffs reviewable; each PR self-contained and shippable.

- **PR-A** — Backend stats endpoints accept `?orgId=` filter (Wallet, Blueprint, Register, Tenant). Includes unit tests. Behaviour without the param is unchanged, so this PR is a pure non-breaking extension and can ship independently.
- **PR-B** — Gateway dashboard endpoint requires auth + scope-aware routing + UI toggle + UI card hiding. Depends on PR-A's filters being live.

## Files

### PR-A — backend filters

| File | Change |
|---|---|
| `src/Services/Sorcha.Wallet.Service/Program.cs:315-333` | Accept `[FromQuery] Guid? orgId`. When set, call `IWalletRepository.CountAsync(tenantId: orgId.ToString())`. |
| `src/Core/Sorcha.Wallet.Portable/Repositories/Interfaces/IWalletRepository.cs` | Overload `Task<int> CountAsync(string? tenantId = null, CancellationToken ct = default)`. |
| `src/Core/Sorcha.Wallet.Portable/Repositories/EfCoreWalletRepository.cs` (or current impl) | Implement the overload; filter on `Tenant` column. |
| `src/Services/Sorcha.Blueprint.Service/Program.cs:2322` (the `/api/stats` MapGet) | Accept `[FromQuery] Guid? orgId`. Filter via the publishing participant's `OrgId`. |
| `src/Services/Sorcha.Blueprint.Service/Storage/IBlueprintStore.cs` (or whichever surface provides the count) | Add `CountByOrgAsync(Guid orgId)` if not present; reuse existing index. |
| `src/Services/Sorcha.Register.Service/Program.cs:3105` | Accept `[FromQuery] Guid? orgId`. When set, return `{ registerCount = subscribedActive, transactionCount = sumAcrossSubscribed }`. Register Service calls Tenant Service via the existing service-to-service client for the subscription list (Register→Tenant cross-service hop). When unset, retain current platform-wide return. |
| `src/Services/Sorcha.Tenant.Service/Endpoints/...` (org stats) | Already accepts org context via the dashboard service. Confirm `/api/organizations/stats` accepts `?orgId=` and returns `{ totalOrganizations = 1, totalUsers = orgUsers }` for that org; platform shape unchanged when omitted. |
| `tests/Sorcha.Wallet.Service.Tests/.../StatsEndpointTests.cs` | Two cases: `?orgId=` filters; omitting returns platform total. |
| `tests/Sorcha.Blueprint.Service.Tests/...` | Same pattern. |
| `tests/Sorcha.Register.Service.Tests/...` | Same pattern + cross-service subscription read mock. |
| `tests/Sorcha.Tenant.Service.Tests/...` | Confirm new shape. |

### PR-B — gateway + UI

| File | Change |
|---|---|
| `src/Services/Sorcha.ApiGateway/Program.cs:151` | `.RequireAuthorization()` on `/api/dashboard`. Accept `[FromQuery] string? scope`. Read `org_id` + role claims from `HttpContext.User`. Resolve effective scope: `platform` if `scope == "platform"` AND `IsInRole("SystemAdmin")`; else `org` with `orgId` from claim. Pass through to `DashboardStatisticsService`. |
| `src/Services/Sorcha.ApiGateway/Services/DashboardStatisticsService.cs` | `GetDashboardStatisticsAsync(Guid? orgId, CancellationToken ct)`. Each fan-out method appends `?orgId={orgId}` to its backend call when set. |
| `src/Services/Sorcha.ApiGateway/Services/DashboardStatisticsService.cs:198-212` (`DashboardStatistics` class) | Add `Scope` (string) + `OrgId` (Guid?). Keep `ConnectedPeers` + `TotalOrganizations` nullable; omit on serialize when null (default JsonIgnoreCondition.WhenWritingNull). |
| `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Services/User/DashboardService.cs:28` | Accept `string? scope = null` param; forward `?scope=platform` when "platform". |
| `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Models/User/Dashboard/DashboardStatsViewModel.cs` | Add `Scope`, `OrgId`, `ConnectedPeers?`, `TotalOrganizations?` (nullable on the wire too). |
| `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/Home.razor` | Add scope-toggle `MudButtonGroup` (visible only when user is `SystemAdmin`). Bind selection to `localStorage` via `IJSRuntime` keyed by `platform_user_id`. Hide ConnectedPeers + TotalOrganizations cards when `_stats.Scope == "org"`. Reload stats on toggle change. |
| `tests/Sorcha.ApiGateway.Tests/.../DashboardEndpointTests.cs` | Cases: anon → 401; non-admin → org scope; admin no-toggle → org scope; admin `?scope=platform` → platform scope; non-admin `?scope=platform` → org scope (silent ignore). |
| `tests/Sorcha.UI.E2E.Tests/Docker/DashboardScopeTests.cs` (Playwright) | Smoke: SystemAdmin sees toggle, can flip; non-admin doesn't see toggle. |

## PR ordering

```
PR-A backend filters  ──►  PR-B gateway + UI
   (independent ship)         (depends on PR-A live)
```

PR-A is shippable in isolation — no caller uses `?orgId=` until PR-B. Risk: if PR-B is delayed, PR-A widens the API surface without exercising it. Acceptable; the contract is small.

## Effort estimate

- PR-A: ~4h (4 backends × ~30 min code + ~30 min test each; one cross-service hop for Register).
- PR-B: ~5h (gateway claim extraction + UI toggle + localStorage + Playwright smoke).
- Total: ~9h. Tracks the design's 8–10h estimate.

## Verification

- All four backends: unit tests pass with the `?orgId=` overload and the unscoped path.
- Gateway: integration test pass for the four scope-resolution cases.
- UI: Playwright smoke + manual run on n1 against `admin@sorcha.local` (SystemAdmin) and a freshly-created Administrator for org A (Acme Verification Co. exists from earlier walkthroughs).
- SC-001 through SC-008 from `spec.md` validated on n1.
