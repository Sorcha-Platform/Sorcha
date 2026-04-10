# API Contracts: OpenID4VCI Issuer Endpoint (HAIP)

**Feature**: 097-openid4vci-issuer

## Public HAIP Endpoints (Sorcha.Haip.Service)

These endpoints are anonymous (no Sorcha JWT required). They implement the OpenID4VCI
pre-authorized code flow per HAIP 1.0. Rate limiting is the primary abuse control.

---

### `GET /.well-known/openid-credential-issuer`

**Auth**: Anonymous (public, cacheable)
**Cache-Control**: `public, max-age=3600` (configurable)
**Content-Type**: `application/json`

**200 Response** (`IssuerMetadata`):
```json
{
  "credential_issuer": "https://sorcha.example.com",
  "credential_endpoint": "https://sorcha.example.com/credential",
  "token_endpoint": "https://sorcha.example.com/token",
  "nonce_endpoint": "https://sorcha.example.com/nonce",
  "display": [
    {
      "name": "Sorcha Platform",
      "locale": "en"
    }
  ],
  "credentials_supported": [
    {
      "format": "vc+sd-jwt",
      "vct": "urn:sorcha:credential:short-term-let-licence",
      "cryptographic_binding_methods_supported": ["jwk"],
      "credential_signing_alg_values_supported": ["ES256"],
      "display": [
        {
          "name": "Short-Term Let Licence",
          "locale": "en"
        }
      ]
    }
  ]
}
```

**Status codes**:
| Code | Condition |
|------|-----------|
| 200 | Metadata returned (may have empty `credentials_supported` if no issuers enrolled) |
| 404 | HAIP issuance not configured for this deployment (alternative to empty array; deployment chooses one) |

---

### `GET /.well-known/oauth-authorization-server`

**Auth**: Anonymous (public, cacheable)
**Cache-Control**: `public, max-age=3600` (configurable)
**Content-Type**: `application/json`

**200 Response** (`OAuthServerMetadata`):
```json
{
  "issuer": "https://sorcha.example.com",
  "token_endpoint": "https://sorcha.example.com/token",
  "grant_types_supported": [
    "urn:ietf:params:oauth:grant-type:pre-authorized_code"
  ],
  "token_endpoint_auth_methods_supported": ["none"],
  "response_types_supported": []
}
```

**Status codes**:
| Code | Condition |
|------|-----------|
| 200 | Metadata returned |

---

### `POST /token`

**Auth**: Anonymous (rate limited via `HaipToken` policy)
**Content-Type**: `application/x-www-form-urlencoded`

**Request body** (form-encoded):
| Parameter | Required | Description |
|-----------|----------|-------------|
| `grant_type` | Yes | Must be `urn:ietf:params:oauth:grant-type:pre-authorized_code` |
| `pre-authorized_code` | Yes | One-time code from the Credential Offer |
| `tx_code` | No | User-presented transaction code (future extension, not MTI) |

**200 Response** (`TokenResponse`):
```json
{
  "access_token": "eyJhbGciOi...",
  "token_type": "Bearer",
  "expires_in": 300,
  "c_nonce": "tZignsnFbp",
  "c_nonce_expires_in": 300
}
```

**Error responses** (standard OAuth 2.0 error shape):
```json
{
  "error": "invalid_grant",
  "error_description": "The pre-authorized code has already been used."
}
```

**Status codes**:
| Code | Condition |
|------|-----------|
| 200 | Token issued successfully |
| 400 | `invalid_grant` -- code expired, already consumed, or unknown |
| 400 | `invalid_request` -- missing required parameter (e.g. `tx_code` when required by offer) |
| 400 | `unsupported_grant_type` -- grant type is not `pre-authorized_code` |
| 429 | Rate limit exceeded |

---

### `POST /nonce`

**Auth**: Anonymous (rate limited via `HaipToken` policy). Access token in `Authorization: Bearer` header.
**Content-Type**: n/a (empty body)

**Request headers**:
| Header | Required | Description |
|--------|----------|-------------|
| `Authorization` | Yes | `Bearer {access_token}` |

