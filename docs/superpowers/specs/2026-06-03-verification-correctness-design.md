# Verification-correctness — design

**Date:** 2026-06-03
**Initiative:** Security hardening (from the 2026-06-02 architecture review)
**Sub-project:** 3 of N — Verification-correctness
**Branch:** `148-verification-correctness`
**Source findings:** `docs/reviews/2026-06-02-architecture-review.md` §2 (H3, M3), §5.1, §7, §8

---

## 1. Problem

Three places accept something without fully verifying its signature — or, where full verification is genuinely impossible (offline), present the result as if they had.

| # | Finding | Today | Risk |
|---|---------|-------|------|
| H3 | PWA-local presentation verifier silently accepts unverified issuer signatures | `Sorcha.Wallet.Pwa/Extensions/ServiceCollectionExtensions.cs:103-109` wires `VerifiablePresentationValidator` with `OptOutIssuerKeyResolver` (always null) + `requireIssuerSignature:false` → `VerifiablePresentationValidator.cs:~196-202` logs a warning and returns **plain success** on the holder→device chain alone. | A citizen doing a doorstep verify sees "valid" though the issuer signature was never cryptographically checked. **Offline, citizen-side** — not the authoritative server gate (Blueprint Service + desk `Sorcha.Verifier` correctly default `requireIssuerSignature:true` under F120). |
| M3a | OIDC ID-token signature not validated | `Sorcha.Tenant.Service/Services/OidcExchangeService.cs:298-301` skips JWS signature validation with a `TODO`. Confirmed **authorization code flow** — the ID token is fetched server-side from the token endpoint over TLS (`ExchangeCodeAsync` → `PostAsync(config.TokenEndpoint…)`), and `iss`/`aud`/`exp`/`nonce` **are** validated. | Spec-permitted for code flow over TLS (OIDC Core §3.1.3.7), so lower-risk than it first appears — but it's a `TODO`/footgun and not defense-in-depth. |
| M3b | Passkey-recovery WebAuthn assertion is a no-op | `Sorcha.Wallet.Service/Services/Implementation/PasskeyRecoveryService.cs:83-90` re-encrypts the wallet key after only checking the wrap exists + credential id matches; the WebAuthn assertion is a `TODO`. The sibling `OrgRecoveryService` is the same shape ("until signature verification is implemented"). **Both recovery endpoints are feature-gated OFF** (`Features:WalletRecoveryEnabled`, `WalletEndpoints.cs:~2084/2127`). | Latent (gated off), not live — but a footgun: flipping the flag on would authorize recovery without real cryptographic proof. |

### Guiding principle

Verify the signature for real where you can; where you genuinely cannot (offline doorstep), **be honest about it** in the result rather than returning plain success; and make a latent no-op verification **fail loud** so it can't be silently switched on.

---

## 2. Design

### 2.1 H3 — PWA offline-verifier honesty (`Sorcha.Verifier.Engine` + `Sorcha.Wallet.Pwa`)

The chosen scope is **honest-result + document now** (online issuer verification is backlogged).

- **`VerificationOutcome` gains an explicit issuer-signature status** (`Sorcha.Verifier.Engine`) — `IssuerSignatureStatus` (`Verified` / `NotVerified`). The validator sets `Verified` when it resolves an issuer key and the JWS verifies; `NotVerified` on the existing "key unresolved + `requireIssuerSignature:false`" accept branch. **Additive — no behaviour change for the desk verifier or Blueprint Service**: they run `requireIssuerSignature:true`, so they either reach `Verified` or reject (the `NotVerified`-and-still-succeed state is reachable only when `requireIssuerSignature:false`, i.e. the PWA).
- **PWA `RealVerifierEngine` threads the status** into its verify-outcome model; the doorstep-verify UI surfaces it honestly — "Issuer verified ✓" vs "Issuer **not** verified (offline — reduced assurance) ⚠". A citizen never sees a bare "valid" when the issuer was not checked. Holder→device chain + status-list checks are unchanged.
- **Keep `requireIssuerSignature:false` for the PWA** — the fix is honesty, not breaking offline doorstep verification. Document the offline doorstep as a deliberate scoped reduced-assurance exception (PWA README + a note in the `verifiable-credentials` skill).

### 2.2 M3a — OIDC ID-token signature (`OidcExchangeService`)

Add JWS signature validation **before** trusting ID-token claims, as a new step alongside the existing checks:

- Fetch the IdP's **JWKS** from `config.JwksUri` (fallback: discovery via `config.MetadataUrl` / `DiscoveryDocumentJson`), **cache** it with a TTL + key-rotation tolerance, and verify the ID token's JWS signature (matching the token `kid`) using `Microsoft.IdentityModel.Tokens` (`JsonWebKeySet` → `SecurityKey`s; validate signature only — `ValidateIssuerSigningKey:true`, `RequireSignedTokens:true`, other validations left to the existing manual `iss`/`aud`/`exp` + `nonce` checks, which stay with their precise error messages).
- `ValidateIdTokenAsync` becomes genuinely **async** (JWKS fetch) — it reuses the service's existing `HttpClient` and a JWKS cache (keyed by `JwksUri`).
- **Fail-closed**: JWKS unfetchable, no key matches the `kid`, signature invalid, or `JwksUri` unconfigured → reject the exchange with a clear error. Remove the misleading `TODO`.

