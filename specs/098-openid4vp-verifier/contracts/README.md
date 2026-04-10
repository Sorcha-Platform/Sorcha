# API Contracts: OpenID4VP Verifier Endpoint (HAIP)

**Feature**: 098-openid4vp-verifier

## Internal Endpoints (Service-to-Service)

These endpoints are called by the Blueprint Service to create and query Presentation
Requests. They require a valid Sorcha service JWT with the appropriate scope.

---

### `POST /api/v1/verifier/requests`

Create a Presentation Request. Returns an Authorization Request URI that the caller
(typically Blueprint Service) hands to the Sorcha UI for QR rendering or same-device
deep-link invocation.

**Auth**: Service JWT (Blueprint Service principal, scope `haip:verifier`)
**Content-Type**: `application/json`

**Request body** (`CreatePresentationRequestRequest`):
```json
{
  "credentialType": "urn:sorcha:credential:short-term-let-licence",
  "acceptedIssuers": [
    "did:sorcha:org:sorcha1abc123..."
  ],
  "requiredClaims": [
    "licenceNumber",
    "/propertyAddress/streetAddress",
    "validUntil"
  ],
  "verifierOrgId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "verifierWalletAddress": "sorcha1xyz789...",
  "blueprintActionId": "7c9e6679-7425-40de-944b-e07fc1f90ae7",
  "ttlSeconds": 300
}
```

| Field | Required | Description |
|-------|----------|-------------|
| `credentialType` | Yes | VCT value for the required credential |
| `acceptedIssuers` | No | DIDs or X.509 SAN URIs of accepted issuers. Empty = any trusted issuer. |
| `requiredClaims` | Yes | Claim names or JSON Pointer paths (spec 094) that the holder must disclose |
| `verifierOrgId` | Yes | Organisation ID of the verifying org |
| `verifierWalletAddress` | Yes | Wallet address holding the HAIP verifier signing key and `x5c` chain |
| `blueprintActionId` | Yes | Originating Blueprint action ID for routing verification results back |
| `ttlSeconds` | No | Presentation Request TTL (default: 300, max: 600) |

**201 Response** (`PresentationRequestResult`):
```json
{
  "requestId": "a2c4e6f8-1234-5678-9abc-def012345678",
  "authorizationRequestUri": "openid4vp://authorize?client_id=did%3Asorcha%3Aorg%3Asorcha1xyz789...&request_uri=https%3A%2F%2Fsorcha.example.com%2Fapi%2Fv1%2Fverifier%2Frequests%2Fa2c4e6f8%2Frequest-object",
  "requestUri": "https://sorcha.example.com/api/v1/verifier/requests/a2c4e6f8-1234-5678-9abc-def012345678/request-object",
  "nonce": "n-0S6_WzA2Mj",
  "state": "s-7K3bR9xFpQ",
  "expiresAt": "2026-04-10T12:05:00Z",
  "status": "Pending"
}
```

| Field | Description |
|-------|-------------|
| `requestId` | Unique identifier for this Presentation Request |
| `authorizationRequestUri` | Deep-link URI for QR rendering or same-device invocation |
| `requestUri` | HTTPS URL where the wallet fetches the signed Request Object |
| `nonce` | Unique nonce bound into the Request Object (for KB-JWT verification) |
| `state` | Opaque state value linking `direct_post` callback to this request |
| `expiresAt` | UTC expiry timestamp |
| `status` | Initial status: always `Pending` |

**Status codes**:
| Code | Condition |
|------|-----------|
| 201 | Presentation Request created |
| 400 | Invalid request: missing fields, unknown credential type, required claim path cannot be encoded as a PE 2.0 field constraint, or verifier org not enrolled under a trust anchor |
| 401 | Missing or invalid service JWT |
| 403 | Caller lacks `haip:verifier` scope |

---

### `GET /api/v1/verifier/requests/{requestId}/result`

Poll for the verification result of a Presentation Request. Called by the Blueprint
Service to retrieve the outcome after receiving a SignalR signal or on periodic fallback
polling.

**Auth**: Service JWT (Blueprint Service principal, scope `haip:verifier`)

**Path parameters**:
| Parameter | Description |
|-----------|-------------|
| `requestId` | UUID of the Presentation Request |

**200 Response** (`VerificationResult`):
```json
{
  "requestId": "a2c4e6f8-1234-5678-9abc-def012345678",
  "credentialType": "urn:sorcha:credential:short-term-let-licence",
  "blueprintActionId": "7c9e6679-7425-40de-944b-e07fc1f90ae7",
  "status": "Verified",
  "verifiedAt": "2026-04-10T12:01:30Z",
  "createdAt": "2026-04-10T12:00:00Z",
  "expiresAt": "2026-04-10T12:05:00Z",
  "issuerIdentity": {
    "did": "did:sorcha:org:sorcha1abc123...",
    "x509Subject": "CN=Test Council, O=Test Council Ltd",
    "trustPath": "X509"
  },
  "verifiedClaims": {
    "licenceNumber": "STL-2026-001",
    "/propertyAddress/streetAddress": "42 Example Street",
    "validUntil": "2027-04-10"
  },
  "inputDescriptorResults": [
    {
      "descriptorId": "short_term_let_licence",
      "matched": true,
      "matchedClaims": ["licenceNumber", "/propertyAddress/streetAddress", "validUntil"]
    }
  ],
  "verifierAttestation": "eyJhbGciOiJFZERTQSIsInR5cCI6InZlcmlmaWVyLWF0dGVzdGF0aW9uK2p3dCJ9...",
  "failureCause": null
}
```

