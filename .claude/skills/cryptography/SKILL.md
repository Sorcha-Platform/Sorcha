---
name: cryptography
description: |
  Applies multi-algorithm cryptography (ED25519, P-256, RSA-4096) using Sorcha.Cryptography.
  Use when: implementing signing, verification, encryption, key generation, HD wallets, or address encoding.
allowed-tools: Read, Edit, Write, Glob, Grep, Bash, mcp__context7__resolve-library-id, mcp__context7__query-docs
---

# Cryptography Skill

Sorcha.Cryptography provides multi-algorithm support (ED25519, P-256, RSA-4096), symmetric encryption (AES, ChaCha20), and BIP39/44 HD wallet derivation. All operations return `CryptoResult<T>` for explicit error handling—no exceptions for crypto failures.

**secp256k1 / ES256K lives in a SEPARATE project — `Sorcha.Cryptography.Secp256k1` (Feature 177, verify-only).** Pure-managed (BouncyCastle only): `ISecp256k1Verifier` (JOSE **ES256K**), `Secp256k1Jwk` (parse/build/decompress), `Keccak256`, `EthereumAddress` (EIP-55). It is deliberately **NOT** folded into `Sorcha.Cryptography` — `Sorcha.Verifier.Engine` is consumed by the Blazor **WASM** PWA, so it must stay native-dependency-free and cannot reference `Sorcha.Cryptography` (native `Sodium`/`Mcl`); both verification paths need secp256k1. And **.NET's built-in `ECDsa` does NOT reliably support secp256k1 on Windows/WASM** — BouncyCastle is required. Verify-only: no signing, no new `WalletNetworks` member (recoverable *signing* is Phase 3). Consumed by `SdJwtService.Verify` (`alg:ES256K`, SEC1-uncompressed pubkey bytes) and `VerifiablePresentationValidator.VerifyEs256k`. Design: `docs/superpowers/specs/2026-07-09-ethereum-verify-phase1-design.md`.

**Feature 178 (Phase 2) added `ecrecover` to the same primitive — still verify-only.** `Secp256k1Recovery.TryRecover(message, joseSignature)` recovers the candidate secp256k1 public keys from a JOSE ES256K signature (64-byte `r‖s`, no recid → tries recid 0 and 1; SEC1 §4.1.6 `Q = r⁻¹(sR − eG)` via BouncyCastle). `Secp256k1Verifier.VerifyByAddress` composes recover → `EthereumAddress.FromPublicKey` (the Phase-1 keccak/EIP-55) → case-insensitive match, tolerating a CAIP-10 `eip155:{chain}:0x…` input. This is what verifies **address-only** issuer DIDs (`did:pkh` / address-form `did:ethr`) where no key is published — see the `verifiable-credentials` skill. Recovery is **not** signing: no private-key op, no `WalletNetworks` member. Design: `docs/superpowers/specs/2026-07-09-ethereum-verify-phase2-design.md`.

**Feature 180 (Phase 3) added secp256k1 SIGNING to the same primitive — prove-control only, no transactions.** `Secp256k1Signer.SignRecoverable(digest32, privateKey32)` produces a recoverable Ethereum signature (RFC-6979 deterministic `k` via BouncyCastle `HMacDsaKCalculator`, **low-s canonical**, 65-byte `r‖s‖v`, `v = recoveryId + 27`). `Eip191.PersonalSignDigest` = `keccak256("\x19Ethereum Signed Message:\n" + len + message)` — **Ethereum ECDSA hashes with keccak, not SHA-256**, so `Secp256k1Recovery` was refactored to a shared `RecoverCore` with `RecoverFromDigest(digest, r, s, recId)` (Ethereum, digest-direct) beside the unchanged `TryRecover(message, joseSig)` (ES256K, SHA-256). `Siwe/` (message model + `SiweFormatter` format/parse + `SiweVerifier`) implements EIP-4361 produce **and** verify (Sorcha as relying party). All pure-managed (WASM-safe — the PWA wallet signs on-device). **Security:** used only for EIP-191/SIWE prove-control; no arbitrary-digest signing API; the wallet-side `IEthereumIdentityService` (derive-on-demand from the encrypted seed, tx-guard, no key export) is the Phase-3 **part-2** follow-up. No new `WalletNetworks` member (auxiliary `m/44'/60'/0'/0/x` identity). Design: `docs/superpowers/specs/2026-07-10-ethereum-verify-phase3-design.md`.

