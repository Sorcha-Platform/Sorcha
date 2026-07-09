# Phase 0 Research: Ethereum address-form issuer DID verification (Phase 2)

Resolves the code-level unknowns from the design doc §10. All line numbers are **indicative** —
re-locate during implementation. Phase 1 (Feature 177) is the starting point; every seam below is
an *edit to reused code*, not a fork.

## R1 — `ecrecover` in BouncyCastle (no built-in; hand-compute recid 0/1)

- **Decision**: Add `Secp256k1Recovery.TryRecover(message, joseSignature, out candidates)` to the
  primitive. A JOSE ES256K signature is 64 bytes `r‖s` with **no** recovery id, so try recid ∈ {0,1}.
  For each recid: reconstruct the curve point `R` whose x-coordinate is `r` (recid's low bit selects
  the y parity; recid≥2 — the `r + n` wrap — is astronomically rare for secp256k1 and out of scope),
  then compute `Q = r⁻¹ (s·R − e·G)` where `e` is the SHA-256 digest of `message` reduced mod `n`.
  Use BouncyCastle `ECPoint`/`BigInteger` math (`Domain.Curve.DecodePoint` with a `0x02`/`0x03`
  prefix over the 32-byte `r`, `ECAlgorithms.SumOfTwoMultiplies` or explicit `Multiply`). Return the
  valid, non-infinity candidate points. Never throws — malformed input → empty list.
- **`VerifyByAddress(message, joseSignature, expectedAddress)`**: recover candidates → for each derive
  `EthereumAddress.FromPublicKey` → return true iff any equals `expectedAddress`
  (both lowercased). Exposed on `ISecp256k1Verifier` **and** as a `static` entry point (mirrors the
  existing `Secp256k1Verifier.VerifyEs256k` static used by `SdJwtService.Verify`).
- **Rationale**: BouncyCastle has all the curve math but no one-call recover; the standard SEC1 §4.1.6
  recovery is ~30 lines. keccak256 + EIP-55 (`Keccak256`, `EthereumAddress`) already ship from Phase 1
  and are the *only* new runtime callers this phase adds.
- **Anchor (mandatory, built first)**: a known Ethereum test vector — private key
  `0x4c0883a69102937d6231471b5dbb6204fe512961708279f2e3e8a5d4b8e3e3e8` (or any published KAT),
  sign a message → `TryRecover` yields the matching public key among candidates → its EIP-55 address
  equals the vector's known address. Two fixtures pin recid 0 and recid 1 (choose messages/keys whose
  signature parity differs) so both branches are exercised.
- **Alternatives considered**: Nethereum's `EthECKey.RecoverFromSignature` (rejected — pulls a native/
  heavier dependency onto the WASM-critical Verifier path; the whole point of the pure-managed
  primitive is to avoid this). Adding a NuGet ecrecover lib (rejected — no dependency, per FR-013).

## R2 — The seam: recovery-JWK envelope + one verify branch (both currencies)

Both issuer-key resolvers gate on `matched.PublicKeyJwk is not null` today and return null otherwise.
An address-form VM has **no** `publicKeyJwk`. Relax **both** and carry the address in each path's
existing currency:

- **Verifier engine** (`DidResolverBackedIssuerKeyResolver` → `JsonElement?`):
  - Change the VM fallback (`~126`) and null-guard (`~128`) to also accept a VM whose
    `BlockchainAccountId` is set.
  - The assertionMethod gate (`~142-151`) is unchanged — our new resolvers place the recovery VM in
    `assertionMethod`, so it passes.
  - When the matched VM carries `BlockchainAccountId` (no `PublicKeyJwk`), **synthesize** and return a
    recovery-JWK `JsonElement`: `{ "kty":"EC", "crv":"secp256k1", "blockchainAccountId":"eip155:1:0x…" }`.
  - `VerifiablePresentationValidator.VerifyEs256k(JsonElement publicJwk, …)` (`~625`) branches:
    `Secp256k1Jwk.TryParse` succeeds (x/y present) → key-match (Phase 1); else `publicJwk` has
    `blockchainAccountId` → `Secp256k1Verifier.VerifyByAddress(signingInput, signature, address)`.
