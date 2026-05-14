# Feature Specification: AssuredIdentity on the PWA

**Feature Branch**: `124-assured-identity-pwa`
**Created**: 2026-05-14
**Status**: Draft
**Input**: User description: "AssuredIdentity on the PWA — first sub-spec of the Strathcarron citizen arc. Replaces the HAIP filesystem wallet target in the existing AssuredIdentity walkthrough (Feature 107) with the real Citizen Wallet PWA (Feature 114) as the credential recipient, and adds a first-credential takeover arrival moment."

**Authoritative design**: [`docs/superpowers/specs/2026-05-13-spec-1-assured-identity-on-pwa-design.md`](../../docs/superpowers/specs/2026-05-13-spec-1-assured-identity-on-pwa-design.md)
**Umbrella context**: [`docs/superpowers/specs/2026-05-13-strathcarron-citizen-arc.md`](../../docs/superpowers/specs/2026-05-13-strathcarron-citizen-arc.md)

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Sarah enrols her phone before applying (Priority: P1)

Sarah, a Strathcarron resident, opens the Citizen Wallet on her phone for the first time. She runs the enrolment ceremony, naming her device. When enrolment completes with no credentials yet, the "Done" screen tells her plainly what to do next — submit her council application to receive her first credential — rather than reporting "Loaded 0 credential(s)" and leaving her unsure whether something went wrong.

**Why this priority**: Without a wallet enrolled, no later beat in the demo can land. The polished Done copy is small but eliminates the only place in the existing flow where a first-time citizen would feel something has gone wrong.

**Independent Test**: Open the wallet on a fresh device, complete the enrolment wizard without any pre-issued credentials, and confirm the Done screen reads as a forward-looking next step rather than a flat zero-count report.

**Acceptance Scenarios**:

1. **Given** Sarah has a platform account and is signed in to the wallet for the first time, **When** she completes the enrolment ceremony and the server returns zero credentials, **Then** the Done screen reads "Enrolled. Your wallet is ready — submit your council application to receive your first credential." and offers a single Open-wallet action.
2. **Given** Sarah enrols a second device for an existing account that already holds credentials, **When** she completes the enrolment ceremony and credentials are loaded, **Then** the Done screen retains the existing copy reporting the credentials loaded and the delegation expiry.

---

### User Story 2 — Sarah sees the wallet waiting for her credential (Priority: P1)

Sarah submits the AssuredIdentity application on the council web site. She switches to her wallet on her phone. Instead of seeing the generic "no credentials yet" empty state — which would suggest nothing has happened — she sees a clear waiting state: a message that her Assured Identity application is in review, and a pulsing skeleton card showing where the credential will land.

**Why this priority**: The waiting state turns the analyst's processing time from dead air into a designed beat. It also makes the eventual takeover feel like the completion of an in-progress action rather than an event from nowhere.

**Independent Test**: Set a pending-application notice for Sarah's account from outside the wallet UI; open the wallet; confirm the Home shows the new copy and the pulsing skeleton card; clear the notice; confirm Home reverts to the standard empty state.

**Acceptance Scenarios**:

1. **Given** Sarah has enrolled but holds no credentials, and **Given** her account has a pending-application notice for AssuredIdentity, **When** she opens the wallet Home, **Then** the Home shows the message "Your Assured Identity application is being reviewed. You'll see it here when it's ready." together with a pulsing skeleton card.
2. **Given** Sarah has no credentials and no pending-application notice, **When** she opens the wallet Home, **Then** the Home shows the standard empty state without the waiting message or skeleton card.

---

### User Story 3 — Sarah is watching when her Assured Identity arrives (Priority: P1)

Sarah keeps the wallet open while the verification analyst reviews her application. When the analyst approves, the wallet receives the issuance notification in the foreground and presents a full-screen welcome: her new Assured Identity id-card prominently shown, with "Welcome to your wallet" copy and a single Open action to dismiss. Once dismissed, the id-card settles into her Home as her first credential.

