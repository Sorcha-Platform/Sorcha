---
description: "Task list for Feature 178 — Ethereum address-form issuer DID verification (offline ecrecover)"
---

# Tasks: Ethereum Address-Form Issuer DID Verification (Offline ecrecover)

**Input**: Design documents from `/specs/178-ethereum-ecrecover/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md

**Tests**: TDD requested — test tasks precede implementation in every phase; each is written to FAIL first.

**Organization**: Grouped by user story. Foundational (the pure-managed primitive) is the one blocking
prerequisite; US1 is the full end-to-end MVP; US2/US3 add governance and negative-path coverage.

## Path Conventions

Multi-project .NET solution. Source under `src/…`; tests under `tests/…`. Exact test-project locations
are confirmed during implementation (some may need `using Xunit;`/`using FluentAssertions;`/`using System;`
added — Phase-1 gotcha).

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Confirm the Phase-1 wiring this feature extends is in place; no new project is created.

- [ ] T001 Verify `src/Common/Sorcha.Cryptography.Secp256k1` is referenced by `Sorcha.Cryptography`, `Sorcha.ServiceClients.Http`, `Sorcha.Verifier.Engine`, and `Sorcha.Blueprint.Service` (Phase-1 ProjectReferences already exist — confirm, add none new); confirm `tests/Sorcha.Cryptography.Secp256k1.Tests` builds green as the baseline.
- [ ] T002 [P] Locate the DID-resolver test project (where `JwkDidResolver`/`KeyDidResolver` are tested) and the Verifier engine test project (`Sorcha.Verifier.Tests`, InternalsVisibleTo) and the Blueprint issuer-key-resolver test project; record their paths for the phases below.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The `ecrecover` primitive and the DID model field every user story depends on.

**⚠️ CRITICAL**: No user story can be verified end-to-end until the primitive recovers correctly.

- [ ] T003 [P] Add ES256K ecrecover **known-answer test** fixtures to `tests/Sorcha.Cryptography.Secp256k1.Tests`: two (private key, message) vectors whose signatures recover under recid 0 and recid 1 respectively, each asserting the recovered key's `EthereumAddress.FromPublicKey` equals the vector's known address. Tests reference `Secp256k1Recovery.TryRecover` (not yet implemented → FAIL).
- [ ] T004 [P] Add `VerifyByAddress` unit tests (same test project): true for the correct address in **both** lowercase and EIP-55-checksummed form and for a CAIP-10 `eip155:…:0x…`; false for a different address, a tampered signature, a 63/65-byte signature, and a non-hex address. References `Secp256k1Verifier.VerifyByAddress` (not yet implemented → FAIL).
- [ ] T005 Implement `Secp256k1Recovery.TryRecover(message, joseSignature)` in `src/Common/Sorcha.Cryptography.Secp256k1/Secp256k1Recovery.cs` — SEC1 §4.1.6 recovery for recid 0 and 1 (SHA-256 digest, reconstruct R from r, `Q = r⁻¹(sR − eG)` via BouncyCastle curve math), returning valid non-infinity candidates; never throws. Make T003 pass.
- [ ] T006 Implement `VerifyByAddress` on `ISecp256k1Verifier`/`Secp256k1Verifier` + a static entry point in `src/Common/Sorcha.Cryptography.Secp256k1/Secp256k1Verifier.cs` (recover → `EthereumAddress.FromPublicKey` → case-insensitive match; extract trailing `0x{40hex}` from a CAIP-10 input). Make T004 pass. Verification-only — no signing API.
- [ ] T007 Add `string? BlockchainAccountId` to `VerificationMethod` in `src/Common/Sorcha.ServiceClients.Http/Did/DidDocument.cs` (`[JsonPropertyName("blockchainAccountId")]`, `JsonIgnore(WhenWritingNull)`); XML summary. Purely additive.
- [ ] T008 Run `dotnet test tests/Sorcha.Cryptography.Secp256k1.Tests` — all KATs green. **Commit** ("feat: [178] ecrecover primitive + VerifyByAddress + blockchainAccountId field").

**Checkpoint**: The primitive recovers addresses correctly and the DID model can carry an address.

---

## Phase 3: User Story 1 — Accept a credential from a trusted address-form issuer (Priority: P1) 🎯 MVP

**Goal**: A credential from an allow-listed `did:pkh` / address-form `did:ethr` issuer, validly signed,
verifies to **Pass** offline; a signature that doesn't match the DID's address **Rejects**.

**Independent Test**: Allow-list one address-form issuer DID; present a genuine credential → Pass; present
one whose signature maps to a different address → Reject. Runs on both verify surfaces.

### Tests for User Story 1 ⚠️ (write first, must FAIL)

- [ ] T009 [P] [US1] `PkhDidResolver` tests (DID-resolver test project): `did:pkh:eip155:1:0x…` → one `EcdsaSecp256k1RecoveryMethod2020` VM with `id=#blockchainAccountId`, correct `blockchainAccountId`, VM id in `assertionMethod`; malformed/other-namespace → null.
- [ ] T010 [P] [US1] `EthrDidResolver` tests: bare, named-network (`mainnet`/`sepolia`), and hex-chain-id (`0x89`) forms → `#controller` VM with normalised decimal `blockchainAccountId` in `assertionMethod`; unknown network / non-default-doc → null.
- [ ] T011 [P] [US1] Verifier-engine integration test (`Sorcha.Verifier.Tests`): an ES256K credential with `iss` = allow-listed `did:pkh` → issuer signature verified (Pass); a credential whose signature maps to a different address → Reject. Uses `DidResolverBackedIssuerKeyResolver` + `VerifyEs256k`.
- [ ] T012 [P] [US1] Blueprint-engine integration test: an ES256K SD-JWT VC from an allow-listed address-form issuer → accepted via `DidX5cIssuerKeyResolver` → `SdJwtVcFormatHandler` → `SdJwtService`; address-mismatch → reject.

