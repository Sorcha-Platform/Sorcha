# Resume: Two remaining chain-integrity bugs + latent server-side issues

The credential-delivery gap that blocked most of April 20's walkthrough work is
closed. On n1, TradeFinance A/C pass, ConstructionPermit A/C pass, and the
SelfBuildHouse + PropertyInspection fixes are in master but unrun. Two shaped
failures remain in the B scenarios and two latent server-side bugs are still
unaddressed behind the DevMode workaround.

## Paste the prompt below after `/clear`

```
I'm picking up the Sorcha n1 walkthrough work from 2026-04-20. Three PRs
merged today (#332, #333, #334) cleared the Feature 106 credential-delivery
gap across TradeFinance + the three council-universe walkthroughs:
  - Register creation now passes -DevMode so issuance-plaintext is accepted
    by the InboundCredentialDetector rather than dropped as a DAD security
    signal.
  - Shared Get-SorchaCredentialPresentation helper auto-accepts
    Status=PendingAcceptance credentials before the Active fetch (Feature 106
    Wave C handshake).

Invoke these skills up-front:
  - superpowers:systematic-debugging  (investigation framework; hypothesis last)
  - n1-deploy                          (SSH, logs, MongoDB, pull/recreate gates)
  - walkthrough-builder                (scenario structure, state.json, run scripts)
  - blueprint-builder                  (routes, cycles, credentialRequirements,
                                        late-binding chain anchoring)
  - mongodb                            (per-register db: sorcha_register_<id>)

## Current n1 state

Register IDs from the 2026-04-20 afternoon run are in
`walkthroughs/<name>/state.json` (see `ConstructionPermit` and `TradeFinance`).
Both n1 trade register + Construction Permit register have DevMode=true.
Three walkthroughs (TradeFinance, ConstructionPermit) have been exercised.
SelfBuildHouse + PropertyInspection have the fixes in their setup.ps1
but have never been run.

## Failure 1: TradeFinance Scenario B — VAL_CHAIN_FORK on dispute resubmit

**Shape:** Action 6 returns DISPUTED; walkthrough resubmits Action 5 as
sales-mgr (intended cycle back for corrected invoice). Validator rejects
with:

  VAL_CHAIN_FORK: Fork detected: 1 existing transaction(s) already
  reference previous transaction 'c128c0cf...' in register '0356a161...'

**Root cause (confirmed):** On resubmit, the server picks the prior
Action 5 tx as `previousTxId`. But the dispute tx (Action 6) already
points at that Action 5 tx. Two txs sharing a previousTxId = a legitimate
fork from the validator's point of view.

What the chain should look like post-dispute:
  A5(v1) → A6(dispute) → A5(v2 resubmit) → A6(approve)

What the chain currently looks like:
  A5(v1) ──┬─ A6(dispute)
           └─ A5(v2 resubmit)           ← rejected as fork

**Where to look:**
- `src/Services/Sorcha.Blueprint.Service/Services/Implementation/ActionExecutionService.cs`
  — how `PrevTxId` is resolved for a resubmit. Check
  `ResolvePreviousTransactionIdAsync` or the chain-anchoring block
  (around the lines that handle cycle-back routes).
- `walkthroughs/TradeFinance/procurement-to-pay-template.json` — Action 6
  has a `dispute-invoice` route with `nextActionIds: [5]` (cycle). The
  resubmit path should make Action 5 chain off the dispute tx, not the
  prior Action 5.
- Validator source for VAL_CHAIN_FORK is `ValidationEngine.cs` (grep for
  `VAL_CHAIN_FORK` constant).

Diagnostic:
```bash
ssh sorcha@51.105.7.135 'docker exec sorcha-mongodb mongosh \
  -u sorcha -p sorcha_dev_password --authenticationDatabase admin \
  --quiet --eval "db.getSiblingDB(\"sorcha_register_0356a161...\").transactions.find(
    { \"MetaData.InstanceId\": \"<disputed-instance-id>\" },
    { TxId:1, MetaData:1, PrevTxId:1, _id:0 }
  ).sort({TimeStamp:1}).toArray().forEach(t => print(JSON.stringify(t)))"'
