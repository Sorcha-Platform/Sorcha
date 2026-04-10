# Phase 1 Data Model: OpenID4VCI Issuer Endpoint (HAIP)

**Feature**: 097-openid4vci-issuer
**Date**: 2026-04-10

## Entities and value objects

### 1. `CredentialOffer` (new, transient, Redis-stored)

Tracks an in-flight HAIP credential issuance from the moment a Blueprint action creates the offer until the external wallet redeems it or it expires. Stored in Redis as JSON with TTL-based expiry.

**Redis key pattern**: `haip:offer:{Id}`

```csharp
public class CredentialOffer
{
    public Guid Id { get; init; }
    public string PreAuthorizedCode { get; init; } = string.Empty;
    public string IssuerWalletAddress { get; init; } = string.Empty;
    public string CredentialType { get; init; } = string.Empty;
    public Dictionary<string, object> Claims { get; init; } = new();
    public List<string> DisclosablePaths { get; init; } = [];
    public CredentialOfferStatus Status { get; set; } = CredentialOfferStatus.Pending;
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset ExpiresAt { get; init; }
    public string? BlueprintActionId { get; init; }
    public int? StatusListIndex { get; init; }
    public string? IssuerCoKeyId { get; init; }
}

public enum CredentialOfferStatus
{
    Pending = 0,
    Redeemed = 1,
    Expired = 2,
    Cancelled = 3
}
```

**Fields:**

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `Id` | `Guid` | Yes | Unique offer identifier, generated at creation |
| `PreAuthorizedCode` | `string` | Yes | One-time-use code for the token endpoint exchange. Cryptographically random, URL-safe, minimum 32 bytes entropy |
| `IssuerWalletAddress` | `string` | Yes | Wallet address of the issuing organisation's classical HAIP signing key |
| `CredentialType` | `string` | Yes | The `vct` value identifying the credential type (e.g. `"ShortTermLetLicense"`) |
| `Claims` | `Dictionary<string, object>` | Yes | Pre-computed claim values from the Blueprint action's `ClaimMappings`. Frozen at offer creation; the credential endpoint does not remap |
| `DisclosablePaths` | `List<string>` | No | JSON Pointer paths for selective disclosure (spec 094 nested disclosure). Empty list means no selective disclosure |
| `Status` | `CredentialOfferStatus` | Yes | Lifecycle state. Transitions: `Pending -> Redeemed`, `Pending -> Expired`, `Pending -> Cancelled`. Terminal states are immutable |
| `CreatedAt` | `DateTimeOffset` | Yes | UTC creation timestamp |
| `ExpiresAt` | `DateTimeOffset` | Yes | UTC expiry. Default TTL is 5 minutes from creation |
| `BlueprintActionId` | `string?` | No | Originating Blueprint action identifier for audit and cancellation |
| `StatusListIndex` | `int?` | No | Pre-allocated IETF status list index (spec 095) for the credential to be issued |
| `IssuerCoKeyId` | `string?` | No | Selected classical co-key identifier (spec 094) for signing the credential |

**Validation rules:**
- `PreAuthorizedCode` must be non-empty and unique across all active offers
- `CredentialType` must be non-empty and <= 200 characters
- `Claims` must contain at least one entry
- `ExpiresAt` must be in the future at creation time
- `DisclosablePaths` entries must be valid JSON Pointer strings (starting with `/` for nested, or bare names for top-level)
- Status transitions are one-way: `Pending` is the only mutable state

**Relationships:**
- References an issuer wallet in Wallet Service via `IssuerWalletAddress`
- References a Blueprint action via `BlueprintActionId`
- Consumed by `HaipAccessToken` via `PreAuthorizedCode` exchange at the token endpoint
- Pre-allocates a status list slot (spec 095) via `StatusListIndex`

**Storage notes:**
- Redis JSON serialization via `System.Text.Json`
- TTL set to `ExpiresAt` + a configurable audit retention window (default 1 hour post-expiry) for garbage collection
- No EF Core entity or migration. The HAIP service is stateless except for Redis

---

### 2. `HaipAccessToken` (new, transient, Redis-stored)

Short-lived OAuth 2.0 Bearer token issued by the token endpoint in exchange for a pre-authorized code. Authorises calls to the credential endpoint and carries the `c_nonce` for JWT proof of possession binding.

**Redis key pattern**: `haip:token:{TokenId}`

```csharp
public class HaipAccessToken
{
    public Guid TokenId { get; init; }
    public Guid PreAuthorizedCodeId { get; init; }
    public string CNonce { get; set; } = string.Empty;
    public DateTimeOffset CNonceExpiresAt { get; set; }
    public string Scope { get; init; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; init; }
    public bool IsConsumed { get; set; }
}
```

