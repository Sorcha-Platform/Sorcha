# Tasks — Dashboard org-scoping (UX-005, Feature 131, v2)

**Spec:** `spec.md`
**Plan:** `plan.md`

## PR-A — Backend support

### Register Service

- [ ] **T001** — `Sorcha.Register.Service/Program.cs:3105` `/api/stats`: accept `[FromQuery] string? registerIds`. When non-empty, comma-split into list; defensive validation (drop empty entries, cap list length at 50).
- [ ] **T002** — When `registerIds` is set, return `{ registerCount = listed.Count, transactionCount = sumAcrossListed }`. Use the existing transaction-count source filtered to the listed register ids. When unset, retain platform-wide shape.
- [ ] **T003** — Unit tests: `?registerIds=a,b` filters; unset returns platform shape; over-50 ids → 400 (or capped — pick the documented behaviour and stick).

### Service client extension

- [ ] **T004** — `Sorcha.ServiceClients.Http/Register/IRegisterServiceClient.cs`: add `Task<RegisterStatsResponse> GetStatsAsync(IReadOnlyList<string>? registerIds = null, CancellationToken ct = default)`. New DTO carries `RegisterCount` + `TransactionCount`.
- [ ] **T005** — Implementation builds the URL: when `registerIds` non-empty, append `?registerIds={joined}`. Use `Uri.EscapeDataString` per id.
- [ ] **T006** — Client unit tests via `HttpMessageHandler` stub: verifies query-string shape on with/without ids.

### Tenant Service

- [ ] **T007** — Define `OrgSummaryResponse` DTO in `Sorcha.Tenant.Service/Models/Dtos/`: `Guid OrgId, int ActiveUsers, int PendingInvitations, int SubscribedRegisters, int RecentTransactions, DateTimeOffset Timestamp`.
- [ ] **T008** — Extend `IDashboardService` with `Task<OrgSummaryResponse> GetOrgSummaryAsync(Guid orgId, CancellationToken ct)`.
- [ ] **T009** — Implement `GetOrgSummaryAsync` in `DashboardService`: query active-user count, pending-invitation count, active-subscribed-register list. For `RecentTransactions`, call `IRegisterServiceClient.GetStatsAsync(subscribedIds)` and use `transactionCount`. On client error, set `RecentTransactions = 0` and log warning (non-throwing).
- [ ] **T010** — Inject `IRegisterServiceClient` into `DashboardService` (DI wiring confirmed in `ServiceCollectionExtensions.cs`).
- [ ] **T011** — Map `GET /api/organizations/{orgId:guid}/dashboard-summary` in `DashboardEndpoints.cs`. Policy: `RequireAuthenticated` (any signed-in org member; the existing `/dashboard` endpoint requires Administrator — summary is broader because Auditors should be able to read it).
- [ ] **T012** — Unit tests for `GetOrgSummaryAsync`: happy path; Register-client down → `RecentTransactions = 0`; zero subscribed registers → call still ok with empty list (Register handles empty `?registerIds=` as 0).
- [ ] **T013** — Endpoint integration tests via WebApplicationFactory pattern: 401 anon; 200 authed; 200 includes all four fields.

### PR-A wrap

- [ ] **T014** — `dotnet test` for Register + Tenant test projects + service-clients tests; all green.
- [ ] **T015** — Open PR-A, await CI + claude-review, squash-merge.

---

## PR-B — Gateway + UI

### Gateway

