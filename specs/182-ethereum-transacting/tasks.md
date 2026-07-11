# Tasks: Ethereum Transacting — Phase 4 (Native ETH Transfers)

**Feature**: `182-ethereum-transacting` | **Branch**: `182-ethereum-transacting`
**Design source of truth**: `docs/superpowers/specs/2026-07-11-ethereum-verify-phase4-design.md`
**Inputs**: plan.md, research.md, data-model.md, contracts/ethereum-transactions.openapi.yaml, quickstart.md

**Testing approach**: TDD for the encoder (the interop vector is written and made to pass FIRST) and for the
policy/guardrails. Constitution gate: >85% coverage on new code; every touched Phase 1/2/2b/3 regression
suite stays green.

**Path conventions**: existing projects — primitive `src/Common/Sorcha.Cryptography.Secp256k1`, RPC
`src/Common/Sorcha.ServiceClients.Http/Evm`, Core `src/Core/Sorcha.Wallet.Core`, service
`src/Services/Sorcha.Wallet.Service`; tests under `tests/*`.

---

## Phase 1: Setup

- [ ] T001 Confirm the four test projects can reference the new types (add ProjectReferences only if missing): `tests/Sorcha.Cryptography.Secp256k1.Tests`, `tests/Sorcha.ServiceClients.Tests`, `tests/Sorcha.Wallet.Core.Tests`, `tests/Sorcha.Wallet.Service.Tests`. No code yet — build must still pass.
- [ ] T002 [P] Add the committed EIP-1559 interop fixture (known private key + `{chainId, nonce, maxPriorityFeePerGas, maxFeePerGas, gasLimit, to, value}` → expected signed raw-tx hex + tx hash, sourced from a published/ethers.js-derived vector) as a test resource in `tests/Sorcha.Cryptography.Secp256k1.Tests/Fixtures/Eip1559Vector.json`.
- [ ] T003 [P] Add canonical RLP vectors (empty string→`0x80`, empty list→`0xc0`, single byte, short/long string, list) as a test resource in `tests/Sorcha.Cryptography.Secp256k1.Tests/Fixtures/RlpVectors.json`.

**Checkpoint**: solution builds; fixtures present; no production code changed yet.

---

## Phase 2: Foundational (BLOCKING — prerequisites for every user story)

Shared encoder + RPC + signing seam that US1/US2/US3 all build on. Nothing user-facing works until this
phase completes.

### Primitive: pure-managed RLP + EIP-1559 encoder (TDD, interop-anchored)

- [ ] T004 [P] Write failing `Rlp` unit tests against `RlpVectors.json` (bytes, list, minimal-integer incl. zero→`0x80`, leading-zero strip) in `tests/Sorcha.Cryptography.Secp256k1.Tests/RlpTests.cs`.
- [ ] T005 Implement `Rlp` (minimal encoder: `EncodeBytes`, `EncodeList`, minimal big-endian integer helper) in `src/Common/Sorcha.Cryptography.Secp256k1/Rlp.cs` until T004 passes. License header + file-scoped namespace + XML docs.
- [ ] T006 [P] Write the **failing interop test**: `EthereumTransaction.BuildSigningPayload`/`AssembleSigned` against `Eip1559Vector.json` — assert the signed raw-tx hex + tx hash byte-for-byte, assert `yParity = v − 27`, and a sign→`Secp256k1Recovery`→`from` round-trip, in `tests/Sorcha.Cryptography.Secp256k1.Tests/EthereumTransactionTests.cs`.
- [ ] T007 Implement `EthereumTransaction` (EIP-1559 type-2: hold the 9 fields; `BuildSigningPayload()` → `0x02‖rlp([chainId,nonce,maxPriorityFee,maxFee,gasLimit,to,value,data,accessList=[]])`; `Keccak256` signing hash; `AssembleSigned(r,s,v)` → `0x02‖rlp([…,yParity,r,s])` + tx hash) in `src/Common/Sorcha.Cryptography.Secp256k1/EthereumTransaction.cs` reusing `Keccak256` + `Secp256k1Signer`, until T006 passes.

### EVM RPC write/query extension

