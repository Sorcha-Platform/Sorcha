# Phase 1 Data Model: SIWE / prove-control

Signing + verification logic — no persisted entities (the ETH key is derived on demand from the wallet's
existing encrypted seed). The "model" is the new types.

## Primitive (`Sorcha.Cryptography.Secp256k1`)

### `Secp256k1Signer` (new static)

```csharp
public static class Secp256k1Signer
{
    /// <summary>Recoverable RFC-6979 low-s ECDSA over a 32-byte digest (Ethereum-style, no re-hash).
    /// Returns 65 bytes r(32)‖s(32)‖v(1), v = recoveryId + 27.</summary>
    public static byte[] SignRecoverable(ReadOnlySpan<byte> digest32, ReadOnlySpan<byte> privateKey32);
}
```

### `Eip191` (new static)

```csharp
public static class Eip191
{
    /// <summary>keccak256("\x19Ethereum Signed Message:\n" + len + message).</summary>
    public static byte[] PersonalSignDigest(ReadOnlySpan<byte> message);
}
```

### `Secp256k1Recovery` (edit — add digest-level recovery)

```csharp
// New public single-recid recovery from a raw digest (Ethereum). Existing TryRecover(message, joseSig)
// (SHA-256, ES256K) is unchanged; both now share a private RecoverCore(e, r, s).
public static Secp256k1PublicKey? RecoverFromDigest(ReadOnlySpan<byte> digest32, BigInteger r, BigInteger s, int recoveryId);
```

## SIWE (`Sorcha.Cryptography.Secp256k1/Siwe`)

### `SiweMessage`

| Field | Type | Required |
|---|---|---|
| `Scheme` | `string?` | optional |
| `Domain` | `string` | ✔ |
| `Address` | `string` (EIP-55) | ✔ |
| `Statement` | `string?` | optional |
| `Uri` | `string` | ✔ |
| `Version` | `string` (`"1"`) | ✔ |
| `ChainId` | `long` | ✔ |
| `Nonce` | `string` | ✔ |
| `IssuedAt` | `string` (ISO-8601) | ✔ |
| `ExpirationTime` | `string?` | optional |
| `NotBefore` | `string?` | optional |
| `RequestId` | `string?` | optional |
| `Resources` | `IReadOnlyList<string>?` | optional |

### `SiweFormatter`

- `string Format(SiweMessage)` — EIP-4361 text (R4).
- `bool TryParse(string message, out SiweMessage)` — inverse; fail-closed on missing required field.

### `SiweVerifier`

```csharp
public sealed record SiweValidationOptions(string? ExpectedNonce = null, string? ExpectedDomain = null, DateTimeOffset? NowUtc = null);
public sealed record SiweVerificationResult(bool Valid, string? Address, string? Reason);

public static class SiweVerifier
{
    public static SiweVerificationResult Verify(string message, ReadOnlySpan<byte> signature65, SiweValidationOptions options);
}
```

Invariants: recovered address == message address (case-insensitive); nonce/domain match when supplied;
`NowUtc` within `[NotBefore, ExpirationTime]` when present; malformed/tampered → `Valid=false` (never throws).

## Wallet (`Sorcha.Wallet.Core`)

### `IKeyManagementService` (edit)

```csharp
Task<(byte[] PrivateKey, byte[] PublicKey)> DeriveSecp256k1KeyAtPathAsync(byte[] seed, DerivationPath derivationPath);
// NBitcoin derive → raw secp256k1 scalar (32) + uncompressed SEC1 pubkey (65). No GenerateKeySet / WalletNetworks.
```

### `IEthereumIdentityService` (new)

```csharp
public interface IEthereumIdentityService
{
    Task<string> GetAddressAsync(string walletId, int index = 0, CancellationToken ct = default);
    Task<byte[]> SignPersonalMessageAsync(string walletId, ReadOnlySpan<byte> message, int index = 0, CancellationToken ct = default);
    Task<SiweSignResult> SignSiweAsync(string walletId, SiweMessage message, int index = 0, CancellationToken ct = default);
}
public sealed record SiweSignResult(string Message, string Signature /* 0x-hex 65-byte */, string Address);
```

- Derivation path: `m/44'/60'/0'/0/{index}` (default index 0).
- Custody: decrypt seed via the existing `DecryptPrivateKeyAsync` path → derive → sign → discard. Private
  key never returned. Transaction-payload guard on the message. Same auth as any wallet op.

## Endpoints (`Sorcha.Wallet.Service`)

| Method | Route | Body → Response |
|---|---|---|
| GET | `/api/v1/wallets/{walletId}/ethereum-address` | → `{ address }` |
| POST | `/api/v1/wallets/{walletId}/siwe/sign` | `{ domain, uri, statement?, chainId, nonce, issuedAt?, … }` → `{ message, signature, address }` |
| POST | `/api/v1/siwe/verify` | `{ message, signature, expectedNonce?, expectedDomain? }` → `{ valid, address, reason? }` |

Same authorization as existing wallet endpoints.

## Decision flow (sign)

```
walletId → load wallet → decrypt master seed (direct-master path)
        → DeriveSecp256k1KeyAtPathAsync(seed, m/44'/60'/0'/0/index)
        → build EIP-191/SIWE message → guard: reject if RLP-transaction-shaped
        → Eip191.PersonalSignDigest → Secp256k1Signer.SignRecoverable → 65-byte sig
        → discard key; return { message, signature, address }   (key NEVER returned)
```
