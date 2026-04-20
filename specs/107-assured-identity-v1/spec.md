# Feature Specification: Assured Identity v1

**Feature Branch**: `107-assured-identity-v1`
**Created**: 2026-04-20
**Status**: Draft
**Input**: User description: "Assured Identity v1 — consolidate the platform's 'verified person' story into a single canonical credential and walkthrough."
**Design Spec**: [`docs/superpowers/specs/2026-04-20-assured-identity-v1-design.md`](../../docs/superpowers/specs/2026-04-20-assured-identity-v1-design.md)

## Background

The platform's "verified person" story is currently split across two co-located blueprints in the same walkthrough, producing competing `VerifiedCitizenCredential` and `AssuredPersonCredential` variants that share the same shape and the same seven claims but differ only in their delivery pipe (external HAIP wallet versus register-native sealed disclosure). A second walkthrough, `HaipDrivingLicence`, chains off the HAIP variant to prove a downstream credential-consumer flow. The resulting duplication means there is no single canonical credential for citizen identity, the holder's choice of where to keep the credential is presented as a publisher-side decision rather than a claim-time preference, and the citizen-facing form is functional but unpolished — future-dated birthdays are not blocked in the picker, no photo capture is wired into the core renderer, and no review-before-submit step exists.

Separately, the platform has never exercised Feature 106 (register-native credential delivery) across two peers end-to-end. The architectural design is sound and the single-node code paths are unit-tested, but the cross-peer credential-delivery path — the primary reason Feature 106 exists — has not been verified in practice.

This feature delivers one canonical Assured Identity workflow that replaces both existing person-identity blueprints and the chained driving-licence walkthrough; makes the citizen-facing form substantially more polished by fixing three renderer gaps (future-date block on date-of-birth, photo capture dispatch, a new review-as-id-card schema extension); and bundles a cross-peer smoke test of register-native delivery so the largest untested architectural assumption in the platform's credential story is finally measured. The design also preserves a clean seam for replacing the human assessor role with a real backend identity-validator service at a later date, without any blueprint or platform changes required when that arrives.

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Citizen obtains a canonical Assured Identity credential with a polished form experience (Priority: P1)

A member of the public lands on the Assured Identity service, signs up for a public account, and fills in their details across a short wizard. Their persona profile pre-populates most fields; the date-of-birth field prevents them from picking a future date; the address field autofills from their postcode; the optional photo field lets them take a selfie directly from their device camera with passport-style composition guidance on screen. Before submission, they see a summary page styled like the credential they will receive — their name, date of birth, email, address, and photo laid out as an ID card — and can jump back to any section to correct mistakes. After they submit, the issuer (government) approves, and the citizen claims the credential into either their in-platform wallet or an external HAIP wallet via a single click.

**Why this priority**: This is the headline user experience the entire feature exists to deliver. Without it, none of the platform polish is visible, no credential is issued, and no downstream consumer has anything to consume. The form polish is specifically what transforms the current "functional but unpolished" experience into a showcase example of what the platform can do.

**Independent Test**: Fresh public account on a fresh deployment. Run through the Assured Identity application end-to-end. Verify the credential lands in the holder's chosen wallet with all entered claims, the photo is embedded as a selectively-disclosable claim sized for offline verification, and future dates are never pickable on the date-of-birth field. Testable without any downstream consumer (driving licence).

**Acceptance Scenarios**:

1. **Given** a fresh public account with no persona profile, **When** the citizen opens the Assured Identity application, **Then** they see a five-page wizard presenting one topic per page (about you, address, contact, photo, review) with a progress indicator across the top.
2. **Given** a public account with a saved persona profile that has name, date of birth, email, and address set, **When** the citizen opens the Assured Identity application, **Then** at least those four fields are pre-populated before the citizen touches any field, and each pre-populated field is visually marked as filled from the citizen's profile with a clear "self" provenance label.
3. **Given** the citizen is on the date-of-birth page, **When** they open the date picker, **Then** the picker will not allow any date after today to be selected.
4. **Given** the citizen is on the address page, **When** they type a valid UK postcode and a full-address lookup provider is configured, **Then** they are presented with address candidates and picking one fills all sibling address fields.
5. **Given** the citizen is on the photo page on a mobile device, **When** they tap the take-photo control, **Then** the device's front-facing camera opens and ICAO composition guidance (plain background, centred face, neutral expression, no sunglasses) is visible on screen.
6. **Given** the citizen does not want to provide a photo, **When** they are on the photo page, **Then** a clearly-labelled skip control lets them proceed to the review page without providing an image.
7. **Given** the citizen has completed pages 1–4, **When** they reach the review page, **Then** they see all their entered details rendered as a stylised credential card ("ID card layout") with a "draft" watermark indicating it is not yet issued, an "Edit" control for each section, and a primary submit control.
8. **Given** the citizen clicks Edit on a section from the review page, **When** they arrive back at the corresponding wizard page, **Then** every value they previously entered is still present and editable.
9. **Given** the citizen has submitted their application, **When** the issuer approves it, **Then** the citizen receives a claim notification and a single-click choice to land the credential in either their in-platform wallet or an external HAIP wallet.
10. **Given** the citizen has claimed the credential, **When** they open the credential's detail view in their wallet, **Then** they see the same ID card layout with the draft watermark replaced by an "issued" state showing the issue date and issuer name.
11. **Given** the citizen provided a photo, **When** the credential is inspected in their wallet, **Then** the photo is present as a selectively-disclosable claim at a size consistent with offline biometric verification (token-image resolution, per established portrait standards).
12. **Given** the citizen declined to provide a photo, **When** the credential is inspected in their wallet, **Then** it is still valid and usable for downstream services, with the portrait claim absent rather than empty.