```
Look at the sequence of PrevTxId values — that tells you exactly what the
chain looks like after the dispute and what resubmit is producing.

## Failure 2: ConstructionPermit Scenario B — Action 5 times out at 400

**Shape:** High-Risk Commercial path runs 1→2→3→4, then Action 5
(building-control) hangs for ~61s and returns 400. Validator logs show
no VAL_ rejections; the tx never reaches validation.

**Evidence from blueprint-service logs at 2026-04-20 16:29:26:**
```
Executing action 5 for instance f2084cdc-f086-4b17-b2b1-cdcc8826cd05
Reconstructed state for instance f2084cdc...: 0 actions, previous tx 64392739...
(61 seconds later)
HTTP POST .../actions/5/execute responded 400 in 61038.2890 ms
```
- "0 actions" reconstructed but "previous tx 64392739..." (which is
  Action 3's tx, NOT Action 4's). Action 4 HAD succeeded at 16:29:26
  with tx `4b7bc17f...`.
- So StateReconstructionService is seeing a stale view: it knows about
  Action 3 but not Action 4.
- After 61s of something (waiting for chain settle? timeout on a
  downstream HTTP call? blocked on credential requirement resolution?)
  the request 400s.

**Where to look:**
- `src/Services/Sorcha.Blueprint.Service/Services/Implementation/StateReconstructionService.cs`
  — what does it query and why doesn't it see Action 4's tx? Could be
  (a) reading from a cache that's stale, (b) reading from register
  before Action 4 has been sealed into a docket, (c) credential
  requirement path doing a slow external call.
- `src/Services/Sorcha.Blueprint.Service/Services/Implementation/ActionExecutionService.cs`
  line ~200+ for the pre-execute checks. Any 60s-ish timeouts there?
- `walkthroughs/ConstructionPermit/construction-permit-template.json` —
  Action 5 config. If it has `credentialRequirements`, check whether
  the missing credential is the root of the timeout.

Diagnostic:
```bash
# Check if Action 4's tx is actually sealed and queryable
ssh sorcha@51.105.7.135 'docker exec sorcha-mongodb mongosh \
  -u sorcha -p sorcha_dev_password --authenticationDatabase admin \
  --quiet --eval "db.getSiblingDB(\"sorcha_register_<CP-REG-ID>\").transactions.find(
    { \"MetaData.InstanceId\": \"f2084cdc-f086-4b17-b2b1-cdcc8826cd05\" },
    { TxId:1, \"MetaData.ActionId\":1, TimeStamp:1, _id:0 }
  ).sort({TimeStamp:1}).toArray().forEach(t => print(JSON.stringify(t)))"'

# See exactly what blueprint service was doing for 61 seconds
ssh sorcha@51.105.7.135 'docker logs sorcha-blueprint-service --since 2h 2>&1 \
  | grep -iE "f2084cdc|action 5" | head -80'
```

## Latent server-side bugs (behind the DevMode workaround)

These are why we flipped DevMode=true in the walkthroughs instead of
using the "correct" FLE path. Separate investigation when ready.

### 1. Issuance emits plaintext on DevMode:false registers

Action 6 in TradeFinance (and equivalent in other credential-issuing
walkthroughs) produces tx payloads with:
  - ContentEncoding: "base64url"  (not "encrypted")
  - WalletAccess: []              (empty)
  - Challenges: null, IV: null
  - body: { "payloads": { <walletAddr>: { /credential: ... } } }  (plaintext dict)

That is the DevMode-plaintext shape documented in
`src/Services/Sorcha.Wallet.Service/Services/Implementation/InboundCredentialDetector.cs`
lines 283–293. The detector correctly drops plaintext on a
DevMode:false register — DAD invariant preserved.

Per the detector comment, plaintext-on-non-DevMode only happens when
the encryption pipeline "could not resolve recipient keys (Feature 083
publishing gap)". So either (a) the transaction builder is
unconditionally emitting plaintext (a regression), or (b)
`EncryptionPipelineService` is silently falling back when it can't
resolve `sales-mgr`/`credit-analyst` public keys from the participant
publish records.

Start in:
- `src/Common/Sorcha.TransactionHandler/Encryption/EncryptionPipelineService.cs`
- `src/Services/Sorcha.Blueprint.Service/Services/Interfaces/ITransactionBuilderService.cs`
  — grep "plaintext" and "encrypted" for the fork
- Verify published participant records exist for TradeFinance orgs on n1:
  GET /registers/{tradeRegisterId}/participants
- If participants ARE published but the resolver still misses, check the
  key lookup path in EncryptionPipelineService.

The mid-demo DevMode→FLE transition in TradeFinance's run.ps1
(`-DisableDevMode`) is the authored integration point for this — after
the fix, disabling DevMode should continue to work without breaking the
demo, and encrypted payloads should then flow through the detector's
`TryFindEncryptedCredentialAsync` path.

### 2. Bloom filter rebuild corruption

During the 2026-04-20 investigation I observed the trade register bloom
had `address_count: 11`, `BITCOUNT: 110`, but ~5 of the 16 wallets that
existed at rebuild time were not in the bloom. During the walkthrough
run the bloom had all addresses (logs show successful MayContain
matches), but after the run something had re-rebuilt with a subset.
The other two registers in the same deployment had `address_count: 16`
and `BITCOUNT: 160` as expected.

It's not what blocked the current symptom — the bloom worked AT
walkthrough run time — but it's a real bug. Possible causes:
- A streaming truncation in `BloomFilterRebuilder.RebuildAsync` when
  called via the 10s-timeout fan-in in `RegisterCreationOrchestrator`.
- A race between two concurrent rebuilds (atomic swap means only last
  wins; if one completed with a partial view the partial view sticks).
- `WalletNotificationGrpcService.GetAllLocalAddresses` streaming got
  cancelled mid-page.

Repro: on a post-walkthrough n1, check bloom vs wallet counts:
```bash
ssh sorcha@51.105.7.135 'docker exec sorcha-redis redis-cli HGETALL register:bloom:params:<REG_ID>'
ssh sorcha@51.105.7.135 'docker exec sorcha-postgres psql -U sorcha -d sorcha_wallet -c "SELECT COUNT(*) FROM wallet.\"Wallets\" WHERE \"Status\"='"'"'Active'"'"';"'
```

## What is already proven working (don't re-investigate)

- PR #311 RecipientsWallets persistence fix holds — txs have correct
  recipient sets.
- PR #322 bloom fan-in on register create works at run time — bloom
  matches fire successfully during walkthroughs.
- PR #324 Tier 3 chain-binding validator fix holds — VAL_BP_002 is not
  in recent logs.
- The HTTP query endpoint `/api/query/instance/{id}/transactions/{registerId}`
  returns 200 with non-empty arrays (verified at post-PR-331 deploy).
- The register-native credential path (validator → register → bloom →
  router → notification → InboundCredentialDetector → Postgres) works
  end-to-end on DevMode:true registers. Confirmed by Active + PendingAcceptance
  rows for sales-mgr in TradeFinance, and equivalent BuildingPermitCredential
  issuance for ConstructionPermit Scenario A.

## Infra reminders

- n1 SSH: sorcha@51.105.7.135. Refresh NSG rule with your current IP:
    MY_IP=$(curl -s http://ifconfig.me)
    az network nsg rule update --resource-group sorcha-n1-uk \
      --nsg-name sorcha-n1-nsg --name AllowSSH \
      --source-address-prefixes "$MY_IP/32"
- MongoDB register databases: `sorcha_register_<registerId>` (underscore).
  Transactions collection: `transactions`.
- Register metadata: `sorcha_register_registry.registers` — the
  `DevMode` flag and `Name` live here.
- After any code change: the four gates are merge → Docker Publish (CI)
  → `docker compose pull <service>` on n1 → `up -d --force-recreate
  <service>`. Skipping any gate leaves old image running.
- Walkthrough state re-runs: `find walkthroughs -name state.json -delete`
  before re-running setup. state.json pins to register/org IDs from
  prior runs.

## Memory references worth loading

- ~/.claude/projects/C--Projects-Sorcha/memory/project_recipients_wallets_pipeline_gap.md
- ~/.claude/projects/C--Projects-Sorcha/memory/feedback_multi_node_assumption.md
- ~/.claude/projects/C--Projects-Sorcha/memory/feedback_sed_rename_footgun.md

## Start here

1. Invoke superpowers:systematic-debugging.
2. Pick ONE failure. Recommendation:
   - Failure 1 (TradeFinance B chain-fork) — localised, cleaner shape,
     probably a single-branch fix in the resubmit prevTxId resolver.
     Good warm-up problem.
   - Failure 2 (ConstructionPermit B 61s timeout) — probably touches a
     synchronous wait path that deserves a thorough look. Bigger blast
     radius. Handle AFTER Failure 1 is done.
   - Server-side plaintext-on-non-DevMode bug — the real architectural
     fix. Do this when the above two are cleared and you have time to
     walk the encryption pipeline.
3. For whichever you pick: gather MongoDB + log evidence BEFORE
   hypothesis. Today's Tier 3 incident peeled 4 separate layers; don't
   assume the first signal is root cause.
4. Stop and present a diagnosis before writing any fix.

Current git state: on master, clean. Latest merge is PR #334. Sandbox
is empty. Both TradeFinance and ConstructionPermit state.json carry
register IDs from 2026-04-20 afternoon — valid for re-running scenario
execution without a full re-bootstrap.
```

## Quick context for YOU (the human reading this now)

- Paste the block between the triple-backticks into a fresh Claude Code
  session after `/clear`.
- Today's three PRs (332/333/334) unblocked credential delivery end-to-end
  on the walkthroughs, but flipping DevMode=true is a demo-level workaround;
  the encryption pipeline still needs a proper investigation before the
  "mid-demo disable DevMode → watch FLE kick in" story holds water. That's
  Latent Bug #1 in the prompt.
- The two Scenario-B failures are **independent of** the credential
  delivery fix — they are chain-integrity issues (TradeFinance B) and
  state-reconstruction timing (ConstructionPermit B). Handle in that
  order; the first is narrower.
- SelfBuildHouse and PropertyInspection have the same fixes deployed
  but were never exercised on n1 this session. They are worth a
  smoke run before assuming "council walkthroughs all pass now" —
  SelfBuildHouse in particular stresses the cross-register credential
  chain (PlanningPermissionCredential → Building Warrant) which is a
  different credential type than TradeFinance's VerifiedInvoiceCredential.
