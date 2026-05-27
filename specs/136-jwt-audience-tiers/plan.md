# Implementation Plan: Tiered-Audience JWT Identity Model + Issuer Hardening (Spec A)

**Branch**: `136-jwt-audience-tiers` | **Date**: 2026-05-21 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/136-jwt-audience-tiers/spec.md`
**Design rationale**: `docs/superpowers/specs/2026-05-21-tiered-audience-identity-model-design.md`

## Summary

Replace today's cosmetic/shared JWT audience with four installation-namespaced **trust-tier audiences** — `{installation}:consumer`, `{installation}:platform`, `{installation}:service`, `{installation}:enrol-session` — derived from a single source of truth, selected at issuance by `requestedTier ∩ entitlement`, applied across every token-issuance path, enforced per-endpoint with authenticate-broad/authorize-narrow policies, and paired with issuer hardening (no shared default; `InstallationName` drives issuer + audience namespace; fail-closed startup). Symmetric HMAC retained; no migration. Establishes the dependency contract for downstream Spec B (PWA auth).

## Technical Context

**Language/Version**: C# 14 / .NET 10
**Primary Dependencies**: `Microsoft.AspNetCore.Authentication.JwtBearer`, `Microsoft.IdentityModel.Tokens` / `System.IdentityModel.Tokens.Jwt` (existing), `Sorcha.ServiceDefaults` (shared auth/authz extensions), FluentValidation
**Storage**: No new persistent schema. Tokens are stateless JWTs; refresh tokens carry the tier as a claim (no DB column added). Config-driven (`JwtSettings`).
**Testing**: xUnit + FluentAssertions + Moq (unit); `WebApplicationFactory` (integration / endpoint policy enforcement); Playwright via `dotnet vstest` for the wallet consumer-audience acceptance check (E2E, Docker, INFRA-skipped in CI — see issue #818).
**Target Platform**: .NET 10 services behind the YARP API Gateway; Blazor WASM consumer web host; Citizen Wallet PWA (client only — does not validate tokens).
**Project Type**: web (multi-service backend + frontend hosts).
**Performance Goals**: No measurable regression — tier enforcement is O(1) claim/string comparison added to the existing authn/authz pipeline.
**Constraints**: No migration / no dual-audience compatibility window (coordinated config rollout; existing tokens expire). Symmetric HMAC retained (asymmetric signing deferred). Fail-closed startup when issuer is unresolvable in Production/Staging.
**Scale/Scope**: Platform-wide. One issuance authority (Tenant Service) across all auth flows; audience validation + endpoint tier-classification across every service that hosts protected endpoints (Tenant, Wallet, Blueprint, Register, Peer, Validator, HAIP, Gateway) and the consumer web host.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Assessment |
|-----------|------------|
| **I. Microservices-First** | PASS. New shared primitive (`SorchaAudiences`, tier policies) lives in `Sorcha.ServiceDefaults` (common, downward dependency); issuance logic stays in the Tenant Service (the issuer). No upward or cross-service coupling introduced. |
| **II. Security First** | PASS — this feature *advances* the zero-trust model (tier + installation isolation at the token layer, fail-closed issuer). HMAC signing key continues to support Key Vault / AWS KMS. The requested-tier input is an external boundary and MUST be validated. |
| **III. API Documentation** | PASS. No new REST resources; existing auth endpoints gain an optional tier hint (documented via OpenAPI + `.WithSummary`/`.WithDescription`). New public types carry XML docs. |
| **IV. Testing** | PASS. >85% target for the new `SorchaAudiences`, `TierResolver`, issuer-resolution, and policy code; integration tests assert cross-tier and cross-installation rejection; deterministic + isolated. |
| **V. Code Quality** | PASS. .NET 10 / C# 14, nullable enabled, async I/O, no Release warnings. |
| **VI. Blueprint Standards** | N/A — not a blueprint feature. |
| **VII. Domain-Driven Design** | PASS. No conflict with the Blueprint ubiquitous language; auth terminology kept consistent (tier, audience, issuer, claim). |
| **VIII. Observability** | PASS — add OpenTelemetry instruments for tier decisions (minted-tier counter, rejected-over-request counter) and structured logs (no string interpolation) for issuer resolution + tier selection. |

**Result: PASS — no violations. Complexity Tracking not required.**

## Project Structure

### Documentation (this feature)

```text
specs/136-jwt-audience-tiers/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output
│   ├── token-tiers.md            # tier → audience strings + per-tier claim sets
│   ├── authorization-policies.md # policy → tier matrix + endpoint classification rules
│   ├── issuer-resolution.md      # issuer resolution order + fail-closed behaviour
│   └── auth-entry-tier-request.md# how an auth entry conveys the requested tier
└── tasks.md             # Phase 2 output (/speckit.tasks — NOT created here)
```

### Source Code (repository root)

```text
src/Common/Sorcha.ServiceDefaults/
├── Auth/SorchaAudiences.cs                 # NEW — Tier enum + audience derivation (single source of truth)
├── JwtAuthenticationExtensions.cs          # CHANGE — ValidAudiences = SorchaAudiences.All; issuer resolution (no default, fail-closed)
└── AuthorizationPolicyExtensions.cs        # CHANGE — RequireConsumerAudience, RequirePlatformAudience; RequireService + :service audience

src/Services/Sorcha.Tenant.Service/
├── Services/TokenService.cs                # CHANGE — accept Tier; per-tier claim sets; refresh preserves tier
├── Services/TierResolver.cs                # NEW — requestedTier ∩ entitledTiers(user); reject over-request
├── Services/EnrolSessionService.cs         # CHANGE — :consumer access token on redeem; :enrol-session on mint
├── Pages/Auth/SocialCallback.cshtml.cs     # CHANGE — pass requestedTier (from returnTo) into issuance
├── Pages/Auth/OidcCallback.cshtml.cs       # CHANGE — same
├── Endpoints/AuthEndpoints.cs              # CHANGE — login/verify-2fa/refresh/switch-org accept + propagate requested tier
└── Extensions/AuthenticationExtensions.cs  # CHANGE — apply tier policies; classify endpoints

src/Services/{Sorcha.Wallet,Blueprint,Register,Peer,Validator,Haip}.Service/
└── (endpoint tier classification: consumer / platform / service policies on endpoint groups)

src/Apps/Sorcha.UI/Sorcha.UI.Web/        # consumer web host validates {installation}:consumer where it gates server-side

tests/
├── Sorcha.ServiceDefaults.Tests/         # SorchaAudiences, issuer resolution, policies (unit)
├── Sorcha.Tenant.Service.Tests/          # TierResolver, TokenService per-tier claims, issuance-path coverage, cross-installation rejection (unit + integration)
├── {Wallet,Blueprint,Register}.Service.*Tests/ # endpoint tier-enforcement (integration)
└── Sorcha.UI.E2E.Tests/                  # wallet accepts :consumer token (E2E via dotnet vstest)
```

**Structure Decision**: Multi-service web platform. The reusable primitive (`SorchaAudiences`, tier authorization policies, issuer resolution) belongs in the shared `Sorcha.ServiceDefaults` so every service validates identically; token *issuance* (the `TierResolver` + tier-aware `TokenService`) stays in the Tenant Service, the sole human-token issuer. Each service classifies its own endpoints to a tier via the shared policies.

## Complexity Tracking

> No Constitution Check violations — section intentionally empty.
