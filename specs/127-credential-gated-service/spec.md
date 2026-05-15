# Feature Specification: Credential-gated second council service (Blue Badge)

**Feature Branch**: `127-credential-gated-service`
**Created**: 2026-05-15
**Status**: Draft
**Input**: User description: "Spec 4 of the Strathcarron citizen arc: credential-gated second service (Blue Badge). Builds on F124/F125/F126. Locked design at docs/superpowers/specs/2026-05-15-spec-4-credential-gated-second-service-design.md."

**Locked design contract**: [`docs/superpowers/specs/2026-05-15-spec-4-credential-gated-second-service-design.md`](../../docs/superpowers/specs/2026-05-15-spec-4-credential-gated-second-service-design.md). This spec restates the requirements at the user-value / business-outcome layer; the design owns the technical shape.

**Boundary contract**: [`docs/superpowers/specs/2026-05-15-platform-consumer-boundary-design.md`](../../docs/superpowers/specs/2026-05-15-platform-consumer-boundary-design.md). Application-specific code (the Strathcarron council pages) lives in `samples/`; shared infrastructure (the credential-gating library component, the platform's verification endpoints) lives in `src/`.

## User Scenarios & Testing *(mandatory)*

Sarah is the protagonist. She's the Strathcarron resident from Spec 1–3 who already holds an `AssuredIdentityCredential` issued at the end of her driving-licence application. She returns to her council weeks later to apply for a Blue Badge.

### User Story 1 — Returning Tier 1 citizen completes the gated journey (Priority: P1)

Sarah is signed in, has a paired wallet device, and holds the `AssuredIdentityCredential` from Spec 3. She browses to the council's Blue Badge page, taps a "Prove you're you" affordance, picks the credential in her wallet, confirms consent, and the council form is pre-populated with her identity claims. She fills the Blue Badge-specific fields, submits, and the `BlueBadgeCredential` lands in her wallet.

**Why this priority**: This is the headline demo beat of Spec 4 — the moment the existing wallet stops being "the thing that received a credential" and starts being "the thing that proves something." If only one user story shipped, it would be this one.

**Independent Test**: Provision a Tier 1 citizen via the walkthrough script (chains off Spec 3's `state.json`), browse to the Blue Badge page, walk the journey end-to-end. Success = the wallet receives the new credential and the page surfaced no error states.

**Acceptance Scenarios**:

1. **Given** Sarah is signed in with a paired wallet device and holds a valid `AssuredIdentityCredential`, **When** she taps "Prove you're you" on the Blue Badge page, **Then** her wallet presents the credential and the council page receives the disclosed claims within 2 seconds of her confirmation in 95% of attempts.
2. **Given** the council page has received the disclosed claims, **When** the page rerenders, **Then** the identity fields (given name, family name, date of birth, home address) are pre-populated from the credential and only the Blue Badge-specific fields remain to fill.
3. **Given** Sarah submits the completed form, **When** the blueprint runs, **Then** the `BlueBadgeCredential` is delivered to her wallet via the same register-native path that delivered her `AssuredIdentityCredential` in Spec 3, with no first-credential takeover (she's a returning citizen).

---

### User Story 2 — Consumer council page lives outside the platform (Priority: P1)

The council's Blue Badge page does NOT live in the Sorcha platform repo's web client. It lives in a separate sample artifact (`samples/strathcarron-portal/`) that consumes the platform's shared component library the same way a real third-party council deployment would. The structural extract of the F126 driving-licence page into that sample is the first delivery of Spec 4; the Blue Badge page joins it as new content.

**Why this priority**: Co-equal with User Story 1. The boundary doc treats this as a prerequisite, not a polish item — if the council page ships inside the platform repo, the demo lies about the integration shape. Spec 4 is the natural place to set up the build topology because Spec 4 is adding a second council page anyway; doing it once for two pages is cheaper than retrofitting.

**Independent Test**: After PR-A lands, confirm `samples/strathcarron-portal/` builds to its own container image, runs as a standalone service in `docker-compose`, serves the driving-licence page (existing) and Blue Badge page (new), and contains no `ProjectReference` into `src/Apps/Sorcha.UI/` except `Sorcha.UI.Components.User`. A CI grep gate fails the build if a forbidden reference is added.

**Acceptance Scenarios**:

1. **Given** the project is checked out fresh, **When** `docker-compose up` runs, **Then** a separate `strathcarron-portal` container starts alongside the rest of the Sorcha stack and serves both council pages.
2. **Given** a contributor adds a `ProjectReference` from `samples/strathcarron-portal/Sorcha.Sample.StrathcarronPortal.csproj` to `src/Apps/Sorcha.UI/Sorcha.UI.Core/Sorcha.UI.Core.csproj`, **When** CI runs, **Then** the grep gate fails the build with a message naming the forbidden reference.
3. **Given** a contributor lands the structural PR-A, **When** they run the sample container locally, **Then** the existing F126 cold-start journey (driving licence) still works end-to-end against the same Sorcha APIs — moving the page does not regress the F126 walkthrough.

---

### User Story 3 — Cold-start citizen who lacks the gating credential is routed back (Priority: P2)

A citizen arrives at the Blue Badge page without an `AssuredIdentityCredential`. The page surfaces a clear, dead-end-free error state that points them at the driving-licence flow first.

**Why this priority**: Spec 4's failure paths are real and visible — a citizen who can't get past the gate must be given an actionable next step. Without this, Spec 4's "no dead-ends" promise breaks the moment a new citizen tries the Blue Badge flow first.

**Independent Test**: Provision a citizen via the walkthrough script who is signed in and has a paired wallet device but no `AssuredIdentityCredential`. Browse to the Blue Badge page. Confirm the error state renders, the message names the missing credential, and a link routes back to the driving-licence form.

**Acceptance Scenarios**:

1. **Given** a signed-in citizen with a paired wallet device but no `AssuredIdentityCredential` issued by Strathcarron Council, **When** they attempt to present a credential against the Blue Badge gate, **Then** the council page surfaces "We need an Assured Identity credential from Strathcarron Council to continue" with a link to the driving-licence application.
2. **Given** a citizen scans someone else's presentation QR onto their own wallet, **When** the wallet would present their credential, **Then** the wallet's confirmation dialog names both the council asking and the credential being presented before any signing happens, allowing the citizen to cancel.
3. **Given** a citizen presents an `AssuredIdentityCredential` that has been revoked, **When** the council verifies the presentation, **Then** the council page surfaces "This credential has been revoked. Please contact Strathcarron Council" and does not progress the application.

---

### User Story 4 — Multi-credential picker handles the ambiguous case (Priority: P3)

If a citizen holds more than one credential that satisfies the Blue Badge gate (rare in real life but common during testing), the wallet picker surfaces all matching credentials sorted by issuance date, and the citizen picks one before confirming consent. If only one matches, the picker hides and the citizen confirms consent directly.

**Why this priority**: Spec 4's primary demo path is "one matching credential" — the picker only earns its place when the multi-match case appears. Test environments hit this regularly; production users rarely will until later in the arc. P3 because it's edge-case behaviour, not the load-bearing UX.

**Independent Test**: Provision a citizen with two valid `AssuredIdentityCredential`s in their wallet, walk the journey, confirm the picker renders with both credentials and lets the citizen select one before consent. Then repeat with a single-credential citizen and confirm the picker is suppressed.

**Acceptance Scenarios**:

1. **Given** Sarah holds exactly one `AssuredIdentityCredential`, **When** she taps "Prove you're you," **Then** the wallet shows only the consent sheet (picker hidden).
2. **Given** Sarah holds two matching credentials, **When** she taps "Prove you're you," **Then** the wallet shows both sorted by issuance date (newest first) and requires a selection before the consent sheet renders.

---

### Edge Cases

- **Presentation request expired** before the citizen completes the wallet step: the council page surfaces "QR expired — let's get you a new one" with a regenerate affordance, mirroring Spec 3.
- **PWA loses connectivity** mid-flow: the citizen returns to a partially-completed form on the council page; the F125 sessionStorage form-state pattern preserves their input across the round trip.
- **SignalR connection unavailable** when the PWA posts the presentation: the council page falls back to polling on the same 3-second cadence as F126's `IEnrolPairingSignal`. After 60 seconds with no signal, a manual-recovery affordance appears.
- **Citizen's wallet is paired to a stranger's device**: the PWA-side confirmation dialog before signing surfaces both the council's identity and the credential type being presented, mirroring F126's friend-scans mitigation.
- **Existing F124 / F125 / F126 demo journeys regress** during Spec 4 PR-A's structural extract: the extract is non-functional in intent — the F126 driving-licence walkthrough must still work end-to-end after the page moves.

## Requirements *(mandatory)*

### Functional Requirements

**Credential-gated blueprint authoring**

- **FR-001**: Blueprint authors MUST be able to declare that a starting action requires a presentation of a named credential type issued by a named issuer, via a `prerequisites.presentationRequests` block on the action.
- **FR-002**: The platform MUST resolve a credential-gated starting action into a presentation request that names the credential type, the issuer allowlist, and the claims required by the form.
- **FR-003**: The blueprint runtime MUST reject a presentation that does not satisfy the declared `requiredClaims` and surface the failure to the council page in an actionable form.

**Consumer-facing council surface**

- **FR-004**: The Blue Badge council page MUST live in `samples/strathcarron-portal/`, not in `src/Apps/Sorcha.UI/`.
- **FR-005**: The existing F126 driving-licence council page (`src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/CouncilApplicationDrivingLicence.razor`) MUST be moved into the same sample as the first PR of Spec 4 (PR-A), preserving the F126 walkthrough behaviour end-to-end.
- **FR-006**: The `samples/strathcarron-portal/` artifact MUST build to its own container image, MUST be wired into `docker-compose.yml`, and MUST NOT add a `ProjectReference` into `src/Apps/Sorcha.UI/` other than `Sorcha.UI.Components.User`.
- **FR-007**: CI MUST enforce FR-006 via a grep gate over `samples/**/*.csproj` that fails the build on a forbidden reference.
- **FR-008**: The council sample MUST carry plausible council scaffolding — header with council logotype, primary navigation (services / about / contact), footer with council address and accessibility links — so the demo reads as a real council site on first glance.
- **FR-009**: The council sample MUST NOT deploy to n1 as part of Spec 4. Local docker-compose only.

**Citizen experience on the council page**

- **FR-010**: When a citizen arrives at a credential-gated council page already signed in with a paired wallet device and holding a matching credential, the gate MUST complete the present-from-wallet step within 45 seconds of the citizen tapping the "Prove you're you" affordance in 95% of attempts.
- **FR-011**: After the citizen presents the credential, the council form MUST be pre-populated with the disclosed claims and only the application-specific fields MUST remain for the citizen to fill.
- **FR-012**: The citizen MUST be able to drive the council application from either device — picking up the hybrid universal QR by scanning from a separate phone OR tapping the link on the same device.
- **FR-013**: When the citizen submits the application, the issued credential MUST land in the same wallet that delivered the presented credential, via the existing register-native delivery path, with no first-credential takeover.

**Citizen experience in the wallet**

- **FR-014**: When the wallet receives a presentation request and the citizen's wallet holds exactly one matching credential, the wallet MUST suppress the picker and render only the consent sheet.
- **FR-015**: When the wallet's citizen holds more than one matching credential, the wallet MUST render a picker sorted by issuance date (newest first) and require a selection before the consent sheet renders.
- **FR-016**: The wallet's consent sheet MUST list every claim being disclosed and require an explicit confirmation before signing. The consent surface is all-or-nothing in this spec — the citizen confirms the full claim set or declines.
- **FR-017**: Before signing, the wallet MUST surface a confirmation dialog that names both the verifier (council) and the credential type being presented, so a citizen who scanned someone else's QR can cancel.

**Failure paths**

- **FR-018**: A citizen lacking a matching credential MUST see a council-page error state that names the missing credential and links back to a flow that issues it.
- **FR-019**: A citizen presenting a revoked credential MUST see a council-page rejection that names the revocation and does not progress the application.
- **FR-020**: An expired presentation request MUST surface a regenerate affordance on the council page, mirroring the F126 expiry pattern.

**Cross-device coordination**

- **FR-021**: When the wallet posts a signed presentation, the council page MUST learn of the completion within 2 seconds in 95% of attempts via the platform's primary signalling channel, with a 3-second polling fallback and a 60-second manual-recovery affordance — the same cadence as F126's pairing signal.

### Key Entities

- **Credential gate**: A declared prerequisite on a blueprint starting action that names a credential type, an issuer allowlist, and the claims to be disclosed. Resolved at runtime into a presentation request the council page advertises.
- **Presentation request**: A short-lived artifact minted from a credential gate; carries the request URI, a nonce, and an expiry. Rendered as a hybrid universal QR / tap-link / paste affordance on the council page.
- **Presentation response**: A signed verifiable presentation produced by the wallet, posted by the wallet to the platform, validated server-side, and stashed against the nonce for the council page to fetch.
- **Issued credential (Blue Badge)**: A council-issued credential delivered to the same wallet that produced the presentation; same shape and delivery path as the `AssuredIdentityCredential` from Spec 3, different blueprint and different visual rendering.
- **Strathcarron sample portal**: The consumer-side artifact that hosts the council's citizen-facing pages. Lives in `samples/strathcarron-portal/`, builds to its own container, consumes the platform's component library via its published surface only.

## Assumptions

- Sarah's existing `AssuredIdentityCredential` (issued at the end of Spec 3's driving-licence walkthrough) is the credential the Blue Badge gate accepts. No new issuer setup, no new keys, no new register; the existing Strathcarron Council credentials register from F126 carries both credential types.
- The walkthrough seed script for Spec 4 depends on Spec 3's `state.json` — operators run the Spec 3 cold-start setup first, then Spec 4's setup picks up the citizens it created.
- The hybrid universal QR pattern (one URL, three resolutions: scan / tap / paste) carries through from Spec 3 unchanged.
- The consent surface is all-or-nothing in v1. Per-claim disclosure toggles are deferred to a future spec when a real use case appears.
- The PWA-side friend-scans confirmation is the v1 mitigation. Server-set cookie binding of the presentation request is deferred to Spec 5, where the verifier-is-not-the-issuer story is the load-bearing concern.
- The production `IIssuerKeyResolver` (DID-resolution against tenant register verification methods) is owned by Spec 5. Spec 4 uses the existing demo resolver.
- Council sample chrome is deliberately not Material Design defaults — it carries a distinct council visual identity to make "this is a council site, not a Sorcha surface" land on first glance.

## Dependencies

- **F124** (AssuredIdentity on the PWA, tag `spec-124-complete`): provides the first credential Sarah holds.
- **F125** (Sorcha Wallet User Agent, tag `spec-125-complete`): provides the picker surface, consent sheet, and the `Sorcha.Verifier.Engine` validator that Spec 4 reuses server-side as the first non-PWA consumer.
- **F126** (Enrol inside wizard, tag `spec-126-complete`): provides the enrol gate, the hybrid universal QR affordance, the pairing-signal pattern Spec 4's presentation-signal mirrors, and the F126 driving-licence council page that Spec 4 PR-A extracts into the sample.
- **F092** (Consumer persona + `x-persona` autofill resolver): extended in Spec 4 to accept a presented credential as the autofill source.
- **F079** (Trust hardening — credential revocation + status lists): provides the revocation path that FR-019 exercises.
- **Platform-vs-consumer boundary** (`docs/superpowers/specs/2026-05-15-platform-consumer-boundary-design.md`): provides the rule that locates the council pages in `samples/`.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A returning Tier 1 citizen holding `AssuredIdentityCredential` completes the Blue Badge journey from "click Apply" to "form ready to fill" in under 45 seconds in 95% of attempts.
- **SC-002**: The disclosed identity fields on the Blue Badge form are pre-populated with no manual entry by the citizen in 100% of successful presentation attempts.
- **SC-003**: A citizen without an `AssuredIdentityCredential` arriving at the Blue Badge gate sees a clear, dead-end-free error state that points them at the driving-licence flow within one screen.
- **SC-004**: The presentation-completion signal reaches the council page within 2 seconds of the wallet signing the presentation in 95% of attempts.
- **SC-005**: A revoked credential presented against the gate is rejected with an actionable message that does not progress the application.
- **SC-006**: Existing F124, F125, and F126 demo journeys remain green after Spec 4 lands — the F126 cold-start walkthrough specifically must still complete end-to-end after PR-A moves the driving-licence page.
- **SC-007**: The `samples/strathcarron-portal/` artifact builds and runs as a standalone container against the docker-compose stack, contains no `ProjectReference` into `src/Apps/Sorcha.UI/` other than `Sorcha.UI.Components.User`, and the CI grep gate fails the build on any violation.
- **SC-008**: A new viewer who has never seen the demo can be walked from "Sarah is at the Blue Badge page" to "the new credential is in her wallet" in a single uninterrupted demo session of under 5 minutes, with no narrative seams or operator interventions.

## Out of Scope

- Fully external cross-org presentation (citizen presents a Strathcarron credential to a non-council verifier). Architecturally supported via `Sorcha.Verifier.Engine`; lands in Spec 5.
- Per-claim disclosure toggles on the consent sheet. Deferred until a real use case appears.
- Server-set cookie binding for the presentation request. Deferred to Spec 5.
- Multi-issuer credential matching (a gate that accepts any of several issuers). One issuer per credential per umbrella invariant #2.
- Production `IIssuerKeyResolver` (DID-backed). Owned by Spec 5.
- n1 deployment of the Strathcarron sample portal. Operator-owned work blocked on new domain / services; happens after Spec 5.
- A new wallet-side "first credential" takeover. Sarah is a returning citizen; the new credential appears in the home-row stack without ceremony.

## References

- Locked design: `docs/superpowers/specs/2026-05-15-spec-4-credential-gated-second-service-design.md`
- Boundary doc: `docs/superpowers/specs/2026-05-15-platform-consumer-boundary-design.md`
- Umbrella: `docs/superpowers/specs/2026-05-13-strathcarron-citizen-arc.md`
- Spec 3 (F126): `specs/126-enrol-inside-wizard/`
- Spec 2 (F125): `specs/125-sorcha-wallet-user-agent/`
- Spec 1 (F124): `specs/124-assured-identity-pwa/`
