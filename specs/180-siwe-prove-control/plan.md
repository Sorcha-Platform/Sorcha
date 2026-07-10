# Implementation Plan: SIWE / Prove-Control — Ethereum Address & secp256k1 Signing

**Branch**: `180-siwe-prove-control` | **Date**: 2026-07-10 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/180-siwe-prove-control/spec.md`

**Design doc**: [docs/superpowers/specs/2026-07-10-ethereum-verify-phase3-design.md](../../docs/superpowers/specs/2026-07-10-ethereum-verify-phase3-design.md)

## Summary

The first Ethereum **signing** phase: a Sorcha wallet exposes an Ethereum address (EIP-55) derived from
its existing HD seed at `m/44'/60'/0'/0/{index}` as an **auxiliary** identity, signs an EIP-191
`personal_sign` / SIWE (EIP-4361) prove-control message with a recoverable secp256k1 signature, and
Sorcha verifies inbound SIWE proofs (relying-party dual). **No `WalletNetworks` change** — the ETH key
is derived directly via NBitcoin, orthogonal to the wallet's primary algorithm. **Prove-control only** —
the signer refuses transaction-shaped payloads and exposes no raw-digest signing or key export.

**Technical approach.** The pure-managed `Sorcha.Cryptography.Secp256k1` primitive gains
`Secp256k1Signer` (RFC-6979 deterministic-`k`, low-s canonical, 65-byte `r‖s‖v`), `Eip191`
(personal-sign digest), and a `Siwe/` namespace (message model + formatter + parser + verifier reusing
`Secp256k1Recovery`). `IKeyManagementService` gains a raw-secp256k1 derivation (NBitcoin derive **without**
the primary-algorithm `GenerateKeySet`). A new `IEthereumIdentityService` (Wallet.Core) decrypts the
wallet's master seed via the existing `DecryptPrivateKeyAsync` path, derives the ETH key on demand,
exposes the address, and signs — with a transaction-payload guard. Thin Wallet Service endpoints expose
address / SIWE-sign / SIWE-verify.

## Technical Context

**Language/Version**: C# 14 / .NET 10

**Primary Dependencies**: BouncyCastle (RFC-6979 ECDSA + keccak — already referenced), NBitcoin (BIP32
derivation — already referenced), `System.Text.Json`. **No new package.**

**Storage**: N/A — the ETH key is derived on demand from the wallet's existing encrypted seed; no new
persisted entity.

**Testing**: xUnit v3 + FluentAssertions. New: `Secp256k1.Tests` (signer, EIP-191, SIWE round-trip +
published vector), `Sorcha.Wallet.Core.Tests` (`EthereumIdentityService` derive/sign/guard), Wallet
Service endpoint tests.

**Target Platform**: Pure-managed primitive → runs on the server **and** the Blazor WASM PWA wallet
(FR-010). The wallet identity service runs where the seed is (Wallet Service / holder wallet).

**Project Type**: Multi-project .NET solution. Edits the primitive + Wallet.Core + Wallet.Service; no new
project.

**Performance Goals**: One HD derivation + one ECDSA sign per prove-control operation; no throughput
target beyond "comparable to existing wallet signing".

**Constraints**: Prove-control only (no transactions, no raw-digest sign, no key export); deterministic
low-s signatures; same custody/auth as existing wallet signing; no `WalletNetworks` change; no new
dependency; WASM-safe primitive.

**Scale/Scope**: ~3 primitive types + a SIWE namespace, 1 key-management method, 1 wallet service +
interface, 3 thin endpoints, tests.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Assessment |
|---|---|
| **I. Microservices-First / deps** | ✅ Additive; the signer is a pure leaf, the identity service sits in Wallet.Core beside existing key management. No new cross-service coupling. |
| **II. Security First** | ✅ The central concern. Key **never exported/returned/logged**; derived on demand from the encrypted seed, used, discarded (same custody as existing signing). **Prove-control only** — transaction-payload guard + no raw-digest API. Deterministic RFC-6979 `k` (no nonce-reuse leak) + low-s. Signing needs the same auth as any wallet op. Input-validated (SIWE parse fail-closed). |
| **III. API Documentation** | ✅ 3 new Minimal-API endpoints with `.WithSummary`/`.WithDescription`; XML docs on new public types. |
| **IV. Testing** | ✅ TDD: signer/EIP-191/SIWE KATs (incl. a published personal-sign/SIWE vector) first, then the identity service + guard, then endpoints. >85% new-code coverage; all prior suites green. |
| **V. Code Quality** | ✅ Nullable, async I/O, no new warnings, license headers, file-scoped namespaces, existing wallet/crypto patterns. |
| **VI. Blueprint Standards** | ✅ N/A. |
| **VII. Domain-Driven Design** | ✅ Existing wallet/key vocabulary; new terms (SIWE, EIP-191, prove-control) are external standard vocabulary. |
| **VIII. Observability** | ✅ Structured logging on sign/verify (never the key/seed); reuses wallet-operation telemetry. |

**Result: PASS.** No violations; Complexity Tracking omitted.

## Project Structure

### Documentation (this feature)

```text
specs/180-siwe-prove-control/
├── plan.md, research.md, data-model.md, quickstart.md
├── contracts/
│   ├── secp256k1-signer-eip191.md
│   ├── siwe.md
│   └── ethereum-identity-service.md
├── checklists/requirements.md
└── tasks.md   (/speckit.tasks — not created here)
```

### Source Code (repository root)

```text
src/Common/Sorcha.Cryptography.Secp256k1/
├── Secp256k1Signer.cs      # NEW — recoverable RFC-6979 low-s sign → 65-byte r‖s‖v
├── Eip191.cs               # NEW — personal_sign digest
└── Siwe/
    ├── SiweMessage.cs      # NEW — EIP-4361 fields
    ├── SiweFormatter.cs    # NEW — Format / TryParse (ABNF text)
    └── SiweVerifier.cs     # NEW — parse → recover → address-match → nonce/domain/window

src/Core/Sorcha.Wallet.Core/
├── Services/Interfaces/IEthereumIdentityService.cs   # NEW
├── Services/Implementation/EthereumIdentityService.cs# NEW — derive-on-demand, address, sign, tx-guard
└── (IKeyManagementService + KeyManagementService)    # EDIT — DeriveSecp256k1KeyAtPathAsync (raw, no GenerateKeySet)

src/Services/Sorcha.Wallet.Service/Endpoints/
└── EthereumEndpoints.cs                              # NEW — GET ethereum-address, POST siwe/sign, POST siwe/verify

tests/  — Secp256k1.Tests (signer/eip191/siwe), Wallet.Core.Tests (identity service + guard), Wallet.Service.Tests (endpoints)
```

**Structure Decision**: The signer + SIWE join the existing pure-managed primitive (WASM-safe, so the
PWA wallet signs on-device). Raw-secp256k1 derivation is a new `IKeyManagementService` method beside the
existing `DeriveKeyAtPathAsync` (reuses the NBitcoin path, skips the primary-algorithm re-derivation).
The identity service lives in Wallet.Core where the seed-decryption + repository already are; endpoints
are thin. No new project; no `WalletNetworks` member.

## Complexity Tracking

*No constitution violations — table omitted.*
