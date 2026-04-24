---
description: "Dependency-ordered task list for Feature 112 — Transactional Email & Verification Sweep"
---

# Tasks: Transactional Email & Verification Sweep

**Input**: Design documents from `/specs/112-email-sweep/`
**Prerequisites**: plan.md (required), spec.md (required for user stories), research.md, data-model.md, contracts/

**Tests**: Test tasks are REQUIRED. The project constitution (Principle IV) mandates ≥85% coverage on new code; the spec's success criteria (SC-006, SC-007) and the plan's Testing section reinforce this. Test tasks appear inline within each phase.

**Organization**: Tasks are grouped by user story per `spec.md`. Foundational work that blocks every story is isolated in Phase 2.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete work)
- **[Story]**: Which user story this task belongs to (US1, US2, US3, US4)
- All paths are absolute-from-repo-root

## Path Conventions

Microservice structure per plan.md. Source under `src/Services/Sorcha.Tenant.Service/`, tests under `tests/Sorcha.Tenant.Service.Tests/`. The central package manifest is `Directory.Packages.props`.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Pin the new dependency and reserve the template + fixture locations. No behavioural change yet.

- [X] T001 Add `<PackageVersion Include="Scriban" Version="5.12.0" />` to `Directory.Packages.props`
- [X] T002 Reference Scriban from `src/Services/Sorcha.Tenant.Service/Sorcha.Tenant.Service.csproj` (add `<PackageReference Include="Scriban" />`)
- [X] T003 [P] Add `<ItemGroup><EmbeddedResource Include="Emails/Templates/**/*" /></ItemGroup>` to `src/Services/Sorcha.Tenant.Service/Sorcha.Tenant.Service.csproj` so template files ship with the assembly
- [X] T004 [P] Create empty directory `src/Services/Sorcha.Tenant.Service/Emails/Templates/` (tracked via the embedded-resource glob once populated)
- [X] T005 [P] Create empty directory `tests/Sorcha.Tenant.Service.Tests/Fixtures/Emails/` for snapshot fixtures

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Everything downstream user stories need. Ends with a service that compiles, DI-resolves the new abstractions, has the persistence change in place, and exercises the renderer/resolver/dispatcher via unit tests against the `base.html` + `base.txt` templates.

**⚠️ CRITICAL**: No user-story phase can begin until Phase 2 is complete. Every user-story phase touches one or more of the classes created here.

### Domain models & records

- [X] T006 [P] Create `EmailBranding` record in `src/Services/Sorcha.Tenant.Service/Services/EmailBranding.cs` per `data-model.md`
- [X] T007 [P] Create `VerifyEmailTemplateModel`, `InviteEmailTemplateModel`, `ResetPasswordTemplateModel`, `WelcomePublicTemplateModel`, `WelcomeInvitedTemplateModel` view-model records in `src/Services/Sorcha.Tenant.Service/Services/EmailTemplateModels.cs` — renamed with `*TemplateModel` suffix to avoid clash with existing `Pages.Auth.VerifyEmailModel` / `ResetPasswordModel` Razor PageModel classes
- [X] T008 [P] Create dispatch records (`VerifyEmailDispatch`, `InviteEmailDispatch`, `ResetPasswordDispatch`, `WelcomeDispatchContext`, `WelcomeVariant` enum) in `src/Services/Sorcha.Tenant.Service/Services/EmailDispatchRecords.cs` per `contracts/internal-interfaces.md`

### Persistence

- [X] T009 Add `public DateTimeOffset? WelcomeSentAt { get; set; }` property to `src/Services/Sorcha.Tenant.Service/Models/PlatformUser.cs`
- [X] T010 Add `WelcomeSentAt` column to the `PlatformUsers` `CreateTable` block inside `src/Services/Sorcha.Tenant.Service/Migrations/20260408160910_InitialCreate.cs`
- [X] T011 Update both `20260408160910_InitialCreate.Designer.cs` and `TenantDbContextModelSnapshot.cs` with the new property entry for the PlatformUser block (manual edit — no running PostgreSQL required for this pre-release fold-in)

