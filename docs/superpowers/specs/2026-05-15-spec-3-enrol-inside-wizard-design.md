# Spec 3 — Enrolment inside a council application wizard

**Date:** 2026-05-15
**Status:** Design locked — awaiting plan-phase.
**Umbrella:** [`2026-05-13-strathcarron-citizen-arc.md`](2026-05-13-strathcarron-citizen-arc.md)
**Spec 1 precedent:** [`2026-05-13-spec-1-assured-identity-on-pwa-design.md`](2026-05-13-spec-1-assured-identity-on-pwa-design.md)
**Spec 2 precedent:** [`2026-05-14-spec-2-sorcha-wallet-user-agent-design.md`](2026-05-14-spec-2-sorcha-wallet-user-agent-design.md)

## Purpose

Take a citizen from "I clicked an Apply button on a council page" to "I'm filling out the form, with a Sorcha account and an enrolled wallet device ready to receive whatever this council issues." Designed so that:

- Cold-start citizens (no Sorcha account, no PWA, no device) get onboarded as a side-effect of the council form they came for — not a separate "set up your wallet first" detour.
- Returning citizens see zero friction beyond a sign-in screen.
- The capability shows up wherever a council places an application form. The library houses the gate; councils consume it.

This is the third sub-spec of the Strathcarron citizen arc. Spec 1 (Feature 124) gave the PWA a credential to receive; Spec 2 (Feature 125) gave the PWA the full user-agent shape. Spec 3 is the front door — without it, every citizen has to discover the wallet some other way, which the umbrella expressly rejects.

## Decisions captured (brainstorm summary)

The 2026-05-15 brainstorm settled five decisions. Each is restated below as a locked premise of this spec.

| # | Decision | Where in spec |
|---|----------|---------------|
| 1 | **Placement is preflight for first-time citizens, no-op for returning citizens.** Wallet acquisition happens before the form, not after. Returning citizens (Tier 1) skip the gate entirely. | §2, §3 |
| 2 | **Wallet is mandatory in v1.** No email-pickup fallback. The wallet is the only credential delivery surface. | §2, §11 |
| 3 | **Web-first signup with signed-in QR for enrolment.** Account creation happens on the council page (existing Feature 116 flow). The QR/link that follows carries a short-lived one-time auth token so the PWA opens already-authenticated. | §4 |
| 4 | **Web-shell submission in v1; PWA-hosted form continuation is a deliberate future evolution.** The council page owns the form; the PWA is the delivery surface. The capability to "continue on your phone" stays available for Spec 4/5 to exercise once the F125 `ApplicationInstance.razor` form host matures. | §6 |
| 5 | **Hybrid SignalR with polling fallback** for cross-device "phone enrolled" coordination. Same philosophy as the umbrella's Decision #3 hybrid QR — one mechanism, two resolution paths. | §5, §8 |

## §1 — What ships

Three citizen tiers, three flows, one library-housed gate component the council page consumes.

### Tier 1 — Returning citizen (account ✓, device ✓)

The desired-state path. Sign in, fill, submit, watch wallet.

### Tier 2 — Mini-gate (account ✓, device ✗)

Edge case but real. Signed up but never enrolled, or lost their phone. Sign in, see the hybrid QR/link/paste, enrol, then fill the form.

### Tier 3 — Cold-start (account ✗, device ✗)

The umbrella's defining beat. Preflight signup → enrol → form → submit → watch wallet. Once per citizen per arc.

### Library component + Tenant Service surface

- **`EnrolGateComponent`** in `Sorcha.UI.Components.User` — composes signup + hybrid QR + tier-aware copy. Drop-in for any council page that wants the gate.
- **Two new Tenant Service endpoints** — `POST /api/auth/enrol-session` and `POST /api/auth/enrol-session/redeem` for the QR token mechanics.
- **One new `TenantHub` event** — `DeviceEnrolled(platformUserId, deviceId)` so the council page transitions instantly when the phone completes enrolment.
- **One small extension to existing Feature 116 signup endpoints** — `?returnTo=` query parameter so signup can route back to the council page cleanly.

## §2 — The cold-start preflight, step by step

Sarah arrives at `https://strathcarron.gov/services/driving-licence`. Not signed in, no Sorcha account, no PWA.

### Step 1 — Preflight page

Council page renders the gate's Tier 3 surface:

- **"Sign in or create your account"** — primary affordance. Single button → existing Feature 116 signup/sign-in flow (email/password, social, or passkey). The `returnTo` query parameter carries the council page URL so signup redirects back here on success.
- **"What is this?"** — secondary affordance. Short plain-English explainer with the umbrella's Sorcha Wallet positioning.

