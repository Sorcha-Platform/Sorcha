# Tasks: Auto-Register Participant & PlatformUser Provisioning

**Input**: Design documents from `/specs/077-participant-autolink-user-provisioning/`
**Prerequisites**: plan.md (required), spec.md (required), research.md, data-model.md, contracts/

**Tests**: Included — CLAUDE.md mandates >85% coverage; both GAPs involve security-sensitive operations (wallet linking, user provisioning).

**Organization**: Tasks grouped by user story (3 stories). US1 (auto-link) and US2 (admin provisioning) are co-P1 and independent. US3 (password reset) extends US2.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- Include exact file paths in descriptions

---

## Phase 1: Setup

**Purpose**: Shared DTOs, validators, and service interfaces needed by multiple user stories.

- [x] T001 [P] Create `AdminProvisionUserRequest` and `AdminProvisionUserResponse` DTOs at `src/Services/Sorcha.Tenant.Service/Models/Dtos/PlatformUserProvisioningDtos.cs` — fields per data-model.md
- [x] T002 [P] Create `AdminResetPasswordRequest` DTO at `src/Services/Sorcha.Tenant.Service/Models/Dtos/PlatformUserProvisioningDtos.cs` (same file as T001)
- [x] T003 [P] Create `AutoLinkResult` internal model at `src/Services/Sorcha.Tenant.Service/Models/AutoLinkResult.cs` — ParticipantCreated, WalletLinked, ParticipantId, SkipReason
- [x] T004 [P] Create `AdminProvisionUserValidator` — using inline validation (Tenant Service convention, no FluentValidation)
- [x] T005 Add `VerificationMethod` string property to `LinkedWalletAddress` entity at `src/Services/Sorcha.Tenant.Service/Models/LinkedWalletAddress.cs` — default "challenge-verify", new value "self-created" for auto-links
- [x] T006 Verify solution builds cleanly after model changes: `dotnet build`

**Checkpoint**: All new DTOs and models compile.

---

## Phase 2: User Story 1 — Auto-Link Wallet During Creation (Priority: P1)

**Goal**: Wallet creation automatically registers participant and links wallet. No manual steps needed.

**Independent Test**: Create wallet as user with no participant record, then subscribe to ActionsHub — subscription succeeds.

### Tests for User Story 1

- [ ] T007 [P] [US1] Unit test for auto-link logic — verify participant created if missing, wallet linked with VerificationMethod="self-created", existing participant reused, platform uniqueness respected, failure doesn't throw at `tests/Sorcha.Tenant.Service.Tests/Services/AutoLinkWalletTests.cs`
- [ ] T008 [P] [US1] Unit test for wallet creation post-hook — verify auto-link called after successful wallet creation, verify auto-link failure is logged but doesn't fail wallet creation at `tests/Sorcha.Wallet.Service.Tests/Unit/WalletCreationAutoLinkTests.cs`

### Implementation for User Story 1

- [x] T009 [US1] Add `AutoLinkWalletAsync` method to `ParticipantService` — check if participant exists for user+org, self-register if not, create `LinkedWalletAddress` with VerificationMethod="self-created" (bypass challenge/verify), respect platform uniqueness at `src/Services/Sorcha.Tenant.Service/Services/ParticipantService.cs`
- [x] T010 [US1] Add `AutoLinkWalletAsync` to `IParticipantServiceClient` interface and implement in `ParticipantServiceClient` — POST to internal endpoint at `src/Common/Sorcha.ServiceClients/Participant/IParticipantServiceClient.cs` and `ParticipantServiceClient.cs`
- [x] T011 [US1] Add internal endpoint `POST /api/internal/participants/auto-link` to Tenant Service — accepts walletAddress, userId, orgId; calls `ParticipantService.AutoLinkWalletAsync`; returns `AutoLinkResult` at `src/Services/Sorcha.Tenant.Service/Endpoints/ParticipantEndpoints.cs`
- [x] T012 [US1] Add post-creation auto-link call to wallet creation endpoint — after wallet created successfully, fire-and-forget call to `IParticipantServiceClient.AutoLinkWalletAsync(walletAddress, userId, orgId)`; log warning on failure at `src/Services/Sorcha.Wallet.Service/Endpoints/WalletEndpoints.cs`
- [x] T013 [US1] Add YARP route for internal auto-link endpoint — not needed, uses direct service-to-service communication via `IParticipantServiceClient`