**Fields:**

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `TokenId` | `Guid` | Yes | Unique token identifier. The Bearer token value sent to clients is an opaque encoding of this ID (not the raw GUID) |
| `PreAuthorizedCodeId` | `Guid` | Yes | Reference to the `CredentialOffer.Id` whose pre-authorized code was exchanged to create this token |
| `CNonce` | `string` | Yes | Current challenge nonce for JWT proof of possession. Cryptographically random, minimum 32 bytes entropy. Replaced on each nonce endpoint call |
| `CNonceExpiresAt` | `DateTimeOffset` | Yes | UTC expiry of the current `c_nonce`. Default 5 minutes |
| `Scope` | `string` | Yes | OAuth 2.0 scope. For HAIP pre-authorized code flow, always `"openid_credential"` |
| `ExpiresAt` | `DateTimeOffset` | Yes | UTC expiry of the access token itself. Default 5 minutes |
| `IsConsumed` | `bool` | Yes | Set to `true` after the credential endpoint successfully issues a credential. Prevents duplicate issuance unless the originating Blueprint action permits reissuance |

**Validation rules:**
- `PreAuthorizedCodeId` must reference an existing `CredentialOffer` in `Redeemed` status
- `CNonce` must be non-empty and unique
- `CNonceExpiresAt` must be <= `ExpiresAt` (nonce cannot outlive its parent token)
- `ExpiresAt` must be in the future at creation time
- Once `IsConsumed` is `true`, the credential endpoint rejects further requests with `invalid_request`

**Relationships:**
- Back-references `CredentialOffer` via `PreAuthorizedCodeId`
- `CNonce` is bound into the holder's JWT proof at the credential endpoint
- Refreshed via the nonce endpoint (FR-029): a new `CNonce` replaces the previous one and invalidates it

**Storage notes:**
- Redis JSON serialization via `System.Text.Json`
- TTL set to `ExpiresAt` (no audit retention needed for tokens; the credential offer carries the audit trail)
- The opaque Bearer token value sent on the wire is derived from `TokenId` using a service-local HMAC to prevent token ID guessing

---

### 3. `IssuerMetadata` (computed, not persisted)

HAIP 1.0 Section 5 issuer metadata document served at `/.well-known/openid-credential-issuer`. Assembled at request time from tenant configuration, enrolled HAIP issuer organisations, and their declared credential types. Cached with `Cache-Control` headers (default 1 hour TTL per FR-012).

```json
{
  "credential_issuer": "https://deployment.example.com",
  "credential_endpoint": "https://deployment.example.com/haip/credential",
  "token_endpoint": "https://deployment.example.com/haip/token",
  "nonce_endpoint": "https://deployment.example.com/haip/nonce",
  "display": [
    {
      "name": "Sorcha Deployment",
      "locale": "en"
    }
  ],
  "credentials_supported": [
    {
      "format": "vc+sd-jwt",
      "vct": "ShortTermLetLicense",
      "cryptographic_binding_methods_supported": ["jwk"],
      "credential_signing_alg_values_supported": ["ES256"],
      "display": [
        {
          "name": "Short-Term Let Licence",
          "locale": "en"
        }
      ],
      "claims": {
        "licenseType": { "display": [{ "name": "License Type", "locale": "en" }] },
        "propertyAddress": { "display": [{ "name": "Property Address", "locale": "en" }] }
      }
    }
  ]
}
```

**Fields:**

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `CredentialIssuer` | `string` (URL) | Yes | The public base URL of the HAIP service. Read from configuration (FR-004), not derived from the request |
| `CredentialEndpoint` | `string` (URL) | Yes | Absolute URL of the credential endpoint |
| `TokenEndpoint` | `string` (URL) | Yes | Absolute URL of the token endpoint |
| `NonceEndpoint` | `string` (URL) | Yes | Absolute URL of the nonce endpoint |
| `CredentialsSupported` | `array` | Yes | Array of credential format descriptors. Each entry declares `format` (`vc+sd-jwt`), `vct`, supported binding methods, signing algorithms, display metadata, and claim descriptions |

**Validation rules:**
- All endpoint URLs must be HTTPS in production (FR-010)
- Every `credentials_supported` entry must declare `format: "vc+sd-jwt"` (FR-008)
- Every entry must include `cryptographic_binding_methods_supported` containing at least `"jwk"` (FR-009)
- Every entry must include `credential_signing_alg_values_supported` containing at least `"ES256"` (FR-009)
- `vct` values must be unique across entries
- When no HAIP issuer is enrolled, `credentials_supported` is an empty array (or the endpoint returns 404, per deployment configuration)

