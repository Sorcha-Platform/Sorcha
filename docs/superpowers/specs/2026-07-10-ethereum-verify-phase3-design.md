# Ethereum Interop — Phase 3 (SIWE / prove-control: secp256k1 signing)

- **Date:** 2026-07-10
- **Status:** Design approved (brainstorm), pending implementation plan
- **Scope:** Phase 3 of the 4-phase Ethereum interoperability roadmap (Feature 180)
- **Builds on:** Phase 1 (F177 — `Sorcha.Cryptography.Secp256k1` primitive, ES256K verify), Phase 2 (F178 — `ecrecover`, keccak/EIP-55, `EthereumAddress`), Phase 2b (F179 — `did:ethr` on-chain resolution)
- **Related:** Wallet key management (`Sorcha.Wallet.Core`, `KeyManagementService`, NBitcoin HD derivation), `HolderKeyService`

---

## 1. Context & motivation

Phases 1–2b made Sorcha a **verifier** of Ethereum-signed credentials and DIDs. Phase 3 is the first
phase where Sorcha **signs** with a secp256k1 key — **prove-control**: a Sorcha wallet can prove it
controls an Ethereum address by signing a challenge (EIP-191 `personal_sign`) or a **Sign-In With
Ethereum** (EIP-4361 / SIWE) message, and Sorcha (as a relying party) can **verify** an inbound SIWE
from an external wallet.

The key already exists but is thrown away: `KeyManagementService.DeriveKeyAtPathAsync` uses NBitcoin to
derive a genuine secp256k1 key at the BIP32 path, then feeds those bytes as a *seed* into the wallet's
primary algorithm (Ed25519 / P-256 / RSA) and discards the secp256k1 key as a signing key. Phase 3 keeps
that key — as an **auxiliary Ethereum identity** derived at `m/44'/60'/0'/0/{index}` — without changing
the wallet's primary algorithm.

This is the roadmap's most security-sensitive phase (it adds an outward **signing** capability), so it
is scoped narrowly to **prove-control messages only** — never transactions (Phase 4).

Roadmap:

