# Quickstart: verifying Feature 189 end to end

**Read this before writing a line of code.** Every defect this feature exists to fix was invisible
to a green test suite, and one live test passed for the wrong reason. The steps below are the
acceptance evidence (SC-009), not a smoke test.

## The two traps that will waste a deploy cycle

**1. Test on a register whose genesis has SEALED.** `RightsEnforcementService` admits any control
transaction while `roster == null`. A register created seconds earlier still has `height=0`, so a
governance change will be swept into the genesis docket and appear to succeed without ever
exercising roster enforcement. This is exactly how a first live run of the DevMode promotion
"passed" on 2026-08-06. **Wait for `height >= 1` before promoting.**

```bash
# n1 — confirm genesis sealed before doing anything governance-related
curl -s -H "Authorization: Bearer $TOK" $B/api/registers/$RID | python3 -c "import sys,json;d=json.load(sys.stdin);print('height',d['height'],'devMode',d['devMode'])"
```

**2. "Stored" is not "sealed".** A transaction present in the `transactions` collection but absent
from every docket has **not** taken effect. That was the original bug: the endpoint returned
`200 {"status":"submitted"}` while nothing happened. Always check `TransactionIds`.

```bash
ssh sorcha@51.105.7.135 'docker exec sorcha-mongodb mongosh -u sorcha -p sorcha_dev_password \
  --authenticationDatabase admin --quiet --eval "
db = db.getSiblingDB(\"sorcha_register_<REGISTER_ID>\");
print(\"=== TRANSACTIONS ===\");
db.transactions.find({},{TxId:1,\"MetaData.BlueprintId\":1,\"MetaData.TrackingData\":1,_id:0}).forEach(t=>printjson(t));
print(\"=== DOCKETS ===\");
db.dockets.find({},{DocketNumber:1,State:1,TransactionIds:1,_id:0}).forEach(d=>printjson(d));"'
```

**Pass = the governance tx id appears inside a docket's `TransactionIds` with `State: 4`.**

## US1 — a single organisation governs its register

No network re-genesis needed: a normal register's roster is written at its own genesis, so a
register created with the updated code already carries slot-100 keys.

1. Create a register (single owner, DevMode) with the updated code.
2. **Wait for `height >= 1`.**
3. Confirm the roster records a **slot-100** key, not the wallet's primary key — otherwise every
   later step tests the wrong thing:
   ```bash
   curl -s -H "Authorization: Bearer $TOK" $B/api/registers/$RID \
     | python3 -c "import sys,json;[print(a['role'],a['subject'],a['publicKey']) for a in json.load(sys.stdin)['initialControlRecord']['attestations']]"
   ```
4. `POST /disable-dev-mode` → expect `202 … "status":"submitted"`.
5. Poll until `devMode == false`, then run the Mongo check above.
6. **Cross-node:** confirm the same register on tiny reports `devMode == false`.
   ```bash
   ssh stuart@tiny 'docker exec sorcha-mongodb mongosh -u sorcha -p sorcha_dev_password \
     --authenticationDatabase admin --quiet --eval "
   printjson(db.getSiblingDB(\"sorcha_register_registry\").registers.findOne({_id:\"<REGISTER_ID>\"},{Name:1,DevMode:1,Height:1,SyncState:1}))"'
   ```
   ⚠ The id field is **`_id`**, not `Id`. Querying `{Id: …}` silently returns nothing and reads as a
   replication failure — it isn't.

**Negative check (proves enforcement, not just success):** attempt the same change as an
organisation that is not on the roster → refused, and **nothing** written to the ledger.

## US2 — consortium quorum

1. Create a register with **three owner organisations** and `QuorumFormula = Unanimous`
   (`InitiateRegisterRequest` already supports multiple `Owners`, each signing its own attestation).
2. Raise a proposal. Approve as org 1, then org 2 → **must not enact**.
3. Approve as org 3 → enacts; verify sealed-in-docket + cross-node as above.
4. **SC-010, the sharp one.** With one approver outstanding, remove that organisation from the
   roster. The proposal must be **`Invalidated` (reason `roster-changed`)** — *not* enacted. If
   removing a dissenter enacts the change, roster removal is an attack on every open proposal.
5. Approve twice as the same org → count unchanged.

## US3 — auditability

Reconstruct from the ledger alone: who proposed, which organisations approved and when, and the
outcome. Then diff the recorded sequence against `register-governance-v1` (FR-018) — the published
definition must describe what actually happened.

## US4 — system register ownership transfer

Requires the CLI ceremony change plus a re-genesis (the system register's own roster must carry
slot-100 keys).

1. Mint genesis with the updated ceremony; re-genesis n1; bring tiny up on the same network.
2. **Re-provision the AIAS demo immediately** (`run-demo.ps1 -Target n1 -Force`, then
   `rehearse.ps1 -Target n1`) — a re-genesis wipes it, and leaving it broken is how the demo gets
   discovered broken at the worst moment.
3. Transfer ownership; confirm the former owner can no longer govern and the new owner can.

## Environment notes

- n1 `sorcha@51.105.7.135` (**Bash tool**), gateway `http://localhost:8880`, box is UTC.
- tiny `stuart@tiny` (**PowerShell tool only** — Git Bash has no agent), dir `~/sorcha-test`,
  project `sorcha-test`, gateway `http://localhost:8090`. **Not** `~/sorcha-demo` (stale).
- Admin on both: `admin@sorcha.local` / `Dev_Pass_2025!`.
- Deploying an unmerged branch: `docker save` → `scp` → `docker load` → retag `:latest` →
  `up -d --force-recreate --no-deps <svc>`. **Do not `docker compose pull` afterwards** — it
  overwrites the loaded image with DockerHub's and silently reverts the change under test.
- `PullDocketChain … streamed 0 dockets` is normal caught-up steady state, not a fault. Check
  `SyncState` and docket counts before believing it means anything.
