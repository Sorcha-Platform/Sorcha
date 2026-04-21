# Feature Specification: Verified Citizen v2

**Feature Branch**: `103-verified-citizen-v2`
**Created**: 2026-04-13
**Status**: Draft
**Input**: User description: "Verified Citizen v2 — combined feature covering four workstreams: open starting actions completion, reusable schema component library, address lookup providers, and the Verified Citizen v2 blueprint as the integration consumer."
**Design Spec**: [`docs/superpowers/specs/2026-04-13-verified-citizen-v2-design.md`](../../docs/superpowers/specs/2026-04-13-verified-citizen-v2-design.md)

## Background

A member of the public who wants to access a Sorcha-built government service today hits two walls. First, they cannot start a citizen-facing service unless an administrator has pre-registered them as a participant — even though the platform's design intent is that anyone can walk in, fill in their details, and become the applicant for that service. Second, every service that needs to ask the same basic identity questions (name, date of birth, email, address) reinvents its form from scratch, with no shared validation, no shared layout, no postcode lookup, and no consistency for the citizen. The headline workflow that exposes both walls is the HAIP Verified Citizen credential issuance — a citizen submits their details, a government assessor reviews and approves, and a Verifiable Credential is delivered to the citizen's external HAIP wallet for use across other services.

This feature delivers the platform plumbing to remove both walls — enabling open citizen submission, a reusable identity primitive library, and postcode-driven address autofill — and rebuilds the Verified Citizen workflow as the integration consumer that proves the plumbing works.

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Open citizen submission for a public service (Priority: P1)

A citizen lands on a Sorcha-built government service for the first time, signs up for a public account, and submits their first action of the service. The platform records them as the applicant for that service instance and lets the assigned reviewer (e.g. a government assessor) act on their submission. No administrator has to pre-register the citizen ahead of time.

**Why this priority**: This is the foundational platform capability. Without it, no citizen-facing service can be self-service, and the existing Verified Citizen workflow is broken (the bug we hit). Every other workstream in this feature consumes this capability.

**Independent Test**: Build *any* trivial blueprint with one open starting action and one reviewer action. Sign up as a public user, submit the starting action. Verify the platform records the binding and the reviewer can act on the instance. Verifiable without any of the schema component or address lookup work.

**Acceptance Scenarios**:

1. **Given** a published service with an open starting action, **When** an authenticated public user submits the first action, **Then** the platform records that user as the bound applicant for the rest of the service instance and accepts the submission.
2. **Given** a service instance where a citizen has already submitted the first action, **When** a different public user attempts to submit the same first action on the same instance, **Then** the platform rejects the second submission with a clear "this application is already in progress under another applicant" message.
3. **Given** a credential-bootstrapped service that requires the submitter to present a Verified Citizen credential, **When** a public user attempts to submit without holding the credential, **Then** the platform rejects the submission and explains which credential is missing.
4. **Given** a credential-bootstrapped service, **When** a citizen who holds the required credential submits, **Then** the platform records them as the bound applicant and accepts the submission.
5. **Given** a service designer who tries to publish a service with an open starting action whose participant is incorrectly pre-bound to a wallet, **When** they hit publish, **Then** the platform refuses publication and explains which participant must be left unbound and why.

---

### User Story 2 — Reusable identity primitive library (Priority: P2)

A service designer building a new citizen-facing service needs to ask the user for their name, date of birth, email, and postal address. Instead of writing the schema from scratch, they reference shared identity primitives by URI from the Sorcha core library. Each primitive arrives carrying its own validation rules, its own form layout, its own autofill bindings to the citizen's persona profile, and (for postcode) its own address-lookup behaviour. The new service ends up an order of magnitude shorter than it would otherwise be, and the citizen sees the same beautiful form treatment everywhere.

**Why this priority**: The substrate that makes Verified Citizen v2 a *qualitative* improvement, not just a bug fix. Once it ships, every future citizen-facing service inherits the polish for free. Without it, every blueprint reinvents the same five identity questions in subtly inconsistent ways.

**Independent Test**: Reference a single core component (e.g. `PostalAddress`) from a throwaway blueprint, render the form, verify validation, layout, and persona autofill all activate without any per-blueprint setup. Verifiable independently of the open submission and address lookup workstreams.

**Acceptance Scenarios**:

1. **Given** the core library publishes a Personal Name primitive, **When** a service designer references it in a blueprint by its URI, **Then** the rendered form shows the name fields with the layout, validation, and autofill behaviour declared by the primitive.
2. **Given** a service designer wants a date-of-birth field that cannot be in the future, **When** they reference the Date Of Birth primitive, **Then** the date picker prevents future dates and the validator rejects them on submission, with no per-blueprint configuration required.
3. **Given** a service designer wants a date-of-birth field with a different visual layout than the primitive's default, **When** they declare an override layout alongside the primitive reference, **Then** the override layout renders while the primitive's validation and autofill bindings continue to apply unchanged.
4. **Given** a citizen has saved a persona profile with their name, date of birth, email, and address, **When** they fill in a service that uses the core primitives, **Then** the form is at least 90% pre-populated from the persona profile before they touch any field.
5. **Given** the same primitive is consumed by two different services, **When** one service updates the citizen's persona-relevant data, **Then** the other service's autofill picks up the change on the next form render.

---

### User Story 3 — Postcode-driven address autofill (Priority: P3)

A citizen filling in a postal address types only their postcode. The platform either autocompletes the rest of the address from a configured address-lookup provider, or — if no full-address provider is configured — at least validates the postcode and pre-populates the town and region. If no lookup provider is available at all, the citizen falls back to plain manual entry without losing any fields.

**Why this priority**: A visible quality-of-life improvement for every citizen-facing service that asks for an address. It depends on the primitive library (so the `PostalAddress` primitive can declare its lookup intent) but is otherwise independent — different deployments will configure different providers based on data licensing.

**Independent Test**: Render any form containing the `PostalAddress` primitive, type a known UK postcode, verify the address fields populate (or at least the town/region populate when only a validate-only provider is available). Test the no-provider case by clearing config — the form must still work as plain text input.

**Acceptance Scenarios**:

1. **Given** a citizen on a form with a postal address field, **When** they type a valid UK postcode and the platform has a full-address provider configured, **Then** they are presented with a list of address candidates to pick from and the chosen address fills all sibling fields.
2. **Given** a citizen on the same form, **When** the platform only has a validate-only provider configured, **Then** typing a valid postcode pre-populates the town and region but leaves the street fields for manual entry.
3. **Given** a citizen on the same form, **When** the platform has no address-lookup provider configured, **Then** the postcode field renders as plain text and the citizen enters all address parts manually.
4. **Given** a citizen on the same form, **When** they type an invalid postcode, **Then** the field shows a clear validation error without breaking the rest of the form.
5. **Given** the address-lookup provider is temporarily unavailable, **When** the citizen types a postcode, **Then** the form gracefully falls back to manual entry without losing any data the citizen has already typed.

---

### User Story 4 — Verified Citizen v2 end-to-end (Priority: P4)

A member of the public lands on the Verified Citizen application, signs up for a public account, fills in their name, date of birth, email, and postal address (mostly autofilled from their persona profile, with the postcode looking up the rest of their address), and submits. A government assessor reviews the application against records, approves it, and the platform issues a Verified Citizen credential to the citizen's external HAIP wallet via QR code. The citizen now holds a portable, cryptographically signed credential they can use to bootstrap downstream services without re-entering their identity.

**Why this priority**: This is the integration test that proves all three platform improvements actually combine into the headline experience the platform was built for. It is also the *only* user story that delivers the end-user value (a Verifiable Credential) — the other three workstreams enable it but do not deliver it on their own.

**Independent Test**: Fresh public account on a fresh deployment. Run through the Verified Citizen application end-to-end. Verify the credential lands in an external HAIP wallet with all expected claims (given name, middle name, family name, date of birth, email, address). The test exercises every other workstream as a side effect.

**Acceptance Scenarios**:

1. **Given** a clean public account with a saved persona profile, **When** the citizen lands on the Verified Citizen application, **Then** the form is at least 90% pre-populated from the persona before any typing.
2. **Given** the citizen has filled in their details, **When** they enter their postcode, **Then** the address lookup either populates the full address or at least the town and region depending on what providers are configured.
3. **Given** the citizen has reviewed and submitted their application, **When** the government assessor opens the case, **Then** they see all submitted details and can approve or reject with notes.
4. **Given** the assessor has approved, **When** the credential is issued, **Then** the citizen receives a Verifiable Credential in their external HAIP wallet containing givenName, middleName, familyName, dateOfBirth, email, and the structured address.
5. **Given** the credential has been issued, **When** the citizen presents it to a downstream service that requires a Verified Citizen credential, **Then** the downstream service accepts it as valid identity proof and binds the citizen as that service's applicant without re-collecting their identity.

