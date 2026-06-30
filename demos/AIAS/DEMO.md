# AIAS — AI-Assisted Identity Assurance Service Demo

A single-node demonstration of the AI-Assisted Identity Assurance Service (AIAS). Provisions the AIAS issuing authority — org, issuer wallet, register, published blueprint — against a local Docker stack, and writes the agent configuration so the AIAS agent can process citizen identity applications.

> **Root-cause note.** Earlier versions of this provisioning script created the AIAS register owned by the bootstrap sysadmin account, while the blueprint was published by the AIAS verification-admin (issuer) wallet. The F142 PublishGate rejected publish with `403 "caller lacks a publish-governance role on register"`, causing downstream participant-publish seal timeouts and a public-org subscription 500. This demo now creates the register owned by the issuer wallet (Pattern A, matching `demos/AssuredIdentity/`), which satisfies the governance gate. See `specs/175-fix-aias-publish-governance/` for the full root-cause analysis.

---

## Prerequisites

- Docker Desktop installed and running.
- A clean (or re-created) Sorcha Docker stack: `docker-compose up -d`.
- PowerShell 7+.

---

## Setup — clean Docker stack

```bash
# From repo root
docker-compose down -v     # clear any prior state
docker-compose up -d       # gateway :80, services on Docker ports
# Wait until http://localhost/health reports healthy (usually 15-30s)
```

---

## Operator runbook

```powershell
# Provision the AIAS authority (single command)
pwsh demos/AIAS/run-demo.ps1

# Optional: skip health-check probe (e.g. if gateway takes longer to start)
pwsh demos/AIAS/run-demo.ps1 -SkipHealthCheck

# Custom gateway URL (e.g. remote stack)
pwsh demos/AIAS/run-demo.ps1 -BaseUrl https://my-stack.example.com
```

The script is **idempotent**: re-running against the same stack reuses the existing org, wallet, and register (by name) and still reaches authority-ready state without errors.

---

## What the script provisions

1. Enables the Sorcha public organisation.
2. Creates the `AIAS Authority` organisation and `verification-admin` user.
3. Creates the `AIAS Issuer Wallet` under the verification-admin user.
4. Registers the verification-admin as a platform participant (wallet link).
5. Mints a **fresh** verification-admin session so the JWT carries `wallet_address`.
6. Creates the `AIAS Authority` register **owned by the issuer wallet** — satisfying the F142 PublishGate for all subsequent publish calls.
7. Publishes the AIAS blueprint to the register (no 403).
8. Publishes the `Verification Analyst` participant onto the register (seals within the normal window — no ~90s timeout).
9. Writes `demos/AIAS/agent/agent-config.json` — the authority-ready signal.

---

## Pass criteria

| Check | Source |
|-------|--------|
| Blueprint publish: 0 × HTTP 403 governance failures | SC-001 |
| Agent config written (`demos/AIAS/agent/agent-config.json`) | SC-002 |
| Participant publish: 0 × ~90s seal timeout | SC-003 |
| Public-org subscription: 0 × HTTP 500 | SC-004 |

---

## How it relates to AssuredIdentity

The AIAS demo is a simpler, single-node provisioning script compared to the multi-node `demos/AssuredIdentity/` demo. Both use the same register-ownership pattern (Pattern A):

```
New-SorchaRegister -OwnerUserId $vAdmin.UserId -OwnerWalletAddress $vWallet.Address
```

AssuredIdentity is the canonical multi-node demo (issuer + subscriber nodes, approval agent, status commands). AIAS is a targeted single-node authority provisioner — provision the authority, then the AIAS agent takes over.

---

## Spec and design references

- Root-cause analysis: `specs/175-fix-aias-publish-governance/research.md`
- Governance entity model: `specs/175-fix-aias-publish-governance/data-model.md`
- Verification guide: `specs/175-fix-aias-publish-governance/quickstart.md`
- Reference pattern (AssuredIdentity): `demos/AssuredIdentity/AssuredIdentityDemo.psm1` (`:171`, `:186`)
