# Implementation Plan: Account Linking & Auth-Method Management

**Branch**: `116-account-linking` | **Date**: 2026-04-27 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `specs/116-account-linking/spec.md`
**Authoritative design**: [`docs/superpowers/specs/2026-04-27-account-linking-design.md`](../../docs/superpowers/specs/2026-04-27-account-linking-design.md) (committed `ded4218c`)

## Summary

Let one `PlatformUser` (one verified email) carry multiple sign-in methods — password, OAuth socials (Google, GitHub, Microsoft, Apple), FIDO2 / WebAuthn passkeys — and let the user list, add, rename, and remove them from a new **Accounts** tab in Settings. Password set / change moves into the existing **Security** tab next to 2FA. A shared re-authentication challenge primitive gates every sensitive operation; a hard last-method floor prevents self-lockout; passkey removal soft-deletes for forensic audit; OAuth link disambiguates from login via signed `state.intent`.

The data model already supports the multi-method shape — no new tables besides one challenge-token row. Most endpoints are reused; the additions are: aggregate read (`/api/me/auth-methods`), challenge initiate/verify, password set/change/remove, social unlink, passkey rename. Existing `social/callback` and passkey `DELETE` are modified. The existing 2FA-disable endpoint adopts the new challenge primitive.

The technical approach was fully resolved during the prior brainstorming session — Q1–Q6 in the design doc capture every load-bearing decision. This plan does not re-derive them.

## Technical Context

**Language/Version**: C# 14 / .NET 10
**Primary Dependencies**:
- ASP.NET Core Minimal APIs, EF Core 10 (Npgsql), MediatR-free service-layer pattern (matches existing Tenant Service style).
- Fido2NetLib for WebAuthn (already wired into `PasskeyService`).
- BCrypt.Net-Next for password hashing (already in use via `IPasswordHasher`).
- Microsoft.Extensions.Caching for in-memory bits; **Postgres-only for AuthChallengeToken** (per design §3.2 — durability + audit trail beats Redis latency for 5-min low-volume tokens).
- Sorcha.ServiceDefaults (rate limiting, OpenTelemetry, Scalar).
- MudBlazor 8.x (existing Sorcha.UI version) for the Accounts tab and `AuthChallengeDialog`.

**Storage**: PostgreSQL (Tenant Service DB, public schema). New table `auth_challenge_tokens` squashed into the existing `20260425152258_InitialCreate` migration per pre-release squash policy. No new Redis usage.

**Testing**: xUnit + FluentAssertions + Moq (unit + integration). `WebApplicationFactory` for endpoint integration with Redis mocked per project convention. Playwright (`Sorcha.UI.E2E.Tests/Docker`) for E2E.

**Target Platform**: Linux containers (Docker Compose dev) + Azure Container Apps (production). Blazor WASM client served via API Gateway path-strip (`/app`).

**Project Type**: Multi-service .NET Aspire solution. Backend changes localised to `Sorcha.Tenant.Service`; frontend changes localised to `Sorcha.UI.Web.Client` (Blazor WASM) with a typed client in `Sorcha.UI.Core`.

**Performance Goals**:
- `GET /api/me/auth-methods` p95 < 200 ms cold; UI render of Accounts tab < 2 s end-to-end (SC-006).
- Challenge initiate + verify round-trip p95 < 500 ms.
- Aggregate read is a single Postgres query with three joins (`PlatformUser` ⨝ `PlatformSocialLogin` ⨝ `PasskeyCredential` filtered to non-Revoked).

**Constraints**:
- Pre-release: squash migrations rather than versioning forward.
- Constitutional: Scalar OpenAPI (not Swagger), XML docs on public APIs, FluentValidation on DTOs at boundaries, structured logging, no string interpolation in logs.
- Sorcha-specific: use `Sorcha.ServiceClients` for cross-service HTTP (none needed here — pure within-service work plus UI), use `RequireService` policy on internal-only endpoints (none here — these are user-facing), shared `RateLimitPolicies.PlatformAuth` on challenge endpoints.

**Scale/Scope**:
- Per-PlatformUser footprint: 0–1 password, 0–4 social links, 0–~10 passkeys typically.
- Platform-wide: thousands of users in dev/n1, larger at production scale. Challenge token table is small (≤ 1 row per active 5-minute window per user) — no special partitioning.

