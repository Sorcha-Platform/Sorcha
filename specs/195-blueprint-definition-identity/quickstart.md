# Quickstart — the live acceptance run

**Feature 195.** How to prove this feature works. **A green test suite is not evidence here**, and
that is not a stylistic preference: every defect this feature addresses degrades to *plausible
behaviour* rather than to an error. A substituted definition produces a sealed transaction, a
valid-looking instance, and a passing suite.

Extends the Feature 194 harness at `walkthroughs/VersionPinning/`.

---

## Before you start

```bash
dotnet ef migrations has-pending-model-changes   # F194 lost a deploy to skipping this
```

⚠ **The container reports HEALTHY when migrations have failed.** `Program.cs` catches
`MigrateAsync` failure and logs it. Read the log, not the health status.

⚠ **Deploy order is load-bearing**: `validator-service` → `blueprint-service` → **`register-service`**.
New validator + old producer is safe; the reverse refuses every submission, because the old
`ComputeSignableBytes` rebuild computes different canonical bytes. `register-service` is in scope
because it persists and serves `TransactionMetaData` — F194's design omitted it and the pin was
silently dropped from the typed field while surviving in the tracking JSON.

⚠ **Delete `walkthroughs/**/state.json`** after any node wipe.

---

## The run

Against a **freshly re-genesised** node.

### 1. Publish, start, advance

Publish a blueprint. Start an instance. Advance one action.

Record: the publication txId, the instance id, its pin.

**Assert:** `GET /api/instances/{id}/definition` → `pinState: pinned`, and `publicationTxId` equals
the publish response's `txId`.

### 2. Behavioural republish under a live instance

Republish with a **behavioural** change — add a `required` field to a later action.

**Assert:** a **second** `BlueprintPublish` transaction exists on the register, with a different id.

```bash
docker exec sorcha-mongodb mongosh -u sorcha -p sorcha_dev_password \
  --authenticationDatabase admin --quiet --eval \
  'db.getSiblingDB("sorcha_register_<id>").transactions.find(
     {"MetaData.TransactionType": 9}, {TxId:1, "MetaData.BlueprintId":1, _id:0}).forEach(printjson)'
```

**This is the check #1563 fails today** — the count stays at one and the endpoint still returns 200.

### 3. Restart, and confirm the old instance survives

Restart `blueprint-service`.

**Assert:** the in-flight instance still reports `pinState: pinned`, resolves its **original**
definition, and **advances** against it — enforcing the *old* rule, not the new `required` field.

⚠ Use the `AwaitingInbox` gate between actors. `-WaitForSeal` waits for the **seal**, not the
**fold**; without the gate you get `400 "Action N is not a current action"`, which at the status line
is indistinguishable from a schema refusal. **Capture the response body**, not the status line — one
of F194's own assertions passed vacuously because of exactly this.

### 4. New instance gets the new definition

Start a second instance. **Assert:** it pins to the **new** publication id, and enforces the new
`required` field — submitting the v1-shaped payload is refused.

⚠ Assert the refusal is a *schema* refusal, with the body read. A submission that is merely too early
looks identical at the status line.

### 5. Presentational republish — **execute the counterfactual**

Republish with only a relabelled field.

**Assert three things:**
- a new publication transaction **exists** (relabels must ship);
- `execDefHash` is **unchanged**;
- the F142 rehearsal pass is **still valid** — publishing does not return `409 REHEARSAL_REQUIRED`.

⚠ **This check passes vacuously if written carelessly** — "unchanged" is the default outcome of doing
nothing. Pair it with a behavioural republish in the same run that *does* change `execDefHash` and
*does* require a fresh rehearsal. Only the pair discriminates.

### 6. Two registers, same blueprint — **execute the counterfactual**

Publish the **same** blueprint definition to a **second** register.

**Assert:** two distinct publication ids.

⚠ Also vacuous-prone: two registers rarely get the same blueprint by accident in a scripted run, so
the test must *deliberately* publish identical bytes to both. If the two definitions differ at all,
the assertion passes for the wrong reason.

### 7. Identical republish is a no-op

Republish byte-identical content to the same register.

**Assert:** `alreadyPublished: true`, the same txId, **no** new transaction, and still HTTP 200.

### 8. Replica node

On a node that has only **replicated** the register and never published to it: start and run an
instance of a definition it never saw published.

**Assert:** it resolves and advances (SC-009). This is what the validator's new register-fallback arm
buys.

### 9. The positive check

**Assert `pin_fallback` reads ZERO** across the entire run.

```bash
curl -s http://localhost:<port>/metrics | grep pin_fallback
```

This is the acceptance signal, not the absence of exceptions. Every failure mode of this feature
degrades to the old behaviour with a green suite — a cache re-keyed on one side only, a producer that
stops stamping, a pin dropped from a copy list. Each silently resolves "latest" again.

---

## Traps banked from F194's live run

| Trap | What it looks like |
|---|---|
| `-WaitForSeal` waits for the seal, not the fold | `400 "Action N is not a current action"` — indistinguishable from a schema refusal |
| A docket-write 409 (#814 guard) puts the builder on a **~10-minute** retry | a 90s seal wait times out on transactions that **do** seal. Not a refusal |
| `Publish-SorchaBlueprint` always MINTS a new blueprint id | cannot republish — use `PUT /blueprints/{id}` then `POST /publish` |
| Status lines are not diagnoses | capture the response **body** from the first attempt |
| `$Args` is a reserved PowerShell automatic variable | |
| Module signatures | `Connect-SorchaUser` needs `-OrganizationId`; `Confirm-SorchaUserEmail` takes `-UserId`; `New-SorchaWallet` needs `-FetchPublicKey`; `Register-SorchaParticipant` needs `-WalletUrl`; `BlueprintUrl` already ends in `/api` |
| n1 gateway `authentication` limiter | sliding 1-minute per-IP window |

---

## Mutation matrix — run before claiming any guard works

A guard written after the code is green never ran red naturally. Each mutation must kill **exactly**
its named test (research R-015).

| Mutation | Killing test |
|---|---|
| Remove key-sorting from the canonicaliser | golden vector |
| Rename any `[JsonPropertyName]` on the blueprint graph | golden vector |
| Drop `registerId` from the identity preimage | two-register distinctness |
| Drop the domain tag | preimage separation |
| Compute the id outside the Register Service | architecture gate |
| Make the pin optional on `GetBlueprintAsync` | submit-path resolution |
| Drop the pin from either execution cache key | cross-instance isolation |
| **Add a property to `Blueprint`/`Action`/`Route` and omit it from the hasher** | the new reflection guard — **and nothing in `ExecutableDefinitionHasherTests.cs`**, which is the entire argument for reflection over a hand-written list |
| Restore ordinal-based resolution in the amend loop | amend-source stability |
| Let the validator fall back to latest on an unresolvable pin | pinned-refusal |
