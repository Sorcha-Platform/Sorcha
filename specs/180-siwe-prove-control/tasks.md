---
description: "Task list for Feature 180 — SIWE / prove-control (secp256k1 signing)"
---

# Tasks: SIWE / Prove-Control — Ethereum Address & secp256k1 Signing

**Input**: Design documents from `/specs/180-siwe-prove-control/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md

**Tests**: TDD — test tasks precede implementation in every phase; each written to FAIL first.

**Organization**: By user story. Foundational (signer + EIP-191 + SIWE message) blocks all; US1 is the
end-to-end MVP (wallet proves control / produce); US2 is verification (Sorcha as RP); US3 confines the
key to prove-control. New code: the primitive + Wallet.Core + Wallet.Service.

## Path Conventions

Multi-project .NET solution; source under `src/…`, tests under `tests/…`.

---

## Phase 1: Setup

- [X] T001 Confirm `tests/Sorcha.Cryptography.Secp256k1.Tests` builds green as baseline; confirm `Sorcha.Wallet.Core` references the primitive + NBitcoin (both already present — no new project, no new package).

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The recoverable signer, EIP-191 digest, and SIWE message text every story depends on.

**⚠️ CRITICAL**: Ethereum ECDSA hashes with **keccak** (not SHA-256), so the digest-level recovery + signer must be correct before anything else.

- [X] T002 [P] Signer + recovery + EIP-191 KAT tests in `tests/Sorcha.Cryptography.Secp256k1.Tests`: `Secp256k1Signer.SignRecoverable(digest, key)` → `Secp256k1Recovery.RecoverFromDigest(digest, r, s, v-27)` equals the signer's key and its EIP-55 address; `s ≤ n/2`; `v ∈ {27,28}`; deterministic (same input → same 65 bytes); `Eip191.PersonalSignDigest("abc")` == independently-built `Keccak256("\x19Ethereum Signed Message:\n3abc")`. FAIL.
- [X] T003 [P] SIWE format/parse tests: EIP-4361 **spec example** message round-trips (parse→format byte-identical); `Format`↔`TryParse` for messages with/without each optional field; missing required field → `TryParse` false. FAIL.
- [X] T004 Refactor `Secp256k1Recovery` to a shared private `RecoverCore(e, r, s)` and add public `RecoverFromDigest(digest32, r, s, recId)`; keep `TryRecover(message, joseSig)` behaviour identical (Phase-1/2 KATs guard it). In `src/Common/Sorcha.Cryptography.Secp256k1/Secp256k1Recovery.cs`.
- [X] T005 Implement `Secp256k1Signer.SignRecoverable` (RFC-6979 `HMacDsaKCalculator`, low-s, recId via `RecoverFromDigest`, `r‖s‖v`) + `Eip191.PersonalSignDigest` in the primitive. Make T002 pass.
- [X] T006 Implement `Siwe/SiweMessage` + `Siwe/SiweFormatter` (Format + TryParse, EIP-4361 ABNF per research R4) in the primitive. Make T003 pass.
- [X] T007 Run `dotnet test tests/Sorcha.Cryptography.Secp256k1.Tests` — signer/EIP-191/SIWE + Phase-1/2 ES256K regression green. **Commit** ("feat: [180] recoverable secp256k1 signer + EIP-191 + SIWE message").

**Checkpoint**: A digest signs to a recoverable low-s `r‖s‖v`; SIWE text round-trips to spec.

---

## Phase 3: User Story 1 — A wallet proves control of its Ethereum address (Priority: P1) 🎯 MVP

**Goal**: A wallet exposes its EIP-55 address and produces a signed SIWE prove-control message whose
recovered signer equals that address.

**Independent Test**: Get a wallet's address; sign a SIWE message for a domain/uri/nonce; recover the
signer from the signature and confirm it equals the address and the challenge fields match.

### Tests for User Story 1 ⚠️ (write first, must FAIL)

- [ ] T008 [P] [US1] `IKeyManagementService.DeriveSecp256k1KeyAtPathAsync` test (`Sorcha.Wallet.Core.Tests`): a known BIP39 seed → deterministic secp256k1 key at `m/44'/60'/0'/0/0`; its EIP-55 address is stable and matches an independently-derived NBitcoin address.
- [ ] T009 [P] [US1] `EthereumIdentityService` tests (`Sorcha.Wallet.Core.Tests`, fake wallet repo + key mgmt): `GetAddressAsync` deterministic; `SignSiweAsync` returns `{ message, signature, address }` whose signer (via `RecoverFromDigest` over `Eip191`) equals the address; the private key is never in the result.
- [ ] T010 [P] [US1] Endpoint tests (`Sorcha.Wallet.Service.Tests`): `GET ethereum-address` returns the address; `POST siwe/sign` returns a message+signature; both require auth.

