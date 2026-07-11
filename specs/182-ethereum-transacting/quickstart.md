# Quickstart — Ethereum Transacting (Phase 4)

**Audience**: developers/operators wiring and exercising native ETH transfers. Server-side only, testnet-first.

## 1. Configure (operator)

`appsettings` (Wallet Service), namespace `Ethereum:Transactions` plus the per-chain **write-capable** RPC
URL (reuses the EVM RPC cascade — must accept `eth_sendRawTransaction`, not a read-only public endpoint):

```jsonc
{
  "Ethereum": {
    "Transactions": {
      "EnabledChainIds": [ 11155111 ],           // Sepolia
      "AllowMainnet": false,                       // master gate — leave off for testnet
      "MaxValueWei": "100000000000000000",         // 0.1 ETH per-tx cap
      "MaxFeePerGasWei": "50000000000",            // 50 gwei ceiling
      "DefaultPriorityFeeWei": "1500000000"        // 1.5 gwei fallback tip
    }
  },
  "DidResolver": {                                  // per-chain RPC URL cascade (Phase 2b), write-capable here
    "Ethr": { "Rpc": { "11155111": "https://sepolia.write-capable.example/v1/KEY" } }
  }
}
```

Fund the wallet's Ethereum address (get it via the Phase-3 endpoint
`GET /api/v1/wallets/{walletAddress}/ethereum-address`) from a Sepolia faucet.

## 2. Preview the cost (read-only)

```bash
curl -X POST /api/v1/wallets/{walletAddress}/ethereum/transactions/preview \
  -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -d '{ "chainId": 11155111, "to": "0x742d…44e", "valueWei": "1000000000000000" }'
# → { nonce, gasLimit, maxFeePerGasWei, maxPriorityFeePerGasWei, valueWei, estimatedTotalCostWei }
# Nothing is signed or broadcast.
```

## 3. Send the transfer

```bash
curl -X POST /api/v1/wallets/{walletAddress}/ethereum/transactions \
  -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -d '{ "chainId": 11155111, "to": "0x742d…44e", "valueWei": "1000000000000000" }'
# → { "txHash": "0x…", "from": "0x…", "chainId": 11155111, "nonce": 3, "status": "submitted" }
```

Requires the `CanTransactEthereum` permission. Returns immediately — does not wait for confirmation.

## 4. Poll for status

```bash
curl /api/v1/ethereum/transactions/11155111/0x… -H "Authorization: Bearer $TOKEN"
# → { "txHash": "0x…", "status": "pending" }        while unmined
# → { "txHash": "0x…", "status": "success", "blockNumber": 5123456, "gasUsed": 21000 }
```

## 5. Guardrails you should see (refusals)

| Attempt | Result |
|---|---|
| chainId not in `EnabledChainIds` | 400 `reason: chain-not-enabled` |
| mainnet chain, `AllowMainnet:false` | 400 `reason: mainnet-not-allowed` |
| `valueWei` > `MaxValueWei` | 400 `reason: value-over-cap` |
| computed fee > `MaxFeePerGasWei` | 400 `reason: fee-over-ceiling` |
| bad recipient / amount | 400 `reason: invalid-address` / `invalid-amount` |
| RPC unreachable / gas estimate fails | 502 `reason: rpc-error` / `estimate-failed` — **nothing broadcast** |
| missing `CanTransactEthereum` | 403 |

## 6. Verify the invariants (tests)

- **Encoder interop**: `Sorcha.Cryptography.Secp256k1.Tests` — known key + fields → known raw tx + hash.
- **Guard untouched**: prove-control `SignPersonalMessage`/`SignSiwe` still refuse RLP-shaped payloads.
- **Server-only**: the WASM PWA host registers no `IEthereumTransactionService` / write RPC.
- **Fail-closed**: fake-RPC error at any step ⇒ refusal, no broadcast.
- **Regression**: all Phase 1/2/2b/3 suites green.