**200 Response**:
```json
{
  "c_nonce": "a1b2c3d4e5",
  "c_nonce_expires_in": 300
}
```

Calling this endpoint invalidates any previously issued `c_nonce` for the same access token.

**Status codes**:
| Code | Condition |
|------|-----------|
| 200 | Fresh nonce returned |
| 401 | `invalid_token` -- access token missing, malformed, or expired |
| 429 | Rate limit exceeded |

---

### `POST /credential`

**Auth**: `Authorization: Bearer {access_token}` (rate limited via `HaipCredential` policy)
**Content-Type**: `application/json`

**Request body** (`CredentialRequest`):
```json
{
  "format": "vc+sd-jwt",
  "vct": "urn:sorcha:credential:short-term-let-licence",
  "proof": {
    "proof_type": "jwt",
    "jwt": "eyJhbGciOiJFUzI1NiIsInR5cCI6Im9wZW5pZDRyY2ktcHJvb2Yrand0IiwiandrIjp7Imt0eSI6IkVDIiwiY3J2IjoiUC0yNTYiLCJ4IjoiLi4uIiwieSI6Ii4uLiJ9fQ.eyJhdWQiOiJodHRwczovL3NvcmNoYS5leGFtcGxlLmNvbSIsImlhdCI6MTcxMjAwMDAwMCwibm9uY2UiOiJ0WmlnbnNuRmJwIn0.signature"
  }
}
```

**JWT proof structure** (decoded for reference):

Header:
```json
{
  "alg": "ES256",
  "typ": "openid4vci-proof+jwt",
  "jwk": {
    "kty": "EC",
    "crv": "P-256",
    "x": "...",
    "y": "..."
  }
}
```

Payload:
```json
{
  "aud": "https://sorcha.example.com",
  "iat": 1712000000,
  "nonce": "tZignsnFbp"
}
```

**Proof verification rules**:
1. `jwk` in header declares the holder's public key
2. Signature verifies against that key
3. `nonce` matches a freshly issued `c_nonce` for this access token
4. `aud` matches the `credential_issuer` URL from metadata
5. `iat` is within +/-60 seconds of server time
6. Supported key types: Ed25519, P-256, RSA

**200 Response** (`CredentialResponse`):
```json
{
  "credential": "eyJhbGciOiJFUzI1NiIsInR5cCI6InZjK3NkLWp3dCIsIng1YyI6WyIuLi4iLCIuLi4iXX0.eyJpc3MiOiJkaWQ6c29yY2hhOm9yZzphYmMxMjMiLCJ2Y3QiOiJ1cm46c29yY2hhOmNyZWRlbnRpYWw6c2hvcnQtdGVybS1sZXQtbGljZW5jZSIsImNuZiI6eyJqd2siOnsia3R5IjoiRUMiLCJjcnYiOiJQLTI1NiIsIngiOiIuLi4iLCJ5IjoiLi4uIn19LCJzdGF0dXMiOnsic3RhdHVzX2xpc3QiOnsidXJpIjoiaHR0cHM6Ly9zb3JjaGEuZXhhbXBsZS5jb20vc3RhdHVzLzEiLCJpZHgiOjQyfX0sIi4uLiI6Ii4uLiJ9~disclosures...~",
  "c_nonce": "f6g7h8i9j0",
  "c_nonce_expires_in": 300
}
```

The `credential` field contains a serialised SD-JWT VC. When decoded:
- **JWS header**: `alg`, `typ: vc+sd-jwt`, `x5c` array (leaf = org cert, root = tenant CA)
- **Payload**: `iss`, `vct`, `cnf.jwk` (holder key from proof), `status.status_list` (uri + idx), mapped claims from Blueprint action
- **Disclosures**: selectively disclosable claims per spec 094

**Error responses**:
```json
{
  "error": "invalid_proof",
  "error_description": "c_nonce does not match any recently issued nonce."
}
```

**Status codes**:
| Code | Condition |
|------|-----------|
| 200 | Credential issued successfully |
| 400 | `invalid_proof` -- signature invalid, nonce mismatch, audience mismatch, clock skew, unsupported key type |
| 400 | `invalid_request` -- credential already issued for this code (no reissuance), or Blueprint action cancelled |
| 400 | `unsupported_credential_format` -- requested `vct` not in `credentials_supported` |
| 401 | `invalid_token` -- access token missing, malformed, or expired |
| 429 | Rate limit exceeded |

