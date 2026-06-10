---
description: "Task list for Feature 150 — Unified Account Security Surface"
---

# Tasks: Unified Account Security Surface

**Input**: Design documents from `/specs/150-account-security/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/ (all present)
**Design source of truth**: `docs/superpowers/specs/2026-06-10-unified-account-security-design.md`

**Tests**: INCLUDED — the constitution mandates ≥80% coverage on core libs and the spec's acceptance scenarios are testable; tests are ordered first within each story and must fail before implementation.

**Organization**: grouped by the four user stories (US1=P1 MVP → US4=P4), each independently shippable.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: parallelizable (different files, no dependency on an incomplete task)
- **[Story]**: US1–US4 (Setup / Foundational / Polish carry no story label)
- Every task names an exact file path.

## Path conventions

- Tenant backend: `src/Services/Sorcha.Tenant.Service/`
- Shared UI: `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/`
- Menu (web shared): `src/Apps/Sorcha.UI/Sorcha.UI.Core/`
- Web host: `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/`
- PWA host: `src/Apps/Sorcha.Wallet.Pwa/`
- Tests: `tests/Sorcha.Tenant.Service.Tests/`, `tests/Sorcha.UI.Core.Tests/` (bUnit), `tests/Sorcha.UI.E2E.Tests/` (Playwright/NUnit)

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: prep that is not story-specific. No new project is created.

- [ ] T001 [P] Verify `RootNamespace=Sorcha.UI.Core` on `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Sorcha.UI.Components.User.csproj` (so relocated component namespaces stay stable per R-012) and create the empty `Components/Security/` folder.
- [ ] T002 [P] Scaffold the E2E category: create `tests/Sorcha.UI.E2E.Tests/PageObjects/SecurityPage.cs` and an empty `tests/Sorcha.UI.E2E.Tests/Docker/Security/` folder with a `[Category("Security")]` fixture base (per the `sorcha-ui` skill page-object pattern).
- [ ] T003 [P] Confirm `Sorcha.UI.Web.Client` and `Sorcha.Wallet.Pwa` both transitively reference `Sorcha.UI.Components.User` (web via `Sorcha.UI.Core` re-export, PWA directly) so a shared `SecurityHome` renders on both hosts.

---

## Phase 2: Foundational (Blocking Prerequisites)

**⚠️ CRITICAL**: the assurance model + notifier are consumed by every user story. Complete before US1.

- [ ] T004 Write the EXHAUSTIVE matrix unit test for the floor rule (every proof-tier × operation × target → expected allow/deny, exactly per `contracts/floor-rule-policy.md` Tables A/B/C and the 4 worked invariants) in `tests/Sorcha.Tenant.Service.Tests/Auth/AssurancePolicyTests.cs` — MUST fail first.
- [ ] T005 Implement `AssurancePolicy` (static `TierOf(AuthMethodKind)`, `RequiredProofTierFor(operation,target)`, `CanRemove(user,method)`) in `src/Services/Sorcha.Tenant.Service/Services/Auth/AssurancePolicy.cs` + `AuthAssuranceTier` enum (`Basic=1,Strong=2,Strongest=3`) — make T004 pass.
- [ ] T006 [P] Extend the aggregate DTOs (`AuthMethodsResponse` + `AuthMethodRow`) with `AssuranceTier`, `RequiredProofTier`, `Role`, and `SmsAvailable` per `contracts/account-security.openapi.yaml`, in `src/Services/Sorcha.Tenant.Service/Models/` (auth-methods response model).
- [ ] T007 [P] Add the F112 `SecurityChangeDispatch` record + `security-change.{html,txt}` Sorcha-branded Scriban template (no per-org branding) under `src/Services/Sorcha.Tenant.Service/Emails/Templates/`, register it on `ITransactionalEmailService`, and commit snapshot fixtures in `tests/Sorcha.Tenant.Service.Tests/Fixtures/Emails/security-change.{html,txt}`.
- [ ] T008 Implement `SecurityChangeNotifier` (F118 inbox via `TenantSecurityInboxWriter` + F112 `SecurityChangeDispatch`, wrapped `try`/`LogError`/swallow per FR-011) in `src/Services/Sorcha.Tenant.Service/Services/Auth/SecurityChangeNotifier.cs` + a unit test asserting a notifier failure does not throw, in `tests/Sorcha.Tenant.Service.Tests/Auth/SecurityChangeNotifierTests.cs`.

**Checkpoint**: assurance policy + notifier ready — user stories can begin.

---

## Phase 3: User Story 1 — Consolidated Security home + floor rule + finished proofs (Priority: P1) 🎯 MVP

**Goal**: one discoverable *Security* home (web) with the three job-based groups, assurance badges, the assurance-aware floor rule live, finished Passkey + Re-OAuth step-up proofs, and always-notify on every change.

**Independent Test**: sign in to `/app`, open the avatar menu → *Security* sits between *My Profile* and *My Devices* → opens `/app/security` showing the three groups with badges; a Basic-only proof cannot remove a passkey; every change raises an inbox entry + email.

### Tests for User Story 1

- [ ] T009 [P] [US1] bUnit test: `SecurityHome` renders the three groups, an `AssuranceBadge` per method, and disables Remove per server `CanRemove`/`RequiredProofTier`, in `tests/Sorcha.UI.Core.Tests/Security/SecurityHomeTests.cs` — fail first.
- [ ] T010 [P] [US1] Unit test: `IAuthMethodService` aggregate emits assurance-aware `CanRemove` + `RequiredProofTier` (drives off `AssurancePolicy`) in `tests/Sorcha.Tenant.Service.Tests/Auth/AuthMethodServiceAssuranceTests.cs` — fail first.
- [ ] T011 [P] [US1] Playwright E2E: user-menu *Security* entry (`data-testid=user-menu-security`) between My Profile/My Devices → navigates to `/app/security`; and floor-rule blocks passkey removal when only a Basic proof exists, in `tests/Sorcha.UI.E2E.Tests/Docker/Security/SecurityHomeTests.cs` — fail first.

### Implementation for User Story 1

- [ ] T012 [US1] Widen `IAuthMethodService`/`AuthMethodService` to populate `AssuranceTier`, `Role`, `RequiredProofTier`, and assurance-aware `CanRemove` via `AssurancePolicy`, in `src/Services/Sorcha.Tenant.Service/Services/IAuthMethodService.cs` + `AuthMethodService.cs` (depends T005, T006).
- [ ] T013 [US1] Extend `GET /api/me/auth-methods` to return the new fields (+ `SmsAvailable=false` until US3) with Scalar `.WithSummary()`/`.WithDescription()` + XML docs, in `src/Services/Sorcha.Tenant.Service/Endpoints/AuthMethodsEndpoints.cs`.
- [ ] T014 [P] [US1] Finish the **Passkey** step-up proof (reuse the FIDO2 assertion ceremony scoped to the challenge nonce → Strongest proof) in `src/Services/Sorcha.Tenant.Service/Services/.../AuthChallengeService.cs` + `Endpoints/AuthChallengeEndpoints.cs` (replaces the placeholder).
- [ ] T015 [P] [US1] Finish the **Re-OAuth** step-up proof (re-run the social flow with a `stepup` intent, verify the returned identity matches a linked account → social tier) in `AuthChallengeService.cs` + `Endpoints/SocialLoginEndpoints.cs`.
- [ ] T016 [US1] Enforce the floor rule on `challenge/initiate` + `challenge/verify` (offer only proof methods whose tier ≥ `RequiredProofTier`; server re-checks on verify, `403 proof_tier_insufficient`) in `AuthChallengeEndpoints.cs` (depends T005, T014, T015).
- [ ] T017 [US1] Wire `SecurityChangeNotifier` into every existing auth-method mutation (password set/change/remove, social unlink, passkey add/rename/remove, TOTP enable/disable) across `PasswordEndpoints.cs`, `SocialLoginEndpoints.cs`, `PasskeyEndpoints.cs`, `TotpEndpoints.cs` (depends T008).
- [ ] T018 [P] [US1] Relocate `PasswordSection.razor`, `SocialLinksSection.razor`, `PasskeysSection.razor`, `AuthChallengeDialog.razor` from `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Components/Settings/AuthMethods/` to `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Components/Security/` (namespaces preserved via RootNamespace); update all `using`/references.
- [ ] T019 [P] [US1] Create `AssuranceBadge.razor` (Strongest/Strong/Basic chip) in `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Components/Security/`.
- [ ] T020 [US1] Build `SecurityHome.razor` (three job-based groups: *How you sign in* / *Two-factor authentication* (TOTP only until US2) / *Recovery*) composing the relocated sections + `AssuranceBadge`, in `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Components/Security/` (depends T018, T019).
- [ ] T021 [US1] Add the **Security** item to `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Shared/UserProfileMenu.razor` between *My Profile* and *My Devices* — `Icons.Material.Filled.Security`, `data-testid="user-menu-security"`, base-relative `Navigation.NavigateTo("security")`.
- [ ] T022 [US1] Create the web host page `@page "/security"` → `<SecurityHome/>` in `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/Security.razor` (resolves to `/app/security`).
- [ ] T023 [US1] Retire the Settings *Accounts* + *Security* tabs and redirect old deep-links (`/settings?tab=accounts|security`, `/settings/notifications` unaffected) to `/security`, in `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/Settings.razor`.
- [ ] T024 [US1] Update `AuthChallengeDialog.razor` to render only the server-offered (floor-permitted) proof rungs and ensure the now-finished Passkey + Re-OAuth rungs work (no placeholder text), in `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Components/Security/AuthChallengeDialog.razor` (depends T016).
- [ ] T025 [US1] Use `IInlineFeedback` for own-action feedback on the Security home and inline `MudAlert` for dialog errors (no `ISnackbar`, CLAUDE.md #12), across the Security components.

**Checkpoint**: US1 is a fully functional, discoverable, safe web Security home — the shippable MVP.

---

## Phase 4: User Story 2 — Email OTP second factor (Priority: P2)

**Goal**: enable an emailed one-time code as a Basic second factor, usable at login and as a step-up proof.

**Independent Test**: enable *Email code* → sign out → sign in with the first factor → receive + enter the emailed code → reach the app; reuse/expired codes rejected; rate-limit throttles.

### Tests for User Story 2

- [ ] T026 [P] [US2] Unit: `ServerSentOtpService` — single-use GETDEL, hashed code, 10-min expiry, 5-attempt cap, send rate-limit — in `tests/Sorcha.Tenant.Service.Tests/Auth/ServerSentOtpServiceTests.cs` — fail first.
- [ ] T027 [P] [US2] Unit: `VerificationChannelRegistry` resolves the EmailOtp channel with `Tier=Basic`, in `tests/Sorcha.Tenant.Service.Tests/Auth/VerificationChannelRegistryTests.cs` — fail first.
- [ ] T028 [P] [US2] Email snapshot test for `twofactor-code` in `tests/Sorcha.Tenant.Service.Tests/Services/EmailTemplateSnapshotTests.cs` (+ fixtures `Fixtures/Emails/twofactor-code.{html,txt}`) — fail first.
- [ ] T029 [P] [US2] Playwright E2E: enable Email OTP, login-with-email-code, reuse + expiry rejected, rate-limit, in `tests/Sorcha.UI.E2E.Tests/Docker/Security/EmailOtpTests.cs` — fail first.

### Implementation for User Story 2

- [ ] T030 [US2] **Squash the schema change into the Tenant Service's existing initial migration** (pre-release policy — do NOT add an incremental migration): add `PlatformUser.PhoneNumber` (E.164, nullable) + `PhoneVerifiedAt` (nullable) **and** the new `PlatformUserTwoFactor` 1:1 table (`TotpEnabled`, `EmailOtpEnabled`, `SmsOtpEnabled`, `UpdatedAt`) to the models in `src/Services/Sorcha.Tenant.Service/Models/PlatformUser.cs` + `PlatformUserTwoFactor.cs`, then **regenerate** the existing initial migration (remove + re-add `InitialCreate` against the design-time factory) so `*.Designer.cs` + `*ModelSnapshot.cs` stay in lockstep — never hand-edit. ⚠️ **Shared, once** — US3's phone columns ride this same squashed initial migration; US3 adds NO migration of its own.
- [ ] T031 [P] [US2] Define `IVerificationChannel` (`Kind`, `Tier`, `InitiateAsync`, `VerifyAsync`) + `VerificationChannelRegistry` in `src/Services/Sorcha.Tenant.Service/Services/Auth/`.
- [ ] T032 [US2] Implement `ServerSentOtpService` (Redis GETDEL, hashed codes, expiry, attempt cap, rate-limit) and register it through F113 `IStorageRegistrationLog` as a **cache-style** store (not fail-fast audited), in `src/Services/Sorcha.Tenant.Service/Services/Auth/ServerSentOtpService.cs` (make T026 pass).
- [ ] T033 [P] [US2] Add the F112 `TwoFactorCodeDispatch` record + `twofactor-code.{html,txt}` Sorcha-branded template under `src/Services/Sorcha.Tenant.Service/Emails/Templates/` and register on `ITransactionalEmailService` (make T028 pass).
- [ ] T034 [US2] Implement `EmailOtpChannel : IVerificationChannel` (uses `ServerSentOtpService` + `TwoFactorCodeDispatch`) and register it in the registry (always available), in `src/Services/Sorcha.Tenant.Service/Services/Auth/` (make T027 pass).
- [ ] T035 [US2] Endpoints `POST /api/me/2fa/email/enable`, `POST /api/me/2fa/email/verify`, `DELETE /api/me/2fa/email` (Scalar summaries + XML), in `src/Services/Sorcha.Tenant.Service/Endpoints/TwoFactorChannelEndpoints.cs`; flip `PlatformUserTwoFactor.EmailOtpEnabled`; always-notify via `SecurityChangeNotifier`.
- [ ] T036 [US2] Add `EmailOtp` to the `ChallengeMethod` enum and to step-up `initiate`/`verify` (initiate sends a code) in `AuthChallengeService.cs` + `AuthChallengeEndpoints.cs`.
- [ ] T037 [US2] Wire EmailOtp into the **login** second-factor path (offer strongest-enrolled first + a "use another method" fallback per R-010) in the login/verify-2FA flow (`LoginService` / verify-2fa endpoint).
- [ ] T038 [US2] Build `TwoFactorSection.razor` (TOTP + Email OTP rows with badges, enable/verify/disable) in `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Components/Security/` and slot it into `SecurityHome`; extend `IAuthMethodsClientService` (in `Sorcha.UI.Components.User/Services/Shared/`) with email enable/verify/disable methods.

**Checkpoint**: US1 + US2 both work; Email OTP is a usable Basic second factor.

---

## Phase 5: User Story 3 — SMS OTP second factor, configuration-gated (Priority: P3)

**Goal**: where an SMS provider is configured, verify a mobile number and use an SMS code as a Basic second factor; otherwise the option is entirely absent.

**Independent Test**: with a (fake) SMS provider configured, verify a number → enable SMS → sign in with an SMS code; with no provider, the SMS option never appears and `smsAvailable=false`.

### Tests for User Story 3

- [ ] T039 [P] [US3] Unit: the registry **hides** the SMS channel when `ISmsSender` is unconfigured and **registers** it when configured, in `tests/Sorcha.Tenant.Service.Tests/Auth/SmsChannelGatingTests.cs` — fail first.
- [ ] T040 [P] [US3] Unit: phone verify sets `PhoneVerifiedAt`; per-number send cap enforced; changing the number clears `PhoneVerifiedAt`, in `tests/Sorcha.Tenant.Service.Tests/Auth/SmsPhoneVerifyTests.cs` — fail first.
- [ ] T041 [P] [US3] Playwright E2E with a fake `ISmsSender`: absent-when-unconfigured, verify-phone, enable, login-with-sms-code, in `tests/Sorcha.UI.E2E.Tests/Docker/Security/SmsOtpTests.cs` — fail first.

### Implementation for User Story 3

- [ ] T042 [US3] Define `ISmsSender` + `AcsSmsSender` (config-gated, mirroring `IEmailSender`'s SMTP/ACS auto-select on `Sms:*` config) in `src/Services/Sorcha.Tenant.Service/Services/Sms/`.
- [ ] T043 [US3] Register the SMS channel in `VerificationChannelRegistry` **only when** `ISmsSender` is configured, and set the aggregate `SmsAvailable` flag accordingly (make T039 pass) — `VerificationChannelRegistry` + `AuthMethodService`.
- [ ] T044 [US3] Implement the phone capture + verify flow via `ServerSentOtpService` (`PhoneVerify` purpose) with a per-number send/cost guard; changing the number clears `PhoneVerifiedAt` + disables SMS (make T040 pass) — `src/Services/Sorcha.Tenant.Service/Services/Auth/`.
- [ ] T045 [US3] Endpoints `POST /api/me/2fa/sms/phone`, `POST /api/me/2fa/sms/phone/verify`, `POST /api/me/2fa/sms/enable`, `DELETE /api/me/2fa/sms` — return `404` when SMS is unavailable (Scalar + XML), in `TwoFactorChannelEndpoints.cs`; always-notify.
- [ ] T046 [US3] Implement `SmsOtpChannel : IVerificationChannel` and wire `SmsOtp` into the login second-factor path + the step-up `ChallengeMethod`, in `src/Services/Sorcha.Tenant.Service/Services/Auth/`.
- [ ] T047 [US3] Extend `TwoFactorSection.razor` with the SMS row (hidden when `smsAvailable=false`) + the phone capture/verify UI; extend `IAuthMethodsClientService` with the SMS methods.

**Checkpoint**: US1–US3 work; SMS is available wherever an operator configures a provider and invisible elsewhere.

---

## Phase 6: User Story 4 — Wallet PWA parity (Priority: P4)

**Goal**: the identical Security home is available and fully functional in the citizen wallet PWA, with passkeys kept distinct from wallet device-pairing.

**Independent Test**: in the PWA, open *Security* → same three groups + actions; add/remove behaves like web; *Security → Passkeys* is clearly distinct from *My Devices*; social-link round-trip returns to `/wallet/security`.

### Tests for User Story 4

- [ ] T048 [P] [US4] PWA Playwright: `/wallet/security` renders the three groups, add/remove parity with web, base-relative nav (no origin-root 404), in `tests/Sorcha.UI.E2E.Tests/Docker/CitizenWallet/CitizenWalletSecurityTests.cs` — fail first.
- [ ] T049 [P] [US4] PWA Playwright: *Security → Passkeys* is visually/textually distinct from *My Devices* (FR-026), same file or a sibling test.

### Implementation for User Story 4

- [ ] T050 [US4] Create the PWA host page `@page "/security"` → `<SecurityHome/>` in `src/Apps/Sorcha.Wallet.Pwa/Pages/Security.razor` (resolves to `/wallet/security`; all nav base-relative).
- [ ] T051 [US4] Add a **Security** entry to the PWA navigation (FloatingTabBar / settings menu) using base-relative `NavigateTo("security")`, in `src/Apps/Sorcha.Wallet.Pwa/` layout/nav component.
- [ ] T052 [US4] Ensure the PWA host DI registers `IAuthMethodsClientService` + the Security client services on a consumer-tier token, and confirm the `/me/*` cross-tier endpoints accept the consumer audience, in `src/Apps/Sorcha.Wallet.Pwa/Program.cs`.
- [ ] T053 [US4] Validate + fix the social-link OAuth round-trip from inside the PWA so it returns to `/wallet/security` with the account linked (the one host-specific flow per the design).
- [ ] T054 [US4] Copy/iconography pass ensuring *Security → Passkeys* never reads as the wallet's *My Devices* (FR-026), across `SecurityHome`/`SignInMethodsSection` and the PWA My Devices page.

**Checkpoint**: full web ⇄ PWA parity; a citizen is the same person on both hosts.

---

## Phase 7: Polish & Cross-Cutting Concerns

- [ ] T055 [P] Docs: update the Tenant Service README with the Security surface, 2FA channels, floor rule, and SMS config, in `src/Services/Sorcha.Tenant.Service/README.md`.
- [ ] T056 [P] Docs: add an **F150** section to `.claude/skills/sorcha-architecture/SKILL.md` (endpoint surface, floor-rule policy, channel abstraction, assurance tiers).
- [ ] T057 [P] Docs: add the new endpoints to `docs/reference/API-DOCUMENTATION.md` and the 2FA-channel + floor-rule + SMS-config flows to `docs/guides/AUTHENTICATION-SETUP.md`.
- [ ] T058 [P] Observability: add OTel counters for OTP send/verify (tagged channel/outcome) and floor-rule rejections on the `Sorcha.Tenant.Auth` (or `Sorcha.Identity`) meter, in `src/Services/Sorcha.Tenant.Service/Services/Auth/`.
- [ ] T059 [P] Audit: MIT SPDX/Copyright headers on all new files; `.WithSummary()`/`.WithDescription()` + XML docs on every new endpoint; confirm no hard-coded `<Version>`.
- [ ] T060 Update `docs/reference/development-status.md` (Feature 150 status) and run `quickstart.md` end-to-end against the Docker stack for all four phases.
- [ ] T061 **Decision gate**: confirm or revise the flagged **password-as-Strong-proof** call (`contracts/floor-rule-policy.md` Table A) with the team before merge; if demoted to Basic, update `AssurancePolicy` + the matrix test + the contract in lockstep.

---

## Dependencies & Execution Order

### Phase dependencies

- **Setup (P1)** → no dependencies.
- **Foundational (P2)** → after Setup; **blocks all user stories** (AssurancePolicy + DTOs + notifier).
- **US1 (P3)** → after Foundational. The shippable MVP.
- **US2 (P4)** → after Foundational; extends the US1 `SecurityHome`/`TwoFactorSection`. **Owns the schema squash (T030)** — the regenerated initial migration.
- **US3 (P5)** → after Foundational; **depends on the T030 squash** for `PhoneNumber`/`PhoneVerifiedAt` (adds no migration of its own) and reuses `ServerSentOtpService` + the channel registry from US2.
- **US4 (P6)** → after US1 (renders US1's shared component); independent of US2/US3 (those channels simply appear if present).
- **Polish (P7)** → after the desired stories.

### Critical shared artifacts

- **AssurancePolicy (T005)** — consumed by US1 (CanRemove/RequiredProofTier), US2/US3 (Basic tiering), step-up enforcement.
- **Schema squash (T030)** — pre-release policy: the schema change is folded into the existing **initial** migration (regenerated), NOT a new incremental migration. Carries both US2 (`PlatformUserTwoFactor`) and US3 (phone columns) in one go. Never split, never add a second migration.
- **ServerSentOtpService + VerificationChannelRegistry (T031/T032)** — built in US2, reused by US3.
- **SecurityChangeNotifier (T008)** — every mutation across every story notifies through it.

### Parallel opportunities

- Setup T001–T003 all [P].
- Foundational T006 + T007 [P] (T004→T005 sequential; T008 after T007).
- US1: T009/T010/T011 [P] (tests); T014/T015 [P]; T018/T019 [P].
- US2: T026/T027/T028/T029 [P] (tests); T031/T033 [P].
- US3: T039/T040/T041 [P] (tests).
- US4: T048/T049 [P] (tests).
- Polish T055–T059 [P].

---

## Implementation strategy

### MVP first (US1 only)

1. Phase 1 Setup → Phase 2 Foundational → Phase 3 US1.
2. **STOP and validate** US1 via `quickstart.md` Phase 1: discoverable Security home, floor rule live, finished proofs, always-notify.
3. Ship the MVP (web consolidation) — it delivers the primary ask with zero new channels.

### Incremental delivery

US1 (MVP) → US2 (Email OTP) → US3 (SMS OTP) → US4 (PWA parity). Each is independently demonstrable per the matching `quickstart.md` phase (SC-009).

### Notes

- [P] = different files, no incomplete-task dependency.
- Tests in each story are written first and must fail before implementation (constitution + spec).
- Commit after each task or logical group; reference `T0xx` + the feature id in messages.
- Stop at any checkpoint to validate the story independently before proceeding.
- Keep `Security → Passkeys` (login authenticators) verbally + visually distinct from `My Devices` (wallet delegation) throughout (FR-026).