**Why this priority**: This is the headline moment of the entire arc and the demonstrator the council story is built around. Everything else in this spec exists to support this beat.

**Independent Test**: With Sarah's wallet open and watching, trigger an Assured Identity issuance to her account; confirm the takeover overlay appears with her id-card and welcome copy; confirm dismissal returns her to a Home page showing the new credential.

**Acceptance Scenarios**:

1. **Given** Sarah is on the wallet Home with the waiting state active, **When** the verification analyst approves her application and the wallet receives the issuance notification, **Then** the wallet displays the full-screen takeover showing her Assured Identity id-card, the "Welcome to your wallet" headline, and an Open action.
2. **Given** the takeover is displayed, **When** Sarah selects Open, **Then** the takeover dismisses, the waiting state ends, and Home displays the Assured Identity id-card as her credential.

---

### User Story 4 — Sarah missed the live moment (cold-open takeover) (Priority: P2)

Sarah puts her phone down before the analyst approves, or locks her screen, or switches to another app. When she next opens the wallet, the takeover fires anyway — the welcome is not contingent on her watching the moment of issuance.

**Why this priority**: In real-world use, citizens will rarely watch their wallet during issuance. Without the cold-open path, the takeover would be a coin-toss; missing it would be a permanent regression for that citizen.

**Independent Test**: Issue an Assured Identity to Sarah's account while her wallet is closed; reopen the wallet; confirm the takeover fires on first paint of Home; confirm subsequent re-opens do not re-fire the takeover.

**Acceptance Scenarios**:

1. **Given** Sarah's wallet is closed and her welcome takeover has never fired, **When** an Assured Identity is issued to her account and she subsequently opens the wallet, **Then** the takeover fires before she sees the standard Home state.
2. **Given** the same conditions, **When** the wallet completes its first sync after open and discovers the credential, **Then** the takeover fires immediately rather than waiting for any further user action.

---

### User Story 5 — The welcome never fires a second time (Priority: P2)

After dismissing the takeover once, Sarah continues using her wallet normally. The welcome ceremony does not repeat — she sees the standard Home with her credential and never re-enters the welcome flow on this device, even across app restarts.

**Why this priority**: A repeating welcome would degrade from celebration to annoyance immediately. This is the correctness guarantee that lets US3 and US4 ship safely.

**Independent Test**: Trigger the takeover and dismiss it; close and reopen the wallet multiple times; confirm the takeover never re-appears on this device.

**Acceptance Scenarios**:

1. **Given** Sarah has dismissed the welcome takeover at least once on this device, **When** she closes the wallet and reopens it, **Then** Home displays her credentials directly with no takeover overlay.
2. **Given** the dismissal record is present on this device, **When** Sarah's credential list changes for any reason (a status update, an unrelated future credential), **Then** no welcome takeover is rendered on this device.

---

### User Story 6 — Walkthrough operator runs the end-to-end demo (Priority: P3)

A demo presenter (or test operator) runs the AssuredIdentity walkthrough scripts with the AI verification analyst enabled. From a clean state they bring up the platform, pre-create Sarah's account, sign her in to the wallet, and run the phase 1 script that drives the end-to-end issuance. The script sets and clears the pending-application notice appropriately so the wallet's waiting state and takeover land cleanly. The legacy HAIP filesystem wallet target and its associated files are no longer part of this path.

**Why this priority**: This is the meta-story that makes the previous stories reproducible. The presenter cares about a one-command-per-phase workflow, not the implementation. P3 because the underlying capabilities ship in P1/P2; the operator-facing wrapper is the lowest-risk leaf.

**Independent Test**: From a clean docker-compose start, run the walkthrough setup and the phase 1 run scripts with the agents flag; confirm the demo sequence completes end-to-end and that the legacy filesystem wallet directory is absent.

**Acceptance Scenarios**:

