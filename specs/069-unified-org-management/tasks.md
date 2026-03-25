# Tasks: Unified Organisation Management UI

**Input**: Design documents from `/specs/069-unified-org-management/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/user-endpoints.yaml

**Tests**: E2E tests included per the sorcha-ui workflow (test-driven page development). Unit tests included for backend changes.

**Organization**: Tasks grouped by user story. Backend foundational work in Phase 2 unblocks all UI stories.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- Include exact file paths in descriptions

---

## Phase 1: Setup

**Purpose**: Route constants, test infrastructure updates

- [x] T001 Add route constants for new pages in `tests/Sorcha.UI.E2E.Tests/Infrastructure/TestConstants.cs` — add `AdminOrganizationDetail = "/app/admin/organizations/{orgId}"` and `AdminUserDetail = "/app/admin/organizations/{orgId}/users/{userId}"`
- [x] T002 [P] Add `EmailVerifiedByAdmin` value to `AuditEventType` enum in `src/Services/Sorcha.Tenant.Service/Models/AuditLogEntry.cs`

---

## Phase 2: Foundational (Backend — Blocking Prerequisites)

**Purpose**: Enhanced user list API with filtering, email verification override, and DTOs that all UI stories depend on

**CRITICAL**: No UI user-management work (US3, US6) can begin until this phase is complete. US1, US2, US4, US5 can begin after T003 only.

### DTOs and Models

- [x] T003 Enhance `UserResponse` record in `src/Services/Sorcha.Tenant.Service/Models/Dtos/UserDtos.cs` — add `EmailVerified` (bool), `EmailVerifiedAt` (DateTimeOffset?), `ProvisionedVia` (string), `InvitedByUserId` (Guid?), `ProfileCompleted` (bool), `InvitationStatus` (string?) fields. Update `FromEntity()` to accept PlatformUser and OrgInvitation data.
- [x] T004 [P] Create `PendingInvitationResponse` record in `src/Services/Sorcha.Tenant.Service/Models/Dtos/UserDtos.cs` — fields: Email, AssignedRole, InvitationStatus, InvitedByUserId, ExpiresAt, CreatedAt. Add `FromEntity(OrgInvitation)` factory method.
- [x] T005 [P] Enhance `UserListResponse` in `src/Services/Sorcha.Tenant.Service/Models/Dtos/UserDtos.cs` — add `PendingInvitations` (IReadOnlyList\<PendingInvitationResponse\>) and `PendingInvitationCount` (int) fields.

### Repository Layer

- [x] T006 Update `IIdentityRepository` interface in `src/Services/Sorcha.Tenant.Service/Data/Repositories/IIdentityRepository.cs` — add `GetUsersWithFiltersAsync(Guid orgId, bool includeInactive, bool? emailVerified, string? provisionedVia, CancellationToken ct)` method signature.
- [x] T007 Implement `GetUsersWithFiltersAsync` in `src/Services/Sorcha.Tenant.Service/Data/Repositories/IdentityRepository.cs` — join UserIdentity → PlatformUser (via PlatformUserId) for EmailVerified fields, join to OrgInvitation (via Email+OrgId) for InvitationStatus. Apply filters conditionally.

### Service Layer

- [x] T008 Update `IOrganizationService` interface in `src/Services/Sorcha.Tenant.Service/Services/IOrganizationService.cs` — update `GetOrganizationUsersAsync` signature to accept `bool? emailVerified`, `string? provisionedVia`, `bool includePending`. Add `AdminVerifyEmailAsync(Guid orgId, Guid userId, Guid adminUserId)` method.
- [x] T009 Implement enhanced `GetOrganizationUsersAsync` in `src/Services/Sorcha.Tenant.Service/Services/OrganizationService.cs` — call new repository method, populate enhanced UserResponse with PlatformUser join data, optionally fetch pending OrgInvitations.
- [x] T010 Implement `AdminVerifyEmailAsync` in `src/Services/Sorcha.Tenant.Service/Services/OrganizationService.cs` — validate user exists in org, check not already verified, set PlatformUser.EmailVerified=true and EmailVerifiedAt=UtcNow, clear VerificationToken and VerificationTokenExpiresAt, record `EmailVerifiedByAdmin` audit event. Return 400 if already verified, 404 if user not found.

### Endpoints

- [x] T011 Update `GET /api/organizations/{orgId}/users` endpoint in `src/Services/Sorcha.Tenant.Service/Endpoints/OrganizationEndpoints.cs` — add optional query parameters `emailVerified` (bool?), `provisionedVia` (string?), `includePending` (bool). Pass to service layer. Add `.WithDescription()` for new params.
- [x] T012 Add `POST /api/organizations/{orgId}/users/{userId}/verify-email` endpoint in `src/Services/Sorcha.Tenant.Service/Endpoints/OrganizationEndpoints.cs` — RequireAdministrator policy, call `AdminVerifyEmailAsync`, return 204/400/403/404. Add `.WithName("AdminVerifyEmail")`, `.WithSummary()`, `.WithDescription()`.

### Backend Unit Tests

- [x] T013 [P] Write unit tests for enhanced `GetOrganizationUsersAsync` in `tests/Sorcha.Tenant.Service.Tests/Services/OrganizationServiceTests.cs` — test: no filters (backwards compatible), emailVerified=true filter, emailVerified=false filter, provisionedVia filter, includePending=true, combined filters.
- [x] T014 [P] Write unit tests for `AdminVerifyEmailAsync` in `tests/Sorcha.Tenant.Service.Tests/Services/OrganizationServiceTests.cs` — test: success case, already-verified returns 400, user not in org returns 404, audit event recorded, VerificationToken cleared.

**Checkpoint**: Backend API enhanced. Existing callers unaffected (backwards compatible). New filtering and verify-email endpoint ready.

---

## Phase 3: User Story 1 — Org Admin Manages Their Organisation (P1) MVP

**Goal**: Org admins land directly on their org dashboard with collapsible stats and 3 tabs (Users, Participants, Settings)

**Independent Test**: Login as org admin → navigate to `/admin/organizations` → dashboard loads with org name, stats panel, and tabs

### Navigation & Page Structure

- [x] T015 Update navigation in `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Components/Layout/MainLayout.razor` — move "Organisations" from Admin > System section to a new top-level Admin section (between Administration and Identity). Remove "Platform Organisations" nav item. Authorize for `Administrator,OrganizationAdmin,SystemAdmin`.
- [x] T016 Refactor `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/Admin/Organizations.razor` — add `@page "/admin/organizations/{OrgId:guid?}"` route. For org admins (non-SystemAdmin): auto-load their org via service client, bypass org selector. For system admins: show org selector (Phase 4/US2). Pass org data to dashboard component.

### Collapsible Stats Panel

- [x] T017 Create `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Admin/CollapsibleStatsPanel.razor` — MudExpansionPanel that expands to show stat cards (UserCount, ParticipantCount, PublishedParticipantCount, InvitedUserCount, UnverifiedUserCount) and collapses to single-line summary "12 Users | 8 Participants | 3 Published". Accept `OrganizationDashboardViewModel` parameter and `bool IsExpanded`.
- [x] T018 [P] Update `src/Apps/Sorcha.UI/Sorcha.UI.Core/Models/Admin/OrganizationDashboardViewModel.cs` — add `InvitedUserCount` (int) and `UnverifiedUserCount` (int) properties.

### Dashboard Refactor

- [x] T019 Refactor `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Admin/OrganizationDashboard.razor` — remove Overview tab (replaced by CollapsibleStatsPanel above tabs), remove Published tab (merged into Participants in US4), rename Configuration tab to "Settings". Wire CollapsibleStatsPanel with dashboard stats. Keep 3 tabs: Users, Participants, Settings.

### E2E Tests

- [ ] T020 Update page object `tests/Sorcha.UI.E2E.Tests/PageObjects/AdminPages/OrganizationsPage.cs` — add locators for: CollapsibleStatsPanel (expanded/collapsed), tab buttons (Users/Participants/Settings), org name header. Add methods: `GetStatsText()`, `ToggleStatsPanel()`, `ClickTab(string tabName)`.
- [ ] T021 Update E2E tests in `tests/Sorcha.UI.E2E.Tests/Docker/AdminOrganizationsTests.cs` — test: page loads with org name header, stats panel shows counts, stats panel collapses to summary, Users/Participants/Settings tabs are visible and clickable, org admin cannot access other org's dashboard via URL.

**Checkpoint**: Org admins see unified dashboard with collapsible stats and 3 tabs. System admin view not yet built.

---

## Phase 4: User Story 2 — System Admin Selects and Manages Any Organisation (P1)

**Goal**: System admins see a collapsible org selector panel at the top, can search/filter/select orgs, and create new ones

**Independent Test**: Login as system admin → navigate to `/admin/organizations` → org selector shows all orgs → select one → dashboard loads below

### Org Selector Panel

- [x] T022 Add compact mode to `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Admin/OrganizationList.razor` — add `bool CompactMode` parameter. When true: hide Edit/Deactivate action buttons, show only Name/Status/UserCount columns, single-click row selects org (via `OnOrganizationSelected` callback). Keep existing behaviour when CompactMode=false.
- [x] T023 Create `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Admin/OrgSelectorPanel.razor` — MudExpansionPanel wrapper. Expanded: shows OrganizationList in CompactMode with search input and status filter (Active/Suspended/All) and "Create Organisation" button. Collapsed: shows "Managing: [OrgName]" with expand icon. Accept parameters: `OrganizationDto? SelectedOrg`, `EventCallback<OrganizationDto> OnOrganizationSelected`, `bool IsExpanded`. Clicking "Create Organisation" opens existing OrganizationForm dialog.

### Page Integration

- [x] T024 Update `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/Admin/Organizations.razor` — for SystemAdmin role: render OrgSelectorPanel at top, pass selected org to dashboard below. Handle deep-link: if `OrgId` route param set, pre-select that org and collapse selector. Wire create org callback to refresh list after creation.
- [x] T025 [P] Update UI service client `src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/Admin/PlatformOrgAdminService.cs` — ensure `ListOrganizationsAsync` supports search text and status filter parameters for the org selector panel.

### E2E Tests

- [ ] T026 Update E2E tests in `tests/Sorcha.UI.E2E.Tests/Docker/AdminOrganizationsTests.cs` — add tests: system admin sees org selector panel, search filters org list, status filter works, selecting org loads dashboard below, collapse shows "Managing: [name]", expand returns to full list, deep-link `/admin/organizations/{orgId}` pre-selects org.

### Cleanup

- [x] T042 Delete `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/Admin/PlatformOrganizations.razor` — all functionality now absorbed into unified page. Remove any remaining imports or references. Add a redirect or 404 for the old `/admin/platform-organizations` route if navigated directly.

**Checkpoint**: System admins can browse, search, filter, select, and create organisations from a single page. Old Platform Organisations page removed.

---

## Phase 5: User Story 3 — Admin Manages Users with Status Filtering and Overrides (P1)

**Goal**: Users tab shows composite status (Active/Invited/Unverified), supports filtering, and provides admin override actions (verify email, resend invitation)

**Independent Test**: Navigate to Users tab → filter by Unverified → click Verify Email on a user → status updates to Active

### UI Service Client Updates

- [x] T027 Update `src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/IOrganizationAdminService.cs` — add `VerifyEmailAsync(Guid orgId, Guid userId)` method. Update `GetOrganizationUsersAsync` signature to accept `bool? emailVerified`, `string? provisionedVia`, `bool includePending` parameters. Add `EmailVerified`, `EmailVerifiedAt`, `ProvisionedVia`, `InvitedByUserId`, `ProfileCompleted`, `InvitationStatus` to `UserDto`. Add `PendingInvitationDto` record and `PendingInvitations`/`PendingInvitationCount` to `UserListResult`.
- [x] T028 Implement new methods in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/OrganizationAdminService.cs` — `VerifyEmailAsync` calls `POST /api/organizations/{orgId}/users/{userId}/verify-email`. Update `GetOrganizationUsersAsync` to pass new query params. Map enhanced response fields.

