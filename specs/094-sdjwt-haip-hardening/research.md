# Phase 0 Research: SD-JWT VC HAIP Hardening

**Feature**: 094-sdjwt-haip-hardening
**Date**: 2026-04-09

## Research items

1. BIP32 purpose derivation: does Sorcha have a reusable primitive, or do we build one?
2. KB-JWT wire format: where does the `[kb-jwt]` slot sit in the serialised SD-JWT, and what's the exact payload shape?
3. Nested `_sd` digest arrays: how deep can they go, and how do array-element disclosures work?
4. Holder key JWK format on the wire at issuance time: how does the caller (internal or external) supply it?
5. Classical co-key selection at signing time: how does the signer pick between primary key and co-key?
6. Backward compatibility for legacy (no `cnf`) credentials at verification time.

---

## R1. BIP32 purpose derivation primitive

### Current state

Features 086 (`sorcha:docket-signing`) and 092 (`sorcha:persona-vault`) both derive purpose-scoped sub-keys from the wallet's HD seed. A grep for `DeriveKey` in `src/Common/Sorcha.Cryptography` finds the primitive exists. The wallet domain already has a concept of "derivation purpose" as a string that gets hashed into the derivation path. Spec 094 adds two new purpose strings and nothing more:

- `sorcha:credential-holder-binding` — one key per wallet, used to sign KB-JWTs
- `sorcha:haip-issuer-signing` — one classical co-key per HAIP-issuer wallet, used to sign SD-JWT VCs at the HAIP boundary

### Decision: reuse the existing primitive, add two purpose strings

**Rationale.** The precedent is well established. No new cryptographic primitive is required. The planning phase must verify the exact derivation API surface (likely `IKeyManagementService.DeriveKeyAsync(walletAddress, purpose, ct)` or similar).

**Consequence.** Two new `const string` declarations somewhere in a shared location (likely `Sorcha.Cryptography/SorchaDerivationPaths.cs` if it exists, otherwise a new file). Two new service methods: `HolderBindingKeyService.GetPublicKeyAsync(walletAddress)` and `.SignKbJwtAsync(walletAddress, payload)`. Same shape for `HaipIssuerCoKeyService`.

**Alternative rejected.** Storing the derived keys as standalone wallet entities rather than deriving them on demand. This would require a schema change and break the "mnemonic recovers everything" promise that HD wallets provide.

---

## R2. KB-JWT wire format and appending

### Current state

`SdJwtService.CreatePresentationAsync` at `src/Common/Sorcha.Cryptography/SdJwt/SdJwtService.cs:213-266` constructs the presentation as `jwtPart ~ selected_d1 ~ selected_d2 ~` but **never appends a KB-JWT**. The comment on line 253 (`// Build presentation: jwt~selected_disclosure1~…~[kb-jwt]`) admits the slot is empty. Parameters `holderKey`, `audience`, `nonce` are accepted and ignored.

The IETF SD-JWT draft specifies the KB-JWT as an additional JWT appended after the last `~` with its own header and payload. Its payload contains:

- `aud` — the verifier's expected audience
- `nonce` — the verifier's issued nonce
- `iat` — issuance time of the KB-JWT (Unix seconds)
- `sd_hash` — SHA-256 hash of the preceding portion of the serialised presentation (the issuer JWT + selected disclosures, concatenated exactly as they appear on the wire, including trailing `~`), base64url-encoded

### Decision: append a signed JWT after the last disclosure's `~`, signed by the holder key named in the credential's `cnf.jwk`

