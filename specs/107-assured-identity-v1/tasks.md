# Tasks: Assured Identity v1

**Input**: Design documents from `/specs/107-assured-identity-v1/`
**Prerequisites**: plan.md ✓, spec.md ✓, research.md ✓, data-model.md ✓, contracts/ ✓, quickstart.md ✓
**Tests**: Included per Sorcha constitution Principle IV (≥85% coverage on new code)
**Organization**: Tasks are grouped by user story so each phase ships as an independent PR.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Different files, no incomplete dependencies — safe to run in parallel
- **[Story]**: Maps to a user story (US1–US5)
- All file paths are absolute from repo root `C:\Projects\Sorcha`

## Path Conventions

Web app (microservices backend + Blazor WASM frontend):
- Frontend renderer: `src/Apps/Sorcha.UI/Sorcha.UI.Core/`
- Blueprint models: `src/Common/Sorcha.Blueprint.Models/`
- Validator core: `src/Common/Sorcha.Validator.Core/`
- Backend services: `src/Services/Sorcha.{Service}.Service/`
- Walkthroughs: `walkthroughs/{Name}/`
- Tests: `tests/Sorcha.{Project}.Tests/`
- Repo-root files: `docker-compose.federation.yml`, `CLAUDE.md`, `.gitignore`

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Per-feature workspace prep. The repository, tooling, and CI are already configured.

- [ ] T001 Verify local Sorcha dev environment is healthy by running `docker-compose up -d` and confirming Blueprint, Validator, Tenant, Wallet, Register, Haip, and UI services all reach Healthy state. Baseline for the rest of this feature.
- [ ] T002 [P] Confirm `dotnet build` of the entire solution from `C:\Projects\Sorcha` completes with no warnings on a clean checkout of branch `107-assured-identity-v1`. Cold-start baseline.
- [ ] T003 [P] Confirm `dotnet test --filter "Category=Smoke"` passes against the Docker stack so we know the existing test infrastructure is green before any new tests land.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Cross-cutting prerequisites that block multiple user stories.

**⚠️ CRITICAL**: This phase is intentionally minimal — Sorcha already has the cross-cutting infrastructure (DI, EF Core, MongoDB, Redis, OpenTelemetry, JWT, rate limiting, Scalar OpenAPI, the file-chunks pipeline, Wave 14b claim card, register-native delivery, HAIP issuance, schema $ref resolution). Reserve the new validator warning codes so downstream guardrail and issuance work has a stable contract from day one.

- [ ] T004 Reserve two new validator warning codes in `src/Services/Sorcha.Validator.Service/Models/ValidationErrorCodes.cs` (or wherever publish-time codes are constants today): `WARN_BP_REVIEW_001` (unknown x-review layout variant — surfaces a publish-time warning, not error, renderer falls back to tabular) and `WARN_CRED_PORTRAIT_OVERSIZE_001` (portrait token exceeded ~27KB base64 size bound at issuance — claim omitted, credential still issued). Add XML doc summaries referencing `specs/107-assured-identity-v1/contracts/x-review-extension.md` and `contracts/portrait-claim-format.md` respectively. No behaviour wiring yet — that lands in T026 and T024.

**Checkpoint**: Foundation ready — all five user-story phases can now begin.

---

## Phase 3: User Story 1 — Citizen obtains a canonical Assured Identity credential with a polished form experience (Priority: P1) 🎯 MVP

**Goal**: A public-org citizen runs the 5-page Assured Identity wizard end-to-end (name+DoB → address → email → optional photo → ID-card review) and receives a `AssuredIdentityCredential` in their chosen wallet. Includes the renderer polish (DOB future-block, photo capture dispatch, x-review extension with id-card layout) plus the AssuredIdentity blueprint, plus a single-phase walkthrough to prove it.

**Independent Test**: Fresh public account on a fresh deployment. Run `walkthroughs/AssuredIdentity/run-phase1-identity.ps1`. Verify: the credential lands in the holder's wallet with all expected claims; the photo (when provided) is embedded as a 240×320 token in the `portrait` claim; the date-of-birth picker prevented future dates client-side; the review screen rendered as an ID card with edit-jump navigation.

### Tests for User Story 1 ⚠️

> **NOTE**: Write these tests FIRST, ensure they FAIL, then implement.