### Invitation Resend Verification

- [x] T028b Verify invitation resend endpoint exists and is wired in the UI service client. Check that `POST /api/organizations/{orgId}/invitations/{invitationId}/resend` (or equivalent) is implemented in `src/Services/Sorcha.Tenant.Service/Endpoints/` and that `IOrganizationAdminService` exposes a `ResendInvitationAsync` method. If missing, implement the endpoint and service client method. This unblocks the "Resend Invitation" button in T029.

### Enhanced User List

- [x] T029 Enhance `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Admin/UserList.razor` — add status filter chips above table: "All", "Active", "Invited", "Unverified" (MudChipSet with single selection). Display composite status column with colour-coded MudChips (Active=green, Invited=blue, Unverified=orange, Suspended=red). Add "Verify Email" action button (visible when Unverified, calls `VerifyEmailAsync`, refreshes list). Add "Resend Invitation" button (visible when Invited, calls existing invitation resend). Show pending invitations in the same table when "Invited" filter selected. Add `data-testid` attributes: `user-status-filter`, `user-status-chip-{status}`, `verify-email-btn-{userId}`, `resend-invite-btn-{email}`.

### E2E Tests

- [ ] T030 Update page object `tests/Sorcha.UI.E2E.Tests/PageObjects/AdminPages/OrganizationsPage.cs` — add locators for: status filter chips, verify email buttons, resend invitation buttons, user status chips. Add methods: `FilterByStatus(string status)`, `ClickVerifyEmail(string userId)`, `GetUserStatusChip(string userName)`.
- [ ] T031 Add E2E tests for user management in `tests/Sorcha.UI.E2E.Tests/Docker/AdminOrganizationsTests.cs` — test: user list shows status chips, filter by Active shows only active users, filter by All shows all users, status filter counts match stats panel. (Note: verify-email and resend-invitation E2E tests depend on having unverified/invited test users in Docker seed data — add test stubs with skip if no test data.)

