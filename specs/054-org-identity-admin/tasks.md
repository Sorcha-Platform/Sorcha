# Tasks: Organization Admin & Identity Management

**Input**: Design documents from `/specs/054-org-identity-admin/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/

**Tests**: Included — spec requires >85% coverage on new code (xUnit + FluentAssertions + Moq).

**Organization**: Tasks grouped by user story for independent implementation and testing.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- Include exact file paths in descriptions

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: New enums, entity modifications, EF Core migration, shared abstractions — everything that multiple user stories depend on.

- [x] T001 Add `OrgType` enum in `src/Services/Sorcha.Tenant.Service/Models/OrgType.cs`
- [x] T002 [P] Add `CustomDomainStatus` enum in `src/Services/Sorcha.Tenant.Service/Models/CustomDomainStatus.cs`
- [x] T003 [P] Add `InvitationStatus` enum in `src/Services/Sorcha.Tenant.Service/Models/InvitationStatus.cs`
- [x] T004 [P] Add `ProvisioningMethod` enum in `src/Services/Sorcha.Tenant.Service/Models/ProvisioningMethod.cs`
- [x] T005 Update `UserRole` enum — consolidate to 5 roles (SystemAdmin=0, Administrator=1, Designer=2, Auditor=3, Member=4), remove Developer/User/Consumer in `src/Services/Sorcha.Tenant.Service/Models/UserRole.cs` (or wherever currently defined)
- [x] T006 Modify `Organization` model — add OrgType, SelfRegistrationEnabled, AllowedEmailDomains (string[]), CustomDomain, CustomDomainStatus, AuditRetentionMonths in `src/Services/Sorcha.Tenant.Service/Models/Organization.cs`
- [x] T007 Modify `UserIdentity` model — add EmailVerified, EmailVerifiedAt, VerificationToken, VerificationTokenExpiresAt, ProvisionedVia, InvitedByUserId, ProfileCompleted, FailedLoginCount, LockedUntil, LockedPermanently; rename ExternalIdpUserId→ExternalIdpSubject in `src/Services/Sorcha.Tenant.Service/Models/UserIdentity.cs`
- [x] T008 Modify `IdentityProviderConfiguration` model — add IsEnabled, DisplayName, UserInfoEndpoint, JwksUri, DiscoveryDocumentJson, DiscoveryFetchedAt; rename ProviderType→ProviderPreset; update `IdentityProviderType` enum values (add Google/Okta/Apple, rename AzureEntra→MicrosoftEntra, AwsCognito→AmazonCognito) in `src/Services/Sorcha.Tenant.Service/Models/IdentityProviderConfiguration.cs`
- [x] T009 Create `OrgInvitation` entity in `src/Services/Sorcha.Tenant.Service/Models/OrgInvitation.cs` — Id, OrganizationId, Email, AssignedRole, Token (unique), ExpiresAt, Status, InvitedByUserId, AcceptedByUserId, AcceptedAt, RevokedAt, CreatedAt
- [x] T010 [P] Create `CustomDomainMapping` entity in `src/Services/Sorcha.Tenant.Service/Models/CustomDomainMapping.cs` — Id, OrganizationId (unique), Domain (unique), Status, VerifiedAt, LastCheckedAt, CreatedAt
- [x] T011 Update `TenantDbContext` — add DbSets for OrgInvitation and CustomDomainMapping, update entity configurations for modified models, add indexes (unique on Token, composite on OrganizationId+Email+Status for OrgInvitation; unique on Domain for CustomDomainMapping) in `src/Services/Sorcha.Tenant.Service/Data/TenantDbContext.cs`
- [x] T012 Create EF Core migration `AddOrgIdentityManagement` — alter Organizations, IdentityProviderConfigurations, UserIdentities tables; create OrgInvitations (per-org schema), CustomDomainMappings (public schema); data migration to consolidate Developer/User/Consumer roles to Member in `src/Services/Sorcha.Tenant.Service/Migrations/`
- [x] T013 Extend `AuditEventType` enum with new event types: InvitationSent, InvitationAccepted, InvitationRevoked, InvitationExpired, DomainRestrictionUpdated, CustomDomainConfigured, CustomDomainVerified, CustomDomainFailed, EmailVerificationSent, EmailVerified, AccountLockedOut, AccountUnlockedByAdmin, SelfRegistration, OidcFirstLogin, ProfileCompleted in the existing audit models
- [x] T014 [P] Create `IEmailSender` interface and `SmtpEmailSender` implementation (MailKit SMTP) in `src/Services/Sorcha.Tenant.Service/Services/IEmailSender.cs` and `src/Services/Sorcha.Tenant.Service/Services/SmtpEmailSender.cs`
- [x] T015 Update `ServiceCollectionExtensions.cs` — register new services, repositories, and configure `EmailSettings` in `src/Services/Sorcha.Tenant.Service/Extensions/ServiceCollectionExtensions.cs`
- [x] T016 Update authorization policies for consolidated roles (8→5) in `src/Services/Sorcha.Tenant.Service/Extensions/AuthenticationExtensions.cs`

**Checkpoint**: Data model ready, migration created, enums consolidated, email abstraction available. All user stories can now build on this foundation.

---

## Phase 2: User Story 1 — Enterprise Admin Configures External IDP (Priority: P1)

**Goal**: An org admin can discover, configure, test, and activate an external OIDC identity provider for their organization.

**Independent Test**: Configure a test OIDC provider and verify discovery, connection test, and activation flow via API.

**FRs covered**: FR-001, FR-002, FR-003, FR-004, FR-006, FR-008

### Tests for User Story 1

- [x] T017 [P] [US1] Create `OidcDiscoveryServiceTests` — test discovery doc fetching, caching (24h TTL), invalid URL handling, provider-specific issuer templates in `tests/Sorcha.Tenant.Service.Tests/Services/OidcDiscoveryServiceTests.cs`
- [x] T018 [P] [US1] Create `IdpConfigurationServiceTests` — test CRUD, discover, test connection, toggle, provider presets, secret encryption in `tests/Sorcha.Tenant.Service.Tests/Services/IdpConfigurationServiceTests.cs`
- [x] T019 [P] [US1] Create `IdpConfigurationEndpointTests` — test GET/PUT/DELETE /api/organizations/{orgId}/idp, POST discover/test/toggle endpoints, authorization (RequireAdministrator) in `tests/Sorcha.Tenant.Service.Tests/Endpoints/IdpConfigurationEndpointTests.cs`

### Implementation for User Story 1

- [x] T020 [P] [US1] Create OIDC DTOs — `IdpConfigurationRequest`, `IdpConfigurationResponse`, `DiscoveryResponse` in `src/Services/Sorcha.Tenant.Service/Models/Dtos/IdpConfigurationDtos.cs`
- [x] T021 [US1] Implement `IOidcDiscoveryService` / `OidcDiscoveryService` — fetch `.well-known/openid-configuration`, cache with IMemoryCache (24h TTL), extract endpoints (authorization, token, userinfo, JWKS) in `src/Services/Sorcha.Tenant.Service/Services/IOidcDiscoveryService.cs` and `src/Services/Sorcha.Tenant.Service/Services/OidcDiscoveryService.cs`
- [x] T022 [US1] Implement `IIdpConfigurationService` / `IdpConfigurationService` — CRUD for IDP config, discover (calls OidcDiscoveryService), test connection (client_credentials grant or introspection), toggle enable/disable, provider presets with issuer URL templates in `src/Services/Sorcha.Tenant.Service/Services/IIdpConfigurationService.cs` and `src/Services/Sorcha.Tenant.Service/Services/IdpConfigurationService.cs`
- [x] T023 [US1] Create `IdpConfigurationEndpoints` — map GET/PUT/DELETE `/api/organizations/{orgId}/idp`, POST `discover`, POST `test`, POST `toggle` with RequireAdministrator policy, WithSummary/WithDescription in `src/Services/Sorcha.Tenant.Service/Endpoints/IdpConfigurationEndpoints.cs`
- [x] T024 [US1] Register US1 services in DI and add YARP routes for IDP configuration endpoints in `src/Services/Sorcha.Tenant.Service/Extensions/ServiceCollectionExtensions.cs` and `src/Services/Sorcha.ApiGateway/`

**Checkpoint**: Org admin can configure and test an IDP. Independently testable — no login flow needed yet.

---

## Phase 3: User Story 2 — Employee Signs In via Corporate IDP (Priority: P1)

**Goal**: Users can authenticate via their org's configured IDP, get auto-provisioned on first login, and receive a Sorcha JWT.

**Independent Test**: Complete full OIDC authorization code flow with a configured test IDP, verify account creation and JWT issuance.

**FRs covered**: FR-005, FR-007, FR-012, FR-013, FR-014, FR-015, FR-016, FR-017, FR-018, FR-035, FR-045

**Depends on**: US1 (IDP must be configured)

### Tests for User Story 2

- [x] T025 [P] [US2] Create `OidcExchangeServiceTests` — test authorization URL generation (with state/nonce), code exchange, token validation (signature, issuer, audience, expiry), error handling in `tests/Sorcha.Tenant.Service.Tests/Services/OidcExchangeServiceTests.cs`
- [x] T026 [P] [US2] Create `OidcProvisioningServiceTests` — test claim mapping (email from email/preferred_username/upn, name from name/given_name+family_name), auto-provisioning with Member role, returning user matching by sub, domain restriction check, profile completion redirect in `tests/Sorcha.Tenant.Service.Tests/Services/OidcProvisioningServiceTests.cs`
- [x] T027 [P] [US2] Create `OidcEndpointTests` — test POST /api/auth/oidc/initiate, GET /api/auth/callback/{orgSubdomain}, POST /api/auth/oidc/complete-profile, POST /api/auth/verify-email, POST /api/auth/resend-verification in `tests/Sorcha.Tenant.Service.Tests/Endpoints/OidcEndpointTests.cs`
- [x] T028 [P] [US2] Create `OidcIntegrationTests` — full flow integration test with mocked IDP: initiate → callback → provision → JWT in `tests/Sorcha.Tenant.Service.Tests/Integration/OidcIntegrationTests.cs`

### Implementation for User Story 2

- [x] T029 [P] [US2] Create OIDC endpoint DTOs — `OidcInitiateRequest`, `OidcInitiateResponse`, `OidcCompleteProfileRequest`, `VerifyEmailRequest`, `ResendVerificationRequest` in `src/Services/Sorcha.Tenant.Service/Models/Dtos/OidcDtos.cs`
- [x] T030 [US2] Implement `IOidcExchangeService` / `OidcExchangeService` — generate authorization URL (with state, nonce, PKCE), exchange authorization code for tokens via HttpClient, validate ID token (JWKS signature, issuer, audience, expiry, nonce) in `src/Services/Sorcha.Tenant.Service/Services/IOidcExchangeService.cs` and `src/Services/Sorcha.Tenant.Service/Services/OidcExchangeService.cs`
- [x] T031 [US2] Implement `IOidcProvisioningService` / `OidcProvisioningService` — extract claims (email, name, sub, email_verified), match returning users by ExternalIdpSubject, auto-provision new users with Member role and ProvisionedVia=Oidc, check domain restrictions, determine if profile completion needed in `src/Services/Sorcha.Tenant.Service/Services/IOidcProvisioningService.cs` and `src/Services/Sorcha.Tenant.Service/Services/OidcProvisioningService.cs`
- [x] T032 [US2] Create `OidcEndpoints` — POST `/api/auth/oidc/initiate` (AllowAnonymous), GET `/api/auth/callback/{orgSubdomain}` (AllowAnonymous, redirects), POST `/api/auth/oidc/complete-profile` (bearerAuth partial token) in `src/Services/Sorcha.Tenant.Service/Endpoints/OidcEndpoints.cs`
- [x] T033 [US2] Add email verification endpoints to `AuthEndpoints` — POST `/api/auth/verify-email` (AllowAnonymous), POST `/api/auth/resend-verification` (bearerAuth, rate limited 3/hour), implement token generation (32-byte URL-safe base64, 24h expiry), verification, and email sending in `src/Services/Sorcha.Tenant.Service/Endpoints/AuthEndpoints.cs`
- [x] T034 [US2] Wire audit events for OIDC flows — OidcFirstLogin, EmailVerificationSent, EmailVerified, ProfileCompleted, AccountLockedOut events in the provisioning and auth services
- [x] T034a [US2] Integrate existing TOTP 2FA into OIDC post-auth flow — after successful OIDC callback and provisioning, check if org requires 2FA; if user has TOTP configured, issue a partial token and redirect to TOTP challenge before issuing full Sorcha JWT (FR-011) in `src/Services/Sorcha.Tenant.Service/Endpoints/OidcEndpoints.cs` and `src/Services/Sorcha.Tenant.Service/Services/OidcProvisioningService.cs`
- [x] T035 [US2] Register US2 services in DI and add YARP routes for OIDC and email verification endpoints

**Checkpoint**: Full OIDC login flow works end-to-end. Users are auto-provisioned, email verification works, Sorcha JWT issued.

---

## Phase 4: User Story 3 — Public User Self-Registers via Social Login (Priority: P1)

**Goal**: Public org visitors can sign up via social login (Microsoft/Google/Apple) or create a local email/password account with NIST-compliant password policy and breach checking.

**Independent Test**: Visit a public org's signup page, complete social login or local registration, verify account creation and email verification.

**FRs covered**: FR-009, FR-010, FR-011, FR-046, FR-047, FR-043, FR-044

**Depends on**: US2 (OIDC flow for social login), US1 (IDP configured)

### Tests for User Story 3

- [x] T036 [P] [US3] Create `PasswordPolicyServiceTests` — test min 12 chars enforcement, no complexity rules, HIBP k-Anonymity breach check (SHA-1 prefix lookup, response parsing), negative result caching (24h) in `tests/Sorcha.Tenant.Service.Tests/Services/PasswordPolicyServiceTests.cs`

### Implementation for User Story 3

- [x] T037 [US3] Implement `IPasswordPolicyService` / `PasswordPolicyService` — validate min 12 chars, HIBP k-Anonymity API breach check (SHA-1 hash, send 5-char prefix to api.pwnedpasswords.com/range/{prefix}, check suffix), cache negative results 24h in `src/Services/Sorcha.Tenant.Service/Services/IPasswordPolicyService.cs` and `src/Services/Sorcha.Tenant.Service/Services/PasswordPolicyService.cs`
- [x] T038 [US3] Create self-registration endpoint — POST `/api/auth/register` (AllowAnonymous): validate org is Public and SelfRegistrationEnabled, validate password policy, check email uniqueness, create user with ProvisionedVia=Local, send verification email in `src/Services/Sorcha.Tenant.Service/Endpoints/AuthEndpoints.cs`
- [x] T039 [US3] Update `OrgSettingsEndpoints` — GET/PUT `/api/organizations/{orgId}/settings` to manage OrgType and SelfRegistrationEnabled in `src/Services/Sorcha.Tenant.Service/Endpoints/OrgSettingsEndpoints.cs`
- [x] T040 [US3] Wire audit events — SelfRegistration event on successful local registration
- [x] T041 [US3] Register US3 services in DI and add YARP routes for register and org settings endpoints

**Checkpoint**: Public orgs support social login + local registration. Password policy enforced. Email verification works.

---

## Phase 5: User Story 4 — Org Admin Manages Users (Priority: P2)

**Goal**: Admins can view dashboard, search/filter users, change roles, suspend/activate accounts, and send/revoke invitations.

**Independent Test**: Create an org with users, exercise all admin actions, verify dashboard stats and audit trail.

**FRs covered**: FR-023, FR-024, FR-025, FR-026, FR-036, FR-037, FR-038, FR-039, FR-040

### Tests for User Story 4

- [x] T042 [P] [US4] Create `InvitationServiceTests` — test create (with email, role, 7-day expiry, 32-byte token), accept (consume token, provision user with role, bypass domain restrictions), revoke (set Revoked status), expire (status transition), duplicate prevention in `tests/Sorcha.Tenant.Service.Tests/Services/InvitationServiceTests.cs`
- [x] T043 [P] [US4] Create `DashboardServiceTests` — test active/suspended user counts, users-by-role, recent logins, pending invitation count, IDP status in `tests/Sorcha.Tenant.Service.Tests/Services/DashboardServiceTests.cs`
- [x] T044 [P] [US4] Create `InvitationEndpointTests` — test POST/GET /api/organizations/{orgId}/invitations, POST revoke, authorization (RequireAdministrator) in `tests/Sorcha.Tenant.Service.Tests/Endpoints/InvitationEndpointTests.cs`

### Implementation for User Story 4

- [x] T045 [P] [US4] Create Invitation DTOs in `src/Services/Sorcha.Tenant.Service/Models/Dtos/InvitationDtos.cs` and Dashboard DTOs in `src/Services/Sorcha.Tenant.Service/Models/Dtos/DashboardDtos.cs`
- [x] T046 [P] [US4] Create `IInvitationRepository` / `InvitationRepository` in `src/Services/Sorcha.Tenant.Service/Data/Repositories/IInvitationRepository.cs` and `src/Services/Sorcha.Tenant.Service/Data/Repositories/InvitationRepository.cs`
- [x] T047 [US4] Implement `IInvitationService` / `InvitationService` — create invitation (generate 32-byte token, 7-day expiry, send email via IEmailSender), accept (authenticate, provision with role, bypass domain restrictions), revoke, list by status, check expiry in `src/Services/Sorcha.Tenant.Service/Services/IInvitationService.cs` and `src/Services/Sorcha.Tenant.Service/Services/InvitationService.cs`
- [x] T048 [US4] Implement `IDashboardService` / `DashboardService` — aggregate active/suspended user counts, users-by-role breakdown, recent logins (last 10), pending invitation count, IDP status (configured/enabled/provider name/last IDP login) in `src/Services/Sorcha.Tenant.Service/Services/IDashboardService.cs` and `src/Services/Sorcha.Tenant.Service/Services/DashboardService.cs`
- [x] T049 [US4] Create `InvitationEndpoints` — POST `/api/organizations/{orgId}/invitations` (201 + email sent), GET (list with status filter), POST `/{invitationId}/revoke` in `src/Services/Sorcha.Tenant.Service/Endpoints/InvitationEndpoints.cs`
- [x] T050 [US4] Create `DashboardEndpoints` — GET `/api/organizations/{orgId}/dashboard` with RequireAdministrator policy in `src/Services/Sorcha.Tenant.Service/Endpoints/DashboardEndpoints.cs`
- [x] T051 [US4] Add user management endpoints — POST `/api/organizations/{orgId}/users/{userId}/unlock`, `/suspend`, `/reactivate` to existing `OrganizationEndpoints.cs` with RequireAdministrator policy, session invalidation on suspend in `src/Services/Sorcha.Tenant.Service/Endpoints/OrganizationEndpoints.cs`
- [x] T051a [US4] Add role change endpoint — PUT `/api/organizations/{orgId}/users/{userId}/role` with RequireAdministrator policy, validate target is not SystemAdmin, validate role is one of (Administrator, Designer, Auditor, Member), record role change in audit log (FR-026, FR-037) in `src/Services/Sorcha.Tenant.Service/Endpoints/OrganizationEndpoints.cs`
- [x] T052 [US4] Wire audit events — InvitationSent, InvitationAccepted, InvitationRevoked, AccountUnlockedByAdmin, role change events
- [x] T053 [US4] Register US4 services/repos in DI and add YARP routes for invitation, dashboard, and user management endpoints

**Checkpoint**: Full admin console backend ready — dashboard, user management, invitation system all functional.

---

## Phase 6: User Story 5 — Org Setup & URL Resolution (Priority: P2)

**Goal**: Orgs are accessible via path/subdomain/custom domain URLs. Admins can configure and verify custom domains.

**Independent Test**: Create an org, verify access via all three URL tiers, configure and verify a custom domain.

**FRs covered**: FR-028, FR-029, FR-030, FR-031, FR-032, FR-033, FR-034, FR-042

### Tests for User Story 5

- [ ] T054 [P] [US5] Create `CustomDomainServiceTests` — test configure (set domain, return CNAME instructions), verify (DNS CNAME lookup), status transitions (Pending→Verified/Failed), remove domain in `tests/Sorcha.Tenant.Service.Tests/Services/CustomDomainServiceTests.cs`
- [ ] T054a [P] [US5] Create `UrlResolutionMiddlewareTests` — test path-based extraction (`/org/{subdomain}`), subdomain-based extraction from Host header (`{sub}.sorcha.io`), custom domain lookup via Redis cache, `X-Org-Subdomain` header propagation, unknown domain returns 404 in `tests/Sorcha.ApiGateway.Tests/` or `tests/Sorcha.Tenant.Service.Tests/Integration/UrlResolutionTests.cs`

### Implementation for User Story 5

- [ ] T055 [P] [US5] Create `ICustomDomainRepository` / `CustomDomainRepository` in `src/Services/Sorcha.Tenant.Service/Data/Repositories/ICustomDomainRepository.cs` and `src/Services/Sorcha.Tenant.Service/Data/Repositories/CustomDomainRepository.cs`
- [ ] T056 [US5] Implement `ICustomDomainService` / `CustomDomainService` — configure domain (set CNAME target = `{subdomain}.sorcha.io`), verify via `System.Net.Dns.GetHostEntryAsync()`, status tracking, remove domain in `src/Services/Sorcha.Tenant.Service/Services/ICustomDomainService.cs` and `src/Services/Sorcha.Tenant.Service/Services/CustomDomainService.cs`
- [ ] T057 [US5] Create `CustomDomainEndpoints` — GET/PUT/DELETE `/api/organizations/{orgId}/custom-domain`, POST `verify` with RequireAdministrator policy in `src/Services/Sorcha.Tenant.Service/Endpoints/CustomDomainEndpoints.cs`
- [ ] T058 [US5] Implement `CustomDomainVerificationService` (BackgroundService) — daily CNAME check for all verified domains, update status to Failed if CNAME removed, log status changes in `src/Services/Sorcha.Tenant.Service/Services/CustomDomainVerificationService.cs`
- [ ] T059 [US5] Implement URL resolution middleware in API Gateway — path-based (`/org/{subdomain}`) extraction, subdomain-based (`{sub}.sorcha.io`) extraction from Host header, custom domain lookup via `CustomDomainMapping` (Redis cached, 5min TTL), add `X-Org-Subdomain` header to downstream requests in `src/Services/Sorcha.ApiGateway/`
- [ ] T060 [US5] Wire audit events — CustomDomainConfigured, CustomDomainVerified, CustomDomainFailed
- [ ] T061 [US5] Register US5 services/repos in DI and add YARP routes for custom domain endpoints

**Checkpoint**: All three URL resolution tiers work. Custom domain lifecycle (configure → verify → monitor) functional.

---

## Phase 7: User Story 6 — Admin Configures Domain Restrictions (Priority: P3)

**Goal**: Admins can restrict auto-provisioning to specific email domains while allowing invited users through.

**Independent Test**: Configure domain restrictions, attempt OIDC login with matching and non-matching emails, verify invitation bypass.

**FRs covered**: FR-019, FR-020, FR-021, FR-022

### Tests for User Story 6

- [ ] T062 [P] [US6] Create domain restriction endpoint tests — test GET/PUT `/api/organizations/{orgId}/domain-restrictions`, verify OidcProvisioningService checks domain restrictions, verify invitation bypass in `tests/Sorcha.Tenant.Service.Tests/Endpoints/` (extend existing endpoint tests or create new)

### Implementation for User Story 6

- [ ] T063 [US6] Create `DomainRestrictionEndpoints` — GET `/api/organizations/{orgId}/domain-restrictions` (return allowedDomains, restrictionsActive), PUT (update allowed domains, empty array = no restrictions) with RequireAdministrator policy in `src/Services/Sorcha.Tenant.Service/Endpoints/DomainRestrictionEndpoints.cs`
- [ ] T064 [US6] Update `OrganizationService` — add methods to get/set AllowedEmailDomains on Organization entity in `src/Services/Sorcha.Tenant.Service/Services/OrganizationService.cs`
- [ ] T065 [US6] Wire audit event — DomainRestrictionUpdated on domain list change
- [ ] T066 [US6] Add YARP routes for domain restriction endpoints

**Checkpoint**: Domain restrictions enforce email domain matching on OIDC provisioning. Invitations bypass restrictions.

---

## Phase 8: User Story 7 — Org Admin Reviews Audit Log (Priority: P3)

**Goal**: Admins can search, filter, and review security-relevant audit events. Retention is configurable with automatic purge.

**Independent Test**: Perform various admin actions, verify they appear in the audit log with correct filtering.

**FRs covered**: FR-040, FR-041, FR-048, FR-049

### Tests for User Story 7

- [ ] T067 [P] [US7] Create `AuditEndpointTests` — test GET `/api/organizations/{orgId}/audit` with date range, event type, user, pagination filters; GET/PUT `audit/retention` in `tests/Sorcha.Tenant.Service.Tests/Endpoints/AuditEndpointTests.cs`

### Implementation for User Story 7

- [ ] T068 [P] [US7] Create audit DTOs — `AuditEventResponse`, `AuditQueryParams` (startDate, endDate, eventType, userId, page, pageSize) in `src/Services/Sorcha.Tenant.Service/Models/Dtos/AuditDtos.cs`
- [ ] T069 [US7] Create `AuditEndpoints` — GET `/api/organizations/{orgId}/audit` (RequireAuditor, filtered paginated query, max pageSize=200), GET/PUT `/api/organizations/{orgId}/audit/retention` (RequireAdministrator, 1-120 months) in `src/Services/Sorcha.Tenant.Service/Endpoints/AuditEndpoints.cs`
- [ ] T070 [US7] Implement `AuditCleanupService` (BackgroundService) — daily at 2 AM UTC, query each org's AuditRetentionMonths, delete events older than retention period, log purge counts per org in `src/Services/Sorcha.Tenant.Service/Services/AuditCleanupService.cs`
- [ ] T071 [US7] Register audit services in DI and add YARP routes for audit endpoints

**Checkpoint**: Audit log queryable with full filtering. Retention configurable. Background cleanup running.

---

## Phase 9: Admin Console UI (Blazor WASM)

**Goal**: Blazor WASM admin pages for all admin features — dashboard, user management, IDP config, invitations, domain restrictions, audit log, org settings.

**Depends on**: All backend user stories (Phases 2-8)

### UI Service Clients

- [ ] T072 [P] Create `IIdpConfigurationService` / `IdpConfigurationService` (HTTP client for IDP config API) in `src/Apps/Sorcha.Admin/Sorcha.Admin.Client/Services/IIdpConfigurationService.cs` and `src/Apps/Sorcha.Admin/Sorcha.Admin.Client/Services/IdpConfigurationService.cs`
- [ ] T073 [P] Create `IInvitationService` / `InvitationService` (HTTP client for invitation API) in `src/Apps/Sorcha.Admin/Sorcha.Admin.Client/Services/IInvitationService.cs` and `src/Apps/Sorcha.Admin/Sorcha.Admin.Client/Services/InvitationService.cs`
- [ ] T074 [P] Update `IAuditService` — add retention get/set, query params (date range, event type, user, pagination) in `src/Apps/Sorcha.Admin/Sorcha.Admin.Client/Services/IAuditService.cs`

### Blazor Pages

- [ ] T075 [P] Create `OrgDashboard.razor` — display active/suspended user counts, users-by-role, recent logins, pending invitations, IDP status in `src/Apps/Sorcha.Admin/Sorcha.Admin.Client/Pages/Identity/OrgDashboard.razor`
- [ ] T076 [P] Create `UserManagement.razor` — searchable user table with filter by role/status, inline role change, suspend/reactivate/unlock actions in `src/Apps/Sorcha.Admin/Sorcha.Admin.Client/Pages/Identity/UserManagement.razor`
- [ ] T077 [P] Create `IdpConfiguration.razor` — IDP setup wizard with provider preset dropdown (top 5 shortcuts), issuer URL, client ID/secret fields, discover/test/toggle actions in `src/Apps/Sorcha.Admin/Sorcha.Admin.Client/Pages/Identity/IdpConfiguration.razor`
- [ ] T078 [P] Create `Invitations.razor` — invitation management: create (email, role, expiry), list with status filter, revoke action in `src/Apps/Sorcha.Admin/Sorcha.Admin.Client/Pages/Identity/Invitations.razor`
- [ ] T079 [P] Create `DomainRestrictions.razor` — allowed domains list management (add/remove domains, toggle restrictions) in `src/Apps/Sorcha.Admin/Sorcha.Admin.Client/Pages/Identity/DomainRestrictions.razor`
- [ ] T080 [P] Create `AuditLog.razor` — audit event viewer with date range picker, event type filter, user filter, pagination in `src/Apps/Sorcha.Admin/Sorcha.Admin.Client/Pages/Identity/AuditLog.razor`
- [ ] T081 [P] Create `OrgSettings.razor` — org type display, self-registration toggle, audit retention config in `src/Apps/Sorcha.Admin/Sorcha.Admin.Client/Pages/Settings/OrgSettings.razor`
- [ ] T082 Register new UI services in DI via `ServiceCollectionExtensions.AddAdminServices()` and add navigation menu entries

**Checkpoint**: Full admin console UI operational — all 7 pages connected to backend APIs.

---

## Phase 10: Polish & Cross-Cutting Concerns

**Purpose**: Integration testing, documentation, YARP route verification, final quality pass.

- [ ] T083 Verify all YARP routes are configured for new endpoints (30 new routes) — check `AuthorizationPolicy` on each route in `src/Services/Sorcha.ApiGateway/`
- [ ] T084 [P] Update Tenant Service README with new endpoints, configuration, and features
- [ ] T085 [P] Update `docs/reference/API-DOCUMENTATION.md` with all new REST endpoints
- [ ] T086 [P] Update `docs/reference/development-status.md` with Tenant Service status change
- [ ] T087 [P] Update `docs/guides/AUTHENTICATION-SETUP.md` with OIDC configuration guide
- [ ] T088 Update `.specify/MASTER-TASKS.md` — mark completed tasks, add new work items
- [ ] T089 Run quickstart.md validation — execute all 4 flows against running Tenant Service
- [ ] T090 Verify all new endpoints have `.WithSummary()` and `.WithDescription()` for OpenAPI/Scalar compliance
- [ ] T091 Verify all new public API methods have `/// <summary>` XML docs
- [ ] T092 Final build — verify 0 warnings, all tests pass, >85% coverage on new code

