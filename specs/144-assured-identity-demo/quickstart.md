# Quickstart: Assured Identity Demo Environment

Seed for `demos/AssuredIdentity/DEMO.md`. Two audiences: the **operator** who stands the demo up, and the **tester** who walks it through real UIs.

---

## Operator — stand it up

**Prereqs**: both installations deployed + healthy on the current `:latest` (issuer NAT'd owner, subscriber public); `deploy/keys.env` populated (per-installation JWT keys + sysadmin password); `sorcha-agent` available on PATH; for `ai` mode, `ANTHROPIC_API_KEY` set.

```powershell
# 0. One-time: copy the inventory template and edit for your installations
Copy-Item demos/AssuredIdentity/demo-nodes.example.json ./demo-nodes.json
#   edit ids / gateways / installationNames (tiny + n1 are the defaults)

Import-Module demos/AssuredIdentity/AssuredIdentityDemo.psm1

# 1. Provision the issuing authority on the issuer node (default agent mode = rules)
New-IssuingAuthority -IssuerNode tiny -AgencyName "Strathcarron Identity Authority"
#   → org + analyst + advertised DevMode register + published blueprint (issuerName injected)
#   → launches the deterministic approval agent

# 2. Connect a public subscriber — BLOCKS until a tester can actually apply
Connect-Subscriber -SubscriberNode n1
#   → subscribes n1's public org, then gates on: subscription Active
#     ∧ sync-state CaughtUp ∧ blueprint present in /blueprints/published
#   → returns Ready (or NotReady+reasons on timeout — retry)

# 3. Confirm the demo is tester-ready at a glance
Get-DemoStatus
#   → verdict: Ready, per-node signals
```

**Hand the tester** the subscriber's web address. That's it.

### Variations
- **AI assessor**: `New-IssuingAuthority -AgentMode ai` (requires `ANTHROPIC_API_KEY`; a slow/failed decision shows as "agent idle / decision pending" in `Get-DemoStatus` — retry or re-run as `rules`/`human`).
- **Human analyst**: `New-IssuingAuthority -AgentMode human` → no agent; follow the printed "log into the issuer as the analyst, approve Action 2" steps.
- **Rebrand**: re-run step 1 with a different `-AgencyName` (then `Reset-Demo` if you want a clean slate first). The new name appears on the org, register, and the credential.
- **Deep workflow change**: amend the published blueprint in the real Designer (Describe→Understand→Rehearse→Go-live) and republish to the same register — identity stays intact.
- **More nodes**: add a subscriber to `demo-nodes.json` and run `Connect-Subscriber -SubscriberNode <id>` again.

### Reset
```powershell
Reset-Demo -Scope all          # clean both sides back to pre-provision
Reset-Demo -Scope subscriber -Node n1
```

---

## Tester — anonymous → credential, through the real product

No script. Just a browser (and the Citizen Wallet PWA on your phone).

1. **Sign up** on the subscriber web app (email/password or social).
2. **Pair a wallet**: the app nudges you to add a device → `/setup/add-device` → enrol on the Citizen Wallet PWA. (Gives you a holder key so the credential can reach you.)
3. **Apply**: web app → **New Submissions** (`/new-submissions`) → pick **<agency name>**'s Assured Identity service → **Start** → fill the form (your holder key fills in read-only automatically) → **Submit**.
4. **Wait** ~a few seconds: your application is routed to the issuing authority, the identity-validator agent approves it, and the credential comes back to you.
5. **Receive**: your PWA shows the pending-application card, then a welcome takeover when the **Assured Identity** credential lands in your wallet. Done.

---

## Acceptance checkpoints (map to Success Criteria)

| Check | SC |
|---|---|
| Steps 1–3 (operator) reach `Ready` in ≤10 min from clean | SC-001 |
| Tester steps 1–5 complete in ≤5 min, no "service unavailable" | SC-002 |
| Re-run `New-IssuingAuthority` twice → no duplicate authority | SC-003 |
| `-AgencyName "X"` → credential issuer shows "X", no manual edits | SC-004 |
| All three `-AgentMode` values → credential reaches the wallet | SC-005 |
| Second `Connect-Subscriber` node → tester there completes loop | SC-006 |
| `Get-DemoStatus` verdict matches tester success every time | SC-007 |
| After graduation, no legacy walkthrough script remains | SC-008 |