1. **Given** the platform is freshly started and the walkthrough setup script has been run, **When** the operator runs the phase 1 script with the AI agents enabled, **Then** Sarah's wallet sequence completes through enrolment → waiting → takeover → settled Home without manual intervention beyond the operator-driven actions explicitly required (Sarah's submission, the takeover dismiss).
2. **Given** the walkthrough has been run, **When** the operator runs the independent multi-peer cross-peer delivery test, **Then** the test passes without depending on the legacy filesystem wallet path.

---

### Edge Cases

- **Sarah clears her wallet's local data** between dismissing the takeover and reopening: the welcome may fire again on the next open, because the dismissal record was local to that device. This is acceptable behaviour — a wallet reset is a new wallet.
- **Sarah enrols a second device** after dismissing the takeover on her first: the second device has never been welcomed, so the takeover fires on the second device's first credential view. This is intended — the welcome is per-device.
- **The pending-application notice is set but no credential ever arrives** (analyst declines, system outage, etc.): the waiting state persists indefinitely until the notice is cleared by an out-of-band action. Spec 1 does not own a timeout for this — the walkthrough script clears on completion; production timeout is a follow-up.
- **A credential is issued without a prior pending-application notice**: the takeover still fires (the welcome is contingent on the credential arriving, not on the prior notice). The notice's only role is the waiting state.
- **The wallet is opened during the brief window between credential receipt and the local sync completing**: the takeover must fire as soon as the credential is visible to the wallet, not require a subsequent user gesture.
- **Two devices enrolled to the same account, both with the takeover undismissed**: a credential issuance triggers the takeover independently on each device when each is next opened.
- **The verification analyst takes longer than expected** (minutes, not seconds): the waiting state holds without timing out from the wallet's side; the demo completes when the credential lands.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The enrolment wizard's completion screen MUST present forward-looking copy when zero credentials are loaded, directing the citizen to submit a council application as the next step. When one or more credentials are loaded, the existing completion copy MUST be retained.
- **FR-002**: The wallet Home MUST display a distinct waiting state — informational copy plus a pulsing skeleton credential card — whenever a pending-application notice is set for the signed-in citizen and the wallet holds no matching credential yet.
- **FR-003**: The wallet MUST clear the waiting state immediately when a matching credential arrives.
- **FR-004**: The wallet MUST present a full-screen welcome takeover the first time the citizen views the wallet after their first credential becomes visible. The takeover MUST contain the credential's id-card visualisation, a "Welcome to your wallet" headline, and a single Open action that dismisses the takeover.
- **FR-005**: The welcome takeover MUST fire whether the credential becomes visible while the wallet is open (foreground notification) or while the wallet is closed (discovered on first open after issuance).
- **FR-006**: The wallet MUST persist, per device, a record that the welcome takeover has been dismissed, and MUST NOT re-present the takeover on subsequent app opens on that device.
- **FR-007**: For credentials beyond the first on a given device, the wallet MUST NOT use the welcome takeover. (The steady-state arrival treatment for subsequent credentials is locked in the umbrella design and will be exercised by a later spec — Spec 1 only needs to refrain from reusing the welcome.)
- **FR-008**: The Wallet Service MUST expose a citizen-scoped capability to set, clear, and read a pending-application notice consisting of a human-readable label (no credential content). The capability MUST be authenticated as the citizen and scoped to that citizen's account.
- **FR-009**: The AssuredIdentity walkthrough's phase 1 script MUST set the pending-application notice immediately after the citizen submits the application, and clear it on completion of the issuance action.
- **FR-010**: The AssuredIdentity blueprint MUST issue the AssuredIdentityCredential with a target audience that delivers the credential into the Citizen Wallet PWA rather than any filesystem-based wallet.
- **FR-011**: The legacy filesystem-wallet directory and all script paths that reference it MUST be removed from the AssuredIdentity walkthrough.
- **FR-012**: The independent cross-peer delivery test for AssuredIdentity (the multi-peer script) MUST continue to pass after the removal of the filesystem-wallet path.
- **FR-013**: The Citizen Wallet's existing automated test suite MUST continue to pass with no regressions introduced by this feature.
- **FR-014**: The walkthrough setup script MUST pre-create the demonstration citizen's platform account and leave the citizen signed in to the wallet host so that the demo can begin from the moment the citizen taps the wallet entry point.
- **FR-015**: The credential's id-card visualisation inside the welcome takeover MUST be rendered by the same component used by the application wizard's review page and the standalone credential detail page (the cross-cutting invariant from the umbrella).