**Relationships:**
- Derived from HAIP issuer organisations enrolled in Tenant Service
- Credential types originate from Blueprint `CredentialIssuanceConfig` entries with `TargetAudience: HaipExternalWallet`

**Companion document:** `/.well-known/oauth-authorization-server` (FR-011) declares `token_endpoint`, `grant_types_supported` (including `urn:ietf:params:oauth:grant-type:pre-authorized_code`), and `token_endpoint_auth_methods_supported`.

---

### 4. `CredentialRequest` (wire model, inbound)

Request body posted by the external wallet to the credential endpoint. Not persisted.

```json
{
  "format": "vc+sd-jwt",
  "vct": "ShortTermLetLicense",
  "proof": {
    "proof_type": "jwt",
    "jwt": "eyJhbGciOiJFZERTQSIsInR5cCI6Im9wZW5pZDR2Y2ktcHJvb2Yrand0IiwiandrIjp7Imt0eSI6Ik9LUCIsImNydiI6IkVkMjU1MTkiLCJ4IjoiLi4uIn19.eyJhdWQiOiJodHRwczovL2lzc3Vlci5leGFtcGxlIiwiaWF0IjoxNzEyNzAwMDAwLCJub25jZSI6ImFiYzEyMy4uLiJ9.signature"
  }
}
```

**Fields:**

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `Format` | `string` | Yes | Must be `"vc+sd-jwt"`. Any other value is rejected with `unsupported_credential_format` |
| `Vct` | `string` | No | Credential type identifier. When present, must match a type in `credentials_supported`. When absent, inferred from the access token's backing credential offer |
| `Proof` | `object` | Yes | JWT proof of possession object |
| `Proof.ProofType` | `string` | Yes | Must be `"jwt"` |
| `Proof.Jwt` | `string` | Yes | Compact-serialized JWT signed by the holder's key |

**JWT Proof structure:**

Header:
```json
{
  "alg": "EdDSA",
  "typ": "openid4vci-proof+jwt",
  "jwk": {
    "kty": "OKP",
    "crv": "Ed25519",
    "x": "base64url-public-key"
  }
}
```

Payload:
```json
{
  "aud": "https://issuer.example.com",
  "iat": 1712700000,
  "nonce": "abc123..."
}
```

**Validation rules (FR-035):**
- Proof header must contain `jwk` declaring the holder's public key
- Proof signature must verify against the declared `jwk`
- Proof `nonce` must match a `CNonce` currently associated with the access token
- Proof `aud` must match the HAIP service's `credential_issuer` URL
- Proof `iat` must be within +/- 60 seconds of server clock
- Supported holder key types: Ed25519 (`OKP`), NIST P-256 (`EC`), RSA (`RSA`)
- Failure returns `invalid_proof` with a specific error description (FR-036)

---

### 5. `TokenRequest` (wire model, inbound)

Form-encoded request body posted by the external wallet to the token endpoint. Not persisted.

```
POST /haip/token HTTP/1.1
Content-Type: application/x-www-form-urlencoded

grant_type=urn%3Aietf%3Aparams%3Aoauth%3Agrant-type%3Apre-authorized_code&pre-authorized_code=abc123...
```

**Fields:**

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `GrantType` | `string` | Yes | Must be `"urn:ietf:params:oauth:grant-type:pre-authorized_code"`. Any other value returns `unsupported_grant_type` |
| `PreAuthorizedCode` | `string` | Yes | The one-time code extracted from the Credential Offer URI. Missing or empty returns `invalid_request` |
| `TxCode` | `string` | No | User-presented transaction code for additional binding. Reserved for future use; not required by HAIP 1.0 MTI |

**Validation rules:**
- `GrantType` must exactly match the pre-authorized code grant type URI
- `PreAuthorizedCode` must match an active (non-expired, non-consumed) `CredentialOffer`
- A code that has already been exchanged returns `invalid_grant` (FR-018)
- A code whose TTL has elapsed returns `invalid_grant` (FR-019)
- Content-Type must be `application/x-www-form-urlencoded` per OAuth 2.0

---

### 6. `TokenResponse` (wire model, outbound)

JSON response returned by the token endpoint on successful pre-authorized code exchange. Not persisted.

```json
{
  "access_token": "eyJhbGciOi...",
  "token_type": "Bearer",
  "expires_in": 300,
  "c_nonce": "abc123...",
  "c_nonce_expires_in": 300
}
```

**Fields:**

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `AccessToken` | `string` | Yes | Opaque Bearer token. Derived from `HaipAccessToken.TokenId` via service-local HMAC |
| `TokenType` | `string` | Yes | Always `"Bearer"` |
| `ExpiresIn` | `int` | Yes | Access token lifetime in seconds (default 300 = 5 minutes) |
| `CNonce` | `string` | Yes | Challenge nonce for the holder's JWT proof of possession |
| `CNonceExpiresIn` | `int` | Yes | Nonce lifetime in seconds (default 300 = 5 minutes) |

