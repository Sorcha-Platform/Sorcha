# Implementation Plan: Unified Organisation Management UI

**Branch**: `069-unified-org-management` | **Date**: 2026-03-25 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/069-unified-org-management/spec.md`

## Summary

Consolidate `/admin/organizations` and `/admin/platform-organizations` into a single role-aware page. System admins get a collapsible org selector panel (vertically stacked); org admins land directly on their org dashboard. Enhance the Users tab with status filtering (Active/Invited/Unverified) and admin override actions (email verification bypass). Merge the Participants and Published tabs into one with inline publish status. Add deep-link routing for `/admin/organizations/{orgId}` and `/admin/organizations/{orgId}/users/{userId}`.

## Technical Context

**Language/Version**: C# 13 / .NET 10
**Primary Dependencies**: MudBlazor 8.15.0, Blazor WASM (InteractiveWebAssembly), Entity Framework Core, Playwright (.NET)
**Storage**: PostgreSQL (Tenant Service — per-org schema), MongoDB (Register Service — published participants)
**Testing**: xUnit + FluentAssertions + Moq (unit), Playwright NUnit (E2E)
**Target Platform**: Blazor WASM (browser) + .NET 10 backend services
**Project Type**: Web application (Blazor WASM frontend + microservice backend)
**Performance Goals**: Page load under 2s, org selector search under 500ms, user list filtering instant (client-side)
**Constraints**: Must reuse existing components; no new microservices; maintain existing API contracts
**Scale/Scope**: ~5 Blazor pages/components modified, ~3 backend endpoints added/modified, ~30 test files affected

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Microservices-First | PASS | No new services. Changes within existing Tenant Service and UI boundaries. |
| II. Security First | PASS | Admin override requires Administrator role. Org-scoped access enforced. Deep-link auth checks. |
| III. API Documentation | PASS | New/modified endpoints will have XML docs + Scalar OpenAPI descriptions. |
| IV. Testing Requirements | PASS | E2E tests for every page change (sorcha-ui workflow). Unit tests for backend changes. >85% target. |
| V. Code Quality | PASS | Async/await, DI, nullable reference types, no warnings. |
| VI. Blueprint Standards | N/A | No blueprint changes. |
| VII. Domain-Driven Design | PASS | Using established terms: Participant (not user), Organisation. |
| VIII. Observability | PASS | Audit logging already in place for all user/org operations. |

**Gate result: PASS — no violations.**

## Project Structure

### Documentation (this feature)

```text
specs/069-unified-org-management/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output
│   └── user-endpoints.yaml
└── tasks.md             # Phase 2 output (/speckit.tasks)
```

### Source Code (repository root)

```text
src/
├── Apps/Sorcha.UI/
│   ├── Sorcha.UI.Web.Client/
│   │   ├── Pages/Admin/
│   │   │   ├── Organizations.razor          # MODIFY: unified page with org selector + deep links
│   │   │   ├── OrganizationUserDetail.razor # NEW: deep-linked user detail page
│   │   │   └── PlatformOrganizations.razor  # DELETE: absorbed into Organizations.razor
│   │   └── Components/Layout/
│   │       └── MainLayout.razor             # MODIFY: navigation restructure
│   └── Sorcha.UI.Core/
│       ├── Components/Admin/
│       │   ├── OrganizationDashboard.razor   # MODIFY: remove Overview/Published tabs, add collapsible stats
│       │   ├── OrganizationList.razor        # MODIFY: add compact mode for org selector panel
│       │   ├── OrgSelectorPanel.razor        # NEW: collapsible org selector wrapper
│       │   ├── CollapsibleStatsPanel.razor   # NEW: collapsible overview stats
│       │   ├── OrganizationConfiguration.razor # RENAME label to "Settings" (minimal change)
│       │   ├── OrganizationForm.razor        # NO CHANGE
│       │   ├── UserList.razor                # MODIFY: add status filters + admin override actions
│       │   ├── UserForm.razor                # MODIFY: add email verification override
│       │   └── PublishedParticipantsList.razor # DELETE: merged into ParticipantList
│       ├── Components/Participants/
│       │   └── ParticipantList.razor         # MODIFY: add inline publish status column + revoke action
│       ├── Models/Admin/
│       │   └── OrganizationDashboardViewModel.cs # MODIFY: add invitation/verification stats
│       ├── Models/Participants/
│       │   └── ParticipantViewModels.cs      # MODIFY: add publish status fields
│       └── Services/
│           ├── IOrganizationAdminService.cs  # MODIFY: add user filtering params, email verify method
│           ├── OrganizationAdminService.cs   # MODIFY: implement new methods
│           └── Admin/
│               └── PlatformOrgAdminService.cs # MODIFY: consolidate into unified service or compose
├── Services/Sorcha.Tenant.Service/
│   ├── Endpoints/
│   │   └── OrganizationEndpoints.cs         # MODIFY: add filter params, email verify endpoint
│   ├── Models/Dtos/
│   │   └── UserDtos.cs                      # MODIFY: add EmailVerified, InvitationStatus to UserResponse
│   ├── Services/
│   │   ├── OrganizationService.cs           # MODIFY: user query with filters + PlatformUser join
│   │   └── IOrganizationService.cs          # MODIFY: update interface
│   └── Data/Repositories/
│       ├── IIdentityRepository.cs           # MODIFY: add filtered query methods
│       └── IdentityRepository.cs            # MODIFY: implement filtered queries

tests/
├── Sorcha.UI.E2E.Tests/
│   ├── Docker/
│   │   ├── AdminOrganizationsTests.cs       # MODIFY: update for unified page
│   │   └── AdminUserDetailTests.cs          # NEW: deep-link user detail tests
│   ├── PageObjects/AdminPages/
│   │   ├── OrganizationsPage.cs             # MODIFY: add org selector, tab locators
│   │   └── UserDetailPage.cs               # NEW: user detail page object
│   └── Infrastructure/
│       └── TestConstants.cs                 # MODIFY: add new route constants
├── Sorcha.Tenant.Service.Tests/
│   └── Services/
│       └── OrganizationServiceTests.cs      # MODIFY: tests for filtering, email verify
└── Sorcha.UI.Core.Tests/                    # MODIFY: component unit tests
```

**Structure Decision**: Web application pattern — Blazor WASM frontend with microservice backend. All changes are within existing project boundaries. No new projects created. Two new components (`OrgSelectorPanel`, `CollapsibleStatsPanel`) and one new page (`OrganizationUserDetail`) added.

## Complexity Tracking

> No constitution violations. No complexity justification needed.