**Checkpoint**: Create wallet → participant auto-registered → wallet auto-linked → token refresh includes wallet_address → ActionsHub subscription works.

---

## Phase 3: User Story 2 — Admin User Provisioning (Priority: P1)

**Goal**: SystemAdmin creates PlatformUser + UserIdentity + OrgMembership in one call.

**Independent Test**: Create user in private org with password + skipEmailVerification, then log in as that user.

### Tests for User Story 2

- [x] T014 [P] [US2] Unit test for provisioning service — verify PlatformUser created, UserIdentity created, OrgMembership created, existing PlatformUser reused, password hashed, skipEmailVerification works, invalid org returns error at `tests/Sorcha.Tenant.Service.Tests/Services/PlatformUserProvisioningTests.cs`
- [x] T015 [P] [US2] Integration test for provisioning endpoint — verify 201 on success, 404 on bad org, 409 on duplicate user+org, 403 on non-admin, 400 on validation errors at `tests/Sorcha.Tenant.Service.Tests/Endpoints/PlatformUserEndpointTests.cs`

### Implementation for User Story 2

- [x] T016 [US2] Create `IPlatformUserProvisioningService` interface and `PlatformUserProvisioningService` implementation — ProvisionUserAsync creates PlatformUser (or reuses by email) + UserIdentity + PlatformUserOrgMembership, hashes password if provided, sets EmailVerified if skipEmailVerification at `src/Services/Sorcha.Tenant.Service/Services/PlatformUserProvisioningService.cs`
- [x] T017 [US2] Register `IPlatformUserProvisioningService` in DI at `src/Services/Sorcha.Tenant.Service/Program.cs`
- [x] T018 [US2] Add `POST /api/platform/users` endpoint — validates request via `AdminProvisionUserValidator`, calls `PlatformUserProvisioningService.ProvisionUserAsync`, requires SystemAdmin authorisation, returns `AdminProvisionUserResponse` at `src/Services/Sorcha.Tenant.Service/Endpoints/PlatformManagementEndpoints.cs`
- [x] T019 [US2] Add YARP route for `POST /api/platform/users` at `src/Services/Sorcha.ApiGateway/appsettings.json`

**Checkpoint**: Admin can create user in private org → user can log in → user appears in org with correct role.

---

## Phase 4: User Story 3 — Admin Password Reset (Priority: P2)

**Goal**: SystemAdmin can reset any user's password.

**Independent Test**: Create user with password A, admin resets to password B, user can only log in with B.

### Tests for User Story 3

- [ ] T020 [P] [US3] Unit test for password reset — verify password hash updated, old password rejected, NIST policy enforced, non-existent user returns 404, non-admin rejected at `tests/Sorcha.Tenant.Service.Tests/Endpoints/PasswordResetEndpointTests.cs`

### Implementation for User Story 3

- [ ] T021 [US3] Add `ResetPasswordAsync` to `PlatformUserProvisioningService` — validates password against NIST policy, updates PlatformUser.PasswordHash at `src/Services/Sorcha.Tenant.Service/Services/PlatformUserProvisioningService.cs`
- [ ] T022 [US3] Add `PUT /api/platform/users/{id}/password` endpoint — requires SystemAdmin, validates request, calls ResetPasswordAsync at `src/Services/Sorcha.Tenant.Service/Endpoints/PlatformManagementEndpoints.cs`
- [ ] T023 [US3] Add YARP route for `PUT /api/platform/users/*/password` at `src/Services/Sorcha.ApiGateway/appsettings.json`

**Checkpoint**: Admin resets password → old password fails → new password succeeds.

---

