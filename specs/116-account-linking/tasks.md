---
description: "Task list for Feature 116 — Account Linking & Auth-Method Management"
---

# Tasks: Account Linking & Auth-Method Management

**Input**: Design documents from `specs/116-account-linking/`
**Prerequisites**: `plan.md`, `spec.md`, `research.md`, `data-model.md`, `contracts/*.yaml`, `quickstart.md`
**Authoritative architecture**: `docs/superpowers/specs/2026-04-27-account-linking-design.md` (committed `ded4218c`)

**Tests**: REQUIRED. Constitution principle IV mandates >85% coverage on new code; design §8 specifies unit + integration + Playwright E2E layers. Test tasks appear within each story's phase.

**Organization**: Grouped by user story for independent implementation and testing. **Implementation order note**: spec priorities reflect business value (US1 highest), but US4 (aggregate view) is a *technical prerequisite* for US1/US2/US3 and is therefore implemented first after Foundational. The dependency graph at the bottom captures this.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: `[US1]` link/unlink social, `[US2]` passkey lifecycle, `[US3]` password lifecycle, `[US4]` aggregate read
- All paths absolute / repo-rooted

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Pre-existing project — minimal setup. Just create empty folders for the new file groups so subsequent `Write` calls succeed.

- [ ] T001 Create directory `src/Services/Sorcha.Tenant.Service/Models/Requests/` for new request DTOs
- [ ] T002 [P] Create directory `src/Services/Sorcha.Tenant.Service/Filters/` for `RequireAuthChallengeAttribute`
- [ ] T003 [P] Create directory `src/Services/Sorcha.Tenant.Service/Telemetry/` for the new OpenTelemetry meter wrapper
- [ ] T004 [P] Create directory `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Components/Settings/AuthMethods/` for the four new Razor components

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Backend primitive — re-authentication challenge token + filter + cleanup + telemetry — and UI scaffolding (`AuthChallengeDialog`, tab restructure). All user stories depend on these.

**⚠️ CRITICAL**: No user-story phase can begin until this phase is complete.

### Backend — entities & DB

- [ ] T005 [P] Create `AuthChallengeToken` entity at `src/Services/Sorcha.Tenant.Service/Models/AuthChallengeToken.cs` per data-model.md §"New entity"
- [ ] T006 [P] Create `ChallengeMethod` + `ScopedOperation` enums at `src/Services/Sorcha.Tenant.Service/Models/AuthChallengeEnums.cs` per data-model.md §"Enums"
- [ ] T007 Update `src/Services/Sorcha.Tenant.Service/Data/TenantDbContext.cs` — add `DbSet<AuthChallengeToken> AuthChallengeTokens` + `OnModelCreating` configuration with the four indexes from data-model.md §"Indexes" (depends on T005, T006)
- [ ] T008 Squash migration: delete `src/Services/Sorcha.Tenant.Service/Migrations/20260425152258_InitialCreate.cs` + `.Designer.cs`, then run `dotnet ef migrations add InitialCreate` per quickstart.md §1; verify the regenerated file references `auth_challenge_tokens` (depends on T007)

### Backend — repositories & services

- [ ] T009 [P] Create `IAuthChallengeRepository` + `AuthChallengeRepository` at `src/Services/Sorcha.Tenant.Service/Data/Repositories/AuthChallengeRepository.cs` (`InsertAsync`, `FindByHashAsync`, atomic `TryConsumeAsync` using `UPDATE … WHERE consumed_at IS NULL` returning rows-affected, `PruneExpiredOlderThanAsync`)
- [ ] T010 [P] Create `IAuthMethodService` interface + `AuthMethodService` implementation at `src/Services/Sorcha.Tenant.Service/Services/AuthMethodService.cs` — implements `WouldRemovingLeaveZero(platformUserId, methodKind, methodId)` per data-model.md §"CanRemove computation"; uses `SELECT … FOR UPDATE` on `PlatformUser` when called inside a mutation transaction
- [ ] T011 Create `IAuthChallengeService` + `AuthChallengeService` at `src/Services/Sorcha.Tenant.Service/Services/AuthChallengeService.cs` — `IssueAsync(platformUserId, scopedOperation, preferredMethod?)` runs the ladder per research.md R-003 (TOTP → password → passkey → re-OAuth) and returns `(challengeId, method, payload?)`; `VerifyAsync(challengeId, proof)` validates proof per method, persists `AuthChallengeToken` with `SHA-256(token)`, returns raw token + 300s TTL (depends on T009)
- [ ] T012 Create `RequireAuthChallengeAttribute` endpoint filter at `src/Services/Sorcha.Tenant.Service/Filters/RequireAuthChallengeAttribute.cs` — reads `X-Auth-Challenge` header, performs the 5-step verification from design §6.4, atomic-consumes via `IAuthChallengeRepository.TryConsumeAsync`, rejects with appropriate 401 codes (depends on T009)
- [ ] T013 Create `AuthChallengeTokenCleanupService` BackgroundService at `src/Services/Sorcha.Tenant.Service/Services/AuthChallengeTokenCleanupService.cs` — daily tick (24h interval), prunes via `IAuthChallengeRepository.PruneExpiredOlderThanAsync(TimeSpan.FromDays(7))`, structured log per tick (depends on T009)