No QR yet. The wallet conversation only starts after the user has committed to creating an account. Avoids "scan this QR" without context, which is the classic phishing red flag.

### Step 2 — Signup completes; redirect back

Sarah completes the F116 signup flow. Council page knows she's back via the OAuth-style redirect with her session JWT. Now signed in, zero devices.

### Step 3 — Wallet enrolment gate

Tier evaluates to 2 (account ✓, device ✗) — render the hybrid QR/link/paste:

- **QR code** with "Sorcha Wallet" tagline below. Scan with phone camera.
- **"Open on this device"** tap-able link. For same-device mobile users; renders prominently when `MediaQueryService.IsMobile` is true.
- **"Copy link"** button as third fallback. For "I'll set this up later on my work laptop" or accessibility paths.

All three resolve the same URL:

```
https://strathcarron.gov/wallet/enrol?session=<one-time-token>
```

Below the QR: a status line — **"Waiting for your phone…"** — which flips to **"Phone ready ✓ — continuing"** when the SignalR `DeviceEnrolled` event fires.

### Step 4 — Device enrolled; back-channel notification

PWA completes the F114 enrolment ceremony. Tenant Service raises `DeviceEnrolled` on `TenantHub`. Council page subscribed to the per-platform-user group; transitions to Step 5.

### Step 5 — Continue with the application

Council page resumes the application form. Sarah fills, submits.

### Step 6 — Watch your wallet

Council page success screen: **"Your application is in. Watch your wallet for the credential — it usually arrives within a few seconds."** Hub event from the wallet service's credential push (Feature 114 / US4) is also observable from the council page, so the success screen transitions to **"Credential received ✓ — open your wallet to see it"** when the credential lands. PWA's F124 welcome takeover fires on the phone simultaneously.

Three steps where the user actually does something (signup, phone enrolment, form completion). Steps 4 and 6 are passive transitions.

## §3 — Returning citizen + mini-gate flows

### Tier-detection mechanic

The council page's entry-point probes:

```
GET /api/auth/whoami       → 200 {platformUserId} if signed in, 401 otherwise
GET /api/me/devices        → active-device list ([] = no devices)
```

Both endpoints exist today. The gate decides:

| `/whoami` | `/me/devices.Count` | Tier | Render |
|---|---|---|---|
| 401 | — | 3 (cold-start) | Preflight signup screen (§2 Step 1) |
| 200 | 0 | 2 (mini-gate) | Hybrid QR/link/paste — no signup involved |
| 200 | ≥1 | 1 (fast path) | Form |

### Tier 1 — Returning citizen

One sign-in screen (existing F116), then straight to the form. No QR, no "you'll need a wallet" copy. When she submits, the credential lands in her existing PWA via the F124 SorchaLocalWallet path; success page shows the standard "Watch your wallet" hint.

**Total user steps from arrival to form: sign in.**

### Tier 2 — Mini-gate

Sign in. Wallet enrolment gate (same hybrid QR as Tier 3 Step 3, no signup). SignalR `DeviceEnrolled` fires; form continues.

**Total user steps: sign in + enrol device.**

### Tier-specific copy

Tier 2 cannot say "you'll need a wallet for this" the way Tier 3 does — Sarah might already have a Sorcha Wallet account on another (lost) device. The mini-gate copy is closer to **"Let's pair this device with your wallet."** Tier-aware copy is a load-bearing UX call — designed into the gate component, not bolted on by the consuming page.

### Tier transitions are one-way

A citizen who hits Tier 3 today is Tier 1 tomorrow. No "you've graduated" ceremony. Each visit probes the two endpoints and renders the right thing.

## §4 — The QR / session-token mechanic

### Token shape

Short-lived JWT signed by Tenant Service:

```json
{
  "sub": "<platformUserId>",
  "scope": "enrol",
  "jti": "<uuid>",
  "iat": <epoch>,
  "exp": <iat + 600>
}
```

10-minute TTL. Signed with the same Tenant Service signing key the existing auth JWTs use. One-time use enforced by Redis `SET NX` on the JTI at redeem.

### Token redemption

PWA loads with `?session=<token>` in URL. PWA POSTs the session token to:

```
POST /api/auth/enrol-session/redeem
```

Tenant Service validates signature + expiry + scope, atomically marks `jti` consumed in Redis, returns `{ accessToken, expiresIn, displayName, email }`. PWA stores the token in `IAccessTokenStore` and proceeds with the existing F114 enrolment ceremony.

