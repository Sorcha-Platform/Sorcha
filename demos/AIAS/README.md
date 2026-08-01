# AIAS — Assured Identity (Feature 174 / M1 + M2)

A single, idempotent provisioning slice that stands up **Acme Identity Assurance
Services (AIAS)** — a fictional assurance provider — and delivers the
anonymous → assured journey end to end: an attendee signs up, applies, includes a
photo, and (once the **autonomous Assure-ID agent** approves) receives an
AIAS-branded, photo-bearing **Assured Identity** credential in their wallet. The
agent evaluates real signals (email verified, photo present, **postcode
existence**, **profanity**) and either approves or **rejects with an on-brand,
humorous reason**. This is M1 of the AIAS conference demo (M0–M5).

**M2 adds a second, independent workflow: the AIAS Cyber Level.** The citizen
presents their Assured Identity credential to prove entitlement, answers an
eight-question cyber-hygiene questionnaire, and a second autonomous agent — the
**Cyber agent** — scores the answers into a Bronze/Silver/Gold/Platinum band and
issues a `CyberLevelCredential`, or hard-rejects before scoring if the presented
identity carries no portrait. See "AIAS Cyber Level (M2)" below.

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
3. Publish the AIAS blueprints — rendered from `blueprints/aias-assured-identity.template.json`
   and `blueprints/aias-cyber-level.template.json` (plus the device-registration
   template) with `{{issuerName}}` set to the AIAS name (single source). The
   Assured Identity + device-registration blueprints publish onto the Identity
   register; the Cyber Level blueprint publishes onto its own, separate Cyber
   register (see "AIAS Cyber Level (M2)" below). **Skips any blueprint already
   published.**
4. Generate the runtime agent configs `agent/assure-id.config.json` +
   `agent/cyber.config.json` and launch **both** autonomous agents: the
   **Assure-ID agent** (`sorcha-agent run --config agent/assure-id.config.json
   --state state.json`, rules mode) services the Identity register; the **Cyber
   agent** (`sorcha-agent run --config agent/cyber.config.json --state
   state.json`) services the Cyber register. Same underlying agent wallet/identity,
   two independent processes — each is register-scoped, so neither can pick up
   the other's pending actions.

Re-running against an already-provisioned environment completes without creating
duplicate orgs / registers / blueprints / agents (the gate for **SC-001**). A node
provisioned before M2 self-heals: the Cyber register + its agent participant are
created on the next `New-AiasOrg` run without needing `-Force`.

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

## AIAS Cyber Level (M2)

A second, independent workflow: once a citizen holds an Assured Identity, they can
apply to AIAS for a **Cyber Level** — present the Assured Identity to prove it's
theirs, answer eight quick questions about password habits, 2FA, update
discipline, phishing awareness and password sharing, and the autonomous **Cyber
agent** scores the card and issues a `CyberLevelCredential` carrying the band
(Bronze/Silver/Gold/Platinum) and the portrait carried forward from the
presentation.

- **Scoring**: six graded multiple-choice questions + two 0-3 sliders, 24 points
  total. Bands: **24 Platinum**, **21-23 Gold**, **16-20 Silver**, **12-15
  Bronze**, **below 12 → rejected** (no credential). The scoring table lives in
  `agent/cyber.checks.json`; the band thresholds are ordinary JSON-Logic rules in
  `agent/cyber.rules.json`.
