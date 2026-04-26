---
description: "Implementation tasks for feature 115 social-signup"
---

# Tasks: Public Social Signup on n1

**Input**: Design documents from `/specs/115-social-signup/`
**Prerequisites**: plan.md ✓, spec.md ✓, research.md ✓, data-model.md ✓, contracts/ ✓, quickstart.md ✓

**Tests**: Test tasks INCLUDED. Spec FR-018 + plan §"Testing" + design doc both require xUnit coverage of the policy gates.

**Organization**: Tasks are grouped by user story (US1, US2, US3) so each story can be implemented and verified independently.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: Which user story this task belongs to (US1, US2, US3) — only on Phase 3+ tasks
- All paths are absolute under `C:\Projects\Sorcha\`

## Path Conventions

This feature modifies the existing `Sorcha.Tenant.Service` and its test
project. No new projects. Paths follow the established service layout:

- Service code: `src/Services/Sorcha.Tenant.Service/...`
- Tests: `tests/Sorcha.Tenant.Service.Tests/...`
- Config: `docker-compose.n1.yml`, `.env.example` (repo root)
- Docs: `docs/guides/...`

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Add the configuration surface that the rest of the work
binds to. These tasks touch only configuration files and documentation
templates — no production code changes.

- [ ] T001 Add `GOOGLE_OAUTH_CLIENT_ID`, `GOOGLE_OAUTH_CLIENT_SECRET`, `GITHUB_OAUTH_CLIENT_ID`, `GITHUB_OAUTH_CLIENT_SECRET` placeholder lines (with empty values) to `.env.example` in a `# Social login OAuth credentials (n1 only)` block
- [ ] T002 Add `SocialProviders__0__*` and `SocialProviders__1__*` env-var entries to the `tenant-service.environment` section of `docker-compose.n1.yml`, mapping to the four `${...}` placeholders from T001; also add `PlatformSettings__SeedPublicOrgEnabled: "true"` to the same `environment` block
- [ ] T003 [P] Create stub at `docs/guides/SOCIAL-LOGIN-SETUP.md` with section headings only (Google · GitHub · Verifying the deploy · Rotating credentials); content lands in T033

**Checkpoint**: Configuration surface exists. Operators can copy `.env.example` to `/opt/sorcha/.env` and start filling values once OAuth apps are registered.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Substrate that every user story depends on — the trust-claim capture, the new return shape, the redirect-URI bug fix, and the provider-list visibility surface. Without these, no user story can be implemented end-to-end.

**⚠️ CRITICAL**: No US1 / US2 / US3 work can begin until this phase is complete.

### Trust-claim substrate (FR-010 dependency)

- [ ] T004 [P] Add `bool EmailVerified` field to `SocialAuthCallbackResult` record in `src/Services/Sorcha.Tenant.Service/Models/Dtos/SocialLoginDtos.cs`
- [ ] T005 [P] Create `SocialLoginRefusal` enum (`None`, `ProviderUnverified`, `ExistingUnverified`) and `ResolveSocialUserResult` record (`PlatformUser? User`, `bool IsNew`, `SocialLoginRefusal Refusal`) in `src/Services/Sorcha.Tenant.Service/Models/Dtos/SocialLoginDtos.cs`

### `SocialLoginService` claim plumbing (sequential — same file)

- [ ] T006 Update `ParseIdTokenClaims` in `src/Services/Sorcha.Tenant.Service/Services/SocialLoginService.cs` to extract `email_verified` boolean (default `false` when absent) and propagate it via the private `IdTokenClaims` record
- [ ] T007 Update `FetchUserInfoClaimsAsync` in same file to read `email_verified` from userinfo response (default `false` when absent)
- [ ] T008 Update `ExtractGitHubClaimsAsync` in same file to set `EmailVerified = true` only when the primary `/user/emails` entry has `verified: true`
- [ ] T009 Update `ExtractOidcClaimsAsync` and the GitHub path to construct `SocialAuthCallbackResult` with the new `EmailVerified` field populated

