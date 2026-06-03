---
description: "Task list for Authorization-gap closure"
---

# Tasks: Authorization-gap closure

**Input**: Design documents from `/specs/147-authorization-gap-closure/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/authorization-matrix.md, quickstart.md

**Tests**: TDD is REQUIRED for this feature (per spec + design). Each story writes failing tests first.

**Organization**: Grouped by user story. Stories are independent and map 1:1 to the four findings (US1=H1, US2=H2, US3=F124, US4=LOW). Delivery is one PR with one commit per story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on an incomplete task)
- **[Story]**: US1 / US2 / US3 / US4

## Path Conventions

Multi-service repo. Production code under `src/Services/Sorcha.{Wallet,Blueprint,Tenant}.Service/`; tests under `tests/Sorcha.{...}.Service.Tests/`.

---

## Phase 1: Setup

**Purpose**: Establish a green TDD baseline so the new failing tests are meaningfully red. No project initialization needed (existing solution).

- [x] T001 Establish baseline: build `tests/Sorcha.Wallet.Service.Tests`, `tests/Sorcha.Blueprint.Service.Tests`, and `tests/Sorcha.Tenant.Service.Tests` to confirm they compile and pass before changes (MTP runs whole projects; `--filter` is ignored).

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Shared prerequisites for all stories.

**None.** There is no shared new infrastructure — each story introduces only service-local types and reuses the existing `Sorcha.ServiceDefaults.Auth` primitives (`SorchaAudiences`, `HasTierAudience`) and the `AuthorizationPolicies` constants already registered by `AddSorchaAuthorizationPolicies`. User stories may begin immediately after Setup.

---

## Phase 3: User Story 1 - Validator signing keys cannot be seated by unauthorized callers (Priority: P1) 🎯 MVP

**Goal**: H1 — drop `.AllowAnonymous()` on the system-wallet endpoints; `create` requires a service token; `recover` requires `:service` OR `Administrator`+`:platform`; keep the 409-on-exists guard.

**Independent Test**: Evaluate `CanRecoverSystemWallet` against the caller matrix and assert the two endpoints carry no `AllowAnonymous` and require their policies — see `contracts/authorization-matrix.md` (H1 rows).

### Tests for User Story 1 (write first, must FAIL) ⚠️

- [x] T002 [P] [US1] Policy-evaluation tests for `CanRecoverSystemWallet` in `tests/Sorcha.Wallet.Service.Tests/Authorization/WalletAuthorizationPolicyTests.cs` — build a provider via `services.AddLogging(); services.AddWalletAuthorization();` (mirror `AuthorizationPolicyExtensionsTests`) and assert: service(`token_type=service`+`sorcha:service`)→allow; admin role+`sorcha:platform`→allow; consumer(`sorcha:consumer`)→deny; admin role+`sorcha:consumer`→deny; authenticated non-admin no-audience→deny; unauthenticated→deny. (Fails: policy does not exist yet.)
- [x] T003 [P] [US1] Endpoint-metadata regression tests in `tests/Sorcha.Wallet.Service.Tests/Endpoints/SystemWalletEndpointAuthorizationTests.cs` — map the wallet endpoints onto a minimal host, enumerate `EndpointDataSource`, and assert `POST /api/v1/wallets/system` and `/system/recover` carry **no** `IAllowAnonymous` metadata and an `IAuthorizeData` with policy `RequireService` and `CanRecoverSystemWallet` respectively. (Fails: endpoints currently `AllowAnonymous`.)

### Implementation for User Story 1

- [x] T004 [P] [US1] Create `SystemWalletRecoveryRequirement : IAuthorizationRequirement` (marker, XML-documented) in `src/Services/Sorcha.Wallet.Service/Authorization/SystemWalletRecoveryRequirement.cs`.
- [x] T005 [US1] Create `SystemWalletRecoveryAuthorizationHandler : AuthorizationHandler<SystemWalletRecoveryRequirement>` in `src/Services/Sorcha.Wallet.Service/Authorization/SystemWalletRecoveryAuthorizationHandler.cs` — inject `SorchaAudiences`; succeed iff (`token_type==service` AND `HasTierAudience(Service)`) OR ((`IsInRole("Administrator")`||`IsInRole("SystemAdmin")`) AND `HasTierAudience(Platform)`); never `Fail()`. (Depends on T004.)
- [x] T006 [US1] Register the handler (`AddSingleton<IAuthorizationHandler, SystemWalletRecoveryAuthorizationHandler>`) and define policy `CanRecoverSystemWallet` (`RequireAuthenticatedUser().AddRequirements(new SystemWalletRecoveryRequirement())`) in `src/Services/Sorcha.Wallet.Service/Extensions/AuthenticationExtensions.cs`. (Depends on T004, T005.)
- [x] T007 [US1] In `src/Services/Sorcha.Wallet.Service/Endpoints/WalletEndpoints.cs`: remove `.AllowAnonymous()` from `/system` and `/system/recover`; add `.RequireAuthorization(AuthorizationPolicies.RequireService)` to create and `.RequireAuthorization("CanRecoverSystemWallet")` to recover; update the misleading inline comments; leave the 409-on-exists guard untouched. (Depends on T006.)
- [x] T008 [US1] Build + run `tests/Sorcha.Wallet.Service.Tests` to green; commit `feat(147): H1 gate system-wallet create/recover (close AllowAnonymous)`.

**Checkpoint**: System-wallet create/recover are gated; tests green.

---

## Phase 4: User Story 2 - Blueprint and schema authoring is closed to non-platform callers (Priority: P1)

**Goal**: H2 — redefine `CanManageBlueprints` as (service+`:service`) OR (org_id+`:platform`) in the policy definition, fixing all bare authoring endpoints with no per-endpoint edits.

**Independent Test**: Evaluate `CanManageBlueprints` against the caller matrix — see `contracts/authorization-matrix.md` (H2 rows). Consumer-with-org denied; platform-with-org and service allowed.

### Tests for User Story 2 (write first, must FAIL) ⚠️

- [x] T009 [P] [US2] Policy-evaluation tests for `CanManageBlueprints` in `tests/Sorcha.Blueprint.Service.Tests/Authorization/BlueprintManagementPolicyTests.cs` — build a provider via `services.AddLogging(); services.AddBlueprintAuthorization();` and assert: consumer(`org_id`+`sorcha:consumer`)→deny; platform(`org_id`+`sorcha:platform`)→allow; service(`token_type=service`+`sorcha:service`)→allow; platform no-`org_id`→deny; `org_id` but no tier audience→deny. (Fails: current policy allows consumer-with-org.)

### Implementation for User Story 2

- [x] T010 [P] [US2] Create `BlueprintManagementRequirement : IAuthorizationRequirement` (marker, XML-documented) in `src/Services/Sorcha.Blueprint.Service/Authorization/BlueprintManagementRequirement.cs`.
- [x] T011 [US2] Create `BlueprintManagementAuthorizationHandler : AuthorizationHandler<BlueprintManagementRequirement>` in `src/Services/Sorcha.Blueprint.Service/Authorization/BlueprintManagementAuthorizationHandler.cs` — inject `SorchaAudiences`; succeed iff (`token_type==service` AND `HasTierAudience(Service)`) OR (non-empty `org_id` AND `HasTierAudience(Platform)`); never `Fail()`. (Depends on T010.)
- [x] T012 [US2] In `src/Services/Sorcha.Blueprint.Service/Extensions/AuthenticationExtensions.cs`: register the handler and redefine `CanManageBlueprints` to `policy.AddRequirements(new BlueprintManagementRequirement())` (replacing the `hasOrgId OR isService` assertion). Leave `RehearsalEndpoints`/`BlueprintFromPublishedEndpoint` (which also compose `RequirePlatformAudience`) untouched. (Depends on T010, T011.)
- [x] T013 [US2] Build + run `tests/Sorcha.Blueprint.Service.Tests` to green; commit `feat(147): H2 fold platform-audience gate into CanManageBlueprints`.

**Checkpoint**: Consumer tokens cannot reach blueprint/schema/credential/status-list authoring; service + platform-admin unchanged.

---

## Phase 5: User Story 3 - A citizen's pending-application notice is reachable only by that citizen (Priority: P3)

**Goal**: F124 — pending-applications group requires consumer-tier.

**Independent Test**: Assert the pending-applications group requires `RequireConsumerAudience` (platform token denied) — `contracts/authorization-matrix.md` (F124 rows).

### Tests for User Story 3 (write first, must FAIL) ⚠️

- [ ] T014 [P] [US3] Endpoint-metadata test in `tests/Sorcha.Wallet.Service.Tests/Endpoints/PendingApplicationAuthorizationTests.cs` — assert the `/api/v1/wallet/pending-applications` group endpoints carry an `IAuthorizeData` with policy `RequireConsumerAudience`. (Fails: currently plain `RequireAuthorization()`.)

### Implementation for User Story 3

- [ ] T015 [US3] In `src/Services/Sorcha.Wallet.Service/Endpoints/PendingApplicationEndpoints.cs`: change the group `.RequireAuthorization()` to `.RequireAuthorization(AuthorizationPolicies.RequireConsumerAudience)`.
- [ ] T016 [US3] Build + run `tests/Sorcha.Wallet.Service.Tests` to green; commit `fix(147): F124 require consumer audience on pending-applications`.

**Checkpoint**: Pending-application notice is consumer-only.

---

## Phase 6: User Story 4 - Platform-administration requires system-admin-org membership (Priority: P3)

**Goal**: LOW — delete Tenant's duplicate role-only `RequireSystemAdmin` so the shared org-scoped definition stands.

**Independent Test**: Evaluate `RequireSystemAdmin` (via `AddTenantAuthorization`) — `contracts/authorization-matrix.md` (LOW rows). Non-system-org SystemAdmin denied; system-admin-org SystemAdmin allowed.

### Tests for User Story 4 (write first, must FAIL) ⚠️

- [ ] T017 [P] [US4] Policy-evaluation tests in `tests/Sorcha.Tenant.Service.Tests/Authorization/TenantSystemAdminPolicyTests.cs` — build a provider via `services.AddLogging(); services.AddTenantAuthorization();` and assert: SystemAdmin in `00000000-0000-0000-0000-000000000001`→allow; SystemAdmin in a different org→deny; non-SystemAdmin in the system-admin org→deny. (Fails: Tenant override is role-only, so non-system-org SystemAdmin currently allowed.)

### Implementation for User Story 4

- [ ] T018 [US4] In `src/Services/Sorcha.Tenant.Service/Extensions/AuthenticationExtensions.cs`: delete the duplicate `options.AddPolicy("RequireSystemAdmin", policy => policy.RequireRole("SystemAdmin"))` so the shared org-scoped definition from `AddSorchaAuthorizationPolicies` wins. (FR-010 verified: all four Tenant usages already compose `RequirePlatformAudience` and are platform-management endpoints.)
- [ ] T019 [US4] Build + run `tests/Sorcha.Tenant.Service.Tests` to green; commit `fix(147): LOW restore org-scope on Tenant RequireSystemAdmin`.

**Checkpoint**: All four findings closed.

---

## Phase 7: Polish & Cross-Cutting

- [ ] T020 [P] Doc sync: update the `jwt` skill policy catalogue (`.claude/skills/jwt/`) — add `CanRecoverSystemWallet`, note `CanManageBlueprints` is now tier-aware (service OR platform+org), and that Tenant `RequireSystemAdmin` is org-scoped (duplicate removed). Update `docs/guides/AUTHENTICATION-SETUP.md` if it enumerates these policies.
- [ ] T021 Push branch `147-authorization-gap-closure`; open PR (`gh pr create`) referencing the design + spec; confirm `claude-review` is green (full-solution `build-and-test` stays red on the unrelated Refit cert infra issue — claude-review is the gate); merge on green.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: none — start immediately.
- **Foundational (Phase 2)**: none.
- **User Stories (Phases 3-6)**: each depends only on Setup. US1 and US2 touch different services and are fully parallelizable. US3 shares the Wallet test project with US1 (different files) but has no logic dependency. US4 is independent (Tenant).
- **Polish (Phase 7)**: after all stories complete.

### Within Each User Story

- Tests first, confirmed failing, before implementation.
- Requirement type before handler before policy registration before endpoint wiring.
- Build + green + commit closes the story.

### Parallel Opportunities

- US1 (Wallet) and US2 (Blueprint) can be implemented fully in parallel.
- Within a story, the marked `[P]` test/requirement tasks (e.g. T002+T003, T004) can run together.

---

## Implementation Strategy

### MVP (highest-impact first)

1. Phase 1 Setup (baseline green).
2. Phase 3 US1 (H1) — the highest-impact gap (validator signing key). STOP and validate.
3. Phase 4 US2 (H2) — close consumer access to authoring.
4. Phases 5-6 US3 + US4 — the two LOW-severity same-theme gaps.
5. Phase 7 — doc sync + PR.

### Delivery

One PR (`147-authorization-gap-closure`), one commit per story (T008, T013, T016, T019), each built + tested against its service's whole test project before committing. Merge on green `claude-review`.

---

## Notes

- `[P]` = different files, no dependency on an incomplete task.
- MTP runs whole test projects; do not rely on `--filter`.
- The handlers never call `Fail()` (compose-safe). Audience checks always go through `SorchaAudiences`/`HasTierAudience` (no literal audience strings) — FR-012.
- No behaviour change for legitimate callers (service create, admin-CLI recover, platform authoring, consumer pending-apps) — FR-004 / FR-007.
