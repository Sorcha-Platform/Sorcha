# Ethereum-key VC Verification — Phase 1 (verify-only, secp256k1 / ES256K)

- **Date:** 2026-07-09
- **Status:** Design approved (brainstorm), pending implementation plan
- **Scope:** Phase 1 of a 4-phase Ethereum interoperability roadmap
- **Related:** DID resolution architecture (`Sorcha.ServiceClients.Did`), F120 DID-backed issuer keys, F135 unified trust (`ITrustEvaluator`), F155 four-layer verification / reduced-assurance precedent

---

## 1. Context & motivation

A pivotal question for Sorcha's adoption and interoperation with established systems is whether it can participate in Ethereum-based identity and Verifiable Credential (VC) ecosystems. A wide research pass (internal code mapping + external standards survey) established:

- Sorcha's DID layer is **already method-pluggable** — an `IDidResolver` registry that today hosts `did:sorcha`, `did:web`, and `did:key` (two of which are external methods Sorcha does not issue). Adding a method is one interface + one registration.
- Sorcha's VC pipeline is **already format/trust-pluggable** — `ICredentialFormatHandler`, `ITrustSourceResolver`, and `ITrustEvaluator` are enum-dispatched extension seams.
- Sorcha's HD wallet **already derives genuine secp256k1 keys** via NBitcoin (BIP32), and BouncyCastle (with secp256k1 + Keccak) is already referenced. The gap is a thin crypto/resolution adapter, **not** a new dependency.
- The interoperable Ethereum DID methods are **`did:ethr`** (ERC-1056) and **`did:pkh`** (CAIP-10) — plus **`did:key`/`did:jwk`** with a secp256k1 key. There is no registered `did:eth`; a bespoke method would be non-interoperable and is explicitly rejected.
- **GDPR / CNIL / EDPB guidance and Sorcha's DAD model agree:** personal data stays off-chain. Nothing in this roadmap puts PII on-chain.

The full roadmap is four phases, all of which are intended to be built eventually:

| Phase | Capability | Cost/risk |
|---|---|---|
| **1 (this spec)** | **Verify** secp256k1/ES256K-signed VCs where the DID resolves offline to a key | Low — no node, no new dep |
| 2 | `did:ethr` / `did:pkh` (address-form) resolution via read-only EVM RPC + `ecrecover` | Adds an RPC client seam |
| 3 | Prove-control / SIWE — expose the wallet's ETH address + secp256k1 message signing | Adds recoverable signing |
| 4 | Transacting — payments / contract writes via Nethereum | Gas, funded accounts, custody-of-value |

This spec covers **Phase 1 only**, but designs its seams so Phases 2–4 slot in without a rewrite.

## 2. Goal & non-goals

**Goal.** Sorcha can verify a W3C VC / SD-JWT VC signed with a secp256k1 key (JOSE **ES256K**) — in **both** the issuer-signature and holder-key-binding positions — where the issuer/holder DID resolves **offline** to a secp256k1 public key. The credential flows through the **existing** verification pipeline (format handler → trust evaluator → resolver registry); the only new crypto is an isolated primitive. Works identically for a Web3-SSI credential (Veramo-style JWT-VC) and an EU/EUDI-style `did:jwk` credential — shared VC plumbing.

**Non-goals (deferred to later phases or explicitly excluded):**

- No `ecrecover`; no `did:pkh`; no address-form `did:ethr` (these need an EVM RPC read and/or public-key recovery) → **Phase 2**.
- No `EthereumEip712Signature2021` / JSON-LD Data Integrity proofs; no EAS attestations (different data model / no JSON-LD path in Sorcha today) → separate, later.
- No secp256k1 **signing**; no recoverable signatures; **no change to the `WalletNetworks` signing enum** → **Phase 3**.
- No EVM JSON-RPC client; no self-hosted or containerised Ethereum node.
- No on-chain writes; no personal data on-chain (ever).

## 3. Confirmed design decisions