---

### User Story 2 — Downstream service chain-consumes the Assured Identity credential (Priority: P2)

A citizen who already holds an Assured Identity credential applies to the Driver Licensing Authority (DLA) for a driving licence. They pick a vehicle class and review the licence-to-be on a second stylised card. When the DLA officer (or the unattended agent standing in for them) opens the application, they see both the identity the citizen presented (selectively disclosed — only given name, family name, date of birth, and portrait) and the licence-to-be as two stacked cards, and approve. A `DrivingLicenceCredential` is minted carrying the licence number, vehicle class, issue and expiry dates, the citizen's name and date-of-birth, and the citizen's portrait carried forward from the presented identity. The citizen claims the licence using the same claim-card mechanism as the identity credential.

**Why this priority**: Proves the identity credential is *useful* for the platform, not just mintable. Exercises the full credential chain including selective disclosure (address and email are withheld; the DLA has no business reason for them), key-binding proof (the holder of the identity must also sign the licence application), and cross-organisation trust (government and DLA are distinct issuers under one trust anchor). Without this story, the Assured Identity credential is a one-shot artefact with no validation that it can be consumed elsewhere.

**Independent Test**: Starting from a clean state, run user story 1 to receive an AssuredIdentityCredential into a HAIP-compatible wallet. Then run the driving-licence flow. Verify: the DLA review screen renders both stacked cards (presented identity with withheld claims faded, licence-to-be preview); the citizen receives a signed driving licence credential; the licence's holder identity matches the identity that was presented. Independently testable from any other downstream consumer: if we never add another consumer, story 2 still proves the credential is chainable.

**Acceptance Scenarios**:

1. **Given** a citizen holding an `AssuredIdentityCredential` in a HAIP-compatible wallet, **When** they open the driving licence application, **Then** they see a short wizard asking for vehicle class and presenting a licence-to-be review card on the final page.
2. **Given** the citizen has submitted the licence application, **When** the DLA requests identity verification, **Then** the citizen is prompted to present the specific claims the DLA has requested (given name, family name, date of birth, portrait), with the request clearly listing each claim.
3. **Given** the citizen consents to the presentation, **When** the platform presents the credential, **Then** only the requested claims are disclosed — the email and address claims are withheld from the DLA.
4. **Given** the DLA officer opens the pending licence application, **When** the review screen renders, **Then** it shows two stacked credential cards: the presented identity on top (with disclosed claims populated and withheld claims shown as faded placeholders with an explanatory label) and the licence-to-be below (in a distinct colour theme signalling the different credential type, with a "pending" watermark).
5. **Given** the DLA officer approves, **When** the licence credential is minted, **Then** the credential carries the licence number, vehicle class, issue date, expiry date, holder name, holder date of birth, and holder portrait (if the citizen chose to present a portrait).
6. **Given** the licence credential has been minted, **When** the citizen opens their My Actions queue, **Then** a claim card for the driving licence is pending and claiming it lands the credential in the same wallet as the identity.
7. **Given** the citizen has claimed the licence, **When** they open its detail view, **Then** it renders in the same ID card layout with a credential-type-appropriate colour theme distinct from the Assured Identity card.
8. **Given** the citizen did not provide a portrait for their Assured Identity, **When** the DLA issues their licence, **Then** the licence is still issued and the portrait claim on the licence is also absent.

---

### User Story 3 — Unattended assessment by a background agent (Priority: P3)

When the Assured Identity (or Driving Licence) workflow runs in a demo or walkthrough setting, a background process stands in for the human assessor. It picks up pending applications from its inbox, applies pre-declared rules (for the demo: approve if all required fields are present and the date-of-birth is plausible) or — for a future AI-mode variant — uses a vision model to examine the submitted photo, and posts the approval or rejection decision without a human opening the assessor UI. When a real backend identity-validation service becomes available later, it plugs in through the same agent mechanism as either an external agent mode or an HTTP call inside a rule, with no blueprint or platform changes required.