### Implementation for User Story 1

- [ ] T013 [P] [US1] Implement `PkhDidResolver` in `src/Common/Sorcha.ServiceClients.Http/Did/PkhDidResolver.cs` per contracts/did-resolvers.md (offline, `eip155` only, emit recovery VM in authentication+assertionMethod). Make T009 pass.
- [ ] T014 [P] [US1] Implement `EthrDidResolver` in `src/Common/Sorcha.ServiceClients.Http/Did/EthrDidResolver.cs` — address-form variants, decimal chain-id normalisation (known-network table + hex parse), default document only, optional `IEvmRpcClient? rpc = null` seam (unused this phase). Make T010 pass.
- [ ] T015 [US1] Register both resolvers in `src/Common/Sorcha.ServiceClients.Http/Extensions/HttpServiceCollectionExtensions.cs::AddDidResolvers` (alongside `KeyDidResolver`/`JwkDidResolver`).
- [ ] T016 [US1] Verifier engine — in `DidResolverBackedIssuerKeyResolver.cs`, relax the VM fallback + null-guard to accept a `BlockchainAccountId` VM (keep the assertionMethod gate) and return a synthesised recovery-JWK `JsonElement` (`{kty:EC,crv:secp256k1,blockchainAccountId}`).
- [ ] T017 [US1] Verifier engine — in `VerifiablePresentationValidator.cs::VerifyEs256k`, branch: JWK has x/y → key-match (unchanged); JWK has `blockchainAccountId` → `Secp256k1Verifier.VerifyByAddress(signingInput, signature, address)`. Make T011 pass.
- [ ] T018 [US1] Blueprint engine — add `string? BlockchainAccountId` to `IssuerKeyResolution` in `src/Core/Sorcha.Blueprint.Engine/Credentials/IIssuerKeyResolver.cs` (XML summary; invariant: set ⇒ `PublicKey` empty, `Algorithm="ES256K"`).
- [ ] T019 [US1] Blueprint service — in `src/Services/Sorcha.Blueprint.Service/Credentials/DidX5cIssuerKeyResolver.cs`, accept a `BlockchainAccountId` VM (relax the `PublicKeyJwk is null` return, keep assertionMethod gate) and return `IssuerKeyResolution{ PublicKey=[], BlockchainAccountId=…, Algorithm="ES256K", SigningKeyId=vm.Id }`.
- [ ] T020 [US1] `SdJwtService` — add optional `string? issuerRecoveryAddress = null` to `VerifyTokenAsync`/`VerifyPresentationAsync` and thread to the private `Verify`; ES256K branch: address set → `Secp256k1Verifier.VerifyByAddress(data, signature, address)`, else Phase-1 key-match (`src/Common/Sorcha.Cryptography/SdJwt/SdJwtService.cs`). Backward-compatible.
- [ ] T021 [US1] `SdJwtVcFormatHandler` — pass `key.BlockchainAccountId` as `issuerRecoveryAddress` into the verify call (`src/Core/Sorcha.Blueprint.Engine/Credentials/SdJwtVcFormatHandler.cs`). Make T012 pass.
- [ ] T022 [US1] Run the DID-resolver, Verifier, and Blueprint-engine test projects — US1 green. **Commit** ("feat: [178] address-form DID resolvers + recover-then-match verify seam (US1)").

