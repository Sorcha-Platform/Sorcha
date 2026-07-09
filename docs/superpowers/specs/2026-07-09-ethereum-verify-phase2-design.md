# Ethereum-key VC Verification — Phase 2 (address-form issuer DIDs, offline ecrecover)

- **Date:** 2026-07-09
- **Status:** Design approved (brainstorm), pending implementation plan
- **Scope:** Phase 2 of the 4-phase Ethereum interoperability roadmap (Feature 178)
- **Builds on:** Phase 1 (Feature 177, PR #1140) — the `Sorcha.Cryptography.Secp256k1` primitive, `did:key`(secp256k1)/`did:jwk` resolvers, ES256K verify branches, and `TrustPolicy.WarnOnUnlistedVerifiedIssuer`
- **Related:** DID resolution architecture (`Sorcha.ServiceClients.Did`), F120 DID-backed issuer keys, F135 unified trust (`ITrustEvaluator`), F155 reduced-assurance precedent

---

## 1. Context & motivation

Phase 1 taught Sorcha to verify secp256k1/ES256K-signed VCs **where the DID resolves offline to a public key** — `did:key`(secp256k1 `0xe701`) and `did:jwk`. That covers the Web3-SSI credentials whose DID embeds the key, but it does **not** cover the two most widely-deployed Ethereum issuer DID methods:

- **`did:pkh`** (CAIP-10) — the DID *is* an address; the DID document commits to a `blockchainAccountId`, never a public key.
- **`did:ethr`** (ERC-1056) in its **default, no-rotation** form — the DID is an address; its *default* DID document (the one you get with no on-chain registry events) likewise resolves to an address-only verification method.

For both, the DID document carries a verification method of type **`EcdsaSecp256k1RecoveryMethod2020`** with a **`blockchainAccountId`** (CAIP-10, e.g. `eip155:1:0x…`) and **no** `publicKeyJwk`. Verification is therefore **recover-then-match**: recover the secp256k1 public key from the ES256K JOSE signature, derive the Ethereum address (keccak256 → last-20 → EIP-55), and match it to the DID's address. This needs no key lookup and **no chain interaction** — it is entirely offline.

The 4-phase roadmap (Feature 177 memory):

| Phase | Capability | Status |
|---|---|---|
| 1 | Verify ES256K VCs where the DID resolves **offline to a key** | Shipped (F177, PR #1140) |
| **2 (this spec)** | **`did:pkh` + address-form `did:ethr` (default doc) via offline `ecrecover`** | This spec (F178) |
| 2b | ERC-1056 registry read (rotation/delegates/service) via read-only EVM RPC | Deferred — seam only |
| 3 | Prove-control / SIWE — expose the wallet ETH address + secp256k1 message signing | Deferred |
| 4 | Transacting — payments / contract writes via Nethereum | Deferred |

This spec covers **Phase 2 only** and designs its seams so Phase 2b (RPC-backed ERC-1056) slots into the same `EthrDidResolver` without a rewrite. **No PII on-chain, ever.**

## 2. Goal & non-goals

**Goal.** Sorcha can verify a W3C VC / SD-JWT VC signed with a secp256k1 key (JOSE **ES256K**) in the **issuer** position, where the issuer DID is an **address-form** `did:pkh` or default-document `did:ethr` whose DID document resolves **offline** to an **address only**. Verification recovers the public key from the ES256K signature and matches its EIP-55 address to the DID's `blockchainAccountId`. The credential flows through the **existing** verification pipeline (issuer-key resolver → ES256K verify branch → trust evaluator); the only new crypto is `ecrecover` added to the existing primitive. Works identically for a Web3-SSI (`did:pkh`) and a Veramo-style (`did:ethr`) credential — shared VC plumbing.

**Non-goals (deferred or excluded):**

- **No EVM RPC / provider config / node.** No ERC-1056 registry event read; only the *default* (no-rotation) `did:ethr` document → **Phase 2b**.
- **No key rotation / delegates / service endpoints** for `did:ethr` (those require the registry read) → **Phase 2b**.
- **No holder key-binding by address.** Phase 2 is issuer-position only. (Holder key-binding continues to require a `cnf.jwk` key, per Phase 1.)
- **No `EthereumEip712Signature2021` / JSON-LD Data Integrity; no EAS attestations** — different data model, no JSON-LD path in Sorcha.
- **No secp256k1 signing / recoverable message signing / `WalletNetworks` change** → **Phase 3**.
- **No new trust code.** Reuses Phase 1's allowlist + `WarnOnUnlistedVerifiedIssuer` unchanged.

## 3. Confirmed design decisions

1. **Verification model:** ES256K JWT-VC / SD-JWT, **address-recovery** — `ecrecover` the public key from the signature, derive EIP-55, match the DID's `blockchainAccountId`. Fully offline. A match **is** the proof (recovery is defined by the signature; no redundant second verify).
2. **Positions:** **issuer JWS only.** No holder key-binding by address.
3. **DID methods:** `did:pkh:eip155:{chainId}:0x{40hex}` and address-form `did:ethr` (`did:ethr:0x{40hex}` | `did:ethr:{network}:0x{40hex}` | `did:ethr:0x{chainIdHex}:0x{40hex}`, mainnet default) — **default DID document only**.
4. **The seam (Approach 1 — recovery-JWK envelope + single verify branch):** the two DID resolvers emit a standards-correct `EcdsaSecp256k1RecoveryMethod2020` VM carrying `blockchainAccountId` and **no** `publicKeyJwk`. The two issuer-key resolvers, on matching such a VM, carry the address forward in the **existing** key-material currency (a JWK-shaped `JsonElement` bearing `blockchainAccountId` for the Verifier engine; `IssuerKeyResolution.BlockchainAccountId` for the Blueprint service). The **single** ES256K verify branch dispatches: coordinates present → Phase-1 key-match; `blockchainAccountId` present → address-recovery. **No parallel verifier, no new resolver interface, no changed return contract.**
5. **Chain-id is identity, not crypto:** the address is the recovery-match target; chain-id is part of the DID string (so `DidAllowlistTrustSourceResolver` matching is chain-aware for free) but is **irrelevant to signature recovery**.
6. **Trust reuses Phase 1 verbatim:** allowlisted address-form DID → Pass; signature-valid-but-unlisted → governed by the existing `TrustPolicy.WarnOnUnlistedVerifiedIssuer` (default false → fail-closed) → Warn/Reject. **No new trust code.**
7. **Primitive stays pure-managed** (BouncyCastle only, WASM-safe). `ecrecover` composes with the keccak256 + `EthereumAddress` (EIP-55) already shipped in Phase 1. No new dependency; no signing API.

## 4. Where it fits in the codebase (project map)

Phase 2 edits **four** existing projects — no new project (the primitive already exists from Phase 1).

| Project | Change |
|---|---|
| **`src/Common/Sorcha.Cryptography.Secp256k1`** | **New** `Secp256k1Recovery` (recover candidate public keys from an ES256K signature, recid 0 and 1) + a `VerifyByAddress(message, joseSignature, expectedAddress)` (recover → EIP-55 → case-insensitive match). Composes the existing `Keccak256` / `EthereumAddress.FromPublicKey`. BouncyCastle only. |
| **`src/Common/Sorcha.ServiceClients.Http`** | **New** `Did/PkhDidResolver.cs` and `Did/EthrDidResolver.cs` (offline, default-document, emit an `EcdsaSecp256k1RecoveryMethod2020` VM with `blockchainAccountId`). **Edit** `Did/DidDocument.cs` — add `VerificationMethod.BlockchainAccountId`. **Edit** `Extensions/HttpServiceCollectionExtensions.cs::AddDidResolvers` to register both. |
| **`src/Common/Sorcha.Verifier.Engine`** | **Edit** `DidResolverBackedIssuerKeyResolver.cs` — accept a `blockchainAccountId`-bearing VM (relax the `PublicKeyJwk is not null` gate) and return a recovery-JWK `JsonElement`. **Edit** `VerifiablePresentationValidator.cs::VerifyEs256k` — branch to address-recovery when the JWK carries `blockchainAccountId`. |
| **`src/Services/Sorcha.Blueprint.Service` (+ `src/Blueprint/Sorcha.Blueprint.Engine`)** | **Edit** `Credentials/DidX5cIssuerKeyResolver.cs` — accept a `blockchainAccountId`-bearing VM and populate a new `IssuerKeyResolution.BlockchainAccountId`. **Edit** the engine-side ES256K verify branch (`SdJwtService.Verify` reach) to dispatch address-recovery when the resolution carries an address instead of a key. |

> Line numbers are omitted deliberately — the implementation plan re-locates exact insertion points, as code drifts.

## 5. Architecture

### 5.1 The primitive — `Secp256k1Recovery` + `VerifyByAddress`

`ecrecover` for a JOSE ES256K signature. A JOSE ES256K signature is 64 bytes (`r‖s`) with **no** recovery id, so recovery tries recid 0 and 1:

- **`Secp256k1Recovery.TryRecover(ReadOnlySpan<byte> message, ReadOnlySpan<byte> joseSignature, out IReadOnlyList<Secp256k1PublicKey> candidates)`** — hashes `message` with SHA-256 (JOSE ES256K digest), splits `r‖s`, reconstructs `R` from `r` for each recid ∈ {0,1}, and computes `Q = r⁻¹(sR − eG)` via BouncyCastle curve math. Returns the valid candidate points (0, 1, or 2). Never throws; malformed input → empty.
- **`VerifyByAddress(ReadOnlySpan<byte> message, ReadOnlySpan<byte> joseSignature, string expectedAddress)`** — recovers candidates, derives `EthereumAddress.FromPublicKey` for each, and returns true iff any matches `expectedAddress` case-insensitively (both normalised to lowercase hex; the DID may carry lowercase or EIP-55-checksummed). Exposed on `ISecp256k1Verifier` and as a static entry point (mirroring `VerifyEs256k`) for the static `SdJwtService` call site.

Address compare is case-insensitive. keccak256 / EIP-55 (`Keccak256`, `EthereumAddress`) are unchanged from Phase 1 — Phase 2 is their first runtime caller.

### 5.2 DID resolution (offline, address-bearing)

New `VerificationMethod.BlockchainAccountId` property (`[JsonPropertyName("blockchainAccountId")]`, null-omitted).

- **`PkhDidResolver`** resolves `did:pkh:eip155:{chainId}:0x{40hex}`. Validates the `eip155` namespace + 40-hex address shape (offline; reject others → null). Emits a document with a single VM: `id = {did}#blockchainAccountId`, `type = "EcdsaSecp256k1RecoveryMethod2020"`, `controller = {did}`, `blockchainAccountId = "eip155:{chainId}:0x{addr}"`, no `publicKeyJwk`; the VM id is placed in `authentication` **and `assertionMethod`** (so the issuer-key resolver's assertionMethod gate passes).
- **`EthrDidResolver`** resolves the address-form `did:ethr` variants, **default document only** (no registry read). Normalises the network segment (name → chain-id, or hex chain-id → decimal; mainnet default) to build the CAIP-10 `blockchainAccountId = "eip155:{decimalChainId}:0x{addr}"`. Emits the ERC-1056 default document VM: `id = {did}#controller`, `type = "EcdsaSecp256k1RecoveryMethod2020"`, in `authentication` + `assertionMethod`.
- Both are **offline** and register in `AddDidResolvers` alongside the Phase-1 `did:key`/`did:jwk` resolvers. `alsoKnownAs` cross-resolution (a `did:sorcha` org declaring an equivalent `did:pkh`/`did:ethr`) already works — no new work.

### 5.3 Verification wiring (delegation seam — no parallel pipeline)

Both issuer-key resolvers currently require `matched.PublicKeyJwk is not null` and return null otherwise. Phase 2 relaxes that to also accept a VM whose `BlockchainAccountId` is set, and carries the address forward:

- **Verifier engine** (`DidResolverBackedIssuerKeyResolver` → `JsonElement?`): when the matched VM has `BlockchainAccountId` and no `PublicKeyJwk`, synthesize and return a recovery-JWK `JsonElement`: `{ "kty":"EC", "crv":"secp256k1", "blockchainAccountId":"eip155:1:0x…" }`. `VerifyEs256k(JsonElement publicJwk, …)` branches: `x`/`y` present → Phase-1 `Secp256k1Jwk.TryParse` + key-match; else `blockchainAccountId` present → `Secp256k1Verifier.VerifyByAddress(signingInput, signature, address)`.
- **Blueprint service** (`DidX5cIssuerKeyResolver` → `IssuerKeyResolution`): add `string? BlockchainAccountId`. When the matched VM is address-form, return `IssuerKeyResolution { PublicKey = null, BlockchainAccountId = "eip155:…:0x…", Algorithm = "ES256K", SigningKeyId = matched.Id }`. The engine ES256K verify branch (reaching `SdJwtService.Verify`) dispatches address-recovery when `BlockchainAccountId` is set instead of `PublicKey`.
- The `SdJwtVcFormatHandler → TrustEvaluator → resolver-registry` orchestration is **untouched**.

### 5.4 Trust & assurance (no change)

Identical to Phase 1: `DidAllowlistTrustSourceResolver` vouches when the address-form DID is in the requirement's `AllowedIssuers` (Pass); otherwise `TrustPolicy.WarnOnUnlistedVerifiedIssuer` (default false) governs whether a signature-valid-but-unlisted issuer is Warn (reduced assurance) or Reject. A signature/recovery **failure** always rejects.

## 6. Data flow (end-to-end)

1. A blueprint credential requirement accepts an SD-JWT/JWT VC, lists address-form eth issuer DIDs in `AllowedIssuers`, and optionally sets `warnOnUnlistedVerifiedIssuer`.
2. The presented credential is signed `alg:"ES256K"`, `iss: did:pkh:eip155:1:0x…` (or address-form `did:ethr`).
3. The issuer-key resolver resolves the DID → `PkhDidResolver`/`EthrDidResolver` → an `EcdsaSecp256k1RecoveryMethod2020` VM with `blockchainAccountId` → carried forward as a recovery-JWK / `IssuerKeyResolution.BlockchainAccountId`.
4. The ES256K verify branch recovers the public key from the signature, derives its EIP-55 address, and matches the DID's address. Match → `SignatureVerified = true`; mismatch → reject.
5. `TrustEvaluator`: allowlist vouches → assurance; else warn-flag → assurance None + Warn; else reject (unchanged fail-closed).
6. The engine maps assurance → **Pass** (allowlisted) / **Warn** (verified-but-unlisted-with-flag) / **Reject**.

## 7. Error handling

- ES256K signature that recovers **no** address matching the DID → `SignatureVerified = false` → reject (not Warn).
- Malformed `did:pkh` / `did:ethr` (bad namespace, wrong address length, non-hex, non-default-doc `did:ethr` needing a registry read) → resolver returns null → existing null-resolution reject.
- A VM that carries neither `publicKeyJwk` nor `blockchainAccountId` → unresolved → reject (unchanged).
- Recovery / keccak code never throws to the caller — malformed inputs return "no match".

## 8. Testing strategy

- **Primitive KATs:** sign a message with a known secp256k1 private key → `TryRecover` yields the matching public key among candidates for the correct recid → `EthereumAddress.FromPublicKey` equals the known address (anchors recid 0 and recid 1 with two fixtures). `VerifyByAddress` returns true for the correct address (lowercase **and** EIP-55-checksummed input), false for a different address, false for a tampered signature.
- **Resolver tests:** `did:pkh:eip155:1:0x…` and each `did:ethr` address form resolve to an `EcdsaSecp256k1RecoveryMethod2020` VM with the expected `blockchainAccountId` (chain-id normalisation checked for a named network and a hex chain-id); malformed inputs → null.
- **Integration (both verify paths):** a foreign `did:pkh` and an address-form `did:ethr` ES256K JWT/SD-JWT VC yield **Pass** (issuer allowlisted), **Warn** (unlisted + flag), **Reject** (unlisted, flag unset), **Reject** (tampered signature / address mismatch) — through the Verifier engine **and** the Blueprint-service engine resolver.
- **Regression:** every Phase-1 case (key-form `did:key`/`did:jwk` ES256K, ES256/EdDSA) and every pre-existing suite (Secp256k1, ServiceClients, Cryptography, Verifier, Blueprint.Engine) stays green; fail-closed default preserved for an address-form unlisted issuer with the flag unset.

## 9. Seams for later phases (designed-in, not built)

- **Phase 2b (ERC-1056 registry read):** `EthrDidResolver` gains an optional `IEvmRpcClient` seam (provider-backed, SSRF-guarded like `WebDidResolver`, config-driven RPC URL — no node). When present it reads `DIDOwnerChanged`/`DIDDelegateChanged`/`DIDAttributeChanged` events to build the *current* document (rotation, delegates, service endpoints); when absent it returns the **default** document this phase ships. `did:pkh` is inherently keyless/rotation-free, so it needs no registry read ever. The recovery + keccak primitives are reused as-is.
- **Phase 3 (SIWE / signing):** recoverable secp256k1 **sign** added to the primitive; expose the wallet ETH address; reuse the already-derived `m/44'/60'/0'/0/x` key. `WalletNetworks`/signer decision lives here — out of Phase 2.

## 10. Risks & assumptions to verify during planning

1. **BouncyCastle recovery math.** BouncyCastle has no one-call `ecrecover`; recovery is hand-computed (`Q = r⁻¹(sR − eG)`) with an `R`-from-`r` reconstruction per recid. Anchor with a **known** sign→recover→address KAT before wiring any resolver — the plan builds and green-tests the primitive **first**.
2. **Two issuer-key resolvers, two currencies.** The Verifier-engine resolver returns `JsonElement?`; the Blueprint-service resolver returns `IssuerKeyResolution`. Both gate on `PublicKeyJwk is not null` today. The plan must relax **both** and thread the address through each currency (recovery-JWK vs `BlockchainAccountId`), not conflate them.
3. **assertionMethod gate.** Both resolvers reject a VM not present in `assertionMethod` (F120). The new resolvers must place the recovery VM in `assertionMethod`, or address-form issuers reject spuriously — covered by a resolver test asserting the VM id is in `assertionMethod`.
4. **`did:ethr` chain-id normalisation.** Named-network and hex-chain-id forms must normalise to the decimal CAIP-10 `blockchainAccountId`. Recovery ignores chain-id, and allowlist matches the DID string, so the normalisation only affects the emitted `blockchainAccountId`'s cosmetic correctness and the address extraction — a resolver-test concern, not a crypto one.
5. **Warn scoping unchanged.** No `TrustEvaluator` change in Phase 2; the fail-closed regression (address-form unlisted issuer, flag unset → reject) must stay green to prove no bleed.
6. **No signing-path bleed.** Recovery must not become a signing capability; no `WalletNetworks` member added; primitive stays verify/recover-only.

## 11. Out of scope (YAGNI)

EVM RPC client / provider config / node; ERC-1056 registry read / rotation / delegates / service endpoints; holder address-form key-binding; `EthereumEip712Signature2021` / JSON-LD / EAS; secp256k1 signing / recoverable message signing / `WalletNetworks` change; any on-chain write or on-chain PII.
