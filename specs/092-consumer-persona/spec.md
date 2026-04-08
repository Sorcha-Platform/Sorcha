# Feature Specification: Consumer Persona and Nav Tidy

**Feature Branch**: `092-consumer-persona`
**Created**: 2026-04-08
**Status**: Draft
**Input**: User description: "Consumer Persona and Nav Tidy — per-user My Profile page with self-asserted identity attributes (name, date of birth, emails, phones, addresses, nationalities — multi-value with defaults), encrypted persona storage, form autofill with clear provenance, and a small navigation tidy-up."

Source design document: `docs/superpowers/specs/2026-04-08-consumer-persona-and-nav-tidy-design.md`

---

## Clarifications

### Session 2026-04-08

- Q: Should multi-value attribute lists (emails, phones, addresses, nationalities) have a hard cap per user? → A: 5 entries per list
- Q: What happens to a user's persona when their platform user account is deleted? → A: Cascade delete — persona is hard-deleted atomically with the account
- Q: How should a form render while the persona is still loading? → A: Render immediately; apply fills when persona arrives, but skip any field the user has already started typing in (user activity wins)
- Q: How is persona provenance communicated to screen-reader users? → A: Each autofilled field carries an accessible label announcing "filled from your profile"; the visible summary has an equivalent accessible version
- Q: What is the target latency for persona autofill to appear after a cold form load? → A: 500ms — cold-load target; warm cache hits are effectively instant

---

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Fill a healthcare disclosure form in seconds (Priority: P1)

A consumer has set up their profile once with their name, date of birth, home address, mobile phone number, and personal email. They now need to submit a healthcare disclosure on the Sorcha platform. When they open the form, every field the system recognises — full name, date of birth, address, phone — is already filled from their profile, visibly distinguished from fields they'll still need to type. The reason for visit is the only field requiring attention. They review, submit, and are done in under a minute.

**Why this priority**: This is the core value of the feature. Without autofill, every form is a fresh typing exercise and the persona doesn't earn its keep. The entire rest of the feature exists to make this flow trustworthy and safe. Without P1, nothing else matters.

**Independent Test**: A user who has saved a persona can open any blueprint action form containing recognised identity fields and observe those fields prefilled with clear visual provenance. Can be demonstrated end-to-end on the existing healthcare walkthrough.

**Acceptance Scenarios**:

1. **Given** a user has saved a persona with name, date of birth, address, email, and phone, **When** they open a blueprint action form whose schema contains fields for those attributes, **Then** the matching fields are prefilled with the persona values and rendered with a visually distinct style plus a "self" provenance tick.
2. **Given** an autofilled field, **When** the user edits it, **Then** the distinct style disappears, the provenance tick is removed, and the field is treated as user-entered on submission — even if they retype the exact same value.
3. **Given** an autofilled form, **When** the user clicks "Clear all" in the autofill summary, **Then** every field that was autofilled is cleared, and fields the user already edited are left untouched.
4. **Given** a form with a field whose schema declares a default value, **When** the persona has a matching attribute, **Then** the schema default wins and the persona is not applied to that field.
5. **Given** a user with three saved email addresses, one marked as default, **When** a form asks for an email, **Then** the default email is the one that is filled.

---

### User Story 2 — Manage my personal profile (Priority: P1)

A consumer reaches their profile from the avatar menu in the top-right corner. They see a single "My Profile" page listing their identity attributes grouped into Identity (name, date of birth, nationality) and Contact (emails, phones, addresses). They can add a second email labelled "Work", mark either email as the default, remove an old address, and save. Their changes are encrypted and persisted immediately, and the next form they open reflects the new values.

**Why this priority**: Autofill is only useful if the user can create and curate the source of truth. Equal priority with Story 1 — neither is useful without the other. "My Profile" is also the only discoverable surface for the global autofill toggle, so it must exist in v1.

**Independent Test**: A user can open `/profile`, add attributes to each category, mark defaults, save, and reload the page to see the canonical persisted state.

