# Tasks: Passkey (WebAuthn/FIDO2) Authentication

**Input**: Design documents from `/specs/055-passkey-auth/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/passkey-api.md

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- Include exact file paths in descriptions

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Fido2NetLib configuration, DI registration, and JS interop foundation

- [ ] T001 Add Fido2 configuration section to `src/Services/Sorcha.Tenant.Service/appsettings.json` with ServerDomain, ServerName, and Origins settings
- [ ] T002 Register Fido2 services and configuration in `src/Services/Sorcha.Tenant.Service/Extensions/ServiceCollectionExtensions.cs` using `builder.Services.AddFido2()`
- [ ] T003 [P] Create WebAuthn JS interop module at `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/wwwroot/js/webauthn.js` with `createCredential()` and `getCredential()` functions wrapping `navigator.credentials.create()` and `.get()`
- [ ] T004 [P] Create `PasskeyInteropService.cs` at `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Services/PasskeyInteropService.cs` — C# wrapper using `IJSRuntime` to call webauthn.js functions, handling JSON serialization of WebAuthn options/responses

**Checkpoint**: Fido2 DI registered, JS interop layer ready

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Data model entities, DbContext configuration, EF migration, and core PasskeyService

**CRITICAL**: No user story work can begin until this phase is complete

- [ ] T005 Create `PasskeyCredential.cs` model at `src/Services/Sorcha.Tenant.Service/Models/PasskeyCredential.cs` — all 16 fields per data-model.md (Id, CredentialId, PublicKeyCose, SignatureCounter, OwnerType, OwnerId, OrganizationId, DisplayName, DeviceType, AttestationType, AaGuid, Status, CreatedAt, LastUsedAt, DisabledAt, DisabledReason)
- [ ] T006 [P] Create `SocialLoginLink.cs` model at `src/Services/Sorcha.Tenant.Service/Models/SocialLoginLink.cs` — 8 fields per data-model.md (Id, PublicIdentityId, ProviderType, ExternalSubjectId, LinkedEmail, DisplayName, CreatedAt, LastUsedAt)
- [ ] T007 Modify `PublicIdentity.cs` at `src/Services/Sorcha.Tenant.Service/Models/PublicIdentity.cs` — remove `PassKeyCredentialId`, `PublicKeyCose`, `SignatureCounter` fields; add `EmailVerified` (bool), `EmailVerifiedAt` (DateTimeOffset?); add navigation properties `ICollection<PasskeyCredential> PasskeyCredentials` and `ICollection<SocialLoginLink> SocialLoginLinks`
- [ ] T008 Configure `PasskeyCredential` and `SocialLoginLink` entities in `src/Services/Sorcha.Tenant.Service/Data/TenantDbContext.cs` — add DbSets, configure indexes (unique on CredentialId, composite on OwnerType+OwnerId, composite on OwnerId+Status, on OrganizationId; unique composite on ProviderType+ExternalSubjectId, on PublicIdentityId), configure relationships
- [ ] T009 Generate EF Core migration for PasskeyCredential and SocialLoginLink tables and PublicIdentity column changes — run `dotnet ef migrations add AddPasskeyAndSocialLogin` from Tenant Service directory
- [ ] T010 Create `IPasskeyService.cs` interface at `src/Services/Sorcha.Tenant.Service/Services/IPasskeyService.cs` — define methods: `CreateRegistrationOptionsAsync`, `VerifyRegistrationAsync`, `CreateAssertionOptionsAsync`, `VerifyAssertionAsync`, `GetCredentialsByOwnerAsync`, `RevokeCredentialAsync`
- [ ] T011 Implement `PasskeyService.cs` at `src/Services/Sorcha.Tenant.Service/Services/PasskeyService.cs` — inject `IFido2`, `IDistributedCache`, `TenantDbContext`; implement registration options (store challenge in Redis with `passkey:challenge:{transactionId}` key, 5-min TTL), registration verification (validate attestation, store credential), assertion options, assertion verification (validate signature, update counter, detect cloned authenticator), credential listing, credential revocation
- [ ] T012 Register `IPasskeyService`/`PasskeyService` in DI at `src/Services/Sorcha.Tenant.Service/Extensions/ServiceCollectionExtensions.cs`
- [ ] T013 Build and verify solution compiles: `dotnet build src/Services/Sorcha.Tenant.Service`

**Checkpoint**: Foundation ready — entities, migrations, and core service available for all user stories

---

## Phase 3: User Story 1 — Org User Registers a Passkey as Second Factor (Priority: P1) MVP

**Goal**: Org users can register passkeys in security settings and use them as an alternative 2FA method alongside TOTP

**Independent Test**: Log in as org user → navigate to security settings → register passkey → log out → log in with password → complete 2FA with passkey

### Implementation for User Story 1

- [ ] T014 [US1] Create passkey registration endpoints in `src/Services/Sorcha.Tenant.Service/Endpoints/PasskeyEndpoints.cs` — `POST /api/passkey/register/options` (generate CredentialCreateOptions, store challenge in Redis, return transactionId + options), `POST /api/passkey/register/verify` (validate attestation response, create PasskeyCredential record, return credentialId/displayName/createdAt). Both require authenticated org user JWT. Rate limit: 10 registrations per 24 hours per user.
- [ ] T015 [US1] Create passkey credential management endpoints in `src/Services/Sorcha.Tenant.Service/Endpoints/PasskeyEndpoints.cs` — `GET /api/passkey/credentials` (list user's passkeys with id, displayName, deviceType, status, createdAt, lastUsedAt, maxCredentials:10), `DELETE /api/passkey/credentials/{id}` (revoke credential, return 400 if last auth method, 404 if not found, 204 on success)
- [ ] T016 [US1] Modify login response in `src/Services/Sorcha.Tenant.Service/Endpoints/AuthEndpoints.cs` — when 2FA is required, query user's active PasskeyCredentials; add `availableMethods` array (e.g., `["totp", "passkey"]`) to the existing `TwoFactorLoginResponse`
- [ ] T017 [US1] Create passkey 2FA verification endpoint in `src/Services/Sorcha.Tenant.Service/Endpoints/AuthEndpoints.cs` — `POST /api/auth/verify-passkey` accepting `loginToken` + `assertionResponse`; validate loginToken, create assertion challenge scoped to user's credentials, verify assertion, issue full JWT TokenResponse on success
- [ ] T018 [US1] Add YARP routes for org passkey endpoints in `src/Services/Sorcha.ApiGateway/appsettings.json` — routes: `passkey-register-options`, `passkey-register-verify`, `passkey-credentials`, `auth-verify-passkey` all pointing to tenant-cluster with appropriate auth policies (Required for register/credentials, Anonymous for verify-passkey)
- [ ] T019 [US1] Write unit tests for PasskeyService registration and assertion flows in `tests/Sorcha.Tenant.Service.Tests/Services/PasskeyServiceTests.cs` — test: CreateRegistrationOptions stores challenge in cache, VerifyRegistration creates credential record, VerifyAssertion updates counter, VerifyAssertion disables credential on counter regression, RevokeCredential sets status to Revoked, GetCredentialsByOwner filters by OwnerType+OwnerId
- [ ] T020 [US1] Write endpoint integration tests in `tests/Sorcha.Tenant.Service.Tests/Endpoints/PasskeyEndpointTests.cs` — test: register options returns 200 with transactionId, register verify returns 201 with credentialId, credentials list returns user's passkeys, credentials delete returns 204, verify-passkey returns TokenResponse on valid assertion, verify-passkey returns 401 on invalid credential

**Checkpoint**: Org users can register passkeys, manage them in settings, and use them as 2FA. Story 1 is fully functional and independently testable.

---

## Phase 4: User Story 2 — Public User Signs Up with a Passkey (Priority: P2)

**Goal**: Public users can create accounts using passkeys as primary auth and sign in with them

**Independent Test**: Visit sign-up page → select Passkey → enter name/email → complete ceremony → receive session → log out → sign in with passkey

### Implementation for User Story 2

- [ ] T021 [US2] Create `IPublicUserService.cs` interface at `src/Services/Sorcha.Tenant.Service/Services/IPublicUserService.cs` — define methods: `CreatePublicUserAsync` (from passkey registration), `CreatePublicUserFromSocialAsync` (from social login callback), `GetPublicUserByIdAsync`, `GetPublicUserByEmailAsync`, `GetPublicUserByCredentialIdAsync`
- [ ] T022 [US2] Implement `PublicUserService.cs` at `src/Services/Sorcha.Tenant.Service/Services/PublicUserService.cs` — inject `TenantDbContext`, `IPasskeyService`; implement public user creation (create PublicIdentity + link PasskeyCredential), lookup by email/id/credentialId, handle duplicate email detection (return 409)
- [ ] T023 [US2] Register `IPublicUserService`/`PublicUserService` in DI at `src/Services/Sorcha.Tenant.Service/Extensions/ServiceCollectionExtensions.cs`
- [ ] T024 [US2] Create public passkey registration endpoints in `src/Services/Sorcha.Tenant.Service/Endpoints/PublicAuthEndpoints.cs` — `POST /api/auth/public/passkey/register/options` (accept displayName + email, check for existing email → 409, generate CredentialCreateOptions, store challenge + user info in Redis, return transactionId + options), `POST /api/auth/public/passkey/register/verify` (validate attestation, create PublicIdentity + PasskeyCredential, issue JWT via TokenService.GeneratePublicUserTokenAsync, return TokenResponse). Rate limit: 5 per minute per IP.
- [ ] T025 [US2] Create passkey assertion endpoints in `src/Services/Sorcha.Tenant.Service/Endpoints/PublicAuthEndpoints.cs` — `POST /api/auth/passkey/assertion/options` (optional email for non-discoverable flow, generate AssertionOptions with allowCredentials if email provided, store challenge in Redis, return transactionId + options), `POST /api/auth/passkey/assertion/verify` (validate assertion, lookup owner — if PublicIdentity issue public JWT, if OrgUser issue org JWT, return TokenResponse). Rate limit: 5 per minute per IP.
- [ ] T026 [US2] Add YARP routes for public passkey endpoints in `src/Services/Sorcha.ApiGateway/appsettings.json` — routes: `auth-public-passkey` (Anonymous), `auth-passkey-assertion` (Anonymous)
- [ ] T027 [US2] Write unit tests for PublicUserService in `tests/Sorcha.Tenant.Service.Tests/Services/PublicUserServiceTests.cs` — test: CreatePublicUser creates PublicIdentity and links credential, CreatePublicUser with duplicate email returns conflict, GetPublicUserByCredentialId returns correct user
- [ ] T028 [US2] Write endpoint integration tests in `tests/Sorcha.Tenant.Service.Tests/Endpoints/PublicAuthEndpointTests.cs` — test: public register options returns 200, public register verify creates user and returns tokens, public register with existing email returns 409, assertion options returns 200, assertion verify returns tokens for valid credential

**Checkpoint**: Public users can register and sign in with passkeys. Story 2 works independently.

---

## Phase 5: User Story 3 — Public User Signs Up with Social Login (Priority: P2)

**Goal**: Public users can create accounts using Google, Microsoft, GitHub, or Apple social login

**Independent Test**: Visit sign-up page → select social provider → authorize → return with account created → sign out → sign in via same provider

### Implementation for User Story 3

- [ ] T029 [US3] Extend `PublicUserService.cs` at `src/Services/Sorcha.Tenant.Service/Services/PublicUserService.cs` — implement `CreatePublicUserFromSocialAsync` (create PublicIdentity from provider claims, create SocialLoginLink record, handle account linking when email matches existing PublicIdentity)
- [ ] T030 [US3] Create social login endpoints in `src/Services/Sorcha.Tenant.Service/Endpoints/PublicAuthEndpoints.cs` — `POST /api/auth/public/social/initiate` (accept provider + redirectUri, generate OAuth authorization URL using existing OidcExchangeService, store state in Redis, return authorizationUrl + state), `POST /api/auth/public/social/callback` (accept provider + code + state, exchange code for tokens via OidcExchangeService, extract claims, create or link PublicIdentity, issue JWT, return TokenResponse with isNewUser flag)
- [ ] T031 [US3] Add YARP route for social login endpoints in `src/Services/Sorcha.ApiGateway/appsettings.json` — route: `auth-public-social` (Anonymous) for `/api/auth/public/social/{**catch-all}`
- [ ] T032 [US3] Write integration tests for social login endpoints in `tests/Sorcha.Tenant.Service.Tests/Endpoints/PublicAuthEndpointTests.cs` — test: social initiate returns authorization URL, social callback creates new user with provider claims, social callback links existing user when email matches, social callback returns isNewUser flag correctly

**Checkpoint**: Public users can register and sign in with all 4 social providers. Story 3 works independently.

---

## Phase 6: User Story 4 — Org User Authenticates with Passkey at 2FA Step (Priority: P3)

**Goal**: Org users with registered passkeys can choose passkey over TOTP at the 2FA prompt

**Independent Test**: Log in as org user with both TOTP and passkey → verify method selection is shown → choose passkey → complete ceremony → receive session. Also test: cancel passkey → switch to TOTP.

### Implementation for User Story 4

- [ ] T033 [US4] Add assertion options endpoint for org user 2FA in `src/Services/Sorcha.Tenant.Service/Endpoints/AuthEndpoints.cs` — modify `/api/auth/verify-passkey` to first generate assertion options scoped to the user's credentials (from loginToken), then verify the assertion response in a second call. Alternatively, the frontend calls `/api/auth/passkey/assertion/options` with the user's email extracted from loginToken context, then submits to `/api/auth/verify-passkey`.
- [ ] T034 [US4] Update Blazor Login page at `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/Login.razor` — after password verification when `requiresTwoFactor` is true: show available methods from `availableMethods` array; if passkey available, show "Use Passkey" button that calls PasskeyInteropService to trigger browser ceremony; on success, POST assertion to `/api/auth/verify-passkey`; on failure/cancel, allow switching to TOTP entry
- [ ] T035 [US4] Write integration test in `tests/Sorcha.Tenant.Service.Tests/Endpoints/PasskeyEndpointTests.cs` — test: verify-passkey with valid loginToken and valid assertion returns TokenResponse, verify-passkey with expired loginToken returns 401, user with passkey-only gets passkey challenge directly (no method selection)

**Checkpoint**: Full org user 2FA flow with passkey support. Story 4 works independently.

---

## Phase 7: User Story 5 — Public User Manages Authentication Methods (Priority: P3)

**Goal**: Public users can view, add, and remove passkeys and social login links

**Independent Test**: Sign in as public user → navigate to settings → view auth methods → add a social provider → add another passkey → remove a passkey → verify last-method-removal is prevented

### Implementation for User Story 5

- [ ] T036 [US5] Create auth method listing endpoint in `src/Services/Sorcha.Tenant.Service/Endpoints/PublicAuthEndpoints.cs` — `GET /api/auth/public/methods` (requires public user JWT, return passkeys array + socialLinks array per contracts)
- [ ] T037 [US5] Create social link management endpoints in `src/Services/Sorcha.Tenant.Service/Endpoints/PublicAuthEndpoints.cs` — `POST /api/auth/public/social/link` (requires public user JWT, initiate social link flow for existing user, return authorizationUrl), `DELETE /api/auth/public/social/{linkId}` (requires public user JWT, remove social link, return 400 if last auth method, 204 on success)
- [ ] T038 [US5] Create public user passkey registration endpoints in `src/Services/Sorcha.Tenant.Service/Endpoints/PublicAuthEndpoints.cs` — reuse `/api/passkey/register/options` and `/api/passkey/register/verify` for authenticated public users (adapt PasskeyEndpoints to accept both org and public user JWTs), or create dedicated public user passkey add endpoints under `/api/auth/public/passkey/add/options` and `/api/auth/public/passkey/add/verify`
- [ ] T039 [US5] Implement last-auth-method guard in `PublicUserService.cs` at `src/Services/Sorcha.Tenant.Service/Services/PublicUserService.cs` — count total auth methods (active passkeys + social links) before allowing deletion; return error if count would drop to 0
- [ ] T040 [US5] Add YARP routes for public user management endpoints in `src/Services/Sorcha.ApiGateway/appsettings.json` — routes: `auth-public-methods` (Required auth)
- [ ] T041 [US5] Write integration tests in `tests/Sorcha.Tenant.Service.Tests/Endpoints/PublicAuthEndpointTests.cs` — test: methods endpoint returns passkeys and social links, social link deletion returns 204, social link deletion of last method returns 400, passkey addition for authenticated public user works

**Checkpoint**: Public users can fully manage their auth methods. Story 5 works independently.

---

## Phase 8: UI — Public User Registration Page (Priority: P2)

**Goal**: Blazor WASM pages for public user sign-up and sign-in flows

- [ ] T042 Create `PublicSignup.razor` page at `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/PublicSignup.razor` — method-first layout: show "Passkey" button and 4 social provider buttons (Google, Microsoft, GitHub, Apple); passkey flow: collect displayName + email → call PasskeyInteropService.CreateCredential → POST to `/api/auth/public/passkey/register/verify` → store tokens → redirect; social flow: call `/api/auth/public/social/initiate` → redirect to provider → handle callback
- [ ] T043 Modify `Login.razor` at `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/Login.razor` — add "Sign in with Passkey" button below the password form; on click: call PasskeyInteropService.GetCredential (discoverable flow, no email needed) → POST to `/api/auth/passkey/assertion/verify` → store tokens → redirect. Also add link to PublicSignup page.
- [ ] T044 Add WebAuthn browser compatibility detection in `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/wwwroot/js/webauthn.js` — export `isWebAuthnSupported()` function checking `window.PublicKeyCredential` availability; use in PasskeyInteropService to conditionally show/hide passkey options (FR-017)

**Checkpoint**: Complete UI for public sign-up and passkey sign-in

---

## Phase 9: Polish & Cross-Cutting Concerns

**Purpose**: Documentation, security hardening, and validation

- [ ] T045 [P] Update Tenant Service README with passkey endpoint documentation
- [ ] T046 [P] Update `docs/reference/API-DOCUMENTATION.md` with all new passkey and public auth endpoints
- [ ] T047 [P] Update `docs/guides/AUTHENTICATION-SETUP.md` with passkey configuration and Fido2 settings
- [ ] T048 Add structured logging for all passkey operations in `PasskeyService.cs` — log registration, assertion, counter regression, credential revocation with correlation IDs
- [ ] T049 Ensure all new endpoints have `.WithSummary()` and `.WithDescription()` for Scalar OpenAPI documentation
- [ ] T050 [P] Update `.specify/MASTER-TASKS.md` with passkey feature completion status
- [ ] T051 Run quickstart.md validation — build `dotnet build src/Services/Sorcha.Tenant.Service` and test `dotnet test tests/Sorcha.Tenant.Service.Tests --filter "FullyQualifiedName~Passkey"`

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — can start immediately
- **Foundational (Phase 2)**: Depends on Setup completion — BLOCKS all user stories
- **US1 (Phase 3)**: Depends on Foundational — MVP, implement first
- **US2 (Phase 4)**: Depends on Foundational — can run in parallel with US1 after T011 (PasskeyService)
- **US3 (Phase 5)**: Depends on Foundational + US2 (needs PublicUserService from T022)
- **US4 (Phase 6)**: Depends on US1 (needs passkey endpoints and 2FA flow modifications)
- **US5 (Phase 7)**: Depends on US2 + US3 (needs PublicUserService and social login flow)
- **UI (Phase 8)**: Depends on US2 + US4 (needs backend endpoints to exist)
- **Polish (Phase 9)**: Depends on all prior phases

### User Story Dependencies

```
Phase 1: Setup
    │
