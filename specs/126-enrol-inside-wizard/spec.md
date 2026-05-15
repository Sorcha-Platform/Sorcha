# Feature Specification: Sorcha Wallet enrolment inside a council application wizard

**Feature Branch**: `126-enrol-inside-wizard`
**Created**: 2026-05-15
**Status**: Draft
**Input**: User description: "Spec 3 of the Strathcarron citizen arc — Sorcha Wallet enrolment inside a council application wizard. Lands the cold-start onboarding gate that turns a council-page visitor into a Sorcha-account-holding, wallet-enrolled citizen as a side-effect of the application form they came for. Returning citizens see only a sign-in screen."

**Authoritative design**: [`docs/superpowers/specs/2026-05-15-spec-3-enrol-inside-wizard-design.md`](../../docs/superpowers/specs/2026-05-15-spec-3-enrol-inside-wizard-design.md)
**Umbrella context**: [`docs/superpowers/specs/2026-05-13-strathcarron-citizen-arc.md`](../../docs/superpowers/specs/2026-05-13-strathcarron-citizen-arc.md)
**Predecessor specs**: Feature 124 (Spec 1) shipped 2026-05-14 as `spec-124-complete`; Feature 125 (Spec 2) shipped 2026-05-14 as `spec-125-complete`.

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Sarah onboards from scratch via a council form (Priority: P1)

Sarah has never used Sorcha before. She lands on the Strathcarron Council website looking to apply for a driving licence and taps "Apply". The page tells her plainly that she'll need a Sorcha account and a wallet on her phone for the council to deliver her credential — and walks her through both as part of starting the application. She signs up with her email, sees a QR code on the council page, scans it with her phone, and her phone walks her through pairing the wallet to her new account. The council page reacts the moment her phone is ready, drops her into the application form, and she completes the rest of the application normally. After she submits, the council page tells her to watch her phone; the credential lands in the wallet within seconds.

**Why this priority**: This is the umbrella's defining beat — "Sarah-from-cold-start acquires a wallet as a side-effect of the council form she came for." Without it, every citizen has to discover the wallet some other way, and the citizen arc doesn't hold together. Tier 3 is the only path through which Spec 3's load-bearing decisions (preflight gate, hybrid QR, wallet-mandatory, cross-device coordination) all visibly land at once.

**Independent Test**: A fresh-state browser session (no Sorcha cookies, no PWA installed) arrives at the council page, completes signup, scans/taps/pastes the enrolment link, completes the device-pairing ceremony on a phone or second browser context, returns to the council page to find it has advanced to the form, fills the form, submits, and observes the credential arriving in the wallet. End-to-end, single session.

**Acceptance Scenarios**:

1. **Given** Sarah has no Sorcha account and no wallet, **When** she clicks "Apply" on a council form, **Then** the page renders a preflight explaining she'll need an account and a wallet, with a single clear "Sign in or create your account" call to action — and no QR code is shown until she completes signup.
2. **Given** Sarah has just completed signup, **When** she lands back on the council page, **Then** the page shows a QR code, a tap-able link (prominent on mobile), and a "Copy link" fallback — all resolving the same single-use enrolment URL.
3. **Given** Sarah scans the QR from her phone, **When** her phone's wallet asks her to confirm "You're about to enrol this device for `sarah@example.com`", **Then** she sees her own email and name surfaced before the device is paired, so a stranger scanning by mistake would recognise it as not theirs.
4. **Given** Sarah completes pairing on her phone, **When** the desktop council page has been waiting, **Then** the page transitions within 2 seconds to the application form without requiring her to click anything.
5. **Given** Sarah submits the completed application, **When** the success screen renders, **Then** it tells her to watch her wallet, and the credential appears in her wallet within seconds (matching the F124 first-credential welcome takeover).

---

### User Story 2 — Returning citizen fast-path (Priority: P1)

Sarah comes back to the council a week later to apply for a different service. She still has her phone, still has her account, the wallet is still paired. The council page recognises her immediately — she signs in once and goes straight to the form. No QR, no enrolment screen, no "you'll need a wallet" copy. The credential for the new service lands in her existing wallet the same way it did the first time.

**Why this priority**: This story proves the gate doesn't impose friction on the common case. A cold-start beat that breaks the returning experience would be a net regression. P1 because every citizen becomes a returning citizen on their second visit.

**Independent Test**: A pre-enrolled test account (one with a registered wallet device) arrives at the council page, signs in once, lands straight on the form with no enrolment surface visible, completes and submits, and observes credential delivery to the existing wallet.

**Acceptance Scenarios**:

