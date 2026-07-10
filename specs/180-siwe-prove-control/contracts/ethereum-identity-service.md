# Contract: `IEthereumIdentityService` + endpoints + key-derivation

## `IKeyManagementService.DeriveSecp256k1KeyAtPathAsync(seed, path) → (priv32, pub65)`

NBitcoin `ExtKey.CreateFromSeed(seed).Derive(path)` → raw secp256k1 scalar (32) + uncompressed SEC1
public key (65). **No `GenerateKeySet` / `AlgorithmMapper` / `WalletNetworks`** — orthogonal to the
primary algorithm. Deterministic per (seed, path).

## `IEthereumIdentityService` (Wallet.Core)

| Method | Behaviour |
|---|---|
| `GetAddressAsync(walletId, index=0)` | decrypt seed → derive `m/44'/60'/0'/0/{index}` → `EthereumAddress.FromPublicKey` (EIP-55). Deterministic. |
| `SignPersonalMessageAsync(walletId, message, index=0)` | guard message (reject RLP-tx-shaped) → `Eip191.PersonalSignDigest` → `Secp256k1Signer.SignRecoverable` → 65-byte sig. |
| `SignSiweAsync(walletId, SiweMessage, index=0)` | `SiweFormatter.Format` → sign as above → `{ Message, Signature(0x-hex), Address }`. |

**Custody invariants:** private key derived on demand, used, discarded; **never returned/logged/exported**;
same authorization as any wallet operation; no public method signs an arbitrary raw 32-byte digest.

## Endpoints (Wallet Service; same auth as existing wallet ops)

| Method | Route | → |
|---|---|---|
| GET | `/api/v1/wallets/{walletId}/ethereum-address` | `{ address }` |
| POST | `/api/v1/wallets/{walletId}/siwe/sign` | `{ message, signature, address }` |
| POST | `/api/v1/siwe/verify` | `{ valid, address, reason? }` (Sorcha as relying party; no wallet needed) |

`.WithSummary`/`.WithDescription` on each.

## Test contract

- **Deterministic address:** a known seed → the same EIP-55 address every call.
- **Sign→verify:** `SignSiweAsync` output is accepted by `SiweVerifier` and the recovered address equals `GetAddressAsync`.
- **Transaction guard:** a message that decodes as an RLP transaction → refused (no signature produced).
- **No leak:** no response/log path exposes the private key; no raw-digest signing method exists.
- **No primary-algorithm change:** existing wallet signing/address for a wallet is unchanged (regression).
