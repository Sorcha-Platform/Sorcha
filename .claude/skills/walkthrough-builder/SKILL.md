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

### Working files stay in the walkthrough/demo directory (REQUIRED)

Every runtime artefact a walkthrough or demo produces — `state.json`, `*-state.json`, logs
(`*.log`), generated `wallet/` / `agent-wallet/` dirs, rendered actor configs, deploy outputs —
MUST be written **inside that walkthrough's / demo's own directory** (or the gitignored `/deploy/`
folder), **never the repo root**. Resolve output paths relative to `$PSScriptRoot` (the script's own
dir), not the current working directory:

```powershell
# WRONG — writes to wherever the operator invoked from (often repo root):
param([string]$StateFile = "./state.json")
# RIGHT — always lands in the demo/walkthrough dir, whatever the CWD:
param([string]$StateFile = (Join-Path $PSScriptRoot "state.json"))
```

Then make sure the path is gitignored (the root `.gitignore` already covers
`walkthroughs/**/state.json`, `walkthroughs/*/wallet/`, `*.log`, `/deploy/`; add a rule if a new
tool's output isn't covered). Runtime files dumped at the repo root are a **bug in the producing
script**, not just something to delete — fix the path resolution. (Reasoning: keeps the working tree
clean, avoids committing runtime state/secrets, and behaves identically on every machine.)

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

#### An existing org does NOT adopt the admin you pass — and the auth limiter looks like a platform fault

Two things bite on a **re-run** and neither bites on a clean node, which is why both survived so long.

