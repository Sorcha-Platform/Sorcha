# AIAS — Assured Identity (Feature 174 / M1)

A single, idempotent provisioning slice that stands up **Acme Identity Assurance
Services (AIAS)** — a fictional assurance provider — and delivers the
anonymous → assured journey end to end: an attendee signs up, applies, includes a
photo, and (once the **autonomous Assure-ID agent** approves) receives an
AIAS-branded, photo-bearing **Assured Identity** credential in their wallet. The
agent evaluates real signals (email verified, photo present, **postcode
existence**, **profanity**) and either approves or **rejects with an on-brand,
humorous reason**. This is M1 of the AIAS conference demo (M0–M5).

- Program north-star: `docs/superpowers/specs/2026-06-29-aias-conference-demo-design.md`
- Spec / plan / quickstart: `specs/174-aias-assured-identity/`

This toolkit mirrors the proven `demos/AssuredIdentity` provisioning module and
reuses its generic lib helpers and the shared `SorchaWalkthrough` module. It adds
**no services** — it orchestrates existing Sorcha HTTP endpoints and the existing
`sorcha-agent` CLI.

---

## Prerequisites

- A running Sorcha stack: `docker-compose up -d` (Docker-first) **or** n1.
  The platform **auto-seeds** the System Admin + Public org — **do NOT run
  `sorcha bootstrap`**.
- PowerShell 7+.
- `sorcha-agent` on `PATH` (the agent is launched as a child process; if it's not
  on PATH, the config is written and the manual launch command is printed).
- Agent credentials are taken from `$env:AGENT_EMAIL` / `$env:AGENT_PASSWORD`
  (set automatically by the module from the provisioned identities — no secrets
  are committed).

---

## Provision (idempotent, reboot-proof)

```powershell
# Docker (default)
./demos/AIAS/run-demo.ps1

# n1
./demos/AIAS/run-demo.ps1 -Target n1

# force recreate org + republish blueprint
./demos/AIAS/run-demo.ps1 -Force
```

`run-demo.ps1` imports `AiasDemo.psm1` and runs one idempotent pass:

1. Create org **"Acme Identity Assurance Services (AIAS)"** (subdomain `aias`) —
   **skips if present**.
2. **Set the org VC-issuance master key** (`Set-SorchaOrgMasterKey`, via
   `Set-AiasOrgMasterKey`). ⚠ **Required.** An org that issues native
   `SorchaLocalWallet` VCs without a master key signs with its bare wallet key and
   emits an unresolvable `iss` (no `kid`/`jwk`), so verification later fails
   closed. The AssuredIdentity demo historically skipped this (HAIP-enrolment
   only); AIAS MUST do it. Idempotent (no-op on 409).
3. Publish the AIAS Assured Identity blueprint — rendered from
   `blueprints/aias-assured-identity.template.json` with `{{issuerName}}` set to
   the AIAS name (single source). **Skips if already published.**
4. Generate the runtime agent config `agent/assure-id.config.json` and launch the
   **Assure-ID agent** (`sorcha-agent run --config agent/assure-id.config.json
   --state state.json`, rules mode).

Re-running against an already-provisioned environment completes without creating
duplicate orgs / blueprints / agents (the gate for **SC-001**).

---

## Happy path (anonymous → photo-bearing credential)

1. Sign up an anonymous user on the web app and **verify the email**.
2. Open the **AIAS Assured Identity** application; fill name + an **existing UK
   postcode**; capture a **photo** (camera or upload — optional, but it's the
   point of the demo).
3. Submit. Within ~30 s the Assure-ID agent approves and HAIP issues the
   AssuredIdentityCredential.
4. Claim it into the wallet — it shows AIAS branding and the submitted photo.

## Rejection paths (the on-stage theatre)

Each is rejected automatically with a distinct, on-brand reason and **no
credential is issued**:

| Path | Trigger | On-brand reason |
|------|---------|-----------------|
| **Bad postcode** | `ZZ99 9ZZ` / a place that isn't real | *"AIAS could not locate that address on any map. We assure real people at real places — try a postcode that exists."* |
| **Profanity** | profane/abusive details in a name or address field | *"AIAS does not assure identities described in such… colourful terms. Please reapply with your Sunday-best vocabulary."* |
| **Unverified email** | apply before verifying email | *"AIAS needs a verified email before it can assure you. Confirm your email and reapply."* |

