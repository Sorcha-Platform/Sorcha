# Phase 1 Contracts: Credential & Presentation Security Fixes

**Feature**: 093-vc-security-fixes

## Scope note

This spec makes **no wire-format changes to any HTTP endpoint**. Per spec NFR-1 and NFR-2, request and response shapes on `CredentialEndpoints` (`/api/v1/wallets/{walletAddress}/credentials/*`) and `PresentationEndpoints` (`/api/v1/presentations/*`) are byte-for-byte identical before and after the fix. The OpenAPI schema at `/openapi/v1.json` exposed by the Wallet Service is unchanged except for prose descriptions on affected endpoints.

There are therefore **no new OpenAPI contracts, no new gRPC protos, no new service-client method signatures** in this spec.

## What does change

Three internal contracts tighten without changing their externally visible shape:

### 1. `ISdJwtService.VerifyPresentationAsync` is now actually called by `PresentationRequestService`

**Before (on master)**: `PresentationRequestService.VerifyPresentationAsync` does not call any `ISdJwtService` method. The `vpToken` parameter on the submit endpoint is stored on the request record but never cryptographically validated.

**After (this spec)**: `PresentationRequestService.VerifyPresentationAsync` calls `ISdJwtService.VerifyPresentationAsync(request.VpToken, issuerPublicKey, algorithm, ct)` before any claim extraction or constraint check. Signature-invalid tokens fail fast with a signature error. Claim values in the verification result come from the verified token, not from the server-side credential row.

**Contract tightening** (not a shape change):

- Input: unchanged — `PresentationRequest`, credentialId, disclosedClaims, vpToken.
- Output: `VerificationResult` — same type, but `VerifiedClaims` now contains only claims disclosed in the verified `vpToken`, and `Errors` can contain new error kinds (`SignatureInvalid`, `IssuerMismatch`, `DisclosureIntegrityFailure`).
- Side effects: unchanged — writes a result to the in-memory presentation request record.

### 2. `Sorcha.Wallet.Service.Endpoints.CredentialEndpoints.IssueCredential` acquires a dependency on `IStatusListClient`

**Before**: The endpoint depends on `IWalletRepository`, `IKeyManagementService`, `ISdJwtService`, `ICredentialStore`, `ILoggerFactory`. It does not know about status lists. The Blueprint Service allocates an index **after** the endpoint returns.

**After**: The endpoint additionally depends on a new service-client interface `IStatusListClient` exposing the single method `Task<StatusListAllocation> AllocateIndexAsync(string issuerWallet, string registerId, string credentialId, CancellationToken ct)`. The endpoint calls this **before** signing. The returned `StatusListAllocation` carries `StatusListUrl`, `ListId`, and `Index`, which the endpoint embeds in the `credentialStatus` claim of the SD-JWT payload before passing it to `ISdJwtService.CreateTokenAsync`.

**Contract tightening**:

- Public wire shape on `POST /api/v1/wallets/{walletAddress}/credentials/issue`: unchanged.
- Internal DI container: `IStatusListClient` is registered in the Wallet Service's `ServiceCollectionExtensions` and implemented by a thin HTTP client in `Sorcha.ServiceClients.Http` that talks to the Blueprint Service's existing `POST /api/v1/credentials/status-lists/{listId}/allocate` endpoint.
- Behaviour when `IStatusListClient` is registered but unreachable: fail the issuance call with a 503-equivalent error per FR-008. Do not sign a token without a pointer.
- Behaviour when `CredentialStatus:EnableEmbedding = false` (configuration flag): `IStatusListClient` is not injected and the endpoint falls back to legacy behaviour (no `credentialStatus` claim in the payload, no row-level StatusListUrl). This is for pure-internal dev environments only; the default is `true`.

### 3. `SorchaDidResolver.ResolveWalletDidAsync` and `ResolveOrgDidAsync` emit valid multibase

**Before**: Both methods set `PublicKeyMultibase = $"z{wallet.PublicKey}"` where `wallet.PublicKey` is a hex-encoded raw key. Not valid multibase.

**After**: Both methods call a new helper `Multicodec.ToMultibasePublicKey(algorithm, rawPublicKeyBytes)` in `Sorcha.Cryptography.Utilities`. The helper encodes the multicodec prefix as an unsigned varint, concatenates with the raw key bytes, base58btc-encodes the result, and prefixes with `z`.

**Contract tightening**:

- Input: unchanged. The resolver takes the DID string as before.
- Output: `DidDocument` with a correctly encoded `VerificationMethod.PublicKeyMultibase`.
- Behaviour on unsupported algorithms: the resolver either sets `PublicKeyJwk` (JWK form) and leaves `PublicKeyMultibase` null, or fails closed with a clear error. The first-cut implementation does the JWK fallback because the field already exists on the `VerificationMethod` type.

## New internal helpers

### `Sorcha.Cryptography.Utilities.Multicodec`

```csharp
public static class Multicodec
{
    /// <summary>
    /// Returns multicodec-prefixed public key bytes for the given algorithm.
    /// The prefix is an unsigned varint encoding of the multicodec identifier.
    /// </summary>
    public static byte[] EncodePublicKey(WalletNetworks algorithm, byte[] rawKeyBytes);

    /// <summary>
    /// Returns a full W3C multibase public key string: 'z' + base58btc(multicodec || rawKey).
    /// Returns null for algorithms that do not have an assigned multicodec identifier.
    /// </summary>
    public static string? ToMultibasePublicKey(WalletNetworks algorithm, byte[] rawKeyBytes);
}
```

Internal implementation details (varint encoding, prefix lookup table) are not part of the contract.

### `Sorcha.ServiceClients.Http.StatusList.IStatusListClient`

```csharp
public interface IStatusListClient
{
    /// <summary>
    /// Allocates the next available index on the issuer's status list for the given credential.
    /// Fails with HttpRequestException on network errors and with InvalidOperationException on
    /// list-full or list-not-found responses.
    /// </summary>
    Task<StatusListAllocation> AllocateIndexAsync(
        string issuerWallet,
        string registerId,
        string credentialId,
        CancellationToken cancellationToken = default);
}

public record StatusListAllocation(
    string ListId,
    int Index,
    string StatusListUrl);
```

This is a thin HTTP wrapper around the existing Blueprint Service status list allocation endpoint. It reuses the consolidated `Sorcha.ServiceClients.Http` pattern and is registered via `AddServiceClients(configuration)`.

## Summary

No new public HTTP or gRPC contracts. Two new internal service-client / utility contracts. Three existing internal contracts tightened. Every change is behaviour-preserving at the wire level and behaviour-correcting below it.
