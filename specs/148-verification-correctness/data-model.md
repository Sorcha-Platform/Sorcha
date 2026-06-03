# Phase 1 Data Model: Verification-correctness

No persistent data or schema changes. The "model" is the in-memory verification-outcome shape, the OIDC signature-validation step, and the recovery guard.

## New type — `IssuerSignatureStatus` (enum, `Sorcha.Verifier.Engine.Models`)

| Value | Meaning |
|-------|---------|
| `Verified` | The credential's issuer JWS was checked against a resolved issuer key and is valid. |
| `NotVerified` | The issuer key could not be resolved and the verifier does not require it (offline / reduced-assurance path). The credential was accepted on the holder→device chain + status list alone. |

## Changed type — `VerificationOutcome` (record, `Sorcha.Verifier.Engine.Models`)

Add one **non-required** property (default `NotVerified`) so existing construction sites compile unchanged:

```csharp
public IssuerSignatureStatus IssuerSignature { get; init; } = IssuerSignatureStatus.NotVerified;
```

- Set to `Verified` only on the success path when the issuer JWS verified (`VerifiablePresentationValidator.cs:181-188` branch taken).
- Remains `NotVerified` on the accept-on-chain branch (`:196-202`) and on `Failure` (irrelevant when `!Accepted`).
- **Invariant:** for verifiers with `requireIssuerSignature == true`, an `Accepted` outcome always has `IssuerSignature == Verified` (the only accept path runs through the verified branch; the unresolved-key branch rejects).

## PWA mapping — `RealVerifierEngine.Map` (`VerifyOutcome`)

| `VerificationOutcome` | → `VerifyOutcome` | UI |
|-----------------------|-------------------|-----|
| `Accepted && IssuerSignature == Verified` | `Pass` | green / "verified" |
| `Accepted && IssuerSignature == NotVerified` | `Warn` + message "Issuer not verified — offline / reduced assurance" | amber / warn (existing `VerificationTrustView` rendering) |
| `!Accepted` | `Fail` | red |

## OIDC signature validation — `OidcExchangeService.ValidateIdTokenAsync`

New step, inserted before claim extraction, after the existing iss/aud/exp/nonce checks (or before them — order is not security-significant as long as all run and any failure rejects):

- Inputs: the raw ID token, `IdentityProviderConfiguration` (`JwksUri` / `MetadataUrl` / `IssuerUrl`), the token `kid` header.
- Resolve signing keys: cached JWKS keyed by `JwksUri` (or discovered jwks_uri); refresh once on `kid` miss (rotation).
- Verify: JWS signature only, against the resolved keys (`RequireSignedTokens = true`, `ValidateIssuerSigningKey = true`).
- Outcomes: signature valid → continue; invalid / no matching `kid` / keys unobtainable / key location unconfigured → throw `InvalidOperationException` (reject the exchange, fail-closed).
- The method becomes `async` (JWKS fetch).

## Recovery guard — `PasskeyRecoveryService` / `OrgRecoveryService`

At the point each service would unwrap/re-key without its cryptographic proof:

```csharp
throw new NotSupportedException(
    "Passkey recovery requires WebAuthn assertion verification, which is not implemented. " +
    "Keep Features:WalletRecoveryEnabled disabled until it is.");
```

(org variant names the org-recovery-key signature.) No state mutation occurs before the throw.

## Invariants

- INV-1: Server-side verifiers (`requireIssuerSignature:true`) show no behaviour change — `Accepted ⇒ IssuerSignature == Verified`.
- INV-2: The PWA never presents `Pass` when the issuer was not verified — it presents `Warn`.
- INV-3: A social-login exchange never trusts ID-token claims whose signature was not verified (fail-closed).
- INV-4: With recovery enabled, neither recovery path mutates wallet state before throwing.
