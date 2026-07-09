# Contract: issuer-key resolution → verify seam (both surfaces)

The two issuer-key resolvers relax their `PublicKeyJwk is not null` gate to accept an address-form VM
and carry the address forward; the single ES256K verify branch in each surface dispatches
key-match vs address-recovery.

## Verifier engine surface

`DidResolverBackedIssuerKeyResolver.ResolveAsync(issuer, kid, ct) : JsonElement?`

| Matched VM | Return |
|---|---|
| has `PublicKeyJwk` | that JWK (Phase 1, unchanged) |
| has `BlockchainAccountId`, no `PublicKeyJwk`, in `assertionMethod` | synthesised recovery-JWK `{ "kty":"EC","crv":"secp256k1","blockchainAccountId":"…" }` |
| neither / not in assertionMethod | `null` (reject) |

`VerifiablePresentationValidator.VerifyEs256k(JsonElement publicJwk, signingInput, signature) : bool`

| `publicJwk` | Path |
|---|---|
| parses to x/y (`Secp256k1Jwk.TryParse`) | key-match (Phase 1) |
| has `blockchainAccountId`, no x/y | `Secp256k1Verifier.VerifyByAddress(signingInput, signature, address)` |

## Blueprint engine surface

`DidX5cIssuerKeyResolver.ResolveAsync(rawSdJwt, ct) : IssuerKeyResolution?`

| Matched VM | Return |
|---|---|
| has `PublicKeyJwk` (secp256k1) | `{ PublicKey = SEC1 bytes, Algorithm = "ES256K" }` (Phase 1) |
| has `BlockchainAccountId`, no `PublicKeyJwk`, in `assertionMethod` | `{ PublicKey = [], BlockchainAccountId = "eip155:…:0x…", Algorithm = "ES256K", SigningKeyId = vm.Id }` |
| neither / not in assertionMethod | `null` (reject) |

`SdJwtVcFormatHandler` → `SdJwtService.VerifyPresentationAsync(raw, key.PublicKey, key.Algorithm, ct, issuerRecoveryAddress: key.BlockchainAccountId)`

`SdJwtService.Verify(...)` ES256K branch:

| Condition | Path |
|---|---|
| `issuerRecoveryAddress` is null | `Secp256k1PublicKey.FromSec1(publicKey)` key-match (Phase 1) |
| `issuerRecoveryAddress` set | `Secp256k1Verifier.VerifyByAddress(data, signature, issuerRecoveryAddress)` |

- `issuerRecoveryAddress` is an **optional** param (`= null`) added to `VerifyTokenAsync` /
  `VerifyPresentationAsync` / private `Verify` — backward-compatible; only the format handler passes it.
- Address-recovery applies to the **issuer** JWS only; the holder KB-JWT still key-matches `cnf.jwk`.

## Outcome contract (unchanged from Phase 1)

| Situation | Outcome |
|---|---|
| address-recovery matches + issuer allow-listed | **Pass** |
| address-recovery matches + unlisted + `WarnOnUnlistedVerifiedIssuer` set | **Warn** (reduced assurance) |
| address-recovery matches + unlisted + flag unset | **Reject** (fail-closed default) |
| no candidate matches / tampered / malformed DID / unresolved | **Reject** |