- [ ] T005 [P] [US1] Write `DateTimeRendererFutureBlockTests` in `tests/Sorcha.UI.Core.Tests/Components/Forms/DateTimeRendererFutureBlockTests.cs` covering: `formatMaximum: "today"` blocks all future dates in the picker; `formatMaximum: "today-18Y"` blocks anyone under 18; `formatMinimum: "today"` blocks past dates; tokens parse via `SorchaDateTokenResolver`; absent constraints render the standard picker without bounds. Tests MUST fail before T013.
- [ ] T006 [P] [US1] Write `XFileExtensionParserTests` in `tests/Sorcha.Blueprint.Models.Tests/XFileExtensionParserTests.cs` covering: existing fields still parse; new `capture` field parses (`user`, `environment`, null); new `embedAs` field parses (`image-token-jpeg-240x320`, null); unknown values produce a publish warning, not an error. Tests MUST fail before T014.
- [ ] T007 [P] [US1] Write `FileRendererCaptureTests` in `tests/Sorcha.UI.Core.Tests/Components/Forms/FileRendererCaptureTests.cs` covering: `capture: "user"` propagates to the `<InputFile>` `capture` HTML attribute; `capture: "environment"` propagates correctly; null disables the attribute (legacy behaviour); `embedAs` non-null triggers the resize pipeline (mock `IPhotoTokenResizer`); `embedAs` null skips resize. Tests MUST fail before T015 + T017.
- [ ] T008 [P] [US1] Write `PhotoTokenResizerTests` in `tests/Sorcha.UI.Core.Tests/Services/Forms/PhotoTokenResizerTests.cs` covering: input image at 1920×1080 resizes to 240×320 (cover-style centre crop); output is JPEG; output base64 is ≤27KB; resizer steps quality from 0.85 down to floor 0.5 until size target met; resizer surfaces a "too detailed" error if floor reached without meeting target. Mock the JS interop layer to return canonical fixture bytes. Tests MUST fail before T016.
- [ ] T009 [P] [US1] Write `XReviewExtensionParserTests` in `tests/Sorcha.Blueprint.Models.Tests/XReviewExtensionParserTests.cs` covering: valid extension with all fields parses correctly; missing `header.issuerName` is a publish error; missing `header.credentialName` is a publish error; unknown `layout` value emits `WARN_BP_REVIEW_001` (warning, not error); `editable` defaults to true when absent; unknown `colourTheme` falls back to default. Tests MUST fail before T020.
- [ ] T010 [P] [US1] Write `ReviewSummaryDataSourceTests` in `tests/Sorcha.UI.Core.Tests/Services/Forms/ReviewSummaryDataSourceTests.cs` covering: data source pulls field values from `FormContext.FormData` keyed by JSON pointer; iterates fields declared on prior pages only (not the review page itself); handles nested object pointers (e.g. `/address/town`); returns null for fields the citizen left unfilled. Tests MUST fail before T021.
- [ ] T011 [P] [US1] Write `ReviewSummaryRendererTests` in `tests/Sorcha.UI.Core.Tests/Components/Forms/ReviewSummaryRendererTests.cs` covering: dispatches to `IdCardLayout` for `layout: "id-card"`; falls back to tabular minimal for unknown layouts; passes `IdCardLayoutConfig` with correct watermark per action context (Draft / Pending / Issued); generates Edit-X buttons when `editable: true`; Edit-X buttons fire `OnEditSection(pageIndex)` with the correct originating page index. Tests MUST fail before T023.
- [ ] T012 [P] [US1] Write `BuildClaimsFromMappingsPortraitTests` in `tests/Sorcha.Blueprint.Service.Tests/BuildClaimsFromMappingsPortraitTests.cs` covering: `portrait` claim included when `/portrait/tokenImageBase64` present and ≤27KB; claim omitted when absent (citizen skipped photo); claim omitted with `WARN_CRED_PORTRAIT_OVERSIZE_001` log when present and >27KB; existing claim mappings still resolve correctly. Tests MUST fail before T026.

### Implementation for User Story 1

#### 1a. DOB future-block

- [ ] T013 [US1] Wire `SorchaDateTokenResolver` into `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Forms/DateTimeRenderer.razor`. On the bound `MudDatePicker`, set `MaxDate` from `formatMaximum` and `MinDate` from `formatMinimum` after token resolution. Both client-side bounds graceful-fall-back to no bound when constraints absent. Server-side validator continues to be authoritative — this is convenience.

#### 1b. Photo capture dispatch

- [ ] T014 [P] [US1] Extend `src/Common/Sorcha.Blueprint.Models/Schema/XFileExtension.cs` (or equivalent) with two new optional fields: `Capture` (nullable enum `User | Environment`) and `EmbedAs` (nullable enum, v1 only `ImageTokenJpeg240x320`). Update the parser to read them from JSON without breaking existing `x-file` consumers. Surface unknown enum values via the publish warning channel.
- [ ] T015 [P] [US1] Modify `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Forms/FileRenderer.razor` to read `XFileCaptureConfig.Capture` from the schema and propagate to the rendered `<InputFile>` element's `capture` HTML attribute when non-null. Desktop browsers ignore the attribute by HTML spec; on mobile this opens the device camera with the requested facing.
- [ ] T016 [P] [US1] Implement `src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/Forms/PhotoTokenResizer.cs` and the matching JS module `wwwroot/js/photo-token-resizer.js`. Public method `Task<string> ResizeAsync(IBrowserFile file, ImageTokenSpec spec)`. JS side: load file into a `<canvas>`, scale + centre-crop to spec dimensions, export as JPEG with progressive quality reduction (0.85 → 0.5 floor) until size target met, return base64. Expose via Blazor JS interop.
- [ ] T017 [US1] Wire `PhotoTokenResizer` into `FileRenderer.razor` (depends on T015 + T016). When `XFileCaptureConfig.EmbedAs == ImageTokenJpeg240x320`, after the citizen picks/captures a file: invoke the resizer, store the base64 token at `<bound-pointer>/tokenImageBase64` in `_formContext.FormData`, continue with the existing chunked-upload flow for the original. When EmbedAs is null, behaviour identical to today.
- [ ] T018 [US1] Add ICAO composition guidance panel to `FileRenderer.razor` rendered as a sibling of the capture control, only when `XFileCaptureConfig.EmbedAs == ImageTokenJpeg240x320`. Static markup; tips listed in `contracts/portrait-claim-format.md` § Composition guidance. Localisable via existing resource pattern.