**Acceptance Scenarios**:

1. **Given** a user has just signed in and has no persona, **When** they click the avatar menu and select "My Profile", **Then** they land on an empty profile page with Identity and Contact sections ready to fill.
2. **Given** the user fills in their name and date of birth and adds one email marked as default, **When** they click Save, **Then** the values are persisted and a success confirmation is shown.
3. **Given** the user already has two emails with the first marked default, **When** they mark the second as default and save, **Then** the second becomes the default and the first is still present but no longer marked.
4. **Given** the user adds an email in an invalid format, **When** they click Save, **Then** the invalid field is flagged inline and no data is written.
5. **Given** the user clicks the "Autofill forms from my profile" toggle to off and saves, **When** they open a form with matching fields, **Then** the fields are NOT automatically filled, but a "Fill from profile" button is visible at the top of the form.

---

### User Story 3 — Know what I'm disclosing before I submit (Priority: P1)

Before submitting any form that contains autofilled fields, the user sees a compact one-line summary at the top of the form — "4 fields filled from your profile" — with a Review action that lists exactly which fields were filled from which attributes. This lets them confirm what is about to be disclosed to the recipient, and makes the cream-tinted fields explicable rather than surprising.

**Why this priority**: The DAD model requires that disclosure is an intentional act. Without a clear summary and review, autofill becomes invisible to the user and undermines trust. Grouped with Stories 1 and 2 as P1 because it is the feature's honesty mechanism — the other two are meaningless without it.

**Independent Test**: A user can fill a form with partially matching fields, open the Review popover, and see a tabular list of field paths, attribute names, and values that were autofilled, with a per-row clear action.

**Acceptance Scenarios**:

1. **Given** a form with four autofilled fields, **When** the page loads, **Then** a summary line above the form reads "4 fields filled from your profile" with "Review" and "Clear all" actions.
2. **Given** the summary is visible, **When** the user clicks Review, **Then** a compact popover opens listing each autofilled field name, the attribute it came from, and the current value.
3. **Given** the Review popover is open, **When** the user clicks the clear action on a single row, **Then** only that field is cleared and the summary line decrements.
4. **Given** a form with no matching fields or a persona with no saved attributes, **When** the page loads, **Then** no summary line is shown and no autofill button or banner is rendered.

---

### User Story 4 — Find what I need without nav clutter (Priority: P2)

A returning user notices that the side navigation is no longer labelled with a redundant "Navigation" header, and that the Settings and Notifications entries have been removed from the side nav — they instead find Settings (which now includes notification preferences as a tab) behind the profile icon, alongside the new "My Profile" item. The result is a cleaner left-hand drawer focused on *what the user is working on*, with personal settings grouped behind the avatar where they expected them.

**Why this priority**: The design spec and the user explicitly requested this tidy-up alongside the persona work. It is not blocking for the core value, but it is part of the same consumer-experience improvement and should not be split across releases. P2 because it is a polish/refactor — the core persona value ships without it.

**Independent Test**: A user can navigate the site and confirm the drawer shows no "Navigation" header, no Settings or Notifications entries in the side nav, and that the avatar menu contains both "My Profile" and "Settings".

**Acceptance Scenarios**:

1. **Given** the user has signed in, **When** they open the side drawer, **Then** there is no "Navigation" header text above the link list.
2. **Given** the user scans the side drawer, **When** they look for Settings, **Then** they do not find it in the side nav.
3. **Given** the user opens the avatar menu, **When** they look at the menu items, **Then** they see "My Profile" above "Settings" and both navigate to the respective pages.
4. **Given** the user opens Settings, **When** they look at the tabs, **Then** they find a "Notifications" tab that contains the same notification preference controls that used to live on a separate page.
5. **Given** a user has an old bookmark for the separate notifications settings page, **When** they open it, **Then** they are taken to the Notifications tab of the unified Settings page.

---

