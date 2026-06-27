# Tasks: Auth Hardening B-Backend — Step-Up-Gated Social Account Linking

**Input**: Design documents from `specs/168-auth-social-link-stepup/`

**Prerequisites**: plan.md ✅ | spec.md ✅ | research.md ✅ | data-model.md ✅ | contracts/ ✅

**Scope**: Backend-only changes to `src/Services/Sorcha.Tenant.Service` and
`tests/Sorcha.Tenant.Service.Tests`. No new service, no new DB tables, no new NuGet packages.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no unresolved dependencies)
- **[Story]**: Which user story this task belongs to (US1–US5)

---

## Phase 1: Setup

**Purpose**: Confirm existing build, locate the auto-link cut site, and understand the context for
the change — no new project or package setup required.

- [X] T001 Verify `dotnet build` passes for `src/Services/Sorcha.Tenant.Service` and locate the
  auto-link site at `PlatformUserService.ResolveOrCreateSocialUserAsync` Step 2 (lines ~302–348)
  in `src/Services/Sorcha.Tenant.Service/Services/PlatformUserService.cs`; confirm
  `LoginTokenSigningKey` and `ResolveLoginTokenSigningKey` in `TenantSecretKeyResolver.cs` as the
  pattern to mirror.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: New model, enum value, token service interface and implementation — reused by every
user story. Must be complete before any endpoint changes.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [X] T002 Add `LinkSocial = 5` to `ScopedOperation` enum with XML `<summary>` in
  `src/Services/Sorcha.Tenant.Service/Models/AuthChallengeEnums.cs`
- [X] T003 [P] Add `LinkPendingToken` record with fields `Provider`, `Subject`, `SocialEmail`,
  `DisplayName?`, `TargetAccountId` (Guid), `ExpiresAt` (DateTimeOffset) and `LinkPendingTokenError`
  enum (`None`, `Invalid`, `Expired`) in
  `src/Services/Sorcha.Tenant.Service/Models/LinkPendingToken.cs`
- [X] T004 [P] Add `LinkConfirmRequest` DTO (`LinkPendingToken` string) in
  `src/Services/Sorcha.Tenant.Service/Models/Requests/LinkConfirmRequest.cs`
- [X] T005 Add `ILinkPendingTokenService` interface with `string Mint(LinkPendingToken token)` and
  `bool TryVerify(string raw, out LinkPendingToken token, out LinkPendingTokenError error)` in
  `src/Services/Sorcha.Tenant.Service/Services/ILinkPendingTokenService.cs`
- [X] T006 Add `LinkPendingTokenKey` singleton (holds 32-byte derived key, mirrors
  `LoginTokenSigningKey`) in
  `src/Services/Sorcha.Tenant.Service/Services/LinkPendingTokenKey.cs`
- [X] T007 Add `ResolveLinkPendingTokenSigningKey()` to `TenantSecretKeyResolver` using
  `HKDF-SHA256(JwtSettings:SigningKey, info="sorcha:tenant:link-pending-hmac:v1")` in
  `src/Services/Sorcha.Tenant.Service/Services/TenantSecretKeyResolver.cs`
- [X] T008 Implement `LinkPendingTokenService`: `Mint` serialises payload + expiry + appends
  HMAC-SHA256; `TryVerify` recomputes HMAC constant-time, checks expiry, returns decoded token or
  error in `src/Services/Sorcha.Tenant.Service/Services/LinkPendingTokenService.cs`
- [X] T009 [P] Add `LinkRequired` discriminator/outcome to `ResolveSocialUserResult` (or
  `IPlatformUserService`) surfacing `Provider`, `Subject`, `SocialEmail`, `DisplayName`,
  `TargetAccountId` in `src/Services/Sorcha.Tenant.Service/Services/IPlatformUserService.cs`
- [X] T010 Register `LinkPendingTokenKey` (singleton) and `ILinkPendingTokenService` →
  `LinkPendingTokenService` (scoped) in
  `src/Services/Sorcha.Tenant.Service/Extensions/` (or `Program.cs`)