#### 1c. `x-review` extension and ID card layout

- [ ] T019 [P] [US1] Add `XReviewExtension` record + `XReviewLayoutVariant` enum + `XReviewColourTheme` enum + `XReviewHeader` record to `src/Common/Sorcha.Blueprint.Models/Schema/`. See `contracts/x-review-extension.md` § Parser changes for the exact shape.
- [ ] T020 [P] [US1] Extend `src/Common/Sorcha.Blueprint.Models/SchemaLayoutParser.cs` to recognise `x-review` on a page and emit `XReviewExtension`. Validate at parse time: required header fields present; unknown `layout` produces `WARN_BP_REVIEW_001`; `editable` defaults true; pages with `x-review` MUST NOT declare `properties` (warning).
- [ ] T021 [US1] Implement `src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/Forms/ReviewSummaryDataSource.cs` (depends on T019). Method `IdCardLayoutConfig BuildConfig(XReviewExtension extension, FormContext formContext, ActionRuntimeState runtimeState)`. Pull values from `formContext.FormData` for fields declared on prior pages; build `IdCardSection` list mapping section titles back to originating page indices; derive watermark from runtimeState.
- [ ] T022 [US1] Implement `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Forms/Layouts/IdCardLayout.razor` + `IdCardLayout.razor.css`. Parameters: `IdCardLayoutConfig Config`, `EventCallback<int> OnEditSection`, `RenderFragment? FooterActions`. CSS provides `identity-navy` theme as the v1 baseline (DLA `licence-pink` lands in T036). Watermark rendered per `Config.Watermark` enum. Photo slot on the left, name + structured details on the right, header with issuer + credential name + accent dot, footer with SD-JWT tagline + state.
- [ ] T023 [US1] Implement `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Forms/ReviewSummaryRenderer.razor`. Reads `XReviewExtension` from the current page, calls `ReviewSummaryDataSource.BuildConfig(...)`, dispatches by `Layout` enum (v1: `IdCard` → `IdCardLayout`; other variants log a warning + render minimal tabular fallback). Wires Edit-X event up to `Wizard.NavigateToPage(pageIndex)` with form state preserved.
- [ ] T024 [US1] Modify `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Forms/ControlDispatcher.razor` to detect a page with `XReviewExtension` and dispatch to `ReviewSummaryRenderer.razor` instead of the default field-rendering loop.
- [ ] T025 [US1] Modify `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Forms/SorchaFormRenderer.razor` to: treat `x-review` pages as read-only (no validation pass on bound model for that page); ensure the page's footer action set comes from the hosting action's routes (Submit/Edit on citizen-side draft, Approve/Reject on assessor-side pending).

#### 1d. Server-side portrait size gate

- [ ] T026 [US1] Modify `BuildClaimsFromMappings` in `src/Services/Sorcha.Blueprint.Service/Services/Implementation/ActionExecutionService.cs` (or wherever the claim builder lives) to validate `tokenImageBase64` source-field length ≤27_000 chars before claim inclusion. On overrun: omit the claim, emit `WARN_CRED_PORTRAIT_OVERSIZE_001` structured log, surface to citizen's submission response. Credential issuance proceeds without the portrait claim.

#### 1e. AssuredIdentity blueprint

- [ ] T027 [US1] Author `walkthroughs/AssuredIdentity/blueprints/assured-identity.json`. Three actions: 1) open citizen submission with 5-page wizard (Page 1 Name+DoB via `x-sections`, Page 2 Address with postcode lookup, Page 3 Email, Page 4 optional Photo with `x-file.capture: "user"` + `x-file.embedAs: "image-token-jpeg-240x320"`, Page 5 review via `x-review`); 2) assessor approve/reject (decision schema, agent-friendly shape); 3) citizen claim (existing `x-credential-offer` Wave 14b card with dual-path). Reference core schema `$ref` for `PersonName/v1`, `DateOfBirth/v1` (with `formatMaximum: "today"`), `EmailAddress/v1`, `PostalAddress/v1`. Citizen participant has `walletAddress: null` (open). Government assessor participant has a known wallet (filled by setup). Credential type `AssuredIdentityCredential` with claim mappings per `contracts/assured-identity-credential.md`. Both delivery modes declared (register-native + HAIP external).

#### 1f. Walkthrough scaffolding (Phase 1 only)

