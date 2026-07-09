# Implementation Plan: Ethereum Address-Form Issuer DID Verification (Offline ecrecover)

**Branch**: `178-ethereum-ecrecover` | **Date**: 2026-07-09 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/178-ethereum-ecrecover/spec.md`

**Design doc**: [docs/superpowers/specs/2026-07-09-ethereum-verify-phase2-design.md](../../docs/superpowers/specs/2026-07-09-ethereum-verify-phase2-design.md)

## Summary

Extend Sorcha's Phase 1 ES256K verification (Feature 177) to **address-form issuer DIDs** — `did:pkh` (CAIP-10) and default-document (no-rotation) address-form `did:ethr` (ERC-1056) — whose DID document resolves **offline to a blockchain address only** (an `EcdsaSecp256k1RecoveryMethod2020` verification method carrying a `blockchainAccountId`, no `publicKeyJwk`). Verification is **recover-then-match**: `ecrecover` the secp256k1 public key from the JOSE ES256K signature (try recovery ids 0 and 1), derive its EIP-55 Ethereum address, and match it case-insensitively to the DID's address. A match **is** the proof.

**Technical approach (Approach 1 — "recovery-JWK envelope + single verify branch"):** the existing `Sorcha.Cryptography.Secp256k1` primitive gains `ecrecover` + a `VerifyByAddress` composing the already-shipped `Keccak256`/`EthereumAddress`. Two new offline DID resolvers (`PkhDidResolver`, `EthrDidResolver`) emit the standards-correct address-bearing verification method. The two issuer-key resolvers relax their `PublicKeyJwk is not null` gate to also accept an address-bearing VM, carrying the address forward in each path's existing key-material currency (a JWK-shaped `JsonElement` bearing `blockchainAccountId` for the Verifier engine; a new `IssuerKeyResolution.BlockchainAccountId` for the Blueprint service). The single ES256K verify branch in each path dispatches key-match (Phase 1) vs address-recovery. **No parallel verifier, no new resolver interface, no changed return contract, no new dependency, no trust code, no RPC, no signing.**

## Technical Context

**Language/Version**: C# 14 / .NET 10

**Primary Dependencies**: BouncyCastle.Cryptography (already in `Directory.Packages.props`) — the *only* crypto dependency the new recovery code uses. `System.Text.Json` for DID/JWK handling. No new package.

**Storage**: N/A (pure verification/resolution logic; no persistence)

**Testing**: xUnit v3 + FluentAssertions. New/extended test projects: `Sorcha.Cryptography.Secp256k1.Tests`, `Sorcha.ServiceClients.*Tests` (DID resolvers), `Sorcha.Verifier.Tests` (engine verify branch, InternalsVisibleTo), `Sorcha.Blueprint.Engine.Tests`/service tests (issuer-key resolver).

**Target Platform**: Cross-cutting libraries consumed by services **and** the Blazor WASM PWA (`Sorcha.Wallet.Pwa` via `Sorcha.Verifier.Engine`). The recovery primitive MUST stay pure-managed (BouncyCastle only) and WASM-safe.

**Project Type**: Multi-project .NET solution (libraries + services). Feature edits 4 existing projects; adds no new project.

**Performance Goals**: Verification is a per-credential, in-memory operation (one SHA-256 + at most two curve point recoveries + two keccak hashes). No throughput target beyond "not measurably slower than Phase 1 key-match".

**Constraints**: Fully **offline** — no network/RPC/node. Fail-closed by default. No new third-party dependency. No signing capability. No `WalletNetworks` change.

**Scale/Scope**: ~2 new primitive types (recovery + address-verify), 2 new DID resolvers, 1 new model property (+1 on `IssuerKeyResolution`), 2 relaxed resolver gates, 2 one-line verify branches, DI registration. Roughly a dozen source edits across 4 projects plus tests.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Assessment |
|---|---|
| **I. Microservices-First / dependency direction** | ✅ The primitive is a pure-managed leaf (`Sorcha.Cryptography.Secp256k1`) with no upward deps. Edits respect existing layering; no new cross-service coupling. Dependencies flow downward (Verifier.Engine / Blueprint.Service → primitive). |
| **II. Security First** | ✅ Fail-closed preserved (default reject for unlisted issuers; reject on any signature/recovery failure or malformed DID). No secrets. Verification-only — no signing surface exposed. Offline — no new external boundary. Input shape-validated (DID parsing rejects malformed). |
| **III. API Documentation** | ✅ No new HTTP endpoints. All new public types/methods get XML `<summary>`. (No Scalar/OpenAPI surface added.) |
| **IV. Testing** | ✅ TDD: primitive KATs first (known sign→recover→address vectors), then resolvers, then integration. >85% coverage target on new code; full regression suite must stay green. |
| **V. Code Quality** | ✅ Nullable enabled, async where I/O (resolvers are sync/offline — `Task.FromResult`), no new warnings, license headers, file-scoped namespaces, existing patterns followed. |
| **VI. Blueprint Standards** | ✅ N/A — no blueprint authoring changes; the reduced-assurance flag already exists (Phase 1). |
| **VII. Domain-Driven Design** | ✅ Reuses existing ubiquitous terms (issuer, credential, verification method, trust decision). New terms (`blockchainAccountId`, recovery method) are W3C/CAIP standard vocabulary. |
| **VIII. Observability** | ✅ Reuses the existing `DidResolverBackedIssuerKeyResolver` OTel counters (address-form resolution flows through the same `success`/`kid-unmatched` outcomes); structured logging (no interpolation) on resolver reject paths. |

**Result: PASS.** No violations; Complexity Tracking table not required.

## Project Structure

### Documentation (this feature)

```text
specs/178-ethereum-ecrecover/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output (library contracts, not REST)
│   ├── secp256k1-recovery.md
│   ├── did-resolvers.md
│   └── issuer-key-seam.md
├── checklists/
│   └── requirements.md  # (from /speckit.specify)
└── tasks.md             # Phase 2 output (/speckit.tasks — NOT created here)
```

### Source Code (repository root)

```text
src/Common/Sorcha.Cryptography.Secp256k1/          # (existing project — extend)
├── Secp256k1Recovery.cs        # NEW — ecrecover (recid 0/1) → candidate public keys
├── Secp256k1Verifier.cs        # EDIT — add VerifyByAddress (recover → EIP-55 → match)
├── EthereumAddress.cs          # (existing — reused; first runtime caller)
├── Keccak256.cs                # (existing — reused)
└── Secp256k1PublicKey.cs       # (existing — reused; recovery builds points here)