### Provider-list visibility surface (FR-001 substrate; needed by US1, US3)

- [ ] T010 [P] Add `IReadOnlyList<string> GetConfiguredProviderNames()` to `ISocialLoginService` in `src/Services/Sorcha.Tenant.Service/Services/ISocialLoginService.cs`
- [ ] T011 Implement `GetConfiguredProviderNames` in `src/Services/Sorcha.Tenant.Service/Services/SocialLoginService.cs` returning provider names where both `ClientId` and `ClientSecret` are non-empty (filter `_providers` dictionary)

### Callback URL bug fix (FR-021)

- [ ] T012 [P] In `src/Services/Sorcha.Tenant.Service/Endpoints/SocialLoginEndpoints.cs`, change both `redirectUri` constructions (lines 99 and 262) from `$"{baseUrl}/api/auth/social/callback-redirect"` to `$"{baseUrl}/auth/social/callback"`
- [ ] T013 [P] In `src/Services/Sorcha.Tenant.Service/Pages/Auth/SocialCallback.cshtml.cs`, remove `provider` from the `OnGetAsync` parameter list and use `callbackResult.Provider` (already populated by the service from cached state) for downstream calls

### Resolve-flow signature change (US1 + US2 dependency)

- [ ] T014 In `src/Services/Sorcha.Tenant.Service/Services/IPlatformUserService.cs`, change `ResolveOrCreateSocialUserAsync` signature to return `Task<ResolveSocialUserResult>` and accept `SocialAuthCallbackResult` (instead of separate `provider`, `subject`, `email`, `displayName` parameters); preserves caller ergonomics by carrying `EmailVerified` in the same DTO

**Checkpoint**: Substrate ready. Bug is fixed. Provider visibility query and trust claim are available. User story phases can now proceed in parallel.

---

## Phase 3: User Story 1 — First-time signup via a social provider (Priority: P1) 🎯 MVP

**Goal**: A first-time visitor can click "Continue with Google" or "Continue with GitHub" on n1, complete consent, and land signed-in to a new public-org consumer account with a welcome email dispatched.

**Independent Test**: Configure one provider (e.g., Google), open the signup page in a private browser, click the provider button, complete the provider consent screen, confirm the visitor lands signed in with a new account, the welcome email arrives, and the account is recorded as a public-organisation consumer.

### Tests for User Story 1 (write FIRST, ensure they FAIL before implementation)

- [ ] T015 [P] [US1] In `tests/Sorcha.Tenant.Service.Tests/Models/SocialAuthCallbackResultTests.cs` (new file), add tests covering: `EmailVerified` populated from `email_verified=true` ID-token claim; defaults `false` when claim absent; defaults `false` when claim is non-Boolean
- [ ] T016 [P] [US1] In `tests/Sorcha.Tenant.Service.Tests/Endpoints/SocialLoginEndpointsTests.cs` (extend), add regression test asserting the `redirectUri` value sent to `GenerateAuthorizationUrlAsync` ends with `/auth/social/callback` (NOT `/api/auth/social/callback-redirect`)
- [ ] T017 [P] [US1] In `tests/Sorcha.Tenant.Service.Tests/Pages/SignupModelTests.cs` (extend), add tests covering: `Model.AvailableProviders` populated from `ISocialLoginService.GetConfiguredProviderNames`; empty list when no providers configured; preserves provider name casing
- [ ] T018 [P] [US1] In `tests/Sorcha.Tenant.Service.Tests/Data/DatabaseInitializerTests.cs` (extend or new), add tests covering: `PlatformSettings.PublicOrgEnabled` seeded `true` when `PlatformSettings:SeedPublicOrgEnabled=true` in config; defaults `false` when config key absent; existing row not overwritten on subsequent boots

### Implementation for User Story 1

