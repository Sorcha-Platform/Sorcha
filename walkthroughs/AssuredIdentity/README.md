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
| 2 | _(Future — PR 2)_ Driving licence chain | `run-phase2-licence.ps1` |

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
