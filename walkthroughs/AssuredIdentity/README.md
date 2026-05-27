# Assured Identity Walkthrough

Feature 107 + Feature 124 — single canonical citizen-identity workflow. A
citizen submits a polished 5-page wizard (name + DOB, address, contact,
optional photo, review), the Acme Verification Co. verification analyst
approves, and the citizen receives an **AssuredIdentityCredential** in the
Sorcha Wallet (PWA). The first-credential **welcome takeover** (Feature 124)
fires when the credential lands.

> **Feature 124 swapped this walkthrough to the Sorcha Wallet (PWA)**
> (`SorchaLocalWallet` target audience). The legacy HAIP filesystem-wallet
> path is gone (FR-011). Phase 2 (Driving Licence) is currently a stub —
> the umbrella citizen arc routes the credential-gated second service
> through Spec 4, which redesigns the wallet-side presentation UX.

> **Replaces `HaipVerifiedCitizen` and `HaipDrivingLicence`.** Those walkthroughs
> will be deleted in Phase 7 of that feature, after the driving-licence chain
> and cross-peer smoke tests land.

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
| 1 | Citizen → verification analyst → `AssuredIdentityCredential` lands in the PWA → welcome takeover fires | `run-phase1-identity.ps1` |
| 2 | DEFERRED to Spec 4 (citizen-arc credential-gated second service). The stub script explains the deferral and exits. | `run-phase2-licence.ps1` |

## Cross-node round-trip (Feature 137 — Tier 2)

Phase 1 now carries the citizen's **public delivery keys** in the action-1 payload: `run-phase1-identity.ps1`
fetches `GET /api/v1/wallet/holder-keys` and supplies `holderKeys` so the issued `AssuredIdentityCredential`
is **bound to the citizen's holder key (SD-JWT `cnf`) and encrypted to their wallet** — the blueprint's
`credentialIssuanceConfig.holderKeySourceField` points the issuer at `/holderKeys/holderJwk`. This works
single-node (published participant key wins for encryption; carried holder JWK adds the `cnf` binding) and
is the enabler for the genuine cross-node run.

The **Tier-2 cross-node verification** (citizen on a local SyncOnly replica → submission reaches the n1
owner/validator → analyst approves on n1 → credential delivered back to the local wallet) runs on the
machine holding `genesis-validator-key.json` with the `docker-compose.sync-from-n1.yml` split, per
`specs/137-cross-node-submission/quickstart.md` § Tier-2. SC-004 (fail-closed: zero credentials when
neither a published record nor carried keys resolve) and the `cnf`/precedence logic are covered locally by
unit + single-node integration tests (SC-005 Tier-1).

## Unattended reviewer agents (PR 3 / US3)

The reviewer roles (`verification-analyst`, `licensing-officer`) have rules-mode agent
configs in `actors/` that can stand in for a human at the review UI:

| Agent | Action handled | When it fires |
|---|---|---|
| `verification-analyst` | Identity Action 2 — "Verify Assured Identity Application" | As soon as Action 1 completes and Action 2 enters the inbox |
| `licensing-officer` | Driving Licence Action 3 — "Issue Driving Licence" | As soon as Action 2 (verify) completes and Action 3 enters the inbox |

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
> reviewer actions race today. Full integration (skip the script-side
> reviewer submissions when `-UseAgents` is set, poll the instance state
> for the credential offer, claim from there) is tracked as a follow-up
> so the primary `run.ps1` path becomes agent-first without coordination
> issues. See issue #347.

Human operators can still open the reviewer UI during the pending
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
└── state.json                       # Per-run state, written by setup.ps1
```