(The reasons live in `agent/assure-id.rules.json`; a catch-all rule approves
clean applications.)

**The applicant sees the reason (Feature 183).** A reject is no longer a black
hole: the reject route carries an `x-decision-notice` so, when AIAS declines, a
durable **bell/inbox** entry lands for the applicant carrying the on-brand reason
— it survives reload, logout, and a device switch. (Approval is already visible:
a "claim your credential" action appears, then a credential-received notice on
delivery.) The email gate is now genuine too: the web form carries the citizen's
*real* verified status via `x-claim-source: email_verified`, so a verified
applicant is approved and an unverified one is really rejected — not a hardcoded
pass. `rehearse.ps1` exercises both directions.

---

## Offline behaviour

The postcode-existence check uses the public UK **postcodes.io** lookup when
available, and **degrades gracefully** to the bundled allow-list at
`fixtures/postcodes.offline.json` when it's unreachable. To force offline, set
`agent/assure-id.checks.json` → the `postcodeExists` check's `"offlineMode"` to
`"always"` (it ships as `"auto"`), or simply pull the venue's internet. Approvals
and rejections still flow with **no internet** (SC-007).

---

## Rehearsal / test hook

```powershell
./demos/AIAS/rehearse.ps1            # Docker
./demos/AIAS/rehearse.ps1 -Target n1
```

Against the provisioned environment this runs **one approval** and **one
rejection** end to end (FR-011, SC-004) and asserts: approval → credential issued
(offer present) with portrait; rejection (bad postcode `ZZ99 9ZZ`) → decision
`rejected` recorded with the on-brand reason and **no credential**. Exit 0 on
success, non-zero on failure.

---

## Status & reset

```powershell
Import-Module ./demos/AIAS/AiasDemo.psm1
Get-AiasDemoStatus            # Ready / NotReady + per-signal detail
Reset-AiasDemo                # local: stop agent + clear state.json + rendered config
```

`Reset-AiasDemo` is a **local** reset. A full server-side wipe (org, register
Mongo DBs, demo wallets) is node-side: **Docker** `docker compose down -v`; **n1**
the documented reset recipe (`network-bootstrap` skill). Re-run `run-demo.ps1`
afterwards to re-provision (it's idempotent).

---

## How it works

This milestone is **~80% assembly of proven parts** (the AssuredIdentity blueprint
+ `Sorcha.Agent` + F107 portrait capture/embed + HAIP issuance). The **one
genuine code addition** is the **external-check hook** in
`src/Apps/Sorcha.Agent/Decision/Checks`: each configured check (email verified,
photo present, postcode exists, profanity) produces a boolean **fact** under
`checks.{name}`, and `agent/assure-id.rules.json` (JSON-Logic) decides on those
facts. The module embeds the bare rule array into the runtime ActorDefinition
under `"rules"` and points `"checksFile"` at `assure-id.checks.json` (which sits
next to the generated config so its relative `../fixtures/postcodes.offline.json`
resolves).

### Files

| File | Purpose |
|------|---------|
| `AiasDemo.psm1` | Idempotent provisioning module (org, master key, blueprint, agent config + launch, status, reset). |
| `run-demo.ps1` | Thin entry point — `Initialize-AiasDemo` + final status. |
| `rehearse.ps1` | Test hook — one approval + one rejection, asserted. |
| `blueprints/aias-assured-identity.template.json` | Blueprint template (`{{issuerName}}` + AIAS branding + reject route + `emailVerified` signal). |
| `agent/assure-id.rules.json` | Bare JSON-Logic rule array (3 reject rules + catch-all approve). |
| `agent/assure-id.checks.json` | External-check config (email/photo/postcode/profanity). |
| `agent/assure-id.config.json` | **Generated** runtime ActorDefinition (rules inline + `checksFile`). |
| `fixtures/postcodes.offline.json` | Offline allow-list for the postcode check. |
