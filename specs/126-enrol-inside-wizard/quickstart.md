# Quickstart — Sorcha Wallet enrolment inside a council application wizard

**Feature**: 126-enrol-inside-wizard
**Audience**: Demo presenters, operators, reviewers running Spec 3 end-to-end.

This document is the runbook for demonstrating the cold-start onboarding gate against the local Docker stack (or n1.sorcha.dev). It assumes the implementation has merged and the operator wants to walk a fresh citizen through Tier 3 and observe Tier 2 + Tier 1 paths.

## Prerequisites

- Docker Desktop running, `docker compose up -d` completed cleanly.
- Secrets initialised: `pwsh walkthroughs/initialize-secrets.ps1`.
- PowerShell 7.5+ (`pwsh`).
- A primary device — a desktop browser, plus a second device or browser profile acting as the citizen's phone wallet.

## One-time setup

```powershell
pwsh walkthroughs/Strathcarron/setup-cold-start-demo.ps1
```

This script (added in PR-A of the implementation plan):

- Provisions the Strathcarron Council org if absent.
- Publishes a `DrivingLicence` blueprint with `targetAudience: "SorchaLocalWallet"` on its credential-issuance action.
- Generates three reset-able test-citizen email addresses for the three tiers:
  - `cold-start-<random>@example.test` — Tier 3 (no account)
  - `mini-gate-<random>@example.test` — Tier 2 (account, no device)
  - `returning-<random>@example.test` — Tier 1 (account + paired device)
- For Tier 2 + Tier 1, pre-creates the platform user via the Feature 116 signup endpoints; for Tier 1, also runs the Feature 114 device-pairing ceremony so the test account starts with one active device.

Re-running the script resets all three test accounts to their target tier.

## Walk 1 — Cold-start (Tier 3, headline beat)

**Scenario**: A citizen with no Sorcha account and no wallet wants to apply for a driving licence.

In two windows:

1. **Desktop council page**: `http://localhost/strathcarron/services/driving-licence` (or the n1 equivalent).
2. **Phone wallet**: a second browser profile, mobile viewport (DevTools), no Sorcha cookies, no installed PWA.

Walk:

1. On the desktop, the page renders the **preflight signup surface** — short "you'll need an account and a wallet" copy + a single "Sign in or create your account" button. **No QR yet.**
2. Click the signup button → Feature 116 signup flow → use the script-provided cold-start email + a generated password.
3. After signup completion, the desktop redirects back to the council page (return-to allowlist validated). Page now renders the **wallet-pairing surface** with the hybrid QR + tap-link + copy-link.
4. On the phone, scan the QR (or copy the URL into the phone browser).
5. The wallet PWA loads, then immediately presents the **`EnrolmentRedeemConfirmDialog`**: *"You're about to enrol this device for `cold-start-XYZ@example.test`. If that's not you, close this page."* Confirm.
6. The PWA runs the Feature 114 device-pairing ceremony.
7. On the desktop, within 2 seconds of pairing completion, the council page transitions out of the waiting state — the **`DeviceEnrolled`** SignalR event has fired — and renders the application form.
8. Fill the form. Submit.
9. The success screen says "Watch your wallet for your credential." On the phone, the Feature 124 welcome takeover fires when the credential lands.

**Stopwatch checkpoints** (SC-001 target: <90 s):

- Preflight visible: t = 0
- Form ready: t ≤ 90 s

Variations:

- **Same-device cold-start** — use the phone browser as the desktop too. The page detects the mobile viewport and renders the tap-link more prominently than the QR. Tap-link opens the PWA on the same phone; pairing completes; switch back to the browser tab (form ready).
- **Stranger scans by mistake** — share the QR URL with a different browser profile / device. The confirmation dialog appears displaying the cold-start citizen's email + name. Cancel; verify no device row appears on the cold-start account (`GET /me/devices` returns empty).
- **Session token expires** — leave the QR alone for 11 minutes. Click "I've enrolled — continue" or wait for the auto status. The page surfaces "QR expired — let's get you a new one" with a regenerate button. Regenerate; new QR works.

