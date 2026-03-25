# Quickstart: Unified Organisation Management UI

**Branch**: `069-unified-org-management` | **Date**: 2026-03-25

## Prerequisites

```bash
# .NET 10 SDK, Docker Desktop, Node.js (for Playwright)
docker-compose up -d
# Verify: http://localhost:5400 (UI), http://localhost:80 (API Gateway)
# Login: admin@sorcha.local / Dev_Pass_2025!
```

## Implementation Order

### Phase 1: Backend — User List Enhancement (no UI changes yet)

1. **Enhance UserResponse DTO** — Add `EmailVerified`, `EmailVerifiedAt`, `ProvisionedVia`, `InvitedByUserId`, `ProfileCompleted`, `InvitationStatus` fields to `UserDtos.cs`
2. **Update repository queries** — Join UserIdentity → PlatformUser for email verification fields; join to OrgInvitation for invitation status
3. **Add filter parameters** — `emailVerified`, `provisionedVia`, `includePendingInvitations` to `GetOrganizationUsersAsync()`
4. **Add verify-email endpoint** — `POST /api/organizations/{orgId}/users/{userId}/verify-email`
5. **Unit tests** — Test filtering logic, verify-email override, authorization checks

```bash
# Verify backend
dotnet test tests/Sorcha.Tenant.Service.Tests --filter "FullyQualifiedName~Organization"
```

### Phase 2: UI Foundation — Page Structure + Navigation

1. **Update MainLayout.razor** — Move "Organisations" nav from System to Admin, remove "Platform Organisations" entry
2. **Refactor Organizations.razor** — Add route parameters `{orgId:guid?}`, role-based rendering
3. **Create OrgSelectorPanel.razor** — Wraps OrganizationList in MudExpansionPanel with compact mode
4. **Create CollapsibleStatsPanel.razor** — Overview stats with collapse to single-line
5. **Update OrganizationDashboard.razor** — Remove Overview tab (replaced by collapsible stats), remove Published tab, rename Configuration to Settings

```bash
# Quick smoke test
dotnet test tests/Sorcha.UI.E2E.Tests --filter "Category=Smoke"
```

### Phase 3: UI Enhancement — Users Tab + Participants Tab

1. **Enhance UserList.razor** — Add status filter chips (Active/Invited/Unverified), admin override buttons (Verify Email, Resend Invitation)
2. **Update UI service client** — Add `emailVerified`, `provisionedVia`, `includePendingInvitations` params + `VerifyEmailAsync()` method
3. **Enhance ParticipantList.razor** — Add publish status column, inline revoke action
4. **Delete PublishedParticipantsList.razor** — Functionality absorbed into ParticipantList

### Phase 4: Deep Links + User Detail Page

1. **Create OrganizationUserDetail.razor** — `/admin/organizations/{orgId}/users/{userId}` page
2. **Create UserDetailPage.cs** — Playwright page object
3. **E2E tests** — Deep-link navigation, access control, user editing

### Phase 5: E2E Tests + Cleanup

1. **Update AdminOrganizationsTests.cs** — Tests for unified page, org selector, tab navigation
2. **Add new test classes** — User status filtering, admin overrides, deep links
3. **Delete PlatformOrganizations.razor** — All functionality migrated
4. **Update TestConstants.cs** — New route constants

```bash
# Full E2E test suite
dotnet test tests/Sorcha.UI.E2E.Tests --filter "Category=Docker"
```

## Key Files to Touch

| Priority | File | Change |
|----------|------|--------|
| 1 | `Tenant.Service/Models/Dtos/UserDtos.cs` | Add fields to UserResponse |
| 1 | `Tenant.Service/Endpoints/OrganizationEndpoints.cs` | Filter params + verify-email endpoint |
| 2 | `UI.Web.Client/Components/Layout/MainLayout.razor` | Nav restructure |
| 2 | `UI.Web.Client/Pages/Admin/Organizations.razor` | Unified page with route params |
| 2 | `UI.Core/Components/Admin/OrgSelectorPanel.razor` | New: collapsible org picker |
| 2 | `UI.Core/Components/Admin/CollapsibleStatsPanel.razor` | New: collapsible overview |
| 3 | `UI.Core/Components/Admin/UserList.razor` | Status filters + overrides |
| 3 | `UI.Core/Components/Participants/ParticipantList.razor` | Inline publish status |
| 4 | `UI.Web.Client/Pages/Admin/OrganizationUserDetail.razor` | New: deep-linked user page |
| 5 | `UI.E2E.Tests/Docker/AdminOrganizationsTests.cs` | Updated E2E tests |

## Verification

```bash
# 1. Backend tests
dotnet test tests/Sorcha.Tenant.Service.Tests

# 2. UI component tests
dotnet test tests/Sorcha.UI.Core.Tests

# 3. E2E smoke tests
dotnet test tests/Sorcha.UI.E2E.Tests --filter "Category=Smoke"

# 4. Full E2E suite
dotnet test tests/Sorcha.UI.E2E.Tests --filter "Category=Docker"

# 5. Manual verification
# - Login as admin@sorcha.local → Admin > Organisations
# - Verify org selector panel (system admin)
# - Verify Users tab filters
# - Verify deep link: /app/admin/organizations/{orgId}
```
