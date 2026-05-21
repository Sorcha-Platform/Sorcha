---
description: "Task list for Tiered-Audience JWT Identity Model + Issuer Hardening (Spec A)"
---

# Tasks: Tiered-Audience JWT Identity Model + Issuer Hardening (Spec A)

**Input**: Design documents from `/specs/136-jwt-audience-tiers/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/

**Tests**: INCLUDED. Constitution IV mandates >85% coverage for new code, and the feature's success criteria (SC-002, SC-003, SC-004, SC-006, SC-007) are themselves boundary assertions — they are written test-first.

**Organization**: Grouped by user story (US1–US5 from spec.md), in priority order.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on incomplete tasks)
- **[Story]**: US1–US5 (user-story phases only)

## Path Conventions

Multi-service .NET platform. Shared primitives in `src/Common/Sorcha.ServiceDefaults/`; issuance in `src/Services/Sorcha.Tenant.Service/`; validation/classification across each service; tests in `tests/*`.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Test scaffolding the rest of the work relies on.

- [X] T001 [P] Ensure `tests/Sorcha.ServiceDefaults.Tests` project exists (create with xUnit + FluentAssertions + a ProjectReference to `Sorcha.ServiceDefaults` if absent) for the audience/policy/issuer unit tests. — already present.
- [ ] T002 [P] Add a shared test token-builder helper `tests/Sorcha.Testing/TieredTokenFactory.cs` that mints an HMAC JWT for a given `(installationName, Tier, claims)` so every test project can construct tier-scoped tokens deterministically.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The shared issuance/validation engine. **No user story can be implemented until this phase is complete.**

- [X] T003 Create `Tier` enum + `SorchaAudiences` (single source of truth: `For(Tier)`, `All`, `TierFor`, prefix from `InstallationName` default `sorcha`, normalised trim+lowercase) in `src/Common/Sorcha.ServiceDefaults/Auth/SorchaAudiences.cs`.
- [X] T004 [P] Unit tests for `SorchaAudiences` (default `sorcha`, override, normalisation, all four suffixes, `All` set, `TierFor` round-trip) in `tests/Sorcha.ServiceDefaults.Tests/Auth/SorchaAudiencesTests.cs` — 8 tests, green.
- [ ] T005 Wire bearer validation to `ValidAudiences = SorchaAudiences.All` and make `InstallationName` (default `sorcha`) drive the audience namespace in `src/Common/Sorcha.ServiceDefaults/JwtAuthenticationExtensions.cs` (do NOT touch issuer resolution here — that is US3/T029).
- [ ] T006 Add authorization policy definitions `RequireConsumerAudience` and `RequirePlatformAudience`, and extend `RequireService` to also assert `aud == SorchaAudiences.For(Service)` (keep the existing `token_type==service` check), in `src/Common/Sorcha.ServiceDefaults/AuthorizationPolicyExtensions.cs`.
- [ ] T007 [P] Unit tests for the three policies (allow/deny by `aud`; `RequireService` requires both `aud` and `token_type`) in `tests/Sorcha.ServiceDefaults.Tests/Auth/TierPolicyTests.cs`.
- [ ] T008 Add the `Sorcha.Identity` OTel meter with `sorcha_token_minted_total{tier}` and `sorcha_tier_request_rejected_total{requested,reason}` in `src/Common/Sorcha.ServiceDefaults/Auth/IdentityMetrics.cs`; register on the meter provider.
- [ ] T009 Implement `TierResolver` (`mintedTier = requestedTier ?? Consumer`, then `∩ entitledTiers(user, activeContext)`; reject over-request, never downgrade; entitlement = Consumer for all, Platform iff platform role in active context) in `src/Services/Sorcha.Tenant.Service/Services/TierResolver.cs`; emit the rejected-counter on over-request.
- [ ] T010 [P] Unit tests for `TierResolver` (default consumer; platform only with platform role; over-request rejected; per active-context entitlement) in `tests/Sorcha.Tenant.Service.Tests/Services/TierResolverTests.cs`.
- [ ] T011 Make `TokenService` tier-aware: accept a `Tier`, stamp `aud = SorchaAudiences.For(tier)`, and shape the per-tier claim set — consumer omits `org_id`/roles; platform keeps existing shape; refresh token carries a `tier` claim — in `src/Services/Sorcha.Tenant.Service/Services/TokenService.cs`; increment `sorcha_token_minted_total{tier}`.
- [ ] T012 [P] Unit tests for `TokenService` per-tier claim sets (consumer has `platform_user_id` and NO `org_id`/roles; platform full; refresh preserves tier) in `tests/Sorcha.Tenant.Service.Tests/Services/TokenServiceTierTests.cs`.
- [ ] T013 Remove the dead `JwtAudiences.CitizenWallet` constant and update references in `src/Common/Sorcha.CitizenWallet.Abstractions/Constants/JwtAudiences.cs` (and any usages) to `SorchaAudiences`.
- [ ] T014 Add a reusable fallback "default to platform tier" authorization convention (an endpoint with no explicit tier policy resolves to `RequirePlatformAudience`) in `src/Common/Sorcha.ServiceDefaults/AuthorizationPolicyExtensions.cs` (e.g. set as the `FallbackPolicy` or a documented convention applied per host).

**Checkpoint**: Engine ready — audiences, policies, resolver, tier-aware issuance, metrics all in place and unit-tested.

---

## Phase 3: User Story 1 - Trust-tier isolation (consumer ⊥ platform) (Priority: P1) 🎯 MVP

**Goal**: A consumer token cannot reach platform/admin surfaces and a platform token cannot reach consumer surfaces, enforced at the token layer independent of roles.

**Independent Test**: Mint a consumer token and a platform token for the same person; confirm the consumer token is refused at every platform endpoint and the platform token at every consumer endpoint.

### Tests for User Story 1 (write first, must fail)

- [ ] T015 [P] [US1] Integration test: a `:consumer` token is refused (403) at a representative platform endpoint and a `:platform` token is refused at a representative consumer endpoint, in `tests/Sorcha.Tenant.Service.Tests/Integration/TierIsolationTests.cs`.
- [ ] T016 [P] [US1] Integration test: an endpoint with no explicit tier policy refuses a `:consumer` token (defaults to platform), in `tests/Sorcha.Tenant.Service.Tests/Integration/UnclassifiedEndpointDefaultTests.cs`.

### Implementation for User Story 1

- [ ] T017 [US1] Wire the interactive login path (`/api/auth/login`) to derive the requested tier from `returnTo` (consumer host/`/wallet` ⇒ Consumer; admin/platform ⇒ Platform; default Consumer) and call `TierResolver` → `TokenService`, in `src/Services/Sorcha.Tenant.Service/Endpoints/AuthEndpoints.cs` + a `RequestedTierResolver` helper in `src/Services/Sorcha.Tenant.Service/Services/RequestedTierResolver.cs`.
- [ ] T018 [P] [US1] Classify + apply `RequireConsumerAudience` to consumer endpoint groups in `src/Services/Sorcha.Wallet.Service/Endpoints/CitizenWalletEndpoints.cs` and the citizen `/me/*` + persona consumer reads in `src/Services/Sorcha.Tenant.Service/Endpoints/`.
- [ ] T019 [P] [US1] Apply `RequirePlatformAudience` to admin/designer/org-management/`/platform/*`/IdP-config endpoint groups across `src/Services/Sorcha.Tenant.Service/Endpoints/` and admin surfaces.
- [ ] T020 [P] [US1] Apply the platform-tier classification to Blueprint/Register designer + management endpoints in `src/Services/Sorcha.Blueprint.Service/` and `src/Services/Sorcha.Register.Service/`.
- [ ] T021 [US1] Enable the fallback "default to platform" policy (T014) on each service host so unclassified authenticated endpoints fail safe.
- [ ] T022 [US1] Verify role policies (`RequireAdministrator`, etc.) still compose on top of `RequirePlatformAudience` (tier gate then role gate) — adjust policy composition in `src/Services/Sorcha.Tenant.Service/Extensions/AuthenticationExtensions.cs`.

**Checkpoint**: Consumer/platform tier isolation enforced and independently testable.

---

## Phase 4: User Story 2 - Infrastructure (service) isolation (Priority: P1)

**Goal**: Internal/service endpoints accept only `:service` tokens; human tokens are refused there at the token layer, and service tokens are refused at human endpoints.

**Independent Test**: Present a human token to `/api/internal/*` (refused), a service token there (accepted), and a service token to a human endpoint (refused).

### Tests for User Story 2 (write first, must fail)

- [ ] T023 [P] [US2] Integration test: a human (`:consumer`/`:platform`) token is refused at an `/api/internal/*` endpoint; a `:service` token is accepted, in `tests/Sorcha.Tenant.Service.Tests/Integration/ServiceTierIsolationTests.cs`.
- [ ] T024 [P] [US2] Integration test: a `:service` token is refused at a consumer and a platform human endpoint, in the same file.

### Implementation for User Story 2

- [ ] T025 [US2] Ensure `GenerateServiceTokenAsync` stamps `aud = SorchaAudiences.For(Service)` in `src/Services/Sorcha.Tenant.Service/Services/TokenService.cs`.
- [ ] T026 [P] [US2] Apply the extended `RequireService` policy to every `/api/internal/*` group across `src/Services/Sorcha.Tenant.Service/Endpoints/Internal*.cs`, `src/Services/Sorcha.Register.Service/Endpoints/ObservationEndpoints.cs`, `src/Services/Sorcha.Peer.Service/`, and any other internal surfaces.
- [ ] T027 [P] [US2] Confirm `CanWriteDockets` / `CanReportRegisterObservation` (which mirror `RequireService`) inherit the `:service` audience assertion in `src/Common/Sorcha.ServiceDefaults/AuthorizationPolicyExtensions.cs`.
- [ ] T028 [US2] Verify the `ServiceAuthClient` acquisition path produces a `:service`-audience token end to end (no human-audience leakage) in `src/Common/Sorcha.ServiceClients.Http/Auth/ServiceAuthClient.cs` (config/contract check only).

**Checkpoint**: Service ⊥ human isolation enforced at the audience layer.

---

## Phase 5: User Story 3 - Cross-installation isolation + issuer hardening (Priority: P1)

**Goal**: No shared default issuer; installation identity is explicit; misconfigured installations fail closed; tokens never cross installations.

**Independent Test**: Two installations with distinct identities reject each other's tokens; a production-like installation with no issuer/name fails to start.

### Tests for User Story 3 (write first, must fail)

- [ ] T029 [P] [US3] Unit test: issuer resolution — explicit wins; else `urn:sorcha:{InstallationName}`; missing-both throws in Production and yields `urn:sorcha:dev-local` in Development, in `tests/Sorcha.ServiceDefaults.Tests/Auth/IssuerResolutionTests.cs`.
- [ ] T030 [P] [US3] Integration test: a token signed with installation A's key/issuer is rejected under installation B's settings, in `tests/Sorcha.ServiceDefaults.Tests/Auth/CrossInstallationRejectionTests.cs`.

### Implementation for User Story 3

- [ ] T031 [US3] Rework issuer resolution in `src/Common/Sorcha.ServiceDefaults/JwtAuthenticationExtensions.cs`: remove the `https://tenant.sorcha.io` default; explicit `JwtSettings:Issuer` wins, else `urn:sorcha:{InstallationName}`, else fail-closed (throw) in Production/Staging and `urn:sorcha:dev-local` in Development; `InstallationName` drives both issuer and audience namespace.
- [ ] T032 [P] [US3] Update each service's `appsettings*.json` to set `JwtSettings:InstallationName`, remove the now-derived `Audiences` array, and drop any hard-coded issuer default.
- [ ] T033 [P] [US3] Set `InstallationName` (e.g. via `INSTALLATION_NAME`/env) in `docker-compose.yml` and the n1 deployment config so the dev/demo installation has an explicit identity.
- [ ] T034 [US3] Add the actionable startup failure message + a `Storage`-style log when issuer is unresolvable in Production/Staging, in `JwtAuthenticationExtensions.cs`.
- [ ] T035 [US3] Update `docs/guides/AUTHENTICATION-SETUP.md` and `docs/getting-started/` with the new `InstallationName`/issuer configuration and the fail-closed behaviour.

**Checkpoint**: Cross-installation isolation + fail-closed issuer in place.

---

## Phase 6: User Story 4 - Consumer token across every sign-in method (Priority: P2)

**Goal**: Every server-side auth flow can mint a `:consumer` token; a consumer destination yields Consumer; the wallet accepts it. (Spec B dependency contract.)

**Independent Test**: Complete each sign-in method with a consumer entry and confirm a `:consumer` token results and is accepted by the wallet surface.

### Tests for User Story 4 (write first, must fail)

- [ ] T036 [P] [US4] Integration test: login, verify-2fa, and refresh each yield a `:consumer` token (carrying `platform_user_id`, no `org_id`/roles) when consumer is requested, in `tests/Sorcha.Tenant.Service.Tests/Integration/ConsumerIssuancePathsTests.cs`.
- [ ] T037 [P] [US4] Integration test: `SocialCallback` and `OidcCallback` mint a `:consumer` token for a consumer `returnTo`, in `tests/Sorcha.Tenant.Service.Tests/Integration/ConsumerCallbackTests.cs`.
- [ ] T038 [P] [US4] Integration test: enrol-session redeem returns a `:consumer` access token, in `tests/Sorcha.Tenant.Service.Tests/Integration/EnrolConsumerTokenTests.cs`.
- [ ] T039 [P] [US4] E2E (vstest): the Wallet Service accepts a `:consumer` token on `/api/v1/wallet/*`, in `tests/Sorcha.UI.E2E.Tests/Docker/CitizenWallet/ConsumerAudienceAcceptanceTests.cs` (run via `dotnet vstest --TestCaseFilter`, per #818).

### Implementation for User Story 4

- [ ] T040 [US4] Extend `RequestedTierResolver` (T017) to be the shared derivation for all paths (returnTo → tier, optional explicit override, default Consumer) and unit-test it in `tests/Sorcha.Tenant.Service.Tests/Services/RequestedTierResolverTests.cs`.
- [ ] T041 [P] [US4] Wire verify-2fa and refresh to apply the resolver / preserve tier in `src/Services/Sorcha.Tenant.Service/Endpoints/AuthEndpoints.cs`.
- [ ] T042 [P] [US4] Wire signup-completion to mint via the resolver in `src/Services/Sorcha.Tenant.Service/Pages/Auth/Signup.cshtml.cs`.
- [ ] T043 [P] [US4] Wire `SocialCallback` to derive tier from `returnTo` and pass to issuance in `src/Services/Sorcha.Tenant.Service/Pages/Auth/SocialCallback.cshtml.cs`.
- [ ] T044 [P] [US4] Wire `OidcCallback` similarly in `src/Services/Sorcha.Tenant.Service/Pages/Auth/OidcCallback.cshtml.cs`.
- [ ] T045 [US4] Ensure `EnrolSessionService` redeem mints a `:consumer` access token (replace the `Audiences.FirstOrDefault()` fallback) and `:enrol-session` on mint, in `src/Services/Sorcha.Tenant.Service/Services/EnrolSessionService.cs`.
- [ ] T046 [US4] Confirm the consumer web host (`src/Apps/Sorcha.UI/Sorcha.UI.Web`) validates `{installation}:consumer` where it gates server-side, and the Wallet Service consumer endpoints (T018) accept it.

**Checkpoint**: Consumer token works across all sign-in methods — Spec B contract satisfied.

---

## Phase 7: User Story 5 - Dual-role person gets the right tier per context (Priority: P3)

**Goal**: A citizen who is also an org admin gets a consumer token on consumer surfaces and a platform token in org context, re-minted on switch.

**Independent Test**: As a dual-role person, obtain a consumer token via a consumer entry, switch to org context, confirm a platform token is re-minted; confirm each only works on its tier.

### Tests for User Story 5 (write first, must fail)

- [ ] T047 [P] [US5] Integration test: a dual-role person authenticates via a consumer entry → `:consumer`; switch-org → `:platform`; each token works only on its tier, in `tests/Sorcha.Tenant.Service.Tests/Integration/DualRoleContextTests.cs`.
- [ ] T048 [P] [US5] Integration test: a roleless user requesting `tier=platform` is rejected (not downgraded), in the same file.

### Implementation for User Story 5

- [ ] T049 [US5] Wire `/api/auth/switch-org` to re-run `TierResolver` against the new active context and re-mint access + refresh at the resulting tier, in `src/Services/Sorcha.Tenant.Service/Endpoints/AuthEndpoints.cs`.
- [ ] T050 [US5] Ensure `entitledTiers` reads the **active** org context (post-switch) so platform entitlement reflects the switched org, in `src/Services/Sorcha.Tenant.Service/Services/TierResolver.cs`.
- [ ] T051 [US5] Surface the explicit over-request rejection as a clean 403 with the rejected-counter, in `AuthEndpoints.cs`.

**Checkpoint**: All five stories independently functional.

---

## Phase 8: Polish & Cross-Cutting Concerns

- [ ] T052 [P] Update `docs/reference/API-DOCUMENTATION.md` and the `sorcha-architecture` skill with the tiered-audience model + per-tier claim sets + policy names.
- [ ] T053 [P] Update `CLAUDE.md` Critical Patterns with the tier-policy convention (which policy gates which surface; default-platform).
- [ ] T054 Run `specs/136-jwt-audience-tiers/quickstart.md` validation end to end (configure, mint, verify all six boundary checks).
- [ ] T055 [P] Add OTel dashboard/alert note for `sorcha_token_minted_total` + `sorcha_tier_request_rejected_total` in `docs/`.
- [ ] T056 Full regression: `dotnet build` (no warnings) + `dotnet test` across affected projects; confirm >85% coverage on new code (`SorchaAudiences`, `TierResolver`, `RequestedTierResolver`, issuer resolution, policies).
- [ ] T057 Confirm no remaining hard-coded audience strings or shared issuer defaults anywhere (grep gate); confirm `JwtAudiences.CitizenWallet` is gone.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (P1)**: no dependencies.
- **Foundational (P2)**: depends on Setup. **BLOCKS all user stories.**
- **US1–US5 (P3–P7)**: all depend on Foundational. US1, US2, US3 are mutually independent (P1 — do first). US4 depends on Foundational + US1's `RequestedTierResolver` (T017) and consumer-endpoint validation (T018). US5 depends on US4 (composes the resolver + switch-org).
- **Polish (P8)**: depends on the desired stories being complete.

### User Story Dependencies

- **US1 (P1)**: Foundational only. MVP.
- **US2 (P1)**: Foundational only — independent of US1.
- **US3 (P1)**: Foundational only — independent (issuer/config axis).
- **US4 (P2)**: Foundational + US1 (T017/T018 shared resolver + consumer validation).
- **US5 (P3)**: US4 (switch-org re-mint reuses the resolver).

### Within Each Story

- Tests first (must fail) → implementation → re-run green.
- Shared engine (Foundational) before any wiring.

### Parallel Opportunities

- Setup T001, T002 in parallel.
- Foundational: T004/T007/T010/T012 (tests) parallel with each other; T003 before T004; T009 before T010; T011 before T012.
- US1: T015/T016 (tests) parallel; T018/T019/T020 (per-service classification, different files) parallel after T017.
- US2: T023/T024 parallel; T026/T027 parallel after T025.
- US3: T029/T030 parallel; T032/T033 parallel after T031.
- US4: T036/T037/T038/T039 parallel; T041/T042/T043/T044 parallel after T040.
- US1, US2, US3 can be developed by different people in parallel once Foundational lands.

---

## Parallel Example: Foundational tests

```bash
# After the engine code lands, run the foundational unit tests together:
dotnet test tests/Sorcha.ServiceDefaults.Tests   # SorchaAudiences (T004), policies (T007)
dotnet test tests/Sorcha.Tenant.Service.Tests --filter "FullyQualifiedName~TierResolver|FullyQualifiedName~TokenServiceTier"
```

## Parallel Example: US1 per-service endpoint classification

```text
# Different files, no shared state — run together:
Task T018: apply RequireConsumerAudience in Sorcha.Wallet.Service/Endpoints/CitizenWalletEndpoints.cs
Task T019: apply RequirePlatformAudience across Sorcha.Tenant.Service admin endpoints
Task T020: apply platform classification in Sorcha.Blueprint.Service + Sorcha.Register.Service
```

---

## Implementation Strategy

### MVP First (US1 only)

1. Phase 1 Setup → Phase 2 Foundational (the engine — critical).
2. Phase 3 US1 (consumer⊥platform isolation).
3. **STOP & VALIDATE**: mint consumer + platform tokens, confirm cross-tier refusal.
4. This alone delivers the headline defense-in-depth value.

### Incremental Delivery

1. Foundational → engine ready.
2. + US1 (tier isolation) → MVP, demo cross-tier refusal.
3. + US2 (service isolation) → internal APIs hardened.
4. + US3 (issuer/cross-installation) → installations isolated, fail-closed.
5. + US4 (consumer token everywhere) → unblocks Spec B (PWA auth).
6. + US5 (dual-role context) → full correctness for citizen-admins.

### Suggested PR slicing

- PR1: Setup + Foundational (engine + unit tests).
- PR2: US1 (+ US2 if small) — tier isolation.
- PR3: US3 — issuer hardening + config.
- PR4: US4 — consumer issuance across all paths (Spec B contract) + E2E.
- PR5: US5 — dual-role context.

---

## Notes

- `[P]` = different files, no incomplete-task dependency.
- Tests written first and confirmed failing before implementation (Constitution IV; security boundaries verified, not assumed).
- E2E tests run via `dotnet vstest --TestCaseFilter`, NOT `dotnet test --filter` (issue #818).
- No migration: deploy config, existing tokens expire (research R-009).
- Commit after each task or logical group; keep PRs focused per the slicing above.