When `status` is `Denied`, the response omits `verifiedClaims` (FR-026, holder privacy)
and populates `failureCause`:

```json
{
  "requestId": "a2c4e6f8-1234-5678-9abc-def012345678",
  "credentialType": "urn:sorcha:credential:short-term-let-licence",
  "blueprintActionId": "7c9e6679-7425-40de-944b-e07fc1f90ae7",
  "status": "Denied",
  "verifiedAt": null,
  "createdAt": "2026-04-10T12:00:00Z",
  "expiresAt": "2026-04-10T12:05:00Z",
  "issuerIdentity": null,
  "verifiedClaims": null,
  "inputDescriptorResults": null,
  "verifierAttestation": null,
  "failureCause": {
    "code": "kb_jwt_nonce_mismatch",
    "message": "KB-JWT nonce does not match the Presentation Request nonce."
  }
}
```

**Failure cause codes**:
| Code | Meaning |
|------|---------|
| `trust_anchor_unknown` | `x5c` chain does not terminate in a trusted root |
| `issuer_signature_invalid` | Issuer signature does not verify against leaf cert or DID key |
| `kb_jwt_audience_mismatch` | KB-JWT `aud` does not match verifier `client_id` |
| `kb_jwt_nonce_mismatch` | KB-JWT `nonce` does not match Presentation Request nonce |
| `kb_jwt_clock_skew` | KB-JWT `iat` is outside the +/-60s tolerance window |
| `kb_jwt_sd_hash_mismatch` | KB-JWT `sd_hash` does not match the presentation hash |
| `kb_jwt_signature_invalid` | KB-JWT signature does not verify against `cnf.jwk` |
| `credential_revoked` | Credential status is revoked (W3C or IETF status list) |
| `credential_expired` | Credential `exp` has passed |
| `input_descriptor_unmatched` | A required input descriptor has no matching disclosure |
| `field_constraint_failed` | A disclosed claim does not satisfy a PE 2.0 field constraint |
| `presentation_request_expired` | The Presentation Request TTL elapsed before submission |
| `presentation_request_cancelled` | The Presentation Request was cancelled by the originating action |
| `request_already_fulfilled` | A submission has already been accepted for this request |

**Status values**: `Pending`, `Submitted`, `Verified`, `Denied`, `Expired`, `Cancelled`.

**Status codes**:
| Code | Condition |
|------|-----------|
| 200 | Request found (any status) |
| 401 | Missing or invalid service JWT |
| 403 | Caller lacks `haip:verifier` scope |
| 404 | Request not found |

---

## Public HAIP Endpoints (Wallet-Facing)

These endpoints are anonymous (no Sorcha JWT required). They implement the OID4VP
verifier side per HAIP 1.0. Rate limiting via `HaipVerifier` policy is the primary
abuse control.

---

### `GET /api/v1/verifier/requests/{requestId}/request-object`

Fetch the signed Request Object. A HAIP-conformant wallet fetches this via the
`request_uri` returned in the Authorization Request URI.

**Auth**: Anonymous (public, rate limited via `HaipVerifier` policy)
**Content-Type**: `application/oauth-authz-req+jwt`

**Path parameters**:
| Parameter | Description |
|-----------|-------------|
| `requestId` | UUID of the Presentation Request |

**200 Response**:

The response body is a signed JWT (compact serialisation). Content-Type is
`application/oauth-authz-req+jwt` per RFC 9101.

Decoded JWT structure for reference:

**Header**:
```json
{
  "alg": "ES256",
  "typ": "oauth-authz-req+jwt",
  "x5c": ["MIIBkj...", "MIICDj..."],
  "kid": "verifier-key-2026-04"
}
```

**Payload**:
```json
{
  "client_id": "did:sorcha:org:sorcha1xyz789...",
  "client_id_scheme": "x509_san_uri",
  "response_type": "vp_token",
  "response_mode": "direct_post",
  "response_uri": "https://sorcha.example.com/api/v1/verifier/requests/a2c4e6f8-1234-5678-9abc-def012345678/direct-post",
  "nonce": "n-0S6_WzA2Mj",
  "state": "s-7K3bR9xFpQ",
  "aud": "https://self-issued.me/v2",
  "presentation_definition": {
    "id": "a2c4e6f8-pd",
    "input_descriptors": [
      {
        "id": "short_term_let_licence",
        "format": {
          "vc+sd-jwt": {
            "sd-jwt_alg_values": ["ES256", "EdDSA"]
          }
        },
        "constraints": {
          "fields": [
            {
              "path": ["$.vct"],
              "filter": {
                "type": "string",
                "const": "urn:sorcha:credential:short-term-let-licence"
              }
            },
            {
              "path": ["$.licenceNumber"],
              "intent_to_retain": false
            },
            {
              "path": ["$.propertyAddress.streetAddress"],
              "intent_to_retain": false
            },
            {
              "path": ["$.validUntil"],
              "intent_to_retain": false
            }
          ]
        }
      }
    ]
  }
}
```

