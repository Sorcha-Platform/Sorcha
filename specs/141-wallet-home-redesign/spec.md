# Feature Specification: Citizen Wallet Home — "Bolder" Visual Reskin

**Feature Branch**: `141-wallet-home-redesign`
**Created**: 2026-05-26
**Status**: Draft
**Input**: Redesign the Citizen Wallet PWA Home screen to the approved "B · Bolder" visual direction (`docs/mockups/design_handoff_wallet_home/`). A visual reskin of the existing home chrome — not a change to its information architecture or behaviour.

## Overview

The Citizen Wallet home is the first screen a citizen sees after the app loads. Today it is functional but visually plain: a standard app bar, flat action tiles, a plain bottom navigation rail, and a text-only empty state. The design team has selected the **"Bolder"** direction — a wallet-native treatment with a gradient hero, a stacked-card metaphor for the empty wallet, two prominent action buttons, and a floating pill navigation bar.

This feature restyles the home **chrome** to that direction while preserving every existing capability and information element. It is explicitly a reskin: the same data, the same actions, the same navigation destinations, the same supporting bands (needs-attention, recent activity, other-context peek, waiting state, first-credential welcome) — presented through new visual components.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - First-run citizen sees the bold empty home (Priority: P1)

A citizen opens the wallet for the first time (no credentials yet). They see a gradient hero with a welcome message, a stacked-card "add a credential" metaphor that invites them to enrol, and two clear action buttons (Present, Verify). The empty experience feels like a real wallet, not a blank list.

**Why this priority**: The empty state is the default and most common first impression. It carries the signature visual identity of the redesign and is the artboard the design was authored against.

**Independent Test**: Sign in as a citizen with zero credentials, open Home, and confirm the gradient hero, the three-card ghost stack with an "Add a credential" top card, and the Present/Verify action pair all render. Tapping the top ghost card or the enrol affordance starts device enrolment.

**Acceptance Scenarios**:

1. **Given** a signed-in citizen with no credentials, **When** Home loads, **Then** a gradient hero shows the eyebrow "WELCOME", a headline indicating the wallet is empty, and a subtitle prompting enrolment.
2. **Given** the empty Home, **When** the citizen taps the top ghost card, **Then** the device-enrolment flow opens (same destination as the existing "Enrol this device" action).
3. **Given** the empty Home, **When** the citizen views the action pair, **Then** Present appears visibly de-emphasised (disabled) and Verify appears active.
4. **Given** the empty Home, **When** the citizen taps Verify, **Then** the verify flow opens.

---

### User Story 2 - Citizen with credentials sees the populated home under the new chrome (Priority: P1)

A citizen who already holds credentials opens the wallet. The gradient hero now reflects an active wallet (credential count, "tap a card to present" guidance). Their existing credential cards render unchanged beneath the new hero and action buttons. Present is enabled. All the supporting bands they rely on (needs-attention, recent activity, other-context peek, waiting state, first-credential welcome) still appear and behave exactly as before.

**Why this priority**: The redesign must not regress the experience for citizens who already use the wallet. This story proves the reskin is additive chrome over preserved behaviour.

**Independent Test**: Sign in as a citizen with at least one credential, open Home, and confirm the hero shows an "active wallet" treatment, the existing credential cards render, Present is enabled and navigates to the present flow, and every pre-existing band still appears when its data is present.

**Acceptance Scenarios**:

1. **Given** a citizen with one or more credentials, **When** Home loads, **Then** the hero shows the eyebrow "ACTIVE WALLET" and a headline reflecting the credential count.
2. **Given** the populated Home, **When** the citizen taps Present, **Then** the present flow opens.
3. **Given** a pending-application notice exists and the wallet is empty, **When** Home loads, **Then** the existing waiting-state treatment is shown in place of the bare empty state.
4. **Given** the citizen's first credential has just arrived and they have not been welcomed on this device, **When** Home loads, **Then** the existing first-credential welcome overlay still fires exactly once.
5. **Given** needs-attention / recent-activity / other-context content exists, **When** Home loads, **Then** those bands render with their existing content and actions.

---

### User Story 3 - Navigating the wallet via the floating tab bar (Priority: P2)

From any wallet screen, the citizen uses a floating pill navigation bar to move between the four primary destinations. The active destination is highlighted and labelled; the others show icons only. The bar floats above the content rather than sitting flush at the screen edge.

**Why this priority**: Navigation is shell-wide and used on every screen, but it depends on the new visual primitives and is lower-risk to land after the home surface itself.

**Independent Test**: From Home, tap each navigation destination in turn and confirm the correct screen loads and the active destination is highlighted/labelled in the floating bar; confirm the bar is present on every primary wallet screen.

**Acceptance Scenarios**:

1. **Given** any primary wallet screen, **When** the citizen views the bottom of the screen, **Then** a floating pill bar shows four destinations: Home, Devices, Activity, Settings.
2. **Given** the citizen is on a destination, **When** they view the bar, **Then** the current destination shows a highlighted pill with its label and the others show icons only.
3. **Given** the floating bar, **When** the citizen taps a destination, **Then** the corresponding screen loads.
4. **Given** content that extends to the bottom of the screen, **When** rendered, **Then** content is not obscured by the floating bar.

