# Implementation Plan: Assured Identity v1

**Branch**: `107-assured-identity-v1` | **Date**: 2026-04-20 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `specs/107-assured-identity-v1/spec.md`
**Design**: [`docs/superpowers/specs/2026-04-20-assured-identity-v1-design.md`](../../docs/superpowers/specs/2026-04-20-assured-identity-v1-design.md)

## Summary

Deliver a single feature in **seven sequenced phases** that consolidate the platform's "verified person" story into one canonical `AssuredIdentityCredential` and one canonical `walkthroughs/AssuredIdentity/`, remove the `VerifiedCitizenCredential` / `AssuredPersonCredential` / `HaipVerifiedCitizen` / `HaipDrivingLicence` duplication, and substantially polish the citizen-facing form experience.

The feature is **mostly new blueprint JSON, a new Blazor renderer extension, and walkthrough plumbing** — not new platform services. The renderer sits inside `Sorcha.UI.Core` and adds three reusable capabilities (DoB future-block via existing `SorchaDateTokenResolver`, photo capture dispatch with client-side token-image resize, the new `x-review` schema extension with a parameterised id-card layout). The two new blueprints consume Feature 103's shared identity primitives via `$ref` and Feature 104's Wave 14b credential claim action. The two new walkthroughs are a single consolidated walkthrough with phase scripts. The cross-peer smoke test is a new `docker-compose.federation.yml` plus a PowerShell driver that runs Phase 1 across two peer stacks and writes a findings markdown.

**No new microservice, no new database technology, no new schema registry.** Every new piece sits inside an existing platform pattern: the `x-review` extension mirrors the existing `x-credential-offer` Wave 14b extension shape; the id-card component is a MudBlazor Razor component like every other in `Sorcha.UI.Core/Components`; the walkthrough structure mirrors `HaipVerifiedCitizen/` and `HaipDrivingLicence/` (which it replaces). The legacy artefacts are deleted in the final phase so that earlier phases can continue to build against them until they are superseded.

## Technical Context

**Language/Version**: C# 13 / .NET 10; Razor for Blazor WASM UI; JSON for blueprint and schema authoring; PowerShell 7.5+ for walkthrough scripts
**Primary Dependencies**: .NET Aspire 13.0, ASP.NET Core, MudBlazor, JsonSchema.Net 7.4, `Sorcha.Cryptography` (XChaCha20-Poly1305 + HKDF for file chunks), NBitcoin (HD wallet), StackExchange.Redis (SignalR backplane + inbound notifications), OpenTelemetry 1.12; browser `MediaDevices.getUserMedia` via Blazor JS interop for camera capture; `<canvas>` via Blazor JS interop for client-side image resize
**Storage**: Existing only — PostgreSQL (Tenant Service persona), MongoDB (Blueprint Service instance store, sealed disclosures), Redis (SignalR backplane, inbound credential detection). Photo evidence is stored via the existing Feature 085 file-chunks path (no new storage). No schema registry beyond Feature 103's `blueprints/schemas/sorcha-core/`
**Testing**: xUnit + FluentAssertions + Moq for unit & integration; walkthrough scripts themselves are the primary E2E proof (Playwright E2E deferred per spec assumption)
**Target Platform**: Linux containers via Docker Compose (single-peer and two-peer variants); Blazor WASM client in modern browsers with camera access; Windows / Linux / macOS dev hosts; n1.sorcha.dev reference deployment
**Project Type**: Web — multi-service backend (microservices + API Gateway via YARP) plus Blazor WASM frontend. This feature does not add or remove a microservice
**Performance Goals**:
- Citizen form completion end-to-end: under 3 minutes with populated persona (SC-001)
- Photo client-side resize to 240×320 token: under 2 seconds on a modest mobile device
- Review summary card render: under 500ms after navigation to page 5
- Agent-based assessor approval: under 30 seconds from submission to approved (SC-008)
- Cross-peer credential delivery: under 30 seconds from issue on peer A to pending-acceptance on peer B (SC-009)
- Driving Licence phase end-to-end: under 2 minutes from first form view to claim notification (SC-002)
**Constraints**:
- Photo size embedded in credential MUST stay within the token-image target (~20KB) regardless of the captured original's size — client-side resize is authoritative and the server-side issuance path MUST reject claim inclusion if the token exceeds the target
- Citizen participant on both blueprints' starting actions MUST remain `walletAddress: null` at publish time; the Feature 103 `VAL_BP_010` guardrail MUST continue to enforce this and MUST NOT be weakened
- The `x-review` extension MUST produce a read-only presentation — no form state mutation on the review page
- Both walkthroughs MUST complete without requiring a human to open any assessor UI (agent-driven), but the human-facing assessor UI MUST remain functional for manual override
- Cross-peer smoke test MUST NOT block release on any replication failure it surfaces; failures become findings, not gates
- Legacy deletion (Phase 7) MUST land last so that Phases 1–6 can validate against the existing walkthroughs until their replacement is proven
**Scale/Scope**:
- 3 modified / new renderer components (`DateTimeRenderer`, `FileRenderer`, `ReviewSummaryRenderer`) plus `IdCardLayout.razor` — ~600 LOC total
- 2 new blueprint JSON files (`assured-identity.json`, `driving-licence.json`) — ~400 LOC combined
- 3 new actor JSON files + 5 walkthrough PowerShell scripts — ~800 LOC
- 1 new `docker-compose.federation.yml` — ~200 LOC
- 2 deleted walkthrough directories (`HaipVerifiedCitizen/` and `HaipDrivingLicence/`) — ~3000 LOC removed
- ~1400 LOC unit / integration / walkthrough-functional tests across the feature
- 7 pull requests (one per phase)
- Touches: `Sorcha.UI.Core` (renderer), `Sorcha.Blueprint.Models` (extension parser), `walkthroughs/` (new + deletes), root (docker-compose.federation.yml)

