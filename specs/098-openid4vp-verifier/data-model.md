# Phase 1 Data Model: OpenID4VP Verifier Endpoint (HAIP)

**Feature**: 098-openid4vp-verifier
**Date**: 2026-04-11

## Entities and value objects

### 1. `PresentationRequest` (new, transient, Redis-stored)

Tracks an in-flight HAIP verification from the moment a Blueprint action (or direct API call) creates the request until the external wallet submits a presentation or the request expires. Stored in Redis as JSON with TTL-based expiry.

**Redis key pattern**: `haip:vp-request:{Id}`

```csharp
public class PresentationRequest
{
    public Guid Id { get; init; }
    public string Nonce { get; init; } = string.Empty;
    public string State { get; init; } = string.Empty;
    public string ClientId { get; init; } = string.Empty;
    public string ResponseUri { get; init; } = string.Empty;
    public string RequestUri { get; init; } = string.Empty;
    public string SignedRequestObject { get; init; } = string.Empty;
    public PresentationDefinition PresentationDefinition { get; init; } = new();
    public string VerifierWalletAddress { get; init; } = string.Empty;
    public string? BlueprintActionId { get; init; }
    public string? BlueprintInstanceId { get; init; }
    public PresentationRequestStatus Status { get; set; } = PresentationRequestStatus.Pending;
    public VerificationResult? Result { get; set; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset ExpiresAt { get; init; }
}

public enum PresentationRequestStatus
{
    Pending = 0,
    Submitted = 1,
    Verified = 2,
    Denied = 3,
    Expired = 4,
    Cancelled = 5
}
```

**Fields:**

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `Id` | `Guid` | Yes | Unique request identifier, generated at creation |
| `Nonce` | `string` | Yes | Unique, unguessable challenge nonce bound into the KB-JWT by the wallet. Cryptographically random, minimum 32 bytes entropy |
| `State` | `string` | Yes | Opaque state token linking the `direct_post` callback to this request. Cryptographically random, minimum 16 bytes entropy |
| `ClientId` | `string` | Yes | The verifier's HAIP identifier. For `x509_san_uri` scheme, this is the SAN URI from the verifier's leaf certificate (e.g. `did:sorcha:org:{walletAddress}`) |
| `ResponseUri` | `string` | Yes | Absolute HTTPS URL of the `direct_post` callback endpoint. The wallet posts `vp_token` here |
| `RequestUri` | `string` | Yes | Absolute HTTPS URL where the signed Request Object is served. Wallets fetch this URI to parse the Authorization Request |
| `SignedRequestObject` | `string` | Yes | The compact-serialized signed JWT containing the Authorization Request payload. Served at `RequestUri` |
| `PresentationDefinition` | `PresentationDefinition` | Yes | DIF Presentation Exchange 2.0 document declaring required credential types, issuer constraints, and field constraints |
| `VerifierWalletAddress` | `string` | Yes | Wallet address of the verifying organisation's classical HAIP signing key (same key used to sign the Request Object) |
| `BlueprintActionId` | `string?` | No | Originating Blueprint action identifier. When present, verification results are routed back to this action |
| `BlueprintInstanceId` | `string?` | No | Originating Blueprint instance identifier for SignalR signal routing |
| `Status` | `PresentationRequestStatus` | Yes | Lifecycle state. See state machine below |
| `Result` | `VerificationResult?` | No | Populated only when `Status` is `Verified` or `Denied`. Null while `Pending` or `Submitted` |
| `CreatedAt` | `DateTimeOffset` | Yes | UTC creation timestamp |
| `ExpiresAt` | `DateTimeOffset` | Yes | UTC expiry. Default TTL is 5 minutes from creation, configurable per deployment |

**State machine:**
```
Pending ──> Submitted ──> Verified  (terminal)
   │            │
   │            └──> Denied    (terminal)
   │
   ├──> Expired    (terminal, set by TTL or sweep)
   └──> Cancelled  (terminal, set by Blueprint action cancellation)
```

**Validation rules:**
- `Nonce` must be non-empty and unique across all active requests
- `State` must be non-empty and unique across all active requests
- `ClientId` must be non-empty
- `ResponseUri` must be a valid absolute HTTPS URL (HTTP permitted in development)
- `RequestUri` must be a valid absolute HTTPS URL (HTTP permitted in development)
- `SignedRequestObject` must be a valid compact JWS
- `ExpiresAt` must be in the future at creation time
- Status transitions are one-way per the state machine: `Pending` and `Submitted` are the only mutable states
- A request in a terminal state rejects further `direct_post` submissions