**Feature 182 (Phase 4) added native ETH TRANSACTING to the same primitive — still no Nethereum, still pure-managed.** `Rlp` (minimal RLP encoder: byte-string / list / **minimal big-endian scalar** — zero → `0x80`, leading zeros stripped) + `EthereumTransaction` (EIP-1559 **type-2**): `BuildSigningPayload()` = `0x02 ‖ rlp([chainId,nonce,maxPriorityFee,maxFee,gasLimit,to,value,data,accessList=[]])`, `SigningHash()` = its keccak, `AssembleSigned(sig65)` = `0x02 ‖ rlp([…,yParity,r,s])` + tx hash (typed-tx signatures carry parity, so **`yParity = v − 27`** — reuses the unchanged `Secp256k1Signer`). Anchored by a hand-derived byte-exact signing-payload vector + a sign→`Secp256k1Recovery`→address round-trip (no external tool needed). The `data` field is empty this phase (native transfers only; contract-write calldata is a later seam via `AbiCodec`). **Custody/layering:** the value-moving signer is the server-side `IEthereumTransactionSigner` in `Sorcha.Wallet.Core` (a SEPARATE interface from the WASM-safe `IEthereumIdentityService` prove-control surface — the prove-control tx-guard is untouched), it takes **no** `IEvmRpcClient` dependency, and the orchestrator/policy/endpoints live in `Sorcha.Wallet.Service` (gated, testnet-first, server-side only). `IEvmRpcClient` gained write/query methods (`eth_sendRawTransaction`, `getTransactionCount` pending, `estimateGas`, `maxPriorityFeePerGas`, base fee via `getBlockByNumber`, `getTransactionReceipt`, `chainId`). Design: `docs/superpowers/specs/2026-07-11-ethereum-verify-phase4-design.md`.

## Quick Start

### Key Generation

```csharp
// Inject ICryptoModule
var keySetResult = await _cryptoModule.GenerateKeySetAsync(WalletNetworks.ED25519);
if (!keySetResult.IsSuccess)
    throw new InvalidOperationException($"Key generation failed: {keySetResult.Status}");

var keySet = keySetResult.Value!;
// keySet.PrivateKey.Key = 64 bytes (ED25519)
// keySet.PublicKey.Key = 32 bytes (ED25519)
```

### Signing & Verification

```csharp
// Hash then sign
byte[] hash = SHA256.HashData(transactionData);
var signResult = await _cryptoModule.SignAsync(
    hash,
    (byte)WalletNetworks.ED25519,
    keySet.PrivateKey.Key!);

// Verify
var status = await _cryptoModule.VerifyAsync(
    signResult.Value!,
    hash,
    (byte)WalletNetworks.ED25519,
    keySet.PublicKey.Key!);
bool isValid = status == CryptoStatus.Success;
```

### HD Wallet Creation

```csharp
var keyRing = await _keyManager.CreateMasterKeyRingAsync(WalletNetworks.ED25519, password: null);
// keyRing.Mnemonic = "word1 word2 ... word12" — user must backup
// keyRing.MasterKeySet contains derived keys
```

## Key Concepts

| Concept | Usage | Example |
|---------|-------|---------|
| `WalletNetworks` | Algorithm selection | `ED25519`, `NISTP256`, `RSA4096` |
| `CryptoResult<T>` | Error handling | `.IsSuccess`, `.Status`, `.Value` |
| `KeySet` | Public/private pair | `.PrivateKey.Key`, `.PublicKey.Key` |
| `KeyRing` | Full wallet with mnemonic | `.Mnemonic`, `.MasterKeySet` |
| `.Zeroize()` | Secure memory clearing | Call when done with keys |

## Common Patterns

### Platform-Specific Key Storage

```csharp
// Encryption provider abstraction handles platform differences
var encrypted = await _encryptionProvider.EncryptAsync(privateKey, "wallet-key-id");
// Windows: DPAPI, Linux: Secret Service, Dev: AES-GCM
```

### Address Generation

```csharp
var address = _walletUtilities.PublicKeyToWallet(publicKey, (byte)WalletNetworks.ED25519);
// Returns: "ws1q8tuvvdykly8n0fy5jkuu8cjw0fu0p6jl5rp9g..."
```

## See Also

- [patterns](references/patterns.md) - Algorithm selection, signing workflows, key management
- [workflows](references/workflows.md) - Wallet creation, transaction signing, encryption

## Related Skills

- See the **nbitcoin** skill for HD wallet derivation paths (BIP32/39/44)
- See the **postgresql** skill for encrypted key storage patterns
- See the **xunit** and **fluent-assertions** skills for testing crypto code

## Documentation Resources

> Fetch latest cryptography documentation with Context7.

**How to use Context7:**
1. Use `mcp__context7__resolve-library-id` to search for "libsodium" or "System.Security.Cryptography"
2. Query with `mcp__context7__query-docs` using the resolved library ID

**Recommended Queries:**
- "ED25519 signing verification"
- "AES-GCM authenticated encryption"
- "BIP39 mnemonic seed derivation"