- [ ] T019 [US1] In `src/Services/Sorcha.Tenant.Service/Services/PlatformUserService.cs`, rewrite `ResolveOrCreateSocialUserAsync` to handle the **new-user happy path**: when no provider link exists and no email collision, create `PlatformUser` with `EmailVerified=true, EmailVerifiedAt=now`, link `PlatformSocialLogin`, return `ResolveSocialUserResult(User, IsNew=true, Refusal=None)`. Preserve existing `LinkSocialLoginAsync` and `CreateAsync` calls.
- [ ] T020 [US1] In same file, handle the **returning-user path** (provider+subject already linked): update `LastUsedAt`, refresh `PlatformUser.DisplayName` from claim if non-empty and differs, return `ResolveSocialUserResult(User=existing, IsNew=false, Refusal=None)`. **Do not** re-check `EmailVerified` per FR-013.
- [ ] T021 [US1] In `src/Services/Sorcha.Tenant.Service/Pages/Auth/SocialCallback.cshtml.cs`, update call to `ResolveOrCreateSocialUserAsync` to pass the new `SocialAuthCallbackResult` directly and consume `ResolveSocialUserResult`. On success path (`Refusal=None`, `User != null`), keep existing welcome-dispatch + JWT-issue + redirect-to-`/app/#token=…` flow unchanged.
- [ ] T022 [US1] In `src/Services/Sorcha.Tenant.Service/Pages/Auth/SignupModel.cs` (`Signup.cshtml.cs`), inject `ISocialLoginService`, add `public IReadOnlyList<string> AvailableProviders { get; private set; } = []` property, populate in `OnGet` from `socialLoginService.GetConfiguredProviderNames()`
- [ ] T023 [US1] In `src/Services/Sorcha.Tenant.Service/Pages/Auth/Signup.cshtml`, replace the four hard-coded `<button class="social-btn" data-provider="…">` lines with a `@foreach (var provider in Model.AvailableProviders)` loop that emits one button per configured provider; preserve the surrounding `<div id="tab-social">` structure
- [ ] T024 [US1] In same file, **remove dead JS** from the social-login click handler: `var redirectUri = …`, `var nonce = …`, `var state = btoa(…)`, `sessionStorage.setItem('social_login_nonce', nonce)`. Keep the `fetch('/api/auth/social/initiate', …)` call. Add a comment line above the handler explaining the server constructs and validates the state — JS only needs the provider name.
- [ ] T025 [US1] In `src/Services/Sorcha.Tenant.Service/Data/DatabaseInitializer.cs`, change the hard-coded `PublicOrgEnabled = false` line (~314) to read `_configuration.GetValue<bool>("PlatformSettings:SeedPublicOrgEnabled", false)`. Update the existing `LogInformation` line that includes `PublicOrgEnabled=false` to log the configured value.
- [ ] T026 [US1] Run all new + extended tests from T015-T018 + T019-T025 paths and confirm green: `dotnet test tests/Sorcha.Tenant.Service.Tests/ --filter "FullyQualifiedName~SocialLogin|FullyQualifiedName~SignupModel|FullyQualifiedName~DatabaseInitializer"`

**Checkpoint**: At this point a fresh n1 (or local-dev with Google credentials) can support new-user social signup end-to-end. Login page still shows hardcoded buttons — that's US3's territory.

---

## Phase 4: User Story 2 — Account-takeover defence (Priority: P1)

**Goal**: The strict link policy is enforced. Social signup is refused when the provider asserts `email_verified=false`. Cross-method linking is refused when the existing Sorcha account is unverified. Refusals are visible in telemetry.

**Independent Test**: Create a password account with email `x@example.com` but do not verify it. From a second browser, attempt social signup with the same email at a provider that asserts the email as verified. Confirm the attempt is refused with the documented message and that no link or new identity is created. Then verify the password account's email and retry — confirm the link succeeds.

