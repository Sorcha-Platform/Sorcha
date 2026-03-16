# Tasks: Platform Organisation Topology

**Input**: Design documents from `/specs/058-platform-org-topology/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/platform-api.yaml, quickstart.md

**Tests**: Not explicitly requested in the feature specification. Test tasks are omitted. Tests should be written alongside implementation per standard project conventions (>85% coverage).

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- Include exact file paths in descriptions

---

## Phase 1: Setup

**Purpose**: Remove obsolete code and prepare for new entity model

- [X] T001 Delete `PublicIdentity.cs` from `src/Services/Sorcha.Tenant.Service/Models/PublicIdentity.cs`
- [X] T002 [P] Delete `SocialLoginLink.cs` from `src/Services/Sorcha.Tenant.Service/Models/SocialLoginLink.cs`
- [X] T003 [P] Delete `OwnerTypes.cs` from `src/Services/Sorcha.Tenant.Service/Models/OwnerTypes.cs`
- [X] T004 [P] Delete `IPublicUserService.cs` from `src/Services/Sorcha.Tenant.Service/Services/IPublicUserService.cs`
- [X] T005 [P] Delete `PublicUserService.cs` from `src/Services/Sorcha.Tenant.Service/Services/PublicUserService.cs`
- [X] T006 [P] Delete `PublicAuthEndpoints.cs` from `src/Services/Sorcha.Tenant.Service/Endpoints/PublicAuthEndpoints.cs`
- [X] T007 Remove all references to deleted types (PublicIdentity, SocialLoginLink, OwnerTypes, IPublicUserService, PublicUserService, PublicAuthEndpoints) across the solution — update DI registration in `Program.cs`, endpoint mappings, DbContext configurations, and any consuming code

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core entities, modified entities, base services, auth infrastructure — MUST complete before ANY user story

**⚠️ CRITICAL**: No user story work can begin until this phase is complete

### New Entities

- [X] T008 [P] Create `PlatformUserStatus.cs` enum (Active=0, Suspended=1, Deleted=2) in `src/Services/Sorcha.Tenant.Service/Models/PlatformUserStatus.cs`
- [X] T009 Create `PlatformUser.cs` entity with all 17 fields, navigations (SocialLogins, PasskeyCredentials, OrgMemberships), unique index on Email, index on Status in `src/Services/Sorcha.Tenant.Service/Models/PlatformUser.cs`
- [X] T010 [P] Create `PlatformSocialLogin.cs` entity with 8 fields, FK to PlatformUser, unique index on (Provider, Subject) in `src/Services/Sorcha.Tenant.Service/Models/PlatformSocialLogin.cs`
- [X] T011 [P] Create `PlatformUserOrgMembership.cs` entity with 5 fields, FK to PlatformUser and Organization, unique index on (PlatformUserId, OrganizationId) in `src/Services/Sorcha.Tenant.Service/Models/PlatformUserOrgMembership.cs`
- [X] T012 [P] Create `PlatformSettings.cs` singleton entity with 5 fields (Id, PublicOrgEnabled, MaxOrgsPerUser, UpdatedAt, UpdatedBy) in `src/Services/Sorcha.Tenant.Service/Models/PlatformSettings.cs`

### Modified Entities

- [X] T013 Add `IsPlatformOrg` (bool, default false) field to `Organization.cs`; change `IdentityProvider` navigation to `IdentityProviders` (`ICollection<IdentityProviderConfiguration>`) in `src/Services/Sorcha.Tenant.Service/Models/Organization.cs`
- [X] T014 [P] Modify `IdentityProviderConfiguration.cs`: remove unique constraint on OrganizationId, add composite unique index on (OrganizationId, ProviderPreset); add `GitHub` value to `IdentityProviderType` enum in `src/Services/Sorcha.Tenant.Service/Models/IdentityProviderConfiguration.cs`
- [X] T015 [P] Add `AdminCreated = 4` to `ProvisioningMethod` enum in `src/Services/Sorcha.Tenant.Service/Models/ProvisioningMethod.cs`
- [X] T016 Modify `UserIdentity.cs`: add `PlatformUserId` (Guid, Required) field; remove `PasswordHash`, `ExternalIdpSubject`, `EmailVerified`, `EmailVerifiedAt`, `VerificationToken`, `VerificationTokenExpiresAt`, `PasswordResetTokenHash`, `PasswordResetTokenExpiresAt`, `FailedLoginCount`, `LockedUntil`, `LockedPermanently` fields in `src/Services/Sorcha.Tenant.Service/Models/UserIdentity.cs`
- [X] T017 Modify `PasskeyCredential.cs`: remove `OwnerType`, `OwnerId`, `OrganizationId` fields and their indexes; add `PlatformUserId` (Guid, FK → PlatformUser, Required) with indexes on PlatformUserId and (PlatformUserId, Status) in `src/Services/Sorcha.Tenant.Service/Models/PasskeyCredential.cs`
- [X] T017b Update passkey authentication flow to resolve PlatformUser via `PlatformUserId` FK instead of polymorphic `OwnerType`/`OwnerId`; update passkey registration and assertion endpoints to use PlatformUser as the credential owner in `src/Services/Sorcha.Tenant.Service/Endpoints/AuthEndpoints.cs` and related services

### EF Core Configuration

- [X] T018 Update DbContext to register new entities (PlatformUser, PlatformSocialLogin, PlatformUserOrgMembership, PlatformSettings) in public schema; update entity configurations for modified entities (Organization one-to-many IDP, UserIdentity +PlatformUserId/-auth fields, PasskeyCredential reparent); update all references from `Organization.IdentityProvider` to `Organization.IdentityProviders` across the solution in `src/Services/Sorcha.Tenant.Service/Data/`
- [X] T019 Reset EF Core initial migration to include all new/modified entity configurations (no production instances) — regenerate migration in `src/Services/Sorcha.Tenant.Service/Data/Migrations/`

### Base Services

- [X] T020 Create `IPlatformUserService.cs` interface with methods: CreateAsync, GetByIdAsync, GetByEmailAsync, GetByProviderSubjectAsync, UpdateAsync, LinkSocialLoginAsync, GetOrgMembershipsAsync, AddOrgMembershipAsync in `src/Services/Sorcha.Tenant.Service/Services/IPlatformUserService.cs`
- [X] T021 Create `PlatformUserService.cs` implementing IPlatformUserService — PlatformUser CRUD, email uniqueness enforcement, social login linking, org membership management in `src/Services/Sorcha.Tenant.Service/Services/PlatformUserService.cs`
- [X] T022 [P] Create `IPlatformSettingsService.cs` interface with methods: GetAsync, UpdatePublicOrgEnabledAsync, UpdateMaxOrgsPerUserAsync in `src/Services/Sorcha.Tenant.Service/Services/IPlatformSettingsService.cs`
- [X] T023 [P] Create `PlatformSettingsService.cs` implementing IPlatformSettingsService — singleton config management, atomically toggle public org status + self-registration in `src/Services/Sorcha.Tenant.Service/Services/PlatformSettingsService.cs`

### Token & Auth Infrastructure

- [X] T024 Modify `TokenService.cs`: add `platform_user_id` claim to JWT; merge `GeneratePublicUserTokenAsync` into `GenerateUserTokenAsync` (all users are PlatformUsers now) in `src/Services/Sorcha.Tenant.Service/Services/TokenService.cs`
- [X] T025 [P] Add `RequirePlatformAuditor` authorization policy (SystemAdmin org member with Auditor+ role) in `src/Common/Sorcha.ServiceDefaults/AuthorizationPolicyExtensions.cs`

### API Gateway Routes

- [X] T026 Add 6 YARP routes to API Gateway: `platform-settings-route` (/api/platform/settings, RequireSystemAdmin), `platform-public-org-route` (/api/platform/settings/public-org, RequireSystemAdmin), `platform-orgs-route` (/api/platform/organizations, RequireSystemAdmin), `platform-org-status-route` (/api/platform/organizations/{id}/status, RequireSystemAdmin), `platform-org-users-route` (/api/platform/organizations/{id}/users, RequirePlatformAuditor), `auth-switch-org-route` (/api/auth/switch-org, RequireAuthenticated) in `src/Services/Sorcha.ApiGateway/appsettings.json`

### DI Registration

- [X] T027 Register new services (IPlatformUserService, IPlatformSettingsService) and update DI for modified services in `src/Services/Sorcha.Tenant.Service/Program.cs`
- [X] T027b [P] Create `PlatformOrgEndpoints.cs` stub with endpoint group registration and route mapping scaffold in `src/Services/Sorcha.Tenant.Service/Endpoints/PlatformOrgEndpoints.cs`

> **Terminology note**: "disabled" in the spec maps to `Organization.Status = Suspended` in the data model.

**Checkpoint**: Foundation ready — all entities, base services, token changes, YARP routes, and auth policies in place. User story implementation can now begin.

---

## Phase 3: User Story 1 — Bootstrap and Enable Public Organisation (Priority: P1) 🎯 MVP

**Goal**: Bootstrap creates two orgs (system admin + public) and PlatformSettings; system admin can enable/disable public org via Platform Settings API and Admin UI.

**Independent Test**: Call bootstrap endpoint → verify both orgs exist with correct flags → call Platform Settings API to enable public org → verify status changes to Active and self-registration enabled.

**Relates to**: FR-001, FR-002, FR-004, FR-021, SC-004

### Implementation for User Story 1

- [X] T028 [US1] Modify `DatabaseInitializer.cs` bootstrap flow: create system admin org (IsPlatformOrg=true, Status=Active) and public org (ID=...0002, IsPlatformOrg=true, Status=Suspended, OrgType=Public); create admin PlatformUser; create admin UserIdentity in system admin org with PlatformUserId; create PlatformUserOrgMembership for admin; seed PlatformSettings (PublicOrgEnabled=false, MaxOrgsPerUser=1) in `src/Services/Sorcha.Tenant.Service/Data/DatabaseInitializer.cs`
- [X] T029 [US1] Modify `BootstrapEndpoints.cs` to return bootstrap response including both org IDs and confirm PlatformUser creation in `src/Services/Sorcha.Tenant.Service/Endpoints/BootstrapEndpoints.cs`
- [X] T030 [P] [US1] Create `PlatformSettingsEndpoints.cs` with 3 endpoints: `GET /api/platform/settings` (RequireSystemAdmin), `PUT /api/platform/settings/public-org` (toggle public org + self-registration atomically), `PUT /api/platform/settings/max-orgs` (update MaxOrgsPerUser) — with `.WithSummary()` and `.WithDescription()` per OpenAPI contract in `src/Services/Sorcha.Tenant.Service/Endpoints/PlatformSettingsEndpoints.cs`
- [X] T031 [US1] Add inline validators (project uses custom InputValidation, not FluentValidation) for `UpdatePublicOrgRequest` and `UpdateMaxOrgsRequest` in `src/Services/Sorcha.Tenant.Service/Models/` (or `Validators/`)
- [X] T032 [P] [US1] Create Platform Settings admin page in Blazor WASM — toggle for public org enable/disable, MaxOrgsPerUser input, display current status in `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/Admin/Settings/PlatformSettings.razor` (Admin app was migrated to Sorcha.UI)
- [X] T033 [US1] Add Platform Settings service client for UI HTTP calls in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/PlatformSettingsAdminService.cs`
- [X] T034 [US1] Add Platform Settings nav link in UI sidebar in `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Components/Layout/MainLayout.razor`

