---
description: "Task list for feature 135 — EUDI Credential Format & Unified Trust"
---

# Tasks: EUDI Credential Format & Unified Trust

**Input**: Design documents from `/specs/135-eudi-credential-format-trust/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/

**Tests**: INCLUDED — the spec mandates ≥85% coverage (SC-008) and security-critical trust logic; tests are written first per story.

**Organization**: By user story (US1 P1 → US2 P2 → US3 P3) so each is independently implementable and testable.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Parallelizable (different files, no incomplete dependency)
- **[Story]**: US1 / US2 / US3 (story phases only)

## Path Conventions

Repo root `C:\Projects\Sorcha`. Source under `src/`, tests under `tests/` mirroring project names.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Dependencies and scaffolding shared by all stories.

- [X] T001 Add `System.Formats.Cbor` and `System.Security.Cryptography.Cose` (both pinned **10.0.8** — Cose 10.0.0 carries NU1903, and Cose 10.0.8 requires Cbor ≥ 10.0.8) to `Directory.Packages.props`; reference both from `src/Common/Sorcha.Cryptography/Sorcha.Cryptography.csproj`.
- [X] T002 [P] Create the `Sorcha.Trust` meter scaffold (no instruments yet) in `src/Core/Sorcha.Blueprint.Engine/Credentials/TrustMetrics.cs` with the SPDX/Copyright header.
- [X] T003 [P] Create mdoc test-vector fixture directory `tests/Sorcha.Cryptography.Tests/Fixtures/Mdoc/` (README placeholder; the real PID `DeviceResponse` vector lands in US2/T036).
- [X] T004 [P] Create the `Sorcha.Cryptography/Mdoc/` folder (README placeholder) and `tests/Sorcha.Cryptography.Tests/Fixtures/Mdoc/` skeletons.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Shared models, enums, and seams. The clean-break model edits break compilation until all references are migrated — this phase MUST leave the solution building.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete and `dotnet build --force` is green.

- [X] T005 [P] Add `CredentialFormat` enum (`SdJwtVc`="sd-jwt-vc", `MsoMdoc`="mso_mdoc") in `src/Common/Sorcha.Blueprint.Models/Credentials/CredentialFormat.cs`.
- [X] T006 [P] Add `AssuranceLevel` (ordered Low<Substantial<High), `TrustAnchor`, `TrustSourceKind`, `TrustCombinator` enums in `src/Common/Sorcha.Blueprint.Models/Credentials/` (one file each).
- [X] T007 [P] Add `TrustSourceRef` model in `src/Common/Sorcha.Blueprint.Models/Credentials/TrustSourceRef.cs` (Kind, ConfersAssurance?, AllowedIssuers?, TrustListId?, Options?).
- [X] T008 Add `TrustPolicy` model (Sources, Combinator, MinAssuranceLevel) + `TrustPolicyExtensions` (`AllowedIssuerDids`, `FromLegacyIssuers`) in `src/Common/Sorcha.Blueprint.Models/Credentials/TrustPolicy.cs`.
- [X] T009 **CLEAN BREAK** — `CredentialRequirement.cs`: removed `AcceptedIssuers`; added `Format` (default `SdJwtVc`) and `TrustPolicy?`.
- [X] T010 `CredentialIssuanceConfig.cs`: added `Format` (default `SdJwtVc`) and `TrustAnchor` (default `Register`).
- [X] T011 [P] Added result types `TrustDecision`, `TrustEvidence`, `TrustFailureReason` in `src/Core/Sorcha.Blueprint.Engine/Credentials/`.
- [X] T012 [P] Added seam interfaces `ITrustEvaluator`, `ITrustResolverRegistry`, `ITrustSourceResolver` (+ `IssuerContext`, `TrustSourceVouch`).
- [X] T013 [P] Added `IStatusListChecker` (+ `StatusReference`, `StatusListBit`) seam.
- [X] T014 [P] Added `ICredentialFormatHandler` (+ `PresentedCredential`, `FormatVerifyResult`) seam.
- [X] T015 Migrated all 7 `CredentialRequirement.AcceptedIssuers` consumers + 6 test files to the `TrustPolicy` shape (Validator, Fluent builder, engine `CredentialVerifier`, wallet `CredentialMatcher`, `PresentationLifecycleService`, `BlueprintToolExecutor`, `ReviewSummaryRenderer`). The 7 unrelated presentation-request DTOs with their own `AcceptedIssuers` were left untouched.
- [X] T016 `dotnet build` green across the solution; touched test suites pass (Models 426, Fluent 109, Engine 460/+1 skip, Wallet.Service 809, Blueprint.Service 723).

**Checkpoint**: Solution builds with the new models; seams exist but are unimplemented.

---

## Phase 3: User Story 1 — One trust decision for every verification path (Priority: P1) 🎯 MVP

**Goal**: Both verifiers route through one `ITrustEvaluator`; the engine verifier gains real signature + trust-source checks; `AcceptedIssuers`/`_trustedRoots` fully replaced by the trust policy; pinnable `TrustEvidence` produced; fail-closed default. SD-JWT VC only.

**Independent Test**: Present an SD-JWT VC through the engine path and the HAIP path against the same policy and confirm identical accept/reject + evidence; tampered signature rejected on both; assurance/combinator/fail-closed/offline-re-eval all honored (quickstart US1).

### Tests for User Story 1 ⚠️ (write first, must fail)

- [X] T017 [P] [US1] `TrustEvaluatorTests` (anyOf/allOf, MinAssuranceLevel, claim override, fail-closed on unavailable source, status mapping, evidence + policy-digest, default-policy) + `TrustResolverRegistryTests` in `tests/Sorcha.Blueprint.Engine.Tests/Credentials/TrustEvaluatorTests.cs`. **18 tests, all green (suite 478/+1 skip).**
- [X] T018 [P] [US1] Per-source resolver tests (`TrustSourceResolverTests` — register/allowlist via fake `IIssuerDirectory`, x509 via real self-signed certs). 12 tests; Engine suite 490/+1 skip.
- [X] T019 [P] [US1] **Cross-path parity test**: same credential + policy through engine `SdJwtVcFormatHandler` and HAIP `HaipPresentationVerifier` yields equal accept + evidence shape (deciding source, assurance, policy digest) and both reject a tampered signature (SC-001) in `tests/Sorcha.Haip.Service.Tests/CrossPathParityTests.cs`. 2 tests.
- [X] T020 [P] [US1] Engine signature-verification tests proving `SignatureValid` is truthful (valid accepted, tampered rejected — SC-002) in `tests/Sorcha.Blueprint.Engine.Tests/Credentials/CredentialVerifierSignatureTests.cs`. 3 tests; key resolver keyed on header.payload so a tampered token still resolves its key and the real signature check rejects it.
- [X] T021 [P] [US1] Default-policy synthesis test (no policy → register@Low; legacy issuers → did-allowlist) + offline pinned re-evaluation test (SC-005) in `tests/Sorcha.Blueprint.Engine.Tests/Credentials/TrustPolicyDefaultsTests.cs`. 4 tests.

### Implementation for User Story 1

- [X] T022 [P] [US1] Implement `IStatusListChecker` adapter over `BitstringStatusListChecker` (W3C) — `CheckAsync(StatusReference)` maps Active→NotSet, Revoked/Suspended→Set, null→Unknown.
- [X] T023 [P] [US1] `IetfTokenStatusListChecker` implements engine `IStatusListChecker` via explicit interface (maps the local `StatusListBit` → engine enum). Added HAIP→`Sorcha.Blueprint.Engine` project reference.
- [X] T024 [P] [US1] Implement `RegisterTrustSourceResolver` (resolve via engine-local `IIssuerDirectory` + assertionMethod gate) in `.../Sources/RegisterTrustSourceResolver.cs`. **NOTE: depends on new engine-local seams `IIssuerDirectory` + `ITenantTrustAnchorProvider` (added) — service adapters over `IDidResolverRegistry`/`ITrustProvider` are wired in T034.**
- [X] T025 [P] [US1] Implement `X509TenantTrustSourceResolver` (X509Chain CustomRootTrust + optional CRL over engine-local `ITenantTrustAnchorProvider`; `AnchorId`/`Kind` virtual so the US2 trustlist source can subclass) in `.../Sources/X509TenantTrustSourceResolver.cs`.
- [X] T026 [P] [US1] Implement `DidAllowlistTrustSourceResolver` (direct match + alsoKnownAs equivalence via `IIssuerDirectory`) in `.../Sources/DidAllowlistTrustSourceResolver.cs`.
- [X] T027 [US1] Implement `TrustResolverRegistry` (mirror `IDidResolverRegistry`) in `src/Core/Sorcha.Blueprint.Engine/Credentials/TrustResolverRegistry.cs`.
- [X] T028 [US1] Implement `TrustEvaluator` — signature precondition → per-source vouch + combinator → assurance (source-tier + upward-only claim override, honoured only for ≥Substantial sources) → status check → `TrustEvidence` + SHA-256 policy digest → fail-closed. (Resolvers themselves are T024-T026, pending the engine-local directory/root seams.)
- [X] T029 [US1] Default-policy synthesis (FR-026): `NormalisePolicy` in the evaluator falls back to register@Low when no policy is declared; legacy issuers→did-allowlist handled at requirement binding via `TrustPolicyExtensions.FromLegacyIssuers` (foundation).
- [X] T030 [US1] Implement `SdJwtVcFormatHandler` (wraps existing `SdJwtService`; calls `ITrustEvaluator`) in `src/Core/Sorcha.Blueprint.Engine/Credentials/SdJwtVcFormatHandler.cs`. Added engine-local `IIssuerKeyResolver` seam + `IssuerKeyResolution`; handler verifies issuer signature for real (issuer-only / KB overload), extracts IETF+W3C status reference, populates `IssuerContext`, delegates to `ITrustEvaluator`. Real-crypto tests (`SdJwtVcFormatHandlerTests`, 6) + shared `EngineSdJwtTestFactory` mint helper. Engine suite 496/+1 skip.
- [X] T031 [US1] Rewrote engine `CredentialVerifier` to dispatch by `CredentialRequirement.Format` to an `ICredentialFormatHandler` and orchestrate type-match + required-claim constraints over the handler's disclosed claims; signature/issuer-trust/revocation now decided in the handler + `ITrustEvaluator`. Removed the `SignatureValid=false` shortcut, the flat issuer match, and the `IRevocationChecker` branch. New ctor `CredentialVerifier(IEnumerable<ICredentialFormatHandler>)`. Reworked the 4 suites (`CredentialVerifierTests`, `CrossBlueprintCredentialTests`, `CredentialVerifierRevocationTests`, `CredentialVerifierSelectiveDisclosureTests`) onto REAL signed SD-JWT VCs via `EngineSdJwtTestFactory` (no mocked `ISdJwtService`). Folds in the engine-side of T034: Blueprint.Service adapters `DidIssuerDirectory` (over `IDidResolverRegistry`) + `DidX5cIssuerKeyResolver` (x5c→DID→jwk port), and Program.cs DI for evaluator/registry/resolvers/status-checker/handler/verifier. Engine suite 502/+1 skip.
- [X] T032 [US1] Routed `HaipPresentationVerifier` through `ITrustEvaluator`: HAIP still owns SD-JWT signature + KB verification, then builds an `IssuerContext` (issuer/x5c/kid/status) and delegates the trust decision. Deleted `_trustedRoots`/`AddTrustedRoot`/`ValidateX5cChain`/`RevocationMode` and the bespoke W3C/IETF status branching — x5c chain validation is now the x509-tenant source, status is the unified `IStatusListChecker`. HAIP synthesises an AnyOf[x509-tenant, did-allowlist(acceptedIssuers)|register] policy. **Behaviour hardening**: x5c chain validity now actually gates trust (was advisory-only). Reworked `HaipPresentationVerifierTests`.
- [X] T033 [US1] `VerificationResult` carries the engine `TrustEvidence` (vouching source / assurance / policy digest) so spec-079 receipts pin a re-checkable decision (FR-014/015). Set in `HaipPresentationVerifier`; stored + relayed unchanged.
- [X] T034 [US1] DI wiring: HAIP `Program.cs` registers `ConfiguredTenantTrustAnchorProvider`, `DidIssuerDirectory`, the 3 resolvers, `TrustResolverRegistry`, `IStatusListChecker` (IetfTokenStatusListChecker), `ITrustEvaluator`, and the now-scoped `HaipPresentationVerifier`. Engine-side wiring (Blueprint.Service adapters + DI) landed with T031.
- [X] T035 [US1] `TrustMetrics.RecordDecision` records the `sorcha_trust_decision_total` counter (outcome/source/format/assurance/reason — no subject data, FR-024), called from both the engine handler and the HAIP verifier; `Sorcha.Trust` meter registered in ServiceDefaults OTel. Removed the static `Haip:TrustedRootCertificates` `AddTrustedRoot` seeding (roots now flow through `ConfiguredTenantTrustAnchorProvider`).

**Checkpoint**: US1 fully functional — unified trust on SD-JWT VC across both paths, MVP demoable.

---

## Phase 4: User Story 2 — Accept an mdoc credential from an EUDI wallet (Priority: P2)

**Goal**: Accept an `mso_mdoc` presentation online (OpenID4VP) through the format seam and the same trust evaluator; add the `trustlist` source.

**Independent Test**: Submit a PID `DeviceResponse` `vp_token` against an `mso_mdoc` requirement with a `trustlist` policy; valid accepted, untrusted/bad-binding/tampered/revoked rejected; SD-JWT VC parity preserved (quickstart US2).

### Tests for User Story 2 ⚠️ (write first, must fail)

- [X] T036 [P] [US2] CBOR/COSE round-trip + known-answer vector tests (tag-24 wrapping, MSO digest, x5chain label 33) in `tests/Sorcha.Cryptography.Tests/Mdoc/MdocCodecTests.cs`.
- [X] T037 [P] [US2] `MdocService` verify tests using the PID fixture: issuer signature, valueDigests integrity, SessionTranscript/DeviceAuth binding in `tests/Sorcha.Cryptography.Tests/Mdoc/MdocServiceTests.cs`.
- [X] T038 [P] [US2] `MdocPresentationVerifier` tests (untrusted→`UntrustedIssuer`, bad binding→`HolderBindingInvalid`, tampered→`IntegrityFailure`, revoked→`Revoked`) in `tests/Sorcha.Haip.Service.Tests/MdocPresentationVerifierTests.cs`.
- [X] T039 [P] [US2] `TrustListSourceResolver` + `OperatorSnapshotTrustListProvider` tests (snapshot id+freshness into evidence; missing list→`SourceUnavailable`) in `tests/Sorcha.Blueprint.Engine.Tests/Credentials/TrustListSourceTests.cs`.
- [X] T040 [P] [US2] Trust-list admin endpoint contract tests (PUT/GET/list) in `tests/Sorcha.Tenant.Service.Tests/TrustListAdminEndpointTests.cs`.

### Implementation for User Story 2

- [X] T041 [P] [US2] Implement CBOR tag-24 helpers + deterministic encoding in `src/Common/Sorcha.Cryptography/Mdoc/Cbor/`.
- [X] T042 [P] [US2] Implement `CoseX5Chain` helper (label 33, unprotected, bstr/array-of-bstr) in `src/Common/Sorcha.Cryptography/Mdoc/Cose/CoseX5Chain.cs`.
- [X] T043 [P] [US2] Implement mdoc models: `IssuerSigned`/`IssuerSignedItem`, `MobileSecurityObject` (+`MsoStatus`), `DeviceResponse`/`DeviceSigned`/`DeviceAuth`, `SessionTranscript`/`OpenID4VPHandover`(+DCAPI variant) in `src/Common/Sorcha.Cryptography/Mdoc/` (per data-model §3).
- [X] T044 [US2] Implement `IMdocService`/`MdocService` — decode DeviceResponse, verify `issuerAuth` COSE_Sign1, recompute `valueDigests`, reconstruct `DeviceAuthentication`/SessionTranscript, verify `DeviceAuth` (signature + MAC) in `src/Common/Sorcha.Cryptography/Mdoc/MdocService.cs` (depends on T041-T043).
- [X] T045 [US2] Implement `MdocFormatHandler.VerifyAsync` (calls `MdocService` + `ITrustEvaluator`; resolves mdoc `status.status_list` via `IStatusListChecker`) in `src/Core/Sorcha.Blueprint.Engine/Credentials/MdocFormatHandler.cs`.
- [X] T046 [P] [US2] Implement `ITrustListProvider` + `OperatorSnapshotTrustListProvider` in `src/Common/Sorcha.ServiceClients.Http/Trust/`.
- [X] T047 [P] [US2] Implement `TrustListSourceResolver` (loads snapshot into `X509Chain.CustomTrustStore`; records id+freshness in evidence) in `src/Core/Sorcha.Blueprint.Engine/Credentials/Sources/TrustListSourceResolver.cs`; register in `TrustResolverRegistry`.
- [X] T048 [US2] Implement `MdocPresentationVerifier` (OpenID4VP `vp_token` → DeviceResponse decode → `MdocFormatHandler`) in `src/Services/Sorcha.Haip.Service/Services/MdocPresentationVerifier.cs`.
- [X] T049 [US2] Wire DCQL `format: "mso_mdoc"` request parsing + `vp_token` keyed-by-query-id handling + format dispatch into the existing OpenID4VP `direct_post` endpoint in `src/Services/Sorcha.Haip.Service/Endpoints/` (per `contracts/mdoc-presentation.openapi.md`); add `.WithSummary`/`.WithDescription`.
- [X] T050 [US2] Implement `TrustListSnapshotStore` + admin endpoints (`PUT/GET /api/v1/trust/trustlists/{id}`, `GET /api/v1/trust/trustlists`) in `src/Services/Sorcha.Tenant.Service/Trust/` with Scalar docs + `RateLimitPolicies.Strict` (per `contracts/trustlist-admin.openapi.md`).
- [X] T051 [US2] DI wiring for mdoc handler, provider, trustlist resolver, mdoc verifier in `Sorcha.Haip.Service/Program.cs` and Tenant `Program.cs`; storage-registration-log entry for the snapshot store.

**Checkpoint**: US1 + US2 both work — mdoc accepted from an EUDI wallet, SD-JWT VC unchanged.

---

## Phase 5: User Story 3 — Issue an mdoc credential with a chosen trust anchor (Priority: P3)

**Goal**: Mint SD-JWT VC or `mso_mdoc` to an external wallet with a selectable trust anchor; attach the correct chain; resolve the x5c-attach gap.

**Independent Test**: Issue an `mso_mdoc` EAA under `x509-tenant`; confirm valid mdoc with COSE x5chain; round-trip via US2. Repeat SD-JWT VC (now carries x5c) and `register` (no chain); X.509 anchor without org cert fails closed (quickstart US3).

### Tests for User Story 3 ⚠️ (write first, must fail)

- [X] T052 [P] [US3] `MdocFormatHandler.IssueAsync` tests: produces a valid mdoc with signed MSO + x5chain; round-trips through US2 verify in `tests/Sorcha.Cryptography.Tests/Mdoc/MdocIssuanceTests.cs`.
- [ ] T053 [P] [US3] Minter x5c-attach tests: SD-JWT VC under `x509-tenant` carries `x5c`; `register` carries none; X.509 anchor with unresolved chain → fail closed (FR-020/022) in `tests/Sorcha.Haip.Service.Tests/HaipCredentialMinterChainTests.cs`.
- [X] T054 [P] [US3] Issuance-config validation tests (unsupported format/anchor combo → config error, not silent substitution) in `tests/Sorcha.Haip.Service.Tests/IssuanceConfigValidationTests.cs`.

### Implementation for User Story 3

- [ ] T055 [P] [US3] Promote/relocate the fail-soft chain resolver into a shared form usable by HAIP (mirror `IssueCredentialChainResolver.ResolveChainAsync`) and register `IOrgCertChainProvider` in `src/Services/Sorcha.Haip.Service/Program.cs`.
- [ ] T056 [US3] Add an `x5cChain` parameter to `MintCredentialAsync` (replace hardcoded `null`) and to `MintCredentialWithExternalSignerAsync` (currently drops it); forward to `CreateTokenAsync(..., x5cChain:)` in `src/Services/Sorcha.Haip.Service/Services/HaipCredentialMinter.cs`.
- [X] T057 [US3] Implement `MdocFormatHandler.IssueAsync` — build IssuerSigned + MSO, COSE_Sign1 over tag-24 MSO via the issuer signer, attach x5chain when anchored to X.509 in `src/Core/Sorcha.Blueprint.Engine/Credentials/MdocFormatHandler.cs` (depends on T044, T056).
- [ ] T058 [US3] Resolve + thread the chain (by `TrustAnchor`) and dispatch by `Format` at the issuance call site `src/Services/Sorcha.Haip.Service/Endpoints/CredentialEndpoints.cs` (~lines 337-385); X.509 anchor with no chain → fail closed (FR-020/022).
- [ ] T059 [US3] Honor `CredentialIssuanceConfig.Format`/`TrustAnchor` end-to-end (select format handler, validate combo, surface config errors) in the issuance orchestration; map mdoc claim mappings to `(namespace, element)` + `docType` (FR-004).

**Checkpoint**: All three stories independently functional; full issue→present→verify round-trip for both formats.

---

## Phase 6: Polish & Cross-Cutting Concerns

- [ ] T060 [P] Update docs: `docs/reference/API-DOCUMENTATION.md` (mdoc OpenID4VP + trust-list endpoints), `docs/reference/development-status.md`, service READMEs (Haip, Tenant), and `.specify/MASTER-TASKS.md` (📋→🚧→✅).
- [ ] T061 [P] Update the `verifiable-credentials` and `sorcha-architecture` skill files with the format seam + unified trust model + mdoc notes.
- [ ] T062 [P] Add a Strathcarron-style walkthrough or extend an existing one to exercise mdoc verify + issue against the trust list.
- [ ] T063 Verify PQC posture unchanged elsewhere (no signing-option regression — SC-009) and document the mdoc ES256/P-256-only boundary.
- [ ] T064 Coverage pass: confirm ≥85% on new trust + format logic (SC-008); fill gaps.
- [ ] T065 Run `quickstart.md` US1/US2/US3 validations end-to-end; capture results.
- [ ] T066 `dotnet build --force` + full `dotnet test`; CI grep gate that no `AcceptedIssuers`/`AddTrustedRoot` references remain (clean-break enforcement).

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (P1)**: no deps.
- **Foundational (P2)**: depends on Setup; **blocks all stories**; ends with a green build.
- **US1 (P3)**: depends on Foundational. MVP.
- **US2 (P4)**: depends on Foundational; benefits from US1's evaluator/registry being live (uses the same evaluator + adds the `trustlist` source). Independently testable.
- **US3 (P5)**: depends on Foundational; reuses US2's mdoc codec (`MdocService`, `MdocFormatHandler`) for issuance. Independently testable.
- **Polish (P6)**: after desired stories complete.

### Critical cross-story coupling

- US3 issuance reuses the mdoc codec built in US2 (T044/T043). If US3 is built before US2, those codec tasks move forward with it.
- US2's `trustlist` source plugs into the US1 `TrustResolverRegistry` (T027). US1 must land first for the registry to exist.

### Within each story

- Tests first (must fail) → models → services/resolvers → endpoints/handlers → DI/integration.

### Parallel Opportunities

- Setup T002/T003/T004 in parallel.
- Foundational T005/T006/T007, T011/T012/T013/T014 in parallel (distinct files).
- US1: tests T017-T021 in parallel; resolvers T024/T025/T026 in parallel; status adapters T022/T023 in parallel.
- US2: tests T036-T040 in parallel; codec T041/T042/T043 in parallel; provider T046 + resolver T047 in parallel.
- US3: tests T052-T054 in parallel.

---

## Parallel Example: User Story 1

```text
# Tests first (parallel):
T017 TrustEvaluatorTests | T018 per-source resolver tests | T019 cross-path parity |
T020 engine signature tests | T021 default-policy + offline re-eval

# Then resolvers (parallel):
T024 RegisterTrustSourceResolver | T025 X509TenantTrustSourceResolver | T026 DidAllowlistTrustSourceResolver
```

---

## Implementation Strategy

### MVP First (US1 only)

1. Phase 1 Setup → 2. Phase 2 Foundational (green build) → 3. Phase 3 US1 → **STOP & VALIDATE** (quickstart US1) → demo. This alone fixes the `SignatureValid=false` correctness defect and unifies trust — shippable value with zero mdoc work.

### Incremental Delivery

US1 (unified trust, SD-JWT) → US2 (accept mdoc) → US3 (issue mdoc) — each an independently testable increment.

---

## Notes

- [P] = different files, no incomplete dependency.
- Clean break: no compatibility shims for `AcceptedIssuers`/`_trustedRoots` (T066 grep gate enforces).
- FailClosed is the default everywhere; fail-open only via explicit policy.
- Engine trust/format code stays WASM-friendly (no `HttpClient`); network sources injected with in-memory variants.
- SPDX/Copyright header on every new file; `.WithSummary`/`.WithDescription` on every new endpoint.
- `dotnet build --force` before tests (stale-DLL rule).