### Tests for User Story 2

- [ ] T027 [P] [US2] In `tests/Sorcha.Tenant.Service.Tests/Services/SocialLoginPolicyTests.cs` (new file), add tests covering all three scenarios from data-model.md §"State transitions": (a) returning user → no re-check, `LastUsedAt` updated, `DisplayName` refreshed; (b) email-collision + existing unverified → `Refusal.ExistingUnverified`, no link created; (c) email-collision + both verified → link, `LastUsedAt` updated, `DisplayName` refreshed; (d) no link, no collision, provider unverified → `Refusal.ProviderUnverified`, no user created; (e) no link, no collision, provider verified → new user with `EmailVerified=true`. Mock `_db` and `IIdentityRepository`. AAA pattern.
- [ ] T028 [P] [US2] In `tests/Sorcha.Tenant.Service.Tests/Pages/SocialCallbackModelTests.cs` (new file), add tests covering: refusal renders the page with `ErrorMessage` matching the documented copy for each `SocialLoginRefusal` value; success path redirects to `/app/#token=…&refresh=…`

### Implementation for User Story 2

- [ ] T029 [US2] In `src/Services/Sorcha.Tenant.Service/Services/PlatformUserService.cs`, extend `ResolveOrCreateSocialUserAsync` to apply the strict-link gate (Scenario B from data-model.md): when provider+subject is not linked but email matches an existing user, require **both** `provider.EmailVerified == true` AND `existing.EmailVerified == true`. If both true → link via `LinkSocialLoginAsync`, refresh `DisplayName`, return success. If either false → return `ResolveSocialUserResult(User=null, IsNew=false, Refusal=ExistingUnverified)`.
- [ ] T030 [US2] In same file, extend the new-user creation path: if no link, no email collision, and `provider.EmailVerified == false`, return `ResolveSocialUserResult(User=null, IsNew=false, Refusal=ProviderUnverified)`. **Do not** create a `PlatformUser` for refused signups.
- [ ] T031 [US2] In `src/Services/Sorcha.Tenant.Service/Pages/Auth/SocialCallback.cshtml.cs`, after the `ResolveOrCreateSocialUserAsync` call, add a `switch` on `result.Refusal` that sets `ErrorMessage` to the documented copy for `ProviderUnverified` ("Your `<provider>` account hasn't verified this email address. Please verify it with `<provider>` and try again.") and `ExistingUnverified` ("An account exists for this email but isn't verified. Sign in with your password and verify your email first, or recover access at `/auth/login`.") and returns `Page()`. The `<provider>` literal substitutes `result.Provider`.
- [ ] T032 [US2] In `src/Services/Sorcha.Tenant.Service/Services/SocialLoginService.cs` (or co-located `SocialLoginMetrics.cs` if a clean break is preferred — match the existing pattern in `Sorcha.Tenant.Service`), add a `Counter<long>` named `sorcha_social_login_refusal_total` on the `Sorcha.Tenant` meter with tags `provider` and `reason`. Increment it once per refusal at the call site in `SocialCallback.cshtml.cs` (after the switch in T031). Tag values for `reason`: `provider_unverified`, `existing_unverified`. Add `code_exchange_failed` and `state_invalid` tags at the existing failure paths (the early-return cases in `OnGetAsync`).
- [ ] T033 [US2] Add `LogWarning` calls at each refusal site with `{Provider}` and `{Reason}` structured fields plus a hash-based redacted email tag (use `SHA256.HashData(Encoding.UTF8.GetBytes(email)).Take(8)` as a hex string — sufficient for grouping in logs without exposing PII)
- [ ] T034 [US2] Run policy tests and refusal-rendering tests, confirm green: `dotnet test tests/Sorcha.Tenant.Service.Tests/ --filter "FullyQualifiedName~SocialLoginPolicy|FullyQualifiedName~SocialCallbackModel"`

