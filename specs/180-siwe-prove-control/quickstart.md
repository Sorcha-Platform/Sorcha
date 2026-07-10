# Quickstart: prove control of an Ethereum address (SIWE)

## Wallet proves control (produce)

```
GET  /api/v1/wallets/{walletId}/ethereum-address        → { "address": "0x…" }   (EIP-55, deterministic)

POST /api/v1/wallets/{walletId}/siwe/sign
     { "domain": "app.example", "uri": "https://app.example/login",
       "statement": "Sign in", "chainId": 1, "nonce": "abc123" }
  →  { "message": "app.example wants you to sign in…", "signature": "0x…(65 bytes)", "address": "0x…" }
```

The relying party recovers the signer from `signature` over `message` and checks it equals `address` and
that the nonce/domain match the challenge it issued.

## Sorcha verifies an inbound proof (Sorcha as relying party)

```
POST /api/v1/siwe/verify
     { "message": "example.com wants you to sign in…", "signature": "0x…",
       "expectedNonce": "abc123", "expectedDomain": "example.com" }
  →  { "valid": true, "address": "0x…" }        // or { "valid": false, "reason": "…" }
```

## Guardrails (security)

- **Prove-control only** — the sign surface **refuses any payload that decodes as a blockchain transaction**,
  and there is **no** endpoint to sign an arbitrary raw digest. Transactions are Phase 4.
- **No key export** — the Ethereum private key is derived on demand from the wallet's encrypted seed,
  used, and discarded; it never appears in a response or log.
- **Same auth** as any wallet operation.
- **Auxiliary identity** — the wallet's primary signing algorithm, address, and existing signatures are
  unchanged.
- **Deterministic, low-s** signatures (RFC-6979); recovery-compatible with the Ethereum ecosystem.

## For a developer — run the tests

```bash
dotnet test tests/Sorcha.Cryptography.Secp256k1.Tests   # signer round-trip, EIP-191, SIWE format/parse/verify + spec vector
dotnet test tests/Sorcha.Wallet.Core.Tests               # EthereumIdentityService: deterministic address, sign→verify, tx-guard
dotnet test tests/Sorcha.Wallet.Service.Tests            # endpoints
```

Build the whole solution first (stale DLLs → phantom failures). MTP ignores `--filter`;
`dotnet test <project>` runs the whole project.

## No new dependency

BouncyCastle (RFC-6979 + keccak) and NBitcoin (BIP32) are already referenced. No Nethereum, no ABI/RLP
encoder (that arrives with transacting in Phase 4).