**Checkpoint**: Feature complete, documented, tested, ready for PR.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 (Setup)**: No dependencies — start immediately
- **Phases 2-8 (User Stories)**: All depend on Phase 1 completion
- **Phase 9 (Admin UI)**: Depends on Phases 2-8 backend completion
- **Phase 10 (Polish)**: Depends on all phases

### User Story Dependencies

- **US1 (IDP Config)** — P1: Can start after Phase 1. No story dependencies.
- **US2 (OIDC Login)** — P1: Depends on US1 (IDP must be configured to test login flow).
- **US3 (Social Login/Registration)** — P1: Depends on US2 (OIDC flow reused for social login).
- **US4 (Admin User Management)** — P2: Can start after Phase 1. Independent of US1-3 for backend, but testing is richer with users from US2/3.
- **US5 (Org Setup & URL Resolution)** — P2: Can start after Phase 1. Independent of other stories.
- **US6 (Domain Restrictions)** — P3: Can start after Phase 1. Tests integration with US2 provisioning flow.
- **US7 (Audit Log)** — P3: Can start after Phase 1. Richer testing after other stories generate audit events.

### Within Each User Story

1. Tests written first (should fail)
2. DTOs and models before services
3. Services before endpoints
4. Core implementation before audit event wiring
5. DI registration and YARP routes last

