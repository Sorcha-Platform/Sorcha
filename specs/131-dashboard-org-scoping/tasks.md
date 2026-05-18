# Tasks — Dashboard org-scoping (UX-005, Feature 131)

**Spec:** `spec.md`
**Plan:** `plan.md`

## PR-A — Backend filters

### Wallet Service

- [ ] **T001** — Add `Task<int> CountAsync(string? tenantId = null, CancellationToken ct = default)` overload to `IWalletRepository`.
- [ ] **T002** — Implement the overload in the EF Core repository; filter on `Tenant` column when set.
- [ ] **T003** — Update `/api/stats` endpoint at `Sorcha.Wallet.Service/Program.cs:315` to accept `[FromQuery] Guid? orgId` and pass it as `tenantId.ToString()` when set.
- [ ] **T004** — Unit tests in `Sorcha.Wallet.Service.Tests`: orgId-filter case + unscoped case.

### Blueprint Service

- [ ] **T005** — Confirm `BlueprintRecord` (or the equivalent storage row) carries an org id; add an index on it if missing.
- [ ] **T006** — Add `CountByOrgAsync(Guid orgId)` to the blueprint count surface (or extend the existing count call to accept an optional org filter).
- [ ] **T007** — Update `/api/stats` at `Sorcha.Blueprint.Service/Program.cs:2322` to accept `[FromQuery] Guid? orgId` and forward.
- [ ] **T008** — Same shape for `instanceCount` + `activeInstanceCount` if applicable (filter via instance's blueprint→org link).
- [ ] **T009** — Unit tests in `Sorcha.Blueprint.Service.Tests`.

### Register Service

- [ ] **T010** — In `Sorcha.Register.Service/Program.cs:3105`, accept `[FromQuery] Guid? orgId`.
- [ ] **T011** — When orgId is set, Register Service calls Tenant Service (`ITenantSubscriptionClient.ListSubscribedRegistersAsync(orgId)` or the equivalent existing s2s client) for the org's subscribed active register ids; return `registerCount = subscribed.Count` and `transactionCount = sum of per-register transactionCount across subscribed`. Decision: Register→Tenant call shape, not gateway-orchestrated — keeps the gateway endpoint as a single fan-out and isolates the org-scoping logic inside Register Service. Tracked: Tenant becomes a downstream dep of Register for this endpoint; document in `Sorcha.Register.Service/README.md`.
- [ ] **T012** — When orgId is null, return the existing platform-wide shape.
- [ ] **T013** — Unit tests: mock the subscription client; verify cross-service read path; verify unscoped path.

### Tenant Service

- [ ] **T014** — Confirm `/api/organizations/stats` accepts `?orgId=` and returns `{ totalOrganizations = 1, totalUsers = orgUsers }` for that org. If not, extend.
- [ ] **T015** — Unit tests for both shapes.

### PR-A wrap

- [ ] **T016** — Run full `dotnet test` for all four service test projects.
- [ ] **T017** — Open PR-A, await CI green + claude-review, squash-merge.

---

## PR-B — Gateway + UI

### Gateway

- [ ] **T018** — In `Sorcha.ApiGateway/Program.cs:151` change `app.MapGet("/api/dashboard", …)` to `.RequireAuthorization()`. Accept `[FromQuery] string? scope` and `HttpContext`.
- [ ] **T019** — Extract `org_id` (claim type from `Sorcha.ServiceDefaults.Auth`), `platform_user_id`, role from `HttpContext.User`. Compute effective scope: `platform` iff `scope=="platform" && context.User.IsInRole("SystemAdmin")`; else `org` with the claim's org id.
- [ ] **T020** — Refactor `DashboardStatisticsService.GetDashboardStatisticsAsync` signature: `(Guid? orgId, CancellationToken ct)`. Append `?orgId={orgId}` to each backend call when set; omit when null.
- [ ] **T021** — Extend `DashboardStatistics` DTO with `Scope` (string) + `OrgId` (Guid?). Mark `ConnectedPeers` + `TotalOrganizations` as nullable; configure `JsonIgnoreCondition.WhenWritingNull` on the response.
- [ ] **T022** — Gateway integration tests in `Sorcha.ApiGateway.Tests`:
  - anonymous → 401
  - non-admin user → org scope; the two platform-only fields omitted
  - SystemAdmin no toggle → org scope
  - SystemAdmin `?scope=platform` → platform scope; all six fields populated
  - non-admin `?scope=platform` → org scope (silent ignore); response `scope: "org"`

### UI

- [ ] **T023** — Update `IDashboardService.GetDashboardStatsAsync` signature to accept `string? scope = null`.
- [ ] **T024** — Update `DashboardService` impl to forward `?scope=platform` to the gateway when "platform".
- [ ] **T025** — Extend `DashboardStatsViewModel` (`Sorcha.UI.Components.User/Models/User/Dashboard/`): add `Scope`, `OrgId`, make `ConnectedPeers` + `TotalOrganizations` nullable.
- [ ] **T026** — In `Sorcha.UI.Web.Client/Pages/Home.razor`:
  - Render a `MudButtonGroup` scope toggle (`Org` / `Platform`) at the top-right of the stats grid, wrapped in `<AuthorizeView Roles="SystemAdmin">`.
  - Bind toggle to `_scope` field; on change, reload stats and persist `_scope` in `localStorage["dashboard-scope-{platform_user_id}"]`.
  - On `OnInitializedAsync`, read localStorage to restore prior selection.
  - Hide ConnectedPeers + TotalOrganizations cards when `_stats.Scope == "org"`.
- [ ] **T027** — Add `data-testid` attributes: `dashboard-scope-toggle`, `dashboard-scope-org`, `dashboard-scope-platform`, `dashboard-card-peers`, `dashboard-card-organizations`.
- [ ] **T028** — Playwright smoke at `tests/Sorcha.UI.E2E.Tests/Docker/DashboardScopeTests.cs`:
  - SystemAdmin sees toggle, can flip Org↔Platform, six-vs-four cards.
  - Administrator does not see the toggle; sees four cards.
- [ ] **T029** — Manual smoke on n1: `admin@sorcha.local` (SystemAdmin) flips toggle; `verification-admin@assured-identity.local` (Administrator of an org) sees scope-locked org view.

### PR-B wrap

- [ ] **T030** — Run full `dotnet test` + `dotnet test --filter Category=Dashboard` for E2E.
- [ ] **T031** — Open PR-B, await CI + claude-review, squash-merge.
- [ ] **T032** — n1 deploy: pull `sorchadev/ui-web:latest`, `sorchadev/api-gateway:latest`, `sorchadev/wallet-service:latest`, `sorchadev/blueprint-service:latest`, `sorchadev/register-service:latest`, `sorchadev/tenant-service:latest` (any service whose image changed). Recreate.
- [ ] **T033** — On n1: validate SC-001 through SC-008 from `spec.md`. Mark UX-005 ✅ in MASTER-TASKS line 231.

---

## Definition of done

- Tasks T001–T033 closed.
- SC-001 through SC-008 in `spec.md` verified on n1.
- MASTER-TASKS UX-005 row marked ✅ with PR refs.
- Roadmap M4 line for UX-005 either struck (if combined with the docs-strike PR) or annotated with the PR refs.