### Backend — telemetry

- [ ] T014 [P] Create OpenTelemetry meter at `src/Services/Sorcha.Tenant.Service/Telemetry/AuthMetrics.cs` exposing the six counters from data-model.md §"Telemetry surface" on the `Sorcha.Tenant.Auth` meter

### Backend — challenge endpoints

- [ ] T015 Create `AuthChallengeEndpoints` at `src/Services/Sorcha.Tenant.Service/Endpoints/AuthChallengeEndpoints.cs` — `POST /api/auth/challenge/initiate` + `/verify` per `contracts/auth-challenge.openapi.yaml`; `.WithName/Summary/Description`, `.Produces<T>()`, `[Authorize]`, `RateLimitPolicies.PlatformAuth` (depends on T011, T014)
- [ ] T016 Wire DI in `src/Services/Sorcha.Tenant.Service/Extensions/ServiceCollectionExtensions.cs` — new `AddTenantAccountManagement(this IServiceCollection)` registers repository, services, filter, BackgroundService, meter; called from `Program.cs` (depends on T009-T015)
- [ ] T017 Map endpoints in `src/Services/Sorcha.Tenant.Service/Program.cs` — call `app.MapAuthChallengeEndpoints()` (depends on T015)

### Backend — foundational tests

- [ ] T018 [P] Unit tests `tests/Sorcha.Tenant.Service.Tests/Services/AuthChallengeServiceTests.cs` — initiate, verify, ladder selection across enrolment combinations, expired/consumed/wrong-operation rejection, atomic-consume race (concurrent `VerifyAsync` calls — exactly one winner) (depends on T011)
- [ ] T019 [P] Unit tests `tests/Sorcha.Tenant.Service.Tests/Services/AuthMethodServiceTests.cs` — `WouldRemovingLeaveZero` across the seven `{password?, socials, passkeys}` combinations; Disabled passkey not counting (depends on T010)
- [ ] T020 [P] Unit tests `tests/Sorcha.Tenant.Service.Tests/Filters/RequireAuthChallengeAttributeTests.cs` — header missing → 401, hash miss → 401, wrong owner → 401, wrong scope → 401, expired → 401, consumed → 401, success path passes through (depends on T012)
- [ ] T021 [P] Integration tests `tests/Sorcha.Tenant.Service.Tests/Endpoints/AuthChallengeEndpointTests.cs` — full initiate → verify → mutation → re-use 401 cycle via `WebApplicationFactory` with Redis mocked per project convention (depends on T015)

### UI — shared scaffolding

- [ ] T022 [P] Create typed client `IAuthMethodsService` + `AuthMethodsService` at `src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/AuthMethodsService.cs` — methods for `GetAuthMethodsAsync`, `InitiateChallengeAsync`, `VerifyChallengeAsync`, plus stubs for the per-story mutations to be filled in their phases
- [ ] T023 [P] Create `AuthChallengeDialog.razor` at `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Components/Settings/AuthMethods/AuthChallengeDialog.razor` — MudDialog that calls `/challenge/initiate`, renders TOTP-input / password-input / WebAuthn-prompt / re-OAuth-launch by method, shows "Use a different method" link when multiple enrolled, returns the challenge token to caller via `MudDialogInstance.Close(...)`
- [ ] T024 Update `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/Settings.razor` — add the new "Accounts" `MudTabPanel` as the first tab with `Icons.Material.Filled.ManageAccounts`; rename existing "Connections" tab text to "Service Profiles" and swap icon `Dns` → `Cable`; tab body unchanged
- [ ] T025 [P] Add localisation keys for `settings.accounts.*` and `settings.serviceProfiles.*` (replacing `settings.connections.*`) in the relevant `.resx` files under `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Resources/`

