# Phase 1 Contracts (internal seams)

Phase 1 adds **no HTTP/gRPC endpoint** — verification is an internal pipeline. The "contracts"
are the internal seam(s) and the two author-facing/wire shapes that other components depend on.

## 1. `ISecp256k1Verifier` (new, `Sorcha.Cryptography.Secp256k1`)

```csharp
public interface ISecp256k1Verifier
{
    /// <summary>Verify a JOSE ES256K signature (ECDSA over SHA-256, 64-byte r‖s) against a secp256k1 public key.</summary>
    bool Verify(ReadOnlySpan<byte> message, ReadOnlySpan<byte> joseSignature, Secp256k1PublicKey key);
}
```
- **Consumers**: `SdJwtService.Verify` (issuer JWS + KB-JWT), `VerifiablePresentationValidator.VerifyEs256k`, `DidX5cIssuerKeyResolver` (parse), the `did:key`/`did:jwk` resolvers (JWK build).
- **Contract**: pure, deterministic, no I/O, no allocation of secrets. Returns `false` on any malformed input (never throws for bad key/signature material).

## 2. Blueprint credential-requirement JSON (author-facing)

The single new author-visible surface — a boolean on a requirement's trust policy:

```jsonc
{
  "type": "AssuredIdentityCredential/v1",
  "format": "SdJwtVc",
  "trustPolicy": {
    "sources": [
      { "kind": "DidAllowlist", "allowedIssuers": ["did:key:zQ3s…", "did:jwk:eyJ…"] }
    ],
    "combinator": "AnyOf",
    "minAssuranceLevel": "Low",
    "warnOnUnlistedVerifiedIssuer": false   // NEW — default false; true = accept signature-valid unlisted issuer at reduced assurance (Warn)
  }
}
```
- **Default**: absent/`false` → fail-closed (unlisted issuer rejected). Existing blueprints are unaffected.
- **Semantics**: see the state machine in `data-model.md`.

## 3. secp256k1 DID document (resolution output)

`did:key`(secp256k1, multicodec `0xe701`) and `did:jwk`(secp256k1) resolve to:

```jsonc
{
  "id": "did:key:zQ3s…",
  "verificationMethod": [{
    "id": "did:key:zQ3s…#zQ3s…",
    "type": "JsonWebKey2020",
    "controller": "did:key:zQ3s…",
    "publicKeyJwk": { "kty": "EC", "crv": "secp256k1", "x": "…", "y": "…" }
  }],
  "assertionMethod": ["did:key:zQ3s…#zQ3s…"],
  "authentication": ["did:key:zQ3s…#zQ3s…"]
}
```
- **Invariant**: `publicKeyJwk` MUST be present (the issuer-key resolvers consume JWK, not `publicKeyMultibase`).

## 4. `TrustDecision` warn extension (engine-internal)

```csharp
// A verified-but-untrusted (Warn) decision:
new TrustDecision {
    IsTrusted = true, SignatureValid = true,
    EstablishedAssurance = AssuranceLevel.None,   // new lowest member
    ReducedAssurance = true,                       // new Warn signal
    Evidence = /* records unlisted-verified-issuer + policy digest */
}
```
- **Consumer contract**: any consumer that treats `IsTrusted == true` as "accept" MUST honour `ReducedAssurance` to distinguish Warn from Pass. The verifier UI maps it to `VerifyOutcome.Warn`; the blueprint gate accepts-with-evidence.

## Contract tests (what must hold)

- `ISecp256k1Verifier.Verify` returns `true` for a valid ES256K KAT, `false` for a tampered signature, `false` (never throw) for malformed key/sig.
- A `did:key`/`did:jwk` secp256k1 resolves to a VM carrying `publicKeyJwk` with `crv:"secp256k1"`.
- `warnOnUnlistedVerifiedIssuer:false` on an unlisted issuer → Reject; `true` → Warn; allowlisted → Pass (flag ignored).
- A non-secp256k1 credential and a policy without the flag behave exactly as before (fail-closed regression).
