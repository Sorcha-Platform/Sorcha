# Phase 0 Research: Tiered-Audience JWT Identity Model + Issuer Hardening

All Technical-Context unknowns were resolved during brainstorming (design doc `2026-05-21-tiered-audience-identity-model-design.md`). This file records the load-bearing decisions in Decision / Rationale / Alternatives form, grounded in the current codebase.

## R-001 — Where the audience boundary is enforced (authenticate-broad / authorize-narrow)

- **Decision**: The JWT bearer pipeline validates only that `aud` is one of the installation's four tier audiences (`SorchaAudiences.All`); the specific tier is enforced **per endpoint** by authorization policies (`RequireConsumerAudience`, `RequirePlatformAudience`, `RequireService`+`:service`).
- **Rationale**: A single service (e.g. Tenant) hosts consumer (`/me/*`), platform (`/platform/*`), and internal (`/api/internal/*`) endpoints, so a per-service `ValidAudiences` set is too coarse. Policy-level enforcement mirrors the existing `RequireService` pattern (`AuthorizationPolicyExtensions.cs:99-156`) which already checks a claim per endpoint. This keeps authentication uniform and pushes the tier decision to where the endpoint is defined.
- **Alternatives considered**: (a) Per-service `ValidAudiences` — rejected, too coarse for mixed-tier services. (b) A custom `IAuthorizationMiddlewareResultHandler` — rejected, heavier than a policy requirement for an `aud` equality check.

## R-002 — Single source of truth for audience strings

- **Decision**: A `SorchaAudiences` helper in `Sorcha.ServiceDefaults` exposes `Tier` (enum: Consumer, Platform, Service, EnrolSession), `For(tier)` → audience string, and `All` → the four-string set, all derived from `JwtSettings.InstallationName`.
- **Rationale**: Issuance (Tenant Service) and validation (every service) must never diverge on the audience string. `JwtAuthenticationExtensions` already lives in `ServiceDefaults` and is referenced by all services, so the helper is reachable everywhere. Replaces the dead `JwtAudiences.CitizenWallet` constant (`Sorcha.CitizenWallet.Abstractions/Constants/JwtAudiences.cs`).
- **Alternatives**: Hard-coding strings per service — rejected (the original cause of the cosmetic-audience drift).

## R-003 — Issuer resolution, no shared default, fail-closed

- **Decision**: Resolution order — (1) explicit `JwtSettings:Issuer`; (2) else derive `urn:sorcha:{InstallationName}` (opaque, non-domain); (3) else **throw at startup** in Production/Staging, `urn:sorcha:dev-local` in Development. Remove the `https://tenant.sorcha.io` default (`JwtAuthenticationExtensions.cs:45`).
- **Rationale**: A shared default issuer — a domain the platform may not own per-installation — is insecure-by-default and risks multiple installations sharing an identity. Failing closed mirrors the existing `SigningKey` requirement (`JwtAuthenticationExtensions.cs:156-162`). A `urn:` form avoids implying a resolvable/owned domain. These are HMAC-signed internal tokens (not OIDC-discovered), so the issuer need not be a URL.
- **Alternatives**: URL-form derived issuer (`https://{InstallationName}`) — rejected, re-introduces the "domain we don't own" footgun. Keeping a default but warning — rejected, not fail-safe.

## R-004 — Cross-installation isolation rests on the signing key + issuer, not the audience

- **Decision**: The primary cross-installation boundary is the per-installation **symmetric HMAC `SigningKey`** plus the validated `iss`; the installation-namespaced audience prefix is defense-in-depth on top.
- **Rationale**: `ValidateIssuerSigningKey=true` + distinct per-installation secret means installation A's token fails signature validation at B regardless of audience (`JwtAuthenticationExtensions.cs:199-200`). Documenting this prevents mistaking the audience for the boundary.
- **Alternatives**: Asymmetric signing (issuer-only minting) — stronger, but a separate initiative (out of scope; logged as future work).

## R-005 — Tier selection at issuance + transport of the requested tier

- **Decision**: `mintedTier = requestedTier ∩ entitledTiers(user)`; over-request is rejected (not downgraded); absent request defaults to Consumer. `requestedTier` is **derived from the authentication entry's post-auth destination** (`returnTo` to `/wallet`/consumer host ⇒ Consumer; admin/designer surface ⇒ Platform), with an optional explicit override; no mandatory `tier` parameter. `entitledTiers` = {Consumer} for any authenticated human, plus {Platform} when the user holds a platform role (`Administrator|Designer|Auditor|SystemAdmin`, `UserIdentity.cs` `UserRole`) in the active org context.
- **Rationale**: Reuses the existing `Auth:ReturnToAllowlist` (F126) plumbing the auth pages already consume, so cold login and the downstream PWA redirect both "just work" without a new client contract. Default-to-consumer is the lowest-privilege fail-safe.
- **Alternatives**: Mandatory explicit `tier` param on every auth call — rejected, more client churn and a new contract for every caller; derivation from `returnTo` is sufficient with an override escape hatch.

## R-006 — Refresh token carries its tier

- **Decision**: The refresh token embeds the tier (claim); refresh re-mints an access token of the same tier.
- **Rationale**: Prevents a refresh from escalating tier. No DB change — the existing refresh token already carries claims (`token_use: refresh`, `platform_user_id`); add `tier`.
- **Alternatives**: Re-running tier selection at refresh — rejected, a refresh has no fresh `returnTo` context and must not silently change tier.

## R-007 — Endpoint classification + safe default

- **Decision**: Every protected endpoint is classified consumer / platform / service via the shared policies. Anything left unclassified defaults to `RequirePlatformAudience` (most restrictive human tier). Internal `/api/internal/*` → service; wallet `/api/v1/wallet/*` + citizen `/me/*` consumer surfaces → consumer; admin/designer/org-management/platform → platform.
- **Rationale**: A permissive default (e.g. "any tier") would silently accept consumer tokens on forgotten endpoints — the exact gap this feature closes. Defaulting to platform fails safe.
- **Alternatives**: Default-allow-any — rejected (unsafe). Per-endpoint mandatory annotation with build-time enforcement — desirable but heavier; the safe default covers correctness, and a CI grep can be a follow-up.

## R-008 — Observability

- **Decision**: A `Sorcha.Identity` OTel meter exposes `sorcha_token_minted_total{tier}` and `sorcha_tier_request_rejected_total{requested,reason}`; issuer resolution + tier selection log via structured logging (no interpolation). Tier-mismatch authorization failures surface through the existing authz pipeline.
- **Rationale**: Constitution VIII; gives operators visibility into tier distribution and over-request attempts.
- **Alternatives**: No instrumentation — rejected (constitution).

## R-009 — No migration

- **Decision**: Coordinated config rollout; existing tokens expire naturally; no dual-audience acceptance window, no compatibility shims.
- **Rationale**: Pre-release; n1 is dev/demo. A compatibility window would require accepting the old shared audience, weakening the very boundary being introduced.
- **Alternatives**: Dual-audience grace period — rejected (re-opens the hole; unnecessary pre-release).