- [ ] T028 [US1] Create the `walkthroughs/AssuredIdentity/` directory tree: `README.md` (overview, prereqs, run instructions), `blueprints/`, `actors/`, `data/`. Add `data/sample-portrait.jpg` (an ICAO-compliant 480×640 JPEG suitable for the demo citizen).
- [ ] T029 [US1] Implement `walkthroughs/AssuredIdentity/setup.ps1`. Idempotent. Provisions: Government org (`gov-scotland` subdomain), Government admin user, Government wallet (ED25519), Government-as-HAIP-issuer trust enrolment, Citizen public-org account with persona profile pre-populated for the demo, AssuredIdentity register, blueprint publish via `Publish-SorchaBlueprint` (citizen omitted from `$walletMap` per Feature 103 contract). Save state to `state.json`.
- [ ] T030 [US1] Implement `walkthroughs/AssuredIdentity/run-phase1-identity.ps1`. Reads `state.json`. Creates a blueprint instance. Calls action 1 as the citizen with a payload that includes the photo (chunked-uploaded + token). Polls for the assessor approval. Polls for the credential offer. Claims into the citizen's HAIP wallet-dir via `sorcha-agent haip receive`. Asserts the credential exists in the wallet-dir with all expected claims including `portrait`. Logs each step.
- [ ] T031 [US1] Add a `assured-identity` entry to `walkthroughs/.secrets/passwords.json` template with the demo passwords (gov-admin, citizen). Update `walkthroughs/initialize-secrets.ps1` if needed. Document in the README.

#### 1g. Validation pass

- [ ] T032 [US1] Run all renderer test suites (T005–T012) and verify they now pass. Specifically: `dotnet test tests/Sorcha.UI.Core.Tests --filter "Class=DateTimeRendererFutureBlockTests|FileRendererCaptureTests|PhotoTokenResizerTests|ReviewSummaryRendererTests|ReviewSummaryDataSourceTests"` and `dotnet test tests/Sorcha.Blueprint.Models.Tests --filter "Class=XFileExtensionParserTests|XReviewExtensionParserTests"` and `dotnet test tests/Sorcha.Blueprint.Service.Tests --filter "Class=BuildClaimsFromMappingsPortraitTests"`.
- [ ] T033 [US1] Run `walkthroughs/AssuredIdentity/run-phase1-identity.ps1` end-to-end against the local Docker stack. Verify: citizen submits wizard with photo; agent approves (manually or via Phase 5 actor); citizen claims credential; credential exists in `walkthroughs/AssuredIdentity/wallet/credentials/AssuredIdentityCredential.sdjwt` with all claims including `portrait`. Capture output in PR description.

**Checkpoint**: User Story 1 is fully functional and shippable as PR 1. The Assured Identity workflow runs end-to-end. The renderer polish (DOB block, photo capture, ID card review) lands as reusable platform capability.

---

## Phase 4: User Story 2 — Downstream service chain-consumes the Assured Identity credential (Priority: P2)

**Goal**: A citizen who already holds an `AssuredIdentityCredential` applies for a Driving Licence. DLA verifies identity via OpenID4VP presentation, issues a `DrivingLicenceCredential` with the citizen's portrait carried forward (when present), and the citizen claims the licence into the same wallet.

**Independent Test**: Starting from a clean state, run Phase 1 (US1) to receive an AssuredIdentityCredential. Then run `walkthroughs/AssuredIdentity/run-phase2-licence.ps1`. Verify: the DLA review screen renders both stacked id-cards; the citizen presents only `givenName`, `familyName`, `dateOfBirth`, `portrait` (email and address withheld); the licence credential is signed and lands in the citizen's wallet; the licence's portrait matches the identity's portrait.

### Tests for User Story 2 ⚠️

- [ ] T034 [P] [US2] Write `BuildClaimsFromMappingsCarryForwardTests` in `tests/Sorcha.Blueprint.Service.Tests/BuildClaimsFromMappingsCarryForwardTests.cs` covering: claim mappings sourced from `/presentedClaims/*` resolve correctly when an action has a verified-presentation context; `holderPortrait` carries forward when the citizen disclosed `portrait` from their identity; `holderPortrait` is omitted when the citizen withheld `portrait` (still a valid licence). Tests MUST fail before T035.

### Implementation for User Story 2

- [ ] T035 [US2] Author `walkthroughs/AssuredIdentity/blueprints/driving-licence.json`. Four actions: 1) open citizen submission with 2-page wizard (vehicle class + review via `x-review` with `licence-pink` theme); 2) DLA verification action with `credentialRequirements` referencing `AssuredIdentityCredential` (required claims: `givenName`, `familyName`, `dateOfBirth`; optional claims: `portrait`); 3) DLA approve+issue action with stacked-cards review (presented identity + licence-to-be) and `credentialIssuanceConfig.claimMappings` carrying forward from `/presentedClaims/*`; 4) citizen claim card. Citizen participant has `walletAddress: null` (open). DLA officer participant has known wallet (filled by setup). Credential type `DrivingLicenceCredential` with claims per `contracts/driving-licence-credential.md`. 10-year expiry.
- [ ] T036 [US2] Add `licence-pink` colour theme to `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Forms/Layouts/IdCardLayout.razor.css` per `contracts/x-review-extension.md` § CSS theming. Add `LicencePink` enum value to `XReviewColourTheme` (depends on T019). UK driving licence pink convention.
- [ ] T037 [US2] Extend `ReviewSummaryRenderer.razor` (T023) to render two stacked cards on a review page when the hosting action declares both `credentialRequirements` (presented credential) AND `credentialIssuanceConfig` (credential-to-be). Top card pulls disclosed claims from the verified-presentation context, renders withheld claims as faded "— — —" with explanatory caption. Bottom card pulls from the action payload + claim mappings. Each card uses its own `IdCardLayoutConfig`.
- [ ] T038 [US2] Extend `walkthroughs/AssuredIdentity/setup.ps1` to additionally provision: DLA org (`dla-scotland` subdomain), DLA admin user, DLA wallet, DLA-as-HAIP-issuer trust enrolment, Driving Licence register (or reuse the AssuredIdentity register — per design simplicity), publish `driving-licence.json` blueprint. Idempotent.
- [ ] T039 [US2] Implement `walkthroughs/AssuredIdentity/run-phase2-licence.ps1`. Reads `state.json`. Verifies the citizen holds an `AssuredIdentityCredential` in the wallet-dir (Phase 1 prerequisite). Creates the licence blueprint instance. Submits action 1 with `vehicleClass: "Car (B)"`. Calls `sorcha-agent haip present` to disclose the requested claims to action 2. Polls for DLA approval (action 3). Claims the licence credential. Asserts the licence exists in the wallet-dir with `portrait` carried forward.
- [ ] T040 [US2] Implement `walkthroughs/AssuredIdentity/run.ps1`. Runs `setup.ps1` (idempotent), `run-phase1-identity.ps1`, `run-phase2-licence.ps1` in sequence. Asserts both credentials in the wallet-dir at the end. Logs total elapsed time for the SC-001 + SC-002 budget check.