**Relationships:**
- References a verifier wallet in Wallet Service via `VerifierWalletAddress`
- References a Blueprint action via `BlueprintActionId` and `BlueprintInstanceId`
- Consumed by the `direct_post` callback via `State` lookup
- The `Nonce` is bound into the wallet's KB-JWT at presentation time

**Storage notes:**
- Redis JSON serialization via `System.Text.Json`
- TTL set to `ExpiresAt` + a configurable audit retention window (default 1 hour post-expiry) for debugging
- No EF Core entity or migration

---

### 2. `AuthorizationRequest` (computed, not persisted separately)

The HAIP-conformant OID4VP Authorization Request payload, serialized as a signed JWT and served at `request_uri`. This is the content of `PresentationRequest.SignedRequestObject`.

**JWT Header:**
```json
{
  "alg": "ES256",
  "typ": "oauth-authz-req+jwt",
  "x5c": ["base64-leaf-cert", "base64-intermediate", "base64-root"],
  "kid": "verifier-key-id"
}
```

**JWT Payload:**
```json
{
  "client_id": "did:sorcha:org:SyR3...",
  "client_id_scheme": "x509_san_uri",
  "response_type": "vp_token",
  "response_mode": "direct_post",
  "response_uri": "https://deployment.example.com/haip/verifier/callback",
  "nonce": "abc123...",
  "state": "xyz789...",
  "aud": "https://self-issued.me/v2",
  "presentation_definition": { ... },
  "iat": 1712700000,
  "exp": 1712700300
}
```

**Fields:**

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `ClientId` | `string` | Yes | Verifier identifier. For `x509_san_uri`, the SAN URI from the leaf cert |
| `ClientIdScheme` | `string` | Yes | Always `"x509_san_uri"` for HAIP with X.509 trust |
| `ResponseType` | `string` | Yes | Always `"vp_token"` |
| `ResponseMode` | `string` | Yes | Always `"direct_post"` (HAIP 1.0 MTI) |
| `ResponseUri` | `string` | Yes | The `direct_post` callback URL. Must match the verifier's configured callback base |
| `Nonce` | `string` | Yes | Challenge nonce, unique per request. Bound into the wallet's KB-JWT |
| `State` | `string` | Yes | Opaque state for correlating the `direct_post` response to the originating request |
| `Aud` | `string` | Yes | Always `"https://self-issued.me/v2"` per OID4VP spec |
| `PresentationDefinition` | `object` | Yes | DIF Presentation Exchange 2.0 document (see entity 3) |
| `Iat` | `long` | Yes | Issued-at timestamp (Unix epoch seconds) |
| `Exp` | `long` | Yes | Expiry timestamp (Unix epoch seconds). Matches `PresentationRequest.ExpiresAt` |

**Validation rules:**
- `response_mode` must be `"direct_post"`
- `response_type` must be `"vp_token"`
- `aud` must be `"https://self-issued.me/v2"`
- `client_id_scheme` must be `"x509_san_uri"`
- `exp` must be after `iat`
- The JWS signature must verify against the leaf cert's Subject Public Key Info
- The `x5c` chain in the header must validate against the deployment's trust store

**Relationships:**
- Embedded as `SignedRequestObject` in `PresentationRequest`
- Served at `PresentationRequest.RequestUri`
- Consumed by the external wallet to display consent and construct the `vp_token`

---

### 3. `PresentationDefinition` (value object, embedded in Authorization Request)

DIF Presentation Exchange 2.0 document declaring what the verifier requires from the wallet. Built dynamically from Blueprint credential requirements by `PresentationDefinitionBuilder`.

```csharp
public class PresentationDefinition
{
    public string Id { get; init; } = string.Empty;
    public string? Name { get; init; }
    public string? Purpose { get; init; }
    public List<InputDescriptor> InputDescriptors { get; init; } = [];
}

public class InputDescriptor
{
    public string Id { get; init; } = string.Empty;
    public string? Name { get; init; }
    public string? Purpose { get; init; }
    public InputDescriptorFormat Format { get; init; } = new();
    public InputDescriptorConstraints Constraints { get; init; } = new();
}

public class InputDescriptorFormat
{
    [JsonPropertyName("vc+sd-jwt")]
    public FormatAlgorithms? VcSdJwt { get; init; }
}

public class FormatAlgorithms
{
    [JsonPropertyName("sd-jwt_alg_values")]
    public List<string> SdJwtAlgValues { get; init; } = ["ES256"];

    [JsonPropertyName("kb-jwt_alg_values")]
    public List<string> KbJwtAlgValues { get; init; } = ["ES256"];
}

public class InputDescriptorConstraints
{
    public List<FieldConstraint> Fields { get; init; } = [];
}

public class FieldConstraint
{
    public List<string> Path { get; init; } = [];
    public JsonElement? Filter { get; init; }
    public bool Optional { get; init; } = false;
}
```