**Rationale.** Matches the IETF draft and HAIP 1.0. The signing algorithm is inferred from the `cnf.jwk.kty` + `crv` combination (or the existing `SorchaDidResolver`'s algorithm mapper, extracted as a helper).

**Wire shape example:**
```
eyJ...<issuer-jwt>...~WyJ...disclosure1...~WyJ...disclosure2...~eyJ...<kb-jwt>...
```

**Consequence.** `SdJwtService.CreatePresentationAsync` becomes the holder-signing path (stays in the library, callers supply the signing delegate). A new method `CreatePresentationAsync` overload accepts a `Func<byte[], Task<byte[]>>` signing delegate — this lets the Wallet Service supply a KB-JWT signer that internally calls the `HolderBindingKeyService` without exposing private key material to the library. For external holders (HAIP wallets), the caller supplies their own signing delegate wrapping their own key.

`SdJwtService.VerifyPresentationAsync` learns to split the presentation at the final `~`, parse the trailing segment as a JWT, verify the signature against `cnf.jwk`, check `aud`, `nonce`, `iat` (within the clock-skew window), and `sd_hash`.

**Alternative rejected.** Putting the KB-JWT signer inside `SdJwtService` as a method that takes raw key bytes. This would leak holder private keys into the library layer, which violates the existing pattern where signing lives behind `IKeyManagementService`.

---

## R3. Nested `_sd` digest arrays and array-element disclosure

### Current state

`SdJwtService.CreateTokenAsync` at `SdJwtService.cs:27-113` handles top-level name-keyed disclosures only. The loop at lines 49-59 iterates `disclosableSet` and adds the digest to a flat `sdDigests` list, which is then written to `payload["_sd"]` at line 82. There is no support for nested fields or array elements.

The IETF SD-JWT draft specifies nested disclosure by placing `_sd` arrays inside nested objects and by using per-element object notation `{"...": digest}` for array members. For example, the wire payload for an address where only `locality` is disclosable:

```json
{
  "address": {
    "country": "GB",
    "_sd": ["<sha256-of-locality-disclosure>"]
  }
}
```

And for an array of qualifications where each element is independently disclosable:

```json
{
  "qualifications": [
    {"...": "<sha256-of-element-0-disclosure>"},
    {"...": "<sha256-of-element-1-disclosure>"},
    {"...": "<sha256-of-element-2-disclosure>"}
  ]
}
```

### Decision: introduce a `NestedDisclosure` translator helper

**Rationale.** The disclosure set on the caller side is a list of JSON Pointer paths (`/address/locality`, `/qualifications/0`, etc.). The translator walks the claims tree and for each disclosable path:

1. Generates a random 16-byte salt.
2. Serialises the disclosure as `[salt, key, value]` for nested object fields, or `[salt, value]` for array elements (IETF draft distinguishes these by arity).
3. Base64url-encodes the disclosure and computes its SHA-256 digest.
4. Replaces the claim value in the parent container with either an `_sd` array entry (object fields) or a `{"...": digest}` placeholder (array elements).
5. Returns the list of serialised disclosures for appending to the wire.

**Consequence.** A new `NestedDisclosure.cs` file in `Sorcha.Cryptography.SdJwt`. Two public methods: `Translate(Dictionary<string, object> claims, IEnumerable<string> disclosablePaths)` and `Reconstruct(Dictionary<string, object> decodedPayload, IEnumerable<string> presentedDisclosures)`. The existing top-level name-keyed path is preserved as a special case: paths that are bare names (no leading `/`) are treated as top-level claim names for backward compatibility.

**Alternative rejected.** Changing the wire format of the existing top-level disclosures. Rejected because FR-021 / SC-006 mandates byte-for-byte compatibility for existing Blueprints.

---

## R4. Holder key JWK format at issuance

### Current state

`CredentialEndpoints.IssueCredential` at `src/Services/Sorcha.Wallet.Service/Endpoints/CredentialEndpoints.cs:289-418` accepts `IssueCredentialRequest` which has no holder key field. The issuer signs with its own wallet key and the credential is a bearer token.

### Decision: add an optional `HolderJwk` field to `IssueCredentialRequest`

**Rationale.** When present, the issuer embeds `{"cnf": {"jwk": holderJwk}}` in the signed payload. When absent (legacy callers), the credential is issued without `cnf` and the verifier treats it as pre-fix per FR-006.

For the Blueprint-internal path (the common case), the caller does not supply the JWK directly. Instead, the Blueprint Service's `ActionExecutionService` calls a new `HolderBindingKeyService.GetPublicJwkAsync(recipientWalletAddress)` before invoking the Wallet Service issue call, and passes the returned JWK through.

**Consequence.** One new field on the request DTO, one new plumbing step in `ActionExecutionService`, no change to the direct HTTP caller path.

**Alternative rejected.** Automatically deriving the holder key from the `recipientWallet` field inside `CredentialEndpoints.IssueCredential`. Rejected because it would require a second IWalletRepository dependency in the handler and would blur the "caller supplies the holder identity" contract — HAIP external wallets (spec 097) must supply their own JWK, and the Blueprint path should follow the same contract.

---

## R5. Classical co-key selection at signing time

### Current state

`SdJwtService.Sign` at `SdJwtService.cs:307-332` takes raw private key bytes and branches on an algorithm string (`EDDSA`, `ES256`, `RS256`). PQC algorithms throw `NotSupportedException`.

### Decision: introduce a `HaipIssuerSelector` in the Wallet Service layer

**Rationale.** The library (`SdJwtService`) stays unaware of co-keys — it just receives whichever key bytes + algorithm the caller passes. The *Wallet Service* is where the choice between primary key and co-key is made:

- If the wallet's primary algorithm is classical (Ed25519, P-256, RSA-4096), use it directly.
- If the wallet's primary algorithm is PQC (ML-DSA-65, SLH-DSA-*) AND the wallet carries the `HaipIssuer` capability flag, derive (or retrieve a cached) classical co-key under `sorcha:haip-issuer-signing` and use that instead.
- If the wallet is PQC-primary and has no `HaipIssuer` flag, refuse HAIP-path issuance with a clear capability-missing error (FR-034) — without ever touching the crypto library.

The selection happens in `CredentialEndpoints.IssueCredential` by calling a new `IHaipIssuerCoKeyService.GetSigningKeyForHaipIssuanceAsync(walletAddress)` which returns `(byte[] privateKey, string algorithm)`. The returned pair is passed to `SdJwtService.CreateTokenAsync` unchanged.

**Consequence.** The `CredentialEndpoints.IssueCredential` handler gains a pre-step that resolves the correct signing key. The decision is entirely in the Wallet Service; `SdJwtService` stays algorithm-agnostic.

**Alternative rejected.** Adding PQC algorithms to `SdJwtService.Sign`. Rejected because HAIP 1.0 is classical-only and adding PQC would expose a failure mode where external verifiers reject tokens for unrecognised algorithms. Better to force classical at the Wallet Service layer.

---

## R6. Backward compat for legacy (no `cnf`) credentials at verification time

### Current state

Spec 093 already established the verified-token path. It fetches claims from the verified token's `Claims` dict. If the token has no `cnf` claim, the verifier currently has no awareness of KB-JWT requirements.

### Decision: the verifier only requires KB-JWT when the credential carries `cnf`

**Rationale.** Per FR-006 and FR-007: a token with no `cnf` is a legacy bearer credential, and the verifier must accept it without requiring KB-JWT. A token with `cnf` MUST have a KB-JWT on the presentation wire, or verification fails.

**Consequence.** `SdJwtService.VerifyPresentationAsync` splits the presentation into JWT + disclosures + optional KB-JWT. If the decoded credential JWT payload contains `cnf`, the KB-JWT is mandatory. If not, the KB-JWT is ignored (and if one is present anyway, it is optionally validated but never required).

**Alternative rejected.** Making KB-JWT mandatory for all credentials after this spec ships. Rejected because it would invalidate every pre-fix credential in production wallets.

---

## Summary

All six research items resolved. No `NEEDS CLARIFICATION` markers remain. Key decisions:

1. **Reuse existing BIP32 purpose derivation**; add two new purpose constants.
2. **KB-JWT signer is a delegate** passed into `SdJwtService.CreatePresentationAsync`, so the library never sees raw keys. Wallet Service supplies the delegate internally; external holders supply their own.
3. **`NestedDisclosure` translator** in `Sorcha.Cryptography.SdJwt` handles JSON-Pointer-path disclosables additively — top-level name-keyed disclosure stays byte-for-byte compatible.
4. **`HolderJwk` field** on `IssueCredentialRequest` carries the holder public key to the issuer at issuance time.
5. **`HaipIssuerCoKeyService`** picks the right signing key in the Wallet Service layer; `SdJwtService` stays algorithm-agnostic.
6. **Legacy credentials** (no `cnf`) verify unchanged; KB-JWT is mandatory only when `cnf` is present.

Ready for Phase 1.