### Validation pass

- [ ] T041 [US2] Run `BuildClaimsFromMappingsCarryForwardTests` (T034) and verify all cases pass.
- [ ] T042 [US2] Run `walkthroughs/AssuredIdentity/run.ps1` end-to-end against local Docker stack. Verify: both credentials in the wallet; the licence's `holderPortrait` claim matches the identity's `portrait` claim byte-for-byte; the DLA review screen showed both stacked id-cards (visual confirmation); the citizen's email and address never reached the DLA (verified by inspecting the action 2 payload). Capture output in PR description.

**Checkpoint**: User Story 2 is fully functional and shippable as PR 2. The credential chain is proven; the DLA exercises the full HAIP round-trip including selective disclosure and KB-JWT.

---

## Phase 5: User Story 3 — Unattended assessment by a background agent (Priority: P3)

**Goal**: Both walkthrough phases run without a human opening any assessor UI. `sorcha-agent` actors in rules mode pick up pending applications from their inboxes and post approve decisions automatically.

**Independent Test**: Run `walkthroughs/AssuredIdentity/run.ps1` with the actors started in the background by the script. Verify: no human intervention required between submission and approval; both credentials issued; the human assessor UI still renders correctly if a developer opens it during the pending window (manual check).

### Implementation for User Story 3

- [ ] T043 [P] [US3] Author `walkthroughs/AssuredIdentity/actors/citizen.json`. HAIP wallet-dir at `walkthroughs/AssuredIdentity/wallet/`. Polling mode with SignalR fallback. No rules — citizen actor is interactive (driven by `run-phase1-identity.ps1` and `run-phase2-licence.ps1` directly), this config is for `sorcha-agent validate` and reference only.
- [ ] T044 [P] [US3] Author `walkthroughs/AssuredIdentity/actors/gov-assessor.json`. Rules-mode. Single rule that approves the "Verify Assured Identity Application" action (action name from blueprint), with payload `{ "decision": "approved", "verificationNotes": "Auto-approved by demo agent" }`. SignalR + polling enabled. Logging to `walkthroughs/AssuredIdentity/logs/gov-assessor-actions.jsonl`.
- [ ] T045 [P] [US3] Author `walkthroughs/AssuredIdentity/actors/dla-officer.json`. Rules-mode. Two rules: one approves the verification action (action 2); one approves the issuance action (action 3) with payload generating the licence number, dates, holder name. SignalR + polling enabled. Logging.
- [ ] T046 [US3] Update `walkthroughs/AssuredIdentity/run.ps1`, `run-phase1-identity.ps1`, and `run-phase2-licence.ps1` to start `sorcha-agent` background processes for the gov-assessor and dla-officer actors before submitting actions, and to clean them up on exit (success or failure). Use the same pattern as `walkthroughs/ConstructionPermit/run-agents.ps1` for process management. Pass passwords via env vars from `state.json`.
- [ ] T047 [US3] Run `walkthroughs/AssuredIdentity/run.ps1` end-to-end with NO human in the loop. Verify: both credentials issued within the SC-008 budget (assessor approval ≤ 30s after submission); no script-level state ferrying needed between phases; the citizen actor's wallet-dir contains both credentials at the end. Confirm a developer can still open the gov-assessor UI during the pending window and see the standard review screen (the agent simply gets there first).

**Checkpoint**: User Story 3 is fully functional and shippable as PR 3. The walkthrough is demo-runnable without any human intervention. The agent-based design preserves the seam for future AI-mode and external-API-mode integrations.

---

## Phase 6: User Story 4 — Cross-peer delivery smoke test (Priority: P4)

**Goal**: A two-peer Sorcha federation can be brought up locally; an `AssuredIdentityCredential` issued on peer A reaches the citizen's in-platform wallet on peer B via register-native delivery within the SC-009 latency budget; findings are documented on every run regardless of outcome.

**Independent Test**: `docker compose -f docker-compose.federation.yml up -d` brings both peers healthy. `walkthroughs/AssuredIdentity/run-multi-peer.ps1` produces a findings document. The committed baseline shows a recent pass (or documents the anomaly if the smoke surfaces a replication issue).