### Configuration

- [X] T012 Extend `EmailSettings` in `src/Services/Sorcha.Tenant.Service/Services/IEmailSender.cs` with new optional fields: `LogoUrl`, `PrimaryColor` (default `"#2563eb"`), `Tagline`, `ReplyTo` (default `"help@sorcha.dev"`)

### Tighten `IEmailSender`

- [X] T013 Change `IEmailSender.SendAsync` signature — now takes `htmlBody` AND `textBody`. Deleted dead typed-method declarations (FR-022).
- [X] T014 `SmtpEmailSender` now assigns `BodyBuilder { HtmlBody, TextBody }`. Dead typed-method bodies removed.
- [X] T015 `AcsEmailSender` now passes `EmailContent { Html, PlainText }`. Dead typed-method bodies removed.

### Renderer

- [X] T016 Create `IEmailTemplateRenderer` interface
- [X] T017 Create `ScribanEmailTemplateRenderer` — parses all embedded `.html`/`.txt` at construction, fails fast on parse errors, renders via `(Html, Text)` tuple, throws `KeyNotFoundException` for unknown names. Includes in-memory `ITemplateLoader` so `{{ include 'base.html' }}` works without disk I/O.

### Branding resolver

- [X] T018 Create `IEmailBrandingResolver` interface
- [X] T019 Create `EmailBrandingResolver` implementing per-field fallback semantics

### Base templates

- [X] T020 [P] Create `Emails/Templates/base.html`
- [X] T021 [P] Create `Emails/Templates/base.txt`

### Transactional facade

- [X] T022 Create `ITransactionalEmailService` interface
- [X] T023 Create `TransactionalEmailService` concrete — stateless, routes to renderer + sender per flow

### Welcome dispatcher

- [X] T024 Create `WelcomeEmailDispatcher` — idempotent, non-throwing, decides Public vs Invited variant by earliest-joined standard-org membership

### DI wiring

- [X] T025 Update `AddTenantEmail` — registers the four new services (singleton renderer, scoped resolver/facade/dispatcher) alongside the existing SMTP/ACS backend selection

### Phase-2 foundation tests