| Phase | Capability | Status |
|---|---|---|
| 1 (F177) | Verify ES256K VCs where the DID resolves offline to a key | Shipped (#1140) |
| 2 (F178) | `did:pkh` + address-form `did:ethr` via offline `ecrecover` | Shipped (#1141) |
| 2b (F179) | `did:ethr` current document via read-only EVM RPC | Shipped (#1143) |
| **3 (this spec)** | **SIWE / prove-control — expose the ETH address + secp256k1 message signing** | This spec (F180) |
| 4 | Transacting (payments / contract writes) via Nethereum | Deferred |

## 2. Goal & non-goals

**Goal.** A Sorcha wallet can (a) expose its **Ethereum address** (EIP-55) derived from its existing HD
seed, (b) **sign** an EIP-191 `personal_sign` challenge and a SIWE (EIP-4361) prove-control message with
a recoverable secp256k1 signature, and (c) Sorcha can **verify** an inbound SIWE message + signature
(Sorcha as relying party), reusing the Phase-1/2 recovery primitives. The ETH key is an **auxiliary
identity** — the wallet's primary `WalletNetworks` algorithm is unchanged.

**Non-goals (deferred or excluded):**

- **No transactions / RLP / EIP-155 / Nethereum / on-chain writes** → Phase 4. The signer explicitly
  refuses transaction-shaped payloads.
- **No `WalletNetworks` enum member**; no change to `AlgorithmMapper` / `GenerateKeySet` / the
  primary-algorithm model. The ETH key is derived directly via NBitcoin, orthogonally.
- **No EIP-712 typed-data signing** beyond SIWE (a possible later add).
- **No UI** — thin API surface only.
- **No key export** — the ETH private key is never returned; it is derived on-demand, used, discarded.
- **No new third-party dependency** (NBitcoin + BouncyCastle already referenced).

## 3. Confirmed design decisions

1. **Auxiliary Ethereum identity.** Derive the secp256k1 key at `m/44'/60'/0'/0/{index}` from the
   wallet's existing encrypted master seed; keep it as a secp256k1 signing key (stop discarding). **No
   `WalletNetworks` change** — orthogonal to the primary algorithm.
2. **Produce + verify SIWE.** The wallet produces a signed EIP-191 / SIWE prove-control message; Sorcha
   verifies an inbound SIWE (relying-party dual, reusing `Secp256k1Recovery` + EIP-191 digest).
3. **Signer lives in the primitive.** `Secp256k1Signer` + `Eip191` join the pure-managed
   `Sorcha.Cryptography.Secp256k1` project (BouncyCastle only, WASM-safe) — the PWA *is* a wallet and
   must sign in WASM. The security boundary is key custody, not method presence.
4. **RFC-6979 deterministic `k` + low-s canonical.** No RNG nonce; `s ≤ n/2`; 65-byte `r‖s‖v`,
   `v = recoveryId + 27` (EIP-191 convention).
5. **Prove-control only — transaction guard.** The signer/service refuses to sign anything that decodes
   as an RLP transaction; the ETH key is used solely for EIP-191/SIWE prove-control this phase.
6. **Same custody as existing wallet signing.** Derive-on-demand from the encrypted seed, sign, discard;
   signing needs the same authorization as any wallet operation. The private key is never returned.
7. **SIWE core is pure-managed.** `SiweMessage` model + formatter + parser + `SiweVerifier` in the
   primitive project (WASM-safe, reusable by any service as relying party).

## 4. Where it fits in the codebase (project map)

| Project | Change |
|---|---|
| **`src/Common/Sorcha.Cryptography.Secp256k1`** | **New** `Secp256k1Signer` (recoverable RFC-6979 low-s sign → 65-byte `r‖s‖v`), `Eip191` (personal_sign digest), `Siwe/SiweMessage` + `Siwe/SiweFormatter` (EIP-4361 build/parse) + `Siwe/SiweVerifier` (parse → recover → address-match → nonce/domain/expiry). Pure-managed. |
| **`src/Core/Sorcha.Wallet.Core`** (+ `Sorcha.Wallet.Service`) | **New** `IEthereumIdentityService` / impl: derive `m/44'/60'/0'/0/{index}` secp256k1 key from the wallet's master seed (reuse the NBitcoin path in `KeyManagementService`, minus the discard), expose the EIP-55 address, sign EIP-191 / SIWE. Transaction-payload guard. |
| **`src/Services/Sorcha.Wallet.Service/Endpoints`** | **New** thin endpoints: `GET ethereum-address`, `POST siwe/sign` (produce), `POST siwe/verify` (Sorcha-as-RP). Same auth as existing wallet endpoints. |
| **Docs** | STANDARDS.md (EIP-191, EIP-4361 rows), `cryptography` skill, memory. |

> Verify seams, trust, DID resolution, and the wallet's primary-algorithm model are **untouched**.

## 5. Architecture

### 5.1 Primitive signing

- **`Secp256k1Signer.SignRecoverable(ReadOnlySpan<byte> digest32, ReadOnlySpan<byte> privateKey) → byte[65]`** —
  BouncyCastle `ECDsaSigner` with `HMacDsaKCalculator(Sha256Digest)` (RFC-6979); canonicalise to low-s
  (`if s > n/2: s = n - s; recId ^= 1`); derive `recId` by recovering and matching the public key; emit
  `r(32)‖s(32)‖v(1)`, `v = recId + 27`. Never signs without a valid 32-byte private key.
- **`Eip191.PersonalSignDigest(ReadOnlySpan<byte> message) → byte[32]`** —
  `keccak256("\x19Ethereum Signed Message:\n" + message.Length + message)`.
- Existing `Secp256k1Recovery`/`EthereumAddress` verify the dual.

### 5.2 SIWE (EIP-4361)

- **`SiweMessage`** — the fields (domain, address, statement?, uri, version, chainId, nonce, issuedAt,
  expirationTime?, notBefore?, requestId?, resources?).
- **`SiweFormatter.Format(SiweMessage) → string`** and **`SiweFormatter.TryParse(string, out SiweMessage)`** —
  the EIP-4361 ABNF message text.
- **`SiweVerifier.Verify(string message, byte[65] signature, SiweValidationOptions) → SiweVerificationResult`** —
  parse → `Eip191.PersonalSignDigest` → `Secp256k1Recovery` → recovered address == message `address`
  (case-insensitive) → validate `nonce` (caller-supplied expected), `domain`, `expirationTime`/`notBefore`
  vs now. Fail-closed on any mismatch.

### 5.3 Wallet Ethereum identity

- **`IEthereumIdentityService`** (Wallet.Core), impl in Wallet.Service:
  - `GetAddressAsync(walletId, index=0)` → derive `m/44'/60'/0'/0/{index}` secp256k1 pubkey → EIP-55 address.
  - `SignPersonalMessageAsync(walletId, message, index=0)` → EIP-191 digest → `Secp256k1Signer` → 65-byte sig.
  - `SignSiweAsync(walletId, SiweMessage, index=0)` → format → sign → `{ message, signature }`.
  - Reuses the wallet's master-seed decryption (same as existing signing); derives via NBitcoin exactly
    like `KeyManagementService.DeriveKeyAtPathAsync` but **returns/uses the secp256k1 key directly**
    (no `GenerateKeySet` re-derivation). Guards: refuse a message that decodes as an RLP transaction.

### 5.4 Endpoints (Wallet Service, thin)

- `GET /api/v1/wallets/{walletId}/ethereum-address` → `{ address }`.
- `POST /api/v1/wallets/{walletId}/siwe/sign` `{ domain, uri, statement?, chainId, nonce, … }` → `{ message, signature, address }`.
- `POST /api/v1/siwe/verify` `{ message, signature, expectedNonce?, expectedDomain? }` → `{ valid, address, reason? }`.
- Same authorization as existing wallet operations; `.WithSummary`/`.WithDescription` for OpenAPI.

## 6. Data flow

**Produce (wallet proves control):** client requests a SIWE sign with a challenge (domain/uri/nonce) →
service derives the ETH key from the seed → formats the SIWE text → EIP-191 digest → recoverable sign →
returns `{ message, signature, address }`. The relying party recovers the address and checks the nonce.

**Verify (Sorcha as RP):** an external wallet submits `{ message, signature }` → `SiweVerifier` parses,
recovers the signer address, matches it to the message's `address`, validates nonce/domain/expiry →
`{ valid, address }`.

## 7. Security & error handling

- **Custody:** ETH key derived on-demand from the encrypted master seed, used, discarded; never returned;
  signing gated by the same auth as any wallet operation.
- **Nonce reuse:** RFC-6979 deterministic `k` eliminates ECDSA nonce-reuse key leakage.
- **Transaction guard:** the signer/service refuses a payload that decodes as an RLP transaction (a
  defence-in-depth boundary so the prove-control key can't be coaxed into signing a transfer this phase).
- **SIWE verify is fail-closed:** bad signature, address mismatch, expired / not-yet-valid, wrong
  domain/nonce → `valid=false`. Malformed message → `valid=false` (never throw to the endpoint).
- **No key export / no raw-hash signing** of arbitrary 32-byte digests via the public API (only EIP-191
  / SIWE), so the key cannot be used to sign a transaction digest.

## 8. Testing strategy

- **Primitive:** sign→`Secp256k1Recovery`→address round-trip; low-s canonical (`s ≤ n/2`); `v` recovers
  the right key; `Eip191.PersonalSignDigest` against a known `personal_sign` vector; a **published
  personal_sign / SIWE signature test vector** verifies (interop anchor).
- **SIWE:** format↔parse round-trip (all optional fields); `SiweVerifier` accepts a valid message and
  rejects bad-signature / wrong-address / expired / not-before / wrong-domain / wrong-nonce / malformed.
- **Wallet:** deterministic address from a known seed/path; `SignSiweAsync` output verifies via
  `SiweVerifier`; transaction-payload guard rejects; the wallet's primary-algorithm signing is unchanged.
- **Endpoints:** address / sign / verify happy-path + auth; verify rejects tampered input.
- **Regression:** all Phase 1/2/2b suites green; verify-only paths and `WalletNetworks` unchanged.

## 9. Seams for later phases (designed-in, not built)

- **Phase 4 (transacting):** add Nethereum (RLP / EIP-155 / ABI encode) fed by `Secp256k1Signer`; the
  transaction guard here is removed deliberately and replaced by explicit, gated transaction signing
  with funded-account/custody controls. `Secp256k1Signer` is the shared signer.
- **EIP-712 typed-data** prove-control (structured-data sign) can extend `Eip191`/SIWE with an EIP-712
  digest without touching the key path.

## 10. Risks & assumptions to verify during planning

1. **RFC-6979 + low-s + recovery-id exactness.** Anchor with a published `personal_sign`/SIWE vector
   (known key → known signature) before wiring the wallet service.
2. **Seed access seam.** Confirm the exact `KeyManagementService` / wallet master-seed decryption path to
   reuse for on-demand derivation without duplicating custody logic or weakening it.
3. **Transaction-guard definition.** Define "looks like an RLP transaction" precisely (leading RLP list
   byte + field shape) so prove-control messages are never misclassified and transactions are always
   caught. Prove-control text (EIP-191 prefixed) is inherently non-RLP, so the guard is belt-and-braces.
4. **SIWE ABNF conformance.** The EIP-4361 message grammar (field order, optional lines, datetime format)
   must round-trip and interop; test against the reference message examples.
5. **No `WalletNetworks` bleed.** A test asserts the primary-algorithm signing/derivation is unchanged
   and no new enum member is introduced.

## 11. Out of scope (YAGNI)

Transacting / RLP / EIP-155 / Nethereum / on-chain writes; EIP-712 beyond SIWE; key export; arbitrary
32-byte digest signing via the public API; UI; using the ETH key for anything but prove-control; any
`WalletNetworks` / primary-algorithm change; gas / funded accounts / custody-of-value.
