# Tasks: Ethereum-key VC verification — Phase 1 (verify-only)

**Feature**: `177-ethereum-vc-verify` | **Spec**: [spec.md](./spec.md) | **Plan**: [plan.md](./plan.md)
**Design**: research.md (insertion-point table §R1–R6), data-model.md, contracts/internal-seams.md

**Approach**: TDD (tests precede/accompany implementation). Constitution gates: >85% coverage on new
code; existing credential/trust/verifier regression suite MUST stay green (fail-closed preserved).

**Conventions**: `[P]` = parallelizable (different files, no incomplete dependency). `[US1/US2/US3]` =
user-story task. Line numbers in referenced files are indicative (see research.md); re-locate on edit.

---

## Phase 1: Setup (shared infrastructure)

- [x] T001 Create new pure-managed project `src/Common/Sorcha.Cryptography.Secp256k1/Sorcha.Cryptography.Secp256k1.csproj` (net10.0, C# 14, nullable enabled, MIT SPDX header policy) with a single `<PackageReference Include="BouncyCastle.Cryptography" />`; add it to the solution.
- [x] T002 Create test project `tests/Sorcha.Cryptography.Secp256k1.Tests/Sorcha.Cryptography.Secp256k1.Tests.csproj` (xUnit v3 + FluentAssertions, per repo test conventions); add to solution and reference the new project.
- [x] T003 [P] Add `ProjectReference` → `Sorcha.Cryptography.Secp256k1` to each consumer csproj: `src/Common/Sorcha.Cryptography/Sorcha.Cryptography.csproj`, `src/Common/Sorcha.ServiceClients.Http/Sorcha.ServiceClients.Http.csproj`, `src/Common/Sorcha.Verifier.Engine/Sorcha.Verifier.Engine.csproj`, `src/Services/Sorcha.Blueprint.Service/Sorcha.Blueprint.Service.csproj`. Confirm `Sorcha.Verifier.Engine` still builds native-dep-free (WASM-safe).

**Checkpoint**: solution builds; the new leaf project is referenced by all four consumers.

---

## Phase 2: Foundational (BLOCKING — shared by all user stories)

The secp256k1 primitive + the offline key-bearing DID resolvers. No user story can proceed until this
phase is complete.

### Primitive — tests first

- [x] T004 [P] KAT tests for `ISecp256k1Verifier` in `tests/Sorcha.Cryptography.Secp256k1.Tests/Secp256k1VerifierTests.cs`: valid JOSE ES256K vector → true; tampered signature → false; malformed key/signature → false (never throws).
- [x] T005 [P] Round-trip tests for `Secp256k1Jwk` in `tests/Sorcha.Cryptography.Secp256k1.Tests/Secp256k1JwkTests.cs`: JWK(x,y) parse/build; 33-byte compressed-point decompress → (x,y); off-curve point rejected.
- [x] T006 [P] KAT tests for keccak + address in `tests/Sorcha.Cryptography.Secp256k1.Tests/EthereumAddressTests.cs`: keccak256 known vector; known private-key → EIP-55 checksummed address.

### Primitive — implementation

- [x] T007 [P] Implement `src/Common/Sorcha.Cryptography.Secp256k1/Secp256k1PublicKey.cs` + `Secp256k1Jwk.cs` (parse/build JOSE EC JWK `crv:secp256k1`; decompress compressed point; on-curve validation).
- [x] T008 Implement `src/Common/Sorcha.Cryptography.Secp256k1/ISecp256k1Verifier.cs` + `Secp256k1Verifier.cs` (BouncyCastle ECDSA over SHA-256, 64-byte r‖s, accept high/low-s; return false on malformed input).
- [x] T009 [P] Implement `src/Common/Sorcha.Cryptography.Secp256k1/Keccak256.cs` + `EthereumAddress.cs` (BouncyCastle `KeccakDigest(256)`; keccak→last-20→EIP-55). Foundation only — no Phase 1 runtime caller.

### Offline DID resolvers — tests first

- [x] T010 [P] Test in `tests/Sorcha.ServiceClients.Tests/Did/KeyDidResolverSecp256k1Tests.cs`: `did:key`(secp256k1, multicodec `0xe701`) resolves to a VM with `type:"JsonWebKey2020"` and `publicKeyJwk` `crv:"secp256k1"` (x,y present); listed in `assertionMethod`.
- [x] T011 [P] Test in `tests/Sorcha.ServiceClients.Tests/Did/JwkDidResolverTests.cs`: `did:jwk` resolves secp256k1, P-256, and Ed25519 JWKs to the expected VM; malformed identifier → null.

### Offline DID resolvers — implementation

- [x] T012 Add `0xe701` constants + `BuildSecp256k1Document` (emitting `publicKeyJwk`, 33-byte key length check) to `src/Common/Sorcha.ServiceClients.Http/Did/KeyDidResolver.cs` (delegating point→JWK to `Secp256k1Jwk`).
- [x] T013 Create `src/Common/Sorcha.ServiceClients.Http/Did/JwkDidResolver.cs` (`CanResolve("jwk")`, decode base64url JWK → single VM, all curves).
- [x] T014 Register `JwkDidResolver` in `src/Common/Sorcha.ServiceClients.Http/Extensions/HttpServiceCollectionExtensions.cs` `AddDidResolvers`.

**Checkpoint**: primitive KATs green; both resolvers resolve offline to `publicKeyJwk` VMs. Foundation ready.

---

## Phase 3: User Story 1 — Verify an Ethereum-issued credential (Priority: P1) 🎯 MVP

**Goal**: Verify the ES256K issuer signature of an SD-JWT/JWT VC whose issuer DID (`did:key`/`did:jwk`
secp256k1) resolves offline, through the existing pipeline → Pass (allowlisted) / Reject (tampered).

**Independent test**: Present a Veramo-style ES256K SD-JWT from an allowlisted `did:key`(secp256k1)
issuer → Pass; tamper one signature byte → Reject.

### Tests first

- [x] T015 [P] [US1] ES256K issuer-JWS verify unit test in `tests/Sorcha.Cryptography.Tests/SdJwt/SdJwtEs256kTests.cs`: valid ES256K token verifies; tampered → SignatureInvalid.
- [ ] T016 [P] [US1] Integration test (blueprint/verifier engine) asserting an allowlisted `did:key`(secp256k1) ES256K credential → **Pass** and a tampered copy → **Reject**; add Veramo-style ES256K SD-JWT fixture + a `did:jwk`(secp256k1) EU/EUDI-style fixture under the test project.

### Implementation

- [x] T017 [US1] Add `ES256K`/`SECP256K1` branch to `SdJwtService.Verify` and `MapAlgorithm` in `src/Common/Sorcha.Cryptography/SdJwt/SdJwtService.cs`, delegating to `ISecp256k1Verifier` (covers issuer JWS + request-object paths).
- [x] T018 [US1] Add a `crv` switch to `ExtractPublicKeyFromJwk` in `src/Services/Sorcha.Blueprint.Service/Credentials/DidX5cIssuerKeyResolver.cs` so `crv:"secp256k1"` parses via the primitive (not hard-coded `nistP256`).
- [x] T019 [US1] Add `"ES256K" => VerifyEs256k(...)` to `VerifyJwsSignature` + a `VerifyEs256k` sibling (delegating to the primitive) in `src/Common/Sorcha.Verifier.Engine/VerifiablePresentationValidator.cs`.

**Checkpoint**: US1 independently testable — a foreign Ethereum-issued credential verifies end-to-end. **This is the MVP.**

---

## Phase 4: User Story 2 — Verify an Ethereum holder key-binding (Priority: P2)

**Goal**: Verify a presentation whose holder key-binding uses secp256k1 (`cnf.jwk` secp256k1 +
ES256K KB-JWT proof).

**Independent test**: Present a credential with a secp256k1 `cnf` + valid ES256K KB-JWT → binding
verifies; sign the KB-JWT with a different key → Reject. Depends on the US1 `SdJwtService.Verify`
ES256K branch (shared).

### Tests first

- [x] T020 [P] [US2] KB-JWT holder-binding test in `tests/Sorcha.Cryptography.Tests/SdJwt/SdJwtEs256kKeyBindingTests.cs`: secp256k1 `cnf` + valid ES256K KB-JWT → verified; wrong-key KB-JWT → fail. Add a fixture with a secp256k1 `cnf.jwk`.

### Implementation

- [x] T021 [US2] Add a `crv == "secp256k1"` arm to `ExportPublicKeyFromJwk` in `src/Common/Sorcha.Cryptography/SdJwt/SdJwtService.cs` (returns holder key bytes + `algorithm = "ES256K"` so the shared `Verify` handles the KB-JWT).

**Checkpoint**: US2 independently testable — an Ethereum-key holder binding verifies (and fails correctly).

---

## Phase 5: User Story 3 — Govern trust of unlisted verified issuers (Priority: P3)

**Goal**: Per-requirement control — a signature-valid but unlisted Ethereum issuer is rejected by
default, or accepted at reduced assurance (Warn) when `warnOnUnlistedVerifiedIssuer` is set.

**Independent test**: Verify a signature-valid unlisted issuer twice (flag off → Reject; flag on →
Warn); allowlisted → Pass regardless; a non-eth unlisted issuer with no flag → Reject (unchanged).
The type/evaluator changes are independent; the integration assertion reuses a US1 verifying credential.

### Tests first

- [x] T022 [P] [US3] `TrustEvaluator` tests in `tests/.../TrustEvaluatorWarnTests.cs`: no-vouch + flag off → reject; no-vouch + flag on → `IsTrusted` with `ReducedAssurance` + assurance `None`; vouched → Pass (flag ignored).
- [x] T023 [P] [US3] Fail-closed regression test: a non-Ethereum unlisted issuer with no flag still → Reject (asserts the change is inert when the flag is false), plus confirm `ComputePolicyDigest` changes when the flag flips.

### Implementation

- [x] T024 [P] [US3] Add `bool WarnOnUnlistedVerifiedIssuer { get; set; } = false;` to `src/Common/Sorcha.Blueprint.Models/Credentials/TrustPolicy.cs`.
- [x] T025 [P] [US3] Add `None = -1` to `src/Core/Sorcha.Blueprint.Engine/Credentials/AssuranceLevel.cs` and `bool ReducedAssurance` to `src/Core/Sorcha.Blueprint.Engine/Credentials/TrustDecision.cs`.
- [x] T026 [US3] Add the scoped warn branch inside the `if (!trusted)` block of `EvaluateAsync` and include the flag in `ComputePolicyDigest` in `src/Core/Sorcha.Blueprint.Engine/Credentials/TrustEvaluator.cs` (inert when flag false).
- [~] T027 [US3] (Re-scoped — see PR) The authoritative reduced-assurance signal is `TrustDecision.ReducedAssurance` on the trust-gated path. The citizen-PWA `RealVerifierEngine`/`VerifiablePresentationValidator` path does NOT consume `TrustDecision` (separate offline verifier with its own issuer-not-verified→Warn), so threading the blueprint-trust signal into it is unnecessary/conflates two paths. Deferred as a documented no-op for Phase 1.

**Checkpoint**: US3 independently testable — trust governance behaves per the state machine; fail-closed default preserved.

---

## Phase 6: Polish & cross-cutting

- [ ] T028 [P] XML `<summary>` docs on all new public types (`ISecp256k1Verifier`, `Secp256k1Verifier`, `Secp256k1Jwk`, `EthereumAddress`, `JwkDidResolver`, new model members) — zero new build warnings (Release).
- [ ] T029 [P] Observability: confirm the eth trust decisions (incl. warn) are recorded via the existing `TrustMetrics` (`Sorcha.Trust` meter) with `outcome`/`format`/`assurance` tags; no new meter.
- [ ] T030 Run the full credential/trust/verifier regression suite; confirm ES256/EdDSA SD-JWT + mdoc unchanged and fail-closed preserved (SC-004).
- [ ] T031 [P] Execute `quickstart.md` steps fully offline; confirm no network/blockchain access at any step (SC-005).
- [ ] T032 [P] Coverage check: >85% on new code (`Sorcha.Cryptography.Secp256k1`, resolvers, trust changes) — constitution gate.
- [ ] T033 [P] Docs sync: record F177 (secp256k1 `did:key`/`did:jwk`, ES256K verify, `warnOnUnlistedVerifiedIssuer`) in `STANDARDS.md` and the `sorcha-architecture` skill; note Phases 2–4 as roadmap.

---

## Dependencies & execution order

```
Phase 1 Setup ──► Phase 2 Foundational ──► Phase 3 US1 (MVP) ──► Phase 4 US2
                                                    └──────────► Phase 5 US3 ──► Phase 6 Polish
```

- **Phase 2 blocks everything** (primitive + resolvers are shared).
- **US1 (P1)** is the MVP and depends only on Phase 2.
- **US2 (P2)** reuses US1's `SdJwtService.Verify` ES256K branch → sequence after US1 (or at least after T017).
- **US3 (P3)** implementation (T024–T027) is independent of US1/US2 and could run in parallel after Phase 2; its integration assertion reuses a US1 verifying credential, so run its end-to-end check after US1.
- **Phase 6** last.

## Parallel execution examples

- **Phase 2 primitive**: T004, T005, T006 (tests) in parallel; then T007 + T009 in parallel, T008 after T007.
- **Phase 2 resolvers**: T010, T011 in parallel; T012/T013 after the primitive; T014 after T013.
- **US1**: T015, T016 (tests) in parallel; then T017, T018, T019 (different files) in parallel.
- **US3 types**: T024, T025 in parallel; T026 after both; T027 after T026.

## Implementation strategy

- **MVP = Phase 1 + Phase 2 + Phase 3 (US1)** — Sorcha can verify a foreign Ethereum-issued credential end-to-end, offline. Ship/demo here.
- **Increment 2**: add US2 (holder key-binding).
- **Increment 3**: add US3 (trust governance / Warn), then Polish.
- Each user story is an independently testable slice; stop after any story for a coherent increment.

## Task count

33 tasks — Setup 3, Foundational 11, US1 5, US2 2, US3 6, Polish 6. MVP (through US1) = 19 tasks.