**Checkpoint**: End-to-end MVP — a trusted address-form Ethereum issuer verifies to Pass offline on both surfaces; address-mismatch rejects.

---

## Phase 4: User Story 2 — Governed handling of an unlisted valid signature (Priority: P2)

**Goal**: A signature-valid credential from an **un-allowlisted** address-form issuer is **Rejected** by
default, and **Warn** (reduced assurance) only when `WarnOnUnlistedVerifiedIssuer` is set — Phase-1
governance, unchanged, proven for address-form.

**Independent Test**: Present a validly-signed credential from an unlisted address-form issuer with the
flag unset (Reject) and set (Warn).

### Tests for User Story 2 ⚠️ (write first, must FAIL until behaviour confirmed)

- [ ] T023 [P] [US2] Governance test: unlisted address-form issuer, `WarnOnUnlistedVerifiedIssuer` **unset** → Reject (fail-closed) — asserted on the trust/verify path used by the engine.
- [ ] T024 [P] [US2] Governance test: same credential, flag **set** → Warn (reduced assurance), reduced assurance recorded in the trust evidence.

### Implementation for User Story 2

- [ ] T025 [US2] Confirm no new trust code is needed — the Phase-1 `TrustPolicy.WarnOnUnlistedVerifiedIssuer` / `AssuranceLevel.None` / `TrustDecision.ReducedAssurance` path already governs a verified address-form issuer identically. If a test reveals a gap (e.g. the recovery-verified signal not reaching `IssuerContext.SignatureVerified`), fix at that seam only. Make T023/T024 pass.
- [ ] T026 [US2] Run the governance tests + the full Verifier/Blueprint-engine suites — US2 green, no regression. **Commit** ("test: [178] address-form issuer trust governance (US2)").

**Checkpoint**: Address-form issuers honour the fail-closed default and the reduced-assurance opt-in exactly as key-form issuers do.

---

## Phase 5: User Story 3 — Reject malformed / unsupported issuer identifiers (Priority: P3)

**Goal**: Malformed `did:pkh`/`did:ethr`, and `did:ethr` forms that would need an on-chain read, reject
offline with no network call.

**Independent Test**: Present credentials with malformed / registry-dependent issuer DIDs; each rejects
without a network call.

### Tests for User Story 3 ⚠️ (write first, must FAIL / then confirm)