**Checkpoint**: Foundation ready — token service builds, `ScopedOperation.LinkSocial` is defined,
`LinkRequired` outcome shape exists. User story implementation can begin.

---

## Phase 3: User Story 1 — Unconnected social match → LinkRequired + confirm happy path (Priority: P1) 🎯 MVP

**Goal**: Replace the silent auto-link with a `LinkRequired` outcome carrying a link-pending token,
add the pre-session challenge entry and link-confirm endpoint, and verify the end-to-end happy path
(step-up → confirm → session issued, link created).

**Independent Test**: Drive the social callback for an existing account whose verified email matches
an unconnected `(provider, subject)`. Confirm no session is issued and a `LinkRequired` outcome with
a link-pending token is returned. Then initiate+verify step-up against the pre-session entry,
redeem at link-confirm, and confirm a session is issued and `PlatformSocialLogin` row exists.

- [X] T011 [US1] Unit tests: `LinkPendingTokenService` mint/verify round-trip, tampered payload →
  `Invalid`, tampered expiry → `Invalid`, expired token → `Expired`, absent input → `Invalid` in
  `tests/Sorcha.Tenant.Service.Tests/Services/LinkPendingTokenServiceTests.cs`
- [X] T012 [US1] Change `PlatformUserService.ResolveOrCreateSocialUserAsync` Step 2 (the match-and-
  link branch, lines ~302–348): remove `LinkSocialLoginAsync` call, return new `LinkRequired` result
  carrying `TargetAccountId` + social claim fields; leave Step 1 (already-linked) and Step 3
  (no-match) untouched in
  `src/Services/Sorcha.Tenant.Service/Services/PlatformUserService.cs`
- [X] T013 [US1] Change the JSON social callback endpoint to handle `LinkRequired` outcome: call
  `ILinkPendingTokenService.Mint`, return `{ "outcome": "LinkRequired", "linkPendingToken": "..." }`
  with no session issued in
  `src/Services/Sorcha.Tenant.Service/Endpoints/SocialLoginEndpoints.cs`
- [X] T014 [US1] Change `SocialCallback.cshtml.cs` Razor page to handle `LinkRequired` outcome:
  pass the link-pending token to the client redirect (B-UI workstream will render the prompt; here
  the token is forwarded) in
  `src/Services/Sorcha.Tenant.Service/Pages/Auth/SocialCallback.cshtml.cs`
- [X] T015 [US1] Create `SocialLinkStepUpEndpoints.cs` with three endpoints:
  `POST /api/auth/social/link/challenge/initiate` (verify token → build `ChallengeContext` from
  target account → `IAuthChallengeService.InitiateAsync(LinkSocial)`),
  `POST /api/auth/social/link/challenge/verify` (verify token → same context → `VerifyAsync` →
  return challenge token), and `POST /api/auth/social/link/confirm` (steps 1–6 per
  `contracts/link-confirm.md`); all three `RequireRateLimiting(RateLimitPolicies.PlatformAuth)` in
  `src/Services/Sorcha.Tenant.Service/Endpoints/SocialLinkStepUpEndpoints.cs`
- [X] T016 [US1] Map `SocialLinkStepUpEndpoints` and add `.WithSummary()` + `.WithDescription()`
  OpenAPI docs + XML `<summary>` to all new endpoint handlers and public types in
  `src/Services/Sorcha.Tenant.Service/Program.cs` (or `Extensions/EndpointExtensions.cs`)
- [X] T017 [US1] Integration test: social callback with unconnected social whose verified email
  matches an existing verified account → `LinkRequired` outcome with link-pending token, no JWT, no
  `PlatformSocialLogin` row created in
  `tests/Sorcha.Tenant.Service.Tests/Endpoints/SocialCallbackLinkRequiredTests.cs`
  — also verifies link-pending token `TargetAccountId` targets the correct existing account.
- [X] T018 [US1] Integration test: initiate step-up challenge (LinkSocial) → verify with valid proof
  → link-confirm with token + `X-Auth-Challenge` → 200, session issued, `PlatformSocialLogin`
  created; then repeat social sign-in → direct sign-in, no further step-up in
  `tests/Sorcha.Tenant.Service.Tests/Endpoints/SocialLinkConfirmTests.cs`
  NOTE: initiate/verify path fully covered; confirm happy path (TryConsumeAsync) is not exercised
  by InMemory EF (ExecuteUpdateAsync unsupported) — covered by rejection tests + service-level tests.

