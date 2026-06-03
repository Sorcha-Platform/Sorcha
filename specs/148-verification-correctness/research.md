# Phase 0 Research: Verification-correctness

All decisions are grounded in the existing code. No NEEDS CLARIFICATION remained from the spec.

## R1 — H3: how to surface the issuer-signature status (Verifier.Engine + PWA)

**Findings:**
- `VerificationOutcome` (`Sorcha.Verifier.Engine/Models/VerifierSession.cs`) is a `sealed record` with `Accepted` / `DisclosedClaims` / `Errors` / `CompletedAt`. The validator's success path builds it at `VerifiablePresentationValidator.cs:275`; `Failure` at `:291`.
- The issuer-signature decision is already localised: `:181-188` verifies the JWS when a key resolves; `:189-195` rejects when `requireIssuerSignature` and no key; `:196-202` is the **accept-on-holder-chain** branch (the silent path) when `!requireIssuerSignature`.
- The PWA UI outcome enum `VerifyOutcome` (`Sorcha.UI.Components.User/Models/Verification/VerificationResult.cs`) already has **`Pass` / `Warn` / `Fail`**, and `Warn` is documented as "at least one check produced a warning (e.g. status list unreachable)" — `VerificationTrustView.razor` already renders all three with distinct colour/tone.

**Decision:** Add an `IssuerSignatureStatus` enum (`Verified` / `NotVerified`) in `Sorcha.Verifier.Engine.Models` and a non-required `IssuerSignature` property on `VerificationOutcome` (default `NotVerified`). The validator sets a local `issuerSignatureVerified` flag in the `:181` branch and threads it into the `:275` success outcome. `RealVerifierEngine.Map` maps `Accepted && IssuerSignature == NotVerified` → `VerifyOutcome.Warn` with a clear message ("Issuer not verified — offline / reduced assurance"); `Accepted && Verified` → `Pass`; `!Accepted` → `Fail`.

**Rationale:** Reuses the existing `Warn` UI state (no new rendering work), is additive (the `IssuerSignature` property defaults so `Failure` and existing tests compile unchanged), and **does not change server verifiers**: they run `requireIssuerSignature:true`, so they only ever reach `Verified` or reject — the `NotVerified && Accepted` state is unreachable for them.

**Alternatives considered:** a new `VerifyOutcome.PassReducedAssurance` value (rejected — `Warn` already exists and fits); a bare boolean on `VerificationResult` surfaced only as text (rejected — `Warn` gives the citizen a visible colour/tone signal, which is the point).

## R2 — H3: keep offline graceful degradation

**Decision:** Leave the PWA's `requireIssuerSignature:false` and `OptOutIssuerKeyResolver` unchanged. The fix is honesty (Warn + message + docs), not forcing issuer verification.

**Rationale:** The DID-resolver-backed resolver depends on `IDidResolverRegistry → SorchaDidResolver → IWalletServiceClient → ServiceAuthClient` — a **service principal** the citizen PWA does not have (this is why even the desk verifier only wires the DID tier when a principal is configured). Forcing issuer verification would break offline doorstep verification. Online issuer verification via a consumer/anonymous DID path (public `GET /orgs/{id}/did.json`) is a backlog enhancement.

## R3 — M3a: JWKS signature validation mechanism

**Findings:** `IdentityProviderConfiguration` already carries `JwksUri`, `MetadataUrl`, `DiscoveryDocumentJson`, `DiscoveryFetchedAt`. `OidcExchangeService` injects `IHttpClientFactory` (`_httpClientFactory.CreateClient()` at `:134`). Tenant references `System.IdentityModel.Tokens.Jwt` + `Microsoft.IdentityModel.Tokens` (and `Microsoft.AspNetCore.Authentication.OpenIdConnect`). `ValidateIdTokenAsync` has **no callers outside** `OidcExchangeService.ExchangeCodeAsync` (already `await`ed), so making it genuinely async is safe.