**Checkpoint**: Users tab fully functional with status filtering and admin overrides.

---

## Phase 6: User Story 4 — Admin Manages Participants with Inline Publish Status (P2)

**Goal**: Participants tab shows publish status inline (Draft/Published/Revoked) with publish and revoke actions. Published tab removed.

**Independent Test**: Navigate to Participants tab → see publish status for each participant → publish a draft → status updates to Published

### View Model Updates

- [x] T032 Enhance `ParticipantListItemViewModel` in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Models/Participants/ParticipantViewModels.cs` — add `PublishStatus` (string?), `PublishedRegisterName` (string?), `PublishedAt` (DateTimeOffset?), `PublishedVersion` (int?) fields.

### Participant List Enhancement

- [x] T033 Enhance `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Participants/ParticipantList.razor` — add "Publish Status" column showing colour-coded MudChips (None=grey, Published=green, Revoked=red). Add inline "Revoke" action button (visible when Published, confirmation dialog, calls `PublishingService.RevokeAsync`). On data load: fetch published records from register endpoints, merge with participant list by matching ParticipantId. Show register name and version in tooltip or expandable detail. Add `data-testid` attributes: `participant-publish-status-{id}`, `revoke-btn-{id}`.

### Cleanup

- [x] T034 Delete `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Admin/PublishedParticipantsList.razor` — all functionality absorbed into ParticipantList. Remove any references from OrganizationDashboard.razor (already removed in T019).

### E2E Tests

- [ ] T035 Add E2E tests for participants in `tests/Sorcha.UI.E2E.Tests/Docker/AdminOrganizationsTests.cs` — test: Participants tab shows publish status column, published participants show green chip, unpublished show grey chip. (Publish/revoke E2E tests depend on having wallet-linked test participants — add test stubs with skip if no test data.)

**Checkpoint**: Single Participants tab with inline publish status replaces separate Participants + Published tabs.

---

## Phase 7: User Story 6 — Deep-Linked User Detail View (P2)

**Goal**: `/admin/organizations/{orgId}/users/{userId}` loads a full user detail/edit page with org context

**Independent Test**: Navigate directly to deep-linked URL → correct org and user load → can edit and save user details

### User Detail Page

- [x] T036 Create `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/Admin/OrganizationUserDetail.razor` — `@page "/admin/organizations/{OrgId:guid}/users/{UserId:guid}"`. Authorize for Administrator, OrganizationAdmin, SystemAdmin. Load org via service client, validate org access (org admin: must be their org). Load user via `GetOrganizationUserAsync`. Display: user info header (name, email, status chip), editable fields (DisplayName, Role selector, Status toggle). Show email verification status with "Verify Email" override button if unverified. Save via existing `UpdateOrganizationUserAsync`. Breadcrumbs: Admin > Organisations > [Org Name] > Users > [User Name]. Add `data-testid` attributes: `user-detail-name`, `user-detail-email`, `user-detail-status`, `user-detail-save-btn`, `user-detail-verify-btn`.

### Page Object & E2E Tests

- [ ] T037 Create page object `tests/Sorcha.UI.E2E.Tests/PageObjects/AdminPages/UserDetailPage.cs` — locators for: user name, email, status chip, role selector, save button, verify email button, breadcrumbs. Methods: `NavigateAsync(Guid orgId, Guid userId)`, `GetUserName()`, `GetStatus()`, `ClickSave()`, `ClickVerifyEmail()`.
- [ ] T038 Create E2E tests `tests/Sorcha.UI.E2E.Tests/Docker/AdminUserDetailTests.cs` — `[Category("Docker")]`, `[Category("Authenticated")]`, `[Category("Admin")]`. Tests: deep-link loads correct user, breadcrumbs show correct hierarchy, edit display name and save persists, org admin cannot access user in different org (403 handling), system admin can access any org's users.

**Checkpoint**: Deep-linked user detail pages work with proper org-scoped access control.

---

## Phase 8: User Story 5 — Admin Configures Organisation Settings (P3)

**Goal**: Settings tab (renamed from Configuration) manages branding and security policies

**Independent Test**: Navigate to Settings tab → modify branding → save → reload → changes persist

- [x] T039 [US5] Rename tab label from "Configuration" to "Settings" in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Admin/OrganizationDashboard.razor` — update the MudTabPanel text. (Already partially done in T019 if tab restructure happened there; verify and finalize.)
- [x] T040 [US5] Add `data-testid="settings-tab"` to the Settings tab panel and `data-testid="settings-save-btn"` to the save button in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Admin/OrganizationConfiguration.razor`.
- [ ] T041 [US5] Add E2E test in `tests/Sorcha.UI.E2E.Tests/Docker/AdminOrganizationsTests.cs` — test: Settings tab loads, branding fields are editable, save button persists changes.

**Checkpoint**: Settings tab functional with existing branding and security policy features.

---

## Phase 9: Polish & Cross-Cutting Concerns

**Purpose**: Cleanup, edge cases, documentation

- [x] T043 [P] Handle edge cases in `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/Admin/Organizations.razor` — suspended org shows read-only banner, non-existent org ID shows "not found" with back link, org admin accessing other org shows access denied message.
- [x] T044 [P] Handle edge cases in `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/Admin/OrganizationUserDetail.razor` — non-existent user shows "user not found", last admin role change prevention, already-verified hides verify button.
- [x] T045 [P] Update `src/Services/Sorcha.Tenant.Service/README.md` — document new `verify-email` endpoint, enhanced user list filters, `EmailVerifiedByAdmin` audit event.
- [x] T046 [P] Update `docs/reference/API-DOCUMENTATION.md` — add verify-email endpoint documentation, updated user list query parameters.
- [ ] T047 Run full E2E test suite and fix any regressions: `dotnet test tests/Sorcha.UI.E2E.Tests --filter "Category=Docker"`
- [ ] T048 Run full backend test suite: `dotnet test tests/Sorcha.Tenant.Service.Tests`
- [ ] T049 Run quickstart.md validation — manual verification of all 5 steps in `specs/069-unified-org-management/quickstart.md`

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 (Setup)**: No dependencies — start immediately
- **Phase 2 (Foundational/Backend)**: Depends on Phase 1 — BLOCKS US3 and US6
- **Phase 3 (US1)**: Depends on Phase 1 only — can start in parallel with Phase 2
- **Phase 4 (US2)**: Depends on Phase 3 (needs page structure from US1)
- **Phase 5 (US3)**: Depends on Phase 2 (backend) + Phase 3 (page structure)
- **Phase 6 (US4)**: Depends on Phase 3 (dashboard refactor removed Published tab)
- **Phase 7 (US6)**: Depends on Phase 2 (backend) + Phase 3 (page structure)
- **Phase 8 (US5)**: Depends on Phase 3 (tab rename done in dashboard refactor)
- **Phase 9 (Polish)**: Depends on all desired user stories being complete

**Note**: T042 (delete PlatformOrganizations.razor) moved from Phase 9 to Phase 4 to prevent stale page from being accessible after nav removal.

### User Story Dependencies

```
Phase 1 (Setup)
    │
    ├─── Phase 2 (Backend) ──┬── Phase 5 (US3: User Filtering)
    │                         └── Phase 7 (US6: Deep Links)
    │
    └─── Phase 3 (US1: Org Admin Dashboard) ─┬── Phase 4 (US2: Sys Admin Selector)
                                              ├── Phase 5 (US3: User Filtering)
                                              ├── Phase 6 (US4: Participants Merge)
                                              ├── Phase 7 (US6: Deep Links)
                                              └── Phase 8 (US5: Settings)