The `displayName` + `email` fields feed the redeem-confirmation dialog (see §7 — friend-scans-by-mistake mitigation).

### Token minting

Council page calls a separate endpoint at Step 3 to mint the session token:

```
POST /api/auth/enrol-session
→ { sessionToken, qrUrl, expiresAt }
```

Auth required (the caller must already be signed in). Mints a token bound to the caller's PlatformUserId.

### Why no `?form=` parameter

In v1 cold-start, the PWA doesn't need to know about the form. The PWA enrols; the council page (which has the form open) reacts to `DeviceEnrolled`. URL stays clean: `https://strathcarron.gov/wallet/enrol?session=<token>`. The `?form=` evolution is reserved for Spec 4/5 when "continue on your phone" lights up.

## §5 — Cross-device coordination

### Hub event

```
TenantHub.DeviceEnrolled(Guid platformUserId, Guid deviceId)
```

Raised by `PlatformUserDeviceService.RegisterAsync` after a new active device lands. Published to the per-platform-user group via the existing `TenantHubGroups.User(platformUserId)` builder.

### Subscription on the council page

Council page joins the per-user group via the existing `TenantHubConnection` shim. On `DeviceEnrolled`, transitions out of the waiting state.

### Polling fallback

If `TenantHubConnection.StartAsync()` doesn't establish within 2 s, the gate falls back to polling `GET /api/me/devices` every 3 s for up to 60 s. After 60 s of polling failure, the gate surfaces a manual recovery affordance — **"Trouble hearing from your phone — tap *I've enrolled* to continue, or refresh this page"**.

Both paths are equivalent functionally; SignalR is the smoother polish.

## §6 — Web-shell submission; PWA-hosted continuation is a future capability

The application form lives in the council page (`Sorcha.UI.Web.Client`). Submission happens on the web shell. The wallet is the delivery target.

The F125 `ApplicationInstance.razor` exists in the PWA today as a stub for a future evolution where the citizen chooses to continue on their phone. Spec 3 v1 deliberately does NOT exercise that path — the cold-start gate's job is to get the citizen a wallet, not to relocate the form. The capability stays available for Spec 4/5 to light up when the application catalogue API + persona autofill wiring matures.

This keeps Spec 3's scope tight and avoids two parallel form-submission paths in v1.

## §7 — Failure paths

| What goes wrong | Council-page behaviour | PWA behaviour |
|---|---|---|
| **Session token expires** (10 min, citizen distracted) | Status line flips to **"QR expired — let's get you a new one"** with a regenerate button that calls `POST /api/auth/enrol-session` again | If PWA opens an expired token URL, render an `ErrorRecoveryScaffold` (Feature 125 PR-F) — "This link has expired. Open the council page on your other device for a new code." |
| **Session token already consumed** (race condition, PWA reload) | No change — still waiting on `DeviceEnrolled` | If redeem fails with `already_used`, the PWA already holds a citizen JWT — proceed to enrolment if not already enrolled, or surface "you're already set up" if enrolled |
| **PWA install dismissed** | Status line stays "Waiting for your phone…" | Browser PWA loads; user dismisses the "add to home screen" prompt — in-browser session works fine for enrolment, only the home-screen icon defers |
| **Phone offline during enrolment** | Stuck on waiting | PWA surfaces `ErrorRecoveryScaffold` — "Couldn't reach Sorcha — check your connection"; retry on reconnect |
| **SignalR fails to connect within 2 s** | Silently falls back to polling — no UX change | n/a |
| **Polling fails too (60 s of no device seen)** | Status flips to manual recovery — **"Trouble hearing from your phone — tap *I've enrolled* to continue, or refresh this page"** | n/a |
| **User abandons signup** (closes browser at signup screen) | Next visit: not signed in → Tier 3 path from scratch | n/a |
| **User abandons enrolment** (signed up, never enrolled) | Next visit: signed in + 0 devices → Tier 2 mini-gate | n/a |
| **Council form was halfway filled** before the gate fired | Form state persists in browser `sessionStorage` — restored after the gate clears | n/a |
| **Two browser tabs open concurrently** for the same Sorcha account | Each tab's `TenantHubConnection` subscription receives the event — both transition. Idempotent. | n/a |
| **Friend scans the QR by mistake** | Council page transitions to "Phone ready ✓" as soon as `DeviceEnrolled` fires for Sarah's platformUserId — regardless of whose physical phone got enrolled. The **PWA-side confirmation dialog before redeem** is the mitigation; see below. | PWA renders **"You're about to enrol this device for `sarah@example.com` (Sarah Example). If that's not you, close this page."** Confirmation gate before the redeem completes; cancel returns to the friend's existing PWA state with no side-effects. |