Dependencies already present in `Sorcha.Tenant.Service`: `Microsoft.AspNetCore.Authentication.OpenIdConnect`, `System.IdentityModel.Tokens.Jwt`, `Microsoft.IdentityModel.Tokens`. No new package required (exact JWKS-fetch/cache mechanism finalized in planning — direct `JsonWebKeySet` fetch vs `ConfigurationManager<OpenIdConnectConfiguration>`).

### 2.3 M3b — recovery fail-loud guard (`PasskeyRecoveryService` + `OrgRecoveryService`)

Make the unverified unwrap path **throw `NotSupportedException`** with a clear message in both gated-off recovery services, so enabling `Features:WalletRecoveryEnabled` cannot silently authorize recovery without real proof. The full WebAuthn assertion (passkey) and org-signature verification are deferred to the wallet-recovery feature build (backlog). Both services are bundled because they share the exact "incomplete verification behind the recovery flag" shape; guarding only one would leave an inconsistent footgun.

---

## 3. Testing (TDD)

- **H3** — validator unit tests: `IssuerSignatureStatus == NotVerified` on the unresolved-key + `requireIssuerSignature:false` branch (still `IsValid`); `== Verified` when a key resolves and the issuer JWS verifies; `requireIssuerSignature:true` + unresolved → reject (unchanged). PWA-side: `RealVerifierEngine` maps the status into its outcome (and the UI label where unit-testable).
- **M3a** — `ValidateIdTokenAsync`: a token signed by the test JWKS key passes; a tampered/wrong-key/unsigned token is rejected; JWKS-fetch failure → reject; `iss`/`aud`/`exp`/`nonce` still enforced. Use a locally-generated signing key + matching JWKS.
- **M3b** — the gated recovery path throws `NotSupportedException` for both passkey and org recovery.
- **Test-runner note (MTP):** `dotnet test --filter` ignored; build + test scoped to each affected test project (`Sorcha.Verifier.Engine` tests / `Sorcha.Wallet.Pwa` tests, `Sorcha.Tenant.Service.Tests`, `Sorcha.Wallet.Service.Tests`).

---

## 4. Increments & delivery

One focused PR, three separable commits, each built + tested against the affected test project before commit:

1. **H3** — `VerificationOutcome` issuer-signature status + PWA surfacing + docs.
2. **M3a** — OIDC JWKS signature validation.
3. **M3b** — recovery fail-loud guards.

Push → open PR → merge on green **claude-review** (full-solution `build-and-test`/`test` stay red on the unrelated Refit-cert / Playwright infra issues).

Documentation sync on merge: `verifiable-credentials` skill (H3 offline-exception note + the `VerificationOutcome` status), `docs/guides/AUTHENTICATION-SETUP.md` if it documents the OIDC exchange, and the PWA README.

---

## 5. Out of scope / backlog (named, not dropped)

- **§5.1 — two VC verification stacks** (`Sorcha.Verifier.Engine.VerifiablePresentationValidator` with its own SD-JWT parsing + `IIssuerKeyResolver` + `RequireIssuerSignature` bool, vs the F135 unified `ITrustEvaluator`; SD-JWT compact-form splitting reimplemented in 3 places). Consolidating onto `ITrustEvaluator` (or at minimum collapsing the 3 splitters) is a medium-term architectural refactor (review improvement #11) — **its own future sub-project**, too large and risky to bundle with these correctness fixes.
- **H3 enhancement** — online issuer verification in the PWA via a consumer/anonymous DID-backed resolver over the public `GET /orgs/{id}/did.json`, so issuer signatures verify when the PWA is connected (offline still falls back to the labelled reduced-assurance result).
- **M3b enhancement** — full WebAuthn assertion (passkey) + org-signature (org) recovery verification, built alongside the real wallet-recovery feature.

---

## 6. Key decisions (settled during brainstorming)

1. **H3 = honest-result + document now**, online issuer verification backlogged — the actual defect is the *silent accept*; surfacing an honest issuer-verification status closes it without new network dependencies, and offline doorstep verification stays usable.
2. **M3a = implement JWKS validation** (not document-the-exception) — removes the TODO/footgun and is defense-in-depth even though the code flow makes the current behaviour spec-defensible.
3. **M3b = fail-loud guard now**, full WebAuthn backlogged — the feature is gated off; a loud failure prevents accidental enablement without rebuilding the whole recovery feature.
4. **§5.1 consolidation deferred** to its own sub-project.