---

### Edge Cases

- **Re-binding attempts.** What happens when a second public user tries to submit Action 1 on an instance that already has a bound applicant? Rejected with a clear "already bound" message; the second user is offered the option to start a new instance instead.
- **Cold cache for instance bindings.** What happens after a service restart when an instance's binding is no longer in cache? The platform rebuilds the binding by replaying the originating action from the canonical ledger.
- **Persona profile missing fields.** What happens when the citizen's persona has some but not all of the identity fields the form needs? The autofill populates what it can; the citizen fills in the gaps; their next save updates the persona for future services.
- **Service designer overrides the layout of a referenced primitive but tries to override its validation rules.** Validation rules are component-owned and cannot be overridden at the reference site; the override is silently dropped (or surfaced as a publish-time warning, TBD by planning).
- **Layout cycle in `$ref` chains.** What happens when component A references component B which references component A? Resolver detects the cycle at resolve time and refuses to resolve, surfacing a publish-time error.
- **Invalid postcode after provider has returned candidates.** The citizen picks an address, then edits the postcode to an invalid value. The form re-validates the postcode field but does not clear the already-populated address parts; the citizen must edit them themselves.
- **Address lookup provider rate limit hit during normal use.** The form falls back to plain text for the next request and surfaces a transient warning.
- **Citizen has middle name in persona but the form schema doesn't ask for it.** The middle name field is hidden; the autofill resolver does not surface it. The persona stores it without affecting the current form.
- **Date-of-birth field must be in the past, but the citizen's persona stores a future date by data error.** The autofill populates it; the validator rejects it on submission with a clear error pointing the citizen at the date field.
- **Pre-existing walkthroughs that pre-bound the citizen wallet.** All affected walkthroughs are rewritten as part of this feature; no compatibility shim is provided.

## Requirements *(mandatory)*

### Functional Requirements

#### Open citizen submission (Workstream 1)

- **FR-001**: System MUST allow an authenticated public user with no prior participant record to submit the first action of a citizen-facing service.
- **FR-002**: System MUST record the first submitter's identity as the bound applicant for the service instance, persistently for the life of the instance.
- **FR-003**: System MUST resolve the bound applicant from the recorded binding (not from any pre-set service-definition value) when subsequent actions disclose data to or issue credentials to the applicant.
- **FR-004**: System MUST reject any attempt to re-bind the applicant role on an instance that already has a recorded binding, with a message explaining that the application is already in progress.
- **FR-005**: System MUST refuse to publish a service definition where the participant role of an open starting action has been incorrectly pre-bound to a specific wallet, with a clear publish-time error identifying the offending participant.
- **FR-006**: System MUST allow service designers to gate an open starting action on the presentation of a verifiable credential, so that only submitters holding the required credential become the bound applicant.
- **FR-007**: System MUST recover the recorded applicant binding from the canonical service-instance history if the in-memory or cached binding is unavailable, without requiring administrator intervention.
- **FR-008**: System MUST update existing walkthroughs that incorrectly pre-bind open citizen participants so that they conform to the open-submission contract.

#### Reusable identity primitives (Workstream 2)

- **FR-009**: System MUST provide a Sorcha-managed library of reusable identity primitives covering at minimum: personal name (given/middle/family/full), date of birth, single email, multi-email with default, and postal address.
- **FR-010**: System MUST allow service definitions to reference library primitives by stable URI.
- **FR-011**: System MUST resolve all primitive references to a fully-merged form before validating or rendering a service form.
- **FR-012**: System MUST cause a referenced primitive to contribute its own form layout (page/section structure, field widths) by default, with the consuming service definition able to override layout while keeping the primitive's validation and field set unchanged.
- **FR-013**: System MUST allow primitives to declare explicit autofill bindings to the citizen's persona profile, and the form renderer MUST honour those bindings when populating the form.
- **FR-014**: System MUST extend the citizen persona profile to include middle name (optional) so that the personal-name primitive can autofill it.
- **FR-015**: System MUST express past-only and future-only date constraints on date primitives via standard date constraint keywords, using a small named-token vocabulary (today, today-N years, etc.) interpretable at evaluation time.
- **FR-016**: System MUST refuse to resolve a primitive reference chain that contains a cycle, surfacing a clear error.
- **FR-017**: System MUST identify primitives by URIs that are stable across the lifetime of the platform and that can later be resolved against a register-published source without changing the URI.