**No `NEEDS CLARIFICATION` items.** Every architectural decision is locked in the design doc.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design.*

| Principle | Status | Notes |
|---|---|---|
| **I. Microservices-First** | ✅ Pass | All backend changes in `Sorcha.Tenant.Service`. UI changes in `Sorcha.UI.Web.Client` + `Sorcha.UI.Core`. No cross-service contract changes; no new service dependencies. |
| **II. Security First** | ✅ Pass | `AuthChallengeToken` stores `SHA-256(token)`, never raw token. OAuth `state` is HMAC-SHA256 signed. New request DTOs use FluentValidation at the boundary. No new secrets in source. Re-auth gating closes the hijacked-session pruning gap. 2FA disable adoption (FR-024) is a security-positive change to existing surface. |
| **III. API Documentation** | ✅ Pass | All new Minimal API endpoints get `.WithName(…)` + `.WithSummary(…)` + `.WithDescription(…)` and typed `.Produces<T>()`. XML docs on public service methods. Scalar already serves them at `/openapi/v1.json`. |
| **IV. Testing Requirements** | ✅ Pass | Spec SC-001..SC-010 + design §8 mandates >85% on new code. Unit + integration + E2E layers per existing project shape. Tests deterministic (Redis mocked). |
| **V. Code Quality** | ✅ Pass | Nullable reference types enabled. `async`/`await` throughout. DI registration via existing `ServiceCollectionExtensions.AddTenantEmail`-style pattern (new `AddTenantAccountManagement`). Target `.NET 10` / `C# 14`. No new compiler warnings. |
| **VI. Blueprint Standards** | N/A | Feature does not touch blueprints. |
| **VII. Domain-Driven Design** | ✅ Pass | Reuses existing ubiquitous-language terms (`PlatformUser`, `PasskeyCredential`, `PlatformSocialLogin`). New term *re-authentication challenge* is platform-internal, not user-domain. |
| **VIII. Observability by Default** | ✅ Pass | New endpoints emit structured logs (`LogInformation` with `{PlatformUserId}`, `{ScopedOperation}`, `{ChallengeMethod}` — no string interpolation). New OpenTelemetry counters on the existing `Sorcha.Tenant.Auth` meter: `sorcha_auth_challenge_issued_total{method,scope}`, `sorcha_auth_challenge_consumed_total{method,scope,outcome}`, `sorcha_auth_method_removed_total{kind}`. `AuthChallengeTokenCleanupService` reports a `BackgroundService` health-check style log every tick. |

**No violations.** No entries in Complexity Tracking.

## Project Structure

### Documentation (this feature)

```text
specs/116-account-linking/
├── plan.md              # This file
├── spec.md              # Business spec (already written)
├── research.md          # Phase 0 — consolidates Q1–Q6 + alternatives
├── data-model.md        # Phase 1 — AuthChallengeToken + reuse semantics
├── contracts/
│   ├── auth-methods.openapi.yaml      # GET /api/me/auth-methods
│   ├── auth-challenge.openapi.yaml    # POST initiate + verify
│   ├── auth-password.openapi.yaml     # POST set / change / remove
│   ├── auth-social.openapi.yaml       # link via initiate(intent), DELETE unlink
│   └── passkey-management.openapi.yaml # PUT rename, DELETE soft-revoke
├── quickstart.md        # Phase 1 — local-dev walk-through
├── checklists/
│   └── requirements.md  # Already written by /speckit.specify
└── tasks.md             # NOT created by /speckit.plan — generated by /speckit.tasks
```

### Source Code (repository root)

The Sorcha solution is a multi-project .NET Aspire monorepo. This feature touches two existing services-shaped folders only — Tenant Service (backend) and Sorcha.UI.Web.Client (Blazor WASM frontend) plus its shared client library `Sorcha.UI.Core`. No new top-level projects.