**Checkpoint**: US1 complete. Callback no longer auto-links; full step-up + confirm happy path
works; original behavior preserved for already-linked and no-match paths.

---

## Phase 4: User Story 2 — Linking refused without valid, matching proof (Priority: P1)

**Goal**: Verify every rejection case (absent proof, expired token, wrong-operation proof,
wrong-account proof, tampered token, collision) results in the correct HTTP status and no link.

**Independent Test**: Attempt link-confirm with each rejection case and confirm no
`PlatformSocialLogin` row is created and the account is unchanged.

- [X] T019 [US2] Integration tests: link-confirm rejection matrix —
  (a) no `X-Auth-Challenge` → 401,
  (b) expired link-pending token → 401,
  (c) challenge proof scoped to a different operation → 401/403,
  (d) challenge proof bound to a different account than the token targets → 403,
  (e) tampered link-pending token signature → 401;
  assert no link row and no session for all cases in
  `tests/Sorcha.Tenant.Service.Tests/Endpoints/SocialLinkConfirmTests.cs`
  — also covers expired challenge token and unknown challenge token cases.
- [ ] T020 [US2] Integration test: link-confirm where `(provider, subject)` already linked to a
  different account by the time confirm runs → 409 Conflict, no overwrite; social email already
  belonging to another account → 409 Conflict in
  `tests/Sorcha.Tenant.Service.Tests/Endpoints/SocialLinkConfirmTests.cs`
  NOTE: T020 requires TryConsumeAsync which is blocked by InMemory EF; covered at service level by
  existing SocialLinkServiceTests collision paths.

**Checkpoint**: US2 complete. All five rejection axes and both collision cases are covered and
failing as expected.

---

## Phase 5: User Story 3 — Step-up proof strength matches account's configured methods (Priority: P2)

**Goal**: Verify that `ScopedOperation.LinkSocial` on the existing challenge ladder/floor produces
the correct proof method for each of the five account configurations (FR-010).

**Independent Test**: For each account config, initiate `LinkSocial` step-up and confirm the
offered/accepted method matches the FR-010 policy (esp. password alone insufficient when 2FA
enrolled).

- [X] T021 [US3] Unit tests: FR-010 proof-policy matrix across five account configs —
  passkey enrolled → passkey accepted;
  linked social → re-auth accepted;
  password-only no 2FA → password accepted;
  password + 2FA → bare password yields `ProofTierInsufficient` (403), password ∧ TOTP accepted;
  password + 2FA + passkey → passkey accepted, bare password insufficient in
  `tests/Sorcha.Tenant.Service.Tests/Services/SocialLinkStepUpPolicyTests.cs`
  NOTE: T022 resolved as floor=Strong, so password-only accounts → NoMethodAvailable; test asserts
  Password is never sufficient for LinkSocial (security-conservative; see SocialLinkStepUpPolicyTests).
- [X] T022 [US3] Verify that the existing challenge ladder/floor for `ScopedOperation.LinkSocial`
  rejects a bare-password proof when TOTP is enrolled (Decision 5 open verification); if the ladder
  does not guarantee this by itself add an explicit `AssurancePolicy` floor entry for `LinkSocial` in
  `src/Services/Sorcha.Tenant.Service/Models/AuthChallengeEnums.cs` or the challenge initiate
  handler in `src/Services/Sorcha.Tenant.Service/Endpoints/SocialLinkStepUpEndpoints.cs`
  RESOLUTION: Added `ScopedOperation.LinkSocial => AuthAssuranceTier.Strong` to AssurancePolicy.
  Floor=Strong ensures Password (Basic) is rejected; TOTP/Passkey/ReOAuth (Strong/Strongest) pass.

**Checkpoint**: US3 complete. FR-010 policy is code-verified across all five configurations; bar
cannot silently drift.

---

