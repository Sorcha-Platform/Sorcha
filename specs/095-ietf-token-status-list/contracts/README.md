# Phase 1 Contracts: IETF Token Status List

**Feature**: 095-ietf-token-status-list

## New HTTP endpoint

### `GET /api/v1/credentials/ietf-status-lists/{listId}`

**Auth**: Anonymous (public, matches W3C endpoint)
**Cache-Control**: `public, max-age=300` (configurable)
**Content-Type**: `application/statuslist+jwt`

**200 Response** (JWT as plain text):
```
eyJhbGciOiJFZERTQSIsInR5cCI6InN0YXR1c2xpc3Qrand0In0.eyJpc3MiOiJkaWQ6c29yY2hhOm9yZzouLi4iLCJzdWIiOiJodHRwczovL2RlcGxveW1lbnQvLi4uIiwiaWF0IjoxNzEyNzAwMDAwLCJleHAiOjE3MTI3MDM2MDAsInR0bCI6MzAwLCJzdGF0dXNfbGlzdCI6eyJiaXRzIjoxLCJsc3QiOiJlTnJ0eHpFUkFDQU1BbU9HLi4uIn19.signature
```

**404 Response**: List ID does not exist and has not been provisioned. (Provisioned-but-empty lists return 200 with an all-zero `lst`.)

### Existing `GET /api/v1/credentials/status-lists/{listId}`

Unchanged. W3C Bitstring Status List Credential wire format preserved.

## Internal accessors

### `IStatusListManager.GetRawBitstringBytesAsync(listId, ct)` — new

Returns the uncompressed raw bitstring bytes for a given list. Both envelope serialisers call this accessor.

```csharp
Task<byte[]?> GetRawBitstringBytesAsync(string listId, CancellationToken ct = default);
```

Returns null if the list does not exist.

### `IetfTokenStatusListSerializer` — new class

```csharp
public interface IIetfTokenStatusListSerializer
{
    Task<string> SerializeAsync(
        BitstringStatusList list,
        string listEndpointUrl,
        CancellationToken ct = default);
}
```

**Behaviour**: builds the IETF JWT with `typ: "statuslist+jwt"`, zlib-compresses the raw bitstring, base64url-encodes, signs using the list issuer's classical signing key (via `IHaipIssuerCoKeyService` from spec 094).

## Wallet Service verifier extension

### `PresentationRequestService.TryExtractIetfStatusList` — new helper

Reads a `status.status_list` claim from the verified token's claims dict and returns `(uri, idx)` as a pair. Mirrors the spec 093 `TryExtractEmbeddedCredentialStatus` helper but for the IETF form.

The verifier's status check path (already extended in spec 093) gains a second try: prefer IETF `status.status_list` → fall back to W3C `credentialStatus` → fall back to server-side `CredentialEntity.StatusListUrl`/`StatusListIndex`.

### Verification flow update

```csharp
// Existing spec 093 logic extended:
var (ietfUrl, ietfIdx) = TryExtractIetfStatusList(verifiedTokenClaims);
if (ietfUrl != null && ietfIdx.HasValue)
{
    // Use IETF endpoint — verify the JWT envelope signature, decompress lst, read bit at idx
}
else
{
    // Fall back to spec 093 W3C path
    var (w3cUrl, w3cIdx) = TryExtractEmbeddedCredentialStatus(verifiedTokenClaims);
    // ... existing logic ...
}
```

## Wallet Service issuance extension

### `IssueCredentialRequest.StatusClaimForm` — new optional field

See data-model.md §4. Default `W3cBitstringStatusListEntry`. When `IetfTokenStatusList`, the `IssueCredential` handler embeds `status.status_list` instead of `credentialStatus` in the signed payload.

## Wire format impacts

- **New endpoint**: `GET /api/v1/credentials/ietf-status-lists/{listId}` — public, cacheable, returns signed JWT
- **Existing W3C endpoint**: unchanged
- **Signed credential payload**: HAIP-path credentials carry `status.status_list`; internal-path credentials keep `credentialStatus`
- **Presentation verifier**: reads either claim form with deterministic precedence (IETF > W3C > row fallback)