**Validation rules:**
- `ExpiresIn` must be > 0
- `CNonceExpiresIn` must be > 0 and <= `ExpiresIn`
- Response must use `Cache-Control: no-store` and `Pragma: no-cache` per OAuth 2.0

**Error responses** (standard OAuth 2.0 format):

| Error | Condition |
|-------|-----------|
| `invalid_grant` | Code already consumed, expired, cancelled, or not found |
| `invalid_request` | Missing required parameter or malformed request |
| `unsupported_grant_type` | Grant type is not `pre-authorized_code` |

---

### 7. `TargetAudience` enum (extended on existing `CredentialIssuanceConfig`)

New field on the Blueprint Service's `CredentialIssuanceConfig` model that controls which issuance path runs at action execution time.

```csharp
public enum TargetAudience
{
    SorchaInternal = 0,
    HaipExternalWallet = 1
}
```

Added to `CredentialIssuanceConfig`:

```csharp
public class CredentialIssuanceConfig
{
    // ... existing fields (CredentialType, ClaimMappings, RecipientParticipantId, etc.) ...

    /// <summary>
    /// Controls whether the credential is issued to a Sorcha participant wallet
    /// (SorchaInternal, default) or to an external HAIP-conformant wallet
    /// (HaipExternalWallet) via the OpenID4VCI pre-authorized code flow.
    /// </summary>
    [JsonPropertyName("targetAudience")]
    public TargetAudience TargetAudience { get; set; } = TargetAudience.SorchaInternal;
}
```

**Values:**

| Value | Behaviour |
|-------|-----------|
| `SorchaInternal` (0, default) | Existing internal issuance path runs unchanged. Credential is written to the recipient's Sorcha wallet row. No HAIP interaction |
| `HaipExternalWallet` (1) | Blueprint action calls HAIP service to create a `CredentialOffer`. Returns a `CredentialOfferUri` in the action execution result. Credential is minted later when the external wallet completes the OpenID4VCI flow |

**Validation rules:**
- When `HaipExternalWallet`, `RecipientParticipantId` is advisory (display/audit only), not a binding constraint (FR-048)
- When `HaipExternalWallet`, the Blueprint action must NOT pre-write a Sorcha-internal credential row (FR-047)
- When absent or `SorchaInternal`, all existing behaviour is preserved (FR-049)
- `DisclosablePaths` (spec 094 nested disclosure) are honoured identically for both paths (FR-050)

**Relationships:**
- Part of the existing `CredentialIssuanceConfig` model in `Sorcha.Blueprint.Models/Credentials/`
- Drives the routing decision in `CredentialIssuanceHandler` (or equivalent action executor)
- `HaipExternalWallet` produces a `CredentialOfferUri` on the action execution result

---

## Entity relationship summary

```
CredentialIssuanceConfig (Blueprint)
    |
    | TargetAudience = HaipExternalWallet
    |
    v
CredentialOffer (Redis)
    |
    | PreAuthorizedCode exchange
    |
    v
HaipAccessToken (Redis)
    |
    | JWT Proof + CNonce binding
    |
    v
SD-JWT VC (issued, returned to wallet)
    |-- cnf.jwk from CredentialRequest.Proof
    |-- claims from CredentialOffer.Claims
    |-- x5c from spec 096
    |-- status.status_list from spec 095
```

## Redis key layout

| Pattern | TTL | Purpose |
|---------|-----|---------|
| `haip:offer:{offerId}` | `ExpiresAt` + retention window | Credential Offer lifecycle |
| `haip:code:{preAuthorizedCode}` | Same as parent offer | Reverse lookup: code -> offer ID. Enables O(1) code validation at the token endpoint |
| `haip:token:{tokenId}` | `ExpiresAt` | Access token + c_nonce state |
| `haip:nonce:{cNonce}` | `CNonceExpiresAt` | Reverse lookup: nonce -> token ID. Enables O(1) nonce validation at the credential endpoint |

All keys use JSON serialization via `System.Text.Json`. TTL-based expiry handles garbage collection for the common path; the audit retention window on offers ensures expired-but-recent offers remain queryable for debugging.

## Migration notes

None required. The HAIP service is stateless except for Redis. No EF Core entities, no PostgreSQL tables, no MongoDB collections. The only schema change is the additive `TargetAudience` field on the existing `CredentialIssuanceConfig` model, which is a JSON-serialized Blueprint model with no database migration impact.