**Why this priority**: Makes the walkthrough demonstrable end-to-end without a human in the loop, which is required for autonomous CI-style walkthrough runs and for demos where the presenter doesn't want to mime being a government officer. Also locks in the design-seam for real automated identity validation: the blueprint's decision action is shape-stable against either a human or an automated reviewer, so whoever picks up validator integration next does not need to change the blueprint or the platform.

**Independent Test**: Run the Assured Identity walkthrough with only the citizen actor interactive and the assessor provided as a background agent. Verify: the application moves from submission to approval without any human opening the assessor UI; the approval transaction is cryptographically signed by the agent's wallet; the same assessor UI still renders correctly if a human opens it during the pending window. Independently testable from the other user stories: swap a rules-mode agent for a human-facing assessor and the other stories all still work.

**Acceptance Scenarios**:

1. **Given** an Assured Identity application has been submitted, **When** the assessor role is filled by a background agent running in rules mode, **Then** the agent picks the pending application from its inbox within a small number of seconds and posts the approval decision.
2. **Given** a Driving Licence application has reached the licensing-officer review step, **When** that role is filled by a background agent in rules mode, **Then** the agent posts the approval decision and the licence credential is minted.
3. **Given** the same Assured Identity application, **When** a human opens the assessor UI during the pending window, **Then** they see the standard approve/reject review screen with all submitted details.
4. **Given** a future validator-integration build where the agent's rule calls out to an HTTP endpoint for automated ID verification, **When** the endpoint returns a decision, **Then** the agent posts that decision to the platform in the exact same shape as the rules-mode decision — no blueprint or platform change is required.
5. **Given** a future AI-mode agent variant that uses a vision model to examine the submitted photo against the form data, **When** the agent is run in AI mode, **Then** its decision flows through the same pipeline as the rules-mode decision.

---

### User Story 4 — Cross-peer delivery smoke test of register-native credentials (Priority: P4)

A Sorcha operator needs to know whether the register-native credential delivery path (Feature 106) actually works when the issuing peer and the holding peer are different machines, not just when they are both on one node. The feature bundles a two-peer smoke test that issues an Assured Identity credential from peer A and verifies it lands in the holder's in-platform wallet on peer B within a small number of seconds. The test produces a findings document on every run — pass, fail, or anomaly — and does not block the feature from shipping if it surfaces replication issues, because those issues are scoped to whoever owns peer replication, not this feature.

**Why this priority**: Retires the largest untested architectural assumption in the platform's credential-delivery story without coupling the ship date of this feature to the remediation of any bugs that surface. Documented, measurable, and timeboxed. Once it exists, every release can run it cheaply.

**Independent Test**: Bring up a two-peer federation (both peers subscribed to the same register). Run the smoke script. Verify: the findings document is produced regardless of pass or fail; on pass, the credential exists in the holder's pending-acceptance list on peer B within the target latency; on acceptance, the accept transaction is cryptographically signed by the holder's key; any replication anomaly is recorded with a clear description.

**Acceptance Scenarios**:

1. **Given** a two-peer federation is running with both peers subscribed to the same Assured Identity register, **When** the smoke script issues a credential on peer A, **Then** the credential appears in the holder's pending-acceptance list on peer B within the configured latency budget.
2. **Given** the credential is pending on peer B, **When** the holder accepts it on peer B, **Then** the accept transaction is signed by the holder's key (not by peer A's key) and the credential transitions to active.
3. **Given** any cross-peer run (pass or fail), **When** the smoke script finishes, **Then** a findings document is written to a known location recording the outcome, the latency, and any anomalies observed.
4. **Given** the smoke script surfaces a replication bug, **When** the operator inspects the findings, **Then** the bug is clearly described with sufficient context for a peer-replication engineer to reproduce it, and the bug does not cause the feature's release to be blocked.
5. **Given** the smoke script passes consistently across multiple runs, **When** the release is cut, **Then** the findings history establishes baseline latency for future regression comparisons.

---

### User Story 5 — Consolidation of legacy walkthroughs and credential types (Priority: P5)

A platform user looking at the walkthroughs directory today sees two separate HAIP walkthroughs for citizen identity (`HaipVerifiedCitizen` and `HaipDrivingLicence`) and two separate credential type names (`VerifiedCitizenCredential` and `AssuredPersonCredential`) for the same semantic. After this feature, they see one canonical Assured Identity walkthrough with both phases inside it, and one credential type name. The legacy folders and type names are gone; no back-compat shims remain.

**Why this priority**: Delivers the "single canonical" promise of the feature. Without it, the consolidation is cosmetic — the old and new coexist indefinitely and the platform keeps carrying the duplication debt.