- [ ] T008 [P] Write failing tests (fake `HttpMessageHandler`) for the new `IEvmRpcClient` methods — canned JSON-RPC responses decode correctly; `Error`/`NotConfigured` propagate; `0x…`→`BigInteger`; receipt `status` 0x1/0x0/null mapping — in `tests/Sorcha.ServiceClients.Tests/Evm/EvmRpcClientWriteTests.cs`.
- [ ] T009 Extend `IEvmRpcClient` with `SendRawTransactionAsync`, `GetTransactionCountAsync` (pending), `EstimateGasAsync`, `GetMaxPriorityFeePerGasAsync`, `GetBaseFeePerGasAsync` (via `eth_getBlockByNumber("pending",false)`), `GetTransactionReceiptAsync`, `GetChainIdAsync`, plus result types (`EvmSendResult`/`EvmUIntResult`/`EvmReceiptResult`) in `src/Common/Sorcha.ServiceClients.Http/Evm/IEvmRpcClient.cs`.
- [ ] T010 Implement the new methods over the existing `SendAsync` core (SSRF/HTTPS/timeout/never-throws preserved) in `src/Common/Sorcha.ServiceClients.Http/Evm/EvmRpcClient.cs` until T008 passes.

### Wallet.Core value objects + sanctioned signing path (NO IEvmRpcClient dependency)

- [ ] T011 [P] Add `EthereumTransactionRequest` (chainId, to, valueWei `BigInteger`, nonce, gasLimit, maxFeePerGasWei, maxPriorityFeePerGasWei, empty data; invariants: priority ≤ maxFee, data empty, to = 20 bytes) in `src/Core/Sorcha.Wallet.Core/Domain/ValueObjects/EthereumTransactionRequest.cs`.
- [ ] T012 [P] Add `SignedEthereumTransaction { RawTxHex, TxHash, From }` in `src/Core/Sorcha.Wallet.Core/Domain/ValueObjects/SignedEthereumTransaction.cs`.
- [ ] T013 [P] Write failing tests: `SignTransactionAsync` produces a deterministic raw tx from a known seed/path/request (verifies via `EthereumTransaction`/recovery), key is cleared, never returned; AND the prove-control `GuardAgainstTransaction` STILL refuses on `SignPersonalMessageAsync`/`SignSiweAsync` (unchanged) — in `tests/Sorcha.Wallet.Core.Tests/EthereumIdentityServiceTransactionTests.cs`.
- [ ] T014 Add `SignTransactionAsync(walletAddress, EthereumTransactionRequest, index)` → `SignedEthereumTransaction` to `IEthereumIdentityService` (`src/Core/Sorcha.Wallet.Core/Services/Interfaces/IEthereumIdentityService.cs`) and implement it in `src/Core/Sorcha.Wallet.Core/Services/Implementation/EthereumIdentityService.cs` (reuse `DeriveAsync`; build/sign/assemble; clear key). **Leave the existing `GuardAgainstTransaction` on the message-signing methods untouched.** Until T013 passes.
- [ ] T015 Assert-by-build that `Sorcha.Wallet.Core` gains NO reference to `Sorcha.ServiceClients.Http` (inspect `Sorcha.Wallet.Core.csproj`); document the layering in a code comment on `SignTransactionAsync`.

### Service policy + wiring scaffolding (server-side only)

- [ ] T016 [P] Add `EthereumTransactionOptions` (EnabledChainIds default `[11155111,17000]`, AllowMainnet=false, MaxValueWei "0.1 ETH", MaxFeePerGasWei, DefaultPriorityFeeWei) bound from `Ethereum:Transactions` in `src/Services/Sorcha.Wallet.Service/Configuration/EthereumTransactionOptions.cs`, with a known-testnet set helper.
- [ ] T017 Register `IEvmRpcClient` (server host, reuse Phase-2b registration pattern), bind `EthereumTransactionOptions`, and add the `CanTransactEthereum` authorization policy (defaults to the `CanManageWallets` requirement, separately nameable) in the Wallet Service DI extension + `Program.cs`.

**Checkpoint**: encoder proven against the interop vector; RPC write methods green; `SignTransactionAsync`
green with the guard intact; policy + DI wired. No endpoints yet.

---

## Phase 3: User Story 1 — Send a native ETH transfer (Priority: P1) 🎯 MVP

**Goal**: an authorized caller sends a native ETH transfer and gets a tx hash; status is pollable.
**Independent test**: with an enabled testnet + funded wallet, POST a valid transfer → tx hash + "submitted";
GET status → pending→success. (Guardrail refusals are US3.)