1. **Given** Sarah has a Sorcha account with at least one active device, **When** she arrives at the council page signed-out, **Then** she sees a sign-in screen — not a QR code, not an enrolment gate, not a "what is this" explainer.
2. **Given** Sarah has signed in, **When** the council page evaluates her state, **Then** it drops her directly into the application form without an intermediate "wallet setup" step.
3. **Given** Sarah completes the application, **When** the credential is issued, **Then** it lands in the same wallet she enrolled previously without her needing to re-pair anything.

---

### User Story 3 — Lost-phone mini-gate (Priority: P2)

Sarah's phone was lost last week. She got into the council page on her work laptop, signed in (she remembers her password), and wants to apply for something — but her account has no active device anymore. The page recognises this and shows her the enrolment QR alone (no signup gate, because she already has an account). She enrols her new phone, returns to the council form, and continues.

**Why this priority**: Real-world wallet users will lose phones. Without a clean mini-gate, lost-phone recovery is a confusing "you have an account but the page thinks you don't" loop. P2 because it's a real edge case but happens much less than first-time onboarding.

**Independent Test**: A test account with all devices revoked attempts a council application. The page shows the wallet-pairing gate without re-prompting signup; pairing a new device transitions to the form.

**Acceptance Scenarios**:

1. **Given** Sarah has an account but zero active devices, **When** she arrives at the council page signed in, **Then** the page shows the enrolment QR with copy like "Let's pair this device with your wallet" — explicitly not "Sign up to get a wallet".
2. **Given** Sarah completes pairing the replacement device, **When** the council page receives the device-paired signal, **Then** the page advances to the application form within 2 seconds.

---

### User Story 4 — Stranger scans the QR by mistake (Priority: P2)

Sarah's QR is showing on her desktop screen. Her brother, curious, scans it with his phone before she can stop him. Instead of silently pairing his phone to her account, the wallet on his phone surfaces a clear "You're about to enrol this device for Sarah Example (`sarah@example.com`). If that's not you, close this page." He recognises that's his sister's email and closes the page — no device is registered.

**Why this priority**: The session token is a bearer credential; whoever scans it gets to act on the bound account. Without the confirmation gate, accidental scans turn into account compromise. P2 because the abuse path requires physical proximity to the QR, but the mitigation is cheap and the failure mode is catastrophic.

**Independent Test**: Generate a session-token URL on one browser/account. Open the URL on a second browser. Verify the confirmation dialog renders the bound user's identifying details. Verify cancelling the confirmation leaves no device registered against either account.

**Acceptance Scenarios**:

1. **Given** Sarah's QR is showing on her desktop, **When** any device opens the enrolment URL, **Then** the wallet shows a confirmation surface naming the email and display name of the account the link is bound to — before any device-pairing happens.
2. **Given** the wallet shows the confirmation, **When** the person holding the phone chooses "this isn't me / close this page", **Then** no device gets registered against the bound account and the original user can mint a fresh QR.
3. **Given** the wallet shows the confirmation, **When** the person holding the phone confirms it IS them, **Then** the device-pairing ceremony continues normally.

---

### User Story 5 — Same-device tap-link (Priority: P3)

Sarah is on her phone's browser when she arrives at the council page. The page detects she's on mobile and renders the tap-able link more prominently than the QR (which she can't easily scan with the device she's already on). She taps the link, her phone's wallet opens, she confirms it's her, the pairing happens, and the browser tab she came from transitions to the form when she switches back.

**Why this priority**: Increasing share of users are mobile-only. The umbrella's Decision #3 hybrid universal QR is meant to handle this without a separate "phone user" flow. P3 because the underlying mechanism is the same as the cross-device case; this story is mostly a copy-and-emphasis concern.

**Independent Test**: A mobile-emulated browser session arrives at the council page; the tap-link is prominent (above or replacing the QR), tapping it opens the same enrolment URL and proceeds through the same confirmation + pairing flow.

**Acceptance Scenarios**:

1. **Given** the council page detects a mobile viewport, **When** the enrolment gate renders, **Then** the tap-able "Open on this device" link is more prominent than the QR (or the QR is hidden by default behind a "show QR" expander).
2. **Given** Sarah taps the link on her phone, **When** her wallet opens with the enrolment URL, **Then** the confirmation + pairing flow proceeds identically to the cross-device case.

---

### Edge Cases

