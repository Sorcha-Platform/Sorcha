# Phase 1 Quickstart (verify-only, offline)

How to exercise the Ethereum-key verification path end-to-end — entirely offline, no node, no RPC.
This is a test/dev walkthrough; there is no runtime service to start.

## Prerequisites

- .NET 10 SDK. No network access required at any step.
- The new project `src/Common/Sorcha.Cryptography.Secp256k1` builds (BouncyCastle only).

## 1. Verify the primitive (known-answer vectors)

```bash
dotnet test tests/Sorcha.Cryptography.Secp256k1.Tests \
  --filter "FullyQualifiedName~Secp256k1"
```
Expected: ES256K verify passes a published JOSE ES256K vector and rejects a tampered one; a
secp256k1 JWK round-trips (incl. 33-byte compressed-point decompress); keccak256 + EIP-55 match a
known private-key → address vector.

## 2. Resolve an Ethereum key-bearing DID (offline)

```bash
dotnet test tests/Sorcha.ServiceClients.Tests \
  --filter "FullyQualifiedName~KeyDidResolver|FullyQualifiedName~JwkDidResolver"
```
Expected: `did:key:zQ3s…`(secp256k1) and `did:jwk:…`(secp256k1) resolve to a DID document whose
verification method carries `publicKeyJwk` with `crv:"secp256k1"`; `did:jwk` also resolves a P-256
and an Ed25519 key.

## 3. Verify a full credential through the existing pipeline

Fixtures (committed under the test project): a Veramo-style ES256K SD-JWT issued by a
`did:key`(secp256k1) issuer, plus a `did:jwk`(secp256k1) EUDI-style fixture.

```bash
dotnet test tests/Sorcha.Cryptography.Tests --filter "FullyQualifiedName~SdJwt&FullyQualifiedName~Es256k"
# and the format-handler integration tests (blueprint/verifier engine)
```

The four outcome cases the integration tests assert:

| Fixture / requirement | Expected outcome |
|---|---|
| Valid ES256K credential, issuer **allowlisted** | **Pass** (full assurance) |
| Valid ES256K credential, issuer **unlisted**, `warnOnUnlistedVerifiedIssuer:false` | **Reject** (fail-closed) |
| Valid ES256K credential, issuer **unlisted**, `warnOnUnlistedVerifiedIssuer:true` | **Warn** (reduced assurance) |
| **Tampered** ES256K credential (any requirement) | **Reject** (signature invalid) |
| Holder KB-JWT signed by wrong key | **Reject** (binding failed) |

## 4. Confirm nothing regressed (fail-closed guard)

```bash
dotnet test --filter "FullyQualifiedName~Credential|FullyQualifiedName~Trust|FullyQualifiedName~Verifier"
```
Expected: the existing ES256 / EdDSA SD-JWT + mdoc verification suite passes unchanged, and a
non-Ethereum unlisted issuer (no flag) still fail-closes to Reject.

## Offline guarantee

Every step above runs with no network. `did:key`/`did:jwk` resolution is pure decoding; there is no
RPC, no `did:ethr`/`did:pkh` address lookup, and no on-chain access anywhere in the Phase 1 path.
