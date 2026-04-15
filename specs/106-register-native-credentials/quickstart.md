# Quickstart — Register-native credential delivery

**Feature**: 106-register-native-credentials
**Phase**: 1 (/speckit.plan)
**Date**: 2026-04-15
**Audience**: Developers verifying Feature 106 end-to-end after implementation

## Prerequisites

- Working docker-compose deployment of Sorcha (all services healthy — verify via `docker ps --format '{{.Names}} {{.Status}}'`)
- PowerShell 7.5+ on your development machine
- `sorcha-cli` built and on PATH, OR the repo root accessible so you can run `dotnet run --project src/Apps/Sorcha.Cli -- ...` directly
- Feature 106 merged to master and deployed to the nodes you're testing against

## Scenario 1 — Single-node end-to-end (User Story 2)

**Goal**: verify that on a single-node `docker-compose up` deployment, a public user can sign up, submit a Verified Citizen application, have it approved, and accept the resulting credential through the MyCredentials PENDING tab.

### Setup

```powershell
# Start the standard single-node Sorcha stack
cd C:\Projects\Sorcha
docker-compose up -d
# Wait for all 13 containers to report healthy
```

### Run the walkthrough

```powershell
# Set up the Verified Citizen walkthrough (creates the issuer org + blueprint)
pwsh walkthroughs/HaipVerifiedCitizen/setup.ps1

# Note the blueprint id and register id from state.json
Get-Content walkthroughs/HaipVerifiedCitizen/state.json | ConvertFrom-Json |
  Select-Object blueprintId, registerId
```

### Sign up a public user in the browser

1. Open `http://localhost/auth/signup` (or `https://n1.sorcha.dev/auth/signup` for the n1 deployment).
2. Click the **Email** tab, fill out the form, submit.
3. After account creation, log in with the same credentials at `/auth/login`.

### Create a wallet

1. On the dashboard, click **CREATE WALLET**.
2. Give it a name (e.g. "Primary Wallet"), accept the default ED25519 algorithm and 12-word mnemonic.
3. Back up the recovery phrase (or ignore for throwaway test accounts).
4. Acknowledge both checkboxes and click **CONTINUE TO WALLET**.

### Submit the Verified Citizen application

1. Click **New Submission** in the sidebar.
2. Find **HAIP Verified Citizen** and click **START**.
3. Fill out the four wizard pages (Name → DOB → Contact → Address), review the Check Your Answers screen, click **SUBMIT**.
4. The page auto-navigates to **My Pending Actions**. No pending action for you yet — you submitted Action 1, Action 2 is pending for the government assessor.

### Approve as the government assessor

Using the walkthrough module helper:

```powershell
Import-Module walkthroughs/modules/SorchaWalkthrough/SorchaWalkthrough.psm1 -Force
$state = Get-Content walkthroughs/HaipVerifiedCitizen/state.json -Raw | ConvertFrom-Json

$assessor = Connect-SorchaUser `
  -TenantUrl $state.tenantUrl `
  -Email $state.roles.govAssessor.email `
  -Password $state.roles.govAssessor.password `
  -OrganizationId $state.roles.govAssessor.organizationId

# List open instances to find yours
$instances = Invoke-SorchaApi -Method GET `
  -Uri "$($state.blueprintUrl)/instances?blueprintId=$($state.blueprintId)" `
  -Headers $assessor.Headers
$instances.items | Select-Object id, @{n='name';e={$_.accumulatedData.name.givenName + ' ' + $_.accumulatedData.name.familyName}}

# Approve the one matching your test name
$instanceId = "..."  # copy from the listing
Invoke-SorchaAction `
  -BlueprintUrl $state.blueprintUrl `
  -InstanceId $instanceId `
  -ActionId '2' `
  -BlueprintId $state.blueprintId `
  -SenderWallet $state.govWalletAddress `
  -RegisterId $state.registerId `
  -Token $assessor.Token `
  -PayloadData @{
    verificationDecision = 'approved'
    reviewerNotes = 'Quickstart verification.'
  }
```

### Accept the credential

Back in the browser:

1. Navigate to **My Credentials** in the sidebar. Click the **PENDING** tab.
2. A card for **VerifiedCitizenCredential** is visible (may take up to a few seconds for the SignalR notification to fire).
3. Click **CLAIM CREDENTIAL** (or open the card and click the claim button).
4. Snackbar: **"VerifiedCitizenCredential stored in your wallet"**.
5. Click the **ACTIVE** tab — the credential is listed there with a green **Active** badge.

### Success criteria

- ✅ Credential appears in PENDING tab within 30 seconds of the CLI approval call (SC-002).
- ✅ After clicking CLAIM CREDENTIAL, credential transitions to ACTIVE (SC-001, SC-004).
- ✅ The issuer's instance transitions to Completed (verify with the assessor's instance list — previously listed as pending Action 3 for citizen participant, now terminal).
- ✅ No page reloads or manual refreshes required — SignalR push drives the UI update (FR-007, SC-005).

### Decline path variant

Repeat the same setup, but on step "Accept the credential" click **DECLINE** instead of **CLAIM CREDENTIAL**:

1. Credential moves to the **DECLINED** tab (or is retained under a declined filter — see the UI wiring for the exact surface).
2. The issuer's instance transitions to **Rejected**.
3. The credential is still visible in the local wallet store (matches FR-015). You can explicitly delete it from the UI if you want.

---

## Scenario 2 — Federated two-node end-to-end (User Story 1)

**Goal**: verify that a holder on node B receives and accepts a credential issued by an assessor on node A, with no direct communication between B and A.

### Setup (new shape)

A new docker-compose file `docker-compose.federation.yml` brings up two Sorcha nodes subscribed to the same register, modelled after the existing `DistributedRegister` walkthrough pattern. Key settings:

- Node A: `sorcha-a-*` containers on internal network `sorcha-a-net`
- Node B: `sorcha-b-*` containers on internal network `sorcha-b-net`
- Shared Peer Service bridge between the two networks
- Single register peer-sync'd to both sides
- Each node exposes its own ports (node A on 8880, node B on 8881)

```powershell
cd C:\Projects\Sorcha
docker-compose -f docker-compose.federation.yml up -d
# Wait for all containers on both nodes to report healthy
```

### Set up issuer org on node A

```powershell
# Initialise node A's CLI profile
sorcha-cli config init --profile node-a --service-url http://localhost:8880 --set-active true

# Bootstrap + create the government assessor org (as in the single-node scenario)
pwsh walkthroughs/HaipVerifiedCitizen/setup.ps1 -Profile node-a
```

### Subscribe node B to the same register

```powershell
# node B subscribes to the register via its own peer service
sorcha-cli config init --profile node-b --service-url http://localhost:8881
sorcha-cli registers subscribe <register-id-from-node-a-state.json> --profile node-b
# Wait for peer sync to catch up — register state should replicate within seconds
```

### Sign up a public user on node B