Phase 2: Foundational (BLOCKS ALL)
    │
    ├── Phase 3: US1 (Org passkey 2FA) ──────────┐
    │                                              │
    ├── Phase 4: US2 (Public passkey signup) ──┐   │
    │                                          │   │
    │   Phase 5: US3 (Public social signup) ◄──┘   │
    │       │                                      │
    │   Phase 7: US5 (Auth method mgmt) ◄──────┘   │
    │                                              │
    │   Phase 6: US4 (Org passkey 2FA login) ◄─────┘
    │       │
    │   Phase 8: UI ◄──────────────────────────────┘
    │
Phase 9: Polish
```

### Within Each User Story

- Models before services
- Services before endpoints
- Endpoints before YARP routes
- Core implementation before tests
- Story complete before moving to next priority

### Parallel Opportunities

- T003 + T004 (JS interop + C# wrapper) — different files
- T005 + T006 (PasskeyCredential + SocialLoginLink models) — different files
- T019 + T020 (US1 unit + integration tests) — different test files
- T027 + T028 (US2 unit + integration tests) — different test files
- T045 + T046 + T047 + T050 (documentation updates) — different files
- US1 and US2 can proceed in parallel after Phase 2 (share PasskeyService but touch different endpoints)

---

## Parallel Example: Foundational Phase

```bash
# Launch model creation in parallel (different files):
Task: "Create PasskeyCredential.cs model"        # T005
Task: "Create SocialLoginLink.cs model"           # T006