**JSON wire format:**
```json
{
  "id": "licence-verification-1",
  "name": "Short-Term Let Licence Verification",
  "purpose": "Verify the operator holds a valid licence",
  "input_descriptors": [
    {
      "id": "short-term-let-licence",
      "name": "Short-Term Let Licence",
      "format": {
        "vc+sd-jwt": {
          "sd-jwt_alg_values": ["ES256"],
          "kb-jwt_alg_values": ["ES256"]
        }
      },
      "constraints": {
        "fields": [
          {
            "path": ["$.vct"],
            "filter": { "type": "string", "const": "ShortTermLetLicense" }
          },
          {
            "path": ["$.licenseNumber"],
            "optional": false
          },
          {
            "path": ["$.councilArea"],
            "optional": false
          },
          {
            "path": ["$.expiryDate"],
            "optional": false
          },
          {
            "path": ["$.propertyAddress.postcode"],
            "optional": true
          }
        ]
      }
    }
  ]
}
```

**Validation rules:**
- `Id` must be non-empty
- `InputDescriptors` must contain at least one entry
- Each `InputDescriptor` must have a unique `Id` within the definition
- Each `FieldConstraint.Path` must contain at least one JSON Path expression
- The `Format` must declare `vc+sd-jwt` (HAIP 1.0 credential format)
- Nested claim paths (e.g. `$.propertyAddress.postcode`) are supported per spec 094's nested disclosure

**Relationships:**
- Embedded in `AuthorizationRequest.PresentationDefinition`
- Built by `PresentationDefinitionBuilder` from Blueprint `CredentialRequirement` fields
- Matched against by the verifier during `direct_post` verification (FR-022)

---

### 4. `PresentationSubmission` (wire model, inbound)

Posted by the external wallet to the `direct_post` callback. Contains the `vp_token` (the actual presentation) and the `presentation_submission` descriptor map that tells the verifier which input descriptors are satisfied by which parts of the `vp_token`.

```csharp
public class PresentationSubmissionPayload
{
    [JsonPropertyName("vp_token")]
    public string VpToken { get; init; } = string.Empty;

    [JsonPropertyName("presentation_submission")]
    public PresentationSubmissionDescriptor Submission { get; init; } = new();

    [JsonPropertyName("state")]
    public string State { get; init; } = string.Empty;
}

public class PresentationSubmissionDescriptor
{
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("definition_id")]
    public string DefinitionId { get; init; } = string.Empty;

    [JsonPropertyName("descriptor_map")]
    public List<DescriptorMapEntry> DescriptorMap { get; init; } = [];
}

public class DescriptorMapEntry
{
    public string Id { get; init; } = string.Empty;
    public string Format { get; init; } = string.Empty;
    public string Path { get; init; } = string.Empty;
}
```

**Wire format (form-encoded POST body):**
```
POST /haip/verifier/callback HTTP/1.1
Content-Type: application/x-www-form-urlencoded

vp_token=eyJhbGci...~eyJhbGci...~&presentation_submission=%7B%22id%22%3A...%7D&state=xyz789...
```

**Fields:**

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `VpToken` | `string` | Yes | The SD-JWT VC presentation in compact serialization (`issuer-jwt~disclosure1~...~kb-jwt`). May contain multiple credentials for batch presentation |
| `Submission` | `PresentationSubmissionDescriptor` | Yes | DIF PE 2.0 Presentation Submission mapping input descriptors to locations within `vp_token` |
| `State` | `string` | Yes | Must match an active `PresentationRequest.State`. Used for request correlation |

**Validation rules:**
- `State` must match an active (non-terminal) `PresentationRequest`
- `VpToken` must be non-empty and parseable as an SD-JWT VC presentation
- `Submission.DefinitionId` must match the `PresentationRequest.PresentationDefinition.Id`
- Every `DescriptorMapEntry.Id` must reference an `InputDescriptor.Id` from the presentation definition
- Every `DescriptorMapEntry.Format` must be `"vc+sd-jwt"`
- Content-Type must be `application/x-www-form-urlencoded` per OID4VP direct_post