- [ ] T018 [P] [US1] Write failing tests for the send happy-path + status mapping using a **fake `IEvmRpcClient`** (nonce/priority/base-fee/estimate canned → sign → broadcast returns hash; receipt pending/success/reverted) in `tests/Sorcha.Wallet.Service.Tests/EthereumTransactionServiceSendTests.cs`.
- [ ] T019 [US1] Add `IEthereumTransactionService` + `EthereumTransactionService` send path — gather nonce (`pending`), priority fee (fallback `DefaultPriorityFeeWei`), base fee, `maxFee = base×2 + priority`, gas estimate; build `EthereumTransactionRequest`; call `SignTransactionAsync`; broadcast; return `{ txHash, from, chainId, nonce, "submitted" }`. Fail-closed on any RPC/estimate error. In `src/Services/Sorcha.Wallet.Service/Services/{Interfaces,Implementation}/`.
- [ ] T020 [US1] Add the receipt/status lookup method (`GetTransactionReceiptAsync` → `pending|success|reverted` + blockNumber/gasUsed) to `EthereumTransactionService`.
- [ ] T021 [P] [US1] Write failing endpoint tests (happy-path send returns hash; status endpoint returns mapped status; 404 unknown wallet) in `tests/Sorcha.Wallet.Service.Tests/EthereumTransactionEndpointsTests.cs`.
- [ ] T022 [US1] Add `POST /api/v1/wallets/{walletAddress}/ethereum/transactions` (send) and `GET /api/v1/ethereum/transactions/{chainId}/{txHash}` (status) per contracts, with `.WithSummary`/`.WithDescription`, `Produces<>`, and BigInteger-safe decimal-string wei, in `src/Services/Sorcha.Wallet.Service/Endpoints/EthereumTransactionEndpoints.cs`; map in `Program.cs`. Until T021 passes.

**Checkpoint**: US1 independently demoable end-to-end on a testnet.

---

## Phase 4: User Story 3 — Enforce value-moving guardrails (Priority: P1)

**Goal**: refuse out-of-policy transfers with a clear reason and no broadcast; gate on `CanTransactEthereum`.
**Independent test**: each guardrail violation (disabled chain, mainnet-off, over-cap, over-ceiling,
missing permission) → refusal, nothing broadcast.

- [ ] T023 [P] [US3] Write failing policy tests (fake RPC asserts `SendRawTransaction` never called): chain-not-enabled, mainnet-not-allowed (chain not in known-testnet set + AllowMainnet=false), value-over-cap, fee-over-ceiling (refuse, not clamp-and-send), invalid-address, invalid-amount — each returns the mapped `reason` — in `tests/Sorcha.Wallet.Service.Tests/EthereumTransactionPolicyTests.cs`.
- [ ] T024 [US3] Implement the policy gate at the front of the send/preview flow in `EthereumTransactionService` (validate address/amount before any RPC; chain allowlist; mainnet gate; value cap; fee ceiling check after fee computation) returning a typed refusal (`RefusalProblem` reason) — until T023 passes.
- [ ] T025 [US3] Apply `.RequireAuthorization("CanTransactEthereum")` to the send + preview endpoints; add endpoint tests asserting 401 (unauthenticated) and 403 (missing permission) in `tests/Sorcha.Wallet.Service.Tests/EthereumTransactionEndpointsTests.cs`.
- [ ] T026 [US3] Map refusals to HTTP: policy/validation → 400 with `RefusalProblem`; RPC/estimate failure → 502 with `RefusalProblem` (nothing broadcast). Add observability counters `sorcha_eth_tx_submitted_total{chainId}`, `sorcha_eth_tx_rejected_total{reason}`, `sorcha_eth_tx_broadcast_failed_total{chainId}` on the `Sorcha.Ethereum` meter (structured logs, no interpolation).

**Checkpoint**: sends are safe — every guardrail enforced and tested; unauthorized callers denied.

---

## Phase 5: User Story 2 — Preview the cost before sending (Priority: P2)

**Goal**: read-only cost projection with zero on-chain effect.
**Independent test**: POST preview → nonce/gas/fees/total cost; fake RPC asserts no broadcast, no signing.

- [ ] T027 [P] [US2] Write failing preview tests (returns computed nonce/gas/maxFee/priority/value/estimatedTotalCost; `SendRawTransaction` and `SignTransactionAsync` never called; same policy gate applies) in `tests/Sorcha.Wallet.Service.Tests/EthereumTransactionPreviewTests.cs`.
- [ ] T028 [US2] Add the preview path to `EthereumTransactionService` (policy + gather chain params + compute `estimatedTotalCostWei = valueWei + gasLimit × maxFeePerGasWei`; no sign, no broadcast) until T027 passes.
- [ ] T029 [US2] Add `POST /api/v1/wallets/{walletAddress}/ethereum/transactions/preview` (`CanTransactEthereum`, `.WithSummary`/`.WithDescription`, `PreviewResponse`) in `EthereumTransactionEndpoints.cs`.