**Independent Test**: Fresh clone of the repository. Verify: `walkthroughs/AssuredIdentity/` exists and runs end-to-end; `walkthroughs/HaipVerifiedCitizen/` and `walkthroughs/HaipDrivingLicence/` no longer exist; no source file references `VerifiedCitizenCredential` or `AssuredPersonCredential` as a credential type name; the `HaipIdentityAttestation` walkthrough (different scope — proves the bare CLI) still exists and still runs.

**Acceptance Scenarios**:

1. **Given** a fresh repository checkout, **When** the walkthroughs directory is listed, **Then** `AssuredIdentity/` is present and `HaipVerifiedCitizen/` and `HaipDrivingLicence/` are both absent.
2. **Given** a code search for `VerifiedCitizenCredential` across the repository, **When** it completes, **Then** there are no live references outside historical design documents and past spec directories.
3. **Given** a code search for `AssuredPersonCredential` across the repository, **When** it completes, **Then** there are no live references outside historical design documents and past spec directories.
4. **Given** the `HaipIdentityAttestation` walkthrough, **When** it is run, **Then** it still passes end-to-end (it proves the bare `sorcha-agent haip receive` CLI and is not dependent on this feature).
5. **Given** the historical spec directories (`specs/103-verified-citizen-v2/`, `specs/104-credential-claim-action/`, `specs/106-register-native-credentials/`), **When** inspected, **Then** they are still present as historical context, and this spec links to them.

---

### Edge Cases

- **Citizen changes their mind mid-wizard.** They navigate back using the browser back button or the in-wizard Back control. All previously-entered values are preserved; navigating forward again does not reset them.
- **Citizen's persona profile has stale data.** They edit a persona-filled field. The "self" provenance label drops from that specific field; other persona-filled fields retain their labels. On submission, the edited value is what reaches the credential — not the original persona value.
- **Citizen declines the optional photo.** The credential is still issued. The portrait claim is absent, not empty. Any downstream service that *required* a portrait (via its presentation request) would reject the citizen's credential; services that only *request* it with "required: false" accept it.
- **Citizen takes a poor-quality photo (blurry, no face visible).** The platform does not automatically reject it — no automated composition checking in v1. The assessor (human or agent) rejects the application in their review step and the citizen is informed.
- **Citizen submits with a future date of birth because the client-side bound was bypassed** (e.g. programmatic submission, browser extension, schema-evasion attack). The server-side validator rejects the submission. The client-side block is convenience; the server-side constraint is authoritative.
- **Credential claim action times out.** If the citizen never claims within the Wave 14b credential offer's expiry window, the claim card expires and the application workflow is recorded as failed-to-claim. Re-submitting the application is the path forward; there is no "re-mint the credential" escape hatch.
- **Citizen picks "Scan with external wallet" on the claim card but their external wallet rejects the credential** (unsupported type, incompatible format). The claim falls back to the in-platform wallet path; the credential is still delivered to the citizen's Sorcha-held wallet.
- **Cross-peer smoke test detects a replication delay above the latency budget but the credential eventually arrives.** The findings document records both the delay and the eventual arrival. The test is marked as a degraded pass with a specific anomaly note; the feature does not fail to ship on degraded performance.
- **Cross-peer smoke test cannot even bring up the two-peer federation** (docker-compose failure, port conflicts). The smoke test is marked as "environment failure — not exercised this run" in the findings document; the feature still ships if the single-peer primary walkthrough passes.
- **DLA officer tries to approve a licence application where no AssuredIdentityCredential was presented** (presentation step skipped or failed). The licence-issue action refuses to proceed because its credential-presentation prerequisite is unmet; the officer sees a clear "no identity presented" message rather than a blank review card.
- **Portrait in the Assured Identity credential is above the size target** (e.g. the client-side resize failed or was bypassed). The issuer rejects the portrait claim rather than including an oversized image in the credential; a warning is surfaced to the citizen and the credential is issued without the portrait claim or the citizen is asked to re-submit the photo.
- **A walkthrough contributor who did not read this spec publishes a new blueprint with a participant pre-bound to the citizen wallet for an open starting action.** The publish-time guardrail from Feature 103 (VAL_BP_010) rejects the publish attempt with a clear message; this feature does not weaken that guardrail.

## Requirements *(mandatory)*

### Functional Requirements

#### Renderer polish (Workstream 1)