### Parallel Opportunities

**Phase 1** — T001-T004 (enums) run in parallel; T009-T010 (new entities) run in parallel; T014 (email sender) parallel with entity work.

**After Phase 1** — US1, US4, US5, US6, US7 can all start in parallel (independent backends). US2 waits for US1. US3 waits for US2.

**Phase 9** — All 7 Blazor pages (T075-T081) and 3 service clients (T072-T074) can run in parallel.

---

## Parallel Example: Phase 1 (Setup)

```bash
# Wave 1 — all enums in parallel:
T001: OrgType enum
T002: CustomDomainStatus enum
T003: InvitationStatus enum
T004: ProvisioningMethod enum

# Wave 2 — entity modifications (after enums):
T005: UserRole consolidation
T006: Organization model changes
T007: UserIdentity model changes
T008: IdentityProviderConfiguration model changes
T009: OrgInvitation entity (parallel with T010)
T010: CustomDomainMapping entity (parallel with T009)
T013: AuditEventType extension (parallel with T009/T010)
T014: IEmailSender abstraction (parallel with T009/T010)

# Wave 3 — depends on all models:
T011: TenantDbContext update
T012: EF Core migration
T015: DI registration
T016: Auth policy update
```

## Parallel Example: User Story 1

```bash
# Wave 1 — tests + DTOs in parallel:
T017: OidcDiscoveryServiceTests
T018: IdpConfigurationServiceTests
T019: IdpConfigurationEndpointTests
T020: IDP DTOs

# Wave 2 — services (after DTOs):
T021: OidcDiscoveryService
T022: IdpConfigurationService (depends on T021)

# Wave 3 — endpoints + wiring:
T023: IdpConfigurationEndpoints
T024: DI + YARP routes
```