**Checkpoint**: Bootstrap creates two orgs + PlatformSettings. System admin can enable/disable public org. Foundation for all subsequent stories.

---

## Phase 4: User Story 2 — Public Organisation Signup via Social Login (Priority: P1)

**Goal**: New users sign up via social login (Google, GitHub, Microsoft, Apple) to the public org; existing users log in via social provider; users can link additional providers.

**Independent Test**: Initiate social login → complete OAuth callback → verify PlatformUser + PlatformSocialLogin + UserIdentity in public org created → verify valid JWT with platform_user_id claim.

**Relates to**: FR-005, FR-006, FR-010, FR-011, FR-027, FR-028, SC-001, SC-008

### Implementation for User Story 2

- [X] T035 [US2] Create social login initiate endpoint `POST /api/auth/social/initiate` — generate OAuth authorization URL with PKCE for requested provider; validate public org is enabled and provider is configured in `src/Services/Sorcha.Tenant.Service/Endpoints/SocialLoginEndpoints.cs`
- [X] T036 [US2] Create social login callback endpoint `POST /api/auth/social/callback` — exchange authorization code, resolve/create PlatformUser + PlatformSocialLogin, create UserIdentity in public org if new, create PlatformUserOrgMembership, issue JWT in `src/Services/Sorcha.Tenant.Service/Endpoints/SocialLoginEndpoints.cs`
- [X] T037 [US2] Create social provider link endpoint `POST /api/auth/social/link` (RequireAuthenticated) — initiate OAuth flow for linking additional provider to existing PlatformUser in `src/Services/Sorcha.Tenant.Service/Endpoints/SocialLoginEndpoints.cs`
- [X] T038 [US2] Implement social login resolution logic in `PlatformUserService.cs`: find by (Provider, Subject) → existing user; find by email → link provider to existing; neither → create new PlatformUser + PlatformSocialLogin in `src/Services/Sorcha.Tenant.Service/Services/PlatformUserService.cs`
- [X] T039 [US2] Add inline validators for `SocialLoginInitiateRequest` and `SocialLoginCallbackRequest` (project uses inline validation, not FluentValidation) in `src/Services/Sorcha.Tenant.Service/Models/Dtos/SocialLoginDtos.cs`
- [X] T040 [P] [US2] Social login UI: implemented SocialCallback server-rendered page with full OAuth exchange, user provisioning, and JWT redirect in `src/Services/Sorcha.Tenant.Service/Pages/Auth/SocialCallback.cshtml.cs`; updated Signup page social JS to use new endpoint
- [X] T041 [US2] Added social login buttons (Google, Microsoft, GitHub, Apple) with JS to Login page in `src/Services/Sorcha.Tenant.Service/Pages/Auth/Login.cshtml`