## Walk 2 — Returning citizen (Tier 1, fast path)

**Scenario**: A citizen who already onboarded comes back for a second service.

In one window:

1. **Desktop council page**: signed-out, using the Tier-1 test email.

Walk:

1. Page renders a **sign-in screen** (no QR, no preflight explainer).
2. Sign in with the Tier-1 credentials.
3. Page drops directly into the application form — no intermediate "wallet setup" surface.

Stopwatch (SC-002 target: <30 s from click-Apply to form-ready, almost all of which is the sign-in form filling):

- Sign-in visible: t = 0
- Form ready: t ≤ 30 s.

## Walk 3 — Mini-gate (Tier 2, lost-phone recovery)

**Scenario**: A citizen whose account exists but who has no active device (lost phone, never enrolled, all devices revoked).

In two windows:

1. **Desktop council page**: signed-out, using the Tier-2 test email.
2. **Phone wallet**: fresh browser profile.

Walk:

1. Page renders the **sign-in screen** (Tier-2 same starting point as Tier-1).
2. Sign in. Page detects zero active devices, renders the **wallet-pairing surface** — same hybrid QR/link/paste as Tier-3 step 3, but **no signup mention**. Copy: "Let's pair this device with your wallet."
3. Scan QR on phone, confirm in the dialog, complete pairing.
4. Desktop transitions to the application form.

## Verify the success criteria

| Criterion | How to verify |
|-----------|----------------|
| **SC-001** | Stopwatch Walk 1; should complete in <90 s in 9.5/10 attempts. |
| **SC-002** | Walk 2; no QR / no enrolment surface visible at any point. |
| **SC-003** | Walk 3; no signup-related copy or button visible at any point. |
| **SC-004** | Devtools network tab: hub event arrives within 2 s of pairing. Or check OpenTelemetry: `sorcha_enrol_pairing_signal_latency_seconds{path=signalr}` p95 < 2 s. |
| **SC-005** | Block SignalR hub via browser devtools (block `/hubs/tenant` WebSocket); verify polling fallback fires within 6 s. |
| **SC-006** | Wait 11 minutes during Walk 1's pairing surface; verify regenerate affordance one-click reachable. |
| **SC-007** | Run Walk 1 ten times with fresh test citizens (re-run `setup-cold-start-demo.ps1`); record completion rate. |
| **SC-008** | Run "stranger scans" variation; verify `GET /me/devices` on the bound account returns 0 active devices after cancel. |
| **SC-009** | `dotnet test` — Feature 124 + Feature 125 suites still green. |

## Common gotchas

- **Council page sits forever on "Waiting for your phone"**. Likely the SignalR connection didn't establish; check devtools network tab for `/hubs/tenant`. The polling fallback should kick in after 2 s — if it doesn't, the council page's `IEnrolPairingSignal` registration is missing.
- **Confirmation dialog doesn't render the citizen's name**. The redeem response's `displayName` is null; check the `PlatformUser.DisplayName` field for the test account.
- **Session token rejected with `expired`** despite being fresh. Tenant Service clock skew vs. browser/device clock — verify the `TimeProvider` injection.
- **Open-redirect attempt succeeds**. The `ReturnToAllowlist` configuration is missing or empty; council origins must be added to `Auth:ReturnToAllowlist:Hosts` in `appsettings.json` / `appsettings.Production.json`.

## Tear down

```powershell
pwsh walkthroughs/Strathcarron/teardown.ps1
```

Clears the three test accounts and any partial enrolment state. Use `docker compose down` afterwards to bring the platform down fully.

## What's next

After Spec 3 ships:

- **Spec 4** (credential-gated second service — Blue Badge) — exercises the multi-context UI in F125 with a citizen who now has more than one credential.
- **Spec 5** (MyStrathcarron portal + third-party verifiers) — third-party verifier UX builds on the wallet established here.
- **F125 follow-ups** — PWA-hosted form continuation (Q4 option C), wallet-wide scaffold sweeps, audit scripts.