**Decision:** Fetch the provider's JWKS from `config.JwksUri` (fallback: discovery — `DiscoveryDocumentJson`/`MetadataUrl`, else `{IssuerUrl}/.well-known/openid-configuration` → `jwks_uri`), parse with `Microsoft.IdentityModel.Tokens.JsonWebKeySet`, and **verify the ID token JWS signature only** via a token handler with `TokenValidationParameters { IssuerSigningKeys = keys, ValidateIssuerSigningKey = true, RequireSignedTokens = true, ValidateIssuer = false, ValidateAudience = false, ValidateLifetime = false }`. Keep the existing manual `iss`/`aud`/`exp`/`nonce` checks and their precise error messages. Cache the JWKS per `JwksUri` in an injected cache with a TTL and a single refresh-on-kid-miss (rotation tolerance). **Fail-closed**: unfetchable JWKS / no matching `kid` / invalid signature / unconfigured key location → throw (reject the exchange).

**Rationale:** Direct `JsonWebKeySet` fetch needs only packages already present (no reliance on transitive `Microsoft.IdentityModel.Protocols.OpenIdConnect`), keeps the existing checks/messages/tests intact, and adds signature verification as a discrete fail-closed step. Refresh-on-kid-miss covers IdP key rotation without failing valid new-key tokens.

**Alternatives considered:** `ConfigurationManager<OpenIdConnectConfiguration>` (idiomatic auto-refresh/caching, but relies on the discovery doc + a transitive package; heavier to unit-test deterministically) — kept as a possible later swap; the seam is the JWKS-key-source, so swapping is localised. Replacing the whole manual validation with `JwtSecurityTokenHandler.ValidateToken` (rejected — would lose the existing precise nonce handling + error messages + tests for marginal gain).

## R4 — M3a: testability

**Decision:** Make the JWKS key source injectable behind a small seam (e.g. an `IOidcSigningKeyResolver`/func returning `IEnumerable<SecurityKey>` for a config) so tests supply a local key set and the production impl does the HTTP fetch + cache. Tests generate an RSA/EC key, build a matching `JsonWebKeySet` (with `kid`), sign a token, and assert: valid signature passes; tampered/wrong-key/unsigned rejected; key-source failure → reject; iss/aud/exp/nonce still enforced.

**Rationale:** Keeps `OidcExchangeServiceTests` deterministic and offline (no live IdP), mirrors how the codebase isolates IJSRuntime/network behind seams.

## R5 — M3b: fail-loud guard

**Findings:** `PasskeyRecoveryService.cs:83-90` re-keys after only confirming the wrap exists (WebAuthn assertion is a TODO). `OrgRecoveryService.cs:82` has `TODO: Verify orgRecoveryKeySignature against the org's recovery public key`. Both recovery endpoints are feature-gated off (`Features:WalletRecoveryEnabled`, `WalletEndpoints.cs` RecoverViaPasskey/RecoverViaOrg).

**Decision:** At the unverified unwrap point in each service, **throw `NotSupportedException`** with a message naming the missing proof and pointing at the feature flag (e.g. "Passkey recovery requires WebAuthn assertion verification, which is not implemented; keep Features:WalletRecoveryEnabled disabled."). Do not alter the feature gate.

**Rationale:** Mirrors the M1 dead-trap treatment (throw rather than silently no-op). Full WebAuthn / org-signature verification is deferred to the wallet-recovery feature.

## R6 — Test projects & runner

**Findings:** Existing test files cover every touch point — `tests/Sorcha.Verifier.Tests/Services/VerifiablePresentationValidatorTests.cs`, `tests/Sorcha.Wallet.Pwa.Tests/Services/Verification/RealVerifierEngineTests.cs`, `tests/Sorcha.Tenant.Service.Tests/Services/OidcExchangeServiceTests.cs`, `tests/Sorcha.Wallet.Service.Tests/Services/{PasskeyRecovery,OrgRecovery}ServiceTests.cs`.

**Decision:** Add cases to these existing files. Build + test scoped per affected project (MTP runs whole projects; `--filter` ignored).
