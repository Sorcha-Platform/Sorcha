# Ethereum Interop — Phase 4 (transacting: native ETH transfers)

- **Date:** 2026-07-11
- **Status:** Design approved (brainstorm), pending implementation plan
- **Scope:** Phase 4 (final) of the 4-phase Ethereum interoperability roadmap (Feature 182)
- **Builds on:** Phase 1 (F177 — `Sorcha.Cryptography.Secp256k1` primitive, ES256K verify), Phase 2 (F178 — `ecrecover`, keccak/EIP-55, `EthereumAddress`), Phase 2b (F179 — `did:ethr` on-chain resolution, `IEvmRpcClient` + `AbiCodec`), Phase 3 (F180 — `Secp256k1Signer` recoverable signing, `Eip191`, SIWE, `EthereumIdentityService` derive-on-demand)
- **Related:** Wallet key management (`Sorcha.Wallet.Core`, `KeyManagementService`, NBitcoin HD derivation), read-only EVM RPC (`Sorcha.ServiceClients.Http/Evm`)

---

## 1. Context & motivation

Phases 1–2b made Sorcha a **verifier** of Ethereum-signed credentials and DIDs. Phase 3 made Sorcha
**sign** secp256k1 **prove-control** messages (EIP-191 / SIWE) with a wallet's auxiliary Ethereum
identity — but the signer explicitly **refuses transaction-shaped payloads** (`GuardAgainstTransaction`).

Phase 4 is the final phase and the **first that moves value**: a Sorcha wallet can **send an Ethereum
transaction** — a native ETH transfer — signed with its existing secp256k1 key and broadcast over a
write-capable EVM RPC endpoint. Because value transfers are **irreversible**, custody, authorization,
and blast radius are the primary design concerns, not an afterthought.

The roadmap originally named **Nethereum** for this phase. That predates a three-phase track record of a
**pure-managed, WASM-safe, zero-new-dependency** primitive. This design keeps that invariant: a minimal
hand-rolled RLP encoder + EIP-1559 transaction builder reusing the existing `Keccak256` and
`Secp256k1Signer`. **No Nethereum, no new dependency.**

Roadmap:

| Phase | Capability | Status |
|---|---|---|
| 1 (F177) | Verify ES256K VCs where the DID resolves offline to a key | Shipped (#1140) |
| 2 (F178) | `did:pkh` + address-form `did:ethr` via offline `ecrecover` | Shipped (#1141) |
| 2b (F179) | `did:ethr` current document via read-only EVM RPC | Shipped (#1143) |
| 3 (F180) | SIWE / prove-control — ETH address + secp256k1 message signing | Shipped (#1144/#1145) |
| **4 (this spec)** | **Transacting — sign + broadcast a native ETH transfer (EIP-1559 type-2)** | This spec (F182) |

## 2. Goal & non-goals

**Goal.** A Sorcha wallet can send a **native ETH transfer** on an operator-enabled chain: the Wallet
Service gathers chain parameters (nonce, gas, fees) over a write-capable EVM RPC, the wallet's auxiliary
secp256k1 key (derived on-demand, Phase 3) signs a fully-specified **EIP-1559 (type-2)** transaction, and
the service broadcasts it and reports the transaction hash. A companion status endpoint reports the
receipt. All value-moving operations are **gated** by explicit policy: testnet-only by default, a per-tx
value cap, a chain allowlist, and a distinct authorization policy. The key is never returned and never
signs anything outside this sanctioned, gated path.

**Non-goals (deferred or excluded):**

- **Nethereum / any new third-party dependency.** RLP + the type-2 envelope are hand-rolled pure-managed
  (decision Q1).
- **Contract writes / ABI-encoded calldata.** ETH transfers only this phase; `data` is always empty. A
  later phase can add calldata (the `AbiCodec` seam already exists). (Decision Q3.)
- **Legacy (type-0) transactions; EIP-2930 access lists; EIP-4844 (type-3) blob transactions.** Type-2 only.
- **Mainnet by default.** A known-testnet allowlist is enabled by default; mainnet-class chains require an
  explicit `AllowMainnet` master switch **and** per-chain RPC configuration. (Decision Q2.)
- **On-device / WASM signing.** Build+sign+broadcast are wired **server-side only** (Wallet Service host);
  the WASM PWA never signs a value transaction. (Decision Q5.)
- **Nonce queueing / replacement / speed-up / cancel; batching; gas-price strategies** beyond the
  `baseFee×2 + priorityFee` heuristic; concurrent-send nonce coordination.
- **No `WalletNetworks` / primary-algorithm change; no key export.** Same custody as Phase 3.

## 3. Confirmed design decisions

1. **Pure-managed transaction encoding (Q1).** A minimal `Rlp` encoder + `EthereumTransaction` (EIP-1559
   type-2) builder join the primitive `Sorcha.Cryptography.Secp256k1`, reusing `Keccak256` +
   `Secp256k1Signer`. No Nethereum, no new dependency, WASM-safe by construction (though only wired
   server-side).
2. **Gated path + policy guardrails (Q2).** A **new** sanctioned transaction-signing method lifts the
   prove-control guard; the existing `GuardAgainstTransaction` on the EIP-191/SIWE methods is **untouched**.
   `EthereumTransactionOptions` enforces a chain allowlist, a `AllowMainnet=false` master gate, a per-tx
   value cap, and a `MaxFeePerGasWei` ceiling. A distinct `CanTransactEthereum` authorization policy gates
   the send endpoint.
3. **Native ETH transfers only, EIP-1559 type-2, testnet-first (Q3).** Smallest blast radius for the first
   value-moving phase. `data` is always empty.
4. **Server-side only (Q5).** The transaction service + write-capable RPC are registered only in the
   Wallet Service host, mirroring the Phase-2b read-only-RPC server-only boundary. A composition test
   asserts the WASM PWA registers no transaction service.
5. **Fire-and-report-hash broadcast (Q4).** The send endpoint returns the transaction hash immediately
   after `eth_sendRawTransaction`; a separate status endpoint reports the receipt. The request never blocks
   on confirmation.
6. **Clean layering.** `Sorcha.Wallet.Core` builds+signs a **fully-specified, deterministic** transaction
   and takes **no** dependency on `IEvmRpcClient`. `Sorcha.Wallet.Service` gathers chain parameters (nonce,
   gas, fees) and broadcasts.
7. **Same key custody as Phase 3.** Derive-on-demand from the encrypted seed at `m/44'/60'/0'/0/{index}`,
   sign, clear; never returned. No `WalletNetworks` change.

## 4. Where it fits in the codebase (project map)

| Project | Change |
|---|---|
| **`src/Common/Sorcha.Cryptography.Secp256k1`** | **New** `Rlp.cs` (minimal RLP encoder: byte-string + list, minimal big-endian integer encoding, `0x80` for zero). **New** `EthereumTransaction.cs` (EIP-1559 type-2: `BuildSigningPayload` → keccak signing hash; `AssembleSigned(r, s, v)` → `0x02‖rlp([...])` signed raw tx + keccak tx hash; converts the signer's `v∈{27,28}` to `yParity∈{0,1}`). Pure-managed; reuses `Keccak256` + `Secp256k1Signer`. |
| **`src/Common/Sorcha.ServiceClients.Http/Evm`** | **Extend** `IEvmRpcClient`/`EvmRpcClient` with the write+query methods over the same `SendAsync` core (SSRF-guarded, 3-outcome, never-throws): `SendRawTransactionAsync` (`eth_sendRawTransaction`), `GetTransactionCountAsync` (`eth_getTransactionCount`, `pending`), `EstimateGasAsync` (`eth_estimateGas`), `GetMaxPriorityFeePerGasAsync` (`eth_maxPriorityFeePerGas`), `GetBaseFeePerGasAsync` (via `eth_getBlockByNumber("pending", false)` → `baseFeePerGas`), `GetTransactionReceiptAsync` (`eth_getTransactionReceipt`), `GetChainIdAsync` (`eth_chainId`). |
| **`src/Core/Sorcha.Wallet.Core`** | **Extend** `IEthereumIdentityService`/`EthereumIdentityService` with `SignTransactionAsync(walletAddress, EthereumTransactionRequest, index)` → `SignedEthereumTransaction { RawTxHex, TxHash, From }` — the **sanctioned tx path** (builds the unsigned payload, derives+signs, assembles). The prove-control `GuardAgainstTransaction` stays on the EIP-191/SIWE methods, unchanged. **New** `EthereumTransactionRequest` value object (chainId, to, valueWei, nonce, gasLimit, maxFeePerGasWei, maxPriorityFeePerGasWei, empty data). No `IEvmRpcClient` dependency. |
| **`src/Services/Sorcha.Wallet.Service`** | **New** `IEthereumTransactionService`/`EthereumTransactionService` orchestrator (policy → fetch nonce/fees/gas via `IEvmRpcClient` → `SignTransactionAsync` → broadcast → return hash; and receipt lookup). **New** `EthereumTransactionOptions` (policy config). **New** endpoints on `EthereumEndpoints` (or a sibling). Register `IEvmRpcClient` + `EthereumTransactionOptions` in `Program.cs`; register `CanTransactEthereum` policy. |
| **Docs** | STANDARDS.md (EIP-1559 / EIP-2718 typed-tx / RLP rows), `cryptography` skill, `sorcha-architecture` skill (Wallet Service endpoint surface), Wallet Service README, `docs/reference/API-DOCUMENTATION.md`, memory. |

> Verify seams, DID resolution, trust, prove-control (EIP-191/SIWE), and the wallet's primary-algorithm
> model are **untouched** by design.

## 5. Architecture

### 5.1 Transaction primitive (`Sorcha.Cryptography.Secp256k1`)

- **`Rlp`** — pure-managed RLP encoding: `EncodeBytes(ReadOnlySpan<byte>)`, `EncodeList(params byte[][])`,
  and an integer helper that emits **minimal big-endian** bytes (no leading zeros; zero → the empty string
  `0x80`). This minimal-integer rule is the subtle correctness core.
- **`EthereumTransaction`** — an EIP-1559 (type-2) value object plus:
  - `BuildSigningPayload()` → `0x02 ‖ rlp([chainId, nonce, maxPriorityFeePerGas, maxFeePerGas, gasLimit,
    to, value, data, accessList=[]])`; the signing hash is `Keccak256` of that payload.
  - `AssembleSigned(r, s, v)` → converts `yParity = v − 27` (typed-tx signatures carry the parity, **not**
    `27/28`), emits `0x02 ‖ rlp([…same fields…, yParity, r, s])`, and returns `{ RawTxHex, TxHash =
    Keccak256(signedTx) }`.
- Signing reuses `Secp256k1Signer.SignRecoverable(signingHash, privateKey)` unchanged.

### 5.2 EVM RPC write/query extension (`Sorcha.ServiceClients.Http/Evm`)

`IEvmRpcClient` gains the write+query methods, each built on the existing `SendAsync` (SSRF host-allow
check, HTTPS-only, 5s timeout, `NotConfigured/Error/Ok` outcome, never throws):

- `SendRawTransactionAsync(chainId, rawTxHex)` → `eth_sendRawTransaction` → tx hash (or Error/NotConfigured).
- `GetTransactionCountAsync(chainId, address)` → `eth_getTransactionCount(address, "pending")` → nonce.
- `EstimateGasAsync(chainId, from, to, valueHex, dataHex)` → `eth_estimateGas` → gas limit.
- `GetMaxPriorityFeePerGasAsync(chainId)` → `eth_maxPriorityFeePerGas`.
- `GetBaseFeePerGasAsync(chainId)` → `eth_getBlockByNumber("pending", false)` → `baseFeePerGas`.
- `GetTransactionReceiptAsync(chainId, txHash)` → `eth_getTransactionReceipt` → `{ status, blockNumber,
  gasUsed }` or null (still pending).
- `GetChainIdAsync(chainId)` → `eth_chainId` (cross-check the configured RPC actually serves that chain).

Registration remains **server-side only**. Numeric results are decoded from `0x…` hex via `BigInteger`.

### 5.3 Wallet Ethereum transaction signing (`Sorcha.Wallet.Core`)

- **`EthereumTransactionRequest`** — a fully-specified, deterministic value object: `ChainId`, `To`
  (EIP-55/hex), `ValueWei` (BigInteger), `Nonce`, `GasLimit`, `MaxFeePerGasWei`, `MaxPriorityFeePerGasWei`.
  `Data` is empty this phase.
- **`IEthereumIdentityService.SignTransactionAsync(walletAddress, EthereumTransactionRequest, index)`** →
  `SignedEthereumTransaction { RawTxHex, TxHash, From }`. Derives the key via the existing `DeriveAsync`
  seed path (Phase 3), builds the unsigned payload, signs, assembles, clears the key. This is the **only**
  method that produces a transaction; the prove-control guard remains on the message-signing methods.

### 5.4 Transaction orchestration + policy (`Sorcha.Wallet.Service`)

- **`EthereumTransactionOptions`** (config `Ethereum:Transactions`):
  - `EnabledChainIds` — allowlist; default `[11155111 (Sepolia), 17000 (Holešky)]`.
  - `AllowMainnet` — master gate, default `false`. Any chain **not** in the built-in known-testnet set is
    refused unless this is `true` (defence against fat-fingering a mainnet chainId).
  - `MaxValueWei` — per-tx value cap; default `100000000000000000` (0.1 ETH).
  - `MaxFeePerGasWei` — sanity ceiling on the computed `maxFeePerGas` (a mis-fetched fee can't drain gas).
- **`EthereumTransactionService`** — the send flow:
  1. **Policy**: chainId ∈ `EnabledChainIds`; chain is a known testnet OR `AllowMainnet`; `valueWei ≤
     MaxValueWei`; `to` is a valid 20-byte address.
  2. **Chain params**: `nonce = GetTransactionCount(pending)`; `priority = GetMaxPriorityFeePerGas`;
     `baseFee = GetBaseFeePerGas`; `maxFee = baseFee×2 + priority` (clamped to `MaxFeePerGasWei`);
     `gasLimit = EstimateGas` (**fail-closed** — no estimate ⇒ no send). Optional one-time `GetChainId`
     cross-check.
  3. **Sign**: `SignTransactionAsync` with the fully-specified request.
  4. **Broadcast**: `SendRawTransactionAsync`; return `{ txHash, from, chainId, nonce, status:"submitted" }`.
  - **Receipt**: `GetReceiptAsync(chainId, txHash)` → `{ status: pending|success|reverted, blockNumber?,
    gasUsed? }`.
- Any RPC `Error`/`NotConfigured`, policy failure, or estimate failure ⇒ the send is **refused** (never a
  partial/blind broadcast); each rejection is logged + metered.

### 5.5 Endpoints (Wallet Service, server-side only)

- `POST /api/v1/wallets/{walletAddress}/ethereum/transactions` — `{ chainId, to, valueWei, index? }` →
  `{ txHash, from, chainId, nonce, status }`. `RequireAuthorization("CanTransactEthereum")`.
- `GET /api/v1/ethereum/transactions/{chainId}/{txHash}` — → receipt status. Authenticated.
- `POST /api/v1/wallets/{walletAddress}/ethereum/transactions/preview` — read-only: returns the computed
  `{ nonce, gasLimit, maxFeePerGas, maxPriorityFeePerGas, valueWei, estimatedTotalCostWei }` **without
  signing or broadcasting**, so a caller can see the irreversible cost first. Stateless (not a
  propose/confirm ceremony). `CanTransactEthereum`.
- All endpoints carry `.WithSummary`/`.WithDescription` for OpenAPI.

### 5.6 Observability

Counters on a `Sorcha.Ethereum` (or existing wallet) meter: `sorcha_eth_tx_submitted_total{chainId}`,
`sorcha_eth_tx_rejected_total{reason}` (policy vs rpc-error vs estimate-failed), `sorcha_eth_tx_broadcast_failed_total{chainId}`.

## 6. Data flow (send)

1. Authenticated caller `POST`s `{ chainId, to, valueWei }` for a wallet.
2. `EthereumTransactionService` enforces policy (chain allowlist, mainnet gate, value cap, address shape).
3. It fetches `nonce`/`priorityFee`/`baseFee`/`gasLimit` over `IEvmRpcClient`, computes `maxFee`.
4. `EthereumIdentityService.SignTransactionAsync` derives the key, builds `0x02‖rlp([...])`, keccak-signs,
   assembles the signed raw tx + hash, clears the key.
5. `SendRawTransactionAsync` broadcasts; the service returns `{ txHash, status:"submitted" }` immediately.
6. The caller polls `GET …/transactions/{chainId}/{txHash}` for the receipt (`pending`→`success`/`reverted`).

## 7. Security & error handling

- **Custody:** identical to Phase 3 — key derived on-demand from the encrypted seed, used, cleared, never
  returned. Only `SignTransactionAsync` produces a transaction, and only the Wallet Service host wires it.
- **Irreversibility guardrails:** testnet-only default + `AllowMainnet` gate + per-tx value cap +
  `maxFeePerGas` ceiling + explicit `CanTransactEthereum` authorization. The optional preview endpoint lets
  a caller see cost before an irreversible send.
- **Fail-closed everywhere:** any RPC `Error`/`NotConfigured`, gas-estimate failure, policy violation, or
  malformed input ⇒ the send is refused (no blind broadcast). RPC methods never throw to the orchestrator.
- **RLP/`v`-parity correctness:** the type-2 signing payload, the `yParity = v−27` conversion, and minimal
  integer encoding are anchored by a **published EIP-1559 known-key → known-raw-tx + hash vector** before
  the service is wired.
- **No new signing surface for prove-control:** the EIP-191/SIWE guard is untouched; the key still cannot be
  coaxed into signing a transaction through the prove-control methods.
- **SSRF:** write RPC reuses the existing host-allow / HTTPS-only / timeout guard; `AllowPrivateAddresses`
  only for local dev nodes.

## 8. Testing strategy

- **`Rlp` unit tests:** byte-string, short/long list, minimal-integer (leading-zero strip, zero → `0x80`)
  against known RLP vectors.
- **`EthereumTransaction` interop vector:** a **published EIP-1559 test vector** (known private key + fields
  → known signed raw tx + hash) verifies `BuildSigningPayload`/`AssembleSigned` exactly; `yParity`
  conversion asserted; sign→`Secp256k1Recovery`→`from` address round-trip.
- **`IEvmRpcClient` write methods (fake HTTP):** each method decodes a canned JSON-RPC response; `Error`
  and `NotConfigured` propagate; hex→`BigInteger` decoding; SSRF/timeout unchanged.
- **`EthereumTransactionService` (fake `IEvmRpcClient`):** happy-path send returns a hash; policy rejects
  (disabled chain, mainnet-gated, over value cap, over fee ceiling, bad address); gas-estimate failure ⇒
  refuse; RPC error ⇒ refuse; `maxFee` clamp; receipt mapping (`pending`/`success`/`reverted`); preview
  returns costs without broadcasting (fake asserts `SendRawTransaction` never called).
- **`EthereumIdentityService.SignTransactionAsync`:** deterministic raw tx from a known seed/path/request;
  the prove-control `GuardAgainstTransaction` still refuses on `SignPersonalMessage`/`SignSiwe`.
- **Endpoints:** send happy-path + `CanTransactEthereum` auth (401/403); status; preview; validation
  problems for bad input.
- **Composition:** the WASM PWA host registers **no** `IEthereumTransactionService` / write RPC (offline
  invariant guarded).
- **Regression:** every Phase 1/2/2b/3 suite green; prove-control, verify, DID resolution, and
  `WalletNetworks` paths unchanged.

## 9. Seams for later phases (designed-in, not built)

- **Contract writes:** add non-empty `data` to `EthereumTransactionRequest` and an ABI-encode helper
  (`AbiCodec` already exists for calldata); the tx builder, signer, and broadcast path are unchanged.
- **Nonce management:** a per-address nonce tracker/queue can replace the naive `pending` fetch for
  concurrent-send correctness.
- **Fee strategy / replacement:** speed-up / cancel (same nonce, higher fee) can extend the orchestrator.
- **Mainnet enablement:** `AllowMainnet=true` + a mainnet RPC is the whole switch; the guardrails already
  exist.

## 10. Risks & assumptions to verify during planning

1. **EIP-1559 encoding exactness.** The type-2 signing payload, minimal-integer RLP, empty access list, and
   `yParity = v−27` conversion must match a published vector — anchor before wiring the service.
2. **Fee computation robustness.** `baseFee×2 + priority`, the `MaxFeePerGasWei` clamp, and the
   estimate-fail-closed rule must be covered; confirm `eth_getBlockByNumber("pending")` returns
   `baseFeePerGas` on the target testnets.
3. **Nonce under concurrency.** `pending` nonce races on concurrent sends from one address are a documented
   limitation this phase; a test documents the single-send happy path only.
4. **Layering.** `Sorcha.Wallet.Core` must not gain an `IEvmRpcClient` dependency (verify the Core project
   references); the RPC fetch/broadcast stay in `Sorcha.Wallet.Service`.
5. **Server-only registration.** Assert (composition test) the WASM PWA registers no transaction service /
   write RPC.
6. **Config namespace.** `Ethereum:Transactions` is distinct from the DID-namespaced `DidResolver:Ethr`
   (Phase 2b); the write RPC still reads the same per-chain RPC URL cascade — confirm the endpoint used for
   transacting is write-capable (public read-only endpoints reject `eth_sendRawTransaction`).

## 11. Out of scope (YAGNI)

Contract writes / calldata / ABI encoding; legacy (type-0) transactions; access lists (type-1) / blob
(type-3) transactions; mainnet by default; nonce queueing / replacement / speed-up / cancel; batching;
gas-price strategies beyond `baseFee×2 + priority`; on-device / WASM signing; Nethereum / any new
dependency; `WalletNetworks` / primary-algorithm change; key export; on-chain PII.