### Implementation for User Story 1

- [ ] T011 [US1] Add `DeriveSecp256k1KeyAtPathAsync(seed, path)` to `IKeyManagementService` + `KeyManagementService` (NBitcoin derive → raw secp256k1 scalar + uncompressed pubkey; no `GenerateKeySet`/`WalletNetworks`). Make T008 pass.
- [ ] T012 [US1] Implement `IEthereumIdentityService`/`EthereumIdentityService` in `Sorcha.Wallet.Core` (decrypt seed via the existing direct-master path, derive `m/44'/60'/0'/0/{index}`, `GetAddressAsync`, `SignPersonalMessageAsync`, `SignSiweAsync`; key never returned). Register in DI. Make T009 pass.
- [ ] T013 [US1] Add `EthereumEndpoints.cs` to `Sorcha.Wallet.Service` — `GET …/ethereum-address`, `POST …/siwe/sign` (same auth as existing wallet ops; `.WithSummary`/`.WithDescription`); map in Program.cs. Make T010 pass.
- [ ] T014 [US1] Run Wallet.Core + Wallet.Service suites — US1 green. **Commit** ("feat: [180] wallet Ethereum identity: address + SIWE sign (US1)").

**Checkpoint**: A wallet exposes its ETH address and produces a verifiable SIWE prove-control signature end-to-end.

---

## Phase 4: User Story 2 — Sorcha verifies an inbound SIWE proof (Priority: P1)

**Goal**: Sorcha (as relying party) verifies an external SIWE message + signature — address recovery +
freshness/domain/nonce checks — fail-closed.

**Independent Test**: A valid external SIWE with the expected nonce/domain → accepted; tampered / wrong-address / expired / wrong-nonce → rejected.

### Tests for User Story 2 ⚠️ (write first, must FAIL)

- [X] T015 [P] [US2] `SiweVerifier` tests (`Secp256k1.Tests`): a message signed by its address's key → `Valid=true`, correct `Address`; reject tampered signature / different-address signature / expired (`NowUtc > ExpirationTime`) / not-before (`NowUtc < NotBefore`) / wrong `ExpectedNonce` / wrong `ExpectedDomain` / malformed message.
- [ ] T016 [P] [US2] Verify endpoint test (`Sorcha.Wallet.Service.Tests`): `POST /siwe/verify` accepts a valid proof (no wallet needed) and rejects a tampered one.

### Implementation for User Story 2

- [X] T017 [US2] Implement `Siwe/SiweVerifier` (+ `SiweValidationOptions`/`SiweVerificationResult`) in the primitive: `TryParse` → `Eip191` digest → `RecoverFromDigest` (recId=v-27) → address-match → nonce/domain/window; never throws. Make T015 pass.
- [ ] T018 [US2] Add `POST /api/v1/siwe/verify` to `EthereumEndpoints.cs` (delegates to `SiweVerifier`). Make T016 pass.
- [X] T019 [US2] Run Secp256k1 + Wallet.Service suites — US2 green. **Commit** ("feat: [180] SIWE verification / Sorcha as relying party (US2)").

**Checkpoint**: Sorcha accepts genuine SIWE proofs and rejects tampered/stale/mismatched ones.

---

## Phase 5: User Story 3 — The Ethereum key is confined to prove-control (Priority: P2)