**Notes**:
- `x5c` in the header contains the verifier's certificate chain (leaf to root), same
  chain as used for credential issuance (spec 096). Wallets verify the Request Object
  signature against the leaf cert's public key.
- `client_id` is the DID URI from the leaf cert's SAN, matching `client_id_scheme: x509_san_uri`.
- `response_uri` is the absolute URL of the `direct_post` endpoint for this request.
- `presentation_definition` conforms to DIF Presentation Exchange 2.0.
- JSON Pointer-style nested claims (e.g., `/propertyAddress/streetAddress`) are mapped
  to JSON Path (`$.propertyAddress.streetAddress`) in the PE 2.0 field constraints.

**Status codes**:
| Code | Condition |
|------|-----------|
| 200 | Request Object returned as signed JWT |
| 404 | Request not found |
| 410 | Request expired (TTL elapsed) |
| 429 | Rate limit exceeded |

---

### `POST /api/v1/verifier/requests/{requestId}/direct-post`

Wallet submits `vp_token` and `presentation_submission` after user consent. This is the
HAIP `direct_post` callback endpoint.

**Auth**: Anonymous (public, rate limited via `HaipVerifier` policy)
**Content-Type**: `application/x-www-form-urlencoded`

**Path parameters**:
| Parameter | Description |
|-----------|-------------|
| `requestId` | UUID of the Presentation Request |

**Request body** (form-encoded):
| Parameter | Required | Description |
|-----------|----------|-------------|
| `vp_token` | Yes | The Verifiable Presentation token. For SD-JWT VC: the serialised SD-JWT with KB-JWT appended (`issuer-jwt~disclosure1~...~disclosureN~kb-jwt`) |
| `presentation_submission` | Yes | JSON string conforming to DIF PE 2.0 Presentation Submission, mapping input descriptors to credentials in `vp_token` |
| `state` | Yes | The opaque `state` value from the Request Object. Used to correlate this submission to the Presentation Request. |

**`presentation_submission` structure** (decoded for reference):
```json
{
  "id": "submission-1",
  "definition_id": "a2c4e6f8-pd",
  "descriptor_map": [
    {
      "id": "short_term_let_licence",
      "format": "vc+sd-jwt",
      "path": "$"
    }
  ]
}
```

**Verification pipeline** (executed in order per FR-017 through FR-025):
1. Match `state` to an active Presentation Request (FR-014)
2. Parse `presentation_submission` and extract SD-JWT VC from `vp_token` (FR-015)
3. Validate `x5c` chain against trust store, or fall back to DID trust path (FR-017, FR-018)
4. Verify issuer signature against leaf cert SPKI or DID verification method (FR-019)
5. Verify KB-JWT: `aud` == verifier `client_id`, `nonce` == request nonce, `iat` within +/-60s, `sd_hash` matches (FR-020)
6. Check credential status via W3C or IETF status list claim (FR-021)
7. Check credential `exp` (not expired)
8. Match disclosed claims against all `presentation_definition` input descriptors (FR-022, FR-023)
9. Record `Verified` or `Denied` result (FR-024, FR-025)
10. Emit SignalR signal on ActionsHub for the originating Blueprint action (FR-030)

**200 Response** (verification succeeded):
```json
{
  "redirect_uri": null
}
```

Per HAIP 1.0, the `direct_post` response to the wallet is minimal. The wallet does not
receive the verified claims -- those flow to the Blueprint Service via the internal
result endpoint. A `redirect_uri` may optionally be returned if the verifier wants the
wallet to redirect the user (null when not applicable).

**Error responses** (standard OAuth 2.0 error shape per OID4VP):
```json
{
  "error": "invalid_request",
  "error_description": "The state parameter does not match any active Presentation Request."
}
```

**Error codes**:
| Error | Condition |
|-------|-----------|
| `invalid_request` | `state` does not match any active request, or request is in terminal state |
| `invalid_request` | `presentation_submission` is malformed or does not match `presentation_definition` |
| `access_denied` | Credential verification failed (trust anchor, signature, KB-JWT, status, or claim match). The specific internal failure cause is recorded on the Presentation Request but NOT returned to the wallet (FR-026). |

**Status codes**:
| Code | Condition |
|------|-----------|
| 200 | Submission accepted, verification succeeded |
| 400 | `invalid_request` -- state mismatch, malformed submission, request already fulfilled, or request expired |
| 403 | `access_denied` -- credential verification failed (any step in the pipeline) |
| 429 | Rate limit exceeded |