**Checkpoint**: Users can sign up and log in via social providers. Platform identity is created. Social provider linking works.

---

## Phase 5: User Story 3 — Public Organisation Signup via Email/Password (Priority: P2)

**Goal**: New users sign up with email and password; email verification required for full access.

**Independent Test**: Submit registration → verify PlatformUser + UserIdentity created → verify verification email token generated → call verify-email → confirm email marked verified.

**Relates to**: FR-005, FR-009, FR-012, SC-002

### Implementation for User Story 3

- [X] T042 [US3] Made registration timing-safe (same response shape regardless of email existence) with BCrypt dummy hash for consistent timing in `src/Services/Sorcha.Tenant.Service/Services/RegistrationService.cs`; added `platform-auth` rate limiting to register endpoint
- [X] T043 [US3] Email verification endpoint `POST /api/auth/verify-email` already exists in `OidcEndpoints.cs`; added `platform-auth` rate limiting; `EmailVerificationService.VerifyTokenAsync` correctly updates PlatformUser.EmailVerified
- [X] T044 [US3] Implemented `ValidatePasswordAsync` in `PlatformUserService.cs` with progressive lockout (5→15min, 10→30min, 15→1hr, 20→4hr, 25+→permanent); added `PasswordAuthResult` record to `IPlatformUserService.cs`
- [X] T045 [US3] Updated `LoginService.cs` to delegate password verification to `PlatformUserService.ValidatePasswordAsync` with lockout-aware error responses (AccountLocked error code)
- [X] T045b [US3] Added subdomain-aware login overload `LoginAsync(email, password, orgSubdomain)` in `LoginService.cs`: resolves PlatformUser by email, validates password, verifies PlatformUserOrgMembership, resolves UserIdentity in target org, issues org-scoped JWT; `AuthEndpoints.cs` login handler routes to subdomain overload when `OrganizationSubdomain` is provided
- [X] T046 [US3] Inline validation already exists in `AuthEndpoints.cs` Register handler and `RegistrationService.cs` (password policy via HIBP + NIST, email format, display name); `VerifyEmailRequest` DTO with validation in `OidcEndpoints.cs`
- [X] T047 [P] [US3] Registration page already exists as server-rendered Razor Page `Signup.cshtml` with email/password tab, form validation, and success display in `src/Services/Sorcha.Tenant.Service/Pages/Auth/`
- [X] T048 [P] [US3] Email verification landing page already exists as server-rendered Razor Page `VerifyEmail.cshtml` with token validation and success/error feedback in `src/Services/Sorcha.Tenant.Service/Pages/Auth/`

