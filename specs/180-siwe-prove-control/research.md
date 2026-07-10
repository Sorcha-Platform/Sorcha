# Phase 0 Research: SIWE / prove-control (Phase 3)

Confirmed against **EIP-4361** (SIWE message ABNF + example), **ERC-191** (`personal_sign` prefix), and
the existing wallet seed/derivation path. Line numbers indicative.

## R1 — Ethereum ECDSA hashes with keccak, not SHA-256 (the key distinction)

- Phase-1 ES256K (`Secp256k1Recovery.TryRecover`) hashes the message with **SHA-256** (JOSE). Ethereum
  `personal_sign` / SIWE signs the **keccak256** EIP-191 digest **directly** (the 32-byte digest *is* the
  ECDSA input `e`; no second hash).
- **Decision:** add a digest-level recovery core reused by both. Refactor `Secp256k1Recovery` to a private
  `RecoverCore(BigInteger e, r, s) → candidates` (the existing SEC1 §4.1.6 math), with:
  - `TryRecover(message, joseSig)` = `e = SHA256(message)` → `RecoverCore` (unchanged public behaviour;
    Phase-1/2 KATs guard it).
  - **New** `RecoverFromDigest(ReadOnlySpan<byte> digest32, BigInteger r, s, int recId) → Secp256k1PublicKey?`
    for a single recid (used by the signer to pick `v`, and by `SiweVerifier` given `v`).

## R2 — `Secp256k1Signer` (recoverable, RFC-6979, low-s)

- **Decision:** `Secp256k1Signer.SignRecoverable(ReadOnlySpan<byte> digest32, ReadOnlySpan<byte> privateKey) → byte[65]`:
  BouncyCastle `ECDsaSigner(new HMacDsaKCalculator(new Sha256Digest()))` (RFC-6979 deterministic `k`) →
  `(r, s)`; canonicalise low-s (`if s > n/2: s = n - s`); determine `recId ∈ {0,1}` by
  `RecoverFromDigest` matching the signer's own public key; emit `r(32)‖s(32)‖v(1)`, `v = recId + 27`.
- **Rationale:** deterministic `k` removes ECDSA nonce-reuse key-leak risk (a security requirement);
  low-s + `v=recId+27` is the Ethereum ecosystem convention. Signs the digest directly (Ethereum ECDSA),
  never re-hashing. The signer never receives a message it hashes itself for the public API — the wallet
  service computes the EIP-191 / SIWE digest and calls the signer (see R6 guard).

## R3 — `Eip191` personal-sign digest

- **Decision:** `Eip191.PersonalSignDigest(ReadOnlySpan<byte> message) → byte[32]` =
  `Keccak256("\x19Ethereum Signed Message:\n" + AsciiDecimal(message.Length) + message)`.
- Anchored by constructing the same bytes independently with `Keccak256` in a test, plus the SIWE example
  round-trip (R4).

## R4 — SIWE (EIP-4361) message format (verbatim from the spec)

```
[${scheme}://]${domain} wants you to sign in with your Ethereum account:
${address}

[${statement}]

URI: ${uri}
Version: ${version}
Chain ID: ${chain-id}
Nonce: ${nonce}
Issued At: ${issued-at}
[Expiration Time: ${expiration-time}]
[Not Before: ${not-before}]
[Request ID: ${request-id}]
[Resources:
- ${resources[0]}
- ${resources[1]}]
```

- **Required:** domain, address, uri, version, chain-id, nonce, issued-at. **Optional:** scheme,
  statement, expiration-time, not-before, request-id, resources.
- ABNF byte structure: `address LF LF [statement LF] LF "URI: " uri LF …` — i.e. after the address there
  is always a blank line, then the optional statement + a blank line, then `URI:`. When no statement,
  there are two consecutive blank lines. Message ends **without** a trailing newline (after Issued At or
  the last present optional field). `address` is EIP-55 checksummed.
- **Decision:** `SiweFormatter.Format(SiweMessage)` emits exactly this; `TryParse` is its inverse
  (fail-closed on a missing required field / malformed datetime). **Interop anchor:** the EIP-4361 spec
  example message (`example.com` … `0xC02aaA…756Cc2`, with statement + Resources) must `TryParse` and
  re-`Format` byte-identically.