**Relationships:**
- Correlated to `PresentationRequest` via `State`
- `VpToken` is passed to the core verifier library for full verification
- `Submission.DescriptorMap` guides claim extraction: which credential in the `vp_token` satisfies which input descriptor

---

### 5. `VerificationResult` (value object, stored on PresentationRequest)

The outcome of verifying a `direct_post` submission. Stored as part of the `PresentationRequest` in Redis and propagated to the Blueprint action as its verified input.

```csharp
public class VerificationResult
{
    public bool IsValid { get; init; }
    public Dictionary<string, Dictionary<string, object>> VerifiedClaims { get; init; } = new();
    public List<VerificationError> Errors { get; init; } = [];
    public bool HolderKeyVerified { get; init; }
    public bool X5cChainValid { get; init; }
    public bool? DidTrustPathValid { get; init; }
    public StatusCheckResult? StatusCheckResult { get; init; }
    public string? IssuerIdentity { get; init; }
    public DateTimeOffset VerifiedAt { get; init; }
    public string? VerifierAttestation { get; init; }
}

public class VerificationError
{
    public VerificationErrorKind Kind { get; init; }
    public string Description { get; init; } = string.Empty;
    public string? InputDescriptorId { get; init; }
}

public enum VerificationErrorKind
{
    TrustAnchorMissing = 0,
    X5cChainInvalid = 1,
    IssuerSignatureInvalid = 2,
    KbJwtAudienceMismatch = 3,
    KbJwtNonceMismatch = 4,
    KbJwtClockSkew = 5,
    KbJwtSdHashMismatch = 6,
    KbJwtSignatureInvalid = 7,
    CredentialExpired = 8,
    CredentialRevoked = 9,
    CredentialStatusCheckFailed = 10,
    InputDescriptorUnmatched = 11,
    FieldConstraintFailed = 12,
    PresentationFormatInvalid = 13,
    SubmissionMappingInvalid = 14
}

public class StatusCheckResult
{
    public bool IsActive { get; init; }
    public string ClaimForm { get; init; } = string.Empty;
    public string? StatusListUrl { get; init; }
    public int? StatusListIndex { get; init; }
}
```

**Fields:**

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `IsValid` | `bool` | Yes | `true` if all verification steps passed; `false` if any failed |
| `VerifiedClaims` | `Dictionary<string, Dictionary<string, object>>` | When valid | Verified claim subset indexed by input descriptor ID. Each entry is a dictionary of claim name to claim value. Only populated on `IsValid == true` |
| `Errors` | `List<VerificationError>` | When invalid | Specific errors identifying which verification step failed. Only populated on `IsValid == false`. Claim values are NOT included in errors (FR-026 privacy) |
| `HolderKeyVerified` | `bool` | Yes | Whether the KB-JWT signature verified against the credential's `cnf.jwk` |
| `X5cChainValid` | `bool` | Yes | Whether the `x5c` chain (if present) validated against the trust store |
| `DidTrustPathValid` | `bool?` | No | Whether the DID-based trust path validated (null if `x5c` was used instead) |
| `StatusCheckResult` | `StatusCheckResult?` | No | Credential status check outcome. Null if the credential carries no status claim |
| `IssuerIdentity` | `string?` | When valid | Issuer identity from the X.509 chain subject (or DID document). Populated only on success |
| `VerifiedAt` | `DateTimeOffset` | Yes | UTC timestamp of the verification |
| `VerifierAttestation` | `string?` | When valid | Verifier-signed attestation that verification succeeded (compact JWS). Populated only on success |

**Validation rules:**
- When `IsValid == true`: `VerifiedClaims` must be non-empty, `Errors` must be empty, `IssuerIdentity` must be non-null
- When `IsValid == false`: `Errors` must be non-empty, `VerifiedClaims` must be empty
- `Errors` must NOT contain disclosed claim values (FR-026 privacy requirement)
- `VerifiedAt` must be set to the server's UTC clock at the moment verification completes

**Relationships:**
- Stored on `PresentationRequest.Result`
- Propagated to the Blueprint action as its verified input when `IsValid == true`
- The `Errors` list drives the Blueprint action's failure branch when `IsValid == false`

---

### 6. `PresentationSource` enum (extended on existing `CredentialRequirement`)

New field on the Blueprint Service's `CredentialRequirement` model (the input-side counterpart of spec 097's `TargetAudience` on the output-side `CredentialIssuanceConfig`).