- **Blueprint engine** (`DidX5cIssuerKeyResolver` → `IssuerKeyResolution`; consumed by
  `SdJwtVcFormatHandler` → `SdJwtService`):
  - Add `string? BlockchainAccountId` to `IssuerKeyResolution` (`IIssuerKeyResolver.cs:~30`).
  - `DidX5cIssuerKeyResolver` (`~95-113`): accept an address-VM (relax the `PublicKeyJwk is null`
    return) and return `IssuerKeyResolution { PublicKey = [], BlockchainAccountId = "eip155:…:0x…",
    Algorithm = "ES256K", SigningKeyId = matched.Id }`.
  - `SdJwtVcFormatHandler` (`~120-125`): thread `key.BlockchainAccountId` into the verify call.
  - `SdJwtService.VerifyPresentationAsync`/`VerifyTokenAsync` gain an **optional**
    `string? issuerRecoveryAddress = null` (backward-compatible; only the format handler passes it),
    threaded to the private `Verify` (`~1059`). ES256K branch: `issuerRecoveryAddress` set →
    `Secp256k1Verifier.VerifyByAddress(data, signature, issuerRecoveryAddress)`; else the Phase-1
    `Secp256k1PublicKey.FromSec1(publicKey)` key-match. `issuerPublicKey` is `[]` for address-form,
    which passes the existing `ThrowIfNull` guard (empty ≠ null).
- **Rationale**: reuses the exact resolution→verify thread in each path; no new resolver interface, no
  changed return type, one one-line verify branch per site. The recovery-JWK envelope is a documented
  type-overload (a JWK-shaped carrier for an address); the alternative (a discriminated key-material
  type) would force changing the `JsonElement?` contract baked into the holder/delegation call sites.
- **Alternatives considered**: (a) discriminated `Secp256k1KeyMaterial` result — rejected (churns
  contracts for no Phase-1 gain); (b) recover→synthesize key→run ordinary key-match verify — rejected
  (redundant second verify; still needs the address threaded, so no seam simplification).

## R3 — `did:pkh` resolver (offline, CAIP-10)

- **Decision**: `PkhDidResolver : IDidResolver`, `CanResolve("pkh")`. Parse
  `did:pkh:eip155:{chainId}:0x{40hex}` — validate the `eip155` namespace, decimal chainId, and a
  40-hex-char `0x` address (case-insensitive); anything else → null (offline, no network). Emit a
  document with one VM: `id = {did}#blockchainAccountId`, `type =
  "EcdsaSecp256k1RecoveryMethod2020"`, `controller = {did}`,
  `blockchainAccountId = "eip155:{chainId}:0x{addr}"`, **no** `publicKeyJwk`; place the VM id in
  `authentication` **and `assertionMethod`**.
- **Rationale**: matches the did:pkh spec's Ethereum representation. The `#blockchainAccountId`
  fragment is the did:pkh convention. `assertionMethod` membership is required or the issuer-key
  resolver's F120 gate rejects it.
- **Scope note**: only the `eip155` (EVM) namespace is in scope; other CAIP-2 namespaces (Bitcoin,
  Solana …) → null.

## R4 — `did:ethr` resolver (offline, default document only)

