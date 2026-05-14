# Feature Specification: Sorcha Wallet (Full User-Agent v1)

**Feature Branch**: `125-sorcha-wallet-user-agent`
**Created**: 2026-05-14
**Status**: Draft
**Input**: User description: "Spec 2 of the Strathcarron citizen arc — 'Sorcha Wallet (Full User-Agent v1)'. Renames the Citizen Wallet PWA to Sorcha Wallet, grows it from credentials-only to a full end-user agent (hold/present credentials, doorstep verification, application submission from phone with persona + camera, devices/auth management, transaction history, multi-context UI). Managed-mode v1 default; self-custody opt-in deferred to v2. Three headline demo beats: doorstep verification, application-from-phone, context switching. Closes #700 Phase 2."

**Authoritative design**: [`docs/superpowers/specs/2026-05-14-spec-2-sorcha-wallet-user-agent-design.md`](../../docs/superpowers/specs/2026-05-14-spec-2-sorcha-wallet-user-agent-design.md)
**Umbrella context**: [`docs/superpowers/specs/2026-05-13-strathcarron-citizen-arc.md`](../../docs/superpowers/specs/2026-05-13-strathcarron-citizen-arc.md)
**Predecessor spec**: Feature 124 (Spec 1 of the citizen arc) — shipped 2026-05-14 as `spec-124-complete`

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Margaret verifies the engineer at her door (Priority: P1)

Margaret is an elderly resident. A man arrives at her door claiming to be a Caledonian Water engineer come to check her stopcock. He's wearing a uniform but Margaret can't be sure he's genuine. She opens her Sorcha Wallet on her phone, taps the **Verify a credential** button, and shows him her phone. He responds by tapping his employer-issued credential to her phone (NFC) or holding up a QR code from his own wallet. Margaret's phone shows a green panel: *"Liam Buchanan — Water Engineer, Caledonian Water. Credential valid until November 2027. Identity confirmed."* She lets him in.

**Why this priority**: Inverts the credential conversation in a way no demo has done. Sorcha goes from "manages my stuff" to "protects vulnerable people from doorstep scams." Strongest "Sorcha matters" story for public-sector audiences. Genuinely differentiating capability.

**Independent Test**: A presenter can demonstrate this in isolation — start from a wallet with no credentials of its own, scan a generated test QR or NFC tag carrying a known credential, observe the trust panel render the correct verdict. Doesn't require the citizen to hold any credentials, doesn't require submission flows, doesn't require multiple contexts.

**Acceptance Scenarios**:

1. **Given** Margaret has her wallet installed and is signed in, **When** she taps Verify and scans a valid credential, **Then** the wallet shows a green trust panel within 5 seconds confirming the holder's identity, the issuing organisation, and the validity period.
2. **Given** Margaret scans a credential whose status-list entry indicates revocation, **When** the wallet completes the verification, **Then** the panel shows red and clearly states *"This credential has been revoked. Do not let this person in until they can prove their identity another way."*
3. **Given** Margaret scans a credential whose issuer signature cannot be verified, **When** the wallet completes the verification, **Then** the panel expands and explains in plain English what could not be checked and what Margaret should do (e.g. try again, call the company, decline access).
4. **Given** Margaret is offline (no internet), **When** she scans a credential, **Then** the wallet either completes verification using cached issuer keys (if available) or surfaces a clear "couldn't reach the registry" message with safe-default advice.

---

### User Story 2 — Sarah submits her council application from her phone (Priority: P1)

Sarah has just enrolled her wallet (the Spec 1 / Feature 124 flow) and holds an Assured Identity credential. She wants to apply for a driving licence. She opens the wallet, sees a Needs-attention prompt *"Start a Driving Licence application — uses your verified identity automatically."* She taps. A five-page form opens directly in the wallet. Her name, date of birth, and address auto-fill from her stored profile. Page 4 asks for a portrait — she taps the camera button, takes a selfie, the wallet resizes and embeds it. Page 5 reviews her details as a draft credential card. She taps Submit. The wallet's Home now shows *"Driving Licence application in review"* in the Needs-attention band.