- **Two deliberate traps** cost 2 points each — a confident-but-wrong password
  rotation habit ("Every 30 days, like clockwork" instead of "only when I think
  one's been exposed") and inspecting a suspicious email's sender address instead
  of verifying out-of-band. A perfect card minus both traps lands at exactly 20 →
  Silver, proving the questionnaire produces a real spread rather than handing
  everyone the top band.
- **Portrait hard-gate**: if the presented Assured Identity carries no portrait,
  the Cyber agent rejects before scoring at all — AIAS can't put a level on a
  face it's never seen. A `portraitPresent` external check gates this, distinct
  from the scoring check.
- **Own register**: the Cyber questionnaire runs on its own advertised DevMode
  register, separate from the Identity register the Assured Identity credential
  was issued on. This is the one new cross-register assumption M2 introduces — a
  credential minted on one register gates a workflow on another — and is why the
  Cyber blueprint/agent/register are provisioned and tracked independently of the
  Identity ones throughout `AiasDemo.psm1`.

(The band messages + reject reasons live in `agent/cyber.rules.json` and the
blueprint's `x-decision-notice` catalogue; the answer strings ARE the scoring
keys — matched ordinally against `agent/cyber.checks.json`, so a drifted or
mistyped enum value scores 0 silently, with no error.)

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
# Identity scenario (default) — M1
./demos/AIAS/rehearse.ps1                       # Docker
./demos/AIAS/rehearse.ps1 -Target n1

# Cyber scenario — M2
./demos/AIAS/rehearse.ps1 -Scenario cyber        # Docker
./demos/AIAS/rehearse.ps1 -Scenario cyber -Target n1
```

`-Scenario identity` (default) runs against the provisioned environment: **one
approval** and **two rejections** end to end (FR-011, SC-004) and asserts:
approval → credential issued (offer present) with portrait; bad-postcode
rejection → decision `rejected` recorded with the on-brand reason and **no
credential**; unverified-email rejection → decision `rejected` via the email
gate and **no credential**.

`-Scenario cyber` mints a fresh Assured Identity for each path (submit → agent
approves → credential delivered), presents it into the Cyber register's
questionnaire action, and asserts four paths: a perfect card → `Platinum`; a
perfect card minus both traps (-2 each, 24 → 20) → `Silver` — the path proving
the questionnaire produces a real spread, not just a working mechanism; a
dishonest-but-consistent low score (0/24) → **no credential**, with a durable
inbox decision notice matching the `cyber-fail` catalogue entry; and a perfect
card presented with a **portrait-less** Assured Identity → hard-rejected before
scoring, **no credential**, inbox notice matching `no-portrait`.

Exit 0 on success (all paths pass for the chosen scenario), non-zero on any
assertion failure.

---

## Verify the Cyber Level (M3)

```powershell
./demos/AIAS/verify-cyber.ps1 -Target n1 -Email <citizen holding a Cyber Level credential>
```

Drives the whole verify moment headlessly: creates an OpenID4VP request for
`https://sorcha.dev/vc/cyber-level/v1`, answers it **as the holder**, and reads the verifier's own
result back. Exit 0 means the moment works. A passing run asserts `state=Verified`, `isValid=true`,
`holderKeyVerified=true`, and that `level` + `portrait` were actually disclosed.

The holder lives in **`lib/HolderPresentation.ps1`** (`Complete-SorchaWalletPresentation`), shared
with `rehearse.ps1` — the same server-custody path `Sorcha.Wallet.Pwa`'s `Present.razor` uses
(RFC 9901 `sd_hash` → KB-JWT signed by `POST /api/v1/wallet/presentations/sign-kb` → OpenID4VP
object-keyed `vp_token` posted to the request's own `response_uri`). It is transport-agnostic
because it reads `nonce`, `client_id` and `response_uri` **from the request object**, so the same
function answers a blueprint credential gate and a verifier request.

> **Why this is scriptable at all:** an AIAS credential is issued `SorchaLocalWallet` — an SD-JWT
> encrypted to the citizen wallet's key with the holder private key in server custody — so there is
> no device in the loop. The `sorcha-agent haip present` CLI holder **cannot** substitute: it
> consumes an OpenID4VCI *offer* into a file wallet, and no offer exists for a SorchaLocalWallet
> credential.

Three things that will waste your time otherwise:

- **Credentials arrive as OFFERS.** Feature 106 lands inbound register-native credentials as
  `PendingAcceptance`, and the credential listing defaults to **Active only** — so a freshly-issued
  credential is invisible to a plain `GET .../credentials` and reads as "holds no Cyber Level
  credential" when they demonstrably do. Use `?status=All` and accept it (`PATCH .../credentials/{id}`
  with `{"status":"Active"}`) as the script does. The rehearsals assert *delivery* and stop there, so
  nothing else in the demo exercises acceptance.
- **The create-response's `state` is the request STATUS enum**, not the OAuth `state` parameter — a
  name collision. The OAuth state this request declares is the request id. Passing `$vreq.state`
  gets you `"state parameter does not match the request"` from `direct_post`.
- **The verifier result's `state` is on the envelope**, not inside `result`:
  `{ requestId, state, result: { isValid, verifiedClaims, holderKeyVerified, … }, vpToken }`.

The kiosk's `service-verifier` principal is registered with scope `blueprints:read`. The verifier
endpoints gate on `RequireService` (token_type=service), **not** a `haip:*` scope — requesting a
scope the principal does not hold returns 401, which looks exactly like "not provisioned".

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

**M2's genuine code addition** is the `scored-questionnaire` check
(`ScoredQuestionnaireCheck`, same `Decision/Checks` folder): it produces a
**numeric** fact instead of a boolean (`ExternalCheckResult.Numeric`), summing a
questionnaire's answers into a single score that `agent/cyber.rules.json`'s
JSON-Logic band thresholds compare against. `ExternalCheckRunner` merges a
check's numeric OR boolean value into `checks.*` (never both, per check name),
so `{"var": "checks.cyberScore"}` resolves to the band-comparable total. See the
`sorcha-architecture` skill's "AIAS Cyber Level (M2)" section for the full
contract.

### Files

| File | Purpose |
|------|---------|
| `AiasDemo.psm1` | Idempotent provisioning module (org, master key, both registers, both blueprints, both agent configs + launch, status, reset). |
| `run-demo.ps1` | Thin entry point — `Initialize-AiasDemo` + final status. |
| `rehearse.ps1` | Test hook — `-Scenario identity` (default, one approval + two rejections) or `-Scenario cyber` (four scored-questionnaire paths), asserted. |
| `blueprints/aias-assured-identity.template.json` | Identity blueprint template (`{{issuerName}}` + AIAS branding + reject route + `emailVerified` signal). |
| `blueprints/aias-cyber-level.template.json` | Cyber Level blueprint template (M2) — credential-presentation gate + 8-question scored questionnaire + banded issuance. |
| `agent/assure-id.rules.json` | Bare JSON-Logic rule array for the Assure-ID agent (3 reject rules + catch-all approve). |
| `agent/assure-id.checks.json` | External-check config for the Assure-ID agent (email/photo/postcode/profanity). |
| `agent/assure-id.config.json` | **Generated** runtime ActorDefinition for the Assure-ID agent (rules inline + `checksFile`). |
| `agent/cyber.rules.json` | Bare JSON-Logic rule array for the Cyber agent (M2) — no-portrait hard reject + band thresholds + catch-all Platinum. |
| `agent/cyber.checks.json` | External-check config for the Cyber agent (M2) — `portraitPresent` + the `scored-questionnaire` scoring table. |
| `agent/cyber.config.json` | **Generated** runtime ActorDefinition for the Cyber agent (M2, rules inline + `checksFile`). |
| `fixtures/postcodes.offline.json` | Offline allow-list for the postcode check. |