**Checkpoint**: All three policy gates enforced. Refusals visible in telemetry. The unverified-existing-account hijack scenario is closed.

---

## Phase 5: User Story 3 — Operator controls which providers are available (Priority: P2)

**Goal**: Provider buttons on the **login** page (US1 covered the signup page) reflect the operator's `SocialProviders` configuration. Adding a provider requires only configuration + restart.

**Independent Test**: On n1 (or local dev), configure exactly one provider. Open `/auth/login` and confirm exactly one provider button is rendered. Add a second provider's configuration and restart the service. Open `/auth/login` and confirm both buttons are rendered.

### Tests for User Story 3

- [ ] T035 [P] [US3] In `tests/Sorcha.Tenant.Service.Tests/Pages/LoginModelTests.cs` (extend), add tests mirroring T017's `SignupModelTests` shape: `Model.AvailableProviders` populated from `ISocialLoginService.GetConfiguredProviderNames`; empty list renders zero buttons

### Implementation for User Story 3

- [ ] T036 [US3] In `src/Services/Sorcha.Tenant.Service/Pages/Auth/LoginModel.cs` (`Login.cshtml.cs`), inject `ISocialLoginService`, add `public IReadOnlyList<string> AvailableProviders { get; private set; } = []` property, populate in `OnGet` (mirror SignupModel changes from T022)
- [ ] T037 [US3] In `src/Services/Sorcha.Tenant.Service/Pages/Auth/Login.cshtml`, render conditional buttons via `@foreach (var provider in Model.AvailableProviders)` (mirror Signup.cshtml changes from T023). Remove any dead JS in the login page social-login handler matching what T024 cleared from signup.
- [ ] T038 [US3] Run login-page tests, confirm green: `dotnet test tests/Sorcha.Tenant.Service.Tests/ --filter "FullyQualifiedName~LoginModelTests"`

**Checkpoint**: Operator can add/remove providers via configuration alone — no code changes — and both signup and login pages reflect the change after a service restart.

---

## Phase 6: Polish & Cross-Cutting

**Purpose**: Documentation, deploy preparation, and final verification before merging.

- [ ] T039 [P] Write the body of `docs/guides/SOCIAL-LOGIN-SETUP.md` (created as stub in T003) covering: Google OAuth-app registration steps (Cloud Console → OAuth consent screen → Web app credentials → redirect URI `https://n1.sorcha.dev/auth/social/callback`); GitHub OAuth-app registration steps; how to populate `/opt/sorcha/.env` on the n1 host; how to verify the deploy; how to rotate credentials. Include screenshots if available, otherwise text-only step-by-step.
- [ ] T040 [P] Update `src/Services/Sorcha.Tenant.Service/README.md` "Social login" section: link to new doc, note the strict link policy, list the env vars consumed
- [ ] T041 [P] Update `docs/reference/API-DOCUMENTATION.md` if any of the social-auth endpoints' summaries changed (likely no — but verify)
- [ ] T042 Run full Tenant Service test suite to confirm nothing else regressed: `dotnet test tests/Sorcha.Tenant.Service.Tests/ --filter "Category!=Integration|FullyQualifiedName!~Integration"` — expect all green except known pre-existing failures unrelated to this feature
- [ ] T043 Commit and push branch; open draft PR; request `claude-review` per workflow; address any findings the bot surfaces; mark PR ready
- [ ] T044 [Manual + collaborative] Run the n1 deploy walkthrough from `quickstart.md` §4-7 jointly with operator: register OAuth apps, seed `/opt/sorcha/.env`, run `n1-reset.ps1`, walk the seven smoke scenarios in browser, verify telemetry counter populated for the email-collision refusal step
- [ ] T045 [Manual] After successful n1 deploy, set up the `/schedule` reminder to flip n1's `ASPNETCORE_ENVIRONMENT` to `Staging` per REQ-7 (target +7 days)

---

## Dependencies