**Checkpoint**: Email/password signup works. Email verification flow functional. Login authenticates against PlatformUser.

---

## Phase 6: User Story 4 — Self-Service Organisation Creation via Blueprint (Priority: P2)

**Goal**: Public org members create private orgs through the "Create Organisation" blueprint workflow.

**Independent Test**: Trigger Create Organisation blueprint → complete workflow → verify new org created, user is admin, PlatformUserOrgMembership added, CreatedOrgsCount incremented.

**Relates to**: FR-015, FR-016, FR-017, FR-018, FR-020, SC-003, SC-007

### Implementation for User Story 4

- [X] T049 [US4] Created `create-organisation-v1.json` blueprint template with participants (requestor, system), actions (Submit Request → Validate → Provision → Confirm), and JSON Schema for org name/subdomain input in `blueprints/templates/create-organisation-v1.json`
- [X] T050 [US4] Seeded "Create Organisation" blueprint into system register during bootstrap in `SystemRegisterBootstrapper.SeedBlueprintsIfMissingAsync` following existing blueprint seeding pattern in `src/Services/Sorcha.Register.Service/Services/SystemRegisterBootstrapper.cs`
- [X] T051 [US4] Implemented `OrgProvisioningService` with atomic creation: Organization + admin UserIdentity + PlatformUserOrgMembership + increment CreatedOrgsCount + audit log in single SaveChangesAsync; rollback via EF Core transaction on failure in `src/Services/Sorcha.Tenant.Service/Services/OrgProvisioningService.cs`
- [X] T052 [US4] Implemented validation in `OrgProvisioningService.ValidateAsync`: check EmailVerified, check PlatformUser.Status Active, check CreatedOrgsCount < MaxOrgsPerUser, validate subdomain format/availability via OrganizationService.ValidateSubdomainAsync, validate name 3-100 and description max 500
- [X] T053 [US4] DTOs defined as `ProvisionOrgRequest` and `OrgProvisioningResult` records in `IOrgProvisioningService.cs` with inline validation in service (project uses inline validation, not FluentValidation); endpoint added as `POST /api/auth/create-org` in `AuthEndpoints.cs`
- [X] T054 [P] [US4] Created "Create Organisation" MudBlazor page with name, subdomain, description fields; success/error display; `OrgProvisioningClientService` for HTTP calls in `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/CreateOrganization.razor`