**Checkpoint**: Foundation ready — challenge primitive, filter, cleanup, telemetry, tab structure, dialog scaffold all in place. User-story phases can now begin.

---

## Phase 3: User Story 4 — View all sign-in methods in one place (Priority: P4) 🎯 visual MVP

**Goal**: User opens Settings → Accounts and sees password presence, every linked social provider (with email + last-used), and every Active/Disabled passkey (with display name + device type + last-used) in a single round-trip.

**Why this phase first despite P4 label**: US1, US2, US3 all need the aggregate read endpoint and the four section components to exist before they can wire their interactivity. Implementing US4 first is the cheapest path to unblocking them all.

**Independent Test**: A user with mixed methods (password + Google + 2 passkeys) opens the Accounts tab and sees all four rows with accurate metadata; the page makes one HTTP call.

### Backend — aggregate read

- [ ] T026 [P] [US4] Create `AuthMethodsResponse` + `AuthMethodsPassword` + `AuthMethodsSocial` + `AuthMethodsPasskey` records at `src/Services/Sorcha.Tenant.Service/Models/Requests/AuthMethodsResponse.cs` matching `contracts/auth-methods.openapi.yaml` §components/schemas
- [ ] T027 [US4] Implement `IAuthMethodService.GetAggregateAsync(platformUserId, ct)` in `AuthMethodService.cs` — single LINQ query joining `PlatformUser` ⨝ `PlatformSocialLogin` ⨝ `PasskeyCredential` filtered to `Status != Revoked`; populate `CanRemove` per row using the floor helper; apply "Unnamed passkey" fallback for empty `DisplayName` rows (depends on T010, T026)
- [ ] T028 [US4] Create `AuthMethodsEndpoints` at `src/Services/Sorcha.Tenant.Service/Endpoints/AuthMethodsEndpoints.cs` — `GET /api/me/auth-methods` per `contracts/auth-methods.openapi.yaml`; `[Authorize]`, `.WithName/Summary/Description`, `.Produces<AuthMethodsResponse>()` (depends on T027)
- [ ] T029 [US4] Map endpoints in `Program.cs` — `app.MapAuthMethodsEndpoints()` (depends on T028)

### UI — read-only shell

- [ ] T030 [US4] Create `AccountsTab.razor` at `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Components/Settings/AccountsTab.razor` — top-level shell loading `/api/me/auth-methods` on init; renders an Account-email card and the four section components below (depends on T022)
- [ ] T031 [P] [US4] Create read-only `PasswordSection.razor` at `Components/Settings/AuthMethods/PasswordSection.razor` — displays `Set` / `Not set` + last-changed; no actions wired yet (filled by US3)
- [ ] T032 [P] [US4] Create read-only `SocialLinksSection.razor` at `Components/Settings/AuthMethods/SocialLinksSection.razor` — displays linked-providers list + empty-state; pills rendered visually but inert (filled by US1)
- [ ] T033 [P] [US4] Create read-only `PasskeysSection.razor` at `Components/Settings/AuthMethods/PasskeysSection.razor` — displays passkey rows including Disabled-warning state with cloned-authenticator tooltip; no actions wired (filled by US2)
- [ ] T034 [US4] Mount `<AccountsTab />` in the new `MudTabPanel` from T024 (depends on T030)

### Tests

- [ ] T035 [P] [US4] Integration test `tests/Sorcha.Tenant.Service.Tests/Endpoints/AuthMethodsEndpointTests.cs` — aggregation correctness across the seven `{password?, socials, passkeys}` shapes; `CanRemove` accuracy; Revoked passkeys excluded; Disabled passkeys included with warning fields populated (depends on T028)
- [ ] T036 [P] [US4] Playwright E2E `tests/Sorcha.UI.E2E.Tests/Docker/AccountsTabTests.cs::AccountsTab_ShowsAllMethods` — seed a user with password + Google + 2 passkeys, open Settings → Accounts, assert all rows render with accurate metadata (depends on T034)