### User Story 5 — Turn off automatic fill for privacy (Priority: P3)

A privacy-conscious user wants to keep their profile saved for convenience but prefers to consciously decide each time whether to use it. They open My Profile, flip the "Autofill forms from my profile" switch off, and save. From then on, forms load empty, but a clear "Fill from profile" button at the top of any form with matching fields lets them opt into autofill on demand with a single click.

**Why this priority**: The default behaviour (on) covers the majority; this is a minority preference that nonetheless matters for trust. P3 because the flow is operable without it for the consent-first majority — though the toggle itself should ship in v1 since retrofitting a default behaviour is harder than shipping the switch now.

**Independent Test**: A user flips the autofill toggle off, opens a form with matching fields, and sees no automatic fill but a functional "Fill from profile" button.

**Acceptance Scenarios**:

1. **Given** the user has toggled autofill off and saved, **When** they open a form with matching fields, **Then** the fields are empty and a "Fill from profile" button is visible above the form body.
2. **Given** the Fill from profile button is visible, **When** the user clicks it, **Then** the matching fields are filled with the same visual styling and provenance ticks as the automatic path.
3. **Given** the user later toggles autofill back on, **When** they open a new form, **Then** autofill is automatic again without needing to click the button.

---

### Edge Cases

- **New user with no persona**: The form renderer must render cleanly with no summary line, no autofill button, and no errors logged.
- **User without a provisioned wallet**: Reads of the persona succeed with an empty state; writes are blocked with a clear error that explains a wallet must be provisioned first. A user without a wallet cannot save a persona.
- **Corrupt or undecryptable stored persona**: The user sees a clear "Couldn't load your profile — please contact support" message. The form still renders fully functional without autofill so the user is not blocked from submitting.
- **Tenant service or wallet service temporarily unavailable**: The form renders without autofill and a non-blocking notice indicates the profile is unavailable. The user can still fill in and submit the form manually.
- **Form field named `email` that is not the user's email** (for example, "Emergency contact email"): The blueprint author must be able to tag the field as non-persona to prevent accidental autofill. Without tagging, conservative inference must not be so eager that it autofills a field that is clearly someone else's.
- **User with three emails editing the default**: Changing the default does not delete the other entries; they remain and can be promoted later.
- **User edits an autofilled field then retypes the exact persona value**: The field is still treated as user-entered. Provenance is a statement about who typed, not what was typed.
- **User clicks "Clear all" after editing two of four autofilled fields**: Only the two still claimed as autofilled are cleared. The user's edits are preserved.
- **Form submission after "Clear all" with a mix of cleared and user-typed fields**: The form validates normally; cleared fields behave the same as fields the user never filled.
- **Multi-value list where the user removes the only default**: The system must ensure a default exists if any entry exists. The service promotes another entry automatically rather than leaving the list in an invalid state.
- **Schema field with an explicit default that also matches a persona attribute**: The schema default wins; the persona is not applied to that field.
- **A form field whose schema declares `x-persona: false`**: The field must never be autofilled by any rule, including inference.
- **Existing blueprints with no `x-persona` extensions**: Must benefit from conservative inference on obvious cases (email, phone, standard date-of-birth field names) without any blueprint author action.
- **Autofilled address field in a form that has a "Different shipping address?" toggle**: The autofill applies to the first matching address; the user can override it as normal. No special handling required in v1.

---

## Requirements *(mandatory)*

### Functional Requirements

**Persona data and storage**