**Why this priority**: Proves the wallet can replace the web form for citizens who prefer their phone. Tightest follow-on to Spec 1's headline (the first-credential takeover). Exercises three major capability paths in one flow — form rendering, portrait capture, persona autofill — and ends with the wallet showing the in-progress state it already knows how to render.

**Independent Test**: A presenter can demonstrate this in isolation — open a wallet that holds an Assured Identity, tap into the application surface, complete the form, take a selfie, submit. The credential issuance that follows is exercised by Feature 124's existing flow; this story ends at submission.

**Acceptance Scenarios**:

1. **Given** Sarah has an Assured Identity in her wallet, **When** she opens the wallet, **Then** Home surfaces relevant follow-on applications (driving licence, blue badge, etc.) she's eligible for.
2. **Given** Sarah taps a recommended application, **When** the form opens, **Then** the first three pages have her name, date of birth, and address pre-filled from her profile, clearly marked as auto-filled and easy to edit.
3. **Given** Sarah reaches a page requesting a portrait, **When** she taps the camera button, **Then** the device camera opens full-screen with a clear retake button, the captured image is resized client-side, and the resulting token is embedded into the form data.
4. **Given** Sarah completes all required pages, **When** she taps Submit on the review page, **Then** the application is sent to the issuing organisation and the wallet's Home immediately shows the in-progress state.
5. **Given** the device camera permission is denied, **When** Sarah reaches the portrait page, **Then** the wallet explains why the camera is needed and offers a fallback (skip with reduced credential, scan with a friend's phone, or grant permission via browser settings).
6. **Given** Sarah's session expires mid-submission, **When** she taps Submit, **Then** the wallet prompts re-authentication and resumes the submission exactly where she left off without losing the form data.

---

### User Story 3 — Ben switches between personal and work contexts (Priority: P1)

Ben is a construction worker with two organisational memberships: Personal (his own citizen identity) and Caledonian Builders Ltd (his employer). In the morning at home he opens the wallet, sees his Personal Home — his Assured Identity, recent presentation history at the leisure centre, and a council application in progress. On site at midday he taps the context chip at the top of his screen and switches to Caledonian Builders Ltd. His Home content swaps immediately: his site-safety certification appears in Credentials, a "Submit incident report" application surfaces in the appropriate band, his persona autofill now uses his work email and the office address. At the end of the day, when he presents site evidence to his foreman, he's clearly acting as the work-context Ben — the trust display shows the correct issuing organisation.

**Why this priority**: Establishes that the wallet is one wallet for the whole person — personal identity and any number of organisational roles. Sets up Spec 4 (credential-gated second service) and Spec 5 (third-party verifiers) by making it natural to think about Sarah-personal vs Sarah-at-work. Resolves the long-standing question about whether a single citizen could ever be plausibly served by the wallet as anything other than a private individual.

**Independent Test**: A presenter can demonstrate this with a single test account that holds memberships in two organisations and at least one credential in each. Switch the context, observe content swap; switch back, observe revert. Doesn't require doorstep verification or form submission.

**Acceptance Scenarios**:

1. **Given** Ben has memberships in two organisations and credentials in each, **When** he opens the wallet, **Then** Home shows the active context name prominently, and all visible content (credentials, recent activity, suggested actions, persona) reflects that context only.
2. **Given** Ben has content in another context, **When** Home renders, **Then** a quiet but visible "peek" indicator near the bottom of the page tells him that other contexts exist with content (e.g., *"+ 2 credentials in Caledonian Builders Ltd"*), so other contexts never feel hidden.
3. **Given** Ben taps the active context chip, **When** the context switcher opens, **Then** all his organisational memberships are listed with their content summaries, the current one is clearly indicated, and tapping any other switches immediately.
4. **Given** Ben switches context mid-task (e.g. with a presentation in progress), **When** the switch is confirmed, **Then** any in-flight signing operation is cancelled gracefully and Ben is prompted to retry the action under the new context.
5. **Given** Ben has only one membership (e.g. Personal), **When** he opens the wallet, **Then** the context chip is still visible to set the user mental model but is not interactive; the peek indicator is hidden.

---

### User Story 4 — Sarah reviews her credential and presentation history (Priority: P2)

Sarah has used her wallet several times. She wants to see what's happened — when each credential was issued, when she's presented credentials and to whom, whether any are about to expire. She taps the Activity entry in the wallet's footer navigation. A time-ordered feed shows every event: *"Assured Identity issued by Strathcarron Council — 2 weeks ago"*, *"Presented Assured Identity to Caledonian Water — 3 days ago"*, *"Driving Licence application submitted — yesterday"*. Each entry can be tapped for full detail, including the cryptographic receipt and verification trust display.

**Why this priority**: Trust-building for the wallet user. Without history, the wallet feels like a black box. With it, citizens can audit what they've done with their credentials. Mature wallets in other ecosystems (Apple Wallet, Google Wallet) treat this as table stakes.

**Independent Test**: A presenter can demonstrate this against a wallet that has at least one issuance and one presentation in its history. Tap Activity, observe the feed, tap an entry, observe the detail drawer with receipt + trust display.

**Acceptance Scenarios**:

1. **Given** Sarah has at least one credential and has presented it at least once, **When** she opens Activity, **Then** all events appear in reverse chronological order with clear icons, summaries, and timestamps.
2. **Given** Sarah taps a specific event, **When** the detail drawer opens, **Then** it shows the full transaction metadata, the cryptographic receipt (if one was generated), and the lifecycle status (pending / sealed / receipted).
3. **Given** Sarah switches to a different context, **When** she opens Activity in the new context, **Then** only events from that context are visible.

---

### User Story 5 — Sarah manages her devices and recovery methods (Priority: P2)

Sarah has enrolled her wallet on her phone. Her partner asks if she can put it on her tablet too. She opens Settings on her phone, taps Devices, sees her phone listed. She follows the prompt to enrol a second device on the tablet (signs in, runs the enrolment ceremony, both devices now visible). Later, her phone is stolen. From her tablet she opens Settings → Devices, taps her phone, taps Revoke. The phone's wallet is invalidated; the tablet remains. She also reviews Settings → Auth methods to ensure she has both a passkey and a recovery email set up, so even if she lost the tablet too she could recover her account.

**Why this priority**: Real-world wallet users will lose phones. Without first-class recovery and revocation, a lost-phone incident is a wallet-rebuild incident. Establishes that the wallet is recoverable in plain English without resorting to BIP39 mnemonics.

**Independent Test**: A presenter can demonstrate this with two test devices enrolled to one account. Revoke one from the other; observe the revoked device's wallet refuse to operate. Demonstrate adding/removing a passkey from auth methods.

**Acceptance Scenarios**:

1. **Given** Sarah has the wallet on one device, **When** she opens Settings → Devices, **Then** the active device is clearly indicated and a clear path to enrol additional devices is offered.
2. **Given** Sarah has the wallet on two devices, **When** she taps Revoke on one from the other, **Then** the revoked device's wallet stops being able to present, verify, or submit within seconds, and a clear "this device has been revoked" message is shown when next opened.
3. **Given** Sarah has at least one auth method beyond email-password (passkey or social), **When** she views Settings → Auth methods, **Then** all linked methods are listed with clear add/remove affordances and an explanation of why diverse methods improve recoverability.

---

### User Story 6 — A first-time user is guided through what the wallet does (Priority: P2)

A new user has just enrolled their wallet (Spec 1 flow). They've never seen the wallet's full surface before. After enrolment completes and the welcome takeover dismisses, the wallet runs a short interactive tour: a tooltip points at the hero Present action with *"Tap here when someone asks to see your credential"*, then the hero Verify action with *"Tap here to check someone else's credential — useful for doorstep callers"*, then the context chip with *"Switch organisations here"*, then the footer nav. Three taps to dismiss, can be replayed from Settings.

**Why this priority**: First-time UX is the difference between a wallet that gets used and a wallet that gets opened once and forgotten. Sets the novice-user UX bar that the rest of the spec inherits.

**Independent Test**: A presenter can demonstrate this with a fresh wallet (clear site data first). Open the wallet for the first time after enrolment, observe the tour, dismiss, replay from Settings.

**Acceptance Scenarios**:

1. **Given** a user has never seen the tour, **When** they open the wallet for the first time after enrolment, **Then** the tour starts automatically.
2. **Given** the tour is mid-stream, **When** the user taps anywhere outside the highlighted element or taps Dismiss, **Then** the tour ends and a "Replay tour" affordance is offered in Settings.
3. **Given** the user has dismissed the tour at least once, **When** they reopen the wallet, **Then** the tour does not re-fire automatically.
4. **Given** the user taps Replay tour from Settings, **When** the tour begins again, **Then** the full sequence runs as if it were the first time.

---

### Edge Cases

- **Wallet opened with multiple unread Needs-attention items.** Display all in the Needs-attention band; never collapse silently. If more than five, show the top three by urgency + "and 2 more →".
- **Context switched mid-verification.** Verification is anonymous (the citizen acts as themselves regardless of context for the purposes of being a verifier). Context switches do not interrupt verification.
- **Camera permission revoked between sessions.** On next portrait-capture attempt, the wallet detects the missing permission and walks the user through restoring it.
- **Network drops mid-application-submission.** Wallet keeps the unsubmitted form data in local storage; retry on reconnect; clear instructions if data was saved successfully.
- **User has zero credentials in active context but multiple in another.** Home's Credentials band shows an empty state with a CTA; the peek footer still surfaces the other-context content.
- **A presented credential is revoked mid-presentation.** Verification fails clearly. Sarah's wallet shows a recovery path ("Sign in to your council account to re-issue").
- **Two wallets enrolled to the same account try to do the same thing simultaneously.** The Wallet Service's existing per-platform-user serialisation handles this; the second action gets a clear "another device is acting; try again in a moment" message.
- **A first-time user disables the tour during the first step.** The tour is fully exited and not re-fired; the affordance to replay is still visible in Settings.

## Requirements *(mandatory)*

### Functional Requirements

#### Identity, context, and naming

- **FR-001**: The wallet MUST be presented to users as **Sorcha Wallet**, with no reference to "citizen" in any user-visible copy.
- **FR-002**: The wallet MUST support a user who holds memberships in one or more organisations and surface the active context prominently in the chrome (an "active-context chip").
- **FR-003**: The wallet MUST allow the user to switch the active context in no more than two taps from any wallet screen.
- **FR-004**: When the active context changes, the visible Home content (credentials, recent activity, suggested applications, persona autofill) MUST reflect the new context within one second.
- **FR-005**: The wallet MUST surface the presence of content in non-active contexts via a quiet "peek" indicator on Home, so other contexts never feel hidden.

#### Credential management

- **FR-006**: The wallet MUST list every credential the active context holds, with prominent issuer name, credential type, validity period, and one-tap access to full detail.
- **FR-007**: The wallet MUST allow the user to present a held credential to a relying party in no more than two taps from Home (one tap if there is only one credential).
- **FR-008**: When the user presents a credential, the wallet MUST allow selective disclosure — the user sees and confirms exactly which claims are revealed before the presentation is completed.
- **FR-009**: The wallet MUST allow the user to view a held credential's full content, status, issuance receipt, and presentation history.

#### Verification (citizen as verifier)

- **FR-010**: The wallet MUST allow the user to verify another party's credential without that other party being part of the user's organisation, account, or contact list.
- **FR-011**: The wallet MUST accept a presented credential via at least one of: QR code scan via the device camera, or NFC tap.
- **FR-012**: The wallet MUST render the verification outcome in plain English with a clear pass / warn / fail visual indicator and supporting detail expandable on demand.
- **FR-013**: When verification cannot complete (e.g. network unreachable, issuer key unresolvable), the wallet MUST explain what could not be checked and offer the user a safe-default recommendation in plain English.
- **FR-014**: The wallet MUST keep a private history of verifications it has performed, accessible to the user from Activity.

#### Application submission from the phone

- **FR-015**: The wallet MUST be able to render any blueprint-driven application form the platform supports, on the device.
- **FR-016**: When the user holds a profile (personal information), the wallet MUST auto-fill form fields that map to that profile, clearly marking auto-filled values and allowing the user to override any field.
- **FR-017**: The wallet MUST support capturing a portrait from the device camera, with full-screen capture, retake, and client-side resize to the platform's standard format.
- **FR-018**: When the user submits an application, the wallet MUST sign the submission using the user's identity (under the active context) and surface the in-progress state on Home.
- **FR-019**: If a submission fails for any reason, the wallet MUST preserve the user's form data and offer a clear retry path without forcing re-entry.

#### Transaction history

- **FR-020**: The wallet MUST keep a per-context history of significant events (credential issued, credential presented, credential verified, application submitted, credential revoked).
- **FR-021**: Each history entry MUST be tappable to reveal full detail including the cryptographic receipt (where one exists) and a verifiable trust panel.

#### Devices and auth methods

- **FR-022**: The wallet MUST allow the user to view all devices currently enrolled to their account, and revoke any device from any other device.
- **FR-023**: The wallet MUST surface the user's auth methods (email/password, passkeys, linked social accounts) and allow adding or removing any, with clear in-context guidance on why diverse methods improve recoverability.
- **FR-024**: When a device is revoked, that device's wallet MUST cease being able to present, verify, or submit within at most 30 seconds, and clearly indicate the revoked status when next opened.

#### Custody and signing

- **FR-025**: The wallet MUST default to managed-mode custody — the user is never required to manage a backup mnemonic phrase to use the wallet, and recovery operates by signing in to the account on another device.
- **FR-026**: The wallet MUST expose signing through a single abstraction (per the design doc's `IUserSigner`) so that future self-custody implementations can be added without rewriting the consuming UI.
- **FR-027**: Where signing requires user consent (presentation, submission), the wallet MUST present a clear consent surface before the cryptographic operation runs.

#### Novice-user UX bar

- **FR-028**: The wallet MUST run a short interactive guided tour the first time a user opens it after enrolment, covering the hero actions (Present, Verify), the context chip, and the footer navigation.
- **FR-029**: The wallet MUST persist the dismissal state of the tour per device; the tour MUST NOT re-fire on subsequent opens unless the user explicitly replays it.
- **FR-030**: Every empty state in the wallet MUST be paired with a clear suggested action or explanation; no empty state may be a bare zero-count message.
- **FR-031**: Every error surface MUST be paired with a plain-English description and a recovery path; technical exception messages must not appear in user-visible copy.
- **FR-032**: The reading age of all user-visible copy MUST average at or below Year 8.

#### Form-factor adaptation

- **FR-033**: The wallet's shared components MUST be usable from both the mobile PWA shell and the desktop web shell, with form-factor-appropriate layout differences (list vs grid, sheet vs dialog) controlled by component parameters, not by forking.

#### Testing and regression coverage

- **FR-034**: All existing Feature 124 (Spec 1) test suites MUST continue to pass after the rename from "Citizen Wallet" to "Sorcha Wallet".
- **FR-035**: Playwright coverage MUST exist for each of the three headline demo beats (doorstep verification, application from phone, context switching), each runnable as an independent automated test.
- **FR-036**: Issue #700 Phase 2 MUST be closed by this feature — Playwright coverage for post-redeploy cache behaviour and auth-gated navigation paths.

### Key Entities *(include if feature involves data)*

- **Custody mode**: A label for how the user's holder key is managed. v1 supports only "managed" (server-anchored holder key, device key local, delegation in the middle). Future values include "self-custody" and "co-signed" but are not implemented in v1.
- **User signer**: The abstraction representing how a user signs an action under the active context. Carries the custody mode, a display label, and an asynchronous sign operation that consuming UI invokes without awareness of custody-mode specifics.
- **Active context**: The current organisational membership the user is acting under. Determines which credentials, applications, persona values, and history entries are visible.
- **Per-context persona**: A user's stored profile (name, date of birth, address, contact details) scoped to a single context. The personal context has personal values; an employer context can have separate work values (work email, office address).
- **Verification record**: A private wallet-side entry recording that the user verified another party's credential at a particular time, including the trust outcome.
- **Guided-tour completion flag**: A per-device record indicating whether the first-time tour has been dismissed; replayable on user request.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A novice user (no prior wallet experience) can verify a stranger's credential at their door in under 30 seconds, starting from a closed wallet, in 95% of attempts where the credential is valid and reachable.
- **SC-002**: A user can submit a multi-page application from their phone — form, persona autofill, portrait capture, submission — in under 5 minutes, with a 95%-or-better single-attempt completion rate.
- **SC-003**: A user with at least two organisational memberships can switch their active context in two taps from any wallet screen, and all visible content reflects the new context within one second of the switch.
- **SC-004**: The first-time guided tour completes (not skipped past) in at least 70% of observed first-open sessions across the user base.
- **SC-005**: Doorstep verification produces a clear pass / warning / fail trust result for the user in 95% of attempts that don't involve genuine credential anomalies.
- **SC-006**: After the rename from "Citizen Wallet" to "Sorcha Wallet", the existing automated test suite for Spec 1 (Feature 124) reports zero regressions attributable to this feature.
- **SC-007**: Across ten observed end-to-end runs of each of the three headline demo beats, all three beats land cleanly without manual intervention beyond the user-driven actions required by the demo.
- **SC-008**: Issue #700 Phase 2 closes — automated coverage exists for post-redeploy cache behaviour (verified by rebuilding and re-deploying the wallet image, then asserting no broken asset references) and for every auth-gated navigation path in the wallet.
- **SC-009**: A code audit of the wallet PWA's pages confirms at least 90% of UI primitives (credential cards, detail views, devices, auth methods, profile) are consumed from the shared component library rather than reimplemented inline.
- **SC-010**: The reading age of all user-visible wallet copy, measured by a standard readability tool, averages at or below Year 8 (US Grade 7).

## Out of Scope

These items are explicitly deferred from this feature; tracked for later specs in the citizen arc or beyond.

- **Self-custody opt-in** (BIP39 on device, no server-side holder key). Reserved for v2. The `IUserSigner` abstraction designed in v1 is what unblocks v2.
- **Co-signed dual-key custody** (collector + employing org). v2 backlog per the 2026-05-10 design.
- **Consolidating `Sorcha.Verifier` (desk shell) into the wallet PWA.** Wallet adds verification capability; the desk verifier stays for counter and back-office use. Future spec can decide whether to fold them entirely.
- **Persistent verifier sessions or multi-credential verifications in a single interaction.** Spec 2 ships single-shot doorstep verification only.
- **A full design-system tokens overhaul** (typography scale, spacing scale, semantic colour palette, motion primitives). Future spec.
- **Field-vs-desk handoff flows** (user picks a task up on their phone after starting on the web shell, or vice versa). Future spec.
- **"Saved trusted parties" / verifier-side memory** (Margaret remembers she verified Liam previously and can fast-track next time). Future enhancement.
- **Native mobile app** (non-PWA). Out of scope; PWA is the v1 mobile surface.

## Assumptions

Captured for plan-phase reference; each is a reasonable default the spec adopts unless contradicted by user input.

- **The umbrella's invariants hold.** Sarah remains the protagonist of the citizen arc; new personas (Ben for context-switching demos, Margaret for doorstep verification) are auxiliary characters in service of demonstrating the surface.
- **The PWA stays a PWA.** No move to native mobile.
- **Today's hybrid custody (server-anchored holder key + browser-local device key + delegation) is formalised as "managed mode".** Citizens never see a BIP39 phrase in v1.
- **The wallet's URL stays `/wallet/`.** The rename is internal — namespaces, project names, container images, and user-visible app name — but not user-visible URLs.
- **The shared component library (`Sorcha.UI.Components.User`) is the contract.** Spec 2 grows the library and migrates PWA pages onto it; it does not fork or duplicate.
- **The `Sorcha.Verifier` desk shell continues to exist.** It can be consolidated later if needed; v2 doesn't require it to go away.
- **Persona is per-context.** Sarah's personal context has personal contact details; a work context can have work contact details; the wallet does not assume one global persona.
- **Verification doesn't require platform registration of the verifier.** The wallet generates an ephemeral verifier identity per session; OID4VP supports this.
- **Reading age is measured against a standard tool.** Plan-phase picks the specific tool and target score; the success criterion is "≤ Year 8 average."
