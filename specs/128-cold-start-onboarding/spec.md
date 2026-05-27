# Feature Specification: Cold-start onboarding and device pairing UX

**Feature Branch**: `128-cold-start-onboarding`
**Created**: 2026-05-16
**Status**: Draft
**Input**: User description: "Cold-start onboarding and device pairing UX. Builds on the design memo at docs/superpowers/specs/2026-05-16-cold-start-onboarding-design.md (PR #726). Four citizen routes outside the F126 council-page gate: (1) app-store cold landing at sorcha.dev/get, (2) desktop-to-phone post-signup gate with QR + dismissable Skip + persistent menu item, (3) mobile-web-to-same-phone-PWA install with start_url-baked token and 6-digit short-code fallback, (4) in-PWA unpaired full-page takeover. One enrol-session token primitive (extends F126 endpoint with mode: gated | standalone), one redeem URL (Enrol.razor), copy varies on mode echoed in response. Pairing is treated as first-time setup — citizen must be cognisant. Read the design memo for full context."

## User Scenarios & Testing *(mandatory)*

### User Story 1 — In-PWA pairing takeover for the unpaired signed-in citizen (Priority: P1)

Sarah is signed in to the Sorcha Wallet PWA on a phone that has no paired device for her account yet (e.g., she signed up from this same phone in mobile Safari and then installed the PWA, or she signed in to a fresh PWA install where seamless pairing didn't fire). She opens the PWA and is presented with a single full-page surface that explains this phone must be paired before it can hold credentials, with one obvious action to set it up. There is no navigation, no skip, and no other content. After completing the pairing ceremony, the takeover dismisses and the normal wallet experience appears.

**Why this priority**: This is the operator-flagged irritant — today the only way for an unpaired signed-in PWA user to pair is to discover `Settings → Enrol this device`. Without this story, every citizen who reaches the PWA signed-in-but-unpaired is stuck. It also unblocks the fallback paths of Stories 2 and 3.

**Independent Test**: Provision a citizen account with no paired devices, sign in to a fresh PWA install, confirm the full-page takeover appears, complete the setup action, and confirm the takeover dismisses and the normal wallet appears with the device now listed as paired.

**Acceptance Scenarios**:

1. **Given** a signed-in citizen with zero paired devices, **When** the PWA launches, **Then** a full-page pairing takeover replaces all wallet UI and offers a single primary action to set up this device.
2. **Given** the takeover is showing, **When** the citizen completes the pairing ceremony, **Then** the takeover dismisses and the normal wallet Home appears with this device listed.
3. **Given** the takeover is showing, **When** the citizen attempts to navigate elsewhere in the PWA, **Then** they cannot — the takeover blocks all other interaction.
4. **Given** the takeover is showing, **When** another device for the same citizen completes pairing (e.g., via Story 2 elsewhere), **Then** the takeover dismisses automatically without the citizen acting on it locally.
5. **Given** the takeover is showing, **When** the citizen expands a secondary affordance and enters a valid pairing short code, **Then** the citizen is paired to the underlying session and the takeover dismisses.

---

### User Story 2 — Desktop sign-up to phone pairing (Priority: P1)

Sarah signs up for a Sorcha account on her laptop. Immediately after signup completes, the web surface presents a dedicated full-page handoff: a large QR code she can scan with her phone camera, an option to email herself a link to come back to this later, and an option to skip for now. If she scans, her phone opens the Sorcha Wallet PWA (either already installed or after an install prompt), the wallet pairs to her account, and she sees confirmation on both surfaces. If she skips, she lands on the web with a persistent reminder banner offering to take her back to the handoff, and a menu entry under her devices area that does the same.

**Why this priority**: Desktop is a primary signup surface and credentials cannot land on a desktop browser, so getting the wallet onto a phone is the natural completion of signup. Without this story, desktop signups become dead-ends.

**Independent Test**: From a desktop browser, complete sign-up; confirm the handoff appears; on a phone, scan the QR; confirm the wallet pairs and both surfaces show success. Separately, repeat and click Skip; confirm the persistent banner and menu entry both return the citizen to the handoff.

**Acceptance Scenarios**:

1. **Given** a citizen who has just completed sign-up on a non-PWA surface with zero paired devices, **When** sign-up completes, **Then** the citizen is taken to a dedicated handoff page that surfaces a phone-pairing QR.
2. **Given** the handoff page is showing, **When** the citizen scans the QR on a phone with the wallet PWA installed, **Then** the phone opens the wallet, the device pairs, and both surfaces reflect success within seconds of the pairing completing.
3. **Given** the handoff page is showing, **When** the citizen chooses "Email me a link," **Then** an email is dispatched to her account email containing a link that, when followed, reopens the handoff page in an authenticated session.
4. **Given** the handoff page is showing, **When** the citizen chooses "Skip for now," **Then** she lands on the web with a persistent dismissable banner that links back to the handoff.
5. **Given** the citizen has skipped and is on the web with zero paired devices, **When** she opens the devices area, **Then** an "Add my phone" entry is present and routes her back to the handoff page.
6. **Given** the citizen has at least one paired device, **When** the next sign-in completes, **Then** she is NOT routed to the handoff page (only first-time / zero-device citizens are auto-routed).

---

### User Story 3 — Mobile-web sign-up to same-phone PWA install (Priority: P2)

Sarah signs up on her phone in mobile Safari or mobile Chrome. The handoff page detects that this browser can install the wallet PWA and presents an "Install Sorcha Wallet" action instead of a QR (since scanning her own screen is impossible). On supported platforms, completing the install opens the PWA already paired to her account, with no further action. On platforms where the install handoff is lossy, the handoff page also shows a 6-digit pairing short code; she opens the just-installed PWA, lands in the Story 1 takeover, enters the short code, and is paired.

**Why this priority**: Mobile-web sign-up is a real path (e.g., from a link shared by a friend) but is less common than desktop sign-up. The fallback path (Story 1 takeover + short code) is already provided by Story 1, so this story is fundamentally a UX improvement on an existing path rather than blocking a new one.

**Independent Test**: From a mobile browser, complete sign-up; confirm the handoff renders the install variant with the short-code fallback visible; install the wallet; confirm either the seamless paired-on-launch experience or the takeover-plus-short-code path completes pairing.

**Acceptance Scenarios**:

1. **Given** a citizen completing sign-up on a mobile browser that can install the wallet PWA, **When** the handoff appears, **Then** it renders an install-flavoured variant ("Install Sorcha Wallet") rather than the QR variant.
2. **Given** the install-flavoured handoff is showing on a platform that preserves the pairing token across install, **When** the citizen completes the install and opens the wallet, **Then** the wallet is already paired and the takeover does not appear.
3. **Given** the install-flavoured handoff is showing on a platform where the install handoff is lossy, **When** the citizen completes the install and opens the wallet, **Then** the takeover appears, she enters the short code shown on the handoff page, and pairing completes.
4. **Given** the install-flavoured handoff is showing, **When** the citizen does not install and instead returns to the web later, **Then** the persistent banner and "Add my phone" menu entry from Story 2 still apply.

---

### User Story 4 — App-store cold landing (Priority: P3)

Someone discovers the Sorcha Wallet via an app-store listing and lands on the marketing landing page at `sorcha.dev/get` without any prior context. The page explains that Sorcha is used by specific services (e.g., a council), invites the visitor to find a service they belong to, and offers a sign-in path for visitors who already have an account. Visitors who sign in are routed through the Story 2 / Story 3 handoff if they have no paired devices.

**Why this priority**: PWA cold discovery is not a primary acquisition channel for Sorcha (citizens reach Sorcha through services that use the platform). The landing exists to handle the case gracefully if app-store visitors arrive, not to optimize that path.

**Independent Test**: Visit `sorcha.dev/get` with no prior session; confirm the landing renders an explanation, a "find a service" link, and a sign-in entry; sign in as a citizen with zero paired devices and confirm the system routes them into the Story 2 / Story 3 handoff.

**Acceptance Scenarios**:

1. **Given** an unauthenticated visitor, **When** they navigate to the cold landing URL, **Then** they see Sorcha explained as a wallet for services that use the platform, plus a way to find services and a sign-in entry.
2. **Given** a visitor who signs in from the cold landing and has zero paired devices, **When** sign-in completes, **Then** they are routed into the handoff from Story 2 / Story 3 (not directly into an empty wallet view).

---

### Edge Cases

- **PWA-installable detection misfires on mobile-web.** If the handoff misclassifies a mobile-browser as desktop and shows a QR, the citizen cannot scan their own screen. The handoff MUST also show the short-code fallback inline on any mobile-installable variant so the citizen always has an out.
- **`mode=gated` token misuse.** If a council-page-flavoured pairing token is somehow redeemed via a generic handoff (or vice versa), the system MUST reject the redeem with a clear error and log the attempt — it MUST NOT silently downgrade between modes.
- **Pairing token already redeemed.** If a citizen tries to redeem an enrol-session token or short code more than once, the second attempt MUST fail with a clear "this code has already been used" error, not a generic failure.
- **Pairing token expired.** Tokens and short codes have a bounded lifetime; expired redemption attempts MUST tell the citizen to return to the handoff and try again.
- **Citizen with multiple devices triggers the handoff again.** Story 2's auto-routing only fires on citizens with zero paired devices; subsequent signups/sign-ins are unaffected. "Add my phone" remains available manually.
- **Citizen completes pairing on Device B while the takeover is open on Device A.** The takeover on Device A MUST dismiss automatically when any device finishes pairing for the same account.
- **Pairing ceremony fails midway.** The takeover / handoff MUST surface a recoverable error and allow the citizen to retry without restarting the whole flow.
- **Email "send me a link" rate-limit abuse.** The email handoff MUST be rate-limited per account and per IP to prevent spam loops.
- **`Settings → Enrol this device` discoverability.** This entry remains available as a power-user / re-pair option but MUST NOT be the only pairing affordance accessible to a signed-in citizen.

## Requirements *(mandatory)*

### Functional Requirements

#### Unified pairing primitive

- **FR-001**: The system MUST extend the existing pairing-token mint endpoint to accept a `mode` field with two values: `gated` (default, preserving today's council-page flow) and `standalone` (for the four routes in this feature).
- **FR-002**: The pairing-token redeem response MUST echo the token's `mode` so the redeem UI can choose the appropriate copy and post-pair destination.
- **FR-003**: The system MUST enforce that a `gated` token cannot be redeemed without its expected gating context (e.g., a return URL) and that a `standalone` token never carries a return-URL effect — mode/context mismatch MUST be rejected, not silently coerced.
- **FR-004**: All four routes (Stories 1–4) MUST flow through this single primitive — no parallel pairing token shapes are introduced.

#### In-PWA pairing takeover (Story 1)

- **FR-010**: The wallet PWA MUST detect at launch whether the signed-in citizen has any paired device for their account and MUST render a full-page pairing takeover if they have zero.
- **FR-011**: The takeover MUST block access to all other PWA surfaces (navigation, content, settings) until pairing succeeds.
- **FR-012**: The takeover MUST offer one primary action that initiates the device-pairing ceremony in-place using the citizen's existing PWA session (no token redeem is needed for the same-device case).
- **FR-013**: The takeover MUST offer a secondary affordance to enter a short code (for the Story 3 fallback and cross-device-handoff-to-this-device path), redeemable via a short-code redeem endpoint.
- **FR-014**: The takeover MUST dismiss automatically when pairing succeeds locally OR when any device pairs to the same account remotely (signaled via the existing real-time device-enrolment event).

#### Desktop / cross-device handoff (Story 2)

- **FR-020**: After first successful sign-up on a non-wallet-PWA surface, if the citizen has zero paired devices the system MUST route them to a dedicated handoff page.
- **FR-021**: The handoff page MUST present a QR code encoding the wallet PWA's redeem URL with a freshly minted `standalone` pairing token.
- **FR-022**: The handoff page MUST offer an "Email me a link" action that dispatches an email to the citizen's account address containing an authenticated resumption link to the handoff page; the email path MUST be rate-limited.
- **FR-023**: The handoff page MUST offer a "Skip for now" action that dismisses the page and lands the citizen on the web wallet surface.
- **FR-024**: When a signed-in citizen has zero paired devices and is on the web wallet surface, the system MUST surface a persistent dismissable banner offering to take them back to the handoff page.
- **FR-025**: The existing devices area MUST surface an "Add my phone" entry that opens the handoff page; this entry MUST remain available regardless of how many devices the citizen has paired.
- **FR-026**: The handoff page MUST NOT auto-route citizens who already have at least one paired device — auto-routing is a first-time setup behaviour only.

#### Mobile-web install handoff (Story 3)

- **FR-030**: The handoff page MUST detect whether the requesting browser can install the wallet PWA and, when it can, render an install-flavoured variant in place of the QR variant.
- **FR-031**: The install-flavoured variant MUST attempt a seamless pairing path — installing the wallet from this handoff carries the pairing token into the just-installed PWA so the wallet can redeem on first launch without further citizen action.
- **FR-032**: The install-flavoured variant MUST also display a short code that the citizen can read off this screen and enter into the wallet's takeover (Story 1 secondary affordance) on platforms where the seamless path is lossy — the short code MUST be visible without requiring extra interaction.
- **FR-033**: The system MUST mint short codes via a dedicated endpoint that pairs a 6-digit code with an underlying `standalone` pairing token, with a bounded TTL and single-use enforcement.

#### App-store cold landing (Story 4)

- **FR-040**: The system MUST host a public landing page at `sorcha.dev/get` (final path may evolve during implementation) that explains Sorcha is used by participating services, offers a way to find services, and offers a sign-in entry.
- **FR-041**: When a visitor signs in from the cold landing and has zero paired devices, the system MUST route them into the Story 2 / Story 3 handoff rather than into an empty wallet view.

#### Cross-cutting

- **FR-050**: All pairing-token redemptions (token, short-code) MUST be single-use; replay attempts MUST fail with a clear "this code has already been used" error.
- **FR-051**: All pairing-token redemptions MUST have a bounded lifetime; expired attempts MUST surface a clear "this code has expired, please return and try again" error.
- **FR-052**: Pairing-ceremony failures mid-flow MUST surface a recoverable error and allow the citizen to retry the pairing step without restarting sign-up.
- **FR-053**: The pairing surfaces (takeover, handoff page, banner, menu entry) MUST emit telemetry sufficient to graph per-route mix, per-route skip rates, per-route success rates, and short-code-fallback usage rates.
- **FR-054**: `Settings → Enrol this device` MUST remain available as a power-user / re-pair entry point but MUST NOT be the only pairing affordance accessible to a signed-in unpaired citizen.

### Key Entities

- **Enrol-session token**: The unified pairing token, single-use, time-bounded, carries a `mode` discriminator (`gated` for council-page flows, `standalone` for the four routes in this feature). Mints from the existing pairing-token mint endpoint; redeems at the wallet PWA's pairing URL.
- **Short code**: A 6-digit human-typeable handle paired with an underlying `standalone` enrol-session token, with a shorter TTL than the token itself, single-use. Used as the resilience path for Story 3 and the cross-device-handoff-to-this-device path in Story 1.
- **Device pairing**: The existing concept of a per-device delegation tied to the citizen's account (cryptographic mechanics unchanged from F114). Pairing succeeds when a device completes the ceremony and the citizen's device list contains it.
- **Has-paired-device probe**: A shared signal both web and PWA surfaces consult to decide whether to surface the takeover (PWA), the auto-routed handoff (web), the persistent banner (web), and the "Add my phone" menu entry visibility. Reacts to the real-time device-enrolment event so the surfaces dismiss instantly on pair-success.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A signed-in citizen who lands on the PWA with no paired device on this hardware reaches a paired state in under 30 seconds of opening the PWA on the happy path, with no navigation away from the wallet.
- **SC-002**: A citizen who completes sign-up on a desktop browser reaches a paired phone within 2 minutes when the phone is available, measured from sign-up success to first pair confirmation.
- **SC-003**: At least 80% of citizens who sign up on a non-PWA surface arrive at a paired-device state in the same session (i.e., do not click "Skip for now" and never return). The 80% target is set against a planning assumption and is revisited after the first 30 days of production telemetry.
- **SC-004**: Zero citizens reach the wallet PWA signed in but unpaired and have no on-screen path to pair other than `Settings → Enrol this device`. Verified by walkthrough of all four routes plus a code-level check that the takeover renders on the zero-device condition.
- **SC-005**: Pairing telemetry distinguishes all four routes (cold, desktop-to-phone, mobile-web-to-same-phone, in-PWA-takeover) plus the `gated` vs `standalone` mode split, allowing the operator to graph mix and skip-rate per route within the first week post-launch.
- **SC-006**: The mobile-web install handoff's short-code fallback usage rate is below 50% on supported mobile platforms within the first 30 days post-launch (i.e., the seamless path works at least half the time on supported devices); if not, the seamless path is treated as broken and removed in favour of always-short-code on that platform.
- **SC-007**: No `gated` pairing token is ever successfully redeemed via the standalone copy path, and vice versa, across the first 30 days of production — verified by telemetry on the mode/context mismatch reject counter being non-zero on attempts but zero on successful coercions.
- **SC-008**: The existing F126 council-page cold-start success rate does not regress after this feature ships — measured as the same conversion rate from council-page-gate-visit to paired device, ± 2 percentage points.

## Assumptions

- The wallet PWA is the only device type that can hold credentials in this scope. Desktop browsers and web wallet surfaces are management views, not holder targets. This matches the F114 design (server-anchored holder key + per-device delegation) and is the basis for treating "no paired phone after sign-up" as a blocker on usefulness.
- The existing F126 pairing-token mint and redeem endpoints, and the existing F114 device-pairing ceremony, are the building blocks. This feature extends those — it does not replace them.
- Citizens have an email address bound to their account (F116 invariant) — the "email me a link" handoff path can rely on this.
- The persistent real-time device-enrolment event used to dismiss the takeover (Story 1, Scenario 4) already exists from F126.
- The marketing landing's service listing is single-service (Strathcarron) for the foreseeable future; expansion is out of scope here.
- The system's authenticated session can detect "first sign-up" vs "subsequent sign-in" reliably enough to gate the auto-route from FR-020 / FR-026.

## Out of Scope

- The Sorcha account model itself (email/password anchor, social/passkey alternatives) — unchanged.
- The cryptographic device-pairing ceremony — unchanged.
- F126 council-page cold-start — preserved back-compatibly, not redesigned.
- Multi-device threat model and revocation — reuses existing limits, no new policy.
- Cross-context persona handling (F125) — pairing is per-account, not per-context, and this feature does not alter that.
- "Lost my phone" recovery — separate backlog item; not addressed here.
- Native iOS/Android app shells — PWA-only.
- Push notifications.
- A consumer-facing service directory beyond the single Strathcarron entry on the cold landing.

## Risks

- **Same-device install + QR is impossible.** Story 3's correctness depends on reliable PWA-installable detection on the mobile browser. The short-code fallback (FR-032) is the safety net; getting detection wrong without the fallback visible would strand mobile-web sign-ups.
- **Skip-then-never-pair rate could be high.** The Story 2 handoff is dismissable; the persistent banner is the mitigation. SC-003's 80% target may need recalibration after launch. If the rate climbs past planning thresholds, revisit whether the desktop handoff should also be non-dismissable.
- **Mode discriminator drift.** A bug that allows a `gated` token to be redeemed in a standalone context (or vice versa) silently bridges two flows with different audit and UX expectations. FR-003 + SC-007 are the explicit guardrails.
- **Telemetry gaps hide route-level regressions.** Without per-route telemetry (FR-053, SC-005) the operator cannot see if one of the four routes silently broke; this risk is mitigated by making telemetry a first-class requirement, not an afterthought.

## Dependencies

- F126 (council-page cold-start) for the existing pairing-token mint/redeem primitive being extended here.
- F114 (per-device delegation + server-anchored holder key) for the underlying pairing ceremony.
- F116 (account model + return-URL allowlist) for the sign-up entry points that this feature attaches to.
- F112 (transactional email facade) for the "email me a link" handoff in FR-022.
- The persistent real-time device-enrolment event from F126, for Story 1's automatic takeover dismissal.
