# Implementation Plan: `did:ethr` On-Chain Resolution via Read-Only EVM RPC (ERC-1056)

**Branch**: `179-ethr-rpc-resolution` | **Date**: 2026-07-09 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/179-ethr-rpc-resolution/spec.md`

**Design doc**: [docs/superpowers/specs/2026-07-09-ethereum-verify-phase2b-design.md](../../docs/superpowers/specs/2026-07-09-ethereum-verify-phase2b-design.md)

## Summary

Realise the `IEvmRpcClient` seam designed-in at Phase 2 (F178): resolve the **current** `did:ethr`
document from ERC-1056 registry state over **read-only EVM RPC** (`eth_call` + `eth_getLogs` only — no
node, no wallet, no write) so ES256K issuer signatures verify against currently-authorised keys — the
current owner (after rotation), unexpired `veriKey`/`sigAuth` delegates, and unexpired `did/pub/*` key
attributes, each honouring `validTo`.

**Technical approach.** A pure-managed JSON-RPC client (`EvmRpcClient`) + a minimal ABI codec
(`AbiCodec`, selectors/topics via the existing `Keccak256`) + the ERC-1056 read/event-walk
(`Erc1056Registry`) live in `Sorcha.ServiceClients.Http`. `EthrDidResolver` gains the optional
`IEvmRpcClient? rpc = null` seam and a **multi-VM current-document builder** that maps each signing
entity to an **existing** verification-method shape — owner + address delegates →
`EcdsaSecp256k1RecoveryMethod2020` `blockchainAccountId` VM (Phase-2 recover-then-match), published
key attributes → `publicKeyJwk` VM (Phase-1 key-match) — placed in `assertionMethod`/`authentication`
per the delegate purpose. **No verify-seam change, no trust change, no new dependency, server-side
only, fail-closed on configured-RPC error.**

## Technical Context

**Language/Version**: C# 14 / .NET 10

**Primary Dependencies**: `System.Text.Json` (JSON-RPC + DID docs), `System.Numerics.BigInteger` (ABI
words), the existing `Sorcha.Cryptography.Secp256k1` primitive (`Keccak256` for selectors/topics,
`Secp256k1PublicKey`/`Secp256k1Jwk` for attribute key → JWK). **No new NuGet package. No Nethereum.**

**Storage**: N/A (resolution logic; existing `DidResolverCache` applies unchanged).

**Testing**: xUnit v3 + FluentAssertions. New: `AbiCodec` unit tests, `Erc1056Registry` tests (fake
`IEvmRpcClient`), `EthrDidResolver` RPC-path tests, `EvmRpcClient` SSRF/timeout tests, end-to-end
verify (fake RPC) in the Verifier/Blueprint suites.

**Target Platform**: Server hosts only for the RPC path (`IEvmRpcClient` registered server-side). The
code is pure-managed/WASM-safe, but the WASM PWA deliberately does **not** register it (`rpc == null`
⇒ Phase-2 offline default doc).

**Project Type**: Multi-project .NET solution. Feature edits one project (`Sorcha.ServiceClients.Http`)
+ server-host DI registration; no new project.

**Performance Goals**: One `eth_call` (`changed`) + one `eth_call` (`identityOwner`) + N `eth_getLogs`
(N = number of change-blocks in the DID's history, usually small) per uncached resolution; cached by
`DidResolverCache`. 5s per-request timeout. No throughput target beyond "not slower than a `did:web`
fetch".

**Constraints**: Read-only (2 RPC methods). Fail-closed on configured-RPC error. SSRF-guarded. No
signing, no `WalletNetworks` change. Server-side only.

**Scale/Scope**: 1 new RPC client interface + impl, 1 ABI codec, 1 ERC-1056 reader, `EthrDidResolver`
RPC path + multi-VM builder, config binding, server-host registration. ~5 new source files in one
project + tests.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Assessment |
|---|---|
| **I. Microservices-First / deps** | ✅ Additive to a shared client library; the RPC path is registered only in server hosts. No upward deps; WASM offline invariant preserved. |
| **II. Security First** | ✅ **Fail-closed on configured-RPC error** (no stale-doc fallback) is the central rule. SSRF-guarded (reuses `WebDidResolver`'s host-allow check), request timeout, read-only (no write, no key). No secrets (RPC URLs are config; public endpoints). Input-validated (DID shape, ABI decode defensive). |
| **III. API Documentation** | ✅ No new HTTP endpoints. XML `<summary>` on all new public types. |
| **IV. Testing** | ✅ TDD: ABI selector/topic KATs first, then registry-walk against a fake RPC, then resolver + end-to-end. >85% on new code; every prior suite stays green. |
| **V. Code Quality** | ✅ Nullable, async I/O, no new warnings, license headers, file-scoped namespaces, existing resolver/HTTP patterns followed. |
| **VI. Blueprint Standards** | ✅ N/A. |
| **VII. Domain-Driven Design** | ✅ Existing DID/VM/issuer vocabulary; new terms (ERC-1056, delegate, blockchainAccountId) are external standard vocabulary. |
| **VIII. Observability** | ✅ Structured logging on RPC failures/SSRF blocks/fail-closed (no interpolation). Resolution flows through the existing `DidResolverMetrics`/issuer-resolve counters unchanged. |

**Result: PASS.** No violations; Complexity Tracking omitted.

## Project Structure

### Documentation (this feature)

```text
specs/179-ethr-rpc-resolution/
├── plan.md, research.md, data-model.md, quickstart.md
├── contracts/
│   ├── evm-rpc-client.md
│   ├── erc1056-registry.md
│   └── ethr-resolver-document.md
├── checklists/requirements.md
└── tasks.md   (/speckit.tasks — not created here)
```

### Source Code (repository root)

```text
src/Common/Sorcha.ServiceClients.Http/
├── Evm/
│   ├── IEvmRpcClient.cs        # NEW — read-only JSON-RPC seam (eth_call, eth_getLogs)
│   ├── EvmRpcClient.cs         # NEW — HttpClient impl, SSRF-guarded, per-chain URL from config
│   ├── EvmRpcOptions.cs        # NEW — bound config (chain→URL, chain→registry override, timeout)
│   ├── AbiCodec.cs             # NEW — selectors/topics via Keccak256; 32-byte word encode/decode
│   └── Erc1056Registry.cs      # NEW — changed/identityOwner + event walk → owner/delegates/attributes
├── Did/
│   └── EthrDidResolver.cs      # EDIT — add IEvmRpcClient? seam + multi-VM current-document builder
└── Extensions/
    └── HttpServiceCollectionExtensions.cs  # EDIT — register IEvmRpcClient (server hosts) + bind options

tests/Sorcha.ServiceClients.Tests/Evm/          # AbiCodec, Erc1056Registry (fake RPC), EvmRpcClient SSRF
tests/Sorcha.ServiceClients.Tests/Did/          # EthrDidResolver RPC-path tests (fake RPC)
tests/Sorcha.Verifier.Tests + Blueprint tests   # end-to-end verify of a rotated/delegate-signed did:ethr
```

**Structure Decision**: One project (`Sorcha.ServiceClients.Http`) already owns DID resolvers and the
SSRF-guarded `WebDidResolver`; the EVM RPC client + ERC-1056 reader belong beside them in a new `Evm/`
folder. Server-only registration keeps the WASM PWA offline. No new project; the `IEvmRpcClient` seam
was already reserved on `EthrDidResolver` in Phase 2.

## Complexity Tracking

*No constitution violations — table omitted.*