**Checkpoint**: Public org members can self-service create private orgs. Atomic provisioning with full rollback on failure.

---

## Phase 7: User Story 5 — Admin-Initiated Organisation Creation with Invite (Priority: P2)

**Goal**: System admins create private orgs directly and invite an admin by email, bypassing the blueprint workflow.

**Independent Test**: System admin calls POST /api/platform/organizations with org details and invitee email → verify org created → verify invitation generated for invitee.

**Relates to**: FR-019, FR-020

### Implementation for User Story 5

- [X] T055 [US5] Added `POST /api/platform/organizations` endpoint to `PlatformOrgEndpoints.cs` (RequireSystemAdmin) — creates org via `AdminProvisionAsync`, resolves admin email, returns 201 with org details and invitation status in `src/Services/Sorcha.Tenant.Service/Endpoints/PlatformOrgEndpoints.cs`
- [X] T056 [US5] Implemented invitation flow in `OrgProvisioningService.AdminProvisionAsync`: if adminEmail matches existing PlatformUser → creates UserIdentity + PlatformUserOrgMembership directly with AdminCreated provisioning; if new email → creates pending OrgInvitation via existing InvitationService in `src/Services/Sorcha.Tenant.Service/Services/OrgProvisioningService.cs`
- [X] T057 [US5] Added `AdminCreateOrganizationRequest` and `AdminCreateOrganizationResponse` DTOs with inline validation (name 3-100, subdomain format, adminEmail format, description max 500) in `src/Services/Sorcha.Tenant.Service/Models/Dtos/OrganizationDtos.cs` — project uses inline validation, not FluentValidation
- [X] T058 [P] [US5] Created Platform Organisations admin page with org creation form (name, subdomain, admin email, description), success display with org details and admin status, nav link added to SystemAdmin section in `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/Admin/PlatformOrganizations.razor`