- **FR-001**: The date-picker form control MUST prevent the citizen from selecting a date that violates a standard past-only constraint declared on the schema (expressed via the Sorcha date token vocabulary, e.g. not after `today`).
- **FR-002**: The date-picker form control MUST continue to prevent violations of future-only and bounded-range constraints (e.g. `today+N years`, `today-N years`) consistently with past-only.
- **FR-003**: When a schema field declares a file-reference intent with capture advisory (user-facing camera), the form renderer MUST present a camera capture control on mobile devices that defaults to the front-facing camera, alongside an upload control on any device.
- **FR-004**: When a schema field declares a file-reference intent for an identity portrait with a target token size, the form renderer MUST produce a resized token-image (within the declared size target) on the client side before submission, alongside the original full-resolution image for issuer review.
- **FR-005**: The form renderer MUST present advisory composition guidance (e.g. ICAO portrait composition rules) alongside the capture control when the schema declares a portrait intent, without enforcing the guidance automatically.
- **FR-006**: The form renderer MUST support a new schema extension that marks a wizard page as a read-only review summary of prior pages' values, with a named layout variant.
- **FR-007**: The review-summary extension MUST support at minimum a credential-card layout ("id-card") that renders the collected data in a styled card resembling the credential to be issued, with a draft watermark indicating it is not yet issued.
- **FR-008**: The review-summary layout MUST be parameterisable by a per-use colour theme and per-use header (issuer name, credential display name) so that different credential types in the same workflow render with distinct visual identity without new per-credential components.
- **FR-009**: The review-summary extension MUST generate per-section edit controls (when declared editable) that navigate the wizard back to the originating page with all previously-entered data intact.
- **FR-010**: The review-summary component MUST render on both the citizen-side (draft state, Edit + Submit controls) and the issuer-side (pending state, Approve + Reject controls), with the action set derived from the blueprint's routes and the state signalled by a per-state watermark and colour treatment.
- **FR-011**: The review-summary layout MUST be capable of rendering two cards stacked in a single review context (for credential-chain workflows: presented credential above, credential-to-be below), with withheld claims rendered as visibly faded placeholders with an explanatory label.
- **FR-012**: The review-summary component in its card layout MUST be reusable for rendering an already-issued credential's detail view in the wallet (with the draft watermark removed and the state set to "issued"), so that the citizen sees the same visual both when previewing before submission and when holding the credential afterwards.

#### Assured Identity credential and blueprint (Workstream 2)

- **FR-013**: The platform MUST provide a single canonical Assured Identity workflow that accepts an open citizen submission, routes it through an assessor approval, and issues a credential to the citizen's chosen wallet.
- **FR-014**: The Assured Identity credential MUST carry claims for given name, middle name (optional), family name, full name (derived), date of birth, email, and structured postal address (line 1, optional line 2, town, optional region, postcode, country), with every claim selectively disclosable.
- **FR-015**: The Assured Identity credential MUST optionally carry a portrait claim when the citizen provided a photo, with the portrait embedded at a token-image resolution consistent with offline biometric verification standards, and the claim selectively disclosable.
- **FR-016**: The Assured Identity workflow MUST allow the holder to choose, at claim time, whether to land the credential in their in-platform Sorcha wallet (register-native delivery) or in an external HAIP wallet.
- **FR-017**: The Assured Identity workflow's submission action MUST be an open starting action with the citizen participant late-bound to the first submitter (per the Feature 103 open-participant contract).
- **FR-018**: The Assured Identity workflow's submission form MUST use the citizen-facing persona profile for autofill on every field that has a corresponding persona attribute, following the Feature 103 explicit persona binding rules.
- **FR-019**: The Assured Identity workflow's submission form MUST use the Feature 103 shared identity primitives (personal name, date of birth, email, postal address) via standard schema reference — no inline duplicate schemas.
- **FR-020**: The Assured Identity workflow's submission form MUST present as a multi-page wizard with one concept per page (name and date of birth, address, contact, photo, review) in that order.
- **FR-021**: The Assured Identity workflow's review page MUST use the new review-summary extension with the id-card layout variant and declare the credential type's display name and issuer name for the card header.

#### Driving Licence credential and blueprint (Workstream 3)

- **FR-022**: The platform MUST provide a Driving Licence issuance workflow that requires the citizen to present an Assured Identity credential as a prerequisite, issues a DrivingLicenceCredential on approval, and uses the same claim-card delivery mechanism as the Assured Identity workflow.
- **FR-023**: The Driving Licence workflow's identity-presentation step MUST request only given name, family name, date of birth, and portrait from the Assured Identity credential — it MUST NOT request email or address.
- **FR-024**: The Driving Licence credential MUST carry claims for licence number, vehicle class, issue date, expiry date, holder name, holder date of birth, and holder portrait (carried forward from the presented identity when present), with a ten-year validity period.
- **FR-025**: The Driving Licence workflow's submission action MUST also be an open starting action with the citizen participant late-bound, so that the same wallet that holds the Assured Identity credential is the one that submits the licence application.
- **FR-026**: The Driving Licence workflow's approve-and-issue review screen MUST use the new review-summary extension with two stacked cards: the presented Assured Identity above (verified state, disclosed claims filled, withheld claims faded) and the licence-to-be below (pending state, in a distinct colour theme appropriate to the credential type).