- **Citizen distracted; session token expires before pairing completes.** The council page shows a clear "QR expired — let's get you a new one" with a regenerate button. No dead end.
- **PWA install prompt dismissed.** Wallet still works in the browser tab and pairing proceeds; only the home-screen icon is deferred.
- **Phone offline during pairing.** Wallet surfaces a "Couldn't reach Sorcha — check your connection" message with retry; council page stays in the waiting state.
- **Real-time pairing-completion signal fails to establish.** Council page silently falls back to checking the citizen's device state every few seconds; if that also fails for a minute, surfaces a manual "I've enrolled — continue" button.
- **Citizen abandons signup mid-flow.** Next visit: not signed in, restart from Tier 3.
- **Citizen completes signup but never enrols a device.** Next visit: signed in with zero devices, mini-gate (Tier 2) takes over — no signup repetition.
- **Council form was partially filled before the gate fired.** Form state is preserved in the browser; restored when the gate clears.
- **Two browser tabs of the same council form, same Sorcha account.** Both subscribe to the same pairing-completion signal; both advance idempotently.
- **Citizen opens enrolment URL more than once on the same phone.** Token's one-time-use property is enforced; the wallet either finishes the pairing it was already doing or surfaces "you're already set up" — no duplicate device row.

## Requirements *(mandatory)*

### Functional Requirements

#### Tier detection and routing

- **FR-001**: The council application page MUST detect whether the visiting citizen has a Sorcha account and whether that account has at least one active wallet device, and render exactly one of three surfaces accordingly: cold-start preflight (no account), wallet-pairing mini-gate (account but no device), or the application form (account + at least one device).
- **FR-002**: Tier detection MUST be transparent to the citizen — they MUST never be asked to declare "I'm new" or "I'm returning"; the page MUST infer it.
- **FR-003**: A citizen who starts as cold-start and completes onboarding MUST be treated as a returning citizen on their next visit (one-way transition).

#### Cold-start preflight

- **FR-004**: The cold-start preflight MUST present a plain-English explanation of why a wallet is needed before any QR code, scan affordance, or wallet-pairing surface is shown.
- **FR-005**: The cold-start preflight MUST offer signup via the platform's existing supported methods (email/password, social, or passkey) — this feature MUST NOT introduce a separate "council-only" account.
- **FR-006**: After signup completion, the citizen MUST be returned to the same council application page they started from — not the platform's default landing surface.

#### Hybrid enrolment affordance

- **FR-007**: The wallet-pairing surface (Tier 2 mini-gate and Tier 3 post-signup) MUST present the same enrolment URL in three equivalent forms: a scannable QR code, a tap-able link, and a copyable text string.
- **FR-008**: On mobile viewports, the tap-able link MUST be visually more prominent than the QR.
- **FR-009**: The enrolment URL MUST carry a short-lived (no more than 10 minutes) one-time-use session token bound to the signed-in citizen's account.
- **FR-010**: When the citizen opens the enrolment URL on a wallet-capable device, the wallet MUST display a confirmation surface naming the email and display name of the account the token is bound to, before any device-pairing operation begins.
- **FR-011**: The confirmation surface MUST allow the recipient to cancel without registering a device against the bound account.
- **FR-012**: When the session token is consumed (whether by completed pairing or by an attempted second use), subsequent attempts to use the same token MUST be rejected.

#### Cross-device coordination

- **FR-013**: When pairing completes on the citizen's wallet device, the council page MUST advance to the application form without requiring manual action from the citizen.
- **FR-014**: The pairing-completion signal MUST reach the council page within 2 seconds of pairing in 95% of attempts.
- **FR-015**: When the real-time signal fails to establish or fails to fire, the council page MUST silently fall back to a polling check on the citizen's device state.
- **FR-016**: When both the real-time signal and the polling fallback fail to surface pairing completion within 60 seconds, the council page MUST present a clear manual recovery affordance ("I've enrolled — continue").

#### Session-token expiry and regeneration

- **FR-017**: If the session token expires before pairing completes, the council page MUST present a clear regenerate affordance with copy that explains what happened in plain English.
- **FR-018**: A regenerate request MUST produce a freshly-minted token without requiring the citizen to repeat signup or any earlier step.

#### Form continuity

- **FR-019**: Form data entered before the gate fired (within the same browser session) MUST be preserved across the gate transition.
- **FR-020**: Submission of the application MUST happen on the council page (web shell), not via the citizen's wallet device, in this feature's v1 scope.
- **FR-021**: The application's success screen MUST tell the citizen to watch their wallet, and MUST update to reflect credential arrival when the wallet receives the credential.

#### Account + device-state preservation

- **FR-022**: A citizen who completes signup but abandons before pairing MUST be able to return later and resume from the wallet-pairing gate (Tier 2), without re-doing signup.
- **FR-023**: A citizen who abandons during the cold-start preflight MUST be able to return later and restart from the preflight, with no orphaned account or partial-state record blocking them.

#### Security

- **FR-024**: The enrolment URL MUST be served over HTTPS only.
- **FR-025**: Signup redirects after completion MUST be validated against an allowlist of trusted return-to domains — open redirects MUST be rejected.

### Key Entities