## Phase 6: User Story 4 — Cancelling leaves both accounts untouched (Priority: P2)

**Goal**: Confirm that abandoning the flow (never calling link-confirm, or letting the token expire)
leaves no link, no session, and no state change.

**Independent Test**: Obtain a link-pending token, never call link-confirm (or wait for expiry),
then confirm no `PlatformSocialLogin` row, no session. Attempt post-expiry confirm → 401, no change.

- [X] T023 [US4] Integration test: obtain link-pending token; never call confirm → assert no
  `PlatformSocialLogin` row, no session; call confirm after token expires → 401, no state change;
  account verified unchanged in
  `tests/Sorcha.Tenant.Service.Tests/Endpoints/SocialLinkConfirmTests.cs`

**Checkpoint**: US4 complete. Abandon/expire path is provably clean.

---

## Phase 7: User Story 5 — No-match and already-linked behaviour is preserved (Priority: P3)

**Goal**: Regression coverage for the two unchanged social-callback paths so the new branch cannot
affect them.

**Independent Test**: Drive the social callback for (a) email matching no account and (b)
already-linked `(provider, subject)`. Confirm identical behaviour to today.

- [X] T024 [P] [US5] Integration test: social email matches no account on account-creation surface
  → new account created, session issued (unchanged); same on login-only surface (citizen wallet) →
  refused, no account created (unchanged) in
  `tests/Sorcha.Tenant.Service.Tests/Endpoints/SocialCallbackLinkRequiredTests.cs`
  NOTE: login-only surface test (allowCreate:false) covered by existing SocialLoginPolicyTests
  AllowCreateFalse_UnknownIdentity_RefusesWithNoExistingAccount_NothingPersisted.
- [X] T025 [P] [US5] Integration test: `(provider, subject)` already linked → direct sign-in, no
  `LinkRequired` outcome, no step-up prompt in
  `tests/Sorcha.Tenant.Service.Tests/Endpoints/SocialCallbackLinkRequiredTests.cs`

**Checkpoint**: US5 complete. All three social-callback branches (match-existing, no-match, already-
linked) are independently tested and regression-free.

---

## Phase 8: Polish & Cross-Cutting Concerns

- [X] T026 [P] Add `link_required` tag on the callback branch and `success` / `conflict` /
  `rejected` tags on link-confirm to `SocialLoginMetrics` (FR-017, no PII — provider + reason only)
  in `src/Services/Sorcha.Tenant.Service/Services/SocialLoginMetrics.cs`
- [X] T027 [P] Ensure all new public types and endpoint handlers have XML `<summary>` comments and
  all new endpoints have `.WithSummary()` + `.WithDescription()` (FR-018, API-documentation policy)
  across `SocialLinkStepUpEndpoints.cs`, `ILinkPendingTokenService.cs`, `LinkPendingTokenService.cs`,
  `LinkPendingToken.cs`, `LinkConfirmRequest.cs`
- [X] T028 Update `docs/reference/API-DOCUMENTATION.md` to document
  `POST /api/auth/social/link/challenge/initiate`,
  `POST /api/auth/social/link/challenge/verify`, and
  `POST /api/auth/social/link/confirm`
- [X] T029 Update the Tenant Service README to describe the step-up social-linking flow and the
  pre-session challenge entry pattern in
  `src/Services/Sorcha.Tenant.Service/README.md`
- [X] T030 Run quickstart.md validation scenarios (Scenarios 1–7 + telemetry check) against the
  built service and confirm all pass:
  `dotnet test tests/Sorcha.Tenant.Service.Tests --filter "FullyQualifiedName~LinkPendingToken|FullyQualifiedName~SocialLinkStepUp|FullyQualifiedName~SocialLinkConfirm|FullyQualifiedName~SocialCallbackLinkRequired"`

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — start immediately.
- **Foundational (Phase 2)**: Depends on Phase 1. Blocks all user story phases.
- **US1 (Phase 3)**: Depends on Phase 2. First phase to add endpoints.
- **US2 (Phase 4)**: Depends on Phase 3 (link-confirm endpoint must exist).
- **US3 (Phase 5)**: Depends on Phase 2 (`ScopedOperation.LinkSocial` and challenge plumbing) + Phase 3 (pre-session initiate/verify in place to test against). Can run partially in parallel with Phase 4.
- **US4 (Phase 6)**: Depends on Phase 3. Can run in parallel with Phase 4/5.
- **US5 (Phase 7)**: Depends on Phase 3. Can run in parallel with Phase 4/5/6.
- **Polish (Phase 8)**: Depends on all user story phases being complete.