# Then sequentially:
Task: "Modify PublicIdentity.cs"                  # T007 (depends on understanding T005/T006)
Task: "Configure TenantDbContext"                 # T008 (depends on T005, T006, T007)
Task: "Generate EF migration"                     # T009 (depends on T008)
```

## Parallel Example: User Story 1

```bash
# After T011 (PasskeyService) is complete:
Task: "Create passkey registration endpoints"      # T014
Task: "Create credential management endpoints"     # T015
# These touch the same file (PasskeyEndpoints.cs) — run sequentially

# After endpoints exist:
Task: "Unit tests for PasskeyService"              # T019
Task: "Integration tests for endpoints"            # T020
# Different test files — can run in parallel
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup (T001-T004)
2. Complete Phase 2: Foundational (T005-T013)
3. Complete Phase 3: User Story 1 (T014-T020)
4. **STOP and VALIDATE**: Test org user passkey 2FA end-to-end
5. Deploy/demo if ready

### Incremental Delivery

1. Setup + Foundational → Foundation ready
2. Add User Story 1 → Test org passkey 2FA → Deploy (MVP!)
3. Add User Story 2 → Test public passkey signup → Deploy
4. Add User Story 3 → Test public social login → Deploy
5. Add User Story 4 → Test org 2FA login flow → Deploy
6. Add User Story 5 + UI → Test auth method management → Deploy
7. Polish → Documentation, logging, validation

### Suggested MVP Scope

**Phase 1 + Phase 2 + Phase 3 (US1)** = Org user passkey registration and 2FA. This delivers immediate value to the most security-sensitive user group.