### Friend-scans-by-mistake — the load-bearing concern

The session token is a bearer credential; anyone who scans it gets a JWT for `sub: <originalUserId>`. The v1 mitigation: **confirmation dialog inside the PWA before redeem completes**, surfacing the email and display-name the token resolves to. Combined with 10-minute TTL and one-time JTI, that's the v1 trust model.

Future hardening (not v1): bind the token to the originating browser's session via a server-set HttpOnly cookie presented alongside the token at redeem. Breaks "scan with a friend's phone" recovery, which the umbrella doesn't currently want to support but might in a later spec.

## §8 — New server surface

### Two new Tenant Service endpoints

| Method | Path | Purpose |
|---|---|---|
| `POST` | `/api/auth/enrol-session` | Mint a new one-time enrolment session token for the signed-in caller. Returns `{ sessionToken, qrUrl, expiresAt }`. |
| `POST` | `/api/auth/enrol-session/redeem` | Redeem a session token for a full citizen JWT. Atomic single-use via Redis `SET NX` on JTI. Returns `{ accessToken, expiresIn, displayName, email }`. |

Both rate-limited per the existing `RateLimitPolicies.PlatformAuth` standard.

### One new hub event on existing `TenantHub`

| Event | Payload | Trigger |
|---|---|---|
| `DeviceEnrolled` | `(Guid platformUserId, Guid deviceId)` | `PlatformUserDeviceService.RegisterAsync` success; published to per-user group |

### One extension to existing Feature 116 signup endpoints

`?returnTo=<url>` query parameter on the signup/sign-in routes. On success, redirect to the supplied URL instead of the default dashboard. Validated against an allowlist of trusted return domains — no open redirects.

That's the full new server surface. Composes with existing F116 (signup), F114 (device enrolment), F118 (hubs). No new microservices.

## §9 — Library component growth

### `EnrolGateComponent` in `Sorcha.UI.Components.User`

Single component that composes signup + hybrid QR + tier-aware copy. Drop-in for any council page that wants the gate.

```razor
<EnrolGateComponent CouncilName="Strathcarron Council"
                    OnReady="@HandleCitizenReady" />
```

Internally evaluates the citizen tier (via `/whoami` + `/me/devices` probes), renders the right surface (signup / mini-gate / fast-through), handles the hub event subscription, manages the session-token lifecycle. The consuming page's only job is to render `EnrolGateComponent` and react to `OnReady`.

### Sub-components

- `EnrolGate.SignupGate` — Tier 3 preflight surface.
- `EnrolGate.WalletPairingGate` — Tier 2 mini-gate (and the post-signup step of Tier 3).
- `EnrolGate.HybridQrAffordance` — the QR + tap-link + copy-link surface. Reusable in other contexts; placed in the library for that reason.
- `EnrolmentRedeemConfirmDialog` (in `Sorcha.Wallet.Pwa`) — PWA-side confirmation surface for the friend-scans-by-mistake path.

### What's PWA-local vs library-shared

PWA-local:
- `EnrolmentRedeemConfirmDialog` (PWA-specific UX moment)
- The session-token redeem call (PWA-specific HTTP client)

Library-shared:
- Everything in `EnrolGateComponent` and its sub-components
- The tier-detection mechanic
- The hub-subscription wiring (consumes the existing `TenantHubConnection`)

## §10 — Testing strategy

| Layer | Coverage |
|---|---|
| **Tenant Service unit** | `EnrolSessionService.MintAsync` (token claims, TTL, signature). `RedeemAsync` (single-use via Redis SET NX, expired rejected, scope-mismatch rejected, consumed-token rejected on re-redeem). |
| **Tenant Service integration** | `POST /api/auth/enrol-session` (auth required, valid JWT structure). `POST /api/auth/enrol-session/redeem` (happy path, replay → 409, expired → 410). |
| **Hub event** | `TenantHubTests.DeviceEnrolled` — group filtering, payload shape. |
| **Council-page component** | `EnrolGateComponent` — renders Tier 1 / Tier 2 / Tier 3 from probe results, renders correct copy per tier, transitions on `DeviceEnrolled` hub event, falls back to polling when hub fails, shows regenerate button on token expiry. |
| **PWA flow** | `EnrolSessionRedeemerTests` (the redeem path), `EnrolmentRedeemConfirmDialogTests`. |
| **E2E (Playwright)** | `[Demo("cold-start-enrolment")]` — full Tier 3 walk on Docker stack: council page arrival → preflight → signup → QR → simulated phone enrolment via second browser context → council form continues → submission → credential lands in PWA. |
| **Walkthrough script** | `walkthroughs/Strathcarron/setup-cold-start-demo.ps1` — pre-creates the Strathcarron council org with the cold-start-enabled application; gives the operator a reset-able fresh-citizen email for demos. |