#### Address lookup (Workstream 3)

- **FR-018**: System MUST provide a postcode-driven address lookup capability accessible from any form field that opts in via a primitive declaration.
- **FR-019**: System MUST support pluggable address lookup providers, with at least one default provider configured out of the box that requires no registration or licence.
- **FR-020**: System MUST support an optional full-address provider that can be enabled by configuration where licence-controlled address data is available.
- **FR-021**: System MUST select the most capable available provider for the user's country at request time, falling back to less capable providers and finally to plain text input if none are available.
- **FR-022**: System MUST gracefully degrade to plain text input when no address lookup provider is configured, without losing form state or breaking submission.
- **FR-023**: System MUST surface provider capabilities (validate-only versus full-address) so that the form renderer can adapt the lookup control's interaction model.
- **FR-024**: System MUST allow a new address lookup provider for a different country to be plugged in without modifying the form renderer or existing primitives.
- **FR-025**: System MUST handle address lookup provider failures (timeout, rate limit, unavailability) by falling back to plain text input for that submission, without breaking the form.

#### Verified Citizen v2 integration (Workstream 4)

- **FR-026**: System MUST publish a Verified Citizen v2 service definition that uses open citizen submission, the personal-name / date-of-birth / email / postal-address primitives, and postcode lookup on the address.
- **FR-027**: System MUST issue a Verified Citizen credential to the bound citizen's external HAIP wallet on assessor approval, with claims for given name, middle name, family name, date of birth, email, and the structured postal address.
- **FR-028**: System MUST allow a downstream service that requires a Verified Citizen credential to accept the v2 credential and use it to bind its own applicant role.
- **FR-029**: System MUST update the existing Verified Citizen walkthrough to use the v2 service definition end-to-end and to validate against the network in the same way the v1 walkthrough did.

### Key Entities

- **Citizen** — A member of the public who interacts with one or more Sorcha-built citizen-facing services. Identified by a wallet under the public organization. Has a persona profile that stores reusable identity attributes for autofill.
- **Verification Analyst** — A staff role within an issuing organization who reviews citizen applications and approves credential issuance.
- **Service Designer** — A human or AI assistant who authors service definitions (blueprints). May reference shared identity primitives.
- **Service Definition** — A multi-step workflow definition that declares its participants, its actions, and the data each step collects. May reference identity primitives.
- **Service Instance** — A single citizen's run through a service definition. Carries the binding from each participant role to a specific identity (the bound applicant being the most important for citizen-facing services).
- **Identity Primitive** — A reusable, URI-identified shape for a single piece of personal information (e.g. a postal address). Carries its own validation, layout, and autofill bindings.
- **Persona Profile** — A citizen's stored personal information, encrypted at rest, used to autofill forms across services.
- **Verifiable Credential** — A cryptographically signed, portable digital credential issued to a citizen's external wallet on completion of a citizen-facing service.
- **Address Lookup Provider** — An external or local service that validates postcodes and (optionally) returns full street addresses. Pluggable per deployment.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A citizen with a saved persona profile can complete the Verified Citizen application from first form view to credential receipt in **under 5 minutes** end to end.
- **SC-002**: When the form is first rendered for a citizen with a complete persona profile, **at least 90% of identity fields are pre-populated** before the citizen touches any field.
- **SC-003**: When a full-address lookup provider is configured, **at least 80% of UK postcode entries** result in a complete address from postcode alone (the remainder fall through to manual entry).
- **SC-004**: The Verified Citizen v2 service definition is **at least 60% shorter** in line count than the v1 definition, with the savings coming from the reusable primitive references rather than feature loss.
- **SC-005**: A second downstream service (driving licence, council tax, or similar) can be built reusing the identity primitives without modifying any primitive or any platform component — measured by **zero changes** to either the primitives or the platform when the downstream service ships.
- **SC-006**: A service designer who attempts to publish a service with an open participant incorrectly pre-bound receives a publish-time error within **2 seconds** that names the participant and explains how to fix it.
- **SC-007**: 100% of the existing Verified Citizen v1 acceptance scenarios continue to pass against the v2 implementation (no functional regressions).
- **SC-008**: A citizen on a deployment with **no address lookup provider configured** can still complete the postal address field manually with no degraded form behaviour beyond the absence of autofill.
- **SC-009**: A second citizen attempting to submit Action 1 on an instance another citizen has already started receives a clear rejection within **2 seconds** explaining that the application is already in progress.
- **SC-010**: A citizen-facing service that gates submission on holding a Verified Citizen credential rejects non-holders within **5 seconds** of submission attempt, with the rejection identifying the missing credential.

