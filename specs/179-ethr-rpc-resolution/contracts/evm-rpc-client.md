# Contract: `IEvmRpcClient` + `AbiCodec`

**Project**: `src/Common/Sorcha.ServiceClients.Http/Evm` — read-only JSON-RPC, SSRF-guarded, server-only.

## `IEvmRpcClient`

```csharp
Task<EvmCallResult> CallAsync(long chainId, string to, string dataHex, CancellationToken ct);
Task<EvmLogsResult> GetLogsAsync(long chainId, string address, string?[] topics, long block, CancellationToken ct);
```

| Input / condition | Result |
|---|---|
| chain has no configured RPC URL | `NotConfigured` (never a network call) |
| configured; host private/reserved and not allowed | `Error` (SSRF-blocked, logged) |
| configured; timeout (5s) / network error / non-200 / malformed JSON-RPC / `error` member | `Error` |
| configured; success | `Ok(hexResult)` / `Ok(logs)` |

- `eth_call` uses `{"to":to,"data":dataHex}` at `"latest"`. `eth_getLogs` uses
  `{"address":address,"fromBlock":hex(block),"toBlock":hex(block),"topics":topics}`.
- Never throws to the caller; every failure is an `Error` outcome.
- Reuses `WebDidResolver.IsPrivateOrReservedAddress`; `DidResolver:AllowPrivateAddresses` permits dev nodes.

## `AbiCodec`

| Method | Behaviour |
|---|---|
| `Selector(sig)` | `Keccak256(sig)[..4]` → `0x`+8 hex |
| `EventTopic(sig)` | `0x`+`Keccak256(sig)` (32 bytes) |
| `Pad32Topic(address)` | `0x`+24 hex zeros+40-hex address (indexed address topic) |
| `EncodeAddress(addr)` | 32-byte left-padded |
| `DecodeAddress(word)` / `DecodeUInt(word)` / `DecodeBytes32(word)` / `DecodeBytes(data, offset)` | ABI word decode |

## Test contract

- **KAT anchor**: `EventTopic("Transfer(address,address,uint256)")` == `0xddf252ad1be2c89b69c2b068fc378daa952ba7f163c4a11628f55a4df523b3ef` (canonical ERC-20 Transfer) — proves the keccak-of-signature machinery.
- `Selector("changed(address)")` / `Selector("identityOwner(address)")` computed and stable; ERC-1056 event topics computed from their signature strings.
- Address/uint/bytes32 word round-trips; `Pad32Topic` shape.
- `EvmRpcClient`: unconfigured chain → `NotConfigured` (no HTTP); private host → `Error`; timeout → `Error`; a canned 200 JSON-RPC → `Ok`.