## R5 — `SiweVerifier` (Sorcha as relying party)

- **Decision:** `SiweVerifier.Verify(string message, ReadOnlySpan<byte> signature65, SiweValidationOptions) → SiweVerificationResult`:
  `TryParse` → `Eip191.PersonalSignDigest(message)` → split `r‖s‖v`, `recId = v - 27` → `RecoverFromDigest`
  → `EthereumAddress.FromPublicKey` → **case-insensitive match** to the message's `address` → validate
  `expectedNonce`/`expectedDomain` (when supplied) and `expirationTime`/`notBefore` vs now. Any failure →
  `{ Valid=false, Reason }`. Never throws.
- `SiweValidationOptions`: `ExpectedNonce?`, `ExpectedDomain?`, `NowUtc` (injectable clock for tests).

## R6 — Wallet Ethereum identity (auxiliary; reuse seed path; no WalletNetworks change)

- The wallet's seed path (from `WalletManager`): direct-master wallets carry `EncryptedMasterKeyBlob`;
  `IKeyManagementService.DecryptPrivateKeyAsync(blob, keyId)` → BIP39 seed; `DeriveKeyAtPathAsync(seed,
  path, algorithm)` currently re-derives into the **primary** algorithm and discards the secp256k1 key.
- **Decision:** add `IKeyManagementService.DeriveSecp256k1KeyAtPathAsync(byte[] seed, DerivationPath) →
  (byte[] PrivateKey32, byte[] PublicKey65)` — NBitcoin `ExtKey.CreateFromSeed(seed).Derive(path)` →
  `derived.PrivateKey.ToBytes()` (the raw secp256k1 scalar) + the uncompressed SEC1 public key. **No
  `GenerateKeySet`, no `AlgorithmMapper`, no `WalletNetworks`.**
- **`EthereumIdentityService`** (Wallet.Core): given a walletId, load the wallet, decrypt the seed (reuse
  the direct-master path), derive at `m/44'/60'/0'/0/{index}` (default index 0), and:
  - `GetAddressAsync` → `EthereumAddress.FromPublicKey(...)`.
  - `SignPersonalMessageAsync(message)` / `SignSiweAsync(SiweMessage)` → `Eip191` digest → `Secp256k1Signer`.
  - **Guard (FR-007):** refuse a message whose raw bytes decode as an RLP transaction (leading byte in the
    RLP-list range `0xc0..0xff` with a plausible 6–9 element structure, or a typed-tx envelope `0x01..0x7f`
    with an RLP body). EIP-191-prefixed prove-control text is inherently non-RLP, so this is defence-in-depth.
    No public method signs an arbitrary raw 32-byte digest; the key is never returned.

## R7 — No new dependency; WASM-safe

- BouncyCastle (RFC-6979 `HMacDsaKCalculator`, keccak) and NBitcoin (BIP32) are already referenced. The
  primitive stays pure-managed → the Blazor WASM PWA wallet can sign on-device (FR-010).

## R8 — Interop / KAT strategy

- **Signer KAT:** `SignRecoverable` → `RecoverFromDigest` (both recids) → the recovered key equals the
  signer's key, and its EIP-55 address equals the known address (self-consistent, proves Ethereum-compat
  since recovery is the ecosystem-standard verify). Low-s asserted (`s ≤ n/2`); `v ∈ {27,28}`.
- **EIP-191 KAT:** `PersonalSignDigest("abc")` equals an independently-constructed
  `Keccak256("\x19Ethereum Signed Message:\n3abc")`.
- **SIWE interop:** the EIP-4361 spec example message round-trips (parse → format identical); a full
  sign→`SiweVerifier` cycle accepts; tamper/expiry/wrong-address/wrong-nonce reject.
- **(Optional) published triple:** a well-known `(address, message, signature)` personal-sign vector
  verified during implementation to anchor cross-implementation compatibility.

## Open items deliberately NOT resolved (out of scope)

Transactions / RLP encode / EIP-155 / Nethereum / on-chain writes (Phase 4); EIP-712 typed-data; key
export; arbitrary raw-digest signing via the public API; UI; any `WalletNetworks` / primary-algorithm
change.