## Constitution Check

Evaluating this plan against `.specify/memory/constitution.md`. Re-evaluation after Phase 1 design at the bottom of this section.

| Principle | Status | Notes |
|---|---|---|
| **I. Microservices-First** | ✅ Pass | No new microservice. All renderer changes in `Sorcha.UI.Core`. Extension parser in `Sorcha.Blueprint.Models`. Walkthrough artefacts are not services. No upward dependencies introduced (UI → Blueprint Models is already the existing dependency direction). |
| **II. Security First** | ✅ Pass with notes | Photo upload is a new class of user input — mitigated by (a) client-side size bounds, (b) server-side size validation in the existing file-chunks pipeline, (c) content-type validation via the existing `x-file.accept` schema extension, (d) the photo full-original lives inside the existing sealed-disclosure file-chunks path (encrypted at rest via Feature 085 XChaCha20-Poly1305). Cross-peer smoke test does not weaken any existing security posture. Open citizen submission uses the public-org JWT floor (Feature 103 contract preserved). `VAL_BP_010` publish guardrail preserved. No secrets introduced in any walkthrough script. |
| **III. API Documentation** | ✅ Pass | **No new HTTP endpoints in this feature.** All platform interaction reuses existing documented endpoints: action execution (Feature 018), file chunks (Feature 085), HAIP offers (Feature 097), HAIP presentations (Feature 098), credential claim (Feature 104), sealed disclosures (Feature 106). The `x-review` and `x-file` schema extensions are documented in `contracts/` as schema-level contracts. |
| **IV. Testing Requirements** | ✅ Pass | Each phase ships with unit tests for new renderer components (xUnit + FluentAssertions + Moq), extension parser tests, schema validation tests, and walkthrough-functional tests (PowerShell scripts asserting state transitions). Coverage target ≥ 85% for new UI code (constitution baseline 80%). Playwright screenshots deferred per explicit spec assumption (user will add "when I need them"). |
| **V. Code Quality** | ✅ Pass | All new C# targets net10.0 with nullable reference types enabled. Async/await for all I/O. DI for all injected services. License header on every new file. No compiler warnings. |
| **VI. Blueprint Creation Standards** | ✅ Pass | Both blueprints ship as JSON files. Identity primitives consumed via `$ref` from Feature 103's `blueprints/schemas/sorcha-core/`. The `x-review` extension is declared in blueprint JSON, not in C# code. No fluent-API additions. |
| **VII. Domain-Driven Design** | ✅ Pass | Uses the established vocabulary: Blueprint, Action, Participant, Disclosure, Claim, Credential, Presentation, Issuance. Adds three new ubiquitous terms, all user-facing and all aligned with existing language: **Assured Identity** (the credential and the workflow), **Review Summary** / **ID Card Layout** (the new review pattern), **Validator Agent** (the sorcha-agent role filling the assessor seat). |
| **VIII. Observability by Default** | ✅ Pass | Renderer changes emit client-side telemetry via the existing Blazor telemetry pipeline (no new instrumentation surface). Photo resize + upload reuses the Feature 085 file-chunks path which already emits OpenTelemetry spans. Walkthrough scripts emit structured log lines via the shared module. Cross-peer smoke test's findings document is itself a form of machine-readable observability record. |