- [ ] T027 [P] [US3] Resolver negative tests: malformed address length, non-hex, wrong namespace, unknown `did:ethr` network, and a `did:ethr` shape that implies a registry read → resolver returns null; assert no HTTP/RPC dependency is invoked (offline).
- [ ] T028 [P] [US3] Engine reject test: an ES256K credential whose issuer DID resolves to null, or to a VM carrying neither `publicKeyJwk` nor `blockchainAccountId` → Reject (unresolved issuer), never Warn.

### Implementation for User Story 3

- [ ] T029 [US3] Ensure `PkhDidResolver`/`EthrDidResolver` guard every malformed/unsupported case with a null return (refine the T013/T014 guards if any case slips through). Make T027 pass.
- [ ] T030 [US3] Confirm both issuer-key resolvers treat a null resolution / neither-key-nor-address VM as reject (existing null handling covers it); add the assertion. Make T028 pass. **Commit** ("test: [178] malformed/unsupported address-form DID rejection (US3)").

**Checkpoint**: All three user stories independently pass; fail-closed on every malformed/unsupported input.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Docs, standards, skills, memory, and the full-solution regression gate.

- [ ] T031 [P] Update `STANDARDS.md` — mark rows for `did:pkh` (CAIP-10), address-form `did:ethr` (ERC-1056), and `EcdsaSecp256k1RecoveryMethod2020` as partial/verify-only under Feature 178.
- [ ] T032 [P] Update the `cryptography` skill (`.claude/skills/cryptography/`) — append the ecrecover / address-form-DID capability to the Feature 177 notes.
- [ ] T033 [P] Update the `verifiable-credentials` skill (`.claude/skills/verifiable-credentials/SKILL.md`) — record `did:pkh`/address-form `did:ethr` verify-by-recovery under the Feature 177 block (now F178).
- [ ] T034 [P] Update the `sorcha-architecture` skill if the DID-resolver/issuer-key-seam surface is catalogued there (add the two resolvers + `blockchainAccountId`).
- [ ] T035 Update memory: `ethereum-vc-interop-initiative.md` (Phase 2 status + PR#) and the MEMORY.md Active-work line, per the frontmatter convention.
- [ ] T036 Build the whole solution (Release) — zero new warnings — then run the full touched-suite regression: `Sorcha.Cryptography.Secp256k1.Tests`, ServiceClients DID tests, `Sorcha.Cryptography` tests, `Sorcha.Verifier.Tests`, Blueprint engine/service tests. All green; fail-closed default preserved.
- [ ] T037 Run `specs/178-ethereum-ecrecover/quickstart.md` validation (the four `dotnet test` invocations) as the acceptance gate. **Commit** ("docs: [178] standards, skills, memory + regression gate").

---

## Dependencies & Execution Order

- **Setup (Phase 1)**: immediate.
- **Foundational (Phase 2)**: depends on Setup; **blocks all user stories** (the primitive must recover correctly first).
- **US1 (Phase 3)**: depends on Foundational. Delivers the MVP (resolvers + both seams).
- **US2 (Phase 4)**: depends on US1 code (reuses resolvers/seams); adds governance tests, ~no new code.
- **US3 (Phase 5)**: depends on US1 resolvers; adds negative-path coverage.
- **Polish (Phase 6)**: after US1–US3.

### Within each story
Tests written first and made to FAIL, then implementation. Primitive before resolvers before seams before integration.

### Parallel opportunities
- T003/T004 (primitive tests) parallel; T009/T010/T011/T012 (US1 tests) parallel; T013/T014 (the two resolvers) parallel; T031–T034 (docs/skills) parallel.
- Sequential where the same file is touched: T016→T017 (VerifiablePresentationValidator), T018→T019/T020/T021 (IssuerKeyResolution consumers), T020 within SdJwtService.

---

## Implementation Strategy

**MVP = Phase 1 + Phase 2 + Phase 3 (US1).** That alone delivers offline verification of a trusted
address-form Ethereum issuer end-to-end on both surfaces — a complete, demonstrable capability. US2 and
US3 harden governance and negative paths. Commit per phase; the touched test suites are the fail-closed
regression signal at every step.