#### Unattended assessor via background agent (Workstream 4)

- **FR-027**: The Assured Identity and Driving Licence workflows' assessor review actions MUST be shaped so that a background `sorcha-agent` process in rules mode can fill them without blueprint or platform modifications.
- **FR-028**: The actor definitions for the walkthrough MUST include rules-mode configurations for the government-assessor and DLA-officer roles that stamp approve on valid applications.
- **FR-029**: The assessor review action's decision schema MUST be shape-stable against a future agent mode that calls out to an external identity-validation service, so that introducing the external mode later is purely additive to the agent and does not require blueprint or platform changes.
- **FR-030**: The assessor human-facing UI MUST remain fully functional when a human opens the pending application during the window between submission and agent pickup, so that humans can still override or audit the agent's decisions.

#### Consolidated walkthrough (Workstream 5)

- **FR-031**: The repository MUST contain a single walkthrough directory (`walkthroughs/AssuredIdentity/`) that hosts both the Assured Identity issuance phase and the Driving Licence chain phase, with shared actors and shared state between phases.
- **FR-032**: The walkthrough MUST provide scripts to run each phase independently and to run both phases end-to-end, so that individual-phase testing and full-lifecycle demonstration are both supported.
- **FR-033**: The walkthrough setup MUST provision all organisations, wallets, participants, and credentials-or-registers required for both phases in a single idempotent setup invocation.
- **FR-034**: The walkthrough's primary (single-peer) run MUST exercise the HAIP external wallet-dir delivery path, because the Driving Licence phase's OpenID4VP presentation requires a filesystem-resident holder wallet.
- **FR-035**: The walkthrough's citizen actor definition MUST be reused across both phases without script-level state ferrying — the credential received in phase 1 is presented in phase 2 directly from the same actor's wallet directory.

#### Cross-peer smoke test (Workstream 6)