### Implementation for User Story 4

- [ ] T048 [US4] Author `docker-compose.federation.yml` at the repo root. Two peer stacks (peer-a, peer-b), each with the full set of services (api-gateway, blueprint, register, validator, wallet, tenant, haip, peer) and per-peer DBs (postgres, mongo, redis). Shared docker network `sorcha-federation`. Per-peer environment overrides for `Peer:NodeId`, `Peer:Seeds`, gateway port (8081 / 8082). Per `contracts/docker-compose-federation.md`. Use `extends:` against the existing `docker-compose.yml` service definitions where possible to avoid duplication.
- [ ] T049 [US4] Implement `walkthroughs/AssuredIdentity/run-multi-peer.ps1`. Sequence per `contracts/docker-compose-federation.md` § Setup behaviour: `down -v`, `up -d`, wait for both peers healthy, provision Government on peer A only, provision citizen on peer B, subscribe peer B to peer A's register, run Phase 1 with citizen on peer B and assessor agent on peer A, **using register-native delivery** (the citizen claims to in-platform Sorcha wallet via the Wave 14b card's "Store in my Sorcha wallet" path, not the HAIP external path). Assert the credential surfaces in the citizen's MyCredentials PENDING tab on peer B. Citizen accepts; verify the accept transaction is signed by the holder's key (FR-040). Record all milestones with timings.
- [ ] T050 [US4] Implement findings document generation in `run-multi-peer.ps1` per `contracts/cross-peer-findings-format.md`. YAML frontmatter (timestamp, peer versions, outcome enum, latencies). Five fixed sections (Topology, Timings, Anomalies, Reproduction, Outcome rationale). Outcome computed automatically: pass if all milestones within budget; degraded-pass if any milestone exceeds budget but credential eventually arrives; fail if milestone never reached within 60s; env-failure if docker-compose fails. Writes to `walkthroughs/AssuredIdentity/multi-peer-findings/<timestamp>.md` (per-run, gitignored).
- [ ] T051 [US4] Add `walkthroughs/AssuredIdentity/multi-peer-findings/` to `.gitignore` (the per-run rolling files). The committed `walkthroughs/AssuredIdentity/multi-peer-findings.md` (singular, no subdirectory) remains tracked as the latest known-good baseline.
- [ ] T052 [US4] Run the smoke test on the local Docker host. Curate the resulting findings document into a `walkthroughs/AssuredIdentity/multi-peer-findings.md` (committed baseline). Record in the PR description what passed, what was slow, what was anomalous. **If the smoke surfaces a Feature 106 cross-peer replication bug, file a separate issue against the peer-replication subsystem owner; this PR still ships.**

**Checkpoint**: User Story 4 is fully functional and shippable as PR 4. The cross-peer architectural assumption is now measured. Subsequent releases can re-run the smoke and compare findings.

---

## Phase 7: User Story 5 — Consolidation of legacy walkthroughs and credential types (Priority: P5)

**Goal**: After this feature, the repository contains exactly one canonical Assured Identity walkthrough and one canonical credential type for citizen identity. The legacy `HaipVerifiedCitizen/` and `HaipDrivingLicence/` directories are gone; the legacy credential type names no longer appear in live code.

**Independent Test**: Fresh clone of the feature branch. Verify `walkthroughs/AssuredIdentity/` exists and runs end-to-end; `walkthroughs/HaipVerifiedCitizen/` and `walkthroughs/HaipDrivingLicence/` no longer exist; no live source code references `VerifiedCitizenCredential` or `AssuredPersonCredential` outside historical specs and design docs; the `HaipIdentityAttestation/` walkthrough still exists and still runs.

### Implementation for User Story 5

- [ ] T053 [US5] Run a code-search across the repository for live references to `VerifiedCitizenCredential` and `AssuredPersonCredential`. Use `Grep -r "VerifiedCitizenCredential" -- '!specs/**/spec.md' '!specs/**/*.md' '!docs/**/*.md'` and equivalent for `AssuredPersonCredential`. Document any live references found, then update them to `AssuredIdentityCredential` or remove them as appropriate. Re-run until both searches return only historical-document hits.
- [ ] T054 [US5] Delete `walkthroughs/HaipVerifiedCitizen/` entirely. `git rm -r walkthroughs/HaipVerifiedCitizen/`.
- [ ] T055 [US5] Delete `walkthroughs/HaipDrivingLicence/` entirely. `git rm -r walkthroughs/HaipDrivingLicence/`.
- [ ] T056 [US5] Run the full test suite (`dotnet test`) and the smoke test (`dotnet test --filter "Category=Smoke"`) to verify no broken references after deletion. Run `walkthroughs/HaipIdentityAttestation/run.ps1` to verify it still works (different scope — proves bare `sorcha-agent haip receive`).
- [ ] T057 [US5] Update `walkthroughs/README.md` to remove `HaipVerifiedCitizen` and `HaipDrivingLicence` entries from the walkthroughs table. Add `AssuredIdentity` entry with status, actors, actions, registers, and key features columns matching the existing pattern.

**Checkpoint**: User Story 5 is shippable as PR 5 (final PR for the feature). The platform has a single canonical citizen-identity story.

---

## Phase 8: Polish & Cross-Cutting Concerns