## Phase 5: Polish & Cross-Cutting Concerns

**Purpose**: Documentation, OpenAPI annotations, regression validation.

- [ ] T024 [P] Add XML doc comments and Scalar OpenAPI annotations (WithName, WithSummary, WithDescription) to all new endpoints (auto-link, provisioning, password reset) per Constitution Principle III
- [ ] T025 [P] Update `.specify/MASTER-TASKS.md` — mark GAP-018 and GAP-019 as complete
- [ ] T026 [P] Update `docs/reference/development-status.md` — add Feature 077 completion entry
- [ ] T027 [P] Update `CLAUDE.md` Participant Identity API section if new endpoints change the API surface
- [ ] T028 Run full test suite: `dotnet test` — verify zero regressions (SC-006)
- [ ] T029 Run quickstart.md validation scenarios (7 scenarios)

---

## Dependencies & Execution Order

### Phase Dependencies

```
Phase 1 (Setup) ──→ Phase 2 (US1: Auto-Link) ── independent
                ──→ Phase 3 (US2: Admin Provisioning) ── independent
                ──→ Phase 4 (US3: Password Reset) ── depends on US2 service

Phase 5 (Polish) ── after all user stories complete
```

### User Story Dependencies

- **US1 (P1)**: Can start after Phase 1 — modifies Wallet + Tenant services
- **US2 (P1)**: Can start after Phase 1 — modifies Tenant Service only. Independent of US1.
- **US3 (P2)**: Depends on US2 (extends PlatformUserProvisioningService)

### Within Each User Story

- Tests MUST be written and FAIL before implementation
- Models/DTOs → Services → Endpoints → YARP routes
- Core service logic before endpoint wiring

### Parallel Opportunities

- **Phase 1**: T001-T004 all [P] — different files
- **Phase 2**: T007 + T008 [P] — different test projects
- **Phase 3**: T014 + T015 [P] — different test approaches
- **Cross-story**: US1 and US2 can run in parallel after Phase 1 (different services, no file conflicts)
- **Phase 5**: T024-T027 all [P] — different documentation files

---

## Parallel Example: After Setup Phase

```bash
# Launch US1 and US2 in parallel (different services):
Agent 1: US1 (Auto-Link) — touches Wallet.Service, Tenant.Service/ParticipantService
Agent 2: US2 (Admin Provisioning) — touches Tenant.Service/PlatformUserProvisioningService

# After US2 completes:
Agent 2: US3 (Password Reset) — extends PlatformUserProvisioningService
```

---

## Implementation Strategy

### MVP First (US1 Only)

1. Complete Phase 1: Setup (6 tasks)
2. Complete Phase 2: US1 — Auto-Link (7 tasks)
3. **STOP AND VALIDATE**: Create wallet, verify ActionsHub subscription works
4. This alone fixes the most visible user pain — wallet creation "just works"

### Incremental Delivery

1. Setup → Foundation ready
2. US1 → **MVP: Auto-link wallets** — wallet creation works end-to-end
3. US2 → Admin user provisioning — multi-org testing unblocked
4. US3 → Admin password reset — operational convenience
5. Polish → Docs, YARP routes, regression check

### Parallel Team Strategy

With multiple agents after Phase 1:
1. Agent A: US1 (auto-link) — Wallet + Tenant services
2. Agent B: US2 (provisioning) → US3 (password reset) — Tenant Service only
3. Both complete → Polish

---

## Notes

- [P] tasks = different files, no dependencies on incomplete tasks
- [Story] label maps task to specific user story
- Auto-link is fire-and-forget — wallet creation must not be blocked by Tenant Service failures (FR-004)
- Platform-wide wallet uniqueness must be respected even in auto-link (FR-003)
- Password policy reuses existing NIST infrastructure from RegistrationService
- All new public types MUST have XML doc comments (Constitution III)
- License header required on all new files
- Total tasks: **29**
- US1: 7 tasks | US2: 6 tasks | US3: 4 tasks
- Setup: 6 tasks | Polish: 6 tasks