**Checkpoint**: System admins can create orgs and invite administrators. Both new and existing users can be invited.

---

## Phase 8: User Story 6 — Organisation Switching (Priority: P3)

**Goal**: Users switch between their organisations via the org switcher; new JWT issued scoped to target org.

**Independent Test**: User with memberships in 2+ orgs → GET /api/auth/me/organizations returns both → POST /api/auth/switch-org with target org → verify new JWT scoped to target org.

**Relates to**: FR-008, FR-014, SC-005

### Implementation for User Story 6

- [X] T059 [US6] Created `GET /api/auth/me/organizations` endpoint (RequireAuthenticated) — queries PlatformUserOrgMembership joined with Organizations, returns org list with names, subdomains, roles, isCurrent flag; added YARP route `auth-me-orgs-route` in `src/Services/Sorcha.Tenant.Service/Endpoints/AuthEndpoints.cs`
- [X] T060 [US6] Created `POST /api/auth/switch-org` endpoint (RequireAuthenticated) — validates PlatformUserOrgMembership, checks org is Active, finds active UserIdentity in target org, issues new JWT via TokenService in `src/Services/Sorcha.Tenant.Service/Endpoints/AuthEndpoints.cs`
- [X] T061 [US6] Added inline validation for `SwitchOrgRequest` (organizationId required, non-empty GUID) and `OrgMembershipEntry`/`OrgMembershipListResponse` DTOs in `src/Services/Sorcha.Tenant.Service/Models/Dtos/AuthDtos.cs` — project uses inline validation, not FluentValidation
- [X] T062 [P] [US6] Created OrgSwitcher component — MudMenu dropdown showing user's orgs with roles and current indicator, click to switch with token replacement and page reload in `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Components/OrgSwitcher.razor`
- [X] T063 [US6] Integrated OrgSwitcher into Main UI app bar (before notifications icon) in `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Components/Layout/MainLayout.razor`
- [X] T064 [US6] OrgSwitcher handles token replacement via ITokenCache + CustomAuthenticationStateProvider.NotifyAuthenticationStateChanged() + Navigation.NavigateTo with forceLoad for full context refresh; IOrgSwitcherService registered in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Extensions/ServiceCollectionExtensions.cs`

**Checkpoint**: Users can see their orgs and switch between them. Session context changes appropriately.

---

## Phase 9: User Story 7 — Platform Organisation Management (Priority: P3)

**Goal**: System admins list all orgs, view user lists (read-only for private orgs), suspend/enable private orgs.

**Independent Test**: System admin calls GET /api/platform/organizations → see all orgs → GET /api/platform/organizations/{id}/users → see user list → PUT /api/platform/organizations/{id}/status to suspend → verify status change, reject suspend on platform orgs.

**Relates to**: FR-002, FR-022, FR-023, FR-024, FR-025, FR-026, SC-006

### Implementation for User Story 7

- [X] T065 [US7] Added `GET /api/platform/organizations` endpoint (RequireSystemAdmin) — paginated org list with status filter, batch user count via GroupBy, page/pageSize clamping in `src/Services/Sorcha.Tenant.Service/Endpoints/PlatformOrgEndpoints.cs`
- [X] T066 [US7] Added `PUT /api/platform/organizations/{orgId}/status` (RequireSystemAdmin) — inline validation rejects Deleted status, 400 for platform orgs, logs status change in `src/Services/Sorcha.Tenant.Service/Endpoints/PlatformOrgEndpoints.cs`
- [X] T067 [US7] Added `GET /api/platform/organizations/{orgId}/users` (RequirePlatformAuditor) — paginated user list via PlatformUserOrgMemberships + PlatformUsers join (public schema, no cross-schema queries) in `src/Services/Sorcha.Tenant.Service/Endpoints/PlatformOrgEndpoints.cs`
- [X] T068 [US7] Inline validation for `UpdateOrgStatusRequest` — pattern match rejects non-Active/Suspended status; DTOs added to `src/Services/Sorcha.Tenant.Service/Models/Dtos/OrganizationDtos.cs`
- [X] T069 [US7] SystemAdmin role constraint enforced in `OrgProvisioningService.AdminProvisionAsync` (error_code: invalid_role), `InvitationService.CreateInvitationAsync`, and `OrganizationEndpoints.ChangeUserRole` in `src/Services/Sorcha.Tenant.Service/Services/`
- [X] T070 [P] [US7] Platform Organisations admin page with org list table, status badges, status filter, click-through user list, suspend/enable actions, pagination in `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/Admin/PlatformOrganizations.razor`
- [X] T071 [US7] Platform Organisations nav link already present in Main UI sidebar (added in Phase 2) in `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Components/Layout/MainLayout.razor`

**Checkpoint**: Full platform governance — system admins can see all orgs, audit user lists, and manage org status. Permission boundaries enforced.

---

## Phase 10: Polish & Cross-Cutting Concerns

**Purpose**: Documentation, integration validation, security hardening

- [ ] T072 [P] Update Tenant Service README with new platform endpoints, PlatformUser model, and bootstrap changes in `src/Services/Sorcha.Tenant.Service/README.md`
- [ ] T073 [P] Update `docs/reference/API-DOCUMENTATION.md` with all new platform and auth endpoints
- [ ] T074 [P] Update `docs/guides/AUTHENTICATION-SETUP.md` with social login flow, platform identity layer, org switching
- [ ] T075 [P] Update `docs/reference/development-status.md` with feature completion
- [ ] T076 Update `CLAUDE.md` Participant Identity API section with new platform endpoints
- [ ] T077 Update `.specify/MASTER-TASKS.md` with feature completion status
- [ ] T078 Run `quickstart.md` validation — verify all curl examples work against running instance
- [ ] T079 Verify well-known org IDs (SystemAdminOrgId=...0001, PublicOrgId=...0002) are consistent across bootstrap, services, and YARP routes
- [ ] T080 Security review: verify BCrypt password hashing, PKCE on all OAuth flows, AES-256-GCM for client secrets, platform org suspension protection, SystemAdmin role constraint
- [ ] T081 [P] Add health check coverage for new platform endpoints and structured logging for social login, org switching, and provisioning flows per constitution VIII

> **Cross-cutting requirement (Constitution III)**: All new endpoints MUST include `.WithSummary()`, `.WithDescription()`, and XML documentation. This applies to all endpoint tasks (T030, T035-T037, T042-T043, T045b, T055, T059-T060, T065-T067).

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — can start immediately
- **Foundational (Phase 2)**: Depends on Setup completion — **BLOCKS all user stories**
- **US1 (Phase 3)**: Depends on Foundational — no dependencies on other stories
- **US2 (Phase 4)**: Depends on Foundational — benefits from US1 (public org enabled) but independently testable
- **US3 (Phase 5)**: Depends on Foundational — benefits from US1 but independently testable
- **US4 (Phase 6)**: Depends on Foundational + US3 (email verification logic) — needs a verified user to create orgs
- **US5 (Phase 7)**: Depends on Foundational + T051 (org provisioning from US4) — reuses provisioning service
- **US6 (Phase 8)**: Depends on Foundational — needs user with 2+ org memberships (from US2/US3/US4/US5)
- **US7 (Phase 9)**: Depends on Foundational — `PlatformOrgEndpoints.cs` created in Phase 2 (T027b), shared with US5
- **Polish (Phase 10)**: Depends on all user stories being complete

### User Story Dependencies

```
Phase 1 (Setup) ─────────────────────────────────────────────────┐
  │                                                               │