### Key Entities

- **Welcome acknowledgement record**: A per-device marker recording that the wallet's first-credential welcome takeover has been dismissed on this device. Has at minimum a dismissed-at timestamp. Lives locally on the device — clearing local data resets it.
- **Pending-application notice**: A per-citizen indication that an application for a credential is in flight. Has at minimum a human-readable label naming the application (e.g. "Assured Identity"). Used by the wallet only to render the waiting state; carries no credential content.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A presenter can run the AssuredIdentity walkthrough setup and phase 1 scripts and complete the demonstration citizen's end-to-end sequence — empty wallet → enrolled → waiting → welcome takeover → settled Home — within 60 seconds of the citizen submitting the application, including the AI analyst's processing time.
- **SC-002**: Across 10 consecutive demo runs in the foreground configuration (citizen watching the wallet at issuance), the welcome takeover renders correctly in 10/10 runs.
- **SC-003**: Across 10 consecutive demo runs in the cold-open configuration (wallet closed between submission and approval, reopened after approval), the welcome takeover renders on the first reopen in 10/10 runs.
- **SC-004**: Across 10 close-and-reopen sequences after a single takeover dismissal, the takeover never re-appears (0/10).
- **SC-005**: The Citizen Wallet's existing automated test suite reports zero regressions attributable to this feature.
- **SC-006**: The AssuredIdentity multi-peer cross-peer delivery test continues to pass after the filesystem-wallet path is removed.
- **SC-007**: A new citizen completing the enrolment ceremony on a wallet with no pre-issued credentials never sees the legacy "Loaded 0 credential(s)" copy; instead the forward-looking guidance is shown.
- **SC-008**: The demonstration citizen's wallet displays the waiting state for the entire duration between submission and credential arrival (100% of the analyst's processing window), and clears it within one second of the credential becoming visible.

## Out of Scope

These are deliberately excluded from this feature; each is owned by a downstream spec in the citizen arc:

- The Progressive Web App install-prompt experience and home-screen-icon ceremony (owned by Spec 2 — wallet UX foundations).
- The first-launch onboarding experience before any enrolment has occurred (owned by Spec 2).
- Any redesign of the enrolment wizard beyond the conditional completion-screen copy in FR-001 (owned by Spec 2).
- Hosting the council application form inside the wallet itself (owned by Spec 5 or later).
- The seam UX for enrolling a wallet from inside a council application wizard (owned by Spec 3).
- A production-grade listing of multiple pending applications per citizen, including timeout handling (a Spec 2 follow-up — Spec 1 only needs the single label that the walkthrough script controls).
- The steady-state arrival treatment for credentials two and beyond (locked in the umbrella; exercised in Spec 4 when the second credential first lands).

## Assumptions

- The demonstration citizen's interaction with the council application form is performed on the existing council web surface (status quo). No new application surface is introduced.
- The AI verification analyst — already present in the AssuredIdentity walkthrough — plays the approval role in the demo. A human analyst is a valid substitute and changes no part of this feature's behaviour.
- The welcome-acknowledgement record is per-device-per-wallet rather than per-account. A citizen enrolling a second device sees the welcome on that second device; this is intended.
- The wallet has a working push channel from the platform (the existing in-app notification path for issued credentials). This feature does not introduce any new push behaviour.
- The pending-application notice is set and cleared by an out-of-band controller (the walkthrough script in the demo; in production a future feature integrates this into the application submission flow). The wallet itself never decides when a notice is active.