1. Open `http://localhost:8881/auth/signup` in an **incognito/private browser window** (so there's no bleed-through with the node A session).
2. Register, log in, create a wallet — all on node B.

### Submit the Verified Citizen application on node B

1. **New Submission** → **HAIP Verified Citizen** → fill the form → submit.
2. The submission goes through node B's Blueprint Service, which validates the blueprint (synced from node A via peer replication) and submits the action transaction to node B's validator. The transaction peer-syncs back to node A.

### Approve on node A

```powershell
# Approve from node A's assessor account — CLI or browser on port 8880
# (Same pattern as single-node scenario, but using node A's profile)
```

### Observe on node B

1. Back in the node B browser window, navigate to **My Credentials** → **PENDING** tab.
2. Within 30 seconds of the approval on node A, the credential card appears.
3. Click **CLAIM CREDENTIAL**.
4. Snackbar confirms acceptance; the credential moves to **ACTIVE**.

### Success criteria

- ✅ Credential arrives on node B within 30 seconds of node A's approval (SC-002).
- ✅ Node B never makes an HTTP call to node A during the flow — verify by tailing node A's Blueprint Service logs for the period between approval and acceptance; the only incoming requests should be from node A's own assessor, never from node B's IP.
- ✅ Holder's MyActions also shows the pending action (FR-009) — navigate to **My Pending Actions** on node B before clicking Accept; Action 3 is listed.
- ✅ Issuer's instance (on node A) transitions to Completed after the holder accepts on node B (SC-003).
- ✅ The whole flow completes without any shared database, without any direct RPC, and without any manual coordination — the register is the only shared channel.

### Cross-node observability checklist

While the flow runs, watch:

```powershell
# Node A blueprint service log — should see Action 2 execute + instance transition on accept observation
docker logs -f sorcha-a-blueprint-service

# Node B blueprint service log — should see mirror reconstruction fire on docket:confirmed events
docker logs -f sorcha-b-blueprint-service

# Node B wallet service log — should see inbound credential detection when the issuance tx arrives
docker logs -f sorcha-b-wallet-service
```

Expected log lines on node B:

- `InstanceMirrorReconstructor` creating a mirror row for the new instance when the Action 1 tx replicates (the citizen's own submission goes through node B, so the mirror is created locally at write time).
- `InstanceMirrorReconstructor` advancing the mirror row when the Action 2 tx replicates from node A (the approval transaction arrives via peer sync).
- `InboundCredentialDetector` extracting the credential from the Action 2 transaction and persisting it as `PendingAcceptance`.
- `CredentialStatusChangedEvent` or `InboundActionEvent` with `CredentialOfferId` firing on the SignalR hub.

Expected log lines on node A after the holder accepts on node B:

- Action 3 execute transaction arriving via register peer sync.
- Instance state transitioning from "Action 3 pending" to "Completed".

---

## Scenario 3 — External wallet path regression (User Story 3)

**Goal**: verify that the existing `HaipExternalWallet` path still works unchanged — no regression from Feature 106.

### Run

```powershell
# Single-node shape is fine for this one
cd C:\Projects\Sorcha
pwsh walkthroughs/HaipDrivingLicence/setup.ps1
pwsh walkthroughs/HaipDrivingLicence/run.ps1
```

### Success criteria

- ✅ Walkthrough passes end-to-end on first run.
- ✅ The walkthrough uses the external-wallet OpenID4VCI pre-authorized-code flow to deliver the driving licence credential (inspect the blueprint JSON under `walkthroughs/HaipDrivingLicence/blueprints/` to confirm `targetAudience: "HaipExternalWallet"`).
- ✅ No changes required to the walkthrough or the HAIP service for this path to keep working.

---

## Troubleshooting

### "Credential doesn't appear in PENDING tab"

Check the Wallet Service log for `InboundCredentialDetector` messages. Common failure modes:

- **Skipped: no-recipient-disclosure** → the action 2 blueprint wasn't using `targetAudience: SorchaLocalWallet`. Verify the blueprint JSON and republish if needed.
- **Skipped: decrypt-failed** → the recipient wallet pubkey used at mint time doesn't match the wallet trying to decrypt. Check the late-binding on Action 1 — which wallet did the citizen submit from?
- **Skipped: duplicate** → this credential id already exists in the local store. Check the credentials list for an earlier arrival.
- **Error** → inspect the exception. Common: register service unreachable, blueprint cache stale.

### "Credential appears but the instance doesn't close after Accept"

Check the Blueprint Service log for `ActionExecutionService` handling Action 3. Common failure modes:

- The accept transaction was submitted but the validator rejected it — check validator logs for `VAL_*` errors.
- The instance on the authoritative node doesn't have `CurrentActionIds = [3]` — mirror reconstruction may have lagged. Restart the reconstructor: `docker restart sorcha-blueprint-service`.

### "MyActions shows the pending action on node A but not node B"

Check the `InstanceMirrorReconstructor` on node B. Common failure modes:

- The `docket:confirmed` event didn't fire on node B — verify Redis pub/sub is working (`docker exec -it sorcha-redis redis-cli psubscribe 'docket:*'`).
- The mirror reconstructor is running but can't find the blueprint on node B — verify blueprints are peer-synced to node B (`GET /api/blueprints/{id}` against node B).

---

## Next steps after Feature 106 ships

- **Feature 105** (consumer onboarding) pairs with this — once a new user's first credential arrives, the onboarding flow can guide them to the PENDING tab automatically.
- **Inbox UX polish** — batch operations (bulk accept, bulk decline, filter by issuer) are future work noted in the spec's Out of Scope section.
- **Decline-reason surfacing** — future enhancement to let issuers see why a holder declined (requires privacy review).