Phase 2 (Foundational) ──────────────────────────────────────────┤
  │                                                               │
  ├─ Phase 3 (US1: Bootstrap + Platform Settings) ── P1          │
  │                                                               │
  ├─ Phase 4 (US2: Social Login) ────────────────── P1           │
  │                                                               │
  ├─ Phase 5 (US3: Email/Password) ─────────────── P2            │
  │     │                                                         │
  │     ├─ Phase 6 (US4: Blueprint Org Creation) ── P2           │
  │     │     │                                                   │
  │     │     └─ Phase 7 (US5: Admin Org Creation) ── P2         │
  │     │                                                         │
  ├─ Phase 8 (US6: Org Switching) ──────────────── P3            │
  │                                                               │
  └─ Phase 9 (US7: Platform Management) ────────── P3            │
                                                                  │
Phase 10 (Polish) ◄──────────────────────────────────────────────┘
```

### Within Each User Story

- Models/validators before services
- Services before endpoints
- Endpoints before UI
- Core implementation before integration

### Parallel Opportunities

- **Phase 1**: T001-T006 all [P] — delete files in parallel, then T007 sequentially
- **Phase 2**: T008, T010, T011, T012 [P]; T013+T014+T015 [P]; T020+T022 [P]; T024+T025 [P]
- **After Foundational**: US1, US2, US3 can start in parallel (different endpoints/files)
- **Within US stories**: UI tasks marked [P] can run alongside backend (different projects)
- **US4 + US5**: Share org provisioning — US4 first, US5 reuses
- **US5 + US7**: Both add endpoints to `PlatformOrgEndpoints.cs` (created in Phase 2). Can run in parallel — different endpoint methods in same file.
- **US2 + US3 + US6**: All modify `AuthEndpoints.cs` — coordinate changes if running in parallel, or execute sequentially for that file.
- **US6 + US7**: Fully independent, can run in parallel

---

## Parallel Example: Phase 2 (Foundational)

```bash
# Wave 1: New entities (all independent files)
Task: "T008 [P] Create PlatformUserStatus enum"
Task: "T010 [P] Create PlatformSocialLogin entity"
Task: "T011 [P] Create PlatformUserOrgMembership entity"
Task: "T012 [P] Create PlatformSettings entity"

