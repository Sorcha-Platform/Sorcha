# Quickstart: verifying Verification-correctness

Verification is by automated tests (no manual service run required).

## Run the tests (per affected project — MTP ignores `--filter`, runs the whole project)

```powershell
# H3 — engine status + PWA Warn mapping
dotnet test tests/Sorcha.Verifier.Tests/Sorcha.Verifier.Tests.csproj
dotnet test tests/Sorcha.Wallet.Pwa.Tests/Sorcha.Wallet.Pwa.Tests.csproj
# M3a — OIDC JWKS signature validation
dotnet test tests/Sorcha.Tenant.Service.Tests/Sorcha.Tenant.Service.Tests.csproj
# M3b — recovery fail-loud
dotnet test tests/Sorcha.Wallet.Service.Tests/Sorcha.Wallet.Service.Tests.csproj
```

## What "done" looks like (maps to Success Criteria)

- **SC-001 / SC-002 (H3)** — validator returns `IssuerSignature=NotVerified` (still `Accepted`) when the issuer key is unresolved and not required, `Verified` when it resolves + checks; `RealVerifierEngine` maps the former to `Warn` and the latter to `Pass`; a server-config (`requireIssuerSignature=true`) test shows the accepted path is always `Verified`.
- **SC-003 / SC-005 (M3a)** — a token signed by the test JWKS key passes; tampered/wrong-key/unsigned tokens and JWKS-fetch failure are rejected; iss/aud/exp/nonce checks still enforced.
- **SC-004 (M3b)** — with recovery enabled, both recovery paths throw `NotSupportedException` without re-keying.
- **SC-006** — the four project suites pass.

## Manual sanity (optional)

- H3: in the PWA doorstep verify flow, present a credential whose issuer DID isn't resolvable offline → the trust panel shows the **amber Warn** state with "issuer not verified", not a green Pass.
- M3a: point a dev IdP config at a JWKS that does not contain the token's signing key → social login is refused.

## No regressions

- Server-side credential verification (Blueprint Service, desk `Sorcha.Verifier`) unchanged — they require and verify the issuer signature.
- Social-login with a correctly-signed token + valid claims still succeeds.
- Wallet recovery remains disabled by its feature flag.