```
Phase 1: Setup (T001 → T002 → T003 in parallel)
   ↓
Phase 2: Foundational
   T004, T005 [P]                      (DTOs — independent files/sections)
   T006 → T007 → T008 → T009            (sequential within SocialLoginService)
   T010 → T011                          (interface then implementation)
   T012, T013 [P]                       (different files, independent of each other)
   T014                                 (depends on T005)
   ↓
Phase 3: US1 (P1, MVP)
   Tests T015–T018 [P]                  (different files; can run together)
   T019 → T020                          (same file, sequential)
   T021                                 (depends on T014, T019, T020)
   T022 → T023 → T024                   (same file group, sequential)
   T025                                 (independent file)
   T026                                 (verification — runs last in US1)
   ↓
Phase 4: US2 (P1)         ┐
   Tests T027, T028 [P]   │ Can run in parallel with US3 after Phase 2
   T029 → T030 (same file)│
   T031, T032, T033       │
   T034                   │
                          ┘
Phase 5: US3 (P2)         ┐
   T035 [P]               │
   T036 → T037            │
   T038                   │
                          ┘
   ↓
Phase 6: Polish
   T039–T041 [P]
   T042 (full suite verification)
   T043 (PR + claude-review)
   T044 (collaborative n1 deploy)
   T045 (schedule reminder)
```

**Key dependencies summarised**:

- **US2 and US3 can run in parallel** after Phase 2 + US1 — they touch disjoint files (US2: PlatformUserService, SocialCallback page model; US3: LoginModel, Login.cshtml).
- **US1 must precede US2** because US2 builds on US1's `ResolveOrCreateSocialUserAsync` happy-path branches (extends them with refusal returns).
- **US3 depends on Phase 2's `GetConfiguredProviderNames`** (T010, T011) but not on any US1 task — could in principle run alongside US1, but the natural order is US1 → US3 since the signup-page change is the canonical pattern that US3's login-page change mirrors.

---

## Parallel Execution Examples

### After T011 completes (Phase 2 nearly done)

T012, T013, T014, plus the four US1 test files (T015-T018) can all be authored in parallel — they touch disjoint files:

```
T012 — SocialLoginEndpoints.cs
T013 — SocialCallback.cshtml.cs (parameter list change only)
T014 — IPlatformUserService.cs (signature change)
T015 — Models/SocialAuthCallbackResultTests.cs (new file)
T016 — Endpoints/SocialLoginEndpointsTests.cs (extend)
T017 — Pages/SignupModelTests.cs (extend)
T018 — Data/DatabaseInitializerTests.cs (extend or new)
```

### Phase 4 + Phase 5 in parallel (after Phase 3 ships)

```
US2 track:  T027, T028, T029, T030, T031, T032, T033, T034
US3 track:  T035, T036, T037, T038
```

These touch disjoint files in disjoint folders.

### Phase 6 docs in parallel

```
T039 — docs/guides/SOCIAL-LOGIN-SETUP.md
T040 — src/Services/Sorcha.Tenant.Service/README.md
T041 — docs/reference/API-DOCUMENTATION.md (verify only)
```

---

## Implementation Strategy

### MVP scope (US1 only)

If you need to ship the smallest valuable increment first, ship Phase 1 + Phase 2 + Phase 3. After that:

- A new public visitor can sign up via Google or GitHub on n1
- The bug is fixed
- Provider buttons render conditionally
- A fresh n1 can do social signup without manual DB edits

**Missing from MVP**: refusal/security gate (US2), login-page parity (US3). The hijack risk means **DO NOT actually deploy MVP-only to n1** — US2 must land before n1 takes organic traffic. Use MVP scope only as a development checkpoint.

### Recommended ship plan

**Single PR.** US1 + US2 + US3 + Polish all merge together. Reasons: (1) the strict-link policy is the security backstop and must not lag behind the happy-path; (2) the file overlap is small enough that the PR is reviewable in one pass; (3) collaborative n1 deploy in T044 needs the whole feature.