- **FR-001**: Each user MUST have at most one personal profile ("persona") containing self-asserted identity attributes: given name, family name, full name, date of birth, zero-or-more email addresses, zero-or-more phone numbers, zero-or-more postal addresses, zero-or-more nationalities.
- **FR-002**: For each multi-value attribute list (emails, phones, addresses, nationalities), when the list is non-empty exactly one entry MUST be marked as the default.
- **FR-002a**: Each multi-value attribute list MUST be capped at 5 entries per user. Write operations that would push any list above 5 entries MUST be rejected with a clear error naming the offending list.
- **FR-003**: The persona MUST be stored as encrypted data, and the encryption key MUST NOT be co-located with the ciphertext in the same service.
- **FR-004**: The encryption key MUST be derived per user from the user's existing wallet key material under a dedicated derivation purpose, distinct from all other derivation purposes in use on the platform.
- **FR-005**: When a user has not yet provisioned a wallet, reading the persona MUST return an empty persona without error; writing the persona MUST fail with a clear, recoverable error message explaining that a wallet is required.
- **FR-006**: The persona MUST be retrievable, replaceable as a whole, updatable one attribute at a time without affecting unchanged attributes, and deletable by the owning user. Delete MUST be idempotent.
- **FR-007**: Every write operation on the persona MUST produce an entry in the user's activity log. Reads MUST NOT be logged.
- **FR-007a**: When a platform user account is deleted, the user's persona MUST be hard-deleted atomically as part of the same operation. No orphaned persona data may remain after an account delete completes successfully.
- **FR-008**: The read-side representation of every attribute MUST carry provenance metadata indicating whether the value is self-asserted or backed by a verified credential. In v1, all values are self-asserted, but the representation MUST support the future verified-credential case without any structural change.

**Autofill behaviour**

- **FR-009**: When a blueprint action form is rendered, the system MUST identify fields whose schema matches a persona attribute and offer those values for autofill.
- **FR-009a**: The form MUST be rendered and interactive immediately on load without waiting for the persona to be fetched. When the persona arrives, autofill MUST be applied to eligible fields — except any field the user has already started typing in (including fields that currently have focus or that already contain any user-entered value), which MUST be left untouched.
- **FR-010**: Matching MUST prefer an explicit per-field schema extension (`x-persona`) declared by the blueprint author over any inference.
- **FR-011**: An explicit schema extension value of `false` MUST prevent a field from being autofilled by any mechanism, including inference.
- **FR-012**: Where no explicit extension is present, the system MUST apply a conservative inference allowlist limited to unambiguous cases (e.g. email format, telephone format, standard field names for date of birth, and recognised postal-address types).
- **FR-013**: If a field already has a default declared in its schema, the schema default MUST win over any persona match.
- **FR-014**: For any multi-value attribute, the default entry MUST be the one used for autofill in v1.
- **FR-015**: The user MUST have a single global preference ("Autofill forms from my profile") that controls whether matching fields are filled automatically on form load. The preference MUST default to on for users who have not set it.
- **FR-016**: When the preference is off and a form contains matching fields, a "Fill from profile" action MUST be visible on the form that applies the same fill logic on demand.

**Visual provenance and control**

- **FR-017**: Every field that was filled from the persona MUST be rendered with a visual style that is clearly distinguishable from user-entered fields, in both light and dark themes.
- **FR-018**: Every autofilled field MUST carry a visible "self" provenance tick.
- **FR-018a**: Every autofilled field MUST expose an accessible description announcing that the value was filled from the user's profile, so that screen-reader users learn the provenance of each field as they navigate to it. The summary line above the form MUST also be exposed to assistive technology with equivalent wording (count of fields filled, available actions).
- **FR-019**: The visual style and provenance tick MUST be removed from a field the moment the user edits it, even if the edited value is identical to the persona value.
- **FR-020**: When any fields in a form have been autofilled, a compact summary MUST be rendered above the form indicating the count of fields filled from the user's profile and offering Review and Clear all actions.
- **FR-021**: The Review action MUST show a list of field name, attribute name, and current value for every field still marked as autofilled, with a per-row action to clear that single field.
- **FR-022**: The Clear all action MUST clear every field still marked as autofilled, and MUST NOT affect fields the user has edited.
- **FR-023**: Any field previously filled from the persona that is subsequently cleared, edited, or cleared-via-Review MUST NOT carry persona provenance on submission.

**"My Profile" page**

