# Phase 1 Data Model — Ethereum Transacting (Phase 4)

No new persistence. These are in-memory value objects / DTOs / config carried through the send/preview/status
flow. All amounts are wei; large integers use `System.Numerics.BigInteger` (serialized as decimal strings on
the wire to avoid JS `number` precision loss).

## EthereumTransactionRequest (Wallet.Core value object)

The **fully-specified, deterministic** transaction the primitive builds+signs. Produced by the Wallet
Service after gathering chain params; the primitive never guesses a field.

| Field | Type | Notes / validation |
|---|---|---|
| `ChainId` | `long` | > 0. Target network. |
| `To` | `string` | 20-byte hex (EIP-55 or lowercase), `0x`-prefixed. Recipient. |
| `ValueWei` | `BigInteger` | ≥ 0 (a plain transfer is typically > 0). |
| `Nonce` | `long` | ≥ 0. From `eth_getTransactionCount(pending)`. |
| `GasLimit` | `long` | > 0. From `eth_estimateGas`. |
| `MaxFeePerGasWei` | `BigInteger` | > 0. Computed, ceiling-checked. |
| `MaxPriorityFeePerGasWei` | `BigInteger` | ≥ 0, ≤ `MaxFeePerGasWei`. |
| `Data` | `ReadOnlyMemory<byte>` | **Empty** this phase (native transfer). |

**Invariants**: `MaxPriorityFeePerGasWei ≤ MaxFeePerGasWei`; `Data.Length == 0`; `To` decodes to exactly 20
bytes.

## SignedEthereumTransaction (Wallet.Core result)

| Field | Type | Notes |
|---|---|---|
| `RawTxHex` | `string` | `0x02…` signed raw transaction, ready for `eth_sendRawTransaction`. |
| `TxHash` | `string` | `0x…` keccak256 of the signed raw tx. |
| `From` | `string` | EIP-55 sender address (the wallet's Ethereum identity). |

## EthereumTransaction (primitive; internal builder state)

Holds the 9 type-2 fields; exposes `BuildSigningPayload()` → `byte[]` (0x02‖rlp(9 fields)) and
`AssembleSigned(r, s, v)` → `SignedEthereumTransaction`-shaped result (`yParity = v − 27`). No identity of
its own; pure function of inputs.

## Transacting request/response DTOs (Wallet Service API)

### SendTransferRequest
| Field | Type | Notes |
|---|---|---|
| `ChainId` | `long` | Target network. |
| `To` | `string` | Recipient address. |
| `ValueWei` | `string` | Decimal-string wei (BigInteger-safe). |
| `Index` | `int?` | Ethereum identity index (default 0). |

### SendTransferResponse
| Field | Type | Notes |
|---|---|---|
| `TxHash` | `string` | Broadcast transaction hash. |
| `From` | `string` | Sender address. |
| `ChainId` | `long` | Network. |
| `Nonce` | `long` | Nonce used. |
| `Status` | `string` | `"submitted"`. |

### PreviewResponse
| Field | Type | Notes |
|---|---|---|
| `ChainId` | `long` | Network. |
| `From` | `string` | Sender address. |
| `Nonce` | `long` | Computed nonce. |
| `GasLimit` | `long` | Estimated gas. |
| `MaxFeePerGasWei` | `string` | Computed (post-ceiling-check). |
| `MaxPriorityFeePerGasWei` | `string` | Computed. |
| `ValueWei` | `string` | Requested amount. |
| `EstimatedTotalCostWei` | `string` | `ValueWei + GasLimit × MaxFeePerGasWei` (worst-case). |

### TransferStatusResponse
| Field | Type | Notes |
|---|---|---|
| `TxHash` | `string` | Queried hash. |
| `Status` | `string` | `pending` \| `success` \| `reverted`. |
| `BlockNumber` | `long?` | Null while pending. |
| `GasUsed` | `long?` | Null while pending. |

### RefusalProblem (validation/policy problem details)
Standard problem-details with a `reason` distinguishing: `chain-not-enabled`, `mainnet-not-allowed`,
`value-over-cap`, `fee-over-ceiling`, `invalid-address`, `invalid-amount`, `rpc-error`, `estimate-failed`,
`broadcast-failed`.

## EthereumTransactionOptions (Wallet Service config — `Ethereum:Transactions`)

| Field | Type | Default | Notes |
|---|---|---|---|
| `EnabledChainIds` | `long[]` | `[11155111, 17000]` | Send allowlist. |
| `AllowMainnet` | `bool` | `false` | Master gate for non-known-testnet chains. |
| `MaxValueWei` | `string` (BigInteger) | `100000000000000000` (0.1 ETH) | Per-tx value cap. |
| `MaxFeePerGasWei` | `string` (BigInteger) | operator-set conservative default | Fee ceiling. |
| `DefaultPriorityFeeWei` | `string` (BigInteger) | `1500000000` (1.5 gwei) | Fallback when `eth_maxPriorityFeePerGas` absent. |

## EVM RPC method results (ServiceClients.Http/Evm)

Reuse the existing `EvmCallResult`/`EvmLogsResult` 3-outcome pattern (`NotConfigured` / `Error` / `Ok`).
New result shapes:

| Result | Ok payload |
|---|---|
| `EvmSendResult` | tx hash string |
| `EvmUIntResult` | `BigInteger` (nonce, gas, fees, chainId — from `0x…` hex) |
| `EvmReceiptResult` | `null` (pending) or `{ StatusOk: bool, BlockNumber: long, GasUsed: long }` |

Each preserves the never-throws contract; `NotConfigured`/`Error` propagate to the orchestrator as a refusal.

## State transitions (transfer)

```
request → [policy check] → refused (reason)
        → [gather chain params] → refused (rpc-error | estimate-failed)
        → [sign] → [broadcast] → refused (broadcast-failed)
                               → submitted (txHash)
submitted → (poll) pending → success | reverted
```