**Checkpoint**: User can view all methods in one place. US1, US2, US3 can now begin in parallel.

---

## Phase 4: User Story 1 — Link and unlink social sign-in providers (Priority: P1)

**Goal**: User can link Google/GitHub/Microsoft/Apple to their existing account and unlink with re-auth.

**Independent Test**: Email/password user links Google → signs out → signs in via Google → lands on same account. Unlink Google requires challenge and succeeds. Linking a Google account whose email belongs to another user is rejected with 409.

### Backend — link flow

- [ ] T037 [US1] Modify `SocialLoginInitiateRequest` at `src/Services/Sorcha.Tenant.Service/Models/Requests/SocialLoginInitiateRequest.cs` — add `Intent` ∈ `{ "login", "link" }` field (default `"login"` for backward compatibility)
- [ ] T038 [US1] Modify `src/Services/Sorcha.Tenant.Service/Services/StateTokenService.cs` (or equivalent) — when signing OAuth `state`, include `intent` and (for `link`) `targetPlatformUserId`; HMAC-SHA256 signature over the full payload using `SocialLogin:StateSigningKey` from configuration
- [ ] T039 [US1] Modify `SocialLoginEndpoints.InitiateSocialLogin` in `src/Services/Sorcha.Tenant.Service/Endpoints/SocialLoginEndpoints.cs` — when `intent=link`, require `[Authorize]`; call state signer with the signed-in user's id; otherwise existing anonymous login path
- [ ] T040 [US1] Modify `SocialLoginEndpoints.CompleteSocialLogin` — after state signature verification, branch on decoded `intent`: `login` runs existing path; `link` requires bearer matching `state.targetPlatformUserId`, calls `ISocialLinkService.LinkAsync`
- [ ] T041 [P] [US1] Create `ISocialLinkService` + `SocialLinkService` at `src/Services/Sorcha.Tenant.Service/Services/SocialLinkService.cs` — `LinkAsync(platformUserId, provider, providerSubject, providerEmail, displayName)` runs collision check (`(Provider, Subject)` unique + email vs. other `PlatformUser`), inserts `PlatformSocialLogin` row; returns 409 on collision

### Backend — unlink + cleanup

- [ ] T042 [US1] Add `DELETE /api/auth/social/{linkId}` to `SocialLoginEndpoints` — `[RequireAuthChallenge(ScopedOperation.RemoveAuthMethod)]`, calls `ISocialLinkService.UnlinkAsync` which checks `WouldRemovingLeaveZero` inside a `SELECT … FOR UPDATE` transaction, hard-deletes the row on success (depends on T010, T012, T041)
- [ ] T043 [US1] Remove orphaned `POST /api/auth/social/link` endpoint from `SocialLoginEndpoints` (UI calls `social/initiate` directly with `intent=link` per design §4.2)

### Backend — tests

- [ ] T044 [P] [US1] Unit tests `tests/Sorcha.Tenant.Service.Tests/Services/SocialLinkServiceTests.cs` — link success, link with no email (Apple/private GitHub), email collision against `PlatformSocialLogin.Subject`, email collision against `PlatformUser.Email`, state HMAC verify pass + tamper rejection (depends on T041)
- [ ] T045 [P] [US1] Integration tests `tests/Sorcha.Tenant.Service.Tests/Endpoints/SocialLinkEndpointTests.cs` — `intent` dispatch (login vs link), 409 on collision, 400 on tampered state (modify each field), unlink requires valid challenge → 401 without, 409 on floor violation, 204 on success, removed `social/link` endpoint returns 404 (depends on T040, T042)

### UI

- [ ] T046 [US1] Wire `SocialLinksSection.razor` actions — Add pills call `IAuthMethodsService.InitiateSocialLinkAsync(provider)` which redirects to the OAuth authorisation URL; "already linked" providers render struck-through and disabled; Unlink action opens `AuthChallengeDialog` then calls `IAuthMethodsService.UnlinkSocialAsync(linkId, challengeToken)`; on success refresh aggregate (depends on T032, T023, T022)
- [ ] T047 [US1] Add corresponding methods to `IAuthMethodsService` / `AuthMethodsService` typed client — `InitiateSocialLinkAsync`, `UnlinkSocialAsync` (depends on T022)
- [ ] T048 [P] [US1] Playwright E2E `AccountsTab_LinkGoogle_FullFlow` — sign in as email/password user, link Google via mocked OAuth, assert row appears, sign out, sign in via Google, assert same account (depends on T046)
- [ ] T049 [P] [US1] Playwright E2E `AccountsTab_UnlinkGoogle_RequiresChallenge` — Google linked, click Unlink, dialog opens, submit TOTP, row disappears (depends on T046)
- [ ] T050 [P] [US1] Playwright E2E `AccountsTab_LinkSocial_RejectsEmailCollision` — seed two users with the same Google email, attempt link as second user, assert 409 message and unaffected session (depends on T046)

