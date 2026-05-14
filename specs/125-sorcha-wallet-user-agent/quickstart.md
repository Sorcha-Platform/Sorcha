# Quickstart — Sorcha Wallet (Full User-Agent v1)

**Feature**: 125-sorcha-wallet-user-agent
**Audience**: Demo presenters, operators, reviewers running the three headline beats end-to-end.

This document is the runbook for demonstrating Spec 2. It assumes the implementation has merged (all six PRs from the plan-phase's PR decomposition landed) and the operator wants to walk through the three headline demo beats and the supporting cast.

## Prerequisites

- Docker Desktop running, `docker compose up -d` completed cleanly.
- Secrets initialised: `pwsh walkthroughs/initialize-secrets.ps1`.
- PowerShell 7.5+ (`pwsh`).
- .NET 10 SDK.
- A primary device (phone or a second browser profile) acting as Sarah's wallet — opens `http://localhost/wallet/`.
- A secondary device (or a different browser profile) acting as the engineer's wallet for Beat 1 — also opens `http://localhost/wallet/` under a different account.

## One-time setup

```powershell
pwsh walkthroughs/AssuredIdentity/setup.ps1
```

Pre-creates Sarah's platform account, the verification analyst, the AssuredIdentity register, the Driving Licence register, and pre-signs everyone in on the wallet host. Idempotent.

For Beat 1 (doorstep verification), additionally provision:

```powershell
pwsh walkthroughs/Strathcarron/setup-doorstep-demo.ps1
```

This script (added in PR-C of the plan):
- Creates the Caledonian Water org as an issuer.
- Provisions Liam Buchanan as a Caledonian Water engineer (PlatformUser + OrgMembership + employee credential).
- Issues a "WaterEngineer/v1" credential to Liam.
- Pre-signs Liam in on a secondary wallet device.

For Beat 3 (context switching):

```powershell
pwsh walkthroughs/Strathcarron/setup-multi-context-demo.ps1
```

This script (added in PR-B of the plan):
- Creates Caledonian Builders Ltd as an employer org.
- Adds Sarah-the-construction-worker as an OrgMembership in Builders.
- Issues a "SiteSafetyCert/v1" to Sarah under the Builders context.
- Sets up the Personal context as Sarah's default.

## Demo Beat 1 — Doorstep verification

**Scenario**: Margaret-the-elderly-homeowner is at her door with Liam-the-water-engineer. She uses her Sorcha Wallet to verify his credential.

In two windows:

1. **Engineer's wallet** (laptop browser profile A): `http://localhost/wallet/` signed in as Liam Buchanan.
2. **Margaret's wallet** (phone or browser profile B): `http://localhost/wallet/` signed in as Margaret (or any citizen account; no credentials needed).

Walk:

1. On Liam's device, tap his WaterEngineer credential card → tap "Show as QR".
2. On Margaret's device, tap **Verify a credential** (the hero Verify action on Home).
3. Margaret's camera opens full-screen. She points it at Liam's QR.
4. Within 5 seconds, the wallet shows a green trust panel: *"Liam Buchanan — Water Engineer, Caledonian Water. Valid until [date]. Identity confirmed."*

Variations:

- **Revoked credential**: revoke Liam's credential via the admin UI, repeat — verify panel turns red and instructs Margaret to decline access.
- **Issuer signature unverifiable**: simulate by temporarily stopping the issuer's status-list publisher container — verify panel expands with a plain-English warning and safe-default advice.
- **Offline**: disable network on Margaret's device, repeat — verify panel either uses cached issuer keys (if previously cached) or surfaces a clear "couldn't reach the registry" message.

## Demo Beat 2 — Application from phone

**Scenario**: Sarah holds her Assured Identity from Spec 1. She wants to apply for a Driving Licence from her phone.

In one window:

1. **Sarah's wallet** (phone): `http://localhost/wallet/` signed in as Sarah, holds an Assured Identity.

Walk:

1. On Sarah's wallet Home, the Needs-attention or recommended-applications surface shows *"Start a Driving Licence application — uses your verified identity automatically."*
2. Tap it. The form opens directly in the wallet.
3. Page 1 (Name + DOB): pre-filled from Sarah's Personal persona. Tap Next.
4. Page 2 (Address): pre-filled. Tap Next.
5. Page 3 (Contact): pre-filled. Tap Next.
6. Page 4 (Portrait): tap the camera button. Mobile camera opens full-screen. Take selfie. Retake if needed. Confirm.
7. Page 5 (Review): id-card preview with all details. Tap Submit.
8. Wallet's Home now shows *"Driving Licence application in review"* in the Needs-attention band.

Variations:

- **Camera permission denied**: revoke camera permission before the demo; on page 4 the wallet shows the recovery scaffold (re-grant via browser settings, or use a friend's phone).
- **Session expires mid-submission**: extreme — let the JWT TTL pass before tapping Submit. The wallet prompts re-auth and resumes the submission with form data preserved.
- **Persona missing**: clear the persona before the demo; pages 1-3 show empty fields with no auto-fill. Form still works.

## Demo Beat 3 — Context switching

**Scenario**: Sarah holds Personal credentials and work credentials at Caledonian Builders Ltd. She switches between the two.

Walk:

1. On Sarah's wallet, the active-context chip at the top reads "Personal".
2. Home shows Sarah's Personal credentials (Assured Identity), her Personal recent activity, and any Personal pending applications.
3. The peek footer at the bottom of Home shows *"+ 1 credential in Caledonian Builders Ltd"*.
4. Tap the context chip. A bottom sheet opens listing all contexts. Tap "Caledonian Builders Ltd".
5. Wallet refreshes within 1 second. Home now shows the SiteSafetyCert credential, Builders-context recent activity, the "Submit incident report" recommended application.
6. Tap context chip again, tap Personal — content swaps back.

Variations:

- **Mid-presentation switch**: start a presentation under Personal context, switch contexts mid-flow. The presentation is cancelled with a clear message; user retries under the new context.
- **Single-context user**: clear the Builders membership before the demo. Context chip is still visible (shows "Personal") but is non-interactive; peek footer hidden.

## Supporting cast verification

### Transaction history (US4)

1. Tap the Activity footer-nav button.
2. Time-ordered feed shows: Assured Identity issued, Driving Licence application submitted, presentation to Caledonian Water (if exercised), etc.
3. Tap any entry → detail drawer with receipt, lifecycle ticks, trust panel.

### Devices & auth (US5)

1. Tap Settings → Devices.
2. List shows Sarah's currently-enrolled devices.
3. Tap a non-active device, tap Revoke. Reload that device's wallet — shows "this device has been revoked."
4. Settings → Auth methods. Add/remove passkey. Verify recovery copy explains diverse-methods benefits.

### Guided tour (US6)

1. Clear site data on a test browser, run enrolment (Spec 1 flow), let it complete.
2. After the welcome takeover dismisses, the guided tour starts.
3. Tap through each step (Present, Verify, Context chip, Footer nav). Dismiss.
4. Reload — tour does not re-fire.
5. Settings → About → Replay tour. Tour restarts.

## Verify the success criteria

| Criterion | How to verify |
|-----------|----------------|
| **SC-001** | Walk Beat 1 with a stopwatch. Should complete (closed wallet → green panel) in < 30 seconds in 95% of attempts. |
| **SC-002** | Walk Beat 2 with a stopwatch. Should complete (start → submission accepted) in < 5 minutes, single-attempt. |
| **SC-003** | Walk Beat 3. Switch from any wallet screen in ≤ 2 taps; content reflects new context within 1 second. |
| **SC-004** | Run Beat 6 (guided tour) with 10 fresh browser profiles, see whether ≥ 7/10 complete the tour. |
| **SC-005** | Walk Beat 1 with valid / revoked / unverifiable credentials. Expect clear pass / warn / fail in each case. |
| **SC-006** | `dotnet test tests/Sorcha.Wallet.Pwa.Tests/`, `tests/Sorcha.Wallet.Service.Tests/`, `tests/Sorcha.UI.E2E.Tests/` — all green, no regressions from Spec 1. |
| **SC-007** | Run the three Playwright demo-tagged tests: `dotnet test --filter "Demo=doorstep-verify"`, etc. All three pass cleanly. |
| **SC-008** | Playwright suite includes `PostRedeployCacheTests` and `AuthGatedNavigationTests` (added in PR-F). Both pass. |
| **SC-009** | Run `pwsh scripts/audit-pwa-library-consumption.ps1` (added in PR-F) — reports % of UI primitives consumed from the library. Should be ≥ 90%. |
| **SC-010** | Run `pwsh scripts/audit-reading-age.ps1` (added in PR-F) — reports average Flesch-Kincaid grade level. Should average ≤ 8.0 (US Grade 8 ≈ UK Year 9, with the SC-010 bar at UK Year 8 leaving a small buffer). |

## Common gotchas

- **Beat 1: QR not scanning**. Ensure good lighting on the engineer's device screen; check the engineer's wallet is rendering the QR with sufficient contrast. The wallet's camera flow includes a "manual entry" fallback that accepts a pasted offer URI.
- **Beat 2: portrait too large**. The client-side resize targets 240×320 JPEG. If the upload fails, the wallet's error scaffold explains and offers a retake.
- **Beat 3: peek footer not showing**. Confirm the user actually has content in another context. The peek footer is suppressed when other contexts have zero credentials AND zero pending applications.
- **Tour fires on every load**. Check that `WalletFlagsRecord.TourDismissedAt` is being persisted to IndexedDB. Open browser DevTools → Application → IndexedDB → the wallet's `device` store.
- **Context switch shows stale content**. Verify the wallet is calling `/auth/switch-org` and updating the bearer token in `IAccessTokenStore` before refreshing Home content.

## Tear down

```powershell
pwsh walkthroughs/Strathcarron/teardown.ps1
```

Clears the demo accounts (Sarah, Margaret, Liam, Builders memberships) while leaving the platform running. Use `docker compose down` afterwards to bring the platform down fully.

## What's next

After Spec 2 ships:

- **Spec 3** (enrol inside a council application wizard) — uses the form-rendering surface this spec proved out, in the council web-shell context.
- **Spec 4** (credential-gated second service — Blue Badge) — exercises the multi-context UI for a citizen with multiple credentials in the Personal context.
- **v2 self-custody opt-in** — slots into the `IUserSigner` abstraction this spec lands.