---

### User Story 4 - Dark mode follows the citizen's theme preference (Priority: P2)

A citizen whose theme preference (or device setting) is dark sees the home — and the rest of the wallet — in the dark palette: a near-black page background, dark surfaces, light text, and the dark variant of the gradient hero. A citizen on light sees the light palette.

**Why this priority**: Dark mode is part of the design contract and is currently not honoured in the wallet, but it is independent of the home layout itself.

**Independent Test**: With the theme preference set to dark, open Home and confirm the dark page background, dark surfaces, light text, and dark-variant hero gradient; switch to light and confirm the light palette. Confirm both render without unreadable contrast.

**Acceptance Scenarios**:

1. **Given** a citizen with a dark theme preference, **When** Home loads, **Then** the page, surfaces, text, and hero use the dark palette.
2. **Given** a citizen with a light theme preference, **When** Home loads, **Then** the light palette is used.
3. **Given** a citizen with the "system" preference, **When** the device is in dark mode, **Then** the dark palette is used.

---

### User Story 5 - Consistent, accessible rendering across sizes and surfaces (Priority: P3)

The home renders correctly at phone and tablet widths without horizontal overflow or unintended scrolling at the default density. The card-stack and button-press motion respect a reduced-motion preference. Interactive elements are reachable and named for assistive technology. The shared visual components also mount cleanly where the main web app would host them.

**Why this priority**: Cross-size and accessibility correctness is essential for shipping but is a verification/hardening concern layered on the visual work.

**Independent Test**: Load Home at phone and tablet widths and confirm no horizontal scroll and all regions visible; enable reduced-motion and confirm card/button transitions are suppressed in favour of instant state changes; navigate the home with keyboard/screen-reader and confirm each action and tab has an accessible name; confirm the shared components render without error when hosted in the main web app at phone/tablet widths.

**Acceptance Scenarios**:

1. **Given** a phone-width viewport, **When** Home loads in the default density, **Then** all regions are visible with no horizontal overflow.
2. **Given** a tablet-width viewport, **When** Home loads, **Then** the layout scales without broken spacing or clipping.
3. **Given** a reduced-motion preference, **When** card-stack or button interactions occur, **Then** transforms/animations are replaced by instant state changes.
4. **Given** assistive technology, **When** the citizen reaches an action button, ghost card, or navigation tab, **Then** each exposes a descriptive accessible name.

### Edge Cases

- **Clock skew present**: the existing device-clock-skew warnings must still appear above the credential area under the new chrome.
- **Sign-in required / sync paused**: an amber sync-warning treatment appears only in that condition and is dismissible/persistent per the existing rules; its copy is sourced from a localisation resource so it can change without a rebuild.
- **Context switch**: switching organisation context refreshes the home (hero count, cards, bands) and dismisses any pending first-credential welcome, exactly as today.
- **Org with no additional memberships**: the org switcher in the hero is non-interactive (display-only) when the citizen holds only the Personal context.
- **Push-then-render race**: a credential arriving via push still triggers the existing refresh and welcome-eligibility checks under the new chrome.
- **Transient sync/notice failures**: a failed background sync or pending-notice fetch never blocks Home from rendering; feedback is shown inline, never via a toast.

## Requirements *(mandatory)*

### Functional Requirements

**Hero & header (US1, US2)**

- **FR-001**: The home MUST present a gradient hero region spanning the top of the screen, in light and dark variants, behind the header and headline content.
- **FR-002**: The hero MUST show an eyebrow, a headline, and a subtitle whose text reflects whether the wallet is empty ("welcome / empty") or active ("active wallet / credential count").
- **FR-003**: The hero MUST host a header row containing the organisation/context switcher, the notifications indicator (with unread badge), and a scan/present affordance, replacing the previous separate app bar on the home screen.
- **FR-004**: The organisation switcher, notifications indicator, and scan affordance MUST retain their existing behaviour (context switching, inbox drawer with live unread count, navigation to present/scan).

**Action buttons (US1, US2)**

- **FR-005**: The home MUST present two prominent action buttons — Present and Verify — as the primary action area.
- **FR-006**: Present MUST be visibly de-emphasised and non-actioning when the active context has no credentials, and enabled (navigating to the present flow) when at least one credential exists.
- **FR-007**: Verify MUST always be enabled and MUST open the verify flow.
- **FR-008**: Both action buttons MUST give immediate press feedback that does not depend solely on a CSS active state.

**Empty-state card stack (US1)**

- **FR-009**: When the active context has no credentials (and no waiting-state notice applies), the home MUST present a three-card "ghost" stack metaphor, with the top card inviting the citizen to add a credential.
- **FR-010**: The top ghost card MUST act as a tap-target that starts the same device-enrolment flow as the existing enrol affordance.
- **FR-011**: When the active context HAS credentials, the home MUST render the existing credential cards unchanged (no fanned/stacked credential treatment in this feature).