**Goal**: The prove-control surface refuses transaction-shaped payloads and never exports the key or
signs a raw digest; the wallet's primary identity is unchanged.

**Independent Test**: Attempt to sign a transaction payload → refused; confirm the primary-algorithm signing/address is unchanged.

### Tests for User Story 3 ⚠️ (write first, must FAIL / then confirm)

- [ ] T020 [P] [US3] Transaction-guard test (`Sorcha.Wallet.Core.Tests`): a message that decodes as an RLP transaction (legacy list `0xc0..0xff` + field shape, and a typed-tx `0x01/0x02` envelope) → `SignPersonalMessageAsync`/`SignSiweAsync` refuse (throw/rejected), no signature produced.
- [ ] T021 [P] [US3] No-leak / no-raw-digest test: the service surface exposes no method to sign an arbitrary 32-byte digest or to return the private key; `SiweSignResult` contains no key material.
- [ ] T022 [P] [US3] Primary-algorithm-unchanged regression: a wallet's existing derive/sign/address for its primary algorithm is identical before/after (no `WalletNetworks` member added).

### Implementation for User Story 3

- [ ] T023 [US3] Add the RLP-transaction guard to `EthereumIdentityService` (reject a message whose bytes decode as an RLP transaction); keep prove-control (EIP-191-prefixed) text always accepted. Make T020 pass; T021/T022 confirm. **Commit** ("feat: [180] prove-control confinement: transaction guard + no key export (US3)").

**Checkpoint**: All three stories pass; the ETH key cannot authorise a transaction or be exported.

---

## Phase 6: Polish & Cross-Cutting Concerns

- [ ] T024 [P] Update `STANDARDS.md` — add rows for **EIP-191** (`personal_sign`) and **EIP-4361 (SIWE)**: status partial (prove-control produce + verify), Feature 180; note secp256k1 signing now present (verify + sign).
- [ ] T025 [P] Update the `cryptography` skill — append the signer / EIP-191 / SIWE / auxiliary-Ethereum-identity capability to the Feature 177–179 notes (verify-only → now also prove-control signing).
- [ ] T026 Update memory: `ethereum-vc-interop-initiative.md` (Phase 3 status + PR#) and the MEMORY.md Active-work line.
- [ ] T027 Whole-solution Release build (zero new warnings) + full touched-suite regression: `Secp256k1.Tests`, `Sorcha.Wallet.Core.Tests`, `Sorcha.Wallet.Service.Tests`, plus the Phase-1/2 verify suites (`Verifier`, `Cryptography`, `ServiceClients`). All green; primary-algorithm signing unchanged.
- [ ] T028 Run `specs/180-siwe-prove-control/quickstart.md` validation (the three `dotnet test` invocations) as the acceptance gate. **Commit** ("docs: [180] standards, skill, memory + regression gate").

---

## Dependencies & Execution Order

- **Setup (P1)** → **Foundational (P2, blocks all)** → **US1 (P3, MVP produce)** → **US2 (P4, verify)** → **US3 (P5, confinement)** → **Polish (P6)**.
- US2 reuses the Foundational signer/EIP-191 + US1's message plumbing.
- US3 hardens US1's signing service.

### Within each story
Tests written first and made to FAIL, then implementation. Signer/recovery before SIWE before the wallet service before endpoints.

### Parallel opportunities
- T002/T003 (foundational tests) parallel; T008/T009/T010 (US1 tests) parallel; T020/T021/T022 (US3 tests) parallel; T024/T025 (docs) parallel.
- Sequential on shared files: T004→T005 (Secp256k1Recovery/Signer), T012→T013 (service→endpoint), T017→T018 (verifier→endpoint), T023 within EthereumIdentityService.

---

## Implementation Strategy

**MVP = Setup + Foundational + US1** — a wallet exposes its ETH address and produces a verifiable SIWE
prove-control signature. US2 adds Sorcha-as-relying-party verification; US3 confines the key to
prove-control (transaction guard, no export). Commit per phase; the touched suites (including the
Phase-1/2 ES256K regression after the recovery refactor) are the fail-closed signal throughout.