**Checkpoint**: P1 done — most-requested business value shipped.

---

## Phase 5: User Story 2 — Manage passkeys from settings (Priority: P2)

**Goal**: User can add, rename, and remove passkeys from the Accounts tab. Removed passkeys are soft-deleted for forensic audit.

**Independent Test**: User adds a passkey with chosen display name → row appears with "Last used: never". Rename inline. Remove with challenge. DB row remains as `Status=Revoked`.

### Backend

- [ ] T051 [US2] Modify `PasskeyEndpoints.RegisterVerify` in `src/Services/Sorcha.Tenant.Service/Endpoints/PasskeyEndpoints.cs` — require non-empty `DisplayName` in the request DTO (FluentValidation + 400 on empty)
- [ ] T052 [US2] Add `PUT /api/passkeys/credentials/{id}` to `PasskeyEndpoints` per `contracts/passkey-management.openapi.yaml` — updates `DisplayName`; rejects with 409 when target passkey has `Status=Disabled`; no challenge required
- [ ] T053 [US2] Modify `PasskeyEndpoints.DeleteCredential` — soft-revoke per data-model.md state-transition table: set `Status=Revoked`, `DisabledAt=now`, `DisabledReason="user-removed"` (or `"user-removed-after-disable"` when starting from Disabled); `[RequireAuthChallenge(RemoveAuthMethod)]` for Active passkeys, bypass for Disabled; floor check via `IAuthMethodService.WouldRemovingLeaveZero` inside same transaction
- [ ] T054 [US2] Modify `PasskeyEndpoints.ListCredentials` — exclude `Status=Revoked` by default; the `/api/me/auth-methods` aggregate already does this independently

### Tests

- [ ] T055 [P] [US2] Unit tests `tests/Sorcha.Tenant.Service.Tests/Services/PasskeyRevocationTests.cs` — Active → Revoked sets all three columns; Disabled → Revoked uses `"user-removed-after-disable"`; Revoked is terminal; floor blocks last-passkey removal
- [ ] T056 [P] [US2] Update integration tests in `tests/Sorcha.Tenant.Service.Tests/Endpoints/PasskeyEndpointTests.cs` — register requires non-empty DisplayName (400), PUT renames an Active passkey (204), PUT against Disabled rejects (409), DELETE Active requires challenge (401 without, 204 with), DELETE Disabled bypasses challenge (204), DELETE last-method blocked (409), Revoked rows excluded from list

### UI

- [ ] T057 [US2] Wire `PasskeysSection.razor` actions — Add button kicks off WebAuthn ceremony with name prompt; Rename uses inline `MudTextField` and `IAuthMethodsService.RenamePasskeyAsync`; Remove opens `AuthChallengeDialog` (skipping for Disabled passkeys per data-model.md) then calls `IAuthMethodsService.RemovePasskeyAsync(id, challengeToken?)`; on success refresh aggregate
- [ ] T058 [US2] Add `AddPasskeyAsync`, `RenamePasskeyAsync`, `RemovePasskeyAsync` methods to `IAuthMethodsService` / `AuthMethodsService` typed client
- [ ] T059 [P] [US2] Playwright E2E `AccountsTab_AddPasskey_FullFlow` — register passkey with name "Dev YubiKey", assert it appears with "Last used: never"
- [ ] T060 [P] [US2] Playwright E2E `AccountsTab_RenameNotChallengeGated` — rename inline, no dialog appears, name updates
- [ ] T061 [P] [US2] Playwright E2E `AccountsTab_RemoveButton_DisabledWhenLastMethod` — passkey-only user; assert Remove disabled with the orange last-method tooltip
- [ ] T062 [P] [US2] Playwright E2E `AccountsTab_RemovePasskey_RequiresChallenge` — multi-method user; click Remove, dialog opens, submit proof, row disappears

