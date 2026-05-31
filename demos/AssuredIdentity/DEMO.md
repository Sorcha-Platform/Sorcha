# Assured Identity — Demo Environment

A standing, node-agnostic demonstration of decentralised identity assurance across
two (or more) Sorcha installations. A tester goes — **unscripted, through the real
product** — from anonymous sign-up to a verified **Assured Identity** credential in
their wallet. The approval in between is performed by an identity-validator agent
(a rule, an AI persona, or a human).

> **Concept: a demo is a mature walkthrough.** This graduated from the scripted
> `walkthroughs/AssuredIdentity/` dev validation into a coherent, parameterised,
> human-operable demo. When someone says "the Assured Identity walkthrough", they
> mean this.

This toolkit is an **operability layer** over the proven F143 cross-installation
loop. It provisions real authorities against real installations and orchestrates
the existing `sorcha-agent` — it adds no services and no tester UI.

---

## Prerequisites

- Two installations deployed + healthy on the current `:latest` images: a NAT'd
  **issuer/owner** node and a public **subscriber** node. (Defaults: `tiny` issuer,
  `n1` subscriber — but any installations work; see *Node inventory*.)
- `deploy/keys.env` populated with each installation's sysadmin password (and its
  own JWT signing key — **never shared between installations**).
- `sorcha-agent` on `PATH` (for `rules`/`ai` agent modes).
- For `ai` mode: `ANTHROPIC_API_KEY` set in the environment.
- PowerShell 7+.

---

## Node inventory

Copy the example and edit it for your installations:

```powershell
Copy-Item demos/AssuredIdentity/demo-nodes.example.json ./demo-nodes.json
```

Each entry: `id`, `role` (`issuer`|`subscriber`), `gateway`, `installationName`,
`rendezvousCapable`. **Swap or rename installations by editing this file** — the
toolkit hard-codes nothing. Secrets stay in `deploy/keys.env`, never here.

---

## Operator runbook

```powershell
Import-Module demos/AssuredIdentity/AssuredIdentityDemo.psm1

# 1. Provision the issuing authority on the issuer node (default agent = rules)
New-IssuingAuthority -IssuerNode tiny -AgencyName "Strathcarron Identity Authority"

# 2. Connect a public subscriber — BLOCKS until a tester can actually apply
Connect-Subscriber -SubscriberNode n1

# 3. Confirm tester-ready at a glance
Get-DemoStatus
```

Then hand the tester the **subscriber's** web address.

### Agent modes (`-AgentMode`)

| Mode | Behaviour |
|------|-----------|
| `rules` (default) | Deterministic JSON-Logic actor auto-approves Action 2. Fast; a live demo never stalls. |
| `ai` | A Claude persona reads each application and decides. Requires `ANTHROPIC_API_KEY`. **Guardrail:** if no decision lands within ~90s, `Get-DemoStatus` shows the approver as pending — retry, or re-provision as `rules`/`human`. The engine does **not** silently auto-degrade. |
| `human` | No agent is launched. Log into the issuer node as the verification analyst and approve Action 2 yourself (the command prints the steps). |

```powershell
New-IssuingAuthority -AgentMode ai      # AI assessor
New-IssuingAuthority -AgentMode human   # you approve
```

### Rebrand (light customisation)

Re-run with a different name; the org, register, and the **credential's displayed
issuer** all reflect it (single source — no manual edits):

```powershell
Reset-Demo -Scope all
New-IssuingAuthority -AgencyName "Glenmara Borough Registry"
```

The credential-issuer DID stays valid across renames because it derives from the
issuer wallet address, which is stable.

### Deep customisation (the application itself)

To change the *workflow* — fields, pages, actions — amend the published blueprint
in the **real F142 Designer** (Describe → Understand → Rehearse → Go-live):

1. Open the authority's published blueprint in the Designer.
2. Amend it (`POST /api/blueprints/from-published` clones it back to a draft).
3. Rehearse, then publish to the **same register**.

The authority's identity (org, wallet, register, DID) is untouched — only the
workflow changes.

### Multi-node (additional independent subscribers)

Add another subscriber to `demo-nodes.json`, then connect it. Each subscriber
independently replicates the register and is independently readiness-gated:

```powershell
Connect-Subscriber -SubscriberNode n2
```

### Reset & status

```powershell
Get-DemoStatus                       # Ready / NotReady + per-node signals
Reset-Demo -Scope all                # local state + stop agent
Reset-Demo -Scope subscriber -Node n1
```

> **Note on reset depth.** `Reset-Demo` clears local demo state and stops the tracked
> agent. A *full* DB wipe (demo wallets, non-system register Mongo DBs, the
> subscriber's `OrganizationRegisterSubscriptions` rows) is a **node-side** operation —
> run the documented reset recipe on each host (see the `n1-deploy` skill, or
> `docker compose down -v` for an ephemeral node). The command prints a reminder.

---

## Tester runbook (anonymous → credential, real product, no script)

You need a browser and the **Citizen Wallet PWA** on your phone.

1. **Sign up** on the subscriber's web app (email/password or social).
2. **Pair a wallet** — the app nudges you to add a device → `/setup/add-device` →
   enrol on the Citizen Wallet PWA. (This gives you a holder key so the credential
   can reach you.)
3. **Apply** — web app → **New Submissions** (`/new-submissions`) → choose the
   agency's **Assured Identity** service → **Start** → fill the form (your holder
   key fills in read-only automatically) → **Submit**.
4. **Wait** a few seconds — your application is routed to the issuing authority, the
   identity-validator agent approves it, and the credential comes back.
5. **Receive** — your PWA shows the pending-application card, then a welcome takeover
   when the **Assured Identity** credential lands in your wallet. Done.

### Surfaces deliberately NOT on the demo path

- The PWA `/applications` page (a "coming soon" placeholder — it displays in-flight
  applications only; you start applications from the web app).
- The `samples/strathcarron-portal` Blue Badge / Driving-Licence pages (non-functional
  stubs awaiting unlanded backend wiring).

---

## How it maps to the design

- Design: `docs/superpowers/specs/2026-05-31-assured-identity-demo-environment-design.md`
- Spec / plan / tasks: `specs/144-assured-identity-demo/`
- Built on Feature 143 (reverse-stream rendezvous) + F137 cross-node delivery +
  F124/F126/F128 citizen onboarding + the `sorcha-agent` rules/ai engines.

---

## Verified (green run)

> _Record the result of the first full green run on the default node pair here
> (provision + connect + each agent mode + multi-node + a tester loop), with SC
> outcomes, before retiring the legacy `walkthroughs/AssuredIdentity/` scripts._
>
> Status: **pending first green run** (requires the live two-node environment).