1. **Proof scope:** ES256K JWT-VC / SD-JWT, **key-match only** (the DID resolves directly to a secp256k1 public key). No `ecrecover`, no address-only DIDs.
2. **Verification positions:** **both** the issuer JWS **and** the holder key-binding (SD-JWT `cnf.jwk` carrying a secp256k1 key + the ES256K KB-JWT / OID4VP holder proof). Same primitive, two call sites.
3. **Issuer trust:** allowlisted eth issuer → full trust via the existing `DidAllowlistTrustSourceResolver` (assurance from config); **signature-valid-but-unlisted → `VerifyOutcome.Warn`** (reduced assurance), **not** rejected — gated by an explicit, blueprint-authored per-requirement flag so the fail-closed default is preserved everywhere else. Holder-binding needs no trust source (pure cryptographic control-proof).
4. **Architecture (Approach C):** an **isolated primitive** owning only secp256k1-verify + keccak256 + secp256k1-JWK, with the **existing pipeline reused** via a delegation seam (no parallel verifier).
5. **Primitive location:** a new **`Secp256k1/` folder inside `Sorcha.Cryptography`** (new namespace) — **not** a separate project and **not** a new dependency (BouncyCastle is already referenced there; `SdJwtService` already lives there). It stays off the `WalletNetworks` signing hot path by being a distinct namespace that adds no signing-enum member.
6. **keccak256 included now** as foundation (address derivation), unit-tested against a known vector, even though Phase 1 verification itself uses SHA-256. It unblocks Phases 2–4 for ~20 lines.
7. **`did:jwk` resolver** added as a general (all-curve) resolver — needed for the secp256k1-JWK-in-DID case and a EUDI/OpenID4VC interop win.

## 4. Where it fits in the codebase (project map)

Phase 1 spans **four** projects. The crypto is in `Sorcha.Cryptography`; the resolvers, issuer-key parsing, and trust model live in their existing homes and are *reused*, not forked.

