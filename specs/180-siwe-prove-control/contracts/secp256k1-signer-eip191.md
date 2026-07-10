# Contract: `Secp256k1Signer` + `Eip191` + `RecoverFromDigest`

**Project**: `src/Common/Sorcha.Cryptography.Secp256k1` (pure-managed, BouncyCastle only, WASM-safe).

## `Secp256k1Signer.SignRecoverable(digest32, privateKey32) → byte[65]`

| Input / condition | Behaviour |
|---|---|
| valid 32-byte digest + 32-byte private key | RFC-6979 deterministic `k`; low-s (`s ≤ n/2`); returns `r(32)‖s(32)‖v(1)`, `v = recId + 27` (`v ∈ {27,28}`) |
| `digest` ≠ 32 bytes or `privateKey` ≠ 32 bytes | throws `ArgumentException` (caller error) |

**Guarantees**: deterministic (same digest+key → same signature); the recovered public key from the
output equals the signer's; no RNG; signs the digest directly (no re-hash).

## `Eip191.PersonalSignDigest(message) → byte[32]`

`keccak256("\x19Ethereum Signed Message:\n" + AsciiDecimal(message.Length) + message)`.

## `Secp256k1Recovery.RecoverFromDigest(digest32, r, s, recId) → Secp256k1PublicKey?`

Single-recid SEC1 §4.1.6 recovery from a raw digest (Ethereum). Returns null on an invalid/off-curve
candidate. Shares a private `RecoverCore` with the existing `TryRecover(message, joseSig)` (which is
unchanged: SHA-256 then recover, for ES256K).

## Test contract

- **Signer round-trip:** for many keys, `SignRecoverable(digest, priv)` → `RecoverFromDigest(digest, r, s, v-27)` equals the signer's public key, and its EIP-55 address equals `EthereumAddress.FromPublicKey`.
- **Low-s:** `s ≤ n/2` for every signature; `v ∈ {27,28}`.
- **Deterministic:** same `(digest, key)` → identical 65-byte output.
- **EIP-191:** `PersonalSignDigest("abc")` == independently-built `Keccak256("\x19Ethereum Signed Message:\n3abc")`.
- **Regression:** existing ES256K `TryRecover` KATs (Phase 1/2) stay green after the `RecoverCore` refactor.