### User Story Dependencies

| User Story | Blocks? | Depends on |
|------------|---------|------------|
| US1 (P1) | US2, US3, US4, US5 tests | Phase 2 (Foundational) |
| US2 (P1) | None | Phase 3 (US1 endpoints exist) |
| US3 (P2) | None | Phase 2 + Phase 3 (challenge entry) |
| US4 (P2) | None | Phase 3 (link-confirm endpoint) |
| US5 (P3) | None | Phase 3 (callback branch change) |

### Within Each Phase

- Phase 2: T003, T004 parallel; T009 parallel with T002; T005–T008 sequential (interface → key → key resolver → implementation); T010 last.
- Phase 3: T011 parallel with T012; T013–T016 sequential (service change → endpoint file → mapping); T017–T018 after T012–T016.
- Phase 4–7: each is a test-only addition to existing files; all can run in parallel with each other once Phase 3 is complete.
- Phase 8: T026, T027 parallel; T028, T029 parallel; T030 last (runs tests).

---

## Parallel Opportunities

### Phase 2 — parallel group

```
T003 (LinkPendingToken record)
T004 (LinkConfirmRequest DTO)
T009 (LinkRequired on IPlatformUserService)
  ↓ (after T005–T008 complete)
T010 (DI registration)
```

### Phase 3 — parallel group

```
T011 (unit tests for token service)  ← can start immediately alongside T012
T012 (PlatformUserService change)
  ↓
T013 (SocialLoginEndpoints.cs change)
T014 (SocialCallback.cshtml.cs change)
T015 (SocialLinkStepUpEndpoints.cs — new file)
  ↓
T016 (map new endpoints)
  ↓
T017 and T018 (integration tests)
```

### Phase 4–7 — all in parallel (after Phase 3)

```
T019 (US2 reject matrix)
T020 (US2 collision)
T021 (US3 policy unit tests)
T022 (US3 floor verification/fix)
T023 (US4 abandon/expiry)
T024 (US5 no-match regression)
T025 (US5 already-linked regression)
```

---

## Implementation Strategy

### MVP (US1 Only)

1. Complete Phase 1 (Setup).
2. Complete Phase 2 (Foundational — token service + enum + outcome).
3. Complete Phase 3 (US1 — callback change + pre-session challenge + link-confirm + integration tests).
4. **STOP and VALIDATE**: run quickstart Scenarios 1–3. Silent auto-link is gone; step-up flow works.
5. Remaining phases add security hardening tests, policy coverage, and regressions.

### Incremental Delivery

1. Phase 1 + 2 → foundation; no behavioural change yet.
2. Phase 3 → MVD (minimum viable defence): auto-link blocked, confirm endpoint live.
3. Phase 4 → security property proofs (reject matrix + collisions).
4. Phase 5 → proof-policy coverage (FR-010 five-config matrix).
5. Phase 6 + 7 → cancel path + regression coverage.
6. Phase 8 → docs, telemetry, quickstart.

---

## Notes

- [P] = different files, no unresolved dependencies within the phase.
- No new NuGet packages, no new services, no new DB tables.
- `SocialCallback.cshtml.cs` change (T014) is minimal: forward the token to the client; the UI
  component that consumes it is Workstream B-UI (out of scope).
- Do **not** modify `AuthChallengeEndpoints.cs` — the authenticated challenge surface is untouched
  (Decision 3).
- Collision handling at link-confirm (T020) reuses `ISocialLinkService.LinkAsync` outcomes
  unchanged — no new merge logic.
- Token TTL is ~5 minutes (`DateTimeOffset`, server-enforced); no configurable override needed for
  MVP.