```text
src/
├── Services/Sorcha.Tenant.Service/
│   ├── Models/
│   │   ├── AuthChallengeToken.cs                    [NEW]
│   │   └── PasskeyCredential.cs                     [unchanged — reuse Status]
│   ├── Data/
│   │   ├── TenantDbContext.cs                       [MOD — DbSet + OnModelCreating]
│   │   └── Repositories/
│   │       ├── IAuthChallengeRepository.cs          [NEW]
│   │       └── AuthChallengeRepository.cs           [NEW]
│   ├── Services/
│   │   ├── IAuthChallengeService.cs                 [NEW]
│   │   ├── AuthChallengeService.cs                  [NEW]
│   │   ├── IAuthMethodService.cs                    [NEW — floor + aggregate]
│   │   ├── AuthMethodService.cs                     [NEW]
│   │   ├── ISocialLinkService.cs                    [NEW]
│   │   ├── SocialLinkService.cs                     [NEW — link/unlink + collision]
│   │   ├── IPasswordManagementService.cs            [NEW]
│   │   ├── PasswordManagementService.cs             [NEW — set/change/remove]
│   │   └── AuthChallengeTokenCleanupService.cs      [NEW — BackgroundService]
│   ├── Endpoints/
│   │   ├── AuthEndpoints.cs                         [MOD — add password set/change/remove]
│   │   ├── SocialLoginEndpoints.cs                  [MOD — intent in state, DELETE link, drop /link]
│   │   ├── PasskeyEndpoints.cs                      [MOD — soft-delete, PUT rename, require name]
│   │   ├── TotpEndpoints.cs                         [MOD — Disable2Fa adopts challenge filter]
│   │   ├── AuthMethodsEndpoints.cs                  [NEW — GET /api/me/auth-methods]
│   │   └── AuthChallengeEndpoints.cs                [NEW — initiate + verify]
│   ├── Filters/
│   │   └── RequireAuthChallengeAttribute.cs         [NEW — endpoint filter]
│   ├── Extensions/
│   │   └── ServiceCollectionExtensions.cs           [MOD — AddTenantAccountManagement]
│   ├── Migrations/
│   │   ├── 20260425152258_InitialCreate.cs          [SQUASH — add auth_challenge_tokens]
│   │   └── 20260425152258_InitialCreate.Designer.cs [REGEN]
│   └── Models/Requests/                             [NEW DTOs — see contracts/]
├── Apps/Sorcha.UI/
│   ├── Sorcha.UI.Web.Client/Pages/
│   │   └── Settings.razor                           [MOD — add Accounts tab, rename Connections]
│   └── Sorcha.UI.Web.Client/Components/Settings/
│       ├── AccountsTab.razor                        [NEW]
│       └── AuthMethods/
│           ├── PasswordSection.razor                [NEW — hosted in Accounts + Security]
│           ├── SocialLinksSection.razor             [NEW]
│           ├── PasskeysSection.razor                [NEW]
│           └── AuthChallengeDialog.razor            [NEW — shared MudDialog]
└── Apps/Sorcha.UI/Sorcha.UI.Core/Services/
    ├── IAuthMethodsService.cs                       [NEW — typed client]
    └── AuthMethodsService.cs                        [NEW]

tests/
├── Sorcha.Tenant.Service.Tests/
│   ├── Services/
│   │   ├── AuthChallengeServiceTests.cs             [NEW]
│   │   ├── AuthMethodServiceTests.cs                [NEW]
│   │   ├── SocialLinkServiceTests.cs                [NEW]
│   │   ├── PasswordManagementServiceTests.cs        [NEW]
│   │   └── PasskeyRevocationTests.cs                [NEW]
│   └── Endpoints/
│       ├── AuthChallengeEndpointTests.cs            [NEW]
│       ├── AuthMethodsEndpointTests.cs              [NEW]
│       ├── PasswordEndpointTests.cs                 [NEW]
│       ├── SocialLinkEndpointTests.cs               [NEW]
│       └── PasskeyEndpointTests.cs                  [MOD — add rename + soft-delete]
└── Sorcha.UI.E2E.Tests/Docker/
    └── AccountsTabTests.cs                          [NEW — five Playwright scenarios]
```

**Structure Decision**: Multi-service Aspire solution; **single-service backend change** (Tenant Service) + **single-app frontend change** (Sorcha.UI.Web.Client + Sorcha.UI.Core). No cross-service contracts; no new top-level projects.

## Complexity Tracking

> No constitutional violations. Section intentionally empty.

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| *(none)*  | *(n/a)*    | *(n/a)*                              |
