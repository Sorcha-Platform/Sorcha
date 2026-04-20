# Research: Assured Identity v1

**Feature**: 107-assured-identity-v1
**Date**: 2026-04-20
**Status**: Complete — all decisions resolved during brainstorming; no open `NEEDS CLARIFICATION` items

## Scope note

This research document is an *index* into the brainstorming design artefact at [`docs/superpowers/specs/2026-04-20-assured-identity-v1-design.md`](../../docs/superpowers/specs/2026-04-20-assured-identity-v1-design.md). Every design decision in this feature was made in that session with rejected alternatives captured. This document summarises each decision in the standard Decision / Rationale / Alternatives format with file-path citations to the existing codebase where the substrate is already present.

## Decisions

### 1. Credential type name: `AssuredIdentityCredential`

- **Decision**: Name the canonical person-identity credential `AssuredIdentityCredential`. Delete the existing `VerifiedCitizenCredential` and `AssuredPersonCredential` variants.
- **Rationale**: Both existing names mislead. "Verified Citizen" implies civic-status verification (which the platform does not perform). "Assured Person" is closer but was positioned as a delivery variant rather than a canonical type. "Assured Identity" describes the semantic precisely — an identity record asserted true within an issuer's assurance frame — and generalises equally to government citizen identity, employer workforce identity, and any other public → issuer → credential pattern.
- **Alternatives considered**:
  - Keep `VerifiedCitizenCredential` and add a delivery field — preserves the downstream DLA chain but bakes in the misleading name.
  - Keep `AssuredPersonCredential` and rebrand around it — keeps the chain at the cost of clarity; "Person" is less general than "Identity".

### 2. Delivery mode: both register-native and HAIP external, holder chooses at claim time

- **Decision**: Both delivery modes. The holder picks at claim time via the existing Feature 104 Wave 14b credential claim card.
- **Rationale**: Register-native (Feature 106) is the simpler UX for Sorcha-native users. HAIP external is required for any verifier outside Sorcha and is necessary to prove OpenID4VP capability for Phase 2. The Wave 14b claim card already supports both paths in code — no new platform work needed. Existing implementation: `CredentialClaimCard.razor` + `HaipLocalReceiveService.cs`.
- **Alternatives considered**:
  - HAIP-external-only — simpler to build but loses register-native investment from Feature 106.
  - Register-native-only — simpler still but cannot prove OpenID4VP for Phase 2.

### 3. Photo embedded as selectively-disclosable `portrait` claim

- **Decision**: Optional photo. Capture at 480×640+, keep full-resolution original on the register as evidence, embed a 240×320 token-image JPEG (~20KB) as a selectively-disclosable `portrait` claim in the credential.
- **Rationale**: Industry precedent — ICAO e-passport chips and ISO 18013-5 mDLs both embed portraits directly as JPEGs of roughly token-image size, not by URL reference. Offline verification matters. Selective disclosure lets the holder withhold the portrait where it's not needed (age gates) and reveal it where it is (DLA roadside check).
- **Alternatives considered**:
  - Evidence-only (no claim) — smaller credential, but verifiers can't confirm the holder visually later, kills DLA-style downstream.
  - Full-resolution as claim — credential bloat to 100KB+ for marginal benefit.

### 4. Form layout: GDS 5-page wizard with review-as-id-card

- **Decision**: Five-page wizard. Page 1 Name + DoB (two `x-sections`), Page 2 Address (postcode lookup), Page 3 Email, Page 4 Photo (optional, skippable), Page 5 Review (new `x-review` extension with `id-card` layout).
- **Rationale**: GDS one-thing-per-page pattern is the established citizen-service UX standard. The ID-card review pattern lets the citizen see what they'll hold before submitting, and the same component renders the issued credential detail view later — one component, three states (draft / pending / issued).
- **Alternatives considered**:
  - Single-page sectioned form — desktop-friendly but overwhelming for citizen-facing flow.
  - 3-page wizard (collapses fields) — loses the one-thing-per-page focus.
  - 4-page wizard (no review) — misses the GDS-standard review-before-submit.