### After-merge sequence

1. Docker Publish CI builds new images (~10–15 min)
2. T044: joint n1 deploy walkthrough with operator
3. Verify the seven smoke scenarios pass
4. T045: schedule the `Staging` flip reminder

### Failure-mode notes

- If T012's bug fix lands without T013's parameter-list update, the social flow will throw at runtime when the Razor page tries to bind `provider` from query (which providers won't include in the redirect). T012 and T013 must ship together.
- If T029/T030 land without T031 (refusal copy), the user sees an empty page on refusal. Worse UX than the existing 404. Land them together.
- If T032 (telemetry counter) lands without T033 (logging refinements), refusals are countable but not investigatable. Land them together.

---

## Validation against spec FRs

| FR | Tasks |
|---|---|
| FR-001 (buttons only for configured providers) | T010, T011, T022, T023, T036, T037 |
| FR-002 (no greying out) | T023, T037 |
| FR-003 (env-scoped config) | T001, T002 |
| FR-004 (add provider via config alone) | T010, T011 (no code change needed for new provider) |
| FR-005 (new-user happy path) | T019, T021 |
| FR-006 (durable provider+sub link) | T019 (uses existing `LinkSocialLoginAsync`) |
| FR-007 (`LastUsedAt` update) | T020, T029 |
| FR-008 (`DisplayName` refresh) | T020, T029 |
| FR-009 (no email refresh) | implicit — no code path updates `PlatformUser.Email` post-creation |
| FR-010 (refuse provider unverified) | T030 |
| FR-011 (refuse existing unverified) | T029 |
| FR-012 (allow both verified) | T029 |
| FR-013 (no re-check on returning) | T020 |
| FR-014 (issue session token) | unchanged from existing `SocialCallback.cshtml.cs` flow |
| FR-015 (welcome at most once) | unchanged — `WelcomeEmailDispatcher.SendIfPendingAsync` is idempotent |
| FR-016 (refusal copy) | T031 |
| FR-017 (tolerate cancellation) | unchanged — existing `OnGetAsync` early-error path |
| FR-018 (telemetry on refusal) | T032, T033 |
| FR-019 (config-driven seed) | T002 (env var), T025 (read it), T018 (test it) |
| FR-020 (demo banner) | unchanged — already wired in `docker-compose.n1.yml:55` |
| FR-021 (single canonical callback) | T012, T013 |

Every FR has at least one task. Every test FR has at least one test task.

---

## Total task count

- Phase 1 (Setup): **3** tasks
- Phase 2 (Foundational): **11** tasks
- Phase 3 (US1 / P1 / MVP): **12** tasks (4 tests + 7 implementation + 1 verify)
- Phase 4 (US2 / P1): **8** tasks (2 tests + 5 implementation + 1 verify)
- Phase 5 (US3 / P2): **4** tasks (1 test + 2 implementation + 1 verify)
- Phase 6 (Polish): **7** tasks (3 docs + 1 full-test + 1 PR + 1 deploy + 1 schedule)

**Total: 45 tasks**

Tests: 8 (within the appropriate user-story phases per spec template guidance)
Implementation: 19 production code, 5 config / docs
Verification & deploy: 5 cross-cutting tasks

---

## Notes

- All checklist items follow the format `- [ ] T### [P?] [Story?] Description with file path` per the SpecKit task convention.
- Tasks that mention "extend existing test file" assume the file exists in master at commit `d0cdd55a`. Verify before adding to avoid stomping unrelated changes.
- Manual tasks (T044, T045) are explicitly marked because they require operator collaboration or external system interaction (provider consoles, schedule routine).
- The spec's "Notes for Planning" section says "planning should consume the design doc directly and produce a tasks list that maps each FR in this spec to one or more implementation steps in the design." The §"Validation against spec FRs" table above provides that mapping.