**Checkpoint**: P2 done.

---

## Phase 6: User Story 3 — Set, change, or remove password from settings (Priority: P3)

**Goal**: User can set a first password (with bootstrap bypass), change an existing password, or remove their password from inside Settings. The existing 2FA-disable adopts the same challenge primitive.

**Independent Test**: Passkey-only user sets a password and signs back in with email+password. Existing-password user rotates via Security tab. Remove-password works when other methods exist; blocked when last method.

### Backend — password endpoints

- [ ] T063 [P] [US3] Create `IPasswordManagementService` + `PasswordManagementService` at `src/Services/Sorcha.Tenant.Service/Services/PasswordManagementService.cs` — `SetAsync(platformUserId, password)`, `ChangeAsync`, `RemoveAsync`; bootstrap detection (`PasswordHash IS NULL AND no socials AND no active passkeys`); BCrypt hashing via existing `IPasswordHasher`; floor enforcement on remove (depends on T010)
- [ ] T064 [US3] Create `PasswordRequest` DTO + `PasswordRequestValidator` (FluentValidation: min length per existing platform policy) at `src/Services/Sorcha.Tenant.Service/Models/Requests/PasswordRequest.cs`
- [ ] T065 [US3] Add `POST /api/auth/password/set` to `src/Services/Sorcha.Tenant.Service/Endpoints/AuthEndpoints.cs` per `contracts/auth-password.openapi.yaml` — challenge conditional on bootstrap (skip filter when `methods.Count == 0`); 409 if password already set (depends on T063, T064)
- [ ] T066 [US3] Add `POST /api/auth/password/change` to `AuthEndpoints` — `[RequireAuthChallenge(ScopedOperation.ChangePassword)]`; 409 if no current password (depends on T063, T064)
- [ ] T067 [US3] Add `POST /api/auth/password/remove` to `AuthEndpoints` — `[RequireAuthChallenge(ScopedOperation.RemovePassword)]`; floor enforcement via `IAuthMethodService.WouldRemovingLeaveZero` inside same transaction (depends on T063)

### Backend — 2FA disable adoption (FR-024)

- [ ] T068 [US3] Modify the existing 2FA-disable endpoint in `src/Services/Sorcha.Tenant.Service/Endpoints/TotpEndpoints.cs` — add `[RequireAuthChallenge(ScopedOperation.Disable2Fa)]` to close the hijacked-session unguarded-prune gap (depends on T012)

### Tests

- [ ] T069 [P] [US3] Unit tests `tests/Sorcha.Tenant.Service.Tests/Services/PasswordManagementServiceTests.cs` — set with challenge, set in bootstrap mode (no challenge), change with challenge, remove with challenge + floor protection, BCrypt hash applied (depends on T063)
- [ ] T070 [P] [US3] Integration tests `tests/Sorcha.Tenant.Service.Tests/Endpoints/PasswordEndpointTests.cs` — all four endpoints (set / change / remove) with conditional challenge enforcement; bootstrap path; 409 on already-set / no-password / floor; verify password actually rotated (depends on T065-T067)
- [ ] T071 [P] [US3] Integration test update in `tests/Sorcha.Tenant.Service.Tests/Endpoints/TotpEndpointTests.cs` — disable 2FA without challenge token returns 401; with valid `Disable2Fa`-scoped token returns 204 (depends on T068)

### UI

- [ ] T072 [US3] Wire `PasswordSection.razor` actions — Set / Change / Remove buttons each open `AuthChallengeDialog` (Set bypasses dialog when bootstrap), then call `IAuthMethodsService.SetPasswordAsync` / `ChangePasswordAsync` / `RemovePasswordAsync`; on success refresh aggregate (depends on T031, T023)
- [ ] T073 [US3] Add `SetPasswordAsync`, `ChangePasswordAsync`, `RemovePasswordAsync` methods to `IAuthMethodsService` / `AuthMethodsService` typed client
- [ ] T074 [US3] Mount `<PasswordSection />` (read+actions) above the existing 2FA section inside `Pages/Settings.razor`'s Security `MudTabPanel`
- [ ] T075 [P] [US3] Playwright E2E `Security_ChangePassword_RequiresChallenge` — open Security tab, click Change, dialog opens, submit current password, provide new, save, sign out + back in with new password
- [ ] T076 [P] [US3] Playwright E2E `AccountsTab_SetPassword_PasskeyOnly` — passkey-only user opens Accounts, clicks Set, dialog asks for WebAuthn step-up, provides new password, save, sign out + back in with email+password
- [ ] T077 [P] [US3] Playwright E2E `AccountsTab_RemovePassword_RequiresChallenge` — multi-method user removes password, dialog appears, submit, row updates to "Not set"

