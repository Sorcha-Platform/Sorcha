# Implementation Plan: Auth Hardening B-Backend — Step-Up-Gated Social Account Linking

**Branch**: `168-auth-social-link-stepup` | **Date**: 2026-06-27 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/168-auth-social-link-stepup/spec.md`

## Summary

Replace the silent social-account auto-link (today, an unconnected social sign-in whose verified
email matches an existing verified account is linked and signed in with **no proof of the existing
account**) with an explicit, step-up-gated linking flow on the **Tenant Service** backend.

When the social callback hits the match-and-link branch (`PlatformUserService.ResolveOrCreateSocialUserAsync`
Step 2, lines 302–348), it no longer calls `LinkSocialLoginAsync` + issues a session. Instead it
returns a new **LinkRequired** outcome carrying a signed, stateless, short-lived **link-pending
token** (HMAC, reusing the deployment-stable HKDF-from-JWT-signing-key approach already used for the
2FA login token — a new distinct `info` label, no new persistence). To complete the link the person
proves ownership of the existing account through the **existing** step-up challenge mechanism, scoped
to a new `LinkSocial` operation, then redeems the link-pending token **plus** the challenge proof at a
new **link-confirm** endpoint. Link-confirm asserts the challenge proof's account == the link-pending
token's target account, calls the existing `ISocialLinkService.LinkAsync` (collision rules unchanged),
and issues the **same** session a normal social sign-in would.

The technical crux: the existing `/api/auth/challenge/{initiate,verify}` endpoints `.RequireAuthorization()`
and derive `ChallengeContext` from bearer claims — but the link flow is **pre-session**. The plan adds
a thin **pre-session challenge entry** that derives `ChallengeContext` from the link-pending token's
target account instead of a bearer, reusing `IAuthChallengeService` unchanged underneath.

## Technical Context

**Language/Version**: C# 14 / .NET 10

**Primary Dependencies**: ASP.NET Core Minimal APIs + Razor Pages (Tenant Service), `Sorcha.ServiceDefaults.Auth`
(`SorchaAudiences`, `Tier`, rate-limit policies), `System.Security.Cryptography` (HKDF / HMAC-SHA256),
existing `IAuthChallengeService`, `ISocialLinkService`, `IPlatformUserService`, `ITokenService`.

**Storage**: None added. Link-pending token is stateless (HMAC-signed, self-contained). The durable
social link uses the existing `PlatformSocialLogin` table via `ISocialLinkService`. Challenge tokens
use their existing hash-in-DB storage unchanged.

**Testing**: xUnit + FluentAssertions + Moq. Unit tests for token mint/verify and the FR-010 proof
policy; integration tests (WebApplicationFactory) for the callback branch, link-confirm accept/reject
matrix, and the two unchanged paths.

**Target Platform**: Linux server (Tenant Service container / Aspire host).

**Project Type**: Backend microservice (single service — Tenant Service). No UI in this workstream.

**Performance Goals**: No new hot path. Token mint/verify is in-memory HMAC (sub-millisecond). Link-confirm
adds one challenge-token consume + one `LinkAsync` (existing cost).

**Constraints**: Link-pending token TTL ~5 minutes, server-enforced. No new persistent storage
(Assumption + FR-004). Surgical change — the no-match and already-linked paths must be byte-for-byte
unchanged (FR-013, FR-014, SC-004). Standard rate limiting + non-leaky status codes (FR-018).

**Scale/Scope**: ~1 new endpoint, 1 new pre-session challenge entry, 1 new `ScopedOperation` enum value,
1 new token type + signer/verifier, 1 new outcome on the social callback branch, 1 new telemetry tag.
~6–9 source files touched/added in `Sorcha.Tenant.Service`, plus tests.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design.*

| Principle | Status | Notes |
|-----------|--------|-------|
| II. Security First | ✅ Core intent | Closes a silent-auto-link account-takeover hole. Input validation on token + proof; integrity-protected token; non-leaky status codes (FR-018). |
| III. API Documentation | ✅ | New link-confirm + pre-session challenge endpoints get `.WithSummary()`/`.WithDescription()` and XML docs; OpenAPI via built-in + Scalar (no Swagger). |
| IV. Testing Requirements | ✅ | xUnit; >85% on new code; full reject matrix (US2) + 5-config policy matrix (US3) + regression on unchanged paths (US5). AAA pattern. |
| V. Code Quality | ✅ | Nullable enabled; async I/O; DI; no new warnings; reuses existing services rather than rewriting. |
| VIII. Observability | ✅ | Extends existing `SocialLoginMetrics` counter with `link_required` / link-confirm outcomes (FR-017); structured logging, no interpolation. |
| I. Microservices-First | ✅ | Change is contained within Tenant Service; no new cross-service coupling. |
| VII. Domain-Driven Design | ✅ | Reuses ubiquitous terms (PlatformUser, social link); names the new concept "link-pending token" / "LinkSocial" consistently. |

**No violations.** Complexity Tracking section omitted (nothing to justify).

## Project Structure

### Documentation (this feature)

```text
specs/168-auth-social-link-stepup/
├── plan.md              # This file
├── research.md          # Phase 0 — decisions (pre-session challenge, token design, proof policy)
├── data-model.md        # Phase 1 — link-pending token, LinkSocial scope, outcomes
├── quickstart.md        # Phase 1 — runnable validation scenarios mapping to US1–US5
├── contracts/           # Phase 1 — endpoint + token contracts
│   ├── link-pending-token.md
│   ├── social-link-stepup.md     # pre-session challenge initiate/verify
│   └── link-confirm.md
├── checklists/          # (pre-existing)
└── tasks.md             # Phase 2 — /speckit-tasks (NOT created here)
```

### Source Code (repository root)

```text
src/Services/Sorcha.Tenant.Service/
├── Endpoints/
│   ├── SocialLoginEndpoints.cs        # CHANGE: callback branch returns LinkRequired (no session)
│   └── SocialLinkStepUpEndpoints.cs   # NEW: pre-session challenge initiate/verify + link-confirm
├── Pages/Auth/
│   └── SocialCallback.cshtml(.cs)     # CHANGE: render/redirect LinkRequired outcome (token to client)
├── Services/
│   ├── PlatformUserService.cs         # CHANGE: Step 2 returns LinkRequired instead of auto-linking
│   ├── IPlatformUserService.cs        # CHANGE: add LinkRequired to ResolveSocialUserResult/refusal
│   ├── ILinkPendingTokenService.cs    # NEW: mint/verify the link-pending token
│   ├── LinkPendingTokenService.cs     # NEW: HMAC over LinkPendingTokenKey (HKDF, new info label)
│   ├── LinkPendingTokenKey.cs         # NEW: singleton key holder (mirrors LoginTokenSigningKey)
│   ├── TenantSecretKeyResolver.cs     # CHANGE: add ResolveLinkPendingTokenSigningKey() (+ info const)
│   ├── ISocialLinkService.cs          # REUSE: LinkAsync (collision rules unchanged)
│   └── SocialLoginMetrics.cs          # CHANGE: add link_required + link-confirm outcome tags
├── Models/
│   ├── AuthChallengeEnums.cs          # CHANGE: add ScopedOperation.LinkSocial
│   ├── LinkPendingToken.cs            # NEW: token payload record (claims + expiry)
│   └── Requests/LinkConfirmRequest.cs # NEW: request DTO for link-confirm
└── Program.cs / Extensions            # CHANGE: register new services + map new endpoints

tests/Sorcha.Tenant.Service.Tests/
├── Services/LinkPendingTokenServiceTests.cs   # NEW: mint/verify, tamper, expiry
├── Services/SocialLinkStepUpPolicyTests.cs    # NEW: FR-010 five-config matrix
├── Endpoints/SocialLinkConfirmTests.cs        # NEW: accept + full reject matrix (US2)
└── Endpoints/SocialCallbackLinkRequiredTests.cs # NEW: US1 + US5 regression
```

**Structure Decision**: Single-service backend change inside `src/Services/Sorcha.Tenant.Service`.
New code follows the established service folder layout (`Endpoints/`, `Services/` with
`Interfaces`/impl, `Models/`, `Models/Requests/`). The link-pending token signer is modelled directly
on the existing `TenantSecretKeyResolver` + `LoginTokenSigningKey` pair (Feature 146) to stay stateless
and deployment-stable. The pre-session challenge entry is a new endpoint file rather than a change to
`AuthChallengeEndpoints.cs`, so the authenticated challenge surface is untouched.

## Complexity Tracking

> No constitution violations — section intentionally empty.
