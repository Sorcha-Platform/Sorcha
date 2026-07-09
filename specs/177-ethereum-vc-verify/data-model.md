# Phase 1 Data Model

Phase 1 is verify-only: it adds no persisted domain entity beyond one optional blueprint field.
The "entities" here are the value types the verification pipeline exchanges and the two model
additions to existing types.

## New value types (in `Sorcha.Cryptography.Secp256k1`)

### `Secp256k1PublicKey`
- **Represents**: an secp256k1 public key as a curve point.
- **Fields**: `X` (32 bytes), `Y` (32 bytes). Constructed from a JOSE EC JWK (`x`,`y`) or by decompressing a 33-byte compressed point (for `did:key` `0xe701` / `did:jwk`).
- **Validation**: point MUST lie on the secp256k1 curve; malformed/off-curve → construction fails → verification Rejects.

### `Secp256k1Jwk` (codec, not persisted)
- Parse: JOSE EC JWK (`kty:"EC"`, `crv:"secp256k1"`, base64url `x`,`y`) → `Secp256k1PublicKey`.
- Build: `Secp256k1PublicKey` → JWK (used to emit `publicKeyJwk` in the `did:key`/`did:jwk` DID document VM).
- Decompress: 33-byte compressed point → `X`,`Y`.

### `ISecp256k1Verifier` (behaviour)
- `bool Verify(message, joseSignature, Secp256k1PublicKey)` — ECDSA over SHA-256, 64-byte fixed-width `r‖s`; accepts high-or-low-s.

### Foundation (no Phase 1 runtime caller)
- `Keccak256(bytes) → 32 bytes`.
- `EthereumAddress.FromPublicKey(Secp256k1PublicKey) → 0x + EIP-55` — tested against a known vector only.

## Additions to existing types

### `TrustPolicy` (`Sorcha.Blueprint.Models/Credentials`)
- **New field**: `bool WarnOnUnlistedVerifiedIssuer` — default `false`.
- **Meaning**: when `true`, a signature-valid issuer that no trust source vouches for yields a **reduced-assurance (Warn)** outcome instead of a reject. When `false` (default), fail-closed reject is unchanged.
- **Covered by**: the policy digest (`ComputePolicyDigest`) so the choice is pinnable in `TrustEvidence`.

### `AssuranceLevel` (`Sorcha.Blueprint.Engine/Credentials`)
- **New member**: `None = -1` (below `Low=0`). Used only for the verified-but-untrusted (Warn) outcome; being below any `MinAssuranceLevel` floor, it can never silently satisfy an assurance requirement.

### `TrustDecision` (`Sorcha.Blueprint.Engine/Credentials`)
- **New field**: `bool ReducedAssurance` (Warn signal). When a warn is returned, `IsTrusted = true`, `SignatureValid = true`, `EstablishedAssurance = None`, `ReducedAssurance = true`. Distinguishes a warn from a full-trust pass.

### `VerificationOutcome` (`Sorcha.Verifier.Engine.Models`)
- **New flag** carrying the reduced-assurance signal outward so `RealVerifierEngine.Map` can produce `VerifyOutcome.Warn` (reuses the existing `IssuerSignatureStatus.NotVerified → Warn` shape).

## DID document verification-method shape (secp256k1)

A `did:key`(secp256k1) or `did:jwk`(secp256k1) resolves to a DID document with a verification method:
- `type`: `"JsonWebKey2020"`
- `publicKeyJwk`: `{ "kty":"EC", "crv":"secp256k1", "x":"…", "y":"…" }` **(required — the verify path consumes JWK, not multibase)**
- optionally `publicKeyMultibase` for consistency
- listed in `assertionMethod` (issuer) / usable for holder `cnf` matching.

## Verification outcome state machine

```
signature verified? ──no──────────────────────────────► REJECT
        │yes
        ▼
trust source vouches (allowlist)? ──yes──► PASS (assurance from config)
        │no
        ▼
WarnOnUnlistedVerifiedIssuer == true? ──no──► REJECT (fail-closed, unchanged)
        │yes
        ▼
                                        WARN (IsTrusted, assurance None, ReducedAssurance)
```

Holder key-binding is a separate cryptographic gate (secp256k1 `cnf` + ES256K KB-JWT proof): a failed binding is always a REJECT, independent of the issuer-trust branch above.
