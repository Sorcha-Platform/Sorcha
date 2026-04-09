# Phase 1 Data Model: SD-JWT VC HAIP Hardening

**Feature**: 094-sdjwt-haip-hardening
**Date**: 2026-04-09

## Entities and value objects

### 1. `cnf` claim (new, inside signed SD-JWT payload)

**Shape**: W3C / IETF SD-JWT VC confirmation claim carrying the holder's public key in JWK form.

```json
{
  "cnf": {
    "jwk": {
      "kty": "OKP",
      "crv": "Ed25519",
      "x": "base64url-encoded-ed25519-public-key"
    }
  }
}
```

For NIST P-256 holders:
```json
{
  "cnf": {
    "jwk": {
      "kty": "EC",
      "crv": "P-256",
      "x": "base64url-x-coord",
      "y": "base64url-y-coord"
    }
  }
}
```

**Semantics:**
- Non-disclosable. Always visible when the token is read.
- Required for every credential issued after this spec ships (FR-001).
- Absence indicates a pre-fix legacy credential; the verifier treats such credentials as bearer tokens per FR-006.

### 2. Key Binding JWT (new, appended to serialised presentations)

**Shape**: a small JWT with its own header and payload, appended after the last disclosure's `~` in the serialised presentation.

**Header**:
```json
{
  "typ": "kb+jwt",
  "alg": "EdDSA"
}
```

**Payload**:
```json
{
  "aud": "https://verifier.example/presentation-callback",
  "nonce": "abc123...",
  "iat": 1712700000,
  "sd_hash": "base64url-sha256-of-preceding-presentation-bytes"
}
```

**Wire format** (example):
```
eyJhbGciOiJFZERTQSJ9.<issuer-jwt-payload>.<issuer-signature>~WyJh...~WyJi...~eyJ0eXAiOiJrYitqd3QiLCJhbGciOiJFZERTQSJ9.<kb-jwt-payload>.<kb-jwt-signature>
```

**Semantics:**
- Signed by the holder's binding key (whose public key is in the credential's `cnf.jwk`).
- `sd_hash` is `base64url(sha256(presentation_string_without_kb_jwt))` where `presentation_string_without_kb_jwt` is the full serialised presentation up to and including the final `~` before the KB-JWT.
- Required when the credential carries `cnf`; optional (and ignored) when the credential does not.
- `iat` must be within ±60 seconds of verifier clock.

### 3. Nested `_sd` digest structures (new, inside signed SD-JWT payload)

**For nested object fields** (`/address/locality` disclosable, `/address/country` always visible):
```json
{
  "address": {
    "country": "GB",
    "_sd": ["<sha256-of-locality-disclosure>"]
  }
}
```

**For array-element disclosure** (`/qualifications/0`, `/qualifications/1`, `/qualifications/2`):
```json
{
  "qualifications": [
    {"...": "<sha256-of-element-0-disclosure>"},
    {"...": "<sha256-of-element-1-disclosure>"},
    {"...": "<sha256-of-element-2-disclosure>"}
  ]
}
```

**Disclosure wire format:**
- Object field: `base64url(json([salt, name, value]))` — three elements.
- Array element: `base64url(json([salt, value]))` — two elements. The parent `{"...": digest}` placeholder carries the digest.

**Semantics:**
- Nested `_sd` arrays can appear at any depth.
- Top-level name-keyed disclosures (spec 031 / pre-spec-094) continue to work unchanged — paths without leading `/` are treated as top-level names.

### 4. `HolderBindingKey` (new value object in `Sorcha.Wallet.Portable/Domain/ValueObjects/`)

Represents the BIP32-derived key under purpose `sorcha:credential-holder-binding`. One per wallet. Deterministic from the wallet's HD seed.

**Public interface:**
- `string WalletAddress` — the owning wallet's address
- `byte[] PublicKey` — raw public key bytes
- `string Algorithm` — always Ed25519 for this purpose (classical, small, fast)
- `JsonWebKey ToJwk()` — serialises to the JWK shape for `cnf.jwk` embedding

**Semantics:**
- Never persisted — derived on demand from the wallet seed via `IKeyManagementService`.
- Recoverable from the wallet's mnemonic like any other HD sub-key.
- Rotation of the holder binding key is out of scope for this spec (FR-027).

### 5. `HaipIssuerCoKey` (new value object in `Sorcha.Wallet.Portable/Domain/ValueObjects/`)

Represents the BIP32-derived classical signing key under purpose `sorcha:haip-issuer-signing`. Present only on wallets carrying the `HaipIssuer` capability flag AND whose primary algorithm is PQC.

**Public interface:**
- `string WalletAddress`
- `byte[] PublicKey`
- `string Algorithm` — ES256 default
- `JsonWebKey ToJwk()`
- `DateTimeOffset DerivedAt` — informational

**Semantics:**
- When the wallet's primary algorithm is already classical, this value object is not materialised — the primary key is used directly for HAIP issuance.
- Rotation is out of scope for this spec.

### 6. `HaipIssuer` wallet capability flag (new field on `Wallet` entity)

Added as an optional boolean on `Wallet`. Default `false`. When `true`, the wallet is eligible for HAIP-path credential issuance and will have `HaipIssuerCoKey` derived on first use.

**Migration**: no EF migration created — per user guidance, new EF migrations in pre-release are squashed into the initial setup migration. The `HaipIssuer` column is simply added to the `Wallet` entity and the schema-sync happens at next migration consolidation.

### 7. `IssueCredentialRequest.HolderJwk` (new field on existing DTO)

Optional. When present, the issuer embeds `{"cnf": {"jwk": holderJwk}}` in the signed payload. When absent, no `cnf` is added and the credential is issued as a legacy bearer token (for backward compatibility with callers that have not yet been updated).

### 8. `CredentialIssuanceConfig.Disclosable` (extended)

The existing list of strings is interpreted as follows:
- Bare names (e.g. `"licenseType"`) — top-level name-keyed disclosure, unchanged behaviour
- JSON Pointer paths (e.g. `"/address/locality"`, `"/qualifications/0"`) — nested disclosure, new behaviour

This is an additive extension — no breaking change to the string list type.

## Validation rules

- `cnf.jwk` MUST be a valid JWK for Ed25519, P-256, or RSA.
- KB-JWT `iat` MUST be within ±60 seconds of server clock.
- KB-JWT `sd_hash` MUST match `base64url(sha256(presentation_bytes_without_kb_jwt))`.
- `NestedDisclosure.Translate` MUST reject paths that do not resolve in the claims tree (FR-020).
- `HaipIssuer` capability MUST be set before the Wallet Service will use a PQC-primary wallet's co-key for signing HAIP credentials.

## Migration notes

None required. Per user guidance, pre-release EF migrations are squashed into the initial setup migration. The `HaipIssuer` column on `Wallet` is added at the schema level and included in the consolidated migration.
