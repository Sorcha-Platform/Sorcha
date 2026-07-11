# Implementation Plan: Ethereum Transacting — Phase 4 (Native ETH Transfers)

**Branch**: `182-ethereum-transacting` | **Date**: 2026-07-11 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/182-ethereum-transacting/spec.md`

**Design source of truth**: [`docs/superpowers/specs/2026-07-11-ethereum-verify-phase4-design.md`](../../docs/superpowers/specs/2026-07-11-ethereum-verify-phase4-design.md)

## Summary

Give a Sorcha wallet the ability to **send a native ETH transfer** — the first value-moving capability of
the 4-phase Ethereum roadmap. The Wallet Service gathers live chain parameters (nonce, base fee, priority
fee, gas estimate) over a write-capable EVM RPC, the wallet's auxiliary secp256k1 identity (Phase 3,
derived on-demand) signs a fully-specified **EIP-1559 (type-2)** transaction, and the service broadcasts it
and returns the transaction hash (fire-and-report-hash). All sends are gated by policy: a chain allowlist,
a `AllowMainnet` master switch (default off), a per-transaction value cap, a `maxFeePerGas` ceiling, and a
distinct `CanTransactEthereum` authorization policy. Transaction encoding (RLP + the type-2 envelope) is
**hand-rolled pure-managed** in the existing `Sorcha.Cryptography.Secp256k1` primitive — no Nethereum, no
new dependency — reusing `Keccak256` and `Secp256k1Signer`, anchored by a published interop vector.

## Technical Context

**Language/Version**: C# 14 / .NET 10

**Primary Dependencies**: BouncyCastle (already referenced, in the primitive); NBitcoin (already referenced,
HD derivation); `System.Text.Json`; `System.Numerics.BigInteger`. **No new third-party dependency** (no
Nethereum).

**Storage**: N/A — no new persistence. Transactions are broadcast to the network; nonce is read live
(`eth_getTransactionCount` pending). No local transaction store this phase.

**Testing**: xUnit v3 + FluentAssertions + Moq. Fake `IEvmRpcClient` for service tests; published EIP-1559
known-key→known-raw-tx+hash interop vector for the encoder; existing primitive KAT pattern reused.

**Target Platform**: Server-side only (`Sorcha.Wallet.Service` host). The encoder is pure-managed and
WASM-safe by construction but is **not wired** into the WASM PWA.

**Project Type**: Distributed .NET service platform (extends existing crypto primitive, HTTP service-client
library, wallet Core library, and Wallet Service).

**Performance Goals**: A single send performs a bounded set of RPC round-trips (nonce, priority fee, base
fee, gas estimate, broadcast) and returns immediately without blocking on confirmation. No throughput
target beyond existing wallet-endpoint norms; testnet, low-volume use.

**Constraints**: Fail-closed on any RPC/estimation/policy failure (no blind or partial broadcast); private
key derived-used-cleared, never returned/logged; `Sorcha.Wallet.Core` MUST NOT depend on
`Sorcha.ServiceClients.Http`; no `WalletNetworks`/primary-algorithm change; prove-control guard untouched.

**Scale/Scope**: ~4 projects touched. New: `Rlp`, `EthereumTransaction`, `EthereumTransactionRequest`,
`SignedEthereumTransaction`, `EthereumTransactionService`, `EthereumTransactionOptions`, `CanTransactEthereum`
policy, 3 endpoints (send / status / preview). Extend: `IEvmRpcClient`/`EvmRpcClient` (7 methods),
`IEthereumIdentityService`/`EthereumIdentityService` (`SignTransactionAsync`).

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|---|---|---|
| I. Microservices-First / layered deps | **PASS** | `Sorcha.Wallet.Core` (Core layer) gains **no** dependency on `Sorcha.ServiceClients.Http` (Infra); it builds+signs a fully-specified deterministic tx. RPC fetch/broadcast stay in `Sorcha.Wallet.Service`. Dependencies flow downward only. |
| II. Security First | **PASS** | Key derived-used-cleared, never returned/logged (Phase 3 custody). Value-moving guardrails (chain allowlist, `AllowMainnet` gate, value cap, fee ceiling, `CanTransactEthereum` policy). Input validation on recipient/amount before any network call. Fail-closed everywhere. SSRF-guarded RPC reused. |
| III. API Documentation | **PASS** | New endpoints use .NET 10 OpenAPI with `.WithSummary`/`.WithDescription`; XML docs on all new public members. No Swagger. |
| IV. Testing (>85% new code) | **PASS (planned)** | Unit tests for `Rlp`/`EthereumTransaction` (incl. published interop vector), `IEvmRpcClient` write methods (fake HTTP), `EthereumTransactionService` (fake RPC — policy/flow/receipt/preview), `SignTransactionAsync`, endpoints, composition (PWA registers nothing), and prior-phase regression. |
| V. Code Quality | **PASS** | Nullable enabled; async/await; DI; no Release warnings; license header + file-scoped namespaces. |
| VII. Domain-Driven Design | **PASS** | New value objects (`EthereumTransactionRequest`, `SignedEthereumTransaction`) and a focused service; consistent naming. Not a Blueprint/Action domain concept — orthogonal auxiliary Ethereum identity. |
| VIII. Observability | **PASS** | Structured logging (no interpolation) + OpenTelemetry counters `sorcha_eth_tx_submitted_total{chainId}`, `sorcha_eth_tx_rejected_total{reason}`, `sorcha_eth_tx_broadcast_failed_total{chainId}`. |

**No violations.** Complexity Tracking not required.

## Project Structure

### Documentation (this feature)

```text
specs/182-ethereum-transacting/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output (OpenAPI for the 3 endpoints)
├── checklists/
│   └── requirements.md  # Spec quality checklist (from /speckit.specify)
└── tasks.md             # Phase 2 output (/speckit.tasks — NOT created here)
```

### Source Code (repository root)

```text
src/Common/Sorcha.Cryptography.Secp256k1/
├── Rlp.cs                              # NEW — minimal RLP encoder (bytes/list/minimal-integer)
├── EthereumTransaction.cs              # NEW — EIP-1559 type-2 build unsigned payload + assemble signed
├── Keccak256.cs                        # reused
├── Secp256k1Signer.cs                  # reused (v∈{27,28} → yParity=v−27)
└── EthereumAddress.cs                  # reused

