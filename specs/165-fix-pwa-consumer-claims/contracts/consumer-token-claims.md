# Contract: Consumer-tier token claims (Feature 136 conformance)

**Feature**: 165 | **Type**: Token-shape contract (not a new HTTP endpoint)

This contract pins the claim shape every consumer-tier access token MUST satisfy. It is enforced by regression tests against `TokenService.GenerateUserTokenAsync(…, Tier.Consumer, …)` and `RefreshTokenAsync` — not by a new API. No new claim names or formats are introduced; this conforms issued tokens to the already-defined Feature 136 consumer shape.

## MUST (assertions)

For **every** path that mints a consumer token (password login, org-selection, post-2FA Razor + API, social callback, passkey assertion, org switch, and any future interactive issuance) **and** for token refresh:

- `aud` == `{installation}:consumer` (from `SorchaAudiences.For(Tier.Consumer)`).
- `iss` == `SorchaIssuer.Resolve(installation)`.
- `token_type` == `user`.
- `sub` present (org-scoped `UserIdentity.Id`).
- **`platform_user_id` present and equal to the citizen's canonical `PlatformUser.Id`.** (FR-001/002/003)
- `org_id`, `org_name`, `email`, `name`, `jti` present.

## MUST NOT (tier-boundary assertions)

- No `role` claim. (FR-006)
- No `wallet_address` claim. (FR-005, FR-006)
- The token MUST be rejected at any endpoint guarded by `RequirePlatformAudience` / `RequireService`. (SC-005)

## Refresh-specific

- A refresh exchange MUST re-emit a consumer token whose `platform_user_id` equals the original's. (FR-003, INV-4)
- If the inbound refresh token lacks `platform_user_id` (legacy), the service MUST recover it from `UserIdentity.PlatformUserId` before re-emitting (existing behaviour at `TokenService.cs:313-324` — assert it stays).

## Test matrix (representative)

| Path | Entry point | Assert |
|------|-------------|--------|
| Password login (single org) | `LoginService.IssueTokensForOrgAsync` | MUST + MUST NOT set |
| Org selection | `LoginService` (org-selection completion) | MUST + MUST NOT set |
| 2FA (Razor) | `Login.cshtml.cs:Handle2FaAsync` | MUST + MUST NOT set |
| 2FA (API) | `AuthEndpoints.Verify2Fa` | MUST + MUST NOT set |
| Social callback (wallet surface) | `SocialCallback.cshtml.cs` | MUST + MUST NOT set |
| Passkey assertion (consumer hint) | `PublicPasskeyEndpoints.AssertionVerify` | MUST + MUST NOT set |
| Org switch (non-admin → consumer) | `AuthEndpoints.SwitchOrganization` | MUST + MUST NOT set |
| Refresh (claim present) | `TokenService.RefreshTokenAsync` | same `platform_user_id` re-emitted |
| Refresh (legacy, claim absent) | `TokenService.RefreshTokenAsync` | recovered from `UserIdentity.PlatformUserId` |
