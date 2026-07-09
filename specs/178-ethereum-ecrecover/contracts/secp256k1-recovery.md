# Contract: `Secp256k1Recovery` + `VerifyByAddress` (primitive)

**Project**: `src/Common/Sorcha.Cryptography.Secp256k1` (pure-managed, BouncyCastle only, WASM-safe)

## `Secp256k1Recovery.TryRecover`

```csharp
public static IReadOnlyList<Secp256k1PublicKey> TryRecover(
    ReadOnlySpan<byte> message, ReadOnlySpan<byte> joseSignature);
```

| Input | Behaviour |
|---|---|
| `message` = the JWS signing input (`header.payload` ASCII bytes) | Hashed with SHA-256 internally (JOSE ES256K digest). |
| `joseSignature` length ≠ 64 | Returns empty list (no throw). |
| valid 64-byte `r‖s` | Returns the valid, non-infinity candidate public keys for recid 0 and 1 (0–2 entries). |
| `r` or `s` ≤ 0, or point reconstruction fails | That recid contributes no candidate; other recid still tried. |

**Guarantees**: never throws; deterministic; no allocation of secret material; no network.

## `ISecp256k1Verifier.VerifyByAddress` / static

```csharp
bool VerifyByAddress(ReadOnlySpan<byte> message, ReadOnlySpan<byte> joseSignature, string expectedAddress);
// + a static entry point mirroring Secp256k1Verifier.VerifyEs256k for SdJwtService's static call site
```

| Case | Result |
|---|---|
| a recovered candidate's EIP-55 address equals `expectedAddress` (case-insensitive) | `true` |
| `expectedAddress` is a CAIP-10 `eip155:{chain}:0x{40hex}` | trailing `0x{40hex}` is extracted and compared |
| no candidate matches / signature malformed / `expectedAddress` malformed | `false` (never throws) |

**Invariants**: verification-only — no signing API is added; the recovered key is never returned to
signing callers. keccak256 / EIP-55 come from the existing `Keccak256` / `EthereumAddress`.

## Test contract (KAT-anchored, built first)

- **Recid 0 & recid 1 vectors**: two (private key, message) pairs whose ES256K signatures recover under
  different recids; each asserts `TryRecover` contains the matching key and `EthereumAddress.FromPublicKey`
  == the known address.
- `VerifyByAddress` true for the correct address in **both** lowercase and EIP-55-checksummed form.
- `VerifyByAddress` false for a different address, a tampered signature, a 63/65-byte signature, and a
  non-hex `expectedAddress`.
