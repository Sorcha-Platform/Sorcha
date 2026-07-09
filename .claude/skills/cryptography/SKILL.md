---
name: cryptography
description: |
  Applies multi-algorithm cryptography (ED25519, P-256, RSA-4096) using Sorcha.Cryptography.
  Use when: implementing signing, verification, encryption, key generation, HD wallets, or address encoding.
allowed-tools: Read, Edit, Write, Glob, Grep, Bash, mcp__context7__resolve-library-id, mcp__context7__query-docs
---

# Cryptography Skill

Sorcha.Cryptography provides multi-algorithm support (ED25519, P-256, RSA-4096), symmetric encryption (AES, ChaCha20), and BIP39/44 HD wallet derivation. All operations return `CryptoResult<T>` for explicit error handling—no exceptions for crypto failures.

**secp256k1 / ES256K lives in a SEPARATE project — `Sorcha.Cryptography.Secp256k1` (Feature 177, verify-only).** Pure-managed (BouncyCastle only): `ISecp256k1Verifier` (JOSE **ES256K**), `Secp256k1Jwk` (parse/build/decompress), `Keccak256`, `EthereumAddress` (EIP-55). It is deliberately **NOT** folded into `Sorcha.Cryptography` — `Sorcha.Verifier.Engine` is consumed by the Blazor **WASM** PWA, so it must stay native-dependency-free and cannot reference `Sorcha.Cryptography` (native `Sodium`/`Mcl`); both verification paths need secp256k1. And **.NET's built-in `ECDsa` does NOT reliably support secp256k1 on Windows/WASM** — BouncyCastle is required. Verify-only: no signing, no new `WalletNetworks` member (recoverable signing + `ecrecover` are Phase 3). Consumed by `SdJwtService.Verify` (`alg:ES256K`, SEC1-uncompressed pubkey bytes) and `VerifiablePresentationValidator.VerifyEs256k`. Design: `docs/superpowers/specs/2026-07-09-ethereum-verify-phase1-design.md`.

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