### 5. Review screen architecture: `x-review` schema extension with parameterised `id-card` layout

- **Decision**: New blueprint schema extension `x-review: { layout, editable, header }` parsed by `SchemaLayoutParser` and rendered by a new `ReviewSummaryRenderer.razor` that dispatches to a layout variant. `id-card` is the v1 variant; `passport-page`, `tabular`, `receipt` are reserved enum values.
- **Rationale**: Mirrors the existing `x-credential-offer` Wave 14b extension pattern exactly. Parameterised colour theme + header lets Phase 2's driving-licence review reuse the same component with a pink theme — no bespoke per-credential component. Same component serves citizen Page 5 (draft watermark, Edit+Submit), assessor pending review (pending watermark, Approve+Reject), and wallet credential detail view (issued, no watermark).
- **Alternatives considered**:
  - Bespoke Blazor component referenced by `x-component: "name"` — works for one flow, accumulates component debt across credentials.
  - Tabular summary — functional but loses the "see what you'll hold" UX win.
  - Leave review screen out of blueprint, hard-code in Razor — locks the layout per credential type, defeats reuse.

### 6. Validator hook: `sorcha-agent` actor in rules mode (not a new platform surface)

- **Decision**: The assessor role is filled by a background `sorcha-agent` process in `rules` mode (approve if the submission is well-formed). Future validator-API integration plugs in as either an `external` agent mode or an HTTP call inside the agent's rules — no platform or blueprint changes required when that arrives.
- **Rationale**: The actor framework already exists (`src/Apps/Sorcha.Agent/`). The blueprint's assessor action stays a normal decision action; the agent is just an unattended human. AI-mode (Claude vision) is a natural v1.1 extension — same code path, different agent mode. No new gate type or hook field on the Action model.
- **Alternatives considered**:
  - New `validatorHook` block on Action — adds platform surface for something the agent framework already handles.
  - Blueprint-declared validator URL — couples the blueprint to a specific vendor; defeats pluggability.

### 7. Cross-peer testing: measure-and-document, not block-and-fix

- **Decision**: Bundle a single cross-peer smoke test (`run-multi-peer.ps1` + `docker-compose.federation.yml`) that exercises Feature 106 register-native delivery end-to-end. Produces a findings document on every run regardless of outcome. **Does not block the feature's release** on any replication issue it surfaces — those become findings for whoever owns peer replication.
- **Rationale**: Feature 106's cross-peer path has never been exercised end-to-end (`DEFERRED-E2E.md` T047/T048 explicitly pending; `MASTER-TASKS.md` Theme 6 open). Fixing it is out of this feature's scope; measuring it is not. Bundling the smoke test retires the largest untested architectural assumption without coupling this feature's ship date to a separate subsystem's bug-fix cadence.
- **Alternatives considered**:
  - Block v1 on cross-peer correctness — couples ship date to a separate subsystem's reliability.
  - Defer cross-peer entirely — leaves the largest architectural assumption unchecked indefinitely.

## Substrate already present in the codebase

Citations that prove each new piece sits inside an existing pattern:

| New piece | Existing substrate | Citation |
|---|---|---|
| DOB client-side future-block | `SorchaDateTokenResolver` already implements token vocabulary server-side | `src/Common/Sorcha.Validator.Core/Tokens/SorchaDateTokenResolver.cs:37-120` |
| Photo capture control | Legacy `FileReferenceField` in `Sorcha.UI.Web.Client` uses `<InputFile capture="environment">` | `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Components/Forms/FileReferenceField.razor:36-56` — needs promotion to core `FileRenderer.razor` |
| Photo chunked upload pipeline | Feature 085 file-chunks path, XChaCha20-Poly1305 encrypted | `src/Services/Sorcha.Blueprint.Service/Endpoints/FileChunkEndpoints.cs`; `docs/reference/API-DOCUMENTATION.md` under "File Chunk Submission" |
| `x-review` schema extension pattern | `x-credential-offer` Wave 14b extension | `src/Common/Sorcha.Blueprint.Models/` — existing `XCredentialOfferExtension` or equivalent; dispatched by `ControlDispatcher.razor` in the core UI |
| Review summary dispatch | `SchemaLayoutParser` already parses `x-pages`, `x-sections`, etc. | `src/Common/Sorcha.Blueprint.Models/SchemaLayoutParser.cs:64-91` |
| Persona autofill that reaches JWT | `PersonaAutofillResolver` writes to `_formContext.FormData` which flows through `BuildClaimsFromMappings` | `src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/Persona/PersonaAutofillResolver.cs`; `src/Services/Sorcha.Blueprint.Service/Services/.../ActionExecutionService.cs:1598-1626`; `BuildClaimsFromMappingsTests.cs` (single-unit regression guard) |
| Credential claim card with dual-path | Feature 104 Wave 14b | `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Credentials/CredentialClaimCard.razor`; `HaipLocalReceiveService.cs` |
| Register-native credential delivery | Feature 106 | `src/Services/Sorcha.Wallet.Service/Services/InboundCredentialDetector.cs`; `src/Services/Sorcha.Blueprint.Service/Services/InstanceMirrorReconstructor.cs`; `ActionExecutionService.cs:496-617` (sealed disclosure branch) |
| HAIP OpenID4VCI / OpenID4VP | Features 097 + 098 | `src/Services/Sorcha.Haip.Service/` |
| Open starting action + late binding | Feature 103 | `src/Services/Sorcha.Blueprint.Service/Services/.../ActionExecutionService.cs:309-332`; `ValidationEngine.cs:1027`; `VAL_BP_010` guardrail |
| Shared identity schema primitives | Feature 103 | `blueprints/schemas/sorcha-core/{PersonName,DateOfBirth,EmailAddress,PostalAddress}.v1.json`; `CoreSchemaSeedService.cs` |
| Agent framework with rules + AI modes | Feature 087 | `src/Apps/Sorcha.Agent/` |
| Walkthrough structure | `walkthroughs/HaipVerifiedCitizen/` and `HaipDrivingLicence/` | both deleted in Phase 7 after the new consolidated walkthrough proves out |

## Resolved-by-informed-default questions

The following tactical questions were resolved during specification authoring with the rationale captured here:

1. **Which delivery path does `run.ps1` exercise?** HAIP external wallet-dir. Reason: Phase 2's OpenID4VP presentation requires a filesystem-resident holder wallet (`sorcha-agent haip present --wallet-dir <dir>`). The register-native path is exercised separately by `run-multi-peer.ps1`. Together the two scripts prove both modes.
2. **Does v1 ship multiple `x-review` layout variants?** No — only `id-card`. The enum reserves `passport-page`, `tabular`, `receipt` as forward-compatibility placeholders but the renderer only implements `id-card` in v1.
3. **Is photo composition auto-checked?** No. ICAO composition guidance is rendered as advisory tips in the capture UI. Automated face-detection, background uniformity, quality scoring are all deferred to a future validator API integration. The assessor (human or agent) rejects bad photos in v1.
4. **What happens if the client-side photo resize fails or is bypassed?** The server-side issuance step validates token-image size bounds and refuses to include the portrait claim if the token exceeds ~20KB. A warning is surfaced; the credential is still issued, without the portrait claim.
5. **Does the DLA Phase 2 walkthrough use the same wallet as Phase 1?** Yes — the citizen actor's wallet-dir is the single source of identity across both phases; no script-level state ferrying.

## Out of scope (explicitly deferred)

These are not researched here because they are deliberately not in v1. Each is natural v1.1+ work:

- Liveness detection on the selfie
- Automated document verification
- Real backend identity-validator service integration
- AI-mode agent with Claude vision
- Additional `x-review` layout variants beyond `id-card`
- Nationality, phone, and social-profile claims
- Per-issuer custom branding (logos, bespoke palettes)
- Bridge from Sorcha in-platform wallet to filesystem HAIP wallet-dir
- Playwright UI screenshot tests (deferred "until needed" per user)