**Purpose**: Documentation and skill updates that make the new patterns discoverable to future contributors.

- [ ] T058 [P] Update `CLAUDE.md` with a new "Critical Patterns" section titled "Review Summary (`x-review`)" — about 15 lines, summarising the extension shape, the dispatch model, the two-card stacked variant, and pointing to `specs/107-assured-identity-v1/contracts/x-review-extension.md` for the full contract. Place after the existing "Open Participants & Late Binding" section to keep related patterns adjacent.
- [ ] T059 [P] Update `.claude/skills/blueprint-builder/SKILL.md` with documentation for the `x-review` extension, the new `x-file.capture` and `x-file.embedAs` fields, and a worked example of the two-card stacked review pattern. Cross-reference the contracts directory.
- [ ] T060 [P] Update `.claude/skills/walkthrough-builder/SKILL.md` to add `AssuredIdentity` to the existing walkthroughs reference table (and remove `HaipVerifiedCitizen` / `HaipDrivingLicence` from that table after Phase 7 lands). Document the multi-peer smoke-test pattern as a reusable shape for future federation testing.
- [ ] T061 [P] Update `docs/reference/development-status.md` to note Feature 107 completion: AssuredIdentityCredential as the canonical citizen identity credential, the consolidated walkthrough, the cross-peer smoke test as the standing measurement of Feature 106's cross-peer correctness.
- [ ] T062 [P] Update `.specify/MASTER-TASKS.md` to mark Theme 6 ("Cross-node verification") status: subsumed by Feature 107 phase 6; baseline cross-peer findings live in `walkthroughs/AssuredIdentity/multi-peer-findings.md`; future regression checks via re-running the smoke.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 (Setup)** → **Phase 2 (Foundational)** → **Phases 3–7 (User Stories)** → **Phase 8 (Polish)**
- Phase 2 has only one task (T004) and is intentionally minimal because the platform substrate is already in place.

### User Story Dependencies

- **US1 (P1, MVP)** is independent — it depends only on Phase 2 and on Feature 103's already-shipped substrate (open starting actions, schema $ref, persona autofill).
- **US2 (P2)** depends on **US1** — it consumes the AssuredIdentityCredential issued by Phase 1 and re-uses the `x-review` extension and `IdCardLayout` from US1.
- **US3 (P3)** depends on **US1 + US2** — it provides the actor configs that automate both. US1 and US2 can be tested with a human assessor before US3 lands.
- **US4 (P4)** depends on **US1** only — the cross-peer smoke exercises Phase 1's register-native delivery path. Independent of US2 and US3.
- **US5 (P5)** depends on **US1 + US2 + US3 + US4** — the consolidation deletion lands last so previous phases can validate against the existing walkthroughs until their replacement is proven.

### Within Each User Story

- Tests (T005–T012, T034) ⚠️ before implementation, fail before implementation lands.
- Within implementation: model changes (`XReviewExtension`, `XFileCaptureConfig`) before parser updates; parser before renderer dispatch; renderer dispatch before SchemaFormRenderer wiring; everything before the validation pass.
- Walkthrough scaffolding can land in parallel with renderer implementation (different file trees).

### Parallel Opportunities

**Within US1**:
- T005–T012: All tests can be written in parallel (different files, no dependencies).
- T013, T014, T016, T019, T020, T022 (CSS): All implementation pieces touch different files and can land in parallel up to the point of cross-wiring (T017, T021, T023, T024, T025).

**Within US2**:
- T035 (blueprint), T036 (CSS theme), T037 (renderer extension) all touch different files.

**Within US3**: T043, T044, T045 are three separate JSON files — fully parallel.

**Within US8 (Polish)**: T058–T062 all touch different files — fully parallel.

### Cross-Story Parallelism

- US3 (T043–T046) and US4 (T048–T051) can be developed in parallel after US1 ships, since they touch different file trees (`actors/` vs `docker-compose.federation.yml` + `run-multi-peer.ps1`).

---

## Parallel Examples

### Example: US1 test wave (write all eight tests before any implementation)

```bash
# All seven test files can be written in parallel:
T005 tests/Sorcha.UI.Core.Tests/Components/Forms/DateTimeRendererFutureBlockTests.cs
T006 tests/Sorcha.Blueprint.Models.Tests/XFileExtensionParserTests.cs
T007 tests/Sorcha.UI.Core.Tests/Components/Forms/FileRendererCaptureTests.cs
T008 tests/Sorcha.UI.Core.Tests/Services/Forms/PhotoTokenResizerTests.cs
T009 tests/Sorcha.Blueprint.Models.Tests/XReviewExtensionParserTests.cs
T010 tests/Sorcha.UI.Core.Tests/Services/Forms/ReviewSummaryDataSourceTests.cs
T011 tests/Sorcha.UI.Core.Tests/Components/Forms/ReviewSummaryRendererTests.cs
T012 tests/Sorcha.Blueprint.Service.Tests/BuildClaimsFromMappingsPortraitTests.cs

# Run them and confirm all fail:
dotnet test --filter "Class~DateTimeRendererFutureBlockTests|Class~XFileExtensionParserTests|Class~FileRendererCaptureTests|Class~PhotoTokenResizerTests|Class~XReviewExtensionParserTests|Class~ReviewSummaryDataSourceTests|Class~ReviewSummaryRendererTests|Class~BuildClaimsFromMappingsPortraitTests"
```