| Project | Change |
|---|---|
| **`src/Common/Sorcha.Cryptography`** | **New** `Secp256k1/` primitive folder (verify + keccak + JWK). **Edit** `SdJwt/SdJwtService.cs` to add the ES256K verify branch (issuer JWS + KB-JWT), delegating to the primitive. |
| **`src/Common/Sorcha.ServiceClients.Http`** | **Edit** `Did/KeyDidResolver.cs` (add secp256k1 multicodec `0xe701`). **New** `Did/JwkDidResolver.cs` (`did:jwk`, all curves). **Edit** `Extensions/HttpServiceCollectionExtensions.cs` `AddDidResolvers` to register the `did:jwk` resolver. |
| **`src/Services/Sorcha.Blueprint.Service` + `src/Common/Sorcha.Verifier.Engine`** | **Edit** the issuer-key JWK parse to add a `crv:"secp256k1"` branch delegating to the primitive (`Sorcha.Blueprint.Service/Credentials/DidX5cIssuerKeyResolver.cs::ExtractPublicKeyFromJwk`; and the Verifier engine's JWS-verify / JWK handling). |
| **`src/Common/Sorcha.Blueprint.Models` + `src/Core/Sorcha.Blueprint.Engine`** | **Edit** the credential-requirement model to add `warnOnUnlistedVerifiedIssuer`. **Edit** `Sorcha.Blueprint.Engine/Credentials/TrustEvaluator.cs` for the scoped Warn-fallback. **Edit** the Verifier engine's outcome mapping (`VerifyOutcome.Warn`). |

> Line numbers are omitted deliberately — the implementation plan re-locates exact insertion points, as code drifts.

## 5. Architecture

### 5.1 The primitive — `Sorcha.Cryptography.Secp256k1`

A focused, independently testable namespace. Depends only on **BouncyCastle** (already referenced). No reference to `Sorcha.Wallet.*` or `WalletNetworks`.

- **`ISecp256k1Verifier`** / `Secp256k1Verifier`
  - `bool Verify(ReadOnlySpan<byte> message, ReadOnlySpan<byte> joseSignature, Secp256k1PublicKey key)` — JOSE **ES256K**: ECDSA over **SHA-256** with a 64-byte fixed-width `r‖s` signature. Accepts high-or-low-s on verify (low-s is a *produce*-side constraint, Phase 3).
- **`Secp256k1PublicKey`** + JWK codec
  - Parse a JOSE EC JWK (`kty:"EC"`, `crv:"secp256k1"`, base64url `x`/`y`) → curve point.
  - Decompress a 33-byte compressed point (needed by `did:key` `0xe701` and `did:jwk`) → `x`/`y`.
  - Build a JWK from a public key (for symmetry / DID-document emission).
- **`Keccak256`** + **`EthereumAddress`** *(foundation, no Phase 1 runtime caller)*
  - `byte[] Keccak256(ReadOnlySpan<byte>)` via BouncyCastle `KeccakDigest(256)`.
  - `EthereumAddress.FromPublicKey(...)` → `keccak256(uncompressed64)[12:]` → `0x` + EIP-55 checksum.
  - Unit-tested against a known private-key → address vector; not wired into any verification path.

### 5.2 DID resolution (offline, key-bearing)

- **`KeyDidResolver`** gains the secp256k1 multicodec `0xe701`: decode the compressed point, build a `publicKeyJwk` verification method (`crv:"secp256k1"`) via the primitive. Small edit alongside the existing `0xed01`/`0x1200` handling.
- **`JwkDidResolver`** (new) resolves `did:jwk:<base64url(JWK)>` by decoding the embedded JWK into a single verification method. Curve-agnostic (P-256 / Ed25519 / secp256k1), so it is a general interop addition; registered in `AddDidResolvers`.
- Both are **offline**. Address-form `did:ethr` / `did:pkh` are **Phase 2** (require RPC / recovery).

### 5.3 Verification wiring (delegation seam — no parallel pipeline)

- **`SdJwtService`** JWS-verify and KB-JWT-verify gain an `alg:"ES256K"` branch that resolves the secp256k1 JWK and **delegates to `ISecp256k1Verifier`**. This single addition covers the issuer signature *and* the holder key-binding (`cnf.jwk` secp256k1 + ES256K KB-JWT).
- **Issuer-key resolvers** (`DidX5cIssuerKeyResolver.ExtractPublicKeyFromJwk`; Verifier-engine equivalent) gain a `crv:"secp256k1"` branch delegating to the primitive's JWK parse.
- **`SdJwtVcFormatHandler` → `TrustEvaluator` → resolver registry** orchestration is **untouched**.

### 5.4 Trust & assurance (the one guarded behavioural change)

- **Allowlisted** issuer: `DidAllowlistTrustSourceResolver` vouches (issuer DID in the requirement's `AllowedIssuers`) → assurance from config. No change to the resolver.
- **Signature-valid but unlisted:** the current `TrustEvaluator` fail-closes when no source vouches. Phase 1 adds a **scoped, opt-in** path: when the requirement sets `warnOnUnlistedVerifiedIssuer = true` **and** the signature verified **and** no source vouched, produce an outcome with assurance `None` + a diagnostic, mapped by the Verifier engine to **`VerifyOutcome.Warn`** (mirrors F155 `LayerStatus.Unverified` / F114 offline reduced-assurance). The flag **defaults false**, so fail-closed is preserved for every non-eth and unconfigured credential.
- **Invariant:** a signature *failure* always rejects. Warn is only for *verified-but-untrusted*. The flag is per-credential-requirement (blueprint-authored), so reduced assurance is always an explicit, auditable choice at a specific gate — never a global loosening.

## 6. Data flow (end-to-end)

1. A blueprint credential requirement accepts an SD-JWT VC, lists eth issuer DIDs in `AllowedIssuers`, and optionally sets `warnOnUnlistedVerifiedIssuer`.
2. The presented credential is an SD-JWT with `alg:"ES256K"`, `iss: did:key:zQ3s…` (or `did:jwk:…`) secp256k1, an optional secp256k1 `cnf.jwk`, and an ES256K KB-JWT.
3. `SdJwtVcFormatHandler.VerifyAsync` resolves the issuer key: issuer-key resolver → `IDidResolverRegistry` → `did:key`/`did:jwk` resolver → secp256k1 JWK.
4. `SdJwtService` verifies the issuer JWS and the holder KB-JWT with `ISecp256k1Verifier`; sets `IssuerContext.SignatureVerified`.
5. `TrustEvaluator`: allowlist vouches → assurance; else if the warn flag is set → assurance `None` + Warn; else reject (unchanged fail-closed).
6. The outcome carries `TrustEvidence`; the engine maps assurance → **Pass** (allowlisted) / **Warn** (verified-but-unlisted-with-flag) / **Reject**.

## 7. Error handling

- Unknown/mismatched `alg`, or a JWK that is malformed / not on the secp256k1 curve → verify fails closed (`SignatureVerified = false` → reject). Not a Warn.
- `did:key` with an unrecognised multicodec, or a malformed `did:jwk` → resolver returns null → existing null-resolution handling (reject).
- keccak / address code has no Phase 1 runtime surface, so it introduces no new runtime error path.

## 8. Testing strategy

- **Primitive unit tests:** ES256K verify against a known JOSE ES256K test vector (positive + tampered-signature negative); secp256k1 JWK parse/round-trip incl. compressed-point decompression; keccak256 + EIP-55 address from a known private-key → address vector.
- **Resolver tests:** `did:key`(secp256k1, `0xe701`) and `did:jwk`(secp256k1) resolve to the expected `publicKeyJwk` verification method; `did:jwk` also resolves a P-256 and an Ed25519 key (all-curve).
- **Integration (format handler):** a Veramo-style ES256K SD-JWT/JWT-VC yields **Accepted** (issuer allowlisted), **Warn** (unlisted + flag set), **Reject** (unlisted, flag unset), **Reject** (tampered signature). Plus an ES256K holder KB-JWT binding verify.
- **Regression:** existing ES256 / EdDSA SD-JWT and mdoc verification unchanged; fail-closed default preserved for a non-eth unlisted issuer (flag unset).
- **Cross-world parity:** one Web3 fixture (Veramo-style ES256K JWT-VC with a `did:key` secp256k1 issuer) and one EU/EUDI-style fixture (`did:jwk` secp256k1) exercise the **same** verification path. (`did:ethr` issuers arrive in Phase 2 — Phase 1 ships only the offline `did:key` + `did:jwk` resolvers.)

## 9. Seams for later phases (designed-in, not built)

- **Phase 2 (`did:ethr`/`did:pkh`):** new `EthrDidResolver` / `PkhDidResolver : IDidResolver` + an `IEvmRpcClient` seam (provider-backed, SSRF-guarded like `WebDidResolver`, config-driven RPC URL — no node). `ecrecover` and ERC-1056 registry-event decode reuse this unit's keccak/JWK primitives.
- **Phase 3 (SIWE / signing):** add recoverable secp256k1 **sign** to the primitive; expose the ETH address; reuse the already-derived `m/44'/60'/0'/0/x` key (stop discarding the secp256k1 key at `KeyManagementService.DeriveKeyAtPathAsync`). This is where a `WalletNetworks`/signer decision is made — deliberately out of Phase 1.
- **Phase 4 (transacting):** add **Nethereum** for RLP / EIP-155 / ABI, fed by the Phase 3 signer.

## 10. Risks & assumptions to verify during planning

1. **SD-JWT library seam.** `Sorcha.Cryptography` uses `HeroSD-JWT`. Confirm whether ES256K can be injected into its verify path or whether `SdJwtService` performs the JWS algorithm dispatch itself (agent evidence indicates Sorcha maps `alg` → verifier internally at `SdJwtService.cs`, which is the intended insertion point). If HeroSD-JWT owns verification and can't accept ES256K, the plan adds a thin pre-verify JWS step.
2. **Two `IIssuerKeyResolver` interfaces.** There are two same-named interfaces (Verifier engine returns a JWK `JsonElement`; Blueprint engine returns an `IssuerKeyResolution`). Both need the secp256k1 JWK branch; the plan must touch both, not conflate them.
3. **Warn-fallback scoping.** The change to `TrustEvaluator` must be provably inert unless `warnOnUnlistedVerifiedIssuer` is set — covered by the regression test asserting a non-eth unlisted issuer still fails closed.
4. **No signing-path bleed.** The primitive must not be referenced from any wallet signing path, and no `WalletNetworks` member is added in Phase 1 (guarded by keeping the primitive in its own namespace with no signing API).
5. **did:key compressed-point handling.** secp256k1 `0xe701` prefixes a 33-byte compressed point; the JWK build must decompress correctly (BouncyCastle `Curve.DecodePoint`).

## 11. Out of scope (YAGNI)

`ecrecover`, `did:pkh`, address-form `did:ethr`; EIP-712 JSON-LD / EAS; secp256k1 signing / recoverable signatures / `WalletNetworks` change; EVM RPC client / node; any on-chain write or on-chain PII.