- **Decision**: `EthrDidResolver : IDidResolver`, `CanResolve("ethr")`. Parse the address-form
  variants and normalise the network segment to a **decimal** chain-id for the CAIP-10 string:
  - `did:ethr:0x{40hex}` → mainnet, chainId 1.
  - `did:ethr:{network}:0x{40hex}` → map a small known-network table (`mainnet`→1, `sepolia`→11155111,
    `goerli`→5, …) → chainId; unknown name → null (offline, cannot look up).
  - `did:ethr:0x{chainIdHex}:0x{40hex}` → the first `0x…` (short, not 40 hex) is the hex chain-id →
    parse to decimal; the second is the address.
  - Distinguish "hex chain-id" from "address": an address is exactly 40 hex chars after `0x`; a
    chain-id segment is a shorter `0x…`. A single `0x{40hex}` with no network → mainnet.
  - Reject a `did:ethr` that is **not** an address-form default document (e.g. one that would require
    reading ERC-1056 registry events) → null (Phase 2b).
  - Emit the ERC-1056 **default document** VM: `id = {did}#controller`, `type =
    "EcdsaSecp256k1RecoveryMethod2020"`, `controller = {did}`,
    `blockchainAccountId = "eip155:{decimalChainId}:0x{addr}"`, in `authentication` +
    `assertionMethod`.
- **Rationale**: the ERC-1056 default document (no on-chain events) is deterministic and offline —
  exactly the `#controller` recovery VM. Chain-id normalisation only affects the emitted CAIP-10 and
  address extraction; recovery ignores chain-id (R6) and the allowlist matches the DID **string**, so
  named-vs-hex-vs-bare forms of the *same* DID string trust independently (they are literally
  different DIDs unless authored identically).
- **Seam for Phase 2b**: the resolver takes an **optional** `IEvmRpcClient` (default null). Null →
  the default document shipped here. Non-null (later) → read `DIDOwnerChanged`/`DIDDelegateChanged`/
  `DIDAttributeChanged` to build the current doc. No such client exists or is referenced now.

## R5 — `VerificationMethod.BlockchainAccountId` model field

- **Decision**: add `public string? BlockchainAccountId { get; set; }` to `VerificationMethod`
  (`DidDocument.cs:~91`), `[JsonPropertyName("blockchainAccountId")]`,
  `[JsonIgnore(WhenWritingNull)]`. Purely additive; existing key-form VMs never set it.
- **Rationale**: the W3C-standard field name for `EcdsaSecp256k1RecoveryMethod2020` /
  `did:pkh` VMs. Keeps `DidDocument` the single VM shape both key-form and address-form flow through.

## R6 — Chain-id is identity, not crypto; address match is case-insensitive

- **Decision**: signature recovery uses only `message` + `r‖s`; chain-id never enters recovery. The
  recovered EIP-55 address is matched to the DID's address **case-insensitively** (lowercase both;
  the DID may carry a lowercase or an EIP-55-checksummed address). Trust matching is unchanged: the
  existing `DidAllowlistTrustSourceResolver` compares the full `iss` DID string, so `eip155:1` vs
  `eip155:137` for the same address are different allowlist entries (FR-007) for free.
- **Rationale**: keeps the primitive chain-agnostic (reusable for any EVM chain) while preserving
  chain-aware trust with zero new trust code.

## R7 — No `TrustEvaluator` / assurance change this phase

- **Decision**: Phase 2 touches **no** trust code. The Phase-1 `TrustPolicy.WarnOnUnlistedVerifiedIssuer`
  (default false), `AssuranceLevel.None`, `TrustDecision.ReducedAssurance`, and the Pass/Warn/Reject
  mapping already handle a signature-verified-but-unlisted issuer identically for address-form DIDs —
  the trust layer sees a verified signature + an `iss` DID and does not care how the signature was
  verified. The fail-closed regression (address-form unlisted issuer, flag unset → reject) proves no
  bleed.
- **Rationale**: the seam ends at "SignatureVerified = true/false"; everything downstream is Phase-1.

## Open items deliberately NOT resolved here (out of Phase 2 scope)

ERC-1056 registry read / key rotation / delegates / service endpoints (Phase 2b, needs
`IEvmRpcClient`); holder address-form key-binding; EIP-712 JSON-LD / EAS; secp256k1 signing /
recoverable message signing / `WalletNetworks`; any EVM RPC / node / on-chain write.