```csharp
public enum PresentationSource
{
    SorchaInternal = 0,
    HaipExternalWallet = 1
}
```

Added to `CredentialRequirement`:

```csharp
public class CredentialRequirement
{
    // ... existing fields (CredentialType, AcceptedIssuers, RequiredClaims, etc.) ...

    /// <summary>
    /// Controls whether the credential presentation is expected from a Sorcha-internal
    /// participant wallet (SorchaInternal, default) or from an external HAIP-conformant
    /// wallet (HaipExternalWallet) via the OpenID4VP direct_post flow.
    /// </summary>
    [JsonPropertyName("presentationSource")]
    public PresentationSource PresentationSource { get; set; } = PresentationSource.SorchaInternal;
}
```

**Values:**

| Value | Behaviour |
|-------|-----------|
| `SorchaInternal` (0, default) | Existing internal credential matching path runs unchanged. Presentation is resolved from the participant's Sorcha wallet. No HAIP interaction |
| `HaipExternalWallet` (1) | Blueprint action calls HAIP verifier to create a `PresentationRequest`. Returns a `PresentationRequestUri` in the action execution result for the UI to render as a QR or deep link. Action suspends in `AwaitingExternalPresentation` state until a verification result arrives |

**Validation rules:**
- When `HaipExternalWallet`, `AcceptedIssuers` may include both Sorcha org DIDs and external issuer identifiers
- When `HaipExternalWallet`, the Blueprint action must NOT attempt internal credential matching (FR-033)
- When absent or `SorchaInternal`, all existing behaviour is preserved
- The same `RequiredClaims` specification is honoured identically for both paths — the claims are expressed as names or JSON Pointer paths per spec 094

**Relationships:**
- Part of the existing `CredentialRequirement` model in `Sorcha.Blueprint.Models/Credentials/`
- Drives the routing decision in the Blueprint action execution engine
- `HaipExternalWallet` produces a `PresentationRequestUri` on the action execution result

---

## Entity relationship summary

```
CredentialRequirement (Blueprint)
    |
    | PresentationSource = HaipExternalWallet
    |
    v
PresentationRequest (Redis)
    |
    | Serves SignedRequestObject at RequestUri
    |
    v
AuthorizationRequest (signed JWT, served to wallet)
    |-- client_id from VerifierWalletAddress
    |-- nonce from PresentationRequest.Nonce
    |-- presentation_definition from PresentationDefinitionBuilder
    |-- x5c chain from spec 096 ITrustStore
    |
    v
Wallet scans QR / follows deep link, fetches RequestUri
    |
    v
PresentationSubmission (direct_post from wallet)
    |-- vp_token (SD-JWT VC presentation)
    |-- presentation_submission (descriptor map)
    |-- state (correlates to PresentationRequest)
    |
    v
VerificationResult (stored on PresentationRequest)
    |-- x5c chain walk (spec 096)
    |-- issuer signature verify
    |-- KB-JWT verify: aud, nonce, iat, sd_hash (spec 094)
    |-- status check: W3C or IETF (spec 095)
    |-- claim matching against presentation_definition (DIF PE 2.0)
    |
    v
Blueprint action resumes with VerifiedClaims
```

## Redis key layout

| Pattern | TTL | Purpose |
|---------|-----|---------|
| `haip:vp-request:{requestId}` | `ExpiresAt` + retention window | Presentation Request lifecycle + verification result |
| `haip:vp-state:{state}` | Same as parent request | Reverse lookup: state -> request ID. Enables O(1) state correlation at the `direct_post` callback |
| `haip:vp-nonce:{nonce}` | Same as parent request | Reverse lookup: nonce -> request ID. Enables nonce uniqueness checks and duplicate detection |
| `haip:vp-reqobj:{requestId}` | Same as parent request | Signed Request Object JWT served at `request_uri`. Separate key for efficient GET serving without deserializing the full request |

All keys use JSON serialization via `System.Text.Json`. TTL-based expiry handles garbage collection for the common path; the audit retention window ensures expired-but-recent requests remain queryable for debugging. The `haip:vp-` prefix distinguishes verifier keys from the `haip:offer:`, `haip:code:`, `haip:token:`, and `haip:nonce:` keys used by the spec 097 issuer side.

## Migration notes

None required. The HAIP service is stateless except for Redis. No EF Core entities, no PostgreSQL tables, no MongoDB collections. The only schema change is the additive `PresentationSource` field on the existing `CredentialRequirement` model, which is a JSON-serialized Blueprint model with no database migration impact.