---

## Implementation Strategy

### MVP First (US1 Only)

1. Complete Phase 1: Setup (data model + migration)
2. Complete Phase 2: User Story 1 (IDP configuration)
3. **STOP and VALIDATE**: Admin can configure an IDP end-to-end
4. Deploy/demo if ready

### Core Auth MVP (US1 + US2)

1. Complete Phase 1 + Phase 2 (IDP config)
2. Complete Phase 3: User Story 2 (OIDC login)
3. **STOP and VALIDATE**: Users can sign in via external IDP
4. This is the minimum viable identity management feature

### Full P1 Delivery (US1 + US2 + US3)

1. Complete Phases 1-4 (all P1 stories)
2. **STOP and VALIDATE**: Enterprise IDP + social login + local registration all work
3. This covers the three most critical user journeys

### Incremental Delivery

1. Setup → Foundation ready
2. Add US1 → Test → Demo (IDP config)
3. Add US2 → Test → Demo (OIDC login)
4. Add US3 → Test → Demo (Social login + registration)
5. Add US4 → Test → Demo (Admin console)
6. Add US5 → Test → Demo (URL resolution)
7. Add US6 + US7 → Test → Demo (Domain restrictions + audit)
8. Add Admin UI → Test → Demo (full admin console)
9. Polish → PR ready

---

## Summary

| Metric | Count |
|--------|-------|
| Total tasks | 95 |
| Phase 1 (Setup) | 16 |
| US1 (IDP Config) | 8 |
| US2 (OIDC Login) | 12 |
| US3 (Social Login) | 6 |
| US4 (User Management) | 13 |
| US5 (URL Resolution) | 9 |
| US6 (Domain Restrictions) | 5 |
| US7 (Audit Log) | 5 |
| Admin UI | 11 |
| Polish | 10 |
| Parallel opportunities | 40 tasks (43%) |
| Independent test criteria | 7 stories |
| MVP scope | US1 only (24 tasks: Phase 1 + Phase 2) |

---

## Notes

- [P] tasks = different files, no dependencies
- [Story] label maps task to specific user story for traceability
- Each user story is independently completable and testable
- Verify tests fail before implementing
- Commit after each task or logical group
- Stop at any checkpoint to validate story independently
- All endpoints must have `.WithSummary()` and `.WithDescription()` (Scalar compliance)
- All public methods must have `/// <summary>` XML docs
- License header required on all new files: `// SPDX-License-Identifier: MIT` + `// Copyright (c) 2026 Sorcha Contributors`
