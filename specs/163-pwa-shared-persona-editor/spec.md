# Feature Specification: PWA Shared Persona/Profile Editor

**Feature Branch**: `163-pwa-shared-persona-editor`

**Created**: 2026-06-26

**Status**: Draft

**Input**: User description: "PWA persona/profile editing via a SHARED component saving to the server (backlog #1, reconciled with F157 #1037 just merged). In Sorcha.UI.Components.User create a shared Persona/Profile editor component (reuse or extract the form from F157 CompleteProfileStep) that loads via IPersonaService.GetAsync and saves via IPersonaService.UpdateAsync (PUT /api/me/persona) with inline validation + 400/409 handling. Host the SAME shared component on BOTH the web My Profile page and the PWA My Profile page (replace the Sorcha.Wallet.Pwa Pages/Profile.razor stub). Register IPersonaService (+ IPersonaClient/PersonaHttpClient) in the Sorcha.Wallet.Pwa DI — it is currently only registered in the web host, which is why PWA persona does not save. Companion-first: ONE shared component saving to the server backend, not a PWA-specific fork. Add bUnit tests for the shared editor and verify it activates under the PWA DI container."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Citizen edits and saves their profile in the mobile wallet companion (Priority: P1)

A citizen opens the "My Profile" page in the Sorcha Wallet PWA, sees their current persona details (name, date of birth, email addresses, phone numbers, postal addresses, nationalities), changes one or more of them, and saves. The change is persisted to the server-side persona store and is reflected the next time the profile loads — on the PWA **or** on the web app.

**Why this priority**: This is the core defect being fixed. Today the PWA profile page is a placeholder stub and the persona service is not wired into the PWA, so a citizen using the mobile companion **cannot save their profile at all**. Restoring this is the headline value; everything else is in service of it.

**Independent Test**: Open the PWA profile page as an enrolled citizen, edit a field (e.g. add a phone number), save, reload the page, and confirm the change persisted. Delivers a working, end-to-end profile save from the mobile companion.

**Acceptance Scenarios**:

1. **Given** an enrolled citizen with an existing persona on the PWA profile page, **When** they change their family name and save, **Then** the new family name is persisted server-side and shown on reload.
2. **Given** a newly enrolled citizen who has never saved a persona, **When** they open the PWA profile page, **Then** they see an empty editable form (not an error) and can populate and save it.
3. **Given** a citizen who saved their persona on the PWA, **When** they later open the web "My Profile" page, **Then** they see exactly the values they entered on the PWA.

---

### User Story 2 - One profile editing experience across web and mobile (Priority: P2)

The profile editor presented to the citizen is the **same** editor on both the web app and the PWA — same fields, same layout, same validation behaviour. There is no separate mobile-only or web-only variant that can drift apart over time.

**Why this priority**: "Companion-first" requires that the mobile experience is a faithful companion to the web, not a fork. A single shared editor prevents the two surfaces from diverging in fields, rules, or behaviour, which would otherwise create citizen confusion and double maintenance.

**Independent Test**: Compare the rendered field set and validation messages on the web profile page and the PWA profile page; confirm they are identical because they are produced by the same component, and that a field added once appears on both surfaces.

**Acceptance Scenarios**:

1. **Given** the shared editor renders on the web profile page, **When** the same editor renders on the PWA profile page, **Then** the available fields, field order, and validation messages are identical.
2. **Given** a future change to the persona field set, **When** the change is made once in the shared editor, **Then** both the web and PWA profile pages reflect it without per-surface edits.

---

### User Story 3 - Clear feedback when a save is rejected (Priority: P3)

When a citizen's save is rejected — because a field is invalid (e.g. a malformed email, too many entries in a list, a missing default) or because their wallet is not yet provisioned — the editor shows the citizen a clear, inline message explaining what to fix, without losing the values they already entered.

**Why this priority**: A save that silently fails or throws an opaque error is worse than no save. Inline, recoverable feedback is what makes the editor trustworthy, but it builds on the P1 save path already working.

**Independent Test**: Submit a persona that violates a validation rule and confirm an inline, field-relevant message appears and the form retains the entered data; submit when the wallet is not provisioned and confirm a distinct, understandable message appears.

**Acceptance Scenarios**:

1. **Given** a citizen enters an invalid email address, **When** they save, **Then** an inline validation message identifies the problem and the rest of their input is preserved.
2. **Given** a citizen whose wallet is not yet provisioned, **When** they save, **Then** they see a distinct message indicating the profile cannot be stored until provisioning is complete, rather than a generic failure.
3. **Given** a save that fails due to a network or server error, **When** the failure occurs, **Then** the citizen is told the save did not complete and can retry without re-entering data.

---

### Edge Cases