- **FR-024**: A new "My Profile" page MUST exist and MUST be reachable only from the user profile menu on the top bar. It MUST NOT appear in the side navigation.
- **FR-025**: The profile menu MUST contain a "My Profile" entry positioned above the existing Settings entry.
- **FR-026**: The My Profile page MUST allow users to create, edit, and delete each identity attribute, including adding and removing entries in multi-value lists and promoting any entry to default.
- **FR-027**: The global autofill toggle MUST be visible and editable on the My Profile page.
- **FR-028**: Saving the profile MUST validate that at most one default exists per multi-value list, reject malformed email and phone values with inline errors, and only persist a consistent state.

**Navigation tidy**

- **FR-029**: The "Navigation" header text at the top of the side drawer MUST be removed.
- **FR-030**: The "Settings" and "Notifications" entries MUST be removed from the side navigation.
- **FR-031**: The Settings page MUST contain a "Notifications" tab whose content is the notification-preference UI that previously lived on a separate notifications settings page.
- **FR-032**: An old bookmark or deep link to the previous notifications settings page MUST navigate the user to the Notifications tab inside the unified Settings page (either directly or via a redirect).
- **FR-033**: The activity-log icon in the top bar — which opens the in-app activity log popover — MUST remain unchanged. It is not the same feature as notification preferences and must not be conflated with them.

**Stability and forward compatibility**

- **FR-034**: The read-side persona contract MUST accept an "acting as" parameter that, in v1, only permits the value "self". Any other value MUST be rejected with a clear error. This reserves the surface for future power-of-attorney and delegation use cases without a breaking change.
- **FR-035**: The persona contract MUST NOT depend on where decryption happens; a later move of decryption from server-side to client-side MUST be possible without changing how applications consume persona data or how forms integrate autofill.

### Key Entities *(include if feature involves data)*

- **Persona**: A per-user collection of self-asserted identity attributes that the user consents to reuse across forms. Attached to the person (the platform-level user account), not to any particular organization or wallet. Encrypted at rest.
- **Identity attribute**: A single named value with provenance metadata. In v1 all values are self-asserted; in future they may be backed by a verifiable credential. Examples: given name, date of birth, default email.
- **Multi-value attribute entry**: An element in a list attribute (email, phone, address, nationality) carrying its value, an optional human label (e.g. "Work", "Home"), and a flag indicating whether it is the default used for autofill.
- **Autofill preference**: A per-user setting controlling whether matching form fields are filled automatically on form load. Default: on.
- **Persona fill result**: A derived, form-scoped record linking a specific form field path to the persona attribute that filled it and the provenance of that value. Exists only during form rendering, never persisted.
- **Schema persona tag**: An author-declared schema extension on a form field indicating either the specific persona attribute that should fill it, or that the field must never be filled from the persona.
- **User profile menu item**: The top-bar entry point to personal user features, gaining a new "My Profile" entry in this feature.

