# Phase 1 Contracts: SD-JWT VC HAIP Hardening

**Feature**: 094-sdjwt-haip-hardening

## Scope

This spec extends library contracts and adds two internal Wallet Service endpoints. No public HTTP wire format changes beyond additive optional fields on existing request DTOs.

## Library contracts (`Sorcha.Cryptography.SdJwt`)

### `ISdJwtService.CreateTokenAsync` — new overload

Extended to accept a holder JWK and a JSON-Pointer-path disclosable list.

```csharp
Task<SdJwtToken> CreateTokenAsync(
    Dictionary<string, object> claims,
    IEnumerable<string>? disclosablePaths,  // mix of bare names and JSON Pointer paths
    string issuer,
    string subject,
    byte[] signingKey,
    string algorithm,
    DateTimeOffset? expiresAt = null,
    JsonWebKey? holderJwk = null,  // NEW — when present, embeds cnf.jwk in the payload
    CancellationToken cancellationToken = default);
```

**Backward compatibility**: the existing `CreateTokenAsync` signature with `disclosableClaims` (top-level names only) remains as an overload that delegates to the new method with `holderJwk: null`.

### `ISdJwtService.CreatePresentationAsync` — new overload

Extended to accept a KB-JWT signing delegate.

```csharp
Task<SdJwtPresentation> CreatePresentationAsync(
    string rawToken,
    IEnumerable<string> claimsToDisclose,  // JSON Pointer paths or bare names
    KbJwtSigningDelegate? kbJwtSigner = null,  // NEW
    string? audience = null,
    string? nonce = null,
    CancellationToken cancellationToken = default);

public delegate Task<byte[]> KbJwtSigningDelegate(
    byte[] signingInput,
    CancellationToken ct);
```

**Behaviour:**
- If the credential's payload contains `cnf`, the delegate MUST be supplied or the call fails.
- If the credential has no `cnf`, the delegate is ignored and no KB-JWT is appended.
- The delegate receives the KB-JWT signing input (header + payload concatenated as `base64url(header).base64url(payload)`) and returns the raw signature bytes.

### `ISdJwtService.VerifyPresentationAsync` — extended behaviour

```csharp
Task<SdJwtVerificationResult> VerifyPresentationAsync(
    string rawPresentation,
    byte[] issuerPublicKey,
    string algorithm,
    CancellationToken cancellationToken = default);
```

**New behaviour:**
- Splits the presentation: `[jwt][disclosures...][optional-kb-jwt]`.
- If the credential JWT contains `cnf`, the last `~`-separated segment MUST be a valid KB-JWT signed by the key in `cnf.jwk`. Checks `aud`, `nonce`, `iat` (±60 s skew), `sd_hash`.
- If `cnf` is absent, the optional trailing segment is ignored.
- `SdJwtVerificationResult` gains a new `HolderKeyVerified` flag (true only when KB-JWT verification passes).

### `NestedDisclosure` — new helper class

```csharp
public static class NestedDisclosure
{
    // Translates top-level claims + disclosable paths into a payload with nested _sd arrays
    // and a list of serialised disclosure strings ready for wire appending.
    public static (Dictionary<string, object> redactedPayload, List<string> disclosures) Translate(
        Dictionary<string, object> claims,
        IEnumerable<string> disclosablePaths);

    // Reconstructs a fully-disclosed claims dict from a redacted payload and the presented disclosures.
    public static Dictionary<string, object> Reconstruct(
        Dictionary<string, object> redactedPayload,
        IEnumerable<string> presentedDisclosures);
}
```

## Wallet Service contracts

### `IHolderBindingKeyService` — new

```csharp
public interface IHolderBindingKeyService
{
    Task<JsonWebKey> GetPublicJwkAsync(string walletAddress, CancellationToken ct = default);
    Task<byte[]> SignKbJwtAsync(string walletAddress, byte[] signingInput, CancellationToken ct = default);
}
```

Derives the key under `sorcha:credential-holder-binding`. Default algorithm: Ed25519.

### `IHaipIssuerCoKeyService` — new

```csharp
public interface IHaipIssuerCoKeyService
{
    Task<(byte[] privateKey, string algorithm, JsonWebKey publicJwk)> GetSigningKeyForHaipIssuanceAsync(
        string walletAddress, CancellationToken ct = default);
}
```

Returns the primary key if classical, or derives the co-key under `sorcha:haip-issuer-signing` if the primary is PQC. Fails with a clear capability-missing error if the wallet is PQC-primary and not `HaipIssuer`-flagged.

### Wallet HTTP contracts — additive

`GET /api/v1/wallets/{address}/holder-binding-key` — returns the public JWK. Anonymous reads (or `CanReadWallet`, TBD during planning).

`POST /api/v1/wallets/{address}/holder-binding-key/sign-kb-jwt` — internal only, requires `CanManageWallets`. Body: `{ "signingInput": "base64" }`. Response: `{ "signature": "base64" }`.

`IssueCredentialRequest` gains an optional `HolderJwk` field (JWK shape). When present, the issuer embeds `cnf.jwk`. When absent, no `cnf`.

## Blueprint contracts — additive

### `CredentialIssuanceBuilder.MakeDisclosablePath(jsonPointer)` — new method

```csharp
public CredentialIssuanceBuilder MakeDisclosablePath(string jsonPointer);
```

Adds a JSON Pointer path (e.g. `/address/locality`, `/qualifications/0`) to the disclosable set. The existing `MakeDisclosable(name)` continues to work for top-level name-keyed fields.

## Wire format impacts

- **Existing endpoints**: no wire format changes beyond new optional fields on `IssueCredentialRequest`.
- **New internal endpoints**: `holder-binding-key` GET and POST, under `/api/v1/wallets/{address}/`, both documented in Scalar.
- **Credential payload**: gains `cnf` (non-disclosable top-level claim) on new credentials. Nested `_sd` arrays at various depths.
- **Presentation wire**: gains optional trailing KB-JWT segment after the last `~`.

All additions preserve backward compatibility with pre-fix credentials and legacy callers.