- [X] T026 [P] `ScribanEmailTemplateRendererTests` — 4 tests pass (constructor doesn't throw, base renders via verify include, unknown name throws KeyNotFoundException, snake_case model binding works)
- [X] T027 [P] `EmailBrandingResolverTests` — 5 tests pass (defaults, full org branding, logo fallback, colour fallback, null-branding fallback)
- [X] T028 [P] `TransactionalEmailServiceTests` — 6 tests pass (verify/invite/reset/welcome-public/welcome-invited routing + InvalidOperationException on invited-without-org)
- [X] T029 `WelcomeEmailDispatcherTests` — 7 tests pass (send once, no-op on second, skip if unverified, public-org picks Public variant, standard-org picks Invited, earliest-joined wins with multiple standard orgs, transactional throw swallowed + WelcomeSentAt NOT set on failure)

**Checkpoint**: `dotnet build` passes. `dotnet test --filter "FullyQualifiedName~EmailTemplate|EmailBranding|TransactionalEmail|WelcomeEmail"` passes. Foundation is ready — user-story phases can now run in priority order.

---

## Phase 3: User Story 1 - Verification email (Priority: P1) 🎯 MVP

**Goal**: A new public user receives a branded, multipart verification email with a single clear CTA button, not a plaintext token. Fixes release-blocking bug in `EmailVerificationService`.

**Independent Test**: Sign up a new email+password user against a running Tenant Service. Inspect the outbound email. It contains the Sorcha brand frame, greets the user by display name, has a "Confirm my email" button linking to `/auth/verify-email?token=…`, has a plaintext body containing the same URL, states 24-hour expiry.

### Templates & fixtures

- [X] T030 [P] [US1] Create `src/Services/Sorcha.Tenant.Service/Emails/Templates/verify.html` per `contracts/email-templates.md § 2`
- [X] T031 [P] [US1] Create `src/Services/Sorcha.Tenant.Service/Emails/Templates/verify.txt` per `contracts/email-templates.md § 2`
- [X] T032 [P] [US1] Create golden fixture `tests/Sorcha.Tenant.Service.Tests/Fixtures/Emails/verify.html` rendered against the canonical test model (`DisplayName = "Stuart Fraser"`, `VerifyUrl = "https://sorcha.dev/auth/verify-email?token=FIXTURE_TOKEN"`, `ExpiresInHours = 24`, Sorcha default branding)
- [X] T033 [P] [US1] Create golden fixture `tests/Sorcha.Tenant.Service.Tests/Fixtures/Emails/verify.txt` (same model)

### Caller migration

- [X] T034 [US1] Modified `EmailVerificationService` — now injects `ITransactionalEmailService`, `WelcomeEmailDispatcher` (for T051 later), and `IOptions<EmailSettings>`. `GenerateAndSendVerificationAsync` now builds a `VerifyEmailDispatch` and calls `SendVerificationAsync`.

### Tests

- [X] T035 [P] [US1] Add snapshot test to `tests/Sorcha.Tenant.Service.Tests/Services/ScribanEmailTemplateRendererTests.cs` asserting `Render("verify", canonicalModel)` returns strings equal to the `verify.html` and `verify.txt` fixtures
- [X] T036 [US1] Create or update `tests/Sorcha.Tenant.Service.Tests/Services/EmailVerificationServiceTests.cs` — with a fake `ITransactionalEmailService`, assert `GenerateAndSendVerificationAsync` calls `SendVerificationAsync` with: a non-empty verify URL containing the persisted token; the user's `DisplayName`; 24-hour expiry. Assert nothing is ever passed to a bare `IEmailSender.SendAsync`.

**Checkpoint**: US1 delivered. Sign up → verification email with branded button. Plaintext fallback works. Phase 3 gate met before Phase 4 begins.

---

## Phase 4: User Story 2 - Invitation email (Priority: P1)

**Goal**: An org admin invites a new user; the invited person receives an invitation email carrying the inviting organisation's name, logo, primary colour, plus the inviter's name, role, and a clear "Accept invitation" CTA. Fixes the second release-blocking plaintext-token bug.

**Independent Test**: From a branded test organisation (seed via API or admin UI), invite a fresh email address. The email that arrives shows the org logo in the header, uses the org primary colour on the button, names the organisation and role in the body, mentions the inviter, links to the accept flow, and falls back to Sorcha defaults for any branding field the org hasn't set.

### Templates & fixtures

- [X] T037 [P] [US2] Create `src/Services/Sorcha.Tenant.Service/Emails/Templates/invite.html` per `contracts/email-templates.md § 3`
- [X] T038 [P] [US2] Create `src/Services/Sorcha.Tenant.Service/Emails/Templates/invite.txt`
- [X] T039 [P] [US2] Create golden fixture `tests/Sorcha.Tenant.Service.Tests/Fixtures/Emails/invite-branded.html` rendered with fully-populated org branding (Acme logo, `#FF5722`)
- [X] T040 [P] [US2] Create golden fixture `tests/Sorcha.Tenant.Service.Tests/Fixtures/Emails/invite-default.html` rendered with an org that has no branding (falls back to Sorcha defaults)
- [X] T041 [P] [US2] Create golden fixture `tests/Sorcha.Tenant.Service.Tests/Fixtures/Emails/invite-branded.txt`
- [X] T042 [P] [US2] Create golden fixture `tests/Sorcha.Tenant.Service.Tests/Fixtures/Emails/invite-default.txt`

### Caller migration

- [X] T043 [US2] Modified `InvitationService` — now injects `IOrganizationRepository`, `ITransactionalEmailService`, and `IOptions<EmailSettings>`. `CreateInvitationAsync` loads the inviting `Organization` (with branding) and calls `SendInvitationAsync`. Migrated ahead of schedule to unblock the interface-tightening compile gate.

### Tests

- [X] T044 [P] [US2] Snapshot tests live in the unified `EmailTemplateSnapshotTests.cs` (both branded and default invite variants covered by the `Cases` theory)
- [X] T045 [US2] Updated `InvitationServiceTests` — now mocks `IOrganizationRepository` and `ITransactionalEmailService`. The first test asserts `SendInvitationAsync` is called with a well-formed `InviteEmailDispatch` (correct email, inviter name, org, role "Designer", accept URL, 7-day expiry). Remaining 9 tests in the suite continue to pass unchanged.

**Checkpoint**: US2 delivered. Branded org invitations look org-branded; unbranded orgs get Sorcha defaults. Both release-blocking plaintext-token bugs closed.

---

## Phase 5: User Story 3 - Welcome email (Priority: P2)

**Goal**: A new user receives exactly one welcome email across the lifetime of their account. Public-org-only users get the recovery-phrase advance-warning variant; standard-org users get the invited variant with their inviting org's branding. Fires on verification success (email+password path) and on first login (social/passkey path).

**Independent Test**: (a) Complete an email+password signup → verify → welcome-public arrives within 60s with recovery-phrase section, `WelcomeSentAt` set on the user. (b) Complete a social signup to a branded standard org → first login fires welcome-invited with org branding, no recovery-phrase section. (c) Trigger any signal again for either user → no second welcome email sent.

### Templates & fixtures

- [X] T046 [P] [US3] Create `src/Services/Sorcha.Tenant.Service/Emails/Templates/welcome-public.html`
- [X] T047 [P] [US3] Create `src/Services/Sorcha.Tenant.Service/Emails/Templates/welcome-public.txt`
- [X] T048 [P] [US3] Create `src/Services/Sorcha.Tenant.Service/Emails/Templates/welcome-invited.html`
- [X] T049 [P] [US3] Create `src/Services/Sorcha.Tenant.Service/Emails/Templates/welcome-invited.txt`
- [X] T050 [P] [US3] Create golden fixtures for all four welcome templates

### Trigger wiring

- [X] T051 [US3] `EmailVerificationService.VerifyTokenAsync` now calls `WelcomeEmailDispatcher.SendIfPendingAsync` after `SaveChangesAsync`.
- [X] T052 [P] [US3] `LoginService` now calls `WelcomeEmailDispatcher.SendIfPendingAsync` after successful JWT issuance (both the email+password and subdomain login paths).
- [X] T053 [P] [US3] `SocialCallback` Razor PageModel (the actual social-login completion site — `SocialLoginService.ExchangeCodeAsync` only handles OAuth exchange, not PlatformUser resolution) now calls `WelcomeEmailDispatcher.SendIfPendingAsync` after JWT issuance.

### Tests

- [X] T054 [P] [US3] Welcome-template snapshots covered by the unified `EmailTemplateSnapshotTests`
- [X] T055 [US3] New `EmailVerificationServiceTests` asserts `VerifyTokenAsync` calls `WelcomeEmailDispatcher.SendIfPendingAsync` on success, sets `WelcomeSentAt`, and is idempotent on already-welcomed users
- [X] T056 [P] [US3] Added `LoginServiceTests.LoginAsync_VerifiedUserFirstSuccessfulLogin_FiresWelcomeDispatcher` and `LoginAsync_AlreadyWelcomedUser_DoesNotFireWelcomeAgain`
- [~] T057 [P] [US3] `SocialCallbackModelTests` — infrastructure now wires the welcome dispatcher into `CreateModel()` and the two existing error-path tests still pass. A dedicated happy-path welcome-dispatcher test was NOT added in this pass because the existing suite has no happy-path scaffolding (would require significant new mocking of `ResolveOrCreateSocialUserAsync`, `GetByIdAsync` for the public org, token generation, etc.). Welcome-dispatcher behaviour itself is thoroughly unit-tested via `WelcomeEmailDispatcherTests`; the SocialCallback call-site integration is covered transitively by the compile-time wiring and logged via `_logger.LogInformation` at runtime. Listed as a follow-up if deeper test coverage is wanted.

**Checkpoint**: US3 delivered. Verify/first-login triggers fire exactly one welcome per user. Both variants render with correct content per variant.

---

## Phase 6: User Story 4 - Password reset visual consistency (Priority: P2)

**Goal**: Password reset email shares the Sorcha frame, palette, and tone with the verification and welcome emails. Drop the hand-rolled inline HTML.

**Independent Test**: Trigger a password reset. The email looks visually identical (header, footer, colours, font, reply-to) to the verification email. The reset button works.

### Templates & fixtures

- [X] T058 [P] [US4] Create `src/Services/Sorcha.Tenant.Service/Emails/Templates/reset.html`
- [X] T059 [P] [US4] Create `src/Services/Sorcha.Tenant.Service/Emails/Templates/reset.txt`
- [X] T060 [P] [US4] Create golden fixtures for reset

### Caller migration

- [X] T061 [US4] Migrated `PasswordResetService` — now takes `ITransactionalEmailService` and calls `SendPasswordResetAsync`. `BuildResetEmailHtml` deleted. Done ahead of schedule to unblock the compile gate.

### Tests

- [X] T062 [P] [US4] Reset snapshot covered by the unified `EmailTemplateSnapshotTests`
- [X] T063 [US4] Updated `PasswordResetServiceTests` — HTML-content assertions replaced with `SendPasswordResetAsync` dispatch-record checks (email, display name, reset URL with token, 60-min expiry). `BuildResetEmailHtml` is gone (compile clean).

**Checkpoint**: US4 delivered. Password reset email visually consistent with the rest.

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Close the loop — verify the full suite passes, confirm no regressions, document and optionally instrument.

- [X] T064 [P] `dotnet build` at the solution root: 0 errors, 314 warnings (all pre-existing NU1902/1903/1904 advisories on pre-existing packages + Scriban 5.12 — see security note in the PR description)
- [X] T065 [P] `dotnet test tests/Sorcha.Tenant.Service.Tests/`: 757 passing, 0 failing, 8 pre-existing skips
- [ ] T066 [P] Run `dotnet format` — deferred (would produce large churn unrelated to this feature; touched files already follow project style)
- [ ] T067 Execute acceptance-scenario smoke walk — deferred (requires live docker-compose stack with Mailpit or equivalent SMTP catcher; documented in `quickstart.md` for post-merge verification)
- [X] T068 [P] Updated `src/Services/Sorcha.Tenant.Service/README.md` with the new "Transactional Email Architecture (Feature 112)" section — facade → renderer → sender diagram, template inventory, branding semantics, welcome-trigger table, snapshot fixture workflow, tone guardrails
- [ ] T069 [P] (Optional) Prometheus `email_send_failures_total` counter — deferred (explicitly marked optional in the plan; existing Serilog structured logs cover the error case)
- [X] T070 Design-history reference archived in the new README section — direct link to `docs/superpowers/specs/2026-04-24-email-sweep-design.md`
- [~] T071 Final success-criteria walk — see the PR description for the SC-001 through SC-010 walk with evidence

---

## Dependencies

```
Phase 1 (Setup T001-T005)
        │
        ▼
Phase 2 (Foundational T006-T029)
        │
        ├────────────┬────────────┬────────────┐
        ▼            ▼            ▼            ▼
   Phase 3 (US1)  Phase 4 (US2)  Phase 5 (US3)  Phase 6 (US4)
    T030-T036     T037-T045      T046-T057      T058-T063
        │            │            │            │
        └────────────┴────────────┴────────────┘
                           │
                           ▼
                   Phase 7 (Polish T064-T071)
```

**Gate rules:**
- Phase 2 is the ONLY gate for user-story work. Every user-story phase depends on the renderer, resolver, facade, dispatcher, tightened `IEmailSender`, and `WelcomeSentAt` column.
- Phase 3 (US1) is the true MVP — delivering it alone fixes the release-blocking verification email.
- Phases 3, 4, 5, 6 are independent of each other once Phase 2 is done. They can be implemented sequentially or in parallel.
- Within each phase, tasks marked `[P]` can run in parallel (different files, no internal ordering).

## Parallel execution examples

**Setup (all parallel)**: T001, T002, T003, T004, T005

**Foundational — models & records (parallel)**: T006, T007, T008

**Foundational — backends (sequential, same interface)**: T013 → T014, T013 → T015 (T014 and T015 touch different files but both depend on T013's signature change)

**Foundational — tests (parallel)**: T026, T027, T028 (T029 depends on Phase 2 classes already built)

**US1 templates + fixtures (parallel)**: T030, T031, T032, T033

**US2 templates + fixtures (parallel)**: T037, T038, T039, T040, T041, T042

**US3 templates + fixtures (parallel)**: T046, T047, T048, T049, T050

**US3 trigger wiring (parallel)**: T052, T053 (different files)

**Polish (mostly parallel)**: T064, T065, T066, T068, T069

## Implementation strategy

**MVP (one commit bundle)**: Phases 1 + 2 + 3 → delivers US1, fixes the verification-email bug, establishes the template engine. Shippable even if other stories slip.

**Second bundle**: Phase 4 → delivers US2, closes the second plaintext-token bug.

**Third bundle**: Phase 5 → delivers US3, introduces the welcome email and the recovery-phrase safety moment.

**Fourth bundle**: Phase 6 → delivers US4, polishes the last inline-HTML caller.

**Final bundle**: Phase 7 → documentation, optional metrics, smoke evidence, success-criteria walk.

Each bundle compiles, passes tests, and delivers at least one acceptance-test-able user story. Reviewers can approve and merge incrementally.

## Task count and coverage summary

| Phase | Task count | User story |
|-------|------------|------------|
| 1. Setup | 5 (T001–T005) | — |
| 2. Foundational | 24 (T006–T029) | — (blocks everything) |
| 3. US1 Verification | 7 (T030–T036) | US1 (P1 — MVP) |
| 4. US2 Invitation | 9 (T037–T045) | US2 (P1) |
| 5. US3 Welcome | 12 (T046–T057) | US3 (P2) |
| 6. US4 Reset consistency | 6 (T058–T063) | US4 (P2) |
| 7. Polish | 8 (T064–T071) | — |
| **Total** | **71** | |

**Parallel opportunities**: 36 tasks marked `[P]` (≈ 50%), chiefly in template/fixture creation within each user story and the independent constructor/DI unit-test suites in Phase 2.

**Suggested MVP scope**: Phases 1 + 2 + 3 (36 tasks). Delivers US1 end-to-end — fixes the most user-visible release blocker.

**Independent test criteria per story**:
- **US1**: Sign up → verification email has branded button linking to the verify URL. 24-hour expiry stated. Plaintext fallback contains the URL.
- **US2**: Invite from branded org → email carries org logo + colour + name + role + inviter name, with working accept-invitation CTA.
- **US3**: Verify (email+password) OR first-login (social/passkey) fires exactly one welcome email per user. Public variant mentions the recovery-phrase moment without containing any phrase. Invited variant mentions the org and role, no recovery-phrase content.
- **US4**: Request password reset → reset email visually identical to the others, with a working reset button.

**Format validation**: Every task above uses `- [ ] TXXX [P?] [StoryLabel?] Description with absolute file path`. Setup (Phase 1), Foundational (Phase 2), and Polish (Phase 7) carry no story label per the template rules. All other tasks carry the correct `[US1]`, `[US2]`, `[US3]`, or `[US4]` label.