**1. `New-SorchaOrganization` reuses an existing org and does not provision its admin.** When the
subdomain already exists the call falls into its duplicate-recovery path and returns
`AdminDirectlyAdded = $false` — the `-AdminEmail` you passed was never given a membership. **11 of 13
setup scripts ignored that return value**, so the admin stays single-org in the PUBLIC org and the
failure surfaces far away as either a `403` on the first org-scoped call (#1427) or

> `Connect-SorchaUser: env@x.local is single-org in 00000000-…-0002, but org <id> was requested`

which reads as an auth bug. **The module now closes this itself** — `New-SorchaOrganization` calls
`Confirm-SorchaOrgMembership` on the recovery path, so callers no longer have to remember. Use that
helper directly if you provision an org some other way. Membership is added via
`POST /organizations/{orgId}/users` (an org identity for an EXISTING platform user); `New-SorchaOrgUser`
(`/users/provision`) creates a NEW platform user and 400s when one already exists.

⚠ **Adoption only happens at org CREATION**, so a brand-new email fails identically against a
pre-existing org — do not diagnose this as stale-user residue.

**2. The auth rate limiter is what breaks a full-suite run (#1533).** The gateway's `authentication`
policy is a **sliding 1-minute window partitioned per IP**, default **60**. Bulk provisioning from one
box saturates it and every setup 429s part-way through — measured on n1: 12 logins at 2s spacing during
suite traffic gave **8 ok / 4 refused**. It is a limiter, not a platform failure, and no amount of
in-script pacing fixes it because the aggregate across sequential scripts is what saturates.

Raise it per deploy via the `RATELIMIT_*` vars in the host `.env` (documented in `.env.example`) —
never by loosening `docker-compose.n1.yml`, which is deliberately tight (#1437). n1 runs
`RATELIMIT_AUTH_PERMIT=1200`. ⚠ Do NOT probe the limiter and then start a run in the same window;
that alone turns a whole suite red.

#### REQUIRED: re-login any session AFTER its wallet is created, so the JWT carries `wallet_address`

This is the single most common cause of walkthrough breakage after the F136 tiered-token + F142 publish-gate work landed. **`wallet_address` is added to the JWT only at login, from the user's first active linked wallet** (`TokenService.AddWalletAddressClaimAsync`). Walkthroughs log in *first*, then create + link the wallet (`New-SorchaWallet` + `Register-SorchaParticipant -WalletAddress`) — so the cached session token has **no `wallet_address` claim**, and every endpoint that authorizes via wallet fails for that stale token. Two confirmed failure modes (triaged 2026-06-02 across ConstructionPermit / TradeFinance / PayloadTests):

- **F142 blueprint publish gate** — `PublishGate` matches the caller's `wallet_address` claim as a substring of the roster member's `did:sorcha:w:{wallet}` subject (`org_id` is a documented fallback but **can't match a wallet-DID**). A token without `wallet_address` ⇒ `403 { "error": "You do not hold a publish-governance role (Owner, Admin, or Designer) on the target register." }` (the governance HARD gate — NOT the `409 REHEARSAL_REQUIRED` soft gate that `-OverrideRehearsal` handles).
- **F085 file download** (`GET /api/v1/wallets/{addr}/files/download`) — authorizes via the caller's wallet; the receiver's stale token ⇒ `403 Forbidden` on download even though the upload + actions succeeded.

**Rule: after `Register-SorchaParticipant` links a user's wallet, re-login that user before they perform any wallet-authorized operation** (create/own a register, publish a blueprint, download a file, etc.). Use the fresh session everywhere downstream:

```powershell
# After the per-role loop has created + linked the owner's wallet:
$ownerSession = Connect-SorchaUser `
    -TenantUrl $env.TenantUrl `
    -Email $users["contractor"].Email `
    -Password $users["contractor"].Password `
    -OrganizationId $orgs.stoniebridge
# (if you cache sessions, overwrite the cache entry too)
# now use $ownerSession.Headers for New-SorchaRegister AND Publish-SorchaBlueprint
```

This is the same pattern AssuredIdentity uses for its issuer-admin publisher. The 403 is NOT the rehearsal soft-gate (that's a `409 REHEARSAL_REQUIRED`, handled by `Publish-SorchaBlueprint -OverrideRehearsal`, default true) — it's the governance HARD gate, and only a `wallet_address`-bearing token clears it. Symptom triaged 2026-06-02 across ConstructionPermit + TradeFinance (both pre-dated F142); fixed in ConstructionPermit by the re-login above.

#### REQUIRED: the register OWNER must be an Administrator, never a Consumer participant

Blueprint publishing requires `CanPublishBlueprints` = **Administrator role OR a `can_publish_blueprint` claim**, AND (F142) the caller's `wallet_address` must be the register's roster owner. So the **same identity** must (a) hold Administrator and (b) own the register wallet. Consumer-role workflow participants (auditors, sales managers, citizens) satisfy neither cleanly. **Make the org admin own + publish the register** (give the org admin a wallet + participant, re-login, use *its* wallet as `-OwnerWalletAddress` and *its* session to publish). Workflow participants stay separate (referenced in the blueprint, not as the register owner). ForestryCertification/TradeFinance originally owned registers with a Consumer participant's wallet → permanent 403; fixed 2026-06-02 by switching ownership to the org admin.

#### REQUIRED: wait for the register-genesis roster to seal before publishing (publish races the seal)

The register's genesis control tx — which records the owner governance roster the F142 gate reads — seals **asynchronously after `New-SorchaRegister` returns**. A blueprint publish issued immediately reads an **empty** roster and fail-closes with the *same* `403 "You do not hold a publish-governance role"`. ConstructionPermit/Forestry got away with it by having other steps between register-create and publish; TradeFinance didn't and 403'd every time. **Use `Wait-SorchaRegisterRoster` before publishing** (the register-genesis analogue of the F145 action-seal cadence):

```powershell
$null = Wait-SorchaRegisterRoster -GatewayUrl $sorchaEnv.GatewayUrl `
    -RegisterId $register.RegisterId -Headers $ownerSession.Headers
```

The 403 wording blames the caller's publish-governance ROLE, which sends you looking at roles and the
`wallet_address` claim — both usually fine. The cause is timing. Whether a walkthrough gets away with
it depends purely on how much work sits between creating the register and publishing.

#### REQUIRED: every org must have its OWN wallet, created by its ADMIN — before the master key

An organisation's canonical wallet is what its issuer DID anchors on (`did:sorcha:org:{address}`) and what
its governance roster identity is matched against. **The platform does not create it.** Its BIP39 recovery
phrase is shown once and never stored, so a service-to-service create generates a phrase with nobody present
to receive it and the organisation can never be recovered — which is exactly what used to happen (#1525).

Pass `-WalletUrl` to `New-SorchaOrganization` and it performs the step for you, by signing in as the admin
it was just given:

```powershell
$org = New-SorchaOrganization -TenantUrl $env.TenantUrl -WalletUrl $env.WalletUrl `
    -Name "Acme Verification Co." -Subdomain "acme-verif" `
    -AdminEmail "ops@acme-verif.test" -AdminPassword $secrets.DefaultPassword `
    -AdminDisplayName "Acme Ops" -AdminEmailVerified
# $org.WalletAddress is now the ORGANISATION's wallet
```

Where an admin session only exists later (orgs created in a loop, or an org that already existed), call the
step explicitly once you have one:

```powershell
$null = New-SorchaOrgWallet -TenantUrl $env.TenantUrl -WalletUrl $env.WalletUrl `
    -OrganizationId $orgId -Headers $adminSession.Headers
```

- **Ordering matters.** It must exist **before** `Set-SorchaOrgMasterKey`, because the issuer DID anchors on
  it — without it there is nothing to anchor a DID document to and `GET /orgs/{id}/did.json` 404s. That was
  #1518, which presented as a timing race and was really a missing step.
- **The org owns the wallet, not the admin.** `POST /api/v1/wallets` takes `organizationId`; the admin
  receives the phrase but the wallet outlives them.
- **A platform admin cannot do it** — refused by design, both at wallet-create and at link. The secret
  belongs to the organisation.
- **Idempotent**, and a second wallet is refused: replacing the canonical wallet orphans every credential
  issued under the old one and every roster entry matched against it.
- **There is no safety net any more.** `OrgWalletReconciliationService` swept every 60s and silently made
  org wallets appear; it is deleted. Miss this step and the failure is visible, which is the point.

#### REQUIRED: a credential-ISSUING org must provision a Feature 083 master key

Any org that issues a native SorchaLocalWallet SD-JWT VC **MUST** call `Set-SorchaOrgMasterKey` for that org in setup (after its session carries `wallet_address`). Without it, `IssuanceKeyService.GetActiveSigningMaterialAsync` returns null and the mint **silently falls back to the org's root wallet key** — producing a credential whose `iss` is a **bare wallet address** (not a `did:`), with **no `kid`** and **no `jwk`** in the JWS header. That credential is **unverifiable**: a cross-register / insurer trust check fails with `TrustEvaluator: issuer signature not verified` (looks like a platform bug; it's a missing setup step).

```powershell
# After the issuer org's session is re-logged with its wallet_address claim:
Set-SorchaOrgMasterKey -WalletUrl $sorchaEnv.WalletUrl `
    -OrganizationId $issuerOrgId -Headers $issuerSession.Headers   # idempotent on 409
```

`Set-SorchaOrgMasterKey` now ALSO calls `POST /api/v1/orgs/{orgId}/issuance-key/ensure`, because a
master key only *enables* the Feature 120 issuance key — it does not derive it, and the org's DID
document is published only as a side effect of a key event. Until then `GET /orgs/{orgId}/did.json`
**404s**, so a walkthrough that must pin the issuer's issuance DID in a trust policy cannot resolve
one before that org's first credential. This stayed hidden for months because these orgs had already
issued credentials; wiping n1 exposed it immediately.

- **Do this:** `ForestryCertification`, `TradeFinance`, `SelfBuildHouse` (their issuer orgs provision master keys).
- **Footgun (don't do this):** `CyberEssentialsUac` / `AssuredIdentity` historically provisioned only **HAIP** enrolment, not a master key — so their blueprint SorchaLocalWallet issuance fell to the bare-wallet `iss` path. HAIP enrolment is for the OID4VCI variant; it does **not** substitute for the F083 master key on the blueprint issuance path.
- **Allowlist interaction:** the F083 master key makes `iss` become `did:sorcha:org:{DERIVED vc-issuance child address}` — which is **different** from the org's operational wallet address. A `trustPolicy.did-allowlist` pinning the *operational* `did:sorcha:org:{wallet}` will then NOT match (no `alsoKnownAs` bridge). See the **`verifiable-credentials` skill → "Org VC-Issuer Signing & DID Anchoring"** for the three-address model and the re-anchor fix; until that lands, pin the *derived* issuance DID (read it back from the issued credential / the org's `did.json` `id`), not the operational one.

#### REQUIRED: PUBLISH participants onto the register, and PROVE their keys resolve

`Register-SorchaParticipant` links a wallet to an identity **in the tenant**. It does **not** put the
participant's public key **on the register**. `Publish-SorchaParticipant` does that, and without it
`POST /registers/{id}/participants/resolve-public-keys` returns `notFound` for every wallet.

Skipping it produces a **four-step silent chain, entirely behind HTTP 202/200**:

1. no resolvable key → **every recipient is skipped** (`Public key not found on register for wallet … — recipient skipped`)
2. no recipients → the action payload carries **no disclosure-group envelope**
3. it therefore **cannot be decrypted** (`payload is neither a disclosure-group envelope nor has a WalletAccess recipient`)
4. so the credential's **claim mappings find nothing and are DROPPED** — `Claim mapping source '/subjectName' has no value in action data; dropping claim` — and the credential mints **with no claims** and is **never delivered**

Every symptom points somewhere else. Step 4's warning names your *schema*, so you go and check the
`sourceField` pointers, which are fine. The wallet simply never receives anything, which reads as a
delivery bug. Found end-to-end while building `CredentialLifecycle` (2026-08-18).

```powershell
$pub = Publish-SorchaParticipant `
    -TenantUrl $env.TenantUrl -OrganizationId $orgId -RegisterId $registerId `
    -ParticipantName "Holder Operator" -OrganizationName "Holder Org" `
    -WalletAddress $wallet.Address -PublicKey $wallet.PublicKey -Headers $session.Headers

# The publish tx seals ASYNC — see the ParticipantSealed cadence mode above.
Wait-SorchaActorReady -Mode ParticipantSealed -TxId $pub.transactionId `
    -RegisterId $registerId -Headers $session.Headers -GatewayUrl $env.GatewayUrl

# PROVE it. This is three lines and it converts an unrecognisable downstream failure
# into a named one at the point of cause.
$resolved = Invoke-SorchaApi -Method POST `
    -Uri "$($env.RegisterUrl)/registers/$registerId/participants/resolve-public-keys" `
    -Body @{ walletAddresses = @($wallet.Address) } -Headers $session.Headers
if (@($resolved.notFound).Count -gt 0) { throw "Keys not on register: $($resolved.notFound -join ', ')" }
```

⚠ **`notFound` here is NOT automatically the cause of a delivery failure.** ConstructionPermit,
ForestryCertification and even the AssuredIdentity recipient that successfully received a credential
all return `notFound` for their state.json role wallets — because those walkthroughs either do not
encrypt to recipients or the addresses in `state.json` are not the ones the action addresses. So:
**use the resolve check as a positive gate in your own setup, not as a diagnosis of someone else's
walkthrough.** Confirm against a walkthrough that both issues and delivers before concluding.

#### Register and blueprint shape limits that only fail at publish

- **Register name ≤ 38 characters.** `"Credential Lifecycle Conformance Register"` (41) is refused
  with `Register name must be 38 characters or less`. Easy to hit with descriptive names.
- **A blueprint needs at least 2 participants** (`Blueprint must have at least 2 participants`). A
  single-action gate still needs someone on the other side — give the second participant a
  disclosure and **no action of its own** if you do not want an extra cadence step per submission.
- **`Publish-SorchaBlueprint` skips the starting-action sender from `$walletMap` regardless** — it
  logs `Skipped <id> (open participant — late-bound at runtime)` even when you passed a wallet for
  it. Do not fight this; supply wallets for the non-starting participants and let the sender bind.

#### A new walkthrough needs an entry in `initialize-secrets.ps1`

`Get-SorchaSecrets -WalkthroughName "my-walkthrough"` throws
`No secrets found for walkthrough 'x'` until the name exists in the `$walkthroughSecrets` map. Add it
there, and — because `initialize-secrets.ps1` refuses to touch an existing file without `-Force` —
add the same key to `walkthroughs/.secrets/passwords.json` by hand rather than regenerating every
other walkthrough's passwords.

#### Cadence is now auto-retried in `Invoke-SorchaAction` (F145)

`Invoke-SorchaAction` wraps its submit POST in `Invoke-SorchaActionPostWithCadenceRetry`, which **retries only the transient `400 "Action N is not a current action"`** (the projector hasn't folded the previous seal yet) up to 15×1s. So you no longer strictly need an explicit `Wait-SorchaActorReady -Mode AwaitingInbox` before every actor switch — the retry self-heals the cadence everywhere. Explicit `AwaitingInbox` gates remain valid (belt-and-braces) and are still clearer in setup.ps1 publish-seal waits. Any non-cadence 400 (schema, auth) is rethrown immediately.

#### Idempotency for cross-walkthrough shared orgs

Some walkthroughs deliberately share an org/identity (Forestry + TradeFinance both use `highland-timber` so a credential issued in one is visible to the other). Re-running one after the other hits `400 "a platform user … already exists"` from `New-SorchaOrgUser`. Participant-user provisioning helpers (e.g. `New-ParticipantUserSession`) MUST catch the `400/409` duplicate and fall through to `Connect-SorchaUser` (the password is deterministic), reusing the existing org-scoped user.

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

## Four PowerShell traps that silently invert a walkthrough's verdict

The first two shipped undetected for months and are invisible on a clean node; the last two
fail loudly but blame the platform.

**An empty string is FALSY.** `/actions/execute` has two 202 shapes: the normal one carries a
populated `transactionId`, but when an action has recipient disclosure groups to encrypt the platform
offloads to a background service and returns **`transactionId = ""`** plus an `operationId`. So

```powershell
if ($WaitForSeal -and $response.transactionId) { ... }   # skips SILENTLY on the offload path
```

the cadence guard the whole framework depends on did nothing — no error, no log line. `Invoke-SorchaAction`
now resolves the id via `GET /api/operations/{operationId}` (its **`transactionHash` IS the tx id**)
and throws rather than continuing unverified.

**`$response.prop` on an ARRAY is member enumeration, and it is truthy.** The idiom
`if ($r.credentials) { $r.credentials } else { $r }` is meant to unwrap "envelope or bare array". On a
bare array PowerShell projects `.credentials` from every element and returns an array of `$null`s —
`Count > 0`, therefore **truthy** — so the envelope branch wins and you get a same-length collection of
nulls. Every downstream match fails and the walkthrough reports "not delivered" for data the platform
returned correctly.

It is **invisible at one element** (a single `$null` is falsy → correct branch) and **appears at two**,
so it presents as passing on a clean node and failing on the second run. Use `Resolve-SorchaCollection`,
which tests for the array FIRST:

```powershell
$items = Resolve-SorchaCollection -Response $walletCreds -PropertyName 'credentials'
```

**The generalisable smell:** any guard whose truthiness depends on a value the *platform* chose —
an empty string, a projected null, a count — needs testing at 0, 1 and 2 elements. "It worked when I
ran it" usually means "I ran it with one".

**`Invoke-WebRequest.Content` is a BYTE ARRAY unless the content type is recognised as text.**
`application/statuslist+jwt` is not, so `.Content.Trim()` dies with
`[System.Byte] does not contain a method named 'Trim'` — which reads like the endpoint returned
something malformed, when it returned exactly the right thing. Decode explicitly:

```powershell
$body = if ($resp.Content -is [byte[]]) { [Text.Encoding]::UTF8.GetString($resp.Content) }
        else { [string]$resp.Content }
```

**`Write-WtInfo ""` throws.** `Cannot bind argument to parameter 'Message' because it is an empty
string` — and because it usually appears inside a `catch` block printing a friendly explanation, it
replaces your diagnostic with a parameter-binding error at exactly the moment you needed the
diagnostic. Use `Write-Host ""` for spacing.

## Testing credentials: four traps that make a walkthrough lie (2026-08-17)

Every one of these produced a *confident wrong verdict* — three said the platform was broken when it
was not, and one hid a real defect. They share a shape: **the harness fed in something other than
what the assertion claimed**, and a green run on a clean node hid it.

### 1. Pin the credential id whenever the assertion depends on WHICH credential

`Get-SorchaCredentialPresentation -CredentialType …` returns the **first** credential of that type.
A wallet accumulates one credential per happy-path run and keeps revoked ones, so from the second run
onward "the revoked credential" is whichever happens to be first — usually an active one.

That produced a false **"a revoked credential is accepted"** security report. The platform was right
throughout; the test presented an active credential and asserted refusal.

```powershell
# WRONG when the assertion is about a specific credential's STATE
$pres = Get-SorchaCredentialPresentation -CredentialType $type -Token $tok

# RIGHT — pin the exact credential, then PROVE you pinned it before asserting the outcome
$pres = Get-SorchaCredentialPresentation -CredentialType $type -CredentialId $cred.id -Token $tok
Assert ($pres.credentialId -eq $cred.id) "presentation is built from the REVOKED credential"
```

**Use the module helpers rather than hand-rolling the snapshot** — three shipped walkthroughs
hand-rolled it and three got it wrong. `Get-SorchaWalletCredentialUri` builds the listing URI with
`?status=All` (the default listing is Active-only, so a revoked credential would be absent from the
"before" set and then read as NEW); `Get-SorchaCredentialIdSnapshot` takes the before-set;
`Wait-SorchaNewCredential` polls for a credential of that type whose id is not in it:

```powershell
$listUri = Get-SorchaWalletCredentialUri -WalletUrl $sorchaEnv.WalletUrl -WalletAddress $addr
$before  = Get-SorchaCredentialIdSnapshot -ListUri $listUri -Headers $h -CredentialType $vct
# ... the action that issues ...
$fresh   = Wait-SorchaNewCredential -ListUri $listUri -Headers $h -CredentialType $vct `
    -ExcludeIds $before -TimeoutSeconds 60
Assert ($null -ne $fresh) "this action issued a NEW credential and it reached the wallet"
# then pin $fresh.id into every downstream presentation
```

`Get-SorchaCredentialIdSnapshot` **throws** on a failed read and deliberately does not default to an
empty set: an empty before-set makes every credential look new, so the guard would go inert at exactly
the moment the wallet read is broken. It matches on **both** `type` and `vct`, because the org listing
returns `type` and the citizen endpoint (`/v1/wallet/credentials`) returns `vct`.

**Assert the subject IS the subject before asserting anything about the result.** Selecting by type
is fine only when any credential of that type genuinely will do.

### 2. The default credential listing is `Active` only

`GET /v1/wallets/{addr}/credentials/` returns Active credentials. A revoked or pending credential is
**not in the list at all** — so pinning by id still finds nothing unless you widen it:

```powershell
GET /v1/wallets/{addr}/credentials/?status=All
```

Freshly-issued credentials sit at `pending-acceptance` for the same reason. The Active-only default is
correct holder-side behaviour (a wallet should not casually hand over a revoked credential); it just
makes the adversarial case — a holder who kept the token and presents it anyway — impossible to build
unless you ask for it explicitly.

### 3b. Scenario ORDER can consume the credential the next scenario needs

`run-revocation.ps1` revokes the only ACTIVE credential in the wallet, so `run-suspension.ps1` run
after it finds nothing to suspend and fails at its first step. Nothing is broken — revocation is
terminal by design and did exactly its job.

Either run suspension **before** revocation, or re-issue with `run-agents.ps1` in between. A suite
runner that just lists scripts in file order will hit this, and the failure lands on the *innocent*
script.

This is why the fallback was removed from `run-revocation.ps1` (PR #1536): it used to drop back to
first-of-type when no ACTIVE credential existed, which turned a missing precondition into an
unrecognisable *"must be in Active or Suspended state"* error one step later, blaming the revoke
endpoint for the selection. It now says which credentials it can see and what state they are in.

### 3. Pick a credential in the state the scenario needs, not the first one

A revocation scenario that revokes `Select-Object -First 1` will, on a re-run, pick an
already-revoked credential and fail at the revoke step with *"must be in Active or Suspended state"* —
looking like a platform fault. Filter on the state you need:

```powershell
$cred = $ofType | Where-Object { $_.status -eq 'active' } | Select-Object -First 1
```

### 4. Know which refusals are synchronous and which hide behind a 202

| Refusal | Shape |
|---|---|
| Credential invalid / revoked / untrusted | **synchronous HTTP 400** at submit |
| Schema violation (`VAL_SCHEMA_004`) | **HTTP 202 + a tx id**, then the tx never seals |

So `-WaitForSeal` timing out is a *schema/validator* refusal, and an immediate 400 is a *credential*
refusal. Asserting `throws` for a schema violation, or a seal timeout for a bad credential, tests the
wrong thing. Read the validator log for the `VAL_*` code rather than inferring from the HTTP status.

### The generalisable rule

A walkthrough asserting "the platform refused X" is only meaningful if it also proves **it submitted
X**. Before filing a platform defect off a failing walkthrough, print what the harness actually sent —
one command usually settles it, and it is cheaper than the write-up you will otherwise have to retract.

## Writing a conformance check (as opposed to a demo)

A demo shows the happy path works. A **conformance check** asks whether the platform's behaviour
matches a specification, and it has to keep asking after something breaks. Reference implementation:
`walkthroughs/CredentialLifecycle/` — 39 checks over the credential status lifecycle, built
2026-08-18 and passing on n1.

**Assert the GATE's verdict, not the platform's self-report.** A wallet status field says what the
platform *believes*; a credential-gated submission says what it *enforces*. Those can differ — a
suspension that never flips a bit changes the status field and nothing else. The gate is also the
only assertion that survives a refactor of the status plumbing.

**Nothing may abort the run.** Make each platform call return a result object rather than throw:

```powershell
function Invoke-Lifecycle(...) {
    try { ...; return [pscustomobject]@{ Ok=$true;  Status=$r.status; HttpStatus=200 } }
    catch { return [pscustomobject]@{ Ok=$false; Error=$_.Exception.Message; HttpStatus=$code } }
}
```

Dying on the first 500 tells you one thing when the run was about to tell you eight. This is not
defensiveness — on the first real run of `CredentialLifecycle` a transient 500 in phase 4 hid six
later phases that were all fine, and the next run passed 39/39 with no change.

**A 5xx is NOT a refusal.** "Reinstating a revoked credential must fail" passes on a 500, but a 500
means the platform *fell over*, not that it *declined*. Score them separately:

```powershell
Check "P5" "reinstating a REVOKED credential is refused" (-not $r.Ok)
Check "P5" "the refusal is a 4xx decision, not a 5xx failure" ($r.HttpStatus -ge 400 -and $r.HttpStatus -lt 500)
```

**Check what you PUBLISH, decoded from the artefact itself.** Read the credential's own
`credentialStatus`, fetch the URLs it names, and decode those bytes — do not ask a convenience API
what it thinks the status is. A verifier you have never met reads the bytes, and asserting that your
reader agrees with your writer proves nothing. That is exactly how #1492 shipped a `bits: 2` header
over a 1-bit array that Sorcha's own checker then misread. If a header declares a width, **assert the
payload is wide enough for it.**

**Two of everything, for anything indexed.** #1491, #1492 and #1502 were all the same shape — the
right operation applied to the wrong entry — and every one is invisible with a single subject. Issue
a second credential and assert it is *unaffected* by the first's status change.

**The failure detail should say what it MEANS, not restate the assertion.** `"got 'Revoked'"` is
weaker than `"suspension is reversible and revocation is not — telling a holder their credential was
revoked when it was suspended is materially misleading"`. Someone reads that line at 2am with no
context.

**Prove the preconditions, at the point of cause.** `setup.ps1` calls `resolve-public-keys` and
throws if the keys are missing, because the alternative is a run that fails four steps later with a
claim-mapping warning pointing at the wrong file. A cheap assertion where the state is created is
worth an hour of log archaeology.

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
| **CredentialLifecycle** | 2 | 3 across 2 blueprints | 1 | **The standard credential conformance check** — issue → active → suspend → reinstate → revoke → terminal, plus W3C + IETF published wire format and index independence. 39 checks, script-based by design (no agents: it must control WHEN each status change happens). Run it after any change to status lists, credential issuance, or the trust/revocation seam. |

## Troubleshooting

| Symptom | Cause | Fix |
|---------|-------|-----|
| Actor hangs, no actions discovered | SignalR not connected, polling too slow | Check logs, reduce `intervalSeconds` |
| "Unresolved variable" error | Missing env var or state.json key | Run `validate` command first |
| Action submission fails 400 | Payload doesn't match schema | Check action name matches blueprint exactly |
| Credential minted with NO claims; `Claim mapping source '/x' has no value in action data` | Participants were never PUBLISHED to the register, so recipients were skipped and the prior action's payload could not be decrypted — the mapping is reading an empty payload, not a wrong pointer | `Publish-SorchaParticipant` + wait `ParticipantSealed`, then assert `resolve-public-keys` returns no `notFound` |
| Credential issued but never arrives in the wallet | Same chain as above (no recipients ⇒ nothing addressed to the holder), OR a circuit breaker opened by unrelated 500s (#1506) | Check for `recipient skipped` and `The circuit is now open` in the blueprint-service log before suspecting delivery |
| Walkthrough "passes" but asserts on a credential from an earlier run | Selected by TYPE in a wallet that accumulates them | Snapshot ids before issuing; require a NEW id; pin `-CredentialId` on the presentation |
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