**Checkpoint**: P3 done. All four user stories independently functional.

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Constitutional compliance + documentation + final validation.

- [ ] T078 [P] Verify all new Minimal API endpoints have `.WithName(...)`, `.WithSummary(...)`, `.WithDescription(...)`, and typed `.Produces<T>()` (constitution principle III)
- [ ] T079 [P] Verify XML doc comments on all new public service methods + DTOs (constitution principle V; no Release-build warnings)
- [ ] T080 [P] Verify all logging uses structured fields, no string interpolation (constitution principle VIII): `_logger.LogInformation("Auth challenge consumed for {PlatformUserId} {Scope}", id, scope)`
- [ ] T081 [P] Verify `RateLimitPolicies.PlatformAuth` applied to `/api/auth/challenge/initiate` + `/verify` + `/api/auth/password/*` + `/api/auth/social/*` + `/api/passkeys/credentials/*`
- [ ] T082 [P] Verify FluentValidation validators registered for all new request DTOs (constitution principle II — input validation at boundaries)
- [ ] T083 Update `docs/reference/API-DOCUMENTATION.md` with the new endpoints from `contracts/*.yaml`
- [ ] T084 Add new "Account Linking & Auth-Method Management" section to `.claude/skills/sorcha-architecture/SKILL.md` summarising endpoints + entity + challenge primitive; add Feature 116 to the skill-pointer line in `CLAUDE.md` "Feature API References"
- [ ] T085 Update `src/Services/Sorcha.Tenant.Service/README.md` with the new endpoint surface and the challenge-primitive design overview
- [ ] T086 Run the full quickstart.md validation walkthrough end-to-end on a fresh Docker environment; capture telemetry sanity check
- [ ] T087 Verify >85% line coverage on new code (`dotnet test --collect:"XPlat Code Coverage"`); address any uncovered branches
- [ ] T088 [P] Mark Feature 116 status in `.specify/MASTER-TASKS.md` (find it under the deferred-feature-gaps theme or add as a new completed entry)
- [ ] T089 Self-merge PR on green CI per Sorcha branch policy: create PR via `gh pr create`, await review, merge with `gh pr merge --squash`

---

## Dependencies & Execution Order

### Phase dependencies

- **Setup (Phase 1)**: trivial directory creation — no real blockers.
- **Foundational (Phase 2)**: depends on Setup. **Blocks all user stories** — challenge primitive, filter, dialog, tab restructure must exist first.
- **US4 (Phase 3)**: depends on Foundational. **Implementation-time prerequisite for US1/US2/US3** — they need the aggregate endpoint and the four section components to exist (even if those components are read-only stubs that the later phases fill in).
- **US1 (Phase 4)**, **US2 (Phase 5)**, **US3 (Phase 6)**: each depends on Foundational + US4. **Independent of each other** — can be parallelised across developers.
- **Polish (Phase 7)**: depends on whichever user stories shipped.

### Within-phase ordering

- T002–T004 are file-system mkdir; trivially parallel.
- T005–T006 entity + enums in parallel; T007 (DbContext) needs them; T008 (squash) needs T007.
- T009 + T010 (repository + floor service) parallel; T011 (challenge service) needs T009.
- T012 (filter) needs T009; T013 (cleanup BG) needs T009; T014 (telemetry) standalone.
- T015 (challenge endpoints) needs T011 + T014; T016 (DI wiring) needs T009-T015; T017 (Program.cs) needs T015.
- Backend tests T018-T021 each need their respective services; T022/T023 UI scaffolding needs no backend (they call typed-client stubs); T024 (Settings.razor mod) standalone.

### User-story cross-dependencies

- US4 → consumed by US1/US2/US3 via the four section components and the typed client.
- US1, US2, US3 → independent of each other. Different files, different endpoints, different sections.
- Polish phase depends on whichever stories shipped.

### Parallel opportunities

