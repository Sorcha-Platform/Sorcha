# Implementation Plan: Public Social Signup on n1

**Branch**: `115-social-signup` | **Date**: 2026-04-26 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification at `specs/115-social-signup/spec.md`
**Companion design**: [`docs/superpowers/specs/2026-04-26-social-signup-n1-design.md`](../../docs/superpowers/specs/2026-04-26-social-signup-n1-design.md)

## Summary

Make public-user social signup work end-to-end on `n1.sorcha.dev` with two
configured providers (Google + GitHub). Fix a live redirect-URI bug,
enforce strict email-verification trust on both sides of cross-method
account linking, refresh display name from provider claims each login,
make signup buttons render only for configured providers, and make the
`PublicOrgEnabled` seed value configurable per-environment so a fresh n1
deploy is signup-ready without a manual database edit.

The OAuth/OIDC plumbing already exists in `Sorcha.Tenant.Service`. The
work is narrow: ~6 small code changes, configuration additions to
`docker-compose.n1.yml` + `.env.example`, a documentation page covering
provider-app registration, and xUnit tests covering the policy gates.

## Technical Context

**Language/Version**: C# 14, .NET 10
**Primary Dependencies**: ASP.NET Core Razor Pages, Microsoft.AspNetCore.DataProtection (distributed state cache), JwtBearer, EF Core 10 with PostgreSQL provider, existing `Sorcha.Tenant.Service` services (`SocialLoginService`, `PlatformUserService`, `WelcomeEmailDispatcher`, `IdentityRepository`, `OrganizationRepository`, `TokenService`)
**Storage**: PostgreSQL — existing tables `PlatformUsers`, `PlatformSocialLogins`, `PlatformSettings`, `UserIdentities`, `PlatformUserOrgMemberships`. **No schema changes** in this feature; one new column considered (`PlatformSocialLogin.LinkVerifiedAt`) but rejected as unnecessary — the existence of the row already represents a successful policy gate at link time.
**Testing**: xUnit v3.2.2 + FluentAssertions 8.8.0 + Moq 4.20.72 in `tests/Sorcha.Tenant.Service.Tests/`. HTTP boundary mocked via `IHttpClientFactory` mock (existing pattern in this test project). No real OAuth round-trips in CI.
**Target Platform**: Linux containers via Docker Compose on n1 (Azure VM); Windows + Linux for local dev. ASP.NET Core hosted in Kestrel inside the `sorcha-tenant-service` container.
**Project Type**: Microservice monolith — modifications confined to one service (Sorcha.Tenant.Service) plus its test project, plus root-level config (`docker-compose.n1.yml`, `.env.example`) and documentation.
**Performance Goals**: SC-001 (signup < 60 s end-to-end including provider consent), SC-002 (returning sign-in < 10 s). Callback handler P95 < 500 ms server time, dominated by provider token-exchange + EF write.
**Constraints**: Provider client secrets must never enter source control (Constitution principle II). Telemetry must not log PII in plaintext. Demo banner must remain visible on the n1 environment per FR-020.
**Scale/Scope**: n1 demonstrator, ≤100 concurrent users in test mode (Google OAuth test-mode cap). Single-VM deployment. Migration to multi-node Kubernetes anticipated as a follow-up that triggers the secrets-store upgrade (Key Vault).

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|---|---|---|
| I. Microservices-First | ✅ Pass | All work in `Sorcha.Tenant.Service`. No new service. No upward dependencies introduced. |
| II. Security First | ✅ Pass | Secrets via host-local `.env` (gitignored). Strict link policy is itself a security improvement. PII not logged in plaintext. KV migration tracked as backlog (BACKLOG-5) per "appropriate-when-multi-node" guidance. |
| III. API Documentation | ✅ Pass | Existing endpoints retain `.WithSummary` / `.WithDescription`. Razor-page callback is server-rendered, not an OpenAPI-tracked surface. No new public API endpoints. |
| IV. Testing Requirements | ✅ Pass | New `SocialLoginPolicyTests` + extensions to existing test classes. xUnit + FluentAssertions + Moq, AAA pattern. Target >85% on new code. |
| V. Code Quality | ✅ Pass | async/await throughout. DI honoured. NRT enabled. No new warnings. |
| VI. Blueprint Standards | N/A | Not a blueprint feature. |
| VII. DDD | ✅ Pass | "Public user", "social-provider link", "public organisation", "consumer role" — terminology preserved. No naming drift. |
| VIII. Observability | ✅ Pass | Adds `sorcha_social_login_refusal_total{provider, reason}` counter on the existing `Sorcha.Tenant` meter. Existing `LogWarning` with redacted email hash. |

**No violations. Phase 0 research proceeds.**

## Project Structure

### Documentation (this feature)

```text
specs/115-social-signup/
├── plan.md                        # This file
├── spec.md                        # User-facing contract
├── research.md                    # Phase 0 output
├── data-model.md                  # Phase 1 output
├── quickstart.md                  # Phase 1 output
├── contracts/
│   ├── oauth-callback.md          # The single per-env redirect URI contract
│   └── social-providers-config.md # The configuration shape providers expect
├── checklists/
│   └── requirements.md            # Spec-quality checklist (existing)
└── tasks.md                       # Phase 2 output (/speckit.tasks)
```