**No constitution violations.** No entries in the Complexity Tracking section.

### Re-evaluation after Phase 1 design

Phase 1 design produces no new architectural surfaces. The `x-review` extension mirrors the `x-credential-offer` Wave 14b extension exactly in shape and dispatch model. The id-card component is a parameterised MudBlazor component like every other in `Sorcha.UI.Core/Components`. The cross-peer smoke test is a new docker-compose file and a PowerShell driver — neither introduces a new subsystem. Constitution check confirmed unchanged. ✅

## Project Structure

### Documentation (this feature)

```text
specs/107-assured-identity-v1/
├── plan.md                         # This file
├── spec.md                         # Feature specification (committed)
├── research.md                     # Phase 0 — design decisions distilled (links to design spec)
├── data-model.md                   # Phase 1 — entities and shapes
├── quickstart.md                   # Phase 1 — developer onboarding
├── contracts/                      # Phase 1 — schema-extension and file-format contracts
│   ├── x-review-extension.md       # New schema extension format
│   ├── x-file-capture-extension.md # Camera-capture + token-resize schema extension
│   ├── id-card-layout-config.md    # Colour theme / header / palette parameters
│   ├── assured-identity-credential.md   # Claim list and SD-JWT shape
│   ├── driving-licence-credential.md    # Claim list and SD-JWT shape
│   ├── portrait-claim-format.md    # Token-image dimensions, JPEG bounds, ISO 19794-5 alignment
│   ├── cross-peer-findings-format.md    # Multi-peer smoke test findings document shape
│   └── docker-compose-federation.md     # Two-peer stack topology
└── checklists/
    └── requirements.md             # Spec quality checklist (committed)
```

### Source Code (repository root)

This feature touches **existing UI, Blueprint Models, and walkthroughs**. No new top-level projects. Concrete paths:

```text
src/
├── Apps/
│   └── Sorcha.UI/
│       └── Sorcha.UI.Core/
│           ├── Components/Forms/
│           │   ├── DateTimeRenderer.razor                    # MOD: wire SorchaDateTokenResolver for formatMin/Max client-side
│           │   ├── FileRenderer.razor                        # MOD: capture attr, front-camera default, client-side token-image resize
│           │   ├── ReviewSummaryRenderer.razor               # NEW: dispatch by x-review.layout; iterate prior-page values
│           │   ├── Layouts/
│           │   │   └── IdCardLayout.razor                    # NEW: styled credential-card layout, parameterised theme
│           │   ├── ControlDispatcher.razor                   # MOD: dispatch x-review pages to ReviewSummaryRenderer
│           │   └── SorchaFormRenderer.razor                  # MOD: treat x-review pages as read-only; wire Edit-X navigation
│           └── Services/Forms/
│               ├── PhotoTokenResizer.cs                      # NEW: client-side resize via JS interop, returns token-sized JPEG
│               └── ReviewSummaryDataSource.cs                # NEW: reads prior-page values from FormContext for review render
│
├── Common/
│   ├── Sorcha.Blueprint.Models/
│   │   ├── SchemaLayoutParser.cs                             # MOD: parse x-review extension alongside x-pages/x-sections
│   │   ├── XReviewExtension.cs                               # NEW: model for { layout, editable, header }
│   │   ├── XFileExtension.cs                                 # MOD: add capture + embedAs fields
│   │   └── XReviewLayoutVariant.cs                           # NEW: enum { IdCard, Tabular?, PassportPage? } — only IdCard implemented in v1
│   └── Sorcha.Validator.Core/
│       └── Tokens/
│           └── SorchaDateTokenResolver.cs                    # (already exists from Feature 103 — unchanged here; wired by DateTimeRenderer in this feature)

blueprints/
└── schemas/
    └── sorcha-core/                                           # (already exists from Feature 103 — unchanged here)
        ├── PersonName.v1.json
        ├── DateOfBirth.v1.json
        ├── EmailAddress.v1.json
        └── PostalAddress.v1.json

walkthroughs/
├── AssuredIdentity/                                           # NEW consolidated walkthrough directory
│   ├── README.md
│   ├── setup.ps1                                             # Provisions Gov + DLA orgs, citizen wallet, both blueprints (idempotent)
│   ├── run.ps1                                               # Runs Phase 1 + Phase 2 end-to-end (single peer, HAIP delivery path)
│   ├── run-phase1-identity.ps1                               # Just Phase 1 (citizen → AssuredIdentity)
│   ├── run-phase2-licence.ps1                                # Just Phase 2 (citizen → DLA → DrivingLicence)
│   ├── run-multi-peer.ps1                                    # Phase 1 across two peers, register-native delivery, findings-writing smoke test
│   ├── blueprints/
│   │   ├── assured-identity.json                             # Replaces verified-citizen.json + assured-person.json
│   │   └── driving-licence.json                              # Replaces HaipDrivingLicence/blueprints/driving-licence.json
│   ├── actors/
│   │   ├── citizen.json                                      # Filesystem HAIP wallet-dir; receives + presents
│   │   ├── gov-assessor.json                                 # Rules-mode, approves identity
│   │   └── dla-officer.json                                  # Rules-mode, approves licence
│   ├── data/
│   │   └── sample-portrait.jpg                               # ICAO-compliant test photo
│   └── multi-peer-findings.md                                # Produced per run of run-multi-peer.ps1 (gitignored; one committed baseline)
├── HaipVerifiedCitizen/                                       # DELETED entirely in Phase 7
├── HaipDrivingLicence/                                        # DELETED entirely in Phase 7
└── HaipIdentityAttestation/                                   # KEPT — different scope (proves bare CLI)

docker-compose.federation.yml                                  # NEW: two-peer compose for run-multi-peer.ps1 (peer-a, peer-b, shared docker network)

tests/
├── Sorcha.UI.Core.Tests/
│   ├── Components/Forms/
│   │   ├── DateTimeRendererFutureBlockTests.cs               # NEW: formatMaximum: "today" blocks future dates
│   │   ├── FileRendererCaptureTests.cs                       # NEW: capture attr propagation, resize invocation
│   │   ├── PhotoTokenResizerTests.cs                         # NEW: resize produces ≤20KB JPEG at 240×320
│   │   └── ReviewSummaryRendererTests.cs                     # NEW: reads FormContext, renders id-card, Edit-X buttons nav correctly
│   └── Services/Forms/
│       └── ReviewSummaryDataSourceTests.cs                   # NEW: pulls correct values from prior pages
├── Sorcha.Blueprint.Models.Tests/
│   └── XReviewExtensionParserTests.cs                        # NEW: x-review schema extension parses with layout, editable, header
└── walkthroughs/AssuredIdentity/
    └── tests/                                                 # (Functional assertions live inside the walkthrough scripts themselves, as in TradeFinance/SelfBuildHouse)
```

**Structure Decision**: No new top-level applications, no new microservices, no new database technologies. The feature fits entirely inside existing patterns: the renderer changes live in `Sorcha.UI.Core`, schema extensions in `Sorcha.Blueprint.Models`, blueprints and walkthroughs under `walkthroughs/AssuredIdentity/`, and the cross-peer topology as `docker-compose.federation.yml` at the repository root alongside the existing `docker-compose.yml`. Legacy walkthrough directories `HaipVerifiedCitizen/` and `HaipDrivingLicence/` are deleted in Phase 7 after Phases 1–6 have been proven against them.

## Phase 0: Outline & Research

### Why this is light

Every design unknown was resolved in the brainstorming session and is documented in [`docs/superpowers/specs/2026-04-20-assured-identity-v1-design.md`](../../docs/superpowers/specs/2026-04-20-assured-identity-v1-design.md) with rationale and rejected alternatives. Phase 0 of this plan is an *index* into that resolution work, not a fresh research pass.

The full research artifact is generated as `research.md` and contains:
- The seven design decisions (credential type naming, delivery mode, photo-as-embedded-claim, 5-page GDS wizard, id-card review pattern, validator-hook-as-agent, cross-peer measure-not-gate), each in the standard Decision / Rationale / Alternatives format
- File:line citations from the existing codebase that prove the substrate is present (`SorchaDateTokenResolver.cs:37-120`, `FileReferenceField.razor:36-56`, `PersonaAutofillResolver.cs:42-359`, `ActionExecutionService.cs:496-617` for the sealed-disclosure path, `InboundCredentialDetector.cs` for the peer-B receive path)
- The pattern each new piece mirrors (`x-credential-offer` Wave 14b → `x-review`; existing MudBlazor components → `IdCardLayout`; existing walkthrough scripts → consolidated `AssuredIdentity/` scripts)
- The two open planning questions resolved by informed-default (which delivery path `run.ps1` exercises — HAIP external, because Phase 2 needs a filesystem wallet; whether photo quality checks are automated — no, deferred to future validator integration)