## Assumptions

The following defaults were chosen during specification authoring rather than asked as clarifications:

- **Identity floor**: a citizen using a citizen-facing service is already authenticated to a public-org account (email/password or social login). Fully anonymous service entry is out of scope for this feature; the public-org account flow is the floor.
- **Geographic scope**: UK is the initial focus for postcode lookup. The provider abstraction is designed multi-country but no non-UK providers ship with this feature.
- **Deployment of a free default address provider**: the free, no-key default provider returns postcode validation and town/region metadata only (it does NOT return full street addresses). Full street address lookup requires a separately licensed provider and is opt-in via configuration.
- **Address lookup endpoint authentication**: the address lookup endpoint is auth-gated to public-org users (anyone reaching the form is already authenticated). It is rate-limited per user.
- **Walkthrough fix scope**: both `HaipVerifiedCitizen` and `HaipDrivingLicence` walkthroughs are fixed to remove the open-participant pre-bind anti-pattern. No backwards compatibility shim is provided for any other walkthrough that may share the same pattern; they are fixed as encountered.
- **Persona model migration**: adding `middleName` to the persona profile is non-destructive (existing personas continue to work; middleName is null until the citizen sets it).
- **Credential format**: Verifiable Credentials are issued as SD-JWT VC via the OpenID4VCI pre-authorized code flow to an external HAIP wallet. The HAIP issuance pipeline is already in place as of prior platform work (Feature 098).
- **Component versioning**: primitives ship at version 1. A migration story for v2+ of any primitive is out of scope for this feature.
- **Library publication mechanism**: identity primitives are file-based for this feature. Register publication (so that a primitive's URI resolves against a register-served source) is out of scope and reserved for a future feature; the URI format is chosen to be forward-compatible.
- **Cycle detection in primitive references**: the resolver detects cycles at resolve time and surfaces an error; design-time linting is not provided in this feature.

## Out of Scope

The following are deliberately deferred:

- Register publication of identity primitives (file-based only for this feature; URI format is forward-compatible).
- Non-UK postcode/address lookup providers.
- Nationality, gender, marital status primitives (only the headline five — name, DOB, email, multi-email, postal address — are in this feature).
- Field-subset derivation of primitives (deriving a "name without middle name" from the personal name primitive).
- Fully anonymous service entry (no public-org account).
- Email-based account claim flow (claim-by-magic-link).
- A blueprint-author UI for editing primitives in-app.
- Migration tooling for component versioning beyond v1.
- Backwards compatibility shims for walkthroughs that pre-bound open participants.
- A generalised "form designer" workflow for non-blueprint authors.
- Verifiable Presentations of the Verified Citizen credential to non-Sorcha consumers (W3C VP1.0/VP2.0 verifier outside the platform).

## Phase Mapping (informative)

This single feature is delivered in **four sequential phases**, each shipping as its own pull request. Phases 1-3 are independently shippable; Phase 4 integrates them.

| Phase | Workstream | User Story | Independently Shippable? |
|---|---|---|---|
| 1 | Open starting actions | US-1 | Yes — fixes the bug and unlocks any open-submission service |
| 2 | Reusable identity primitives | US-2 | Yes — the primitive library can land before any consumer adopts it |
| 3 | Address lookup providers | US-3 | Yes — the provider abstraction works without the v2 blueprint, gated on Phase 2 only because the `PostalAddress` primitive declares the lookup intent |
| 4 | Verified Citizen v2 blueprint + walkthrough | US-4 | No — depends on phases 1, 2, and 3 |

Detailed implementation routes for each phase will be produced by `/speckit.plan` after this specification is reviewed.