src/Common/Sorcha.ServiceClients.Http/Did/
├── PkhDidResolver.cs           # NEW — did:pkh:eip155:{chain}:0x… → recovery VM
├── EthrDidResolver.cs          # NEW — address-form did:ethr (default doc) → recovery VM
├── DidDocument.cs              # EDIT — VerificationMethod.BlockchainAccountId
└── ../Extensions/HttpServiceCollectionExtensions.cs  # EDIT — AddDidResolvers registers both

src/Common/Sorcha.Verifier.Engine/
├── DidResolverBackedIssuerKeyResolver.cs   # EDIT — accept address-VM → recovery-JWK JsonElement
└── VerifiablePresentationValidator.cs      # EDIT — VerifyEs256k address-recovery branch

src/Services/Sorcha.Blueprint.Service/Credentials/
└── DidX5cIssuerKeyResolver.cs  # EDIT — accept address-VM → IssuerKeyResolution.BlockchainAccountId
src/Core/Sorcha.Blueprint.Engine/Credentials/
├── IIssuerKeyResolver.cs       # EDIT — add string? BlockchainAccountId to IssuerKeyResolution
└── SdJwtVcFormatHandler.cs     # EDIT — pass recovery address to SdJwtService verify
src/Common/Sorcha.Cryptography/SdJwt/
└── SdJwtService.cs             # EDIT — optional issuerRecoveryAddress param → ES256K address-recovery branch in Verify

tests/  (mirrors — Secp256k1.Tests, ServiceClients tests, Verifier.Tests, Blueprint.Engine tests)
```

**Structure Decision**: Extend the four existing projects the Phase 1 design established; add **no** new project. The pure-managed primitive already exists and is the correct home for `ecrecover` (keccak/EIP-55 already live there). Exact insertion line numbers are re-located during implementation (code drifts) — see research.md.

## Complexity Tracking

*No constitution violations — table omitted.*