### NEEDS CLARIFICATION markers

**None.** All design questions resolved during brainstorming. Tactical questions resolved during specification authoring (which delivery path the primary walkthrough uses; whether the x-review extension supports multiple layout variants in v1; whether photo composition is auto-checked) are captured as informed defaults in the spec's Assumptions section, with rationale in `research.md`.

## Phase 1: Design & Contracts

**Prerequisites:** `research.md` complete (this section produces it alongside `data-model.md`, `contracts/`, `quickstart.md`).

### Contracts (in `contracts/`)

1. **`x-review-extension.md`** — The new schema extension format:
   - Placement: on a `type: object` page within a schema's `x-pages` list, as a sibling extension alongside `x-sections` / `x-introduction` / `x-width`
   - Shape: `{ layout: "id-card", editable: bool, header: { issuerName: string, credentialName: string } }`
   - Semantics: the page is rendered read-only; field values come from prior pages in the same action's submission model; when `editable: true`, the renderer generates per-section Edit-X buttons that navigate back to the originating page with data preserved
   - Future variants reserved: `layout: "passport-page" | "tabular" | "receipt"` — not implemented in v1

2. **`x-file-capture-extension.md`** — Extension to the existing `x-file` schema extension (from Feature 085):
   - New optional field: `capture: "user" | "environment" | null` — advises front-facing camera (user) or rear-facing (environment) on mobile; null is legacy behaviour (plain file picker)
   - New optional field: `embedAs: "image-token-jpeg-240x320" | null` — advises the renderer to produce a resized token alongside the full original; null disables resize
   - Semantics: the form submission carries both the full-resolution file (via the existing chunked-file pipeline) and the small token as a separate base64 field on the action payload; the credential claim mapping references the token field by JSON pointer

3. **`id-card-layout-config.md`** — Parameters for `IdCardLayout.razor`:
   - `issuerName: string` — rendered in the card header ("Issued by …")
   - `credentialName: string` — rendered as the card's displayed credential type
   - `colourTheme: "identity-navy" | "licence-pink" | custom` — CSS custom property set; v1 ships two themes
   - `watermark: "draft" | "pending" | "issued" | null` — drives the watermark rendering and the accent colour
   - `actions: enum` — derived from the hosting action's routes (Submit / Edit / Approve / Reject); not specified in the extension itself

4. **`assured-identity-credential.md`** — Claim list and SD-JWT VC profile:
   - Credential type: `AssuredIdentityCredential`
   - Claims: `givenName`, `middleName?`, `familyName`, `fullName` (derived), `dateOfBirth`, `email`, `address` (structured: `line1`, `line2?`, `town`, `region?`, `postcode`, `country`), `portrait?`
   - Every claim selectively disclosable
   - Issuer: `did:sorcha:org:<wallet-of-issuing-org>` (per existing DID scheme)
   - Expiry: none (identity credentials do not expire in v1; revocation via the existing Feature 079 revocation transactions)

5. **`driving-licence-credential.md`** — Claim list and SD-JWT VC profile:
   - Credential type: `DrivingLicenceCredential`
   - Claims: `licenceNumber`, `vehicleClass`, `issuedDate`, `expiryDate`, `holderName`, `holderDateOfBirth`, `holderPortrait?`
   - Every claim selectively disclosable
   - Issuer: `did:sorcha:org:<wallet-of-dla-org>`
   - Expiry: 10 years (`P10Y`)
   - Presentation requirement on Phase 2's verification action: `AssuredIdentityCredential` disclosing `givenName`, `familyName`, `dateOfBirth`, `portrait`

6. **`portrait-claim-format.md`** — Portrait claim technical shape:
   - Token-image dimensions: 240×320 (aligned with ISO/IEC 19794-5 token image)
   - Format: JPEG, sRGB colour, quality tuned for ≤20KB final size
   - Embedding: base64 string value of the claim (per SD-JWT convention for binary)
   - Full-resolution original: stored via existing file-chunks pipeline, linked from the action payload, NOT embedded in the credential
   - Issuance path: the issuance step reads the token from the action payload, validates size bounds, base64-encodes into the claim; rejects claim inclusion if the token exceeds bounds (credential issued without portrait)