## §11 — Out of scope / deferred

- **Email-pickup fallback** — deferred per Q2. v1 wallet is mandatory; the wallet-less path is a future graceful-degradation spec.
- **PWA-hosted form continuation** — deferred per Q4. F125's `ApplicationInstance.razor` stub stays a stub for cold-start; Spec 4/5 picks it up when the application catalogue lands.
- **Application catalogue API** — Spec 3 designs the gate; the form behind it is whatever blueprint-driven form already exists. "List available applications" is Spec 4.
- **Cross-council single-sign-on UX polish** — the existing Sorcha auth already handles this; no new design needed here.
- **Multi-tenant council onboarding** — Spec 3 assumes Strathcarron is the council. Onboarding additional councils onto the gate is a roadmap item.
- **Token binding via HttpOnly cookie** — future hardening for the friend-scans-by-mistake path; v1 mitigation is the confirmation dialog.

## §12 — Success criteria

| ID | Criterion | How to verify |
|---|---|---|
| **SC-3-001** | Cold-start citizen (Tier 3, fresh browser) gets from "click apply" to "form ready to fill" in ≤ 90 s, 95th-percentile across 10 manual runs | Demo runbook with stopwatch |
| **SC-3-002** | Returning citizen (Tier 1) sees no QR, no enrolment gate — only a sign-in screen — when they revisit | E2E test: enrol-then-revisit |
| **SC-3-003** | Mini-gate (Tier 2) shows the QR without re-prompting signup | E2E test: signup-then-revoke-device-then-revisit |
| **SC-3-004** | Cross-device hub event reaches the desktop within 2 s of `PlatformUserDeviceService.RegisterAsync` success in 95% of runs | Hub telemetry + E2E timing assertion |
| **SC-3-005** | Expired session token returns a clear "let's get you a new one" affordance — no dead-end | Manual test with clock-skewed token |
| **SC-3-006** | Friend-scans-by-mistake confirmation dialog renders the original user's email/display name before redeem completes | Component test of `EnrolmentRedeemConfirmDialog` |
| **SC-3-007** | Walkthrough script `setup-cold-start-demo.ps1` produces a working cold-start state on n1 within 30 s of invocation | Quickstart runbook |
| **SC-3-008** | Existing Feature 124 + Feature 125 test suites stay green (no regressions) | Standard CI |

## §13 — Open items for plan-phase

Locked during brainstorm; restated for the speckit plan step:

1. **`EnrolGateComponent` lives in `Sorcha.UI.Components.User`** (library), not in the council web shell. Drop-in for any council page that wants the gate.
2. **`EnrolSessionService` lives in `Sorcha.Tenant.Service`** — no new common library needed.
3. **Feature 116 signup endpoints gain a `?returnTo=<url>` query parameter** — validated against a trusted-return-domain allowlist; no open redirects.

## References

- Umbrella: `docs/superpowers/specs/2026-05-13-strathcarron-citizen-arc.md`
- Spec 1 design: `docs/superpowers/specs/2026-05-13-spec-1-assured-identity-on-pwa-design.md`
- Spec 2 design: `docs/superpowers/specs/2026-05-14-spec-2-sorcha-wallet-user-agent-design.md`
- Spec 2 implementation tag: `spec-125-complete` at `6c698ff6` (master)
- Feature 114 (Citizen Wallet PWA — device enrolment): `specs/114-citizen-wallet-pwa/`
- Feature 116 (Account linking — signup/sign-in flows): `specs/116-account-linking/`
- Feature 118 (Notification hubs — `TenantHub`): `specs/118-notifications-architecture/`
- Feature 124 (Spec 1): `specs/124-assured-identity-pwa/`
- Feature 125 (Spec 2): `specs/125-sorcha-wallet-user-agent/`
- sorcha-architecture skill: § "Citizen Wallet PWA (Feature 114)", § "Platform Organisation Topology", § "AssuredIdentity on the PWA (Feature 124)"
- sorcha-ui skill: § "Citizen Wallet PWA — path-prefix gotchas"