- **New user, no persona yet**: loading returns an empty profile (never a "not found" error); the citizen can create one from scratch.
- **List caps**: each multi-value list (emails, phones, addresses, nationalities) is capped at 5 entries; attempting to exceed the cap is prevented or rejected with a clear message.
- **Default entry**: when a multi-value list is non-empty, exactly one entry is marked as the default; if none is marked, the first is treated as default.
- **Wallet not provisioned**: the citizen can reach the editor but a save is rejected with a provisioning-specific message.
- **Offline / flaky mobile network**: a save that cannot reach the server reports failure and preserves entered data for retry.
- **Unauthenticated / expired session**: the citizen is directed to authenticate rather than seeing a silent failure.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: A citizen MUST be able to view their current saved profile (persona) from the PWA "My Profile" page, with all supported fields populated from the server-side store.
- **FR-002**: A citizen MUST be able to edit and save their profile from the PWA "My Profile" page, with the change persisted to the same server-side persona store used by the web app.
- **FR-003**: The PWA "My Profile" placeholder/stub MUST be replaced by the working profile editor.
- **FR-004**: The profile editor presented on the PWA and on the web app MUST be the **same** shared editor — one definition, hosted on both surfaces — not a PWA-specific copy.
- **FR-005**: The editor MUST support the full persona field set: given/middle/family/full name, date of birth, and the capped multi-value lists for email addresses, phone numbers, postal addresses, and nationalities, each with their label/kind/default attributes.
- **FR-006**: The editor MUST load existing profile data on open and MUST treat "no persona yet" as an empty editable form rather than an error.
- **FR-007**: The editor MUST surface validation rejections (invalid field values, exceeding list caps, default-selection rules) as inline, recoverable messages that preserve the citizen's other input.
- **FR-008**: The editor MUST surface a wallet-not-provisioned rejection as a distinct, understandable message separate from generic validation errors.
- **FR-009**: The editor MUST surface network/server failures as a clear "save did not complete" state that allows retry without data loss.
- **FR-010**: The editor MUST confirm a successful save to the citizen.
- **FR-011**: The PWA host MUST have the persona read/save capability registered so the editor is fully functional when used from the PWA (this capability is the reason PWA saves currently fail).
- **FR-012**: Saves and loads MUST be performed against the citizen's own profile in their current context, consistent with the existing web behaviour.
- **FR-013**: The shared editor MUST be covered by component-level automated tests for its load, edit, save-success, validation-rejection, and provisioning-rejection behaviours.
- **FR-014**: The shared editor MUST be verified to activate and function under the PWA's runtime configuration (i.e. its required capabilities are resolvable in the PWA host), guarding against the regression where it works on web but not on the PWA.

### Key Entities *(include if feature involves data)*

- **Persona / Profile**: the citizen's personal attributes — names, date of birth, and the capped lists of emails, phones, addresses, and nationalities (each entry carrying a label/kind and a default flag). Has a read form (as loaded, with provenance) and a write form (the plaintext attributes the citizen edits and submits).
- **Shared Profile Editor**: the single user-facing editing surface for a Persona, hosted identically on the web "My Profile" page and the PWA "My Profile" page.
- **PWA Profile Page**: the mobile-companion host that currently shows a placeholder and must instead host the shared editor.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A citizen can complete an end-to-end profile edit-and-save from the PWA (open → change a field → save → see it persist on reload) — moving from impossible today to a 100%-of-attempts success rate for valid input.
- **SC-002**: A profile saved on the PWA is visible unchanged on the web app (and vice versa) on the next load, demonstrating a single shared server-side source of truth.
- **SC-003**: The web and PWA profile editors present an identical field set and identical validation messages, verified field-by-field, with zero surface-specific divergence.
- **SC-004**: 100% of validation rejections and wallet-not-provisioned rejections produce a specific, inline, recoverable message; no save rejection presents as a silent failure or an opaque error.
- **SC-005**: Automated component tests cover the editor's load, save-success, validation-rejection, and provisioning-rejection paths, and a check confirms the editor activates under the PWA host configuration.

## Assumptions

- **Reconciliation with F157 (#1037)**: The originating backlog item referenced an "F157 CompleteProfileStep" to extract the form from. As of the just-merged #1037, the canonical existing profile editor is the web "My Profile" page rather than a standalone `CompleteProfileStep` component. This feature therefore extracts/relocates the form from the current web profile page into a shared editor; the intent ("reuse the existing form, don't rewrite it") is unchanged.
- The server-side persona read/save capability already exists and is unchanged by this feature; the gap is purely that the PWA host does not wire it up. No new server endpoints or persona fields are introduced.
- The persona field set, list caps (5 per list), default-selection rules, and validation rules are those already enforced by the existing server-side persona store; this feature surfaces them, it does not redefine them.
- "Save" semantics are full-replace of the citizen's persona (consistent with the existing behaviour), not partial/field-level patching.
- Authentication and context selection (which org/context the persona is read/written under) reuse the existing PWA and web mechanisms; this feature does not change how a citizen authenticates or selects context.
- The PWA remains a thin companion to the same backend — there is no offline-only or device-local persona store introduced by this feature; saving requires connectivity, consistent with the web behaviour.
- Component-level tests use the project's established component-testing approach; "activates under the PWA DI container" is validated as part of these tests.