src/Common/Sorcha.ServiceClients.Http/Evm/
├── IEvmRpcClient.cs                    # EDIT — add write+query method signatures
└── EvmRpcClient.cs                     # EDIT — implement over existing SendAsync core

src/Core/Sorcha.Wallet.Core/
├── Services/Interfaces/IEthereumIdentityService.cs   # EDIT — add SignTransactionAsync
├── Services/Implementation/EthereumIdentityService.cs# EDIT — sanctioned tx path (guard untouched)
└── Domain/ValueObjects/
    ├── EthereumTransactionRequest.cs   # NEW — fully-specified deterministic tx request
    └── SignedEthereumTransaction.cs    # NEW — { RawTxHex, TxHash, From }

src/Services/Sorcha.Wallet.Service/
├── Services/Interfaces/IEthereumTransactionService.cs      # NEW
├── Services/Implementation/EthereumTransactionService.cs   # NEW — policy + RPC + broadcast orchestrator
├── Configuration/EthereumTransactionOptions.cs             # NEW — policy config (Ethereum:Transactions)
├── Endpoints/EthereumTransactionEndpoints.cs               # NEW — send / status / preview
├── Extensions/…                                            # EDIT — register service, options, IEvmRpcClient, CanTransactEthereum policy
└── Program.cs                                              # EDIT — wire the above + MapEthereumTransactionEndpoints

tests/
├── Sorcha.Cryptography.Secp256k1.Tests/     # Rlp + EthereumTransaction (incl. interop vector)
├── Sorcha.ServiceClients.Tests/             # IEvmRpcClient write methods (fake HTTP)
├── Sorcha.Wallet.Core.Tests/                # SignTransactionAsync + guard-still-refuses
└── Sorcha.Wallet.Service.Tests/             # EthereumTransactionService + endpoints + composition
```

**Structure Decision**: Extend the four existing projects that already own each concern (primitive →
encoding/signing; ServiceClients.Http/Evm → RPC; Wallet.Core → key/tx-build; Wallet.Service →
policy/orchestration/endpoints). No new project. This preserves the Phase-1→3 layering and the
Core-has-no-Infra-dependency invariant.

## Complexity Tracking

> No constitution violations — section intentionally empty.