### Source Code (repository)

```text
src/Services/Sorcha.Tenant.Service/
├── Endpoints/
│   └── SocialLoginEndpoints.cs                # MODIFY — fix redirect URI (lines 99, 262)
├── Services/
│   ├── SocialLoginService.cs                  # MODIFY — capture email_verified; add GetConfiguredProviderNames
│   ├── ISocialLoginService.cs                 # MODIFY — new method signature
│   └── PlatformUserService.cs                 # MODIFY — strict link gate + DisplayName refresh
├── Models/Dtos/
│   └── SocialLoginDtos.cs                     # MODIFY — SocialAuthCallbackResult.EmailVerified field
├── Pages/Auth/
│   ├── SocialCallback.cshtml.cs               # MODIFY — provider from cached state, refusal rendering
│   ├── Signup.cshtml                          # MODIFY — buttons driven by AvailableProviders; remove dead JS
│   ├── Signup.cshtml.cs                       # MODIFY — populate AvailableProviders in OnGet
│   ├── Login.cshtml                           # MODIFY — same button-visibility treatment
│   └── Login.cshtml.cs                        # MODIFY — populate AvailableProviders in OnGet
└── Data/
    └── DatabaseInitializer.cs                 # MODIFY — read PlatformSettings:SeedPublicOrgEnabled

tests/Sorcha.Tenant.Service.Tests/
├── Services/
│   └── SocialLoginPolicyTests.cs              # NEW — Scenarios A/B/C from design
├── Endpoints/
│   └── SocialLoginEndpointsTests.cs           # EXTEND — redirect URI regression test
├── Pages/
│   ├── SignupModelTests.cs                    # EXTEND — AvailableProviders binding
│   └── LoginModelTests.cs                     # EXTEND — AvailableProviders binding
├── Data/
│   └── DatabaseInitializerTests.cs            # EXTEND — seed-flag honoured
└── Models/
    └── SocialAuthCallbackResultTests.cs       # NEW — EmailVerified parsing

docs/guides/
└── SOCIAL-LOGIN-SETUP.md                      # NEW — Google + GitHub OAuth-app registration runbook

# Repository root
docker-compose.n1.yml                          # MODIFY — add SocialProviders__0__/__1__ + PlatformSettings__SeedPublicOrgEnabled
.env.example                                   # MODIFY — add four GOOGLE_*/GITHUB_* placeholder vars
```

**Structure Decision**: Single-service feature in an established
microservices solution. All code lives in `Sorcha.Tenant.Service`,
matching the existing layout (Endpoints / Services / Pages / Data /
Models). Tests mirror that layout in the parallel test project. No new
projects.

## Phase 2 (later) — task ordering hints

Not generated by `/speckit.plan`. The natural decomposition for
`/speckit.tasks` is roughly:

1. **Trust-claim capture** (FR-010 substrate): extend
   `SocialAuthCallbackResult` with `EmailVerified`; populate from ID
   token / userinfo / GitHub primary-verified path. Tests:
   `SocialAuthCallbackResultTests`.
2. **Strict link policy** (FR-010, FR-011, FR-012, FR-013): rework
   `PlatformUserService.ResolveOrCreateSocialUserAsync` to gate on both
   sides verified. Tests: `SocialLoginPolicyTests`.
3. **Display-name drift** (FR-008): refresh `PlatformUser.DisplayName`
   on each successful resolve.
4. **Bug fix** (FR-021): `SocialLoginEndpoints.cs` redirect URI →
   `/auth/social/callback`. `SocialCallback.cshtml.cs` reads provider
   from cached state. Tests:
   `SocialLoginEndpointsTests` regression.
5. **Provider visibility** (FR-001, FR-002, FR-004): `ISocialLoginService.GetConfiguredProviderNames`;
   `SignupModel` / `LoginModel` populate `AvailableProviders`; views
   render conditional buttons; remove dead JS. Tests:
   `SignupModelTests`, `LoginModelTests`.
6. **Seed config** (FR-019): `DatabaseInitializer` reads
   `PlatformSettings:SeedPublicOrgEnabled`. Tests:
   `DatabaseInitializerTests`.
7. **Telemetry** (FR-018): add refusal counter on `Sorcha.Tenant`
   meter; emit on each refusal path.
8. **Refusal copy** (FR-016, FR-017): wire `SocialCallback` to render
   the documented messages per refusal reason.
9. **Configuration surface** (FR-003): add `SocialProviders__0__*`,
   `SocialProviders__1__*`, `PlatformSettings__SeedPublicOrgEnabled` to
   `docker-compose.n1.yml`; add four placeholder vars to `.env.example`.
10. **Documentation** (REQ-9): write `docs/guides/SOCIAL-LOGIN-SETUP.md`.

`/speckit.tasks` will refine ordering, dependency edges, and
parallelisability.

## Complexity Tracking

No constitutional violations. No complexity-tracking entries needed.
