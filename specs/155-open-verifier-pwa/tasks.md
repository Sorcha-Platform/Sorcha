---
description: "Task list for Open Verifier PWA implementation"
---

# Tasks: Open Verifier PWA

**Input**: Design documents from `specs/155-open-verifier-pwa/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/

**Tests**: Included — the spec defines explicit Independent Tests + success criteria, and the
`sorcha-ui` skill mandates Playwright E2E for UI changes.

**Organization**: Grouped by user story (US1–US4) for independent delivery. MVP = US1.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: parallelizable (different files, no incomplete dependency)
- Paths are repository-root absolute-relative.

---

## Phase 1: Setup (Shared Infrastructure)

- [ ] T001 Add a `Verifier` test category + confirm `Sorcha.Verifier.Tests` project exists and references `Sorcha.Verifier.Engine` + `Sorcha.Verifier`; add `tests/Sorcha.UI.E2E.Tests` page-object folder entry for the verifier (no logic yet).
- [ ] T002 [P] Confirm the verifier app can resolve the Register Service client (`Sorcha.ServiceClients.Http`) — add the ProjectReference/registration scaffolding in `src/Apps/Sorcha.Verifier/Sorcha.Verifier.csproj` + `Extensions/ServiceCollectionExtensions.cs` (no behaviour yet).

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The per-layer outcome shape that US1/US2/US3 all render. MUST complete first.

- [X] T003 Add `ValidationLayer` enum (`LivePresentation`, `IssuerSignature`, `Revocation`, `RegisterAnchor`), `LayerStatus` enum (`Pass`, `Fail`, `Unverified`), and `ValidationLayerResult` record (`Layer`, `Status`, `Headline`, `Detail: IReadOnlyDictionary<string,string>`) in `src/Common/Sorcha.Verifier.Engine/Models/VerifierSession.cs`.
- [X] T004 Add `IReadOnlyList<ValidationLayerResult> Layers { get; init; } = []` to `VerificationOutcome` in the same file; keep existing fields untouched (back-compat).
- [X] T005 [P] Unit test in `tests/Sorcha.Verifier.Tests` asserting `VerificationOutcome.Layers` defaults to empty and serialises round-trip with `JsonDefaults.Api`.

---

## Phase 3: User Story 1 — Minimal-disclosure question → clear verdict (P1) 🎯 MVP

**Goal**: Operator picks "Age over 18?", citizen presents, operator sees a clear pass/fail verdict with
portrait + issuer, only age+portrait disclosed.

**Independent test**: Run verifier → "Age over 18?" → present matching credential → verdict shows
pass/fail + portrait + issuer name; only `age_over_18` + `portrait` shared.

### Engine + issuer (layers 1–2 populated)

- [ ] T006 [US1] In `src/Common/Sorcha.Verifier.Engine/VerifiablePresentationValidator.cs`, populate a `LivePresentation` `ValidationLayerResult` (KB-JWT nonce/aud/freshness + delegation chain) with Detail lines (protocol, nonce-matches, aud, kb-jwt age).
- [ ] T007 [US1] In the same validator, populate an `IssuerSignature` layer: `Pass` when JWS verifies against the resolved key, `Unverified` when key unresolved + `requireIssuerSignature:false`, `Fail` when verification fails; Detail carries `iss`, `kid`, `alg`, resolution source. Surface the resolved issuer DID + display name for the UI.
- [ ] T008 [P] [US1] Unit tests in `tests/Sorcha.Verifier.Tests` for T006/T007: valid presentation → LivePresentation Pass + IssuerSignature Pass; tampered JWS → IssuerSignature Fail.

### Issuance: age_over_18 disclosable claim (demo credential)

- [ ] T009 [US1] In `walkthroughs/AssuredIdentity/blueprints/assured-identity.json`, add an `age_over_18` boolean to the analyst action data (derived from DOB), add claim mapping `{ "claimName": "age_over_18", "sourceField": "/ageCheck/over18" }`, and add `age_over_18` to `disclosable[]`.
- [ ] T010 [US1] Update `walkthroughs/AssuredIdentity/run-phase1-identity.ps1` to (a) ensure the issuing org has an org master key (`Set-SorchaOrgMasterKey`) so the issuer signature resolves, and (b) supply the over-18 + portrait inputs.

### Verifier UI: presets + verdict

- [ ] T011 [P] [US1] Add `QuestionPresets.cs` in `src/Apps/Sorcha.Verifier/Services` with `age-over-18` (vct=AssuredIdentity, required `age_over_18`,`portrait`), `confirm-identity`, and `custom` presets.
- [ ] T012 [US1] Redesign `src/Apps/Sorcha.Verifier/Components/Pages/Index.razor` into the Ask screen: preset chips → on select, set vct/required/optional; `custom` reveals the existing fields; Start builds the request via the unchanged `IPresentationRequestBuilder`.
- [ ] T013 [US1] Add `VerdictViewModel` in `src/Apps/Sorcha.Verifier/Services` mapping `VerificationOutcome` → `OverallPass`, `Headline`, `IssuerDisplayName`, `IssuerDid`, `PortraitBase64`, `DisclosedClaims`, `WithheldClaims` (requested-but-undisclosed ∪ issued-not-requested), `Layers`.
- [ ] T014 [US1] Redesign `src/Apps/Sorcha.Verifier/Components/Pages/Outcome.razor` into the verdict screen — `IdCardLayout`-style header (portrait + headline + age chip + issuer name/DID), wallet look (MudBlazor + Sorcha theme). (Trail steps added in US2.)
- [ ] T015 [US1] Extend `SessionStatusResponse` + `/verify/r/{sessionId}/status` in `src/Apps/Sorcha.Verifier/Endpoints/PresentationResponseEndpoints.cs` to carry `Layers` + `Issuer { displayName, did }`.
- [ ] T016 [P] [US1] E2E test `tests/Sorcha.UI.E2E.Tests/Docker/Verifier/VerifierVerdictTests.cs` (+ page object): "Age over 18?" → present (demo-mint or wallet) → verdict shows pass + portrait + issuer; assert only age+portrait disclosed (SC-002).

**Checkpoint**: US1 is a working minimal-disclosure verifier with a designed verdict.

---

## Phase 4: User Story 2 — Full validation trail (P2)

**Goal**: Expandable four-layer trail under the verdict, each step with raw detail; disclosed vs withheld
visible.

**Independent test**: After a verdict, all four steps listed with status; expand each to its detail;
selective-disclosure block shows disclosed + withheld.

- [ ] T017 [US2] Add the Revocation layer population in `VerifiablePresentationValidator` from `IStatusListCache.CheckAsync` (Active→Pass, Revoked→Fail, Unverifiable→Unverified) with Detail (status-list uri, idx, fresh).
- [ ] T018 [P] [US2] Unit test: revoked credential → Revocation layer `Fail`; status-list unfetchable → `Unverified` (fail-closed but distinct).
- [ ] T019 [US2] Build the validation-trail timeline in `Outcome.razor`: each layer a row (label left; status text + tick + `▾` grouped right per the approved v3 mockup), collapsible Detail panel; render the four layers from `VerdictViewModel.Layers`.
- [ ] T020 [US2] Add the Selective-disclosure expandable block (disclosed list + withheld list struck-through, "never left the wallet") above/within the trail.
- [ ] T021 [US2] Implement the overall-verdict rule in `VerdictViewModel`: `OverallPass = Accepted AND no Fail layer`; `Unverified` never vetoes; revoked (Revocation Fail) → overall not-valid even with IssuerSignature Pass (SC-005).
- [ ] T022 [P] [US2] E2E test: complete a verification, expand each trail step + the disclosure block, assert detail visible/collapsible (SC-003); revoked-credential case asserts overall "not valid" with signature still Pass (SC-005).

**Checkpoint**: US2 adds the trust-story drill-down.

---

## Phase 5: User Story 3 — Register-anchor cross-check (P2, headline)

**Goal**: Explicit "verify against the register" action resolves the credential's anchor, verifies the
inclusion proof, shows sealing detail, exports a bundle.

**Independent test**: Verify an anchored credential → trigger cross-check → anchored ✓ with docket/seal +
exportable bundle; unanchored credential → "unverified" not silent fail.

### Register Service: public anchor read

- [ ] T023 [US3] Add `GetCredentialIssuanceTransactionAsync(registerId, credentialId, ct)` to `src/Core/Sorcha.Register.Core/Storage/IReadOnlyRegisterRepository.cs` and implement on EF + Mongo + in-memory repos (query `MetaData.TrackingData["type"]=="credential-issuance"` AND `["credentialId"]==credentialId`).
- [ ] T024 [US3] Add public `GET /api/registers/{registerId}/credentials/{credentialId}/anchor` in `src/Services/Sorcha.Register.Service/Endpoints/VerificationEndpoints.cs` (`.AllowAnonymous()`), returning `CredentialAnchorResponse` (txId, docketNumber, sealedAt, status, inclusionProof) per `contracts/register-anchor-endpoint.openapi.yaml`; 404 when not found; `.WithSummary()`/`.WithDescription()` + XML docs.
- [ ] T025 [P] [US3] Unit/integration test `tests/Sorcha.Register.Service.Tests` for T023/T024: seeded credential-issuance tx → 200 with valid inclusion proof; unknown credentialId → 404; verify proof against `POST /inclusion-proofs/verify`.

### Issuance: anchor claim

- [ ] T026 [US3] Add `registerAnchor` (registerId) claim to AssuredIdentity `credentialIssuanceConfig` (mapping `/issuanceContext/registerId`, disclosable) and inject `/issuanceContext/registerId` into merged data before `BuildClaimsFromMappings` in `src/Services/Sorcha.Blueprint.Service/Services/Implementation/ActionExecutionService.cs`.

### Verifier: anchor client + UI + export

- [ ] T027 [US3] Add `IRegisterAnchorClient` + impl in `src/Apps/Sorcha.Verifier/Services` calling the public anchor endpoint, verifying the returned Merkle proof, returning `RegisterAnchorResult` (Anchored, Status, TxId, DocketNumber, SealedAt, BundleJson). Register in DI.
- [ ] T028 [US3] Wire the RegisterAnchor trail step in `Outcome.razor` as a "tap to verify inclusion proof" action: reads `registerAnchor` claim + credential `jti`, calls `IRegisterAnchorClient`, appends/updates the `RegisterAnchor` `ValidationLayerResult` (Pass/Fail/Unverified) with docket/seal Detail.
- [ ] T029 [P] [US3] Add "Export verification bundle" affordance (fetch `GET .../verification-bundle` or reuse the anchor result's bundle) producing a downloadable JSON re-checkable via `POST /verification-bundles/verify` (FR-011).
- [ ] T030 [P] [US3] E2E test: anchored credential → cross-check → anchored ✓ + bundle export re-verifies (SC-004); unanchored credential → RegisterAnchor `Unverified` while other layers stand (FR-013 edge case).

**Checkpoint**: US3 delivers the headline open-register cross-check.

---

## Phase 6: User Story 4 — Install as an app (P3)

**Goal**: Installable standalone PWA under `/verify/`.

**Independent test**: open in supported browser → install affordance → install → standalone launch reaches Ask screen.

- [ ] T031 [P] [US4] Add `src/Apps/Sorcha.Verifier/wwwroot/manifest.webmanifest` (name, short_name, `start_url`/`scope` under `/verify/`, `display: standalone`, theme/background colours matching the wallet) + icons (192, 512, maskable) in `wwwroot/icons/`.
- [ ] T032 [P] [US4] Add `src/Apps/Sorcha.Verifier/wwwroot/service-worker.js` caching the static shell + an offline-fallback page (do NOT cache the circuit / host page as immutable); scope `/verify/`.
- [ ] T033 [P] [US4] Add `src/Apps/Sorcha.Verifier/wwwroot/js/pwa-install.js` (`beforeinstallprompt` capture + install button hook).
- [ ] T034 [US4] In `src/Apps/Sorcha.Verifier/Components/App.razor`, add the manifest `<link>`, theme/meta tags, SW registration, and the install button affordance.
- [ ] T035 [US4] Verify/adjust gateway routing so `/verify/manifest.webmanifest` + `/verify/service-worker.js` are served with the correct scope (path-prefix gotcha) — config in the API Gateway (`Sorcha.ApiGateway`) and `appsettings`.
- [ ] T036 [P] [US4] E2E/manual check documented in quickstart: install affordance present; standalone launch reaches Ask screen (SC-006).

**Checkpoint**: US4 makes it installable.

---

## Phase 7: Polish & Cross-Cutting

- [ ] T037 [P] Add OTel counters on the `Sorcha.Verifier` meter for anchor cross-check outcome (`sorcha_verifier_anchor_check_total{outcome}`) and per-layer results; structured logging only.
- [ ] T038 [P] Edge-case UX: decline/timeout/no-match states in `VerifierSession.razor`/`Outcome.razor` (non-alarming, restart/regenerate affordances) per spec Edge Cases.
- [ ] T039 [P] Docs: update `src/Apps/Sorcha.Verifier` README + `docs/reference/API-DOCUMENTATION.md` (new anchor endpoint) + `.claude/skills/sorcha-architecture/SKILL.md` (F114 verifier surface → add F155 anchor read + PWA + per-layer outcome).
- [ ] T040 Run `dotnet build` (no Release warnings) + full `dotnet test` for `Sorcha.Verifier.Tests` + `Sorcha.Register.Service.Tests`; run the `Category=Verifier` E2E against Docker; confirm SC-001..SC-006 in `quickstart.md`.

---

## Dependencies & order

- **Setup (P1)** → **Foundational (P2)** → **US1 (P3)** → US2/US3 (P4/P5, both depend on US1; US3 also needs T023/T024 which are independent of US2) → **US4 (P6)** → **Polish (P7)**.
- US2 and US3 are independent of each other and can proceed in parallel once US1's verdict shape + T003/T004 exist (US3's Register-side T023–T025 can even start right after Foundational).
- US4 is fully independent and can be done any time after Setup.

## Parallel opportunities

- Within US1: T008, T011, T016 are `[P]`; T009/T010 (issuance) parallel to T011–T014 (UI).
- US3 Register-side (T023–T025) ∥ US3 issuance (T026) ∥ verifier-side once endpoint exists.
- All US4 asset tasks (T031–T033, T036) are `[P]`.

## MVP

**US1 (Phase 1–3)** = a working installable-later verifier that asks "Age over 18?", verifies a real
minimal-disclosure presentation, and shows a designed pass/fail verdict with issuer + portrait. Demoable
on its own.
