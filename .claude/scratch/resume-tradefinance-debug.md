# Resume: TradeFinance on n1 — two remaining failures

The Tier 3 chain-binding incident is closed. TradeFinance scenarios A and C now pass
the full 6-action procurement-to-pay phase on n1. But two downstream failures remain.
This document is the handoff for a fresh session to pick up from there.

## Paste the prompt below after `/clear`

```
I'm debugging TradeFinance walkthrough failures on n1.sorcha.dev. The validator
Tier 3 chain-binding incident is resolved (PRs #324, #329, #330, #331 all merged
earlier today; commits verified on master). Procurement-to-pay Action 6 now
passes — VAL_BP_002 is gone. But two new failure surfaces remain.

Invoke these skills up-front:
  - superpowers:systematic-debugging  (investigation framework; use before proposing fixes)
  - n1-deploy                          (SSH, logs, MongoDB access; pull/recreate cycle)
  - walkthrough-builder                (TradeFinance scenario structure, state.json format)
  - verifiable-credentials             (issuance + recipient wallet delivery pipeline)
  - blueprint-builder                  (action routes, cycles, dispute resubmit paths)
  - mongodb                            (register collections — per-register db
                                        `sorcha_register_<registerId>`, transactions coll)

**Failure 1: Credential delivery gap (Scenarios A + C)**
- Action 6 (procurement-mgr) executes successfully; issues `VerifiedInvoiceCredential`
  to recipient `sales-mgr`.
- Walkthrough then polls sales-mgr's wallet and logs:
    "No VerifiedInvoiceCredential found in sales-mgr wallet"
- Subsequent Invoice Finance Action 1 (finance-director) fails 400 because the
  credential requirement isn't met.
- Walkthrough last ran 2026-04-20 ~11:20 UTC. sales-mgr wallet address was
  `ws11qr4ea3xxm0n4727rz3peen94689g4sdptqa0cdhmr3ygy2lgfg4r2pqz3fr`.
- Relevant register: trade register `e1b50bbbb2d04c82b955e165f7c72c53`.
- The TradeFinance blueprint has `credentialIssuanceConfig.targetAudience` on
  Action 6 — check whether it's `SorchaLocalWallet` (register-native delivery via
  InboundCredentialDetector) or legacy `SorchaInternal` (direct wallet write).

  If SorchaLocalWallet:
    - Check validator → register → recipient-wallet delivery via bloom filter.
      `sorcha-register-service` logs should show `InboundTransactionRouter` check
      against sales-mgr's bloom; `sorcha-wallet-service` logs should show
      `InboundCredentialDetector` receiving and decrypting.
    - Bloom hooks A/B/C populate sales-mgr's address in the register's filter
      on wallet create. PR #322 (bloom fan-in) ensures this works for freshly-
      created registers; confirm sales-mgr's bloom entry exists.
    - The credential tx will have `ContentEncoding: "encrypted"`. If it's
      "plaintext", `InboundCredentialDetector` skips it (by design, prior to
      PR #312's DevMode switch — check whether n1 runs in DevMode).

  If SorchaInternal:
    - Direct wallet write via `walletClient.StoreCredentialAsync` from Blueprint
      Service → targets sales-mgr's wallet address. Lookup failure means the
      wrong address was targeted or the wallet didn't persist the credential.
    - SorchaInternal was deprecated in PR #305; check the blueprint template
      actually uses SorchaLocalWallet.

  File pointers:
    - walkthroughs/TradeFinance/procurement-to-pay-template.json
      (Action 6 credentialIssuanceConfig + targetAudience)
    - src/Services/Sorcha.Blueprint.Service/Services/Implementation/
      ActionExecutionService.cs (IssueCredentialFromActionAsync, line ~1710)
    - src/Services/Sorcha.Wallet.Service/Services/Implementation/
      InboundCredentialDetector.cs
    - src/Services/Sorcha.Register.Service/Services/Implementation/
      InboundTransactionRouter.cs
    - src/Services/Sorcha.Wallet.Service/Endpoints/CredentialEndpoints.cs
      (IssueCredential + SkipRecipientStore)

  Diagnostic starting point:
    # Check what actually landed in sales-mgr's wallet
    ssh sorcha@51.105.7.135 'docker exec sorcha-postgres psql -U sorcha \
      -d sorcha_wallet -c "SELECT id, type, issuer_did, subject_did, status, \
      created_at FROM wallet.credentials WHERE wallet_address = \
      '\''ws11qr4ea3xxm0n4727rz3peen94689g4sdptqa0cdhmr3ygy2lgfg4r2pqz3fr'\'' \
      ORDER BY created_at DESC LIMIT 10;"'

    # Check what the register sealed (the issuance tx)
    ssh sorcha@51.105.7.135 'docker exec sorcha-mongodb mongosh \
      -u sorcha -p sorcha_dev_password --authenticationDatabase admin \
      --quiet --eval "db.getSiblingDB(\"sorcha_register_e1b50bbbb2d04c82b955e165f7c72c53\").transactions.find({\"MetaData.ActionId\": 6}, {TxId:1, SenderWallet:1, RecipientsWallets:1, \"Payloads.ContentEncoding\": 1, _id:0}).toArray().forEach(t => print(JSON.stringify(t)))"'

    # Validator + wallet logs during a rerun
    ssh sorcha@51.105.7.135 'docker logs --tail 200 sorcha-wallet-service 2>&1 \
      | grep -iE "inbound|credential|action 6"'

**Failure 2: Dispute resubmit (Scenario B)**
- After Action 6 returns `DISPUTED`, the walkthrough resubmits Action 5 as
  sales-mgr (intended cycle back for a corrected invoice).
- Action 5 resubmit fails 400 Bad Request.
- walkthroughs/TradeFinance/run.ps1 → look for "resubmit" to find the call shape.
- Blueprint Action 5 and Action 6 carry routes — the dispute route from 6 must
  legally land back at 5, AND Action 5 must allow re-entry for sales-mgr (not
  an immutable-binding violation on the second attempt).
- Check the validator logs for the actual rejection reason on that tx — pattern
  is the 400 surfaces from Blueprint Service after the validator rejects; look
  at the full ValidationEngine warning, not just the HTTP log.

  File pointers:
    - walkthroughs/TradeFinance/procurement-to-pay-template.json
      (Action 6 routes, Action 5 cycle-in handling)
    - src/Services/Sorcha.Blueprint.Service/Services/Implementation/
      ActionExecutionService.cs (route traversal, ~line 200-300 for the
      strict wallet check; ~line 1027 for starting-action bypass logic)
    - src/Services/Sorcha.Validator.Service/Services/ValidationEngine.cs
      (lines 1097+ for action sequencing check against PreviousTransactionId)

  Diagnostic starting point:
    ssh sorcha@51.105.7.135 'docker logs --since 3m sorcha-validator-service 2>&1 \
      | grep -iE "VAL_|resubmit|dispute|action 5"'

**What NOT to chase:**
- The Tier 3 chain-binding fix is done. VAL_BP_002 is not in recent logs.
- The register /api/query/instance endpoint exists and returns data with
  non-null InstanceId (verified against MongoDB).
- Don't re-investigate procurement-mgr Action 6 — it passes cleanly now.

**Infra reminders (from ~/.claude/skills/n1-deploy/SKILL.md):**
- n1 SSH IP: 51.105.7.135 (user: sorcha). NSG rule AllowSSH may need your
  current public IP — refresh with:
    MY_IP=$(curl -s http://ifconfig.me)
    az network nsg rule update --resource-group sorcha-n1-uk \
      --nsg-name sorcha-n1-nsg --name AllowSSH \
      --source-address-prefixes "$MY_IP/32"
- Docker Publish → n1 pull requires explicit `docker compose pull <service>`
  then `up -d --force-recreate <service>`. The four gates.
- If you make a code change, validate end-to-end: curl the HTTP endpoint on n1
  BEFORE claiming the fix is deployed. Two PRs today went up without this
  check and each peeled another layer of the same incident.
- Register MongoDB databases are named `sorcha_register_<registerId>`, not
  `register_<id>`. Transactions collection is `transactions`.

**Memory references worth loading if relevant:**
- ~/.claude/projects/C--Projects-Sorcha/memory/feedback_multi_node_assumption.md
- ~/.claude/projects/C--Projects-Sorcha/memory/project_multi_node_audit.md
- ~/.claude/projects/C--Projects-Sorcha/memory/feedback_sed_rename_footgun.md
  (if you need to do any global rename)

**Start here:**
1. Invoke superpowers:systematic-debugging.
2. Pick ONE of the two failures (the credential-delivery gap is more likely
   to block downstream walkthroughs including council; the dispute resubmit
   is only on one scenario branch). Recommend credential delivery first.
3. For whichever you pick: gather evidence (logs, MongoDB, wallet DB) BEFORE
   forming a hypothesis. The Tier 3 incident was a 4-hop chain and each hop's
   fix was invalidated by the next layer — don't assume the first cause is
   the root cause.
4. Stop and present a diagnosis before writing any fix.

Current git state: on master, clean. PR #331 is the latest merge. Sandbox is
empty. TradeFinance state.json carries register IDs from the 2026-04-20 run;
can re-run directly or wipe + fresh setup as you prefer.
```

## Quick context for YOU (the human reading this now)

- Paste the block between the triple-backticks into a fresh Claude Code session
  after `/clear`.
- The prompt assumes the new session has no memory of what happened today.
  It re-lists the skill invocations, file pointers, concrete probe commands,
  and explicit "what not to chase" fences so the new session doesn't rabbit-hole
  back into Tier 3.
- The two failures are separate; the recommendation is to tackle credential
  delivery first because it also blocks the council walkthrough (which shares
  the same register-native credential path).