7. **`cross-peer-findings-format.md`** — The markdown shape of `multi-peer-findings.md`:
   - YAML frontmatter: `run_timestamp`, `peer_a_version`, `peer_b_version`, `outcome: pass | degraded-pass | fail | env-failure`
   - Body sections: Topology, Timings (issue → docket seal → peer-B detection → peer-B MyCredentials PENDING tab → holder Accept), Anomalies, Reproduction notes
   - Convention: one committed baseline representing the latest known-good state, plus a gitignored rolling set produced per run

8. **`docker-compose-federation.md`** — Topology for the two-peer stack:
   - Two full Sorcha stacks (peer-a and peer-b), each with its own Blueprint / Register / Validator / Wallet / Tenant / Peer / API Gateway / DB set
   - Shared Docker network; peers discover each other via static seed configuration
   - Single shared test register subscribed by both peers at setup time
   - No cross-peer auth shortcuts; both peers use their own trust anchors

### Data Model (in `data-model.md`)

Entities, fields, relationships, and (where applicable) state transitions:

- **AssuredIdentityCredential** — SD-JWT VC; claims listed above; issuer is the government org's DID; holder is the citizen's wallet (bound by Phase 1's open starting action's late-binding); disclosure envelope follows the Feature 106 register-native pattern when delivery is register-native, or the Feature 097 OpenID4VCI pre-auth code pattern when delivery is HAIP external
- **DrivingLicenceCredential** — SD-JWT VC; claims listed above; issuer is the DLA org's DID; holder is the citizen's wallet (same wallet as the identity)
- **XReviewExtension** — blueprint-model-side record of the parsed extension: `Layout` (enum), `Editable` (bool), `Header` (IssuerName, CredentialName). Attached to a `SchemaPage` alongside the existing `XSections` / `XIntroduction` / `XWidth` fields
- **XFileCaptureConfig** — fields added to the existing `XFileExtension`: `Capture` (nullable enum `User | Environment`), `EmbedAs` (nullable enum: only `ImageTokenJpeg240x320` in v1)
- **IdCardLayoutConfig** — runtime record passed to `IdCardLayout.razor`: `IssuerName`, `CredentialName`, `ColourTheme`, `Watermark`, `FieldValues` (dictionary of pointer → value read from the form context)
- **Portrait** — a field value representing a captured photo: `FullOriginalChunkIds` (array of transaction ids from the file-chunks pipeline), `TokenImageBase64` (string, ≤20KB), `Hash` (SHA-256 of original), `ContentType` (`image/jpeg`)
- **CrossPeerFindings** — the markdown document produced by `run-multi-peer.ps1`: frontmatter outcome, timings, anomalies, reproduction notes (see contract)

### Quickstart (in `quickstart.md`)

A developer onboarding doc covering:

- **Running the walkthrough locally** — `setup.ps1 -Profile gateway` then `run.ps1`; expected output; how to verify the credential lands in the HAIP wallet-dir
- **Running the multi-peer smoke test** — `docker compose -f docker-compose.federation.yml up -d`, then `run-multi-peer.ps1`; where the findings document lands; what anomalies to look for
- **Adding a new review-summary layout variant** — where the variant enum lives, where the Razor component goes, which CSS custom properties to wire
- **Adjusting the id-card colour theme for a new credential type** — extending the theme enum, adding the CSS, referencing it from a blueprint's `x-review` extension
- **Swapping the assessor agent from rules-mode to AI-mode** — how the actor definition changes, what environment variables are needed (deferred in v1; quickstart documents the extension point)
- **Debugging a photo that fails to embed in the credential** — where the size check happens, how the renderer surfaces the warning, what the action payload looks like with and without the portrait
- **Migrating a downstream consumer from `VerifiedCitizenCredential` / `AssuredPersonCredential` to `AssuredIdentityCredential`** — search-and-replace guide; what to update in `credentialRequirements` presentation requests

### Agent context update

After Phase 1 artifacts are written, run the agent context update script to refresh `CLAUDE.md` and agent-specific files with new technology mentions and patterns introduced by this feature (the `x-review` extension, the photo-capture extension, the id-card layout component).

## Phase 2 (out of scope — `/speckit.tasks` produces tasks.md)

Task generation is **not** part of `/speckit.plan`. After this plan is written and reviewed, run `/speckit.tasks` to produce `tasks.md` with dependency-ordered work items grouped by phase, following the seven-phase mapping declared in the spec.

## Complexity Tracking

No constitution violations. No entries.

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| _none_ | _none_ | _none_ |