**Checkpoint**: all three stories complete; MVP + safety + preview.

---

## Phase 6: Polish & Cross-Cutting Concerns

- [ ] T030 [P] Add the composition/isolation test asserting the WASM PWA host (`Sorcha.Wallet.Pwa`) registers NO `IEthereumTransactionService` / write-capable RPC — the value-moving surface is server-side only — in `tests/Sorcha.Wallet.Service.Tests/ServerOnlyCompositionTests.cs` (or the PWA test project if that is where composition is asserted).
- [ ] T031 Run coverage on the four touched projects; confirm >85% on all new code (`Rlp`, `EthereumTransaction`, RPC methods, `SignTransactionAsync`, `EthereumTransactionService`, endpoints); add tests for any gap.
- [ ] T032 Run every touched regression suite green: `Sorcha.Cryptography.Secp256k1.Tests`, `Sorcha.ServiceClients.Tests`, `Sorcha.Wallet.Core.Tests`, `Sorcha.Wallet.Service.Tests`, and the Phase 1/2/2b verify suites (Verifier / Blueprint.Engine / Blueprint.Service) to prove prove-control + verify + DID resolution unchanged.
- [ ] T033 [P] Docs sweep: STANDARDS.md (EIP-1559 / EIP-2718 typed-tx / RLP rows with full repo-relative paths — run `scripts/check-discoverability.sh` locally), `cryptography` skill (RLP/tx encoder), `sorcha-architecture` skill (Wallet Service Ethereum-transacting endpoint surface), `src/Services/Sorcha.Wallet.Service/README.md`, `docs/reference/API-DOCUMENTATION.md` (3 endpoints).
- [ ] T034 [P] Update the initiative memory (`ethereum-vc-interop-initiative.md` + MEMORY.md) marking Phase 4 spec'd/planned/tasked; note the pure-managed decision and the feature-number collision check with the DCQL/EUDI `[182]` work.
- [ ] T035 Verify `dotnet build` (Release) has no new warnings; XML docs on all new public members; license headers + file-scoped namespaces on every new file.

---

## Dependencies & Execution Order

- **Setup (T001–T003)** → **Foundational (T004–T017)** → **US1 (T018–T022)** → **US3 (T023–T026)** → **US2 (T027–T029)** → **Polish (T030–T035)**.
- **Foundational is a hard gate**: no story task starts until the encoder (T004–T007), RPC (T008–T010), signing (T011–T015), and policy/DI (T016–T017) are done.
- **Story order**: US1 (P1, MVP send) before US3 (P1, guardrails harden the same service) before US2 (P2, preview reuses the gather path). US3 and US2 both depend only on US1's `EthereumTransactionService` existing.
- Within a phase, `[P]` tasks touch different files and can run in parallel; a test task `[P]` precedes its implementation task (same file pair) which is therefore not `[P]`.

## Parallel execution examples

- **Setup**: T002 + T003 together (distinct fixtures).
- **Foundational kick-off**: T004 (Rlp tests) ∥ T008 (RPC tests) ∥ T011 + T012 (value objects) ∥ T016 (options) — different projects/files.
- **US1**: T018 (service tests) ∥ T021 (endpoint tests) before their implementations.
- **Polish**: T030 ∥ T033 ∥ T034 (tests vs docs vs memory).

## Implementation strategy

- **MVP = Phase 1 + Phase 2 + Phase 3 (US1)** — a wallet can send + track a testnet transfer. Ship-review
  point.
- **Safety-complete = + Phase 4 (US3)** — do not enable beyond a controlled testnet until guardrails land.
- **Feature-complete = + Phase 5 (US2) + Phase 6**.
- TDD is mandatory for the encoder (T006 interop vector fails first) and the guardrails (T023 fails first);
  fail-closed and key-custody assertions are non-negotiable.

## Task summary

- **Total**: 35 tasks (T001–T035).
- **Setup**: 3 · **Foundational**: 14 · **US1**: 5 · **US3**: 4 · **US2**: 3 · **Polish**: 6.
- **Parallel opportunities**: ~13 tasks marked `[P]`.
- **Suggested MVP**: US1 (T001–T022).
