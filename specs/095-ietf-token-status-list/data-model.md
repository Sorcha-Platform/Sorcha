# Phase 1 Data Model: IETF Token Status List

**Feature**: 095-ietf-token-status-list

## Entities and claim shapes

### 1. IETF Token Status List JWT (new, served at a new endpoint)

Wire format: a JWS Compact Serialization (header.payload.signature, no tilde).

**JOSE header:**
```json
{
  "typ": "statuslist+jwt",
  "alg": "EdDSA"
}
```

**Payload:**
```json
{
  "iss": "did:sorcha:org:{issuerWalletAddress}",
  "sub": "https://deployment/api/v1/credentials/ietf-status-lists/{listId}",
  "iat": 1712700000,
  "exp": 1712703600,
  "ttl": 300,
  "status_list": {
    "bits": 1,
    "lst": "base64url-zlib-compressed-bitstring"
  }
}
```

**Semantics:**
- `bits` is 1 for revocation-only lists (W3C parity) or 2 for 2-bit lists carrying revocation + suspension in a shared bitstream. This spec ships with 1-bit default to match the existing W3C behaviour.
- `lst` is the base64url encoding of the zlib-compressed raw bitstring. `Decompress(base64UrlDecode(lst))` MUST equal `Decompress(base64UrlDecode(w3cEncodedList))` for the same list.
- `ttl` is the cache TTL in seconds (default 300, matching W3C endpoint).
- `exp` is the envelope expiry — rolling, not the credentials' lifetimes.
- Signed with the list issuer's classical signing key (spec 094's `IHaipIssuerCoKeyService` return value).

### 2. `status.status_list` credential claim (new, inside signed SD-JWT payload)

HAIP-path credentials embed this claim at the top level of the signed payload.

```json
{
  "iss": "did:sorcha:org:...",
  "iat": ...,
  "vct": "ShortTermLetLicense",
  "status": {
    "status_list": {
      "idx": 42,
      "uri": "https://deployment/api/v1/credentials/ietf-status-lists/{listId}"
    }
  }
}
```

**Semantics:**
- Non-disclosable top-level claim (not in `_sd`).
- Embedded only by HAIP-path issuance. Internal-path issuance continues to embed W3C `credentialStatus` (spec 093).
- `idx` is the allocated index in the backing bitstring.
- `uri` is the IETF endpoint URL.

### 3. Raw bitstring (existing, accessor refactor)

`BitstringStatusList` already exists in the Blueprint Service. This spec adds a `GetRawBitstringBytesAsync(listId, ct)` accessor on `IStatusListManager` that returns the uncompressed bitstring bytes. Both envelope handlers call this accessor, then compress with their respective algorithm (gzip for W3C, zlib for IETF).

### 4. `StatusClaimForm` request enum (new, on `IssueCredentialRequest`)

New optional field on the existing DTO:

```csharp
public enum StatusClaimForm
{
    W3cBitstringStatusListEntry = 0,  // default, matches spec 093
    IetfTokenStatusList = 1           // new, HAIP-path
}

public class IssueCredentialRequest
{
    // ... existing fields ...
    public StatusClaimForm StatusClaimForm { get; init; } = StatusClaimForm.W3cBitstringStatusListEntry;
}
```

**Semantics:**
- Default value preserves spec 093 behaviour for callers that don't specify.
- HAIP-path callers (spec 097, future) will explicitly set `IetfTokenStatusList`.
- A single credential carries exactly one claim form — selected at issuance and never changed.

## Endpoint contracts

### New public endpoint

`GET /api/v1/credentials/ietf-status-lists/{listId}`
- Public, anonymous, cacheable (`Cache-Control: public, max-age=300`)
- Returns the IETF Token Status List JWT as `application/statuslist+jwt` content type
- 200 on success, 404 only when the listId has never been provisioned (vs empty list which returns the JWT with all-zero `lst`)

### Unchanged existing endpoint

`GET /api/v1/credentials/status-lists/{listId}` — the existing W3C Bitstring Status List endpoint is preserved unchanged in wire format and behaviour.

## Validation rules

- Raw bitstring bytes are the single source of truth; compression is deterministic per algorithm.
- Byte-identity between W3C and IETF decompressed outputs is a test invariant (SC-004).
- `status.status_list.idx` MUST be within the list's capacity (inherited from W3C FR-012 minimum 131,072).
- IETF JWT `exp` MUST be at least 60 seconds in the future to allow for clock skew at fetch time.

## Migration notes

None. No new persisted entities. `StatusClaimForm` is an optional request field; existing callers don't need updating.