- All [P] tasks within a phase can run together when their dependencies are satisfied.
- After Foundational checkpoint: US4 → then US1+US2+US3 in parallel by 3 developers.
- All Playwright E2E tests within a story are [P] (different test methods, different scenarios).
- All cross-cutting Polish tasks T078-T082, T088 are [P] (different concerns, different files).

---

## Parallel Example: User Story 1 (P1)

Once Foundational + US4 are complete, US1's tasks fan out as follows:

```bash
# Parallel block A — services and endpoints (different files):
Task T037: "Modify SocialLoginInitiateRequest at .../Models/Requests/SocialLoginInitiateRequest.cs"
Task T041: "Create ISocialLinkService + SocialLinkService at .../Services/SocialLinkService.cs"

# After A completes, sequential block B (state signer + endpoints share files):
Task T038: "Modify StateTokenService"
Task T039: "Modify InitiateSocialLogin endpoint"
Task T040: "Modify CompleteSocialLogin endpoint"
Task T042: "Add DELETE /api/auth/social/{linkId} endpoint"
Task T043: "Remove orphaned POST /api/auth/social/link"

# Parallel test block (different test files):
Task T044: "Unit tests SocialLinkServiceTests"
Task T045: "Integration tests SocialLinkEndpointTests"

# UI block (single Razor component):
Task T046: "Wire SocialLinksSection actions"
Task T047: "Add typed client methods"

# Parallel E2E block (different scenarios):
Task T048: "Playwright AccountsTab_LinkGoogle_FullFlow"
Task T049: "Playwright AccountsTab_UnlinkGoogle_RequiresChallenge"
Task T050: "Playwright AccountsTab_LinkSocial_RejectsEmailCollision"
```

---

## Implementation Strategy

### MVP First — read-only Accounts tab (US4)

1. Complete Phase 1: Setup (folders).
2. Complete Phase 2: Foundational — challenge primitive, filter, cleanup, dialog scaffold, tab restructure.
3. Complete Phase 3: US4 — aggregate endpoint + read-only sections.
4. **STOP and VALIDATE**: User can open Accounts tab and see all their methods. Even with no add/remove yet, this is shippable transparency.

### Business-Value MVP — link/unlink social (US1)

5. Complete Phase 4: US1 — link/unlink social provider.
6. **STOP and VALIDATE**: SC-001 met (link a second method in <5 min); SC-003 met (collision rejected).

### Full feature — all stories

7. Complete Phase 5: US2 — passkey lifecycle.
8. Complete Phase 6: US3 — password lifecycle + 2FA disable adoption.
9. Complete Phase 7: Polish — constitutional compliance, docs, coverage check, PR.

### Parallel team strategy (3 developers)

After Foundational + US4 (single-developer or pair):

- **Developer A**: US1 (social link/unlink) — `SocialLinkService`, `SocialLinkEndpoints`, `SocialLinksSection.razor`, 3× E2E.
- **Developer B**: US2 (passkey lifecycle) — `PasskeyEndpoints` mods, `PasskeyRevocationTests`, `PasskeysSection.razor`, 4× E2E.
- **Developer C**: US3 (password lifecycle) — `PasswordManagementService`, password endpoints, 2FA disable adoption, `PasswordSection.razor`, 3× E2E.

Polish phase merges back into one developer / pair.

---

## Notes

- **89 total tasks** spanning Setup (4) + Foundational (21) + US4 (11) + US1 (14) + US2 (12) + US3 (15) + Polish (12).
- **Story labels**: `[US1]` 14 tasks, `[US2]` 12 tasks, `[US3]` 15 tasks, `[US4]` 11 tasks. Foundational + Setup + Polish carry no story label per template rule.
- **Tests included** per project constitution principle IV; coverage gate (>85%) enforced in T087.
- **Pre-release squash** in T008 — single migration cut, no upgrade path.
- **Challenge primitive shared** across stories — built once in Foundational, consumed by US1 (unlink), US2 (remove passkey), US3 (set/change/remove + 2FA disable).
- **OAuth callback dispatch** uses signed `state.intent` per locked decision Q6/R-007 — single callback URL, no per-environment redirect-URI doubling.
- **Last-method floor** enforced at two layers: UI disable from `canRemove` flag (T030–T034 read; T046/T057/T072 wire) and server transaction-scoped re-check (T010, T042, T053, T067) per FR-029.