- [ ] **T016** — `Sorcha.ApiGateway/Program.cs:151`: change `app.MapGet("/api/dashboard", …)` to `.RequireAuthorization()`. Accept `[FromQuery] string? scope` and `HttpContext`. Inject `DashboardStatisticsService`, `ITenantSummaryClient` (new — or extend existing tenant client).
- [ ] **T017** — Extract `org_id`, `platform_user_id`, role claims from `HttpContext.User`. Compute effective scope: `platform` iff `scope == "platform" && context.User.IsInRole("SystemAdmin")`; else `org` with the JWT's `org_id` claim.
- [ ] **T018** — Org scope path: call new `TenantSummaryClient.GetOrgSummaryAsync(orgId, bearerToken)` (forwards the user's JWT). Map result to `DashboardResponse { scope = "org", orgId, activeUsers, pendingInvitations, subscribedRegisters, recentTransactions, timestamp }`.
- [ ] **T019** — Platform scope path: existing `DashboardStatisticsService.GetDashboardStatisticsAsync` (unchanged). Wrap result with `scope = "platform", orgId = null`.
- [ ] **T020** — Extend `DashboardStatistics` DTO with `Scope`, `OrgId`. Make platform-only fields stay; add org-only nullable fields. JSON serializer: `JsonIgnoreCondition.WhenWritingNull` to keep the two wire shapes clean.
- [ ] **T021** — `ITenantSummaryClient` + impl in `Sorcha.ApiGateway/Services/`. Typed HttpClient calling Tenant's `/api/organizations/{orgId}/dashboard-summary`. Forwards bearer token (so Tenant's `RequireAuthenticated` is satisfied). Returns `OrgSummaryResponse`.
- [ ] **T022** — Gateway integration tests in `Sorcha.ApiGateway.Tests`:
  - anonymous → 401
  - non-admin user → `scope: "org"`; platform-only fields omitted
  - SystemAdmin no toggle → `scope: "org"`
  - SystemAdmin `?scope=platform` → `scope: "platform"`; org-only fields omitted
  - non-admin `?scope=platform` → `scope: "org"` (silent ignore)

### UI

- [ ] **T023** — Update `IDashboardService.GetDashboardStatsAsync` to accept `string? scope = null`.
- [ ] **T024** — Update `DashboardService` (UI side) impl to forward `?scope=platform` when provided.
- [ ] **T025** — Extend `DashboardStatsViewModel` (`Sorcha.UI.Components.User/Models/User/Dashboard/`): add `Scope` (string), `OrgId` (Guid?), and the org-only nullables (`ActiveUsers`, `PendingInvitations`, `SubscribedRegisters`, `RecentTransactions`). Make existing platform fields nullable.
- [ ] **T026** — In `Sorcha.UI.Web.Client/Pages/Home.razor`:
  - Add `MudButtonGroup` scope toggle (Org / Platform) at top-right of stats heading, wrapped in `<AuthorizeView Roles="SystemAdmin">`.
  - Bind toggle to `_scope`; on change, fetch stats with new scope and persist `_scope` in `localStorage["dashboard-scope-{platformUserId}"]`.
  - In `OnInitializedAsync`, read localStorage to restore prior selection (SystemAdmin only).
  - Two card-grid branches: `@if (_stats.Scope == "org") { 4 cards }` else `{ 6 cards (existing) }`.
- [ ] **T027** — `data-testid`s: `dashboard-scope-toggle`, `dashboard-scope-org`, `dashboard-scope-platform`, plus card test ids for the four org-view cards.
- [ ] **T028** — Playwright smoke at `tests/Sorcha.UI.E2E.Tests/Docker/DashboardScopeTests.cs`:
  - SystemAdmin sees toggle, can flip Org↔Platform, expected card counts.
  - Administrator (non-system) doesn't see toggle; renders 4 org-view cards.
- [ ] **T029** — Manual smoke on n1: `admin@sorcha.local` (SystemAdmin) toggles; `verification-admin@assured-identity.local` (org Administrator) sees 4 cards locked to org.

### PR-B wrap

- [ ] **T030** — Full `dotnet test`. Run E2E filtered.
- [ ] **T031** — Open PR-B, await CI + claude-review, squash-merge.
- [ ] **T032** — n1 deploy: pull `sorchadev/api-gateway:latest`, `sorchadev/tenant-service:latest`, `sorchadev/register-service:latest`, `sorchadev/ui-web:latest`. Recreate.
- [ ] **T033** — Validate SC-001 through SC-008 on n1; flip UX-005 to ✅ in MASTER-TASKS line 231; strike the M4 line in `2026-05-16-v1-release-roadmap.md`.

---

## Definition of done

- T001–T033 closed.
- SC-001 through SC-008 verified on n1.
- MASTER-TASKS UX-005 marked ✅ with PR refs.
- Roadmap M4 line for UX-005 struck or annotated.
