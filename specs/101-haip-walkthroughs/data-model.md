# Phase 1 Data Model: HAIP Walkthroughs

**Feature**: 101-haip-walkthroughs
**Date**: 2026-04-11

## Entities and value objects

### 1. `HolderKeyPair` (new, `Sorcha.Agent/Haip/HolderKeyManager.cs`)

**Shape**: P-256 (secp256r1) EC key pair managed by `HolderKeyManager`. Persisted as two files in the wallet directory.

**Private key file** (`holder_key.pem`):
```
-----BEGIN EC PRIVATE KEY-----
MHQCAQEEIBkg2yGz...base64...
-----END EC PRIVATE KEY-----
```

**Public key file** (`holder_key.jwk.json`):
```json
{
  "kty": "EC",
  "crv": "P-256",
  "x": "base64url-encoded-x-coordinate",
  "y": "base64url-encoded-y-coordinate"
}
```

**Properties:**
- `privateKeyPem` — `string`, PEM-encoded EC private key (PKCS#8), persisted to `holder_key.pem`
- `publicKeyJwk` — `JsonElement`, JWK representation of the public key, persisted to `holder_key.jwk.json`
- `algorithm` — `string`, always `"ES256"` for HAIP 1.0
- `walletDir` — `string`, absolute path to the wallet directory containing both key files

**Lifecycle:**
- Created on first `haip receive` invocation if no PEM file exists (FR-002)
- Loaded from PEM on subsequent invocations — JWK is regenerated from PEM to ensure consistency
- One key pair per agent identity, reused across all credentials
- Never transmitted — only the public key (as JWK) appears in JWT proof headers and `cnf` claims

**Public interface:**
```csharp
public class HolderKeyManager
{
    /// <summary>Loads existing key pair or generates a new P-256 pair.</summary>
    public ECDsa GetOrCreateKey(string walletDir);

    /// <summary>Returns the public key as a JWK JsonElement for embedding in JWT headers and cnf claims.</summary>
    public JsonElement GetPublicKeyJwk(ECDsa key);

    /// <summary>Returns true if a holder key already exists in the wallet directory.</summary>
    public bool KeyExists(string walletDir);
}
```

### 2. `CredentialWallet` (new, `Sorcha.Agent/Haip/CredentialWallet.cs`)

**Shape**: File-based storage of SD-JWT VC tokens. Each credential is stored as a raw SD-JWT compact serialisation file, named by the credential type extracted from the `vct` claim.

**Directory layout:**
```
wallets/citizen/credentials/
├── VerifiedIdentityCredential.sdjwt
└── DrivingLicenceCredential.sdjwt
```

**Properties:**
- `walletDir` — `string`, absolute path to the wallet directory (parent of `credentials/`)
- `credentials` — `Dictionary<string, string>`, credential type → absolute file path (populated lazily from directory scan)

**File contents**: Raw SD-JWT compact serialisation as a single line of text. Example:
```
eyJhbGciOiJFUzI1NiJ9.eyJ2Y3QiOiJWZXJpZmllZElkZW50aXR5Q3JlZGVudGlhbCIsIl9zZCI6Wy4uLl19.sig~WyJzYWx0IiwiZ2l2ZW5OYW1lIiwiQWxpY2UiXQ~WyJzYWx0IiwiZmFtaWx5TmFtZSIsIlNtaXRoIl0~
```

**Credential type extraction**: The `vct` claim is read from the SD-JWT issuer JWT payload (second segment, base64url-decoded JSON). If `vct` is absent, the credential type falls back to `unknown_{sha256_prefix}`.

**Public interface:**
```csharp
public class CredentialWallet
{
    /// <summary>Saves an SD-JWT VC to disk. Extracts vct to determine filename.</summary>
    public async Task<string> SaveAsync(string walletDir, string rawSdJwt);

    /// <summary>Loads a credential by type. Returns null if not found.</summary>
    public async Task<string?> LoadAsync(string walletDir, string credentialType);

    /// <summary>Lists all credential types stored in the wallet.</summary>
    public IReadOnlyList<string> ListTypes(string walletDir);

    /// <summary>Returns true if the wallet contains a credential of the given type.</summary>
    public bool Exists(string walletDir, string credentialType);
}
```

### 3. `WalkthroughState` (`state.json`)

**Shape**: JSON file persisted between setup.ps1 and run.ps1 scripts. Contains all IDs and references needed to run the walkthrough without re-provisioning.

**HaipIdentityAttestation state.json:**
```json
{
  "tenantId": "guid",
  "orgIds": {
    "governmentAuthority": "guid"
  },
  "walletAddresses": {
    "governmentAuthority": "sorcha1abc..."
  },
  "userCredentials": {
    "citizen": {
      "email": "alice.citizen@example.com",
      "password": "$env:CITIZEN_PASSWORD"
    }
  },
  "credentialPaths": {
    "VerifiedIdentityCredential": "wallets/citizen/credentials/VerifiedIdentityCredential.sdjwt"
  },
  "offerUris": {
    "VerifiedIdentityCredential": "openid-credential-offer://?credential_offer_uri=http://localhost/api/haip/offers/guid"
  },
  "holderWalletDir": "wallets/citizen",
  "registerId": "guid"
}
```

**HaipDrivingLicence state.json** (extends identity attestation state):
```json
{
  "tenantId": "guid",
  "orgIds": {
    "governmentAuthority": "guid",
    "councilAuthority": "guid"
  },
  "walletAddresses": {
    "governmentAuthority": "sorcha1abc...",
    "councilAuthority": "sorcha1def..."
  },
  "userCredentials": {
    "citizen": {
      "email": "alice.citizen@example.com",
      "password": "$env:CITIZEN_PASSWORD"
    }
  },
  "credentialPaths": {
    "VerifiedIdentityCredential": "wallets/citizen/credentials/VerifiedIdentityCredential.sdjwt",
    "DrivingLicenceCredential": "wallets/citizen/credentials/DrivingLicenceCredential.sdjwt"
  },
  "offerUris": {
    "VerifiedIdentityCredential": "openid-credential-offer://?credential_offer_uri=...",
    "DrivingLicenceCredential": "openid-credential-offer://?credential_offer_uri=..."
  },
  "holderWalletDir": "wallets/citizen",
  "registerId": "guid",
  "blueprintId": "guid",
  "identityAttestationStateRef": "../HaipIdentityAttestation/state.json"
}
```

**Properties:**
- `tenantId` — `string` (GUID), the platform tenant used for provisioning
- `orgIds` — `Dictionary<string, string>`, role name → organisation GUID
- `walletAddresses` — `Dictionary<string, string>`, role name → Sorcha wallet address (bech32)
- `userCredentials` — `Dictionary<string, object>`, role name → `{email, password}` object. Passwords reference environment variables via `$env:` prefix
- `credentialPaths` — `Dictionary<string, string>`, credential type → relative file path to the stored SD-JWT
- `offerUris` — `Dictionary<string, string>`, credential type → OID4VCI credential offer URI
- `holderWalletDir` — `string`, relative path to the citizen's wallet directory
- `registerId` — `string` (GUID), the register used for the walkthrough (driving licence only)
- `blueprintId` — `string` (GUID), optional, the published blueprint ID (driving licence only)
- `identityAttestationStateRef` — `string`, optional, relative path to the upstream walkthrough's state.json (driving licence only)

### 4. `ActorDefinition` extension (existing `actors/*.json`)

**Shape**: The existing actor definition JSON gains an optional `haip` section for HAIP wallet configuration. Existing fields (`actor`, `connection`, `inbox`, `mode`, `rules`) are unchanged.

**Extended actor definition** (`citizen.json`):
```json
{
  "actor": {
    "name": "citizen",
    "description": "External HAIP wallet holder — receives and presents credentials"
  },
  "connection": {
    "gatewayUrl": "http://localhost"
  },
  "haip": {
    "holderKeyAlgorithm": "ES256",
    "walletDir": "wallets/citizen"
  }
}
```

**New fields in `haip` section:**
- `holderKeyAlgorithm` — `string`, the algorithm for the holder key pair. Always `"ES256"` for HAIP 1.0. Included for forward-compatibility with future algorithms.
- `walletDir` — `string`, relative or absolute path to the wallet directory. Contains `holder_key.pem`, `holder_key.jwk.json`, and `credentials/` subdirectory.

**Semantics:**
- The `haip` section is optional. Existing actor definitions without it continue to work unchanged.
- The `connection` section is simplified for HAIP actors — only `gatewayUrl` is required (no `registerId`, `credentials`, or `walletAddress`). The HAIP commands handle authentication via the OID4VCI/OID4VP protocols, not via Sorcha JWT auth.
- The `inbox`, `mode`, and `rules` sections are not used by `haip receive` and `haip present` commands.

### 5. `JwtProofBuilder` output (transient, `Sorcha.Agent/Haip/JwtProofBuilder.cs`)

**Shape**: OID4VCI JWT proof of possession. Compact JWT string built for a single credential request. Not persisted.

**Header:**
```json
{
  "typ": "openid4vci-proof+jwt",
  "alg": "ES256",
  "jwk": {
    "kty": "EC",
    "crv": "P-256",
    "x": "base64url-x",
    "y": "base64url-y"
  }
}
```

**Payload:**
```json
{
  "iss": "sorcha-agent",
  "aud": "http://localhost/api/haip",
  "iat": 1712700000,
  "nonce": "c_nonce_from_token_response"
}
```

**Wire format**: `base64url(header).base64url(payload).base64url(es256_signature)`

**Properties:**
- `iss` — `string`, fixed identifier for the agent (`"sorcha-agent"`)
- `aud` — `string`, the credential issuer URL (from issuer metadata)
- `iat` — `long`, Unix timestamp (seconds) of proof creation
- `nonce` — `string`, the `c_nonce` value from the token endpoint response

**Public interface:**
```csharp
public class JwtProofBuilder
{
    /// <summary>Builds a JWT proof of possession for OID4VCI credential requests.</summary>
    public string Build(ECDsa holderKey, JsonElement publicKeyJwk, string issuerUrl, string cNonce);
}
```

### 6. `KbJwtBuilder` output (transient, `Sorcha.Agent/Haip/KbJwtBuilder.cs`)

**Shape**: SD-JWT Key Binding JWT for OID4VP presentations. Compact JWT string appended to the SD-JWT presentation. Not persisted.

**Header:**
```json
{
  "typ": "kb+jwt",
  "alg": "ES256"
}
```

**Payload:**
```json
{
  "aud": "http://localhost/api/haip/verify",
  "nonce": "verifier_nonce_from_request_object",
  "iat": 1712700000,
  "sd_hash": "base64url-sha256-of-presentation-prefix"
}
```

**Wire format**: The KB-JWT is appended after the last disclosure `~` in the SD-JWT presentation:
```
eyJ...<issuer-jwt>...~WyJ...disclosure1...~WyJ...disclosure2...~eyJ...<kb-jwt>...
```

**Properties:**
- `aud` — `string`, the verifier's audience URL (from the presentation request object)
- `nonce` — `string`, the verifier's nonce (from the presentation request object)
- `iat` — `long`, Unix timestamp (seconds) of KB-JWT creation
- `sd_hash` — `string`, `base64url(sha256(presentation_bytes_without_kb_jwt))` where the presentation bytes include the issuer JWT, all selected disclosures, and the trailing `~`

**Public interface:**
```csharp
public class KbJwtBuilder
{
    /// <summary>Builds a Key Binding JWT for SD-JWT presentations.</summary>
    /// <param name="holderKey">The holder's P-256 private key.</param>
    /// <param name="presentationPrefix">The serialised presentation up to and including the final ~.</param>
    /// <param name="audience">The verifier's audience URL.</param>
    /// <param name="nonce">The verifier's nonce.</param>
    public string Build(ECDsa holderKey, string presentationPrefix, string audience, string nonce);
}
```

## Validation rules

- `HolderKeyManager.GetOrCreateKey` MUST generate a P-256 key pair. Other curves are not supported for HAIP 1.0.
- `HolderKeyManager.GetPublicKeyJwk` MUST produce a JWK with `kty: "EC"`, `crv: "P-256"`, and both `x` and `y` coordinates base64url-encoded without padding.
- `CredentialWallet.SaveAsync` MUST extract the `vct` claim from the SD-JWT payload and reject tokens without a `vct` claim (unless fallback naming is used).
- `JwtProofBuilder.Build` MUST include the holder's public key JWK in the JWT header (not the payload).
- `JwtProofBuilder.Build` MUST set `typ` to `"openid4vci-proof+jwt"` per the OID4VCI specification.
- `KbJwtBuilder.Build` MUST compute `sd_hash` as `base64url(sha256(presentationPrefix))` where `presentationPrefix` includes the trailing `~`.
- `KbJwtBuilder.Build` MUST set `typ` to `"kb+jwt"` per the SD-JWT specification.
- `KbJwtBuilder.Build` MUST set `iat` to the current Unix timestamp in seconds.
- Both builders MUST sign with ES256 (ECDSA using P-256 and SHA-256).
- The `haip` section in actor definitions MUST be optional — missing it does not prevent parsing of existing actor definitions.
- `WalkthroughState.credentialPaths` values MUST be valid relative paths that resolve to existing `.sdjwt` files after the walkthrough completes.

## Migration notes

None required. This feature adds only local filesystem storage (PEM/JWK/SDJWT files) and walkthrough scripts. No database entities, no EF migrations, no service-level schema changes.
