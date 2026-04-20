# Assured Identity Walkthrough

Feature 107 — single canonical citizen-identity workflow. A citizen submits a
polished 5-page wizard (name + DOB, address, contact, optional photo, review),
the government assessor approves, and the citizen receives an
**AssuredIdentityCredential** in their chosen wallet.

> **Replaces `HaipVerifiedCitizen` and `HaipDrivingLicence`.** Those walkthroughs
> will be deleted in Phase 7 of this feature, after the DLA chain and cross-peer
> smoke tests land.

## What it proves

- The new `x-review` schema extension rendering a Page 5 ID-card review
- The new `x-file.capture` + `x-file.embedAs` extensions driving camera capture
  and client-side token-image resize (browser path only; the walkthrough script
  supplies a pre-sized token when `-IncludePortrait` is passed)
- The DoB picker client-side bound (`formatMaximum: "today"`) wired via
  `DateTimeFieldBoundResolver`
- Server-side portrait-size gate (`WARN_CRED_PORTRAIT_OVERSIZE_001`)
- Full HAIP OID4VCI credential issuance including the new
  `AssuredIdentityCredential` type
- Open-participant late binding (citizen is not in `$walletMap`)

## Phases

| Phase | What runs | Script |
|---|---|---|
| 1 | Citizen → gov assessor → `AssuredIdentityCredential` | `run-phase1-identity.ps1` |
| 2 | Citizen → DLA (HAIP presentation) → `DrivingLicenceCredential` | `run-phase2-licence.ps1` |

## Unattended assessor agents (PR 3 / US3)

The assessor roles (`gov-assessor`, `dla-officer`) have rules-mode agent
configs in `actors/` that can stand in for a human at the review UI:

| Agent | Action handled | When it fires |
|---|---|---|
| `gov-assessor` | Identity Action 2 — "Verify Assured Identity Application" | As soon as Action 1 completes and Action 2 enters the inbox |
| `dla-officer` | Driving Licence Action 3 — "Issue Driving Licence" | As soon as Action 2 (verify) completes and Action 3 enters the inbox |

Run them alongside the walkthrough with:

```powershell
# In one terminal, launch the agents in background (they exit after firing):
pwsh walkthroughs/AssuredIdentity/run-agents.ps1

# In another terminal, run the existing phase scripts. If an agent has
# already submitted an action, the script will race and the losing
# submission will error gracefully.
pwsh walkthroughs/AssuredIdentity/run.ps1
```

> **PR 3 scope note.** Agent-driven and script-driven submissions of the
> assessor actions race today. Full integration (skip the script-side
> assessor submissions when `-UseAgents` is set, poll the instance state
> for the credential offer, claim from there) is tracked as a follow-up
> so the primary `run.ps1` path becomes agent-first without coordination
> issues. See issue #347.

Human operators can still open the assessor UI during the pending
window — whoever submits first wins (FR-030). AI-mode and external
validator-API modes are shape-compatible extensions per FR-029.

## Prerequisites

- Docker Desktop running with `docker-compose up -d`
- Secrets initialised: `pwsh walkthroughs/initialize-secrets.ps1`
- PowerShell 7.5+
- .NET 10 SDK (to build and run `sorcha-agent`)

## How to run

```powershell
# One-time setup (idempotent)
pwsh walkthroughs/AssuredIdentity/setup.ps1

# Phase 1 — identity issuance
pwsh walkthroughs/AssuredIdentity/run-phase1-identity.ps1

# Force fresh setup (deletes state.json)
pwsh walkthroughs/AssuredIdentity/setup.ps1 -Force
```

## Files

```
walkthroughs/AssuredIdentity/
├── README.md                        # This file
├── setup.ps1                        # Org / wallet / blueprint provisioning
├── run-phase1-identity.ps1          # Submit → approve → claim AssuredIdentityCredential
├── blueprints/
│   └── assured-identity.json        # Three actions: submit, verify, claim
├── data/
│   └── sample-portrait.jpg          # Optional pre-sized token for -IncludePortrait
├── wallet/                          # HAIP filesystem wallet (produced by run-phase1)
└── state.json                       # Per-run state, written by setup.ps1
```