```

### Parallel Opportunities

- **T002** can run in parallel with **T001** (different files)
- **T004, T005** can run in parallel with each other (same file but different records)
- **T013, T014** can run in parallel (different test methods, same test file)
- **Phase 2 backend** and **Phase 3 US1** can run in parallel (different project boundaries)
- **Phase 6 (US4)** and **Phase 7 (US6)** can run in parallel after Phase 3
- **Phase 8 (US5)** can run in parallel with US4 or US6

---

## Parallel Example: Phases 2 + 3

```bash
# These can execute simultaneously:

# Stream A: Backend (Phase 2)
Task: "Enhance UserResponse DTO in UserDtos.cs"
Task: "Create PendingInvitationResponse in UserDtos.cs"
Task: "Update repository with filtered queries"
Task: "Add verify-email endpoint"
Task: "Write backend unit tests"

# Stream B: UI Foundation (Phase 3 - US1)
Task: "Update MainLayout.razor navigation"
Task: "Create CollapsibleStatsPanel.razor"
Task: "Refactor OrganizationDashboard.razor"
Task: "Refactor Organizations.razor page"
Task: "Update E2E tests for unified page"
```

---

## Implementation Strategy

### MVP First (US1 Only)

1. Complete Phase 1: Setup (T001-T002)
2. Complete Phase 3: US1 — Org Admin Dashboard (T015-T021)
3. **STOP and VALIDATE**: Org admin can use unified page with 3 tabs and collapsible stats
4. Deploy/demo if ready — org admins get immediate value

### Incremental Delivery

1. Phase 1 + Phase 3 (US1) → Org admin dashboard works → **Demo**
2. Phase 2 (Backend) + Phase 4 (US2) → System admin org selector works → **Demo**
3. Phase 5 (US3) → User filtering + admin overrides → **Demo** (critical for walkthroughs)
4. Phase 6 (US4) → Participants merged → **Demo**
5. Phase 7 (US6) → Deep links work → **Demo**
6. Phase 8 (US5) + Phase 9 (Polish) → Settings + cleanup → **Release**

### Recommended Execution (Single Developer)

Phase 1 → Phase 2 + Phase 3 (parallel) → Phase 4 → Phase 5 → Phase 6 → Phase 7 → Phase 8 → Phase 9

---

## Notes

- [P] tasks = different files, no dependencies
- [Story] labels: US1-US6 map to spec.md user stories
- E2E tests follow sorcha-ui skill workflow: Page Object → Tests → Implementation
- Backend tests use xUnit + FluentAssertions + Moq per constitution
- Commit after each phase checkpoint
- Docker must be running for E2E tests: `docker-compose up -d`