---

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A user who has saved a persona can open a representative consumer form (e.g. the healthcare disclosure walkthrough) and submit it without typing any identity field, in under 60 seconds from first landing on the form. The existing baseline — typing all fields — is the comparison.
- **SC-002**: For a representative set of existing blueprint action forms (at least five drawn from active walkthroughs including healthcare, construction permit, and self-build house), the autofill identifies the correct target fields for every universally-applicable identity attribute the user has saved, with no false-positive matches into fields that do not belong to the user.
- **SC-003**: A first-time user can locate and open their profile page from the top-bar profile menu in a single click, without referring to documentation. "My Profile" is discoverable.
- **SC-004**: A user can complete a full profile setup (name, date of birth, one email, one phone, one address) in under 3 minutes on a first visit to the profile page.
- **SC-005**: Every field that the system filled from the persona is visually distinguishable from user-entered fields in both light and dark themes, verifiable by observation with no specialised tooling. Users in informal testing can identify which fields were autofilled with 100% accuracy. Screen-reader users can identify the same fields by navigating the form and hearing the per-field provenance announcement, with no visual inspection required.
- **SC-006**: When a user edits an autofilled field, the visual style and provenance tick are removed immediately with no perceptible delay.
- **SC-006a**: On a cold form load (no session cache), persona autofill MUST be applied to eligible fields within 500ms of the form becoming interactive, measured at the 95th percentile across representative consumer forms. On a warm load (persona already cached in the session), autofill MUST be applied before or within the first render frame, with no visible "fields getting filled" moment.
- **SC-007**: Side navigation contains no "Navigation" header, no Settings link, and no Notifications link. Confirmed by inspection of the rendered UI.
- **SC-008**: Notification preferences are reachable via Settings → Notifications tab, and the previous notifications settings URL still resolves to equivalent content.
- **SC-009**: A user who turns off the global autofill preference sees no automatic fill on subsequent forms and, when a form contains matching fields, always has access to an explicit "Fill from profile" action that fills every matching field in one click.
- **SC-010**: No existing blueprint requires modification to gain at least partial autofill value. Conservative inference must produce at least one correct match on every tested form that contains an email or phone field.

---

## Assumptions

- **Wallet custody**: Users have a platform-managed wallet provisioned as part of their signup flow (matching current behaviour). Self-custody modes are out of scope for this feature; they are tracked separately and do not affect the persona contract.
- **Schema format**: Blueprint action schemas already support author-declared field extensions (precedent set by file-attachment fields). The new `x-persona` tag follows that same established convention.
- **Encryption primitive**: The platform's existing authenticated encryption primitive used for encrypted payloads is reused for the persona. No new cryptographic primitives are introduced for this feature.
- **Activity log**: The platform's existing activity-log service is sufficient to record persona write events. No new log channel is introduced.
- **Migration hygiene**: The product is pre-release. Database schema changes are folded into the existing initial-setup migration for the affected service rather than accumulating new incremental migrations.
- **Locale**: v1 targets the platform's current primary locales. Address field validation is limited to universally applicable checks (country code validity, postal code presence) — not country-specific format validation.
- **Verified credentials**: No verifiable-credential-backed attributes exist in v1. The representation supports them, but the ecosystem is not yet mature enough to produce them. Tracked as an explicit follow-up.
- **Delegation / power of attorney**: No cross-user delegation exists in v1. The read contract carries an "acting as" parameter reserved for this, restricted to "self". Tracked as an explicit follow-up.
- **Per-form override**: v1 uses a single global autofill preference. A per-form override (blueprint-author or user-level) is tracked as an explicit follow-up.

---

## Dependencies

- **Platform user identity**: This feature attaches data to the platform-level user account. Requires the platform-user identity model to be stable (already delivered).
- **Wallet provisioning**: Users must have a provisioned wallet before they can save a persona. Users without a wallet see read-only empty state; writes are blocked with a clear recoverable error.
- **Activity log**: Writes to the persona are audited through the existing activity log. Requires the activity log service to be operational.
- **Blueprint form rendering pipeline**: The form renderer used by consumer forms is the integration point for autofill. Requires it to own the rendering lifecycle for the fields being autofilled.

---

## Out of scope (tracked as follow-ups)

These items are explicitly excluded from v1 and must not be addressed as part of this feature's delivery. Each is tracked separately.

- Verifiable-credential-backed persona attributes (blue "verified" tick and issuer attribution).
- Cross-user delegation (power of attorney) flows where a delegate fills a form using the principal's persona.
- A "filling on behalf of" banner for delegated form-filling.
- Migration of persona decryption from server-side to client-side (zero-knowledge) as part of self-custody mode.
- Per-form autofill override, either via a blueprint-author schema flag or a user-level per-form toggle.
- Per-form alternate-value picker for multi-value attributes (e.g. "use my work email for this form").
- A freeform "remembered answers" key/value bag beyond the typed identity essentials, for jurisdiction-specific fields the user has typed before.
