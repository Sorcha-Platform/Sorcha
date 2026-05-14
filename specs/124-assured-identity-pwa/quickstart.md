# Quickstart — AssuredIdentity on the PWA

**Feature**: 124-assured-identity-pwa
**Audience**: Demo presenters, operators, reviewers running the feature end-to-end.

This document is the runbook for demonstrating the feature. It assumes you have just merged the implementation and want to see Sarah's first-credential ceremony land.

## Prerequisites

- Docker Desktop running, `docker-compose up -d` completed cleanly.
- Secrets initialised: `pwsh walkthroughs/initialize-secrets.ps1`.
- PowerShell 7.5+ (`pwsh`).
- .NET 10 SDK (needed if `sorcha-agent` is rebuilt locally).
- A second device (phone or a second browser profile) acting as Sarah's wallet device — opens `http://localhost/wallet/`. The presenter's primary screen stays on `http://localhost/app/` as the council web surface.

## One-time setup

```powershell
pwsh walkthroughs/AssuredIdentity/setup.ps1
```

Pre-creates Sarah's platform account, the verification analyst's account, the AssuredIdentity register, and pre-signs Sarah in on the wallet host so the demo can begin from her tapping the wallet entry point. Idempotent — safe to re-run.

If the script has been run before and you want a clean state, `setup.ps1 -Force`.

## Run the demo

Open three windows:

1. **Wallet device** (phone or second browser profile): `http://localhost/wallet/` — Sarah's PWA. After setup, this should land at the empty Home state with the "Enrol this device" button.
2. **Council site** (presenter's primary screen): `http://localhost/app/` — Sarah's council form will open here.
3. **Terminal**: where you run the demo script.

In the wallet device:

1. Tap **Enrol this device**.
2. Walk through the wizard. On the Done screen, you should see the new forward-looking copy: *"Enrolled. Your wallet is ready — submit your council application to receive your first credential."* (FR-001).
3. Tap **Open wallet** — lands on the empty Home with no waiting state yet.

In the terminal, start the agents and the run script:

```powershell
# In one terminal:
pwsh walkthroughs/AssuredIdentity/run-agents.ps1

# In another:
pwsh walkthroughs/AssuredIdentity/run-phase1-identity.ps1 -UseAgents
```

On the council site, the form fills automatically (or you can drive it manually with `run-phase1-identity.ps1 -NoAutoFill`). Sarah submits.

**Immediately after submit**, the script POSTs `Set-CitizenPendingApplication` to the wallet service with label `"Assured Identity"`. Switch to the wallet device — the Home now shows:

> Your Assured Identity application is being reviewed. You'll see it here when it's ready.

with a pulsing skeleton card below. This is the waiting state (FR-002).

The verification analyst agent picks up the action a few seconds later. When it approves:

- **Wallet device foreground** (the demo path): the SignalR push lands, the wallet syncs, the welcome takeover fills the screen — Sarah's Assured Identity id-card front-and-centre with "Welcome to your wallet" copy and an **Open** button. Sarah taps Open; the id-card settles into Home as her first credential (FR-004, FR-005, US3).
- **Wallet device cold open** (the alternative path you can demo separately): if Sarah closed the wallet before approval, reopening triggers the same takeover on first paint (FR-005, US4).

## Verify the success criteria

| Criterion | How to verify |
|-----------|----------------|
| **SC-001** | The full sequence from `setup.ps1`-completed-state to settled-Home completes in under 60 seconds end-to-end. Time it with `Measure-Command` wrapping `run-phase1-identity.ps1`. |
| **SC-002** | Re-run the demo 10 times. Each foreground run should land the takeover. |
| **SC-003** | Re-run with the wallet device closed between submit and approval (close the browser tab; relaunch from `http://localhost/wallet/` after approval). Takeover should fire on first paint. |
| **SC-004** | After Sarah's first run, close and reopen the wallet five times. The takeover never re-appears. |
| **SC-005** | `dotnet test tests/Sorcha.Wallet.Service.Tests/` — zero regressions. |
| **SC-006** | `pwsh walkthroughs/AssuredIdentity/run-multi-peer.ps1` — passes; check `multi-peer-findings/*.md` for the latest result. |
| **SC-007** | The Done screen on the wallet device never reads "Loaded 0 credential(s)" for a first-time citizen. |
| **SC-008** | The waiting state is visible for the entire duration between submit and the takeover firing; it clears within one second of the takeover dismiss. |

## Common gotchas

- **The takeover doesn't fire**: check that the wallet's IndexedDB has no prior `WalletFlagsRecord` with `WelcomedAt` set. Clear site data for `localhost`'s wallet origin to reset.
- **The waiting state stays after credential arrival**: confirm `run-phase1-identity.ps1` reached the `Clear-CitizenPendingApplication` step (script logs it). Manual clear: `Invoke-RestMethod -Method Delete -Uri "$WalletApi/pending-applications" -Headers $citizenJwt`.
- **`run-multi-peer.ps1` fails after the HAIP filesystem removal**: this script does not depend on the filesystem wallet — failures here are unrelated to Feature 124 and should be triaged separately (see `walkthroughs/AssuredIdentity/multi-peer-findings/`).
- **The wallet's Home is blank where the skeleton card should be**: confirm the pending-application notice is set (`Invoke-RestMethod -Uri "$WalletApi/pending-applications" -Headers $citizenJwt`). If empty, the script did not call Set or the JWT scope is wrong.

## Tear down

```powershell
pwsh walkthroughs/AssuredIdentity/setup.ps1 -Reset
```

Clears Sarah's state, removes the AssuredIdentity register, leaves the platform itself running. Use `docker-compose down` afterwards to bring the platform down fully.

## What's next

After Spec 1 ships, Spec 4 (credential-gated second service — Blue Badge) is the next content beat in the arc. Spec 2 (wallet UX foundations) and Spec 3 (enrol-inside-wizard seam) are infrastructure specs that polish what this quickstart already demonstrates.
