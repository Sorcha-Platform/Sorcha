---
name: walkthrough-builder
description: |
  Architects, builds, and runs Sorcha walkthroughs using the autonomous actor agent framework.
  Use when: Creating new walkthroughs, porting existing walkthroughs to actor-based execution, adding actor definitions, creating launcher scripts, or debugging walkthrough execution.
allowed-tools: Read, Edit, Write, Glob, Grep, Bash, mcp__context7__resolve-library-id, mcp__context7__query-docs
---

# Walkthrough Builder Skill

Walkthroughs are end-to-end integration tests and demos for the Sorcha platform. They exercise workflow blueprints with multiple participants across one or more registers.

## Execution Models

### 1. Script-Based (Legacy)
Single-threaded PowerShell script (`run.ps1`) logs in as each participant sequentially.

### 2. Actor-Based (Current)
Each participant runs as an independent `sorcha-agent` process. Actors are stateless, event-driven, and can run on different machines.

**Always prefer the actor-based model for new walkthroughs.**

## Cadence: gating script execution on docket-sealing (REQUIRED)

Script-based walkthroughs race ahead of the validator's docket-build cycle. The `/actions/execute` HTTP response only means "tx accepted into mempool" — the tx is not yet sealed. If the next action submits during the validator's post-seal cleanup window, you can trigger the **docket-monitoring race** (P0 bug — see issue #787) that wedges the register permanently. **This is not theoretical** — it happened to ConstructionPermit on 2026-05-19 and required filing a P0 bug to fix at the validator level.

Real-world actor flow doesn't race because each participant waits for SignalR notification of an inbound sealed transaction before responding. That natural latency (docket dwell + notification + actor cognition) gives the validator ~3-5 seconds between consecutive actions. Scripts do it in 50 ms.

### The fix: `Wait-SorchaActorReady` and the `-WaitForSeal` switch

The shared module provides a `Wait-SorchaActorReady` cmdlet and an opt-in `-WaitForSeal` switch on `Invoke-SorchaAction`. **Every walkthrough script that calls `Invoke-SorchaAction` MUST pass `-WaitForSeal`.** The CI gate doesn't enforce this (yet) but PR reviewers should.

```powershell
# Required shape for script-based walkthroughs:
$response = Invoke-SorchaAction `
    -BlueprintUrl $state.blueprintUrl `
    -InstanceId $instanceId `
    -ActionId "1" `
    -BlueprintId $state.blueprintId `
    -SenderWallet $wallet `
    -RegisterId $state.registerId `
    -Token $session.Token `
    -PayloadData $payload `
    -WaitForSeal            # <-- bridges script cadence to docket cadence
```

`Invoke-SorchaAction -WaitForSeal` polls the F079 lifecycle endpoint (`/api/registers/{registerId}/transactions/{txId}/status`) for the submitted tx until `Active` / `Revoked` / `Superseded`. Timeout 90s by default; override via `-WaitForSealTimeoutSeconds`. The lifecycle endpoint is auth-gated by `CanReadTransactions` — the helper passes the same `Authorization` header used for the submit.

### Other modes

`Wait-SorchaActorReady` supports four modes for the rest of the cadence gates that show up in walkthrough authoring:

| Mode | What it gates on | Use when |
|---|---|---|
| `AfterSubmit` | tx sealed in a docket | After every `Invoke-SorchaAction` (use `-WaitForSeal` for the ergonomic form) |
| `AwaitingInbox` | `instance.currentActionIds` contains the named action | Between actor switches in a script — the equivalent of an actor receiving a SignalR inbox notification |
| `ParticipantSealed` | participant publish tx sealed | After `Publish-SorchaParticipant` in setup.ps1, before saving `state.json` |
| `BlueprintSealed` | blueprint publish tx sealed | After `Publish-SorchaBlueprint` in setup.ps1, before saving `state.json` |

> **Feature 145 made `AwaitingInbox` MANDATORY between actors (was optional).** Action submission is now single-**async**: `/execute` returns `202` (`isAsync`, empty `nextActions`) and the instance advances only when the `InstanceProjector` folds the **sealed** docket — a beat *after* the tx seals (observed ~1–3s local, longer cross-node). The pre-145 synchronous submit advanced the instance inline, so scripts could fire the next actor's action immediately and got away without a gate. Under 145 a script that submits actor B's action right after actor A's `202` races the projector and gets **`Action N is not a current action for instance …` (400)** even though the projection advances correctly moments later. So: after one actor submits, gate the NEXT actor on `Wait-SorchaActorReady -Mode AwaitingInbox -InstanceId … -ActionId <next> -RegisterId … -Headers <nextActor> -GatewayUrl …` before they act. `-WaitForSeal` (AfterSubmit) alone is NOT enough — it waits for the seal, not for the projection to surface the next action. Reference: `walkthroughs/AssuredIdentity/run-phase1-identity.ps1` Step 5.

### Don't save state.json until publishes seal

A second class of failure (TradeFinance on 2026-05-19) was setup.ps1 saving `state.json` immediately after blueprint/participant publish HTTP responses — same issue, the tx hadn't sealed yet. `run.ps1` then starts instantly, tries to execute Action 1, the auth check looks up the participant record, gets a 404 because the tx hasn't sealed, and returns 403.

**In setup.ps1**, after each `Publish-SorchaBlueprint` / `Publish-SorchaParticipant` call, capture the response's `transactionId` and wait for it:

```powershell
$publishResult = Publish-SorchaBlueprint ...
if ($publishResult.transactionId) {
    Wait-SorchaActorReady -Mode BlueprintSealed `
        -TxId $publishResult.transactionId `
        -RegisterId $registerId `
        -Headers $session.Headers `
        -GatewayUrl $sorchaEnv.GatewayUrl
}
```

### Agents (Sorcha.Agent) don't need this

The autonomous agent already gates on SignalR — its `SignalRInboxListener` subscribes to `BlueprintHub.ActionAvailable` and only acts when notified. **Do not retrofit `-WaitForSeal` into agent code paths.** This helper is for script-based walkthroughs only (the two execution models converge on the same cadence: agents do it via SignalR events, scripts do it via polling).

## Project Locations

```
walkthroughs/
├── modules/SorchaWalkthrough/     # Shared PowerShell module (27 functions)
├── <WalkthroughName>/
│   ├── setup.ps1                  # Creates orgs, wallets, participants, registers, blueprints
│   ├── run.ps1                    # Legacy script-based execution (keep for detailed testing)
│   ├── run-agents.ps1             # Actor-based launcher
│   ├── actors/                    # Actor definition JSON files
│   │   ├── <role>.json            # One file per participant
│   │   └── README.md              # Actor documentation
│   ├── *-template.json            # Blueprint template(s)
│   ├── data/                      # Scenario payload data
│   └── state.json                 # Generated by setup.ps1 (git-ignored)

src/Apps/Sorcha.Agent/             # Actor agent CLI
tests/Sorcha.Agent.Tests/          # Agent unit tests
```

## Creating a New Walkthrough

### Step 1: Design the Blueprint

Define participants, actions, schemas, routes, and conditions in a JSON template file. See `references/patterns.md` for the blueprint template structure.

**Form layout rules** — when an action's schema has many fields, split the form for readability:

| Prop count | Layout |
|---|---|
| ≤ 7 | Flat schema, no sections needed |
| 8–10 | Single page with `x-sections` grouping related fields |
| > 10 | Multi-page wizard via `x-pages`, each page with `x-sections` |

**Exception:** reviewer/approver actions should stay single-page even at 10+ props — the whole point is seeing all context on one screen. Use `x-sections` to group, not `x-pages`.

`x-pages`/`x-sections` are render-only — they don't affect `properties`, `required`, or the submitted payload, so walkthrough runners keep working without script changes. See `references/patterns.md` for the JSON shape. Reference implementations: `FormCoverage/form-coverage-template.json`, `HealthDeclaration/health-declaration-template.json`, `SelfBuildHouse/planning-permission-template.json`.

### Step 2: Write setup.ps1

Use the shared module functions:

```powershell
$modulePath = Join-Path $PSScriptRoot ".." "modules" "SorchaWalkthrough" "SorchaWalkthrough.psm1"
Import-Module $modulePath -Force

# Initialize environment.
# Profiles: gateway (local Docker :80), direct, aspire, n1.
# Deploy-anywhere: pass -GatewayUrl to target ANY node by URL without adding a profile
# enum — all service URLs derive as "{GatewayUrl}/api". Use this for tiny, a second
# n-node, a colleague's box, etc. (added F145):
#   Initialize-SorchaEnvironment -GatewayUrl "http://tiny:8090"   # overrides -Profile
# Setup scripts thread it through: `pwsh setup.ps1 -GatewayUrl http://tiny:8090`.
$env = Initialize-SorchaEnvironment -Profile $Profile
$secrets = Get-SorchaSecrets -WalkthroughName "my-walkthrough"

# Create orgs, wallets, participants
$admin = Connect-SorchaAdmin -TenantUrl $env.TenantUrl -Secrets $secrets
$wallet = New-SorchaWallet -WalletUrl $env.WalletUrl -Headers $admin.Headers -Algorithm "ED25519"
Register-SorchaParticipant ...
Publish-SorchaParticipant ...

# Create register and publish blueprint.
# New-SorchaRegister is idempotent: if the current user is already subscribed
# to a register with the same display name it's reused (Reused=$true in result),
# and the owner org is auto-subscribed on fresh creation so re-running setup.ps1
# doesn't produce duplicates. Cross-peer discovery is not yet supported —
# lookup is current-user-only.
$register = New-SorchaRegister ...
Publish-SorchaBlueprint -TemplatePath "./my-template.json" -WalletMap $walletMap ...

# Save state
$state | ConvertTo-Json -Depth 10 | Set-Content "state.json"
```

#### REQUIRED: provision org operators as org-scoped users — never public (no multi-org)

**Org admins / operators (analysts, officers, issuers) MUST be created single-org.** Do NOT register them as public users. A public user (`Register-SorchaPublicUser`) who is then added to an org via `New-SorchaOrganization -AdminEmail <that email>` becomes **multi-org**, which forces org-selection on login and makes the **OAuth2 password grant return 401** (the grant has no org-selection step) — the #1 cause of walkthrough auth flakiness.

Use the **sysadmin → org → org-scoped operator** hierarchy. `New-SorchaOrganization` with a *fresh* email + password provisions the operator directly in that org only (no public account, no invitation, no email loop):

```powershell
$admin = Connect-SorchaAdmin -TenantUrl $env.TenantUrl -Secrets $secrets   # bootstrap SystemAdmin

# Org + its operator in one call. Operator exists ONLY in this org → single-org → OAuth grant works.
$org = New-SorchaOrganization -TenantUrl $env.TenantUrl -Headers $admin.Headers `
    -Name "Acme Verification Co." -Subdomain "acme-verif" `
    -AdminEmail "ops@acme-verif.test" -AdminPassword $secrets.DefaultPassword `
    -AdminDisplayName "Acme Ops" -AdminEmailVerified

# Log in AS that operator (single-org → direct token, no org-selection):
$ops = Connect-SorchaUser -TenantUrl $env.TenantUrl -Email "ops@acme-verif.test" -Password $secrets.DefaultPassword
```

- `-AdminEmailVerified` requires the installation to enable `Platform:AllowAdminVerifiedUserCreation` (dev + n1 set it; **production does not**). Without it, omit the switch and verify via the admin email-verify endpoint.
- **ANTI-PATTERN (do not do this):** `Register-SorchaPublicUser ops@… ; New-SorchaOrganization -AdminEmail ops@…` → multi-org operator → 401 on the password grant.
- **Citizens / public submitters are the deliberate exception** — they ARE public users (`Register-SorchaPublicUser`): a citizen belongs to the public org and is late-bound into the workflow. Only *org operators* use the org-scoped path.

#### Foot-gun: do NOT include open participants in `$walletMap`

If the blueprint has a participant that is the sender of an `isStartingAction: true` action (a citizen, applicant, public submitter, etc.), that participant is **late-bound** at runtime — its `walletAddress` MUST be null in the published blueprint, and your `$walletMap` MUST NOT contain an entry for it.

Including an open participant in `$walletMap` causes `Publish-SorchaBlueprint` to bake a `walletAddress` into the blueprint, which trips the strict equality check at `ActionExecutionService.cs:196-216` and rejects every real public submitter with:

> `Wallet X is not authorized to execute action 1. This action requires participant 'citizen' with wallet 'Y'.`

The error points at the wallet, not at the cause. The cause is your `$walletMap`.

**Correct shape for citizen-identity walkthroughs (AssuredIdentity Phase 1):**

```powershell
# citizen is late-bound — DO NOT add it
$walletMap = @{
    "verification-analyst" = $verificationWallet.Address
    # "citizen" is intentionally absent — late-bound at runtime
}
```

**Correct shape for credential-bootstrapped flows (AssuredIdentity Phase 2 driving licence):**

```powershell
# citizen is late-bound by whoever presents a valid AssuredIdentityCredential
$walletMap = @{
    "licensing-officer" = $licensingWallet.Address
    # "citizen" is intentionally absent
}
```

See the `blueprint-builder` skill's "Open Participants & Late Binding" section for the full contract. The publish-time guardrail `VAL_BP_010` (shipped in Feature 103 wave 2, PR #269) rejects the bad shape at publish time with an actionable error instead of surfacing a runtime mystery, and `Publish-SorchaBlueprint` in the walkthrough shared module auto-skips patching wallet addresses onto open-sender participants (Feature 103 wave 9) — so including them in `$walletMap` is harmless (the publish step silently omits them with a "Skipped X (open participant — late-bound at runtime)" log line). The shape rule still applies forever after, but walkthrough authors no longer have to remember to filter the map themselves.

### Step 3: Create Actor Definitions

One JSON file per participant in `actors/`:

```json
{
  "actor": {
    "name": "role-name",
    "description": "What this actor does"
  },
  "connection": {
    "gatewayUrl": "http://localhost",
    "registerId": "{{registerId}}",
    "credentials": {
      "email": "{{roles.role-name.email}}",
      "password": "$env:ROLE_PASSWORD",
      "organizationId": "{{roles.role-name.organizationId}}"
    },
    "walletAddress": "{{roles.role-name.walletAddress}}"
  },
  "inbox": {
    "signalR": { "enabled": true },
    "polling": { "enabled": true, "intervalSeconds": 20 }
  },
  "mode": "rules",
  "rules": [
    {
      "actionName": "Action Name From Blueprint",
      "decision": "approve",
      "payload": {
        "field1": "value matching schema",
        "field2": 123
      }
    }
  ],
  "logging": {
    "level": "Information",
    "actionLog": "./logs/role-name-actions.jsonl"
  }
}
```

### Step 4: Write run-agents.ps1 Launcher

```powershell
# Load state
$state = Get-Content $StatePath -Raw | ConvertFrom-Json

# Create blueprint instance(s)
$instance = Invoke-SorchaApi -Method POST -Url "$blueprintUrl/instances/" `
    -Headers $headers -Body @{
        blueprintId = $state.blueprintId
        registerId  = $state.registerId
        tenantId    = $state.organizationId
    }

# Validate instance created
if (-not $instance?.id) { Write-Error "Failed to create instance."; exit 1 }

# Set password env vars from state
[Environment]::SetEnvironmentVariable("ROLE_PASSWORD", $state.roles.'role-name'.password)

# Launch actors
$agentArgs = @("run", "--project", $agentProject, "--", "run", "--config", $configPath, "--state", $StatePath)
$proc = Start-Process -FilePath "dotnet" -ArgumentList $agentArgs ...

# Wait for completion, cleanup, summary
```

**Important PowerShell patterns:**
- Use `$agentArgs` not `$args` (reserved variable)
- Use `$sorchaEnv` not `$env` (avoids confusion with `$env:` provider)
- Validate instance creation before launching agents
- Clean up env vars on exit

## Actor Definition Patterns

### Single-Register Walkthrough
Each actor has rules for their actions. One `registerId` in the connection.

**Examples:** ConstructionPermit (5 actors, 6 actions), PayloadTests (2 actors, 2 actions)

### Multi-Register Walkthrough
Actors span registers — their inbox receives actions from all subscribed registers. The `registerId` in connection config is for action submission context, but inbox discovery is wallet-scoped across all registers.

**Examples:** SelfBuildHouse (7 actors, 14 actions, 2 registers), TradeFinance (6 actors, 10 actions, 2 registers)

### Cross-Register Credential Chains
When Blueprint A issues a VC and Blueprint B requires it:
1. Create both instances at startup
2. All actors start listening immediately
3. Blueprint B's action with `credentialRequirement` blocks until the VC exists
4. Platform validates credentials automatically — actors remain stateless

No actor logic needed for cross-register ordering.

### File Upload (preActions)
For actions with file-reference fields, use `preActions`:

```json
{
  "actionName": "Send File",
  "decision": "approve",
  "preActions": [
    {
      "type": "file-upload",
      "config": {
        "fieldName": "attachment",
        "filePath": "./files/report.pdf"
      }
    }
  ],
  "payload": { "message": "File attached" }
}
```

Generated test files (no `filePath`):
```json
{ "type": "file-upload", "config": { "fieldName": "attachment", "sizeBytes": 1024, "seed": 85 } }
```

### AI Mode
For non-deterministic, contextual responses:

```json
{
  "mode": "ai",
  "ai": {
    "promptFile": "./prompts/persona.md",
    "model": "claude-sonnet-4-6",
    "temperature": 0.3
  }
}
```

Requires `ANTHROPIC_API_KEY` environment variable. The AI engine validates generated payloads against the action schema before submission.

## Authentication Models

### Per-Role Credentials (Recommended)
Each participant has their own email/password stored in `state.json`:
```json
"credentials": {
  "email": "{{roles.role-name.email}}",
  "password": "$env:ROLE_PASSWORD",
  "organizationId": "{{roles.role-name.organizationId}}"
}
```
Used by: ConstructionPermit, TradeFinance

### Single-Admin Delegation
All actors share one admin identity, differentiated by wallet address:
```json
"credentials": {
  "email": "$env:ADMIN_EMAIL",
  "password": "$env:ADMIN_PASSWORD",
  "organizationId": "{{organizationId}}"
},
"walletAddress": "{{wallets.role-name}}"
```
Used by: SelfBuildHouse

## Variable Resolution

- `$env:VAR_NAME` — resolved from environment variables at load time (secrets)
- `{{placeholder}}` — resolved from state.json (IDs, addresses, emails)
- Values are JSON-escaped to prevent injection

## Cross-Machine Deployment

To run actors on a remote machine:
1. Copy actor JSON file(s) and `state.json`
2. Change `gatewayUrl` to point to the remote Sorcha instance
3. Set password environment variables
4. Run: `sorcha-agent run --config actor.json --state state.json`

Create `*-remote.json` variants with `gatewayUrl: "https://n1.sorcha.dev"` for convenience.

## Validation

Before running, validate actor configs:
```bash
sorcha-agent validate --config actor.json --state state.json
```

Checks: JSON structure, variable resolution, credential connectivity, SignalR reachability.

## HAIP Walkthroughs (External Wallet)

The agent supports HAIP wallet commands for OpenID4VCI/OpenID4VP flows with external wallets:

### Agent HAIP Commands

```bash
# Receive a credential via OID4VCI pre-authorized code flow
sorcha-agent haip receive --offer-uri <uri> --wallet-dir ./wallet

# Present a credential via OID4VP direct_post
sorcha-agent haip present --request-uri <uri> --credential <type> --disclose <claims> --wallet-dir ./wallet
```

### HAIP Walkthrough Structure

```
walkthroughs/HaipIdentityAttestation/   # Simple — OID4VCI issuance only
├── setup.ps1                            # Trust anchor, Government org, citizen user
├── run.ps1                              # Create offer → agent receives credential
├── actors/citizen.json                  # Actor def with haip section
└── wallet/                              # Generated — holder keys + credentials

walkthroughs/AssuredIdentity/            # Feature 107 — canonical citizen identity + licence chain
├── setup.ps1                            # Gov + DLA orgs, shared register, both blueprints
├── run.ps1                              # Full Phase 1 + Phase 2 orchestrator
├── run-phase1-identity.ps1              # AssuredIdentityCredential issuance
├── run-phase2-licence.ps1               # Driving Licence credential chain (OID4VP + OID4VCI)
├── run-agents.ps1                       # Unattended verification-analyst + licensing-officer rules-mode
├── run-multi-peer.ps1                   # Cross-peer smoke (non-blocking measurement)
├── actors/                              # citizen.json + verification-analyst.json + licensing-officer.json
├── blueprints/                          # assured-identity.json + driving-licence.json
└── wallet/                              # Generated — holder keys + both credentials
```

### Key Setup Patterns for HAIP Walkthroughs

**Trust anchor provisioning** — required before HAIP credential issuance:
```powershell
# Provision trust anchor (once per tenant)
Invoke-SorchaApi -Method POST `
    -Uri "$($sorchaEnv.GatewayUrl)/api/v1/trust/tenants/$tenantId/provision" `
    -Headers $sysAdmin.Headers -Body @{}

# Enrol org as HAIP issuer
Invoke-SorchaApi -Method POST `
    -Uri "$($sorchaEnv.GatewayUrl)/api/v1/trust/tenants/$tenantId/orgs/$($wallet.Address)/enrol" `
    -Headers $sysAdmin.Headers `
    -Body @{ orgPublicKeyBase64 = $wallet.PublicKey; orgDisplayName = "Org Name" }
```

**Credential offer creation** — creates the offer the agent redeems:
```powershell
$offerResult = Invoke-SorchaApi -Method POST `
    -Uri "$($sorchaEnv.GatewayUrl)/api/v1/offers" `
    -Headers $session.Headers `
    -Body @{
        issuerWalletAddress = $walletAddress
        tenantId = $tenantId
        credentialType = "VerifiedIdentityCredential"
        claims = @{ givenName = "Alice"; familyName = "O'Brien" }
        disclosablePaths = @("givenName", "familyName", "/address/locality")
    }
# offerResult.credentialOfferUri is the URI for the agent
```

**Presentation request creation** — for OID4VP verification:
```powershell
$presRequest = Invoke-SorchaApi -Method POST `
    -Uri "$($sorchaEnv.GatewayUrl)/api/v1/verifier/requests" `
    -Headers $session.Headers `
    -Body @{
        credentialType = "VerifiedIdentityCredential"
        requiredClaims = @("givenName", "familyName", "dateOfBirth")
    }
# presRequest.requestUri is the URI for the agent
```

### Critical HAIP Configuration Notes

| Issue | Cause | Fix |
|-------|-------|-----|
| Agent can't reach issuer metadata | `Haip:IssuerUrl` in docker-compose uses Docker-internal hostname (`api-gateway:8080`) | Change to `http://127.0.0.1` — agent runs on the host, not inside Docker |
| Trust endpoints return 404 through gateway | No YARP route for `/api/v1/trust/*` | Add `"trust-api"` route to `tenant-cluster` in API Gateway `appsettings.json` |
| Credential has no claims | Credential endpoint doesn't know which offer was redeemed | AccessTokenStore maps Bearer token → offer ID → claims. Ensure `StoreAsync` is called in token endpoint |
| Issuer key unresolvable by verifier | No x5c chain or DID resolver configured | Dev mode: issuer JWK is embedded in JWS header. Production: use x5c chains from spec 096 |
| Offer creation returns 403 | Internal endpoints use `RequireService` policy | Relaxed to `RequireAuthorization()` for walkthrough access. Production should use service principal tokens |

### HAIP Walkthrough Chaining

The canonical citizen-identity walkthrough (`AssuredIdentity`) chains the two phases in a single state file, so Phase 2 reads `state.json` for the citizen wallet + HAIP wallet-dir produced by Phase 1 directly:

```powershell
# In run-phase2-licence.ps1:
$state = Get-Content $stateFile -Raw | ConvertFrom-Json
$walletDir = Join-Path $scriptDir "wallet"
$identityCredPath = Join-Path $walletDir "credentials/AssuredIdentityCredential.sdjwt"
if (-not (Test-Path $identityCredPath)) {
    Write-WtFail "No AssuredIdentityCredential in the wallet. Run run-phase1-identity.ps1 first."
    exit 1
}
# The citizen wallet-dir holds the presented credential; sorcha-agent haip
# present reads it directly.
```

### Multi-peer smoke pattern

For cross-peer delivery measurement (Feature 106 register-native path), ship a **non-blocking** smoke harness — runs a full federation, emits findings markdown on every run regardless of outcome, never fails the surrounding process. Reference shape: `AssuredIdentity/run-multi-peer.ps1` + `docker-compose.federation.yml`. Per FR-039 the smoke is measurement tooling, not a gate — exit 0 on every path; the findings document carries the actual status (`pass` / `degraded-pass` / `fail` / `env-failure`).

### Playwright Screenshot Tests

HAIP walkthroughs include Playwright tests that capture UI screenshots for all user paths:

```bash
dotnet test tests/Sorcha.UI.E2E.Tests/ --filter "Category=HaipScreenshots"
```

12 tests capturing: admin dashboard, org management, gov admin wallets, council presentations form, citizen credentials, and HAIP metadata endpoints.

**Multi-org login pattern**: The Sorcha UI renders org selection cards within the login page (SPA, URL stays on `/auth/login`). Tests must wait for the org name text to appear as a clickable element, not wait for URL change:
```csharp
var orgCard = Page.GetByText(orgName, new() { Exact = false });
await orgCard.First.WaitForAsync(new() { Timeout = TestConstants.PageLoadTimeout });
await orgCard.First.ClickAsync();
```

## Existing Walkthroughs Reference

| Walkthrough | Actors | Actions | Registers | Key Feature |
|-------------|--------|---------|-----------|-------------|
| ConstructionPermit | 5 | 6 | 1 | Conditional routing, JSON Logic calculations, VCs |
| PayloadTests | 2 | 2 | 1 | File upload preActions, chunked transfer |
| SelfBuildHouse | 7 | 14 | 2 | Cross-register VCs, credential chains, staged inspections |
| TradeFinance | 6 | 10 | 2 | Cross-register VCs, dispute loops, 4 orgs |
| HaipIdentityAttestation | 1 (agent) | N/A | N/A | OID4VCI pre-auth code flow, SD-JWT VC with cnf |
| AssuredIdentity | 3 (citizen + verification-analyst + licensing-officer) | 7 across 2 blueprints | 1 | Feature 107 — canonical citizen identity (5-page wizard, id-card review, optional portrait) + driving licence chain (OID4VP present + OID4VCI issue) + unattended rules-mode agents + cross-peer smoke |

## Troubleshooting

| Symptom | Cause | Fix |
|---------|-------|-----|
| Actor hangs, no actions discovered | SignalR not connected, polling too slow | Check logs, reduce `intervalSeconds` |
| "Unresolved variable" error | Missing env var or state.json key | Run `validate` command first |
| Action submission fails 400 | Payload doesn't match schema | Check action name matches blueprint exactly |
| Credential requirement blocks | VC not yet issued by upstream action | Expected — actor waits until VC exists |
| Auth fails across registers | Org not subscribed to register | Check setup.ps1 subscriptions |
| Agent: "No such host" on metadata fetch | IssuerUrl uses Docker hostname | Set `Haip__IssuerUrl=http://127.0.0.1` in docker-compose |
| Agent: "No matching credential" | Credential type mismatch or wallet dir wrong | Check `--wallet-dir` points to correct location and credential type matches exactly |
| Walkthrough: secrets not found | Missing entry in passwords.json | Add `haip-identity` / `haip-licence` entries to `walkthroughs/.secrets/passwords.json` |
| Walkthrough: org subdomain taken | Re-running setup without volume reset | Use `docker compose down -v` for clean slate, or use `-Force` flag |
| Walkthrough: Action N fails 400 for the same participant on every scenario | Late-bound participant reuse hitting VAL_BP_002 via a broken Tier 3 chain lookup (incident 2026-04-20) | Check validator logs for "no prior in-instance binding". Confirm `GET /api/query/instance/{id}/transactions/{registerId}` returns 200 with a non-empty list. If empty, inspect MongoDB: `MetaData.InstanceId` must be non-null on sealed txs. See `n1-deploy` skill → "Validator-pipeline changes — end-to-end probe". |
| Walkthrough: re-running after n1 reset but setup keeps state from last run | State files (state.json) persist between resets, pointing at deleted registers/users | Before re-running: `find walkthroughs -name state.json -delete`. The script's idempotency only works against state that still exists server-side. |
| Walkthrough: Action N times out at 60s on `/actions/execute` with "Transaction not confirmed" | Script raced ahead of docket-seal; the previous action's tx is mid-cleanup at the validator and the new tx triggers the docket-monitoring race (P0 issue #787). Register is now wedged — restart won't help; new txs on this register never seal. | Pass `-WaitForSeal` on every `Invoke-SorchaAction` call (see "Cadence" section above). Existing wedged register needs the underlying validator bug fixed, or the register replaced (the wedge survives validator restart because the stuck tx is persisted in the mempool). |
| Walkthrough: Action 1 returns 403 immediately (6 ms response) on a fresh setup, no rate-limit warning | setup.ps1 saved state.json before the participant/blueprint publish txs had sealed; run.ps1 starts instantly, auth check looks up the participant record, 404 upstream becomes 403 at auth layer | Add `Wait-SorchaActorReady -Mode BlueprintSealed` / `ParticipantSealed` in setup.ps1 after each publish, before writing state.json. |

## Running against n1 (ground-truth verification)

The local `docker-compose` stack is fine for fast iteration, but it shares code paths with tests — a change can pass every unit/integration test yet still break on n1 because of a layer the tests don't cover (DI wiring, registration of endpoints, docket-seal projections, Docker image staleness).

**Ground-truth rule:** A walkthrough that completes all scenarios **on n1.sorcha.dev** is the cheapest full-stack regression test we have. Before claiming a validator-pipeline change is done:

1. Merge the PR so Docker Publish runs.
2. Pull the affected service images on n1 (`docker compose pull <service> && up -d --force-recreate <service>`).
3. Delete local `walkthroughs/**/state.json` so setup provisions fresh orgs/registers.
4. Run the walkthrough against n1 — if it completes, the change holds end-to-end.

If the walkthrough still fails at the same action, read the **register-service access log** first (`docker logs sorcha-register-service --since 3m | grep 'query/'`). An empty-list response on a 200 is a persistence-projection gap, not a missing fix.