**Navigation shell (US3)**

- **FR-012**: The wallet MUST present a floating pill navigation bar with four destinations — Home, Devices, Activity, Settings — replacing the previous flush bottom navigation rail.
- **FR-013**: The active destination MUST be visually highlighted and labelled; inactive destinations MUST show icons only.
- **FR-014**: Page content MUST never be obscured by the floating navigation bar.
- **FR-015**: The navigation bar MUST appear consistently across the primary wallet screens (shell-level), not only on Home.

**Theme & tokens (US4)**

- **FR-016**: The wallet MUST render in the dark palette when the citizen's resolved theme preference is dark, and the light palette when it is light, including the page background, surfaces, text, and the hero gradient variant.
- **FR-017**: The design's page-background, surface, text, and brand-gradient values MUST be available as shared theme tokens so all redesigned components draw from a single source.

**Behaviour preservation (US2, cross-cutting)**

- **FR-018**: All existing home information bands — needs-attention, recent-activity, other-context peek, waiting state, and the first-credential welcome overlay — MUST continue to render and behave exactly as before, under the new chrome.
- **FR-019**: Existing device-clock-skew warnings MUST continue to appear in the credential area.
- **FR-020**: An amber sync-warning treatment MUST appear only when sign-in is required or sync is paused, with copy sourced from a localisation resource (not hard-coded), and follow the existing dismiss/persist rules.
- **FR-021**: All user feedback on the home MUST use the inline-feedback surface; the toast/snackbar surface MUST NOT be reintroduced.

**Accessibility & responsiveness (US5)**

- **FR-022**: The home MUST render without horizontal overflow at phone and tablet widths in the default density.
- **FR-023**: Card-stack and button-press motion MUST be suppressed (instant state change) when a reduced-motion preference is set.
- **FR-024**: Each interactive element — action buttons, ghost cards, navigation tabs, header affordances — MUST expose a descriptive accessible name.
- **FR-025**: The new shared visual components MUST mount without error when hosted by the main web application at phone/tablet widths.

### Key Entities

This feature introduces no new persisted data. It consumes existing state only:

- **Credential set (active context)**: drives empty-vs-populated layout and the Present enabled/disabled state. (No new fields required; per-credential accent/type/meta are explicitly out of scope.)
- **Active organisation context**: drives the hero switcher label and refresh-on-switch behaviour.
- **Notifications unread count**: drives the hero notifications badge.
- **Sync / sign-in state**: drives the amber sync-warning visibility.
- **Theme preference (light/dark/system)**: drives the palette.
- **Per-device welcome flag**: gates the existing first-credential welcome overlay (unchanged).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A citizen with no credentials sees the gradient hero, the three-card ghost stack, and the Present/Verify pair on first Home load, and can reach device enrolment in one tap from the home surface.
- **SC-002**: A citizen with credentials sees the active-wallet hero and their existing credential cards, and can reach the present flow in one tap, with zero loss of any pre-existing home band or behaviour.
- **SC-003**: All four navigation destinations are reachable from the floating bar on every primary wallet screen, with the active destination correctly highlighted 100% of the time.
- **SC-004**: The home renders with no horizontal overflow and no unintended scrolling at the default density at both a representative phone width and a representative tablet width.
- **SC-005**: Both light and dark palettes render the home with legible text/contrast and the correct gradient variant, selected by the citizen's theme preference.
- **SC-006**: Automated end-to-end checks for the home and navigation pass against the running stack with zero console errors and zero failed network calls in the happy path.
- **SC-007**: No toast/snackbar surface is introduced; the shared-component bundle remains free of disallowed assemblies.

## Assumptions

- **Direction**: Only the "Bolder" (B) direction is implemented; "Refined" (A) is reference only.
- **Reskin, not re-architecture**: Feature 125's home information architecture and all behaviour are preserved; this feature changes presentation only.
- **Primary surface is the wallet PWA** (mounted at `/wallet`). The shared components are built in the shared user-facing component library so the main web app could host them; no new web wallet-home page is created in this feature, and the web requirement is satisfied by a render-sanity check at phone/tablet widths.
- **Fixed presentation knobs**: comfy density, soft card style, and active-only tab labels are fixed; the design-exploration toggles (density/card-style/tab-labels, alternate palettes) are not exposed to users.
- **Populated card-stack graphics are deferred**: the fanned/accented populated stack and per-credential type code / accent colour / meta line (which would require sourcing issuer display data not currently carried on a cached credential) are explicitly out of scope for this feature.
- **Verification**: end-to-end coverage runs against the Docker stack; the home is validated at phone and tablet widths on the wallet surface, with a render-sanity check on the web surface.

## Out of Scope

- Populated/fanned credential card stack and per-credential accent/type/meta styling.
- Sourcing or displaying issuer display names beyond what existing cards already show.
- Any new citizen wallet-home page in the main web application.
- Changes to enrolment, presentation, verification, sync, or credential data flows (navigation targets are reused as-is).
- Per-tenant branding colour injection into the theme (the gradient token is introduced; dynamic per-tenant override is a later concern).