### Example: US1 model + parser wave

```bash
# After tests fail, implement model and parser pieces in parallel:
T014 src/Common/Sorcha.Blueprint.Models/Schema/XFileExtension.cs
T019 src/Common/Sorcha.Blueprint.Models/Schema/XReviewExtension.cs
T020 src/Common/Sorcha.Blueprint.Models/SchemaLayoutParser.cs

# Parser tests should now pass (T006 + T009).
```

### Example: US3 actor file wave

```bash
# All three actor JSON files in parallel:
T043 walkthroughs/AssuredIdentity/actors/citizen.json
T044 walkthroughs/AssuredIdentity/actors/gov-assessor.json
T045 walkthroughs/AssuredIdentity/actors/dla-officer.json
```

### Example: Polish wave (after Phase 7)

```bash
T058 CLAUDE.md
T059 .claude/skills/blueprint-builder/SKILL.md
T060 .claude/skills/walkthrough-builder/SKILL.md
T061 docs/reference/development-status.md
T062 .specify/MASTER-TASKS.md
```

---

## Implementation Strategy

### MVP First (User Story 1 only)

US1 alone delivers a substantive, demonstrable improvement: the canonical Assured Identity credential issued via a polished GDS-style citizen wizard with the new `x-review` ID-card review pattern. Even without DLA chain (US2), unattended assessment (US3), cross-peer smoke (US4), or legacy cleanup (US5), the platform has a credible "issue an identity credential" story that could be demoed.

**Suggested MVP scope**: T001–T033 (Setup + Foundational + US1).

### Incremental Delivery

| PR | Phases | Ship | What it adds |
|---|---|---|---|
| PR 1 | 1–3 (US1) | Core | Renderer polish + Assured Identity credential + Phase 1 walkthrough (manual assessor OK) |
| PR 2 | 4 (US2) | Chain | Driving Licence credential + Phase 2 walkthrough proving credential chain |
| PR 3 | 5 (US3) | Automation | Unattended walkthrough via background agents |
| PR 4 | 6 (US4) | Measurement | Cross-peer smoke + findings baseline |
| PR 5 | 7 + 8 (US5 + Polish) | Consolidation | Legacy deletion + documentation updates |

Five PRs sequenced over the duration of the feature. Each PR is independently reviewable and ships a testable increment.

### Parallel Team Strategy

If multiple contributors are available:
- Contributor A: PR 1 (US1 — the bulk of the work)
- Contributor B: PR 4 (US4 — independent infra) can start immediately after US1 lands, in parallel with PR 2/3
- Contributor C: PR 3 (US3) can start in parallel with PR 2 since the actor configs are isolated from the blueprint changes

Recommended single-contributor cadence: PR 1 → PR 2 → PR 3 → PR 4 → PR 5 sequentially, ~1 week per PR for a feature this size.

---

## Notes

- **Tests are mandatory** per Sorcha constitution Principle IV (≥85% coverage on new code). Test tasks (T005–T012, T034) are listed before implementation in each user story phase. Sorcha's TDD-leaning practice: write the tests, watch them fail, implement until they pass.
- **No database migrations** in this feature. All credentials, sealed disclosures, file chunks, and persona data flow through existing persistence.
- **No new HTTP endpoints**. All platform interaction reuses existing documented endpoints.
- **Photo capture requires a browser with `<canvas>` and `MediaDevices.getUserMedia`** for the resize pipeline. Older browsers fall back to plain file picker without resize — credential is issued without `portrait`.
- **The cross-peer smoke test is non-blocking**. If T052 surfaces a Feature 106 cross-peer bug, file an issue and ship PR 4 with the findings documented; do not block this feature on a different subsystem's bug fix.
- **Phase 7 (cleanup) MUST be last**. Earlier phases need the existing walkthroughs as references and as comparison test fixtures; deleting them too early breaks intermediate validation.

---

## Task count summary

| Phase | Tasks | User Story | Independently Shippable PR? |
|---|---|---|---|
| 1 — Setup | 3 (T001–T003) | — | No (prerequisite) |
| 2 — Foundational | 1 (T004) | — | No (prerequisite) |
| 3 — US1 (P1) 🎯 MVP | 29 (T005–T033) | Citizen flow + renderer polish | Yes — PR 1 |
| 4 — US2 (P2) | 9 (T034–T042) | DLA chain | Yes — PR 2 |
| 5 — US3 (P3) | 5 (T043–T047) | Unattended assessor | Yes — PR 3 |
| 6 — US4 (P4) | 5 (T048–T052) | Cross-peer smoke | Yes — PR 4 |
| 7 — US5 (P5) | 5 (T053–T057) | Consolidation cleanup | Yes — PR 5 |
| 8 — Polish | 5 (T058–T062) | — | Bundled with PR 5 |
| **Total** | **62** | 5 user stories, 5 PRs | |

**Parallel opportunities identified**:
- 8 test tasks in parallel within US1 (T005–T012)
- 3 actor JSON tasks in parallel within US3 (T043–T045)
- 5 polish tasks in parallel (T058–T062)
- US3 (T043–T046) and US4 (T048–T051) can be developed in parallel after US1 ships