- **FR-036**: The repository MUST contain a two-peer federation composition definition sufficient to stand up two Sorcha node stacks subscribed to the same register.
- **FR-037**: The repository MUST contain a smoke-test script that runs the Assured Identity phase 1 workflow with the issuer on one peer and the holder on the other peer, using register-native delivery specifically (so the cross-peer replication path is actually exercised).
- **FR-038**: The smoke-test script MUST produce a findings document on every run (pass, fail, or environment failure) recording the outcome, observed latency, and any anomalies.
- **FR-039**: The smoke test MUST NOT block the feature's release on a failure or anomaly — its purpose is measurement, not gating.
- **FR-040**: The smoke test MUST verify that the holder's accept-or-decline action on the remote peer is cryptographically signed by the holder's own key (not by the issuing peer's key).

#### Consolidation and cleanup (Workstream 7)

- **FR-041**: The repository MUST delete `walkthroughs/HaipVerifiedCitizen/` and `walkthroughs/HaipDrivingLicence/` in full as part of this feature, leaving no residual files or blueprint definitions from those walkthroughs.
- **FR-042**: The repository MUST retain `walkthroughs/HaipIdentityAttestation/` — it proves the bare HAIP CLI path and is not part of the consolidation.
- **FR-043**: No live source code or configuration outside historical design or spec documents MUST reference the credential type names `VerifiedCitizenCredential` or `AssuredPersonCredential` after this feature ships.
- **FR-044**: The repository MUST retain the historical spec directories for Features 103, 104, and 106 as context, and this feature's spec MUST link back to them.

### Key Entities

- **Citizen** — A member of the public who holds a Sorcha public-org account and may submit citizen-facing applications. Identified by a wallet under the public organisation. Carries a persona profile for autofill across services.
- **Government Assessor** — A staff role within the Assured Identity issuing organisation who reviews citizen applications and approves credential issuance. May be filled by a human user or a background agent in rules or AI mode.
- **DLA Officer** — A staff role within the Driver Licensing Authority. Verifies the citizen's presented identity and approves driving licence issuance. Same agent/human duality as the government assessor.
- **Service Designer** — A human or AI assistant who authors blueprint definitions. For this feature they consume the Feature 103 identity primitives and the new review-summary extension.
- **Assured Identity Credential** — A Verifiable Credential asserting that a citizen's name, date of birth, email, address, and optional portrait have been verified by the issuing government organisation to its assurance standard. Selectively disclosable. Replaces the prior `VerifiedCitizenCredential` and `AssuredPersonCredential` types.
- **Driving Licence Credential** — A Verifiable Credential issued by the DLA asserting the citizen's right to drive a specified vehicle class until a specified expiry date. Carries the citizen's portrait when the Assured Identity's portrait was presented.
- **Review Summary (ID Card Layout)** — A new schema-extension-driven UI pattern that renders a read-only summary of a multi-page wizard's collected data as a styled credential card. Parameterised by issuer name, credential display name, colour theme, and edit-enable flag. Reused for citizen-side pre-submission review, issuer-side pending review, and wallet-side issued credential detail view.
- **Portrait** — An optional citizen-provided photograph, captured via camera or uploaded, resized to a token-image resolution for embedding in the credential as a selectively-disclosable claim. Full original kept on the register as evidence.
- **Validator Agent** — A `sorcha-agent` background process filling the assessor role in a walkthrough. Runs in rules mode for the v1 demo; designed for a future AI mode (vision-based review) and a future external mode (integration with a real identity-validation service).
- **Cross-Peer Smoke Test** — A measurement artefact, not a gate, that exercises Feature 106 register-native credential delivery across two Sorcha peers and documents findings.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A citizen with a populated persona profile can complete the Assured Identity application end-to-end — from first form view to receiving the claim notification — in **under 3 minutes**.
- **SC-002**: A citizen who holds an Assured Identity credential can complete the Driving Licence application — from first form view to receiving the licence claim notification — in **under 2 minutes**.
- **SC-003**: When the Assured Identity form is first rendered for a citizen with a complete persona profile, **at least 90% of auto-fillable fields are pre-populated** before the citizen touches any field.
- **SC-004**: The date-of-birth picker **rejects 100% of future-date selection attempts** in its client-side UI, with the server-side constraint continuing to reject any future date that bypasses the client.
- **SC-005**: When a citizen provides a photo, the embedded portrait claim in the issued credential **is within the token-image size target** (approximately 20KB or less) and meets the declared resolution requirements.
- **SC-006**: A citizen on the driving-licence flow sees **exactly the four claims** (given name, family name, date of birth, portrait) on their consent-to-present screen, and those are the exact four claims disclosed — email and address **do not** leave the citizen's wallet.
- **SC-007**: When the review-summary id-card layout renders, a **first-time user can identify the "Edit" control for a given section within 5 seconds** and editing that section returns to the corresponding wizard page with all prior data intact.
- **SC-008**: When the assessor role is filled by a background agent in rules mode, the application transitions from submitted to approved **within 30 seconds** of submission under normal demo conditions.
- **SC-009**: The cross-peer smoke test, when it passes, completes credential delivery from issuing peer to holding peer **within 30 seconds** across 5 consecutive runs on the reference hardware.
- **SC-010**: After this feature ships, the repository contains **zero live code references** to `VerifiedCitizenCredential` or `AssuredPersonCredential` outside historical design documents and archived spec directories.
- **SC-011**: The consolidated `walkthroughs/AssuredIdentity/` directory **reduces total file count** versus the sum of `walkthroughs/HaipVerifiedCitizen/` and `walkthroughs/HaipDrivingLicence/` before consolidation, with no loss of tested scenarios.
- **SC-012**: A walkthrough author who adds a new credential-issuance blueprint using the review-summary extension **produces the review screen with zero new bespoke UI components** — parameters alone (colour, issuer name, layout variant) suffice.
- **SC-013**: The cross-peer smoke test produces a findings document on **100% of runs** regardless of pass, fail, or environment-failure outcome.
- **SC-014**: An administrator who deletes the `HaipVerifiedCitizen` and `HaipDrivingLicence` folders on the feature branch **finds no failing test, broken reference, or orphaned file** elsewhere in the repository as a result.

## Assumptions

The following defaults were chosen during specification authoring rather than asked as clarifications:

- **Identity floor**: a citizen using Assured Identity is already authenticated to a public-org account (email/password or social login). The public-org account flow is the floor; fully anonymous entry is out of scope.
- **HAIP pipeline is in place**: OpenID4VCI pre-authorised code flow, OpenID4VP `direct_post` presentation, KB-JWT key binding, and the Wave 14b credential-claim-action pattern are assumed to be fully implemented from Features 098, 101, 102, and 104.
- **Register-native delivery is in place**: Feature 106's sealed-disclosure credential delivery, inbound detection, instance mirror reconstruction, and claim-card dual-path (in-platform vs external) are assumed present on single-peer. This feature is the first to measure the cross-peer path end-to-end.
- **Identity primitive library is in place**: Feature 103's shared schema components (`PersonName/v1`, `DateOfBirth/v1`, `EmailAddress/v1`, `PostalAddress/v1`) and their server-side `$ref` resolution are assumed present. This feature consumes them, does not extend or alter them.
- **Open starting action contract is in place**: Feature 103's late-binding, `VAL_BP_010` publish-time guardrail, and walkthrough-module auto-skip of open-participant wallet patching are assumed present. This feature does not weaken or modify them.
- **Agent framework is sufficient**: the existing `sorcha-agent` rules-mode capability is sufficient for the v1 assessor automation. AI-mode is deferred; external-API mode is deferred (both are natural v1.1 additions).
- **Photo embedding follows established biometric portrait standards**: the embedded portrait claim uses a token image consistent with ISO/IEC 19794-5 token image dimensions (~240×320) and mDL (ISO 18013-5) portrait embedding practice (~15-30KB JPEG). No bespoke biometric format is introduced.
- **Cross-peer testing cadence**: the smoke test runs once per release cycle. Automatic per-commit execution is out of scope.
- **Walkthrough single-peer primary path uses HAIP delivery**: because the Driving Licence OpenID4VP presentation currently requires a filesystem-resident wallet, the primary walkthrough uses HAIP delivery for phase 1 so that phase 2 works end-to-end. Register-native delivery is proven separately via the cross-peer smoke test.
- **No back-compat shims**: the old credential type names and the old walkthrough directories are removed outright. Any external repository or script depending on the old names is the responsibility of that external consumer; there is no canary migration period.
- **ICAO composition guidance is advisory, not enforced**: no face detection, no background uniformity check, no quality-score gate in v1. Automated composition checking is deferred to a future validator integration.
- **Credential validity period for the driving licence**: ten years, matching standard real-world conventions. Renewal flow is out of scope.

## Out of Scope

The following are deliberately deferred:

- **Liveness detection on the selfie** (face movement, blink challenge, anti-spoofing).
- **Automated document verification** (scanning an existing ID, OCR, cross-reference against authoritative records).
- **Real backend identity-validator service integration** (vendor selection, contract, integration plumbing).
- **AI-mode agent for assessment** (Claude vision reading the submitted photo and making a contextual decision).
- **Additional review-summary layout variants** beyond the id-card: passport-page, receipt, tabular, timeline, and others are reserved for future features as needs arise.
- **Nationality, phone, and social-profile claims** on the Assured Identity credential. The v1 claim set is exactly the consolidated superset of what the prior credentials carried, plus portrait.
- **Issuer-organisation custom branding**: per-org logos, bespoke colour palettes, custom seal designs on the id-card layout. The v1 colour theming is per-credential-type, not per-issuer-org.
- **Bridge from Sorcha in-platform wallet to filesystem HAIP wallet-dir**: so that a register-native-delivered credential could be used directly in an OpenID4VP presentation without an external HAIP wallet. Natural v1.1.
- **Per-issuer credential-template overrides**: custom card layouts or label text per issuing organisation beyond the parameterised colour / name / variant. Natural v1.1.
- **Bulk-issuance flows**: employer-side workforce identity issuance, school-side student identity, etc.
- **Renewal flows** for either the Assured Identity credential or the Driving Licence credential.
- **Revocation UX for citizens**: how a citizen sees that their Assured Identity has been revoked by the issuer. Revocation transactions themselves exist under Feature 079; citizen-facing UX is deferred.
- **Multi-country address lookup providers**: UK-only, inheriting Feature 103's initial scope. Non-UK providers plug in through the existing abstraction when needed.
- **Playwright UI screenshot tests**: deferred until explicitly needed.
- **Cross-peer federation automation**: the cross-peer smoke test is manual-per-release. Automated per-commit cross-peer testing is a separate investment.

## Phase Mapping (informative)

This feature is delivered in **seven sequential phases**, grouped into three shippable units. Each phase delivers a discrete, reviewable increment. Phases 1–2 ship the platform polish (Cluster A + identity blueprint); phases 3–4 ship the chain-consumption proof (DLA + cross-peer); phases 5–7 consolidate and clean up.

| Phase | Workstream(s) | Primary User Story | Independently Shippable? |
|---|---|---|---|
| 1 | Renderer polish (WS1) | Enables US-1 | Partially — DOB block and review-summary extension ship cleanly; photo capture depends on downstream form referencing it |
| 2 | Assured Identity credential + blueprint (WS2) | US-1 | Yes once Phase 1 is in; closes the headline citizen experience |
| 3 | Driving Licence credential + blueprint (WS3) | US-2 | Yes — depends on Phases 1+2 |
| 4 | Unattended assessor agent (WS4) | US-3 | Yes — depends on Phase 2 (at minimum); enhances Phases 2 and 3 |
| 5 | Consolidated walkthrough (WS5) | US-1, US-2 (end-to-end validation) | Yes once Phases 2+3+4 are in |
| 6 | Cross-peer smoke test (WS6) | US-4 | Yes — depends on Phases 2 and 5; runs in parallel with Phase 5 |
| 7 | Consolidation and cleanup (WS7) | US-5 | Yes — depends on Phase 5 complete; must be last to avoid breaking incremental phase validation |

Detailed implementation routes for each phase, including task-level breakdowns, dependency graphs, and acceptance gates, will be produced by `/speckit.plan` after this specification is reviewed.