---

## Internal Endpoints (Service-to-Service)

These endpoints are called by the Blueprint Service to create and query Credential Offers.
They require a valid Sorcha service JWT with the appropriate scope.

---

### `POST /api/v1/offers`

**Auth**: Service JWT (Blueprint Service principal)
**Content-Type**: `application/json`

**Request body** (`CreateCredentialOfferRequest`):
```json
{
  "credentialType": "urn:sorcha:credential:short-term-let-licence",
  "issuerOrgId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "issuerWalletAddress": "sorcha1abc123...",
  "blueprintActionId": "7c9e6679-7425-40de-944b-e07fc1f90ae7",
  "mappedClaims": {
    "licenceNumber": "STL-2026-001",
    "propertyAddress": "42 Example Street",
    "validFrom": "2026-04-10",
    "validUntil": "2027-04-10"
  },
  "disclosableFields": [
    "/propertyAddress",
    "/licenceNumber"
  ],
  "codeTtlSeconds": 300,
  "txCodeRequired": false
}
```

| Field | Required | Description |
|-------|----------|-------------|
| `credentialType` | Yes | VCT value for the credential |
| `issuerOrgId` | Yes | Organisation ID of the issuing org |
| `issuerWalletAddress` | Yes | Wallet address holding the HAIP issuer co-key |
| `blueprintActionId` | Yes | Originating Blueprint action for audit trail |
| `mappedClaims` | Yes | Pre-computed claims to embed in the credential |
| `disclosableFields` | No | JSON Pointers for selectively disclosable fields (spec 094) |
| `codeTtlSeconds` | No | Pre-authorized code TTL (default: 300) |
| `txCodeRequired` | No | Whether the offer requires a user-presented transaction code (default: false) |

**201 Response** (`CredentialOfferResult`):
```json
{
  "offerId": "9b1deb4d-3b7d-4bad-9bdd-2b0d7b3dcb6d",
  "credentialOfferUri": "openid-credential-offer://?credential_offer_uri=https%3A%2F%2Fsorcha.example.com%2Fapi%2Fv1%2Foffers%2F9b1deb4d%2Foffer.json",
  "preAuthorizedCode": "SplxlOBeZQQYbYS6WxSbIA",
  "expiresAt": "2026-04-10T12:05:00Z",
  "status": "Pending"
}
```

The `credentialOfferUri` is ready for QR code rendering by the Sorcha UI.

**Status codes**:
| Code | Condition |
|------|-----------|
| 201 | Offer created |
| 400 | Invalid request (missing fields, unknown credential type, org not enrolled as HAIP issuer) |
| 401 | Missing or invalid service JWT |
| 403 | Caller lacks required scope |

---

### `GET /api/v1/offers/{offerId}`

**Auth**: Service JWT (Blueprint Service principal)

**Path parameters**:
| Parameter | Description |
|-----------|-------------|
| `offerId` | UUID of the Credential Offer |

**200 Response** (`CredentialOfferStatus`):
```json
{
  "offerId": "9b1deb4d-3b7d-4bad-9bdd-2b0d7b3dcb6d",
  "credentialType": "urn:sorcha:credential:short-term-let-licence",
  "issuerOrgId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "blueprintActionId": "7c9e6679-7425-40de-944b-e07fc1f90ae7",
  "status": "Issued",
  "createdAt": "2026-04-10T12:00:00Z",
  "expiresAt": "2026-04-10T12:05:00Z",
  "exchangedAt": "2026-04-10T12:01:30Z",
  "issuedAt": "2026-04-10T12:01:45Z"
}
```

**Offer status values**: `Pending`, `Exchanged` (code used at token endpoint), `Issued` (credential delivered), `Expired`, `Cancelled`.

**Status codes**:
| Code | Condition |
|------|-----------|
| 200 | Offer found |
| 401 | Missing or invalid service JWT |
| 403 | Caller lacks required scope |
| 404 | Offer not found |
