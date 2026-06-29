# Quickstart: Provision & run the AIAS demo (M1)

Docker-first; the same module targets n1. Everything is one idempotent script — re-runnable after a
network wipe.

## Prerequisites
- A running Sorcha stack: `docker-compose up -d` (or n1). Platform auto-seeds (System Admin + Public
  org) — do **not** run `sorcha bootstrap`.
- PowerShell 7.

## 1. Provision AIAS (idempotent)

```powershell
# Docker (default)
./demos/AIAS/run-demo.ps1

# n1
./demos/AIAS/run-demo.ps1 -Target n1
```

`run-demo.ps1` calls `AiasDemo.psm1` to, idempotently:
1. Create org **"Acme Identity Assurance Services (AIAS)"** (skips if present).
2. **Set the org VC-issuance master key** (`Set-SorchaOrgMasterKey`). ⚠ **Required** — an org that
   issues native `SorchaLocalWallet` VCs without a master key signs with its bare wallet key and
   emits an unresolvable `iss` (no `kid`/`jwk`), so verification later fails closed. The
   AssuredIdentity demo historically skipped this (HAIP-enrolment only); AIAS MUST do it.
3. Publish the AIAS Assured Identity blueprint (template with `{{issuerName}}` + AIAS theme + the
   reject route) to the register.
4. Launch the **Assure-ID agent**: `sorcha-agent run --config demos/AIAS/agent/assure-id.config.json
   --state <state.json>` (rules mode; reads `assure-id.rules.json` + `assure-id.checks.json`).

## 2. Walk the happy path
1. Sign up an anonymous user (verify email).
2. Open the AIAS Assured Identity application; fill name + an **existing UK postcode**; capture a
   **photo** (camera or upload).
3. Submit. Within ~30 s the Assure-ID agent approves and HAIP issues the Assured Identity credential.
4. Claim it into the wallet — the credential shows AIAS branding and the photo.

## 3. Walk the rejection paths (the on-stage theatre)
- **Bad postcode**: enter `ZZ99 9ZZ` / "Hogwarts" → rejected: *"AIAS could not locate that address on
  any map."*
- **Sweary details**: include profanity in a detail field → rejected with an on-brand reason.
- **Unverified email**: skip email verification → rejected/held with the email reason.

## 4. Offline behaviour
Set the postcode check to offline (`assure-id.checks.json` → `"offlineMode": "always"`, or simply
pull the venue's internet): the check resolves against `demos/AIAS/fixtures/postcodes.offline.json`,
so approvals/rejections still flow. Verify the demo completes with no internet.

## 5. Rehearsal / test hook

```powershell
./demos/AIAS/rehearse.ps1            # Docker
./demos/AIAS/rehearse.ps1 -Target n1
```
Runs one **approval** and one **rejection** end to end against the provisioned environment and
asserts: approved → credential issued with portrait; rejected → reason recorded, no credential.

## 6. Re-run safety
Re-running `run-demo.ps1` against an already-provisioned environment completes without creating
duplicate orgs/blueprints/agents (idempotent — the gate for SC-001).
