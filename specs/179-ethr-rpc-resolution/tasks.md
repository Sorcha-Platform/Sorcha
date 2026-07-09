---
description: "Task list for Feature 179 — did:ethr on-chain resolution via read-only EVM RPC (ERC-1056)"
---

# Tasks: `did:ethr` On-Chain Resolution via Read-Only EVM RPC (ERC-1056)

**Input**: Design documents from `/specs/179-ethr-rpc-resolution/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md

**Tests**: TDD — test tasks precede implementation in every phase; each written to FAIL first.

**Organization**: By user story. Foundational (ABI codec + RPC client) blocks all; US1 is the end-to-end
MVP (rotation/delegate verify); US2 is the fail-closed-vs-offline safety rule; US3 preserves the offline
surfaces. All new code lives in `src/Common/Sorcha.ServiceClients.Http/Evm` + an edit to `Did/EthrDidResolver.cs`.

## Path Conventions

Multi-project .NET solution; source under `src/…`, tests under `tests/Sorcha.ServiceClients.Tests/…`
(+ Verifier/Blueprint suites for end-to-end). New test projects may need explicit
`using Xunit;`/`using FluentAssertions;`.

---

## Phase 1: Setup

- [ ] T001 Create `src/Common/Sorcha.ServiceClients.Http/Evm/` and confirm `Sorcha.ServiceClients.Http` + `tests/Sorcha.ServiceClients.Tests` build green as the baseline. No new project, no new package (BouncyCastle/Keccak via the existing `Sorcha.Cryptography.Secp256k1` reference; `System.Numerics` in-box).

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The pure-managed ABI codec + read-only RPC client every user story depends on.

**⚠️ CRITICAL**: ERC-1056 reads are impossible until the ABI machinery and RPC transport are correct.

- [ ] T002 [P] `AbiCodec` KAT tests in `tests/Sorcha.ServiceClients.Tests/Evm/AbiCodecTests.cs`: anchor with `EventTopic("Transfer(address,address,uint256)") == 0xddf252ad1be2c89b69c2b068fc378daa952ba7f163c4a11628f55a4df523b3ef` (canonical ERC-20); assert stable `Selector("changed(address)")` / `Selector("identityOwner(address)")`; compute the three ERC-1056 event topics from their signature strings; address/uint/bytes32 word round-trips; `Pad32Topic`. References `AbiCodec` (not implemented → FAIL).
- [ ] T003 [P] `EvmRpcClient` tests in `tests/Sorcha.ServiceClients.Tests/Evm/EvmRpcClientTests.cs` (mocked `HttpMessageHandler`): unconfigured chain → `NotConfigured` with **no** HTTP call; private/reserved host → `Error` (SSRF); timeout → `Error`; non-200 / JSON-RPC `error` member → `Error`; canned 200 result → `Ok(hex)`. FAIL.
- [ ] T004 Implement `Evm/AbiCodec.cs` — `Selector`/`EventTopic` via `Keccak256`; `EncodeAddress`/`DecodeAddress`/`DecodeUInt`(`BigInteger`)/`DecodeBytes32`/`DecodeBytes`/`Pad32Topic`. Make T002 pass.
- [ ] T005 Implement `Evm/EvmRpcOptions.cs` (bind `DidResolver:Ethr:Rpc:{chainId}`, `…:RegistryAddress:{chainId}` default `0xdca7ef03e98e0dc2b855be647c39abe984fcf21b`, `…:MaxHistoryHops` default 128, shared `DidResolver:AllowPrivateAddresses`) + the 3-outcome result types `EvmCallResult`/`EvmLogsResult` (`NotConfigured` | `Error` | `Ok`).
- [ ] T006 Implement `Evm/IEvmRpcClient.cs` + `Evm/EvmRpcClient.cs` — `eth_call`/`eth_getLogs` over `HttpClient`, per-chain URL from options, SSRF guard (extract `WebDidResolver.IsPrivateOrReservedAddress` to a shared helper), 5s timeout; every failure → `Error` (never throws). Make T003 pass.
- [ ] T007 Run `dotnet test tests/Sorcha.ServiceClients.Tests` — foundational green. **Commit** ("feat: [179] ABI codec + read-only EVM RPC client (SSRF-guarded, 3-outcome)").

**Checkpoint**: Selectors/topics are correct and RPC calls resolve to `NotConfigured`/`Error`/`Ok`.

---

## Phase 3: User Story 1 — Verify a rotated / delegate-signed `did:ethr` issuer (Priority: P1) 🎯 MVP

**Goal**: Resolve the current on-chain document and verify a credential against the current owner,
unexpired `veriKey`/`sigAuth` delegates, and published `did/pub/*` keys.

**Independent Test**: Fake RPC returns a rotation + an active delegate + a published key; a credential
signed by any current authority → Pass; by a former owner / expired delegate → Reject.

### Tests for User Story 1 ⚠️ (write first, must FAIL)

- [ ] T008 [P] [US1] `Erc1056Registry` tests (`tests/Sorcha.ServiceClients.Tests/Evm/Erc1056RegistryTests.cs`, fake `IEvmRpcClient`): `changed==0` → `NoHistory`; a `DIDOwnerChanged` → new `OwnerAddress`; an active `veriKey` `DIDDelegateChanged` → one delegate, an **expired** one → none; `did/pub/Secp256k1/veriKey/hex` → one Secp256k1 attr, `did/pub/Ed25519/veriKey/base64` → one Ed25519 attr, `did/svc/*` + `did/pub/X25519/enc/*` → ignored; supersession keeps newest; `previousChange` walk terminates at 0.
- [ ] T009 [P] [US1] `EthrDidResolver` RPC-path tests (`tests/Sorcha.ServiceClients.Tests/Did/EthrDidResolverRpcTests.cs`, fake RPC): rotated DID → `{did}#controller` recovery VM with the **current** owner's `blockchainAccountId` in `assertionMethod`; `veriKey` delegate → `#delegate-n` recovery VM in `assertionMethod`; Secp256k1 attr → `publicKeyJwk` EC VM; `Ok` state → multi-VM document.
- [ ] T010 [P] [US1] End-to-end verify tests (Verifier + Blueprint engine suites, fake RPC): ES256K credential signed by the **current owner** → **Pass**; by the **former owner** → **Reject**; by an unexpired **`veriKey` delegate** → **Pass**; by an **expired** delegate → **Reject**; by a published **Secp256k1** key → **Pass**.

### Implementation for User Story 1

- [ ] T011 [US1] Implement `Evm/Erc1056Registry.cs` — `ReadAsync(chainId, identity)`: `changed` → `identityOwner` → `eth_getLogs` walk by `previousChange`; decode `DIDOwnerChanged`/`DIDDelegateChanged`/`DIDAttributeChanged` via `AbiCodec`; newest-first fold with `validTo >= now`; `MaxHistoryHops` guard; return `Erc1056State`. Make T008 pass.
- [ ] T012 [US1] Edit `Did/EthrDidResolver.cs` — add the `IEvmRpcClient? rpc = null` ctor seam; when `rpc != null` and `Ok`, build the multi-VM current document (owner `#controller` recovery VM in auth+assertion; `veriKey`→assertion, `sigAuth`→auth recovery VMs; Secp256k1→EC `publicKeyJwk`, Ed25519→OKP `publicKeyJwk`, relationship per purpose). Make T009 pass.
- [ ] T013 [US1] Confirm the resolved multi-VM document flows unchanged through `DidResolverBackedIssuerKeyResolver`/`DidX5cIssuerKeyResolver` + the ES256K verify branches (kid-match → key-match / recover-then-match; assertionMethod gate filters). Wire the fake-RPC harness in the end-to-end tests. Make T010 pass.
- [ ] T014 [US1] Run the ServiceClients + Verifier + Blueprint engine suites — US1 green. **Commit** ("feat: [179] ERC-1056 registry read + EthrDidResolver current-document builder (US1)").

**Checkpoint**: A rotated/delegate-signed `did:ethr` credential verifies against currently-authorised keys end-to-end.

---

## Phase 4: User Story 2 — Safe behaviour when the provider is unavailable (Priority: P1)

**Goal**: Configured-but-errored RPC fails closed (reject); unconfigured / no-history uses the offline
default document. No stale-document acceptance, ever.

**Independent Test**: Fake RPC errors → reject; unconfigured chain → default doc; `changed==0` → default doc.

### Tests for User Story 2 ⚠️ (write first, must FAIL / then confirm)

- [ ] T015 [P] [US2] Safety tests (`EthrDidResolverRpcTests` + `Erc1056RegistryTests`): a **configured** RPC returning `Error` (from `changed`, `identityOwner`, or a `getLogs` hop) → `Erc1056State.RpcError` → resolver returns **null** (reject); an **unconfigured** chain → `NoHistory` → default document; `changed==0` → default document; `MaxHistoryHops` exceeded → `RpcError` → reject.
- [ ] T016 [US2] Ensure `EthrDidResolver` maps `RpcError → null` and `NoHistory → default document` (add/verify the branch; the two outcomes must be provably distinct). Make T015 pass.
- [ ] T017 [US2] Run the safety tests + full ServiceClients/Verifier/Blueprint suites — US2 green, no regression. **Commit** ("feat: [179] fail-closed-on-RPC-error vs offline-when-unconfigured (US2)").

**Checkpoint**: The provider-unavailable path never trusts a stale key; the unconfigured path stays offline.

---

## Phase 5: User Story 3 — The offline surfaces are unaffected (Priority: P2)

**Goal**: The WASM PWA verifier (no `IEvmRpcClient` registered) resolves `did:ethr` offline with zero
network calls; `did:pkh` unchanged.

**Independent Test**: Compose `EthrDidResolver` with `rpc == null` → default document, assert the fake RPC is never invoked.

### Tests for User Story 3 ⚠️ (write first, must FAIL / then confirm)

- [ ] T018 [P] [US3] Offline test: `new EthrDidResolver(logger, rpc: null).ResolveAsync(did:ethr…)` → Phase-2 default document; a spy `IEvmRpcClient` records **zero** calls; `did:pkh` resolution unchanged.
- [ ] T019 [US3] Register `IEvmRpcClient` + bind `EvmRpcOptions` in a **server-only** path (e.g. within `AddServiceClients`/a new `AddEvmRpc`, **not** inside `AddDidResolvers` which the WASM PWA also calls); register `EthrDidResolver` via a factory that resolves `sp.GetService<IEvmRpcClient>()` (null in WASM). Confirm the WASM composition leaves `rpc` null. Make T018 pass.
- [ ] T020 [US3] Run US3 + regression. **Commit** ("feat: [179] server-only EVM RPC registration; offline PWA path preserved (US3)").

**Checkpoint**: All three stories pass; the offline invariant holds.

---

## Phase 6: Polish & Cross-Cutting Concerns

- [ ] T021 [P] Update `STANDARDS.md` — `did:ethr` row → "offline default doc (F178) **+ on-chain current document via read-only EVM RPC (F179)**"; note read-only, server-side.
- [ ] T022 [P] Update the `verifiable-credentials` skill — append a Phase-2b note (on-chain `did:ethr` resolution, server-only, fail-closed, `IEvmRpcClient`/`Erc1056Registry`).
- [ ] T023 Update memory: `ethereum-vc-interop-initiative.md` (Phase 2b status + PR#) and the MEMORY.md Active-work line, per the frontmatter convention.
- [ ] T024 Whole-solution Release build (zero new warnings) + full touched-suite regression: `Sorcha.ServiceClients.Tests`, `Sorcha.Verifier.Tests`, Blueprint engine/service tests. All green; `did:pkh` + Phase-1/2 unaffected.
- [ ] T025 Run `specs/179-ethr-rpc-resolution/quickstart.md` validation (the three `dotnet test` invocations) as the acceptance gate. **Commit** ("docs: [179] standards, skill, memory + regression gate").

---

## Dependencies & Execution Order

- **Setup (P1)** → **Foundational (P2, blocks all)** → **US1 (P3, MVP)** → **US2 (P4)** → **US3 (P5)** → **Polish (P6)**.
- US2 reuses US1's resolver + the foundational 3-outcome result; adds the safety branch + tests.
- US3 depends on the resolver seam (US1) + registration; adds the offline guarantee.

### Within each story
Tests written first and made to FAIL, then implementation. ABI/RPC before registry before resolver before end-to-end.

### Parallel opportunities
- T002/T003 (foundational tests) parallel; T008/T009/T010 (US1 tests) parallel; T021/T022 (docs) parallel.
- Sequential on shared files: T011→T012→T013 (registry → resolver → wiring); T015→T016 (EthrDidResolver).

---

## Implementation Strategy

**MVP = Setup + Foundational + US1** — resolves the current on-chain document and verifies a
rotated/delegate-signed `did:ethr` credential end-to-end. US2 adds the fail-closed safety rule (P1,
security-critical) and US3 preserves the offline PWA. Commit per phase; the touched suites are the
fail-closed regression signal throughout.