- **Enrolment Session Token**: A short-lived (≤10 min) one-time-use bearer token bound to a specific Sorcha account, used by the wallet to authenticate itself for an upcoming pairing operation. Distinct from the citizen's normal access token; specific to this single-use enrolment moment.
- **Citizen Tier**: A derived state — not stored — indicating whether the visiting citizen needs cold-start onboarding, mini-gate pairing, or no gate at all. Recomputed on every visit from account-state + device-state probes.
- **Pairing-Completion Signal**: The event surfacing "a new wallet device has been registered against this account", consumed by the council page to advance its state. Has a primary (real-time push) path and a fallback (polling) path; same logical event, two transports.
- **Trusted Return-To Domain Allowlist**: The set of domains that signup may redirect to on completion. Council pages are added by platform operators; citizens cannot extend it.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A cold-start citizen (no prior Sorcha account or device) completes the journey from "click Apply" to "form ready to fill" in under 90 seconds in 95% of attempts (excluding citizen-driven distractions like leaving the page).
- **SC-002**: A returning citizen with a paired device completes the journey from "click Apply" to "form ready to fill" with no surface other than a single sign-in screen in 100% of cases.
- **SC-003**: A mini-gate citizen (account but no device) completes pairing and reaches the form without seeing any signup or "create account" surface in 100% of cases.
- **SC-004**: The pairing-completion signal reaches the council page within 2 seconds of pairing completion in 95% of attempts.
- **SC-005**: When the real-time signal fails, the polling fallback surfaces pairing completion within 6 seconds in 95% of attempts.
- **SC-006**: An expired session token never leaves the citizen on a dead-end screen — a regenerate affordance MUST be reachable in one click in 100% of cases.
- **SC-007**: Across 10 independent walks of the cold-start journey by different test citizens, all 10 complete successfully end-to-end (preflight → signup → pairing → form → submission → credential delivery).
- **SC-008**: A "stranger scans the QR" simulation (an unintended second party opens the enrolment URL) results in zero device registrations against the bound account when the unintended party cancels the confirmation, in 100% of attempts.
- **SC-009**: Existing Feature 124 and Feature 125 test suites remain green after this feature's changes (no regressions in the wallet's credential-arrival or multi-context behaviour).

## Out of Scope

These items are explicitly deferred from this feature; tracked for later specs or follow-up PRs.

- **Email-pickup fallback for citizens who refuse to enrol a wallet.** v1 keeps the wallet mandatory; a future spec may add a clearly second-class wallet-less delivery path if accessibility/equity testing flags an exclusion problem.
- **Wallet-hosted form continuation.** The PWA's `ApplicationInstance` form-hosting capability (built in Feature 125) is NOT used in this feature's v1 cold-start path; submission stays on the web shell. A future spec may add "continue on your phone" as a citizen-driven choice.
- **A "list of available applications" surface for citizens.** This feature designs the gate; the form behind it is whatever application is consumed via a council page. Citizen-facing application catalogues are out of scope.
- **Multi-tenant council onboarding UX.** This feature assumes a single council (Strathcarron) consuming the gate. Onboarding additional councils onto the same machinery is a later roadmap item.
- **Server-set cookie binding for the session token.** Future hardening for the friend-scans-by-mistake path; v1 mitigation is the confirmation dialog.
- **Form-data preservation across DIFFERENT browser sessions.** Form data preserved across the gate transition within one session is in scope; resuming a half-filled form weeks later on a different browser is not.
- **Cross-council single-sign-on UX polish.** The existing Sorcha auth handles cross-council session sharing; no new design needed.

## Assumptions

Captured for plan-phase reference; each is a reasonable default the spec adopts unless contradicted.

- **The umbrella's invariants hold.** The hybrid universal QR (Decision #3) is the only entry-point mechanism. Email/password is the durable account anchor (Decision #6); social and passkey are equivalent entry points. No new account model is introduced by this spec.
- **Strathcarron is the demo council.** Production deployments may onboard additional councils, but Spec 3's success criteria assume the Strathcarron council page is the one consuming the gate.
- **The wallet's first-credential welcome takeover from Feature 124 fires unchanged.** Spec 3 doesn't redesign the credential-arrival moment; it ensures the moment can happen by getting a wallet into the citizen's hands.
- **Citizens have one phone they use for this.** Multi-device enrolment (one citizen pairing multiple phones in one cold-start session) is unusual and not specifically designed for in v1.
- **Existing Feature 116 signup endpoints are extended (not rewritten).** Adding a return-to parameter is an additive change; signup mechanics, password rules, social provider configuration, etc., all remain as they are.
- **Real-time pairing-completion signal uses an existing platform mechanism.** No new transport is introduced; the implementation reuses the platform's existing real-time push surface.
- **Citizens read the confirmation dialog before tapping through.** The friend-scans-by-mistake mitigation depends on the recipient noticing that the displayed email and name are not theirs. Citizens who reflexively tap through any confirmation prompt are not protected; this is an accepted v1 limitation.