# Wave 2: PlatformUser depends on PlatformUserStatus
Task: "T009 Create PlatformUser entity"

# Wave 3: Modified entities (independent files)
Task: "T013 Modify Organization"
Task: "T014 [P] Modify IdentityProviderConfiguration"
Task: "T015 [P] Add AdminCreated to ProvisioningMethod"
Task: "T016 Modify UserIdentity"
Task: "T017 Modify PasskeyCredential"

# Wave 4: EF Core (depends on all entity changes)
Task: "T018 Update DbContext configurations"
Task: "T019 Reset EF Core migration"

# Wave 5: Services + Auth (depends on entities + DbContext)
Task: "T020 + T021 PlatformUserService"
Task: "T022 + T023 [P] PlatformSettingsService"
Task: "T024 Modify TokenService"
Task: "T025 [P] Add RequirePlatformAuditor policy"
Task: "T026 YARP routes"
Task: "T027 DI registration"
```

## Parallel Example: P1 Stories (US1 + US2)

```bash
# After Phase 2 completes, launch US1 and US2 in parallel:

# US1 (different endpoint files + admin UI):
Task: "T028 [US1] Bootstrap changes"
Task: "T030 [P] [US1] PlatformSettingsEndpoints"
Task: "T032 [P] [US1] Admin UI Platform Settings page"

# US2 (different endpoint methods + main UI):
Task: "T035 [US2] Social login initiate endpoint"
Task: "T040 [P] [US2] Social login UI components"
```

---

## Implementation Strategy

### MVP First (US1 Only)

1. Complete Phase 1: Setup (delete obsolete code)
2. Complete Phase 2: Foundational (all entities, services, infrastructure)
3. Complete Phase 3: US1 — Bootstrap + Platform Settings
4. **STOP and VALIDATE**: Bootstrap creates both orgs, admin can toggle public org
5. Deploy/demo if ready

### Incremental Delivery

1. Setup + Foundational → Foundation ready
2. US1 (Bootstrap + Settings) → Test independently → **MVP!**
3. US2 (Social Login) → Test independently → Primary onboarding path
4. US3 (Email/Password) → Test independently → Fallback signup
5. US4 (Blueprint Org Creation) → Test independently → Self-service growth
6. US5 (Admin Org Creation) → Test independently → Enterprise onboarding
7. US6 (Org Switching) → Test independently → Multi-org UX
8. US7 (Platform Management) → Test independently → Governance
9. Polish → Documentation, security review, quickstart validation

---

## Notes

- [P] tasks = different files, no dependencies
- [Story] label maps task to specific user story for traceability
- Each user story should be independently completable and testable
- Commit after each task or logical group
- Stop at any checkpoint to validate story independently
- Tests not generated as separate tasks — write alongside implementation per project convention (>85% coverage)
- Total: 84 tasks across 10 phases
