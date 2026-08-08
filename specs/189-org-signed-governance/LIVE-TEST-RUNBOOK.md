# Feature 189 — live test runbook

> **RUN 2026-08-08 — PASSED.** Register `254ac2e04ede439885c03b94c949237f` on n1. Four dockets:
> genesis (roster 2) → proposal (`roster: null`, `status: Pending`) → two approvals sealed together →
> enactment (roster 3, `enactsProposalId` set) raised by nothing but the `docket:confirmed` reaction.
> Validator reported `validated 1 transactions, rejected 0` throughout.
>
> **It found two defects first (both fixed, commit `1c4d9440`).** An intermittent payload-hash
> mismatch — an approval whose base64 signature contained a `+` was refused while one whose bytes did
> not sealed, because the submitter's JSON encoder escaped it and the Validator's did not. And a
> total, silent one: the payload was read back with options carrying no enum converters, so the
> deserialise threw and every reader treats a throw as "not an approval" — both the enactment service
> and the Validator counted **zero** approvals, and nothing logged an error. Steps 7 and 8 below
> (substitution, no-regression) are still **not run**.

The propose → approve → enact chain is complete in code. This is how to run it; every step is written
to be falsifiable, so a step that does not produce the stated evidence is a finding, not a hiccup.

> **Why this document exists.** Eight of this feature's known defects were found by the first live
> run after a green suite, and two confident claims of mine were disproved by execution. A green
> suite predicts almost nothing here.

---

## 0. Preconditions

- **A fresh ordinary register.** The existing fixture `cbb1fa4c1bc942b7a1f86eabcfb96ea6` cannot be
  used: its proposal is already enacted, and enacting moved `LastControlTxId`, so FR-011b invalidates
  it. An approval against it will be correctly refused `roster-changed`.
- **Not the system register.** It is unique by design (offline pre-signed genesis, outside this path
  until US4) and can neither confirm nor refute general behaviour.
- **Genesis sealed in docket 0 before anything else.** Before it seals, `roster == null` admits
  everything — that race produced a false PASS in this feature already. Confirm the docket first.
- **Two organisations on the roster**, or quorum is met by the Owner override and the approval path is
  never exercised. An Owner-proposed `Add` on a two-org register under `StrictMajority` takes the
  override; raise the proposal **as the non-Owner admin** so quorum is genuinely required.
- n1 runs a **local branch build** of `register-service`. `docker compose pull` reverts it. Build →
  `docker save | gzip` → `scp` → `docker load` → retag `:latest` → `up -d --force-recreate --no-deps
  register-service`. The validator changed too this time, so **both** `register-service` and
  `validator-service` must be redeployed.

## 1. Deploy

Both services, because the enactment gate is in the Validator and the proposal/approval/enactment
paths are in the Register Service. Deploying one and not the other produces a half-built chain whose
symptoms point at the wrong component.

## 2. Create the register and confirm the genesis sealed

```
POST /api/registers/initiate
  → sign each attestation with derivationPath "sorcha:register-attestation"   (slot 100)
POST /api/registers/finalize                       (echo the WHOLE attestationData object back)
```

The window is 5 minutes, so script it. Then, before anything else:

```bash
docker exec sorcha-mongodb mongosh -u sorcha -p sorcha_dev_password --authenticationDatabase admin \
  --quiet --eval 'db.getSiblingDB("sorcha_register_<id>").dockets.find({},{DocketNumber:1,TransactionIds:1,_id:0}).forEach(printjson)'
```

**Evidence required:** a docket 0 containing the genesis transaction. Stop if it is absent.

## 3. Raise a proposal that needs approval

```
POST /api/registers/{id}/governance/propose      (numeric enums — #1384)
```

Raise it **as the non-Owner admin** so the Owner override does not apply.

**Evidence required:** HTTP **202** with `status: "Pending"`, and after the next docket seals, the
proposal transaction on the register carrying **`"roster": null`**:

```bash
# the proposal must NOT carry a roster, or it invalidates itself the moment it seals
docker exec sorcha-mongodb mongosh ... --quiet --eval '
  var d = db.getSiblingDB("sorcha_register_<id>");
  d.transactions.find({}).forEach(function(t){
    var o = JSON.parse(t.Payloads[0].Data.toString());
    print(t.TxId.substring(0,12) + " docket=" + t.DocketNumber
      + " roster=" + (o.roster ? "PRESENT" : "null")
      + " status=" + ((o.operation||{}).status||"-"));
  });'
```

**A 400 here means the pending-proposal path did not deploy.** A proposal carrying a roster means the
split did not take effect — everything after it will fail in confusing ways.

## 4. Confirm the roster head did NOT move

```
GET /api/registers/{id}/governance/roster
```

**Evidence required:** `lastControlTxId` still names the **genesis**, not the proposal. This is the
property the whole design rests on; if it moved, stop.

## 5. Approve, from the CLI

```bash
sorcha governance show    --register <id> --proposal <tx> --as <org-wallet>
sorcha governance approve --register <id> --proposal <tx> --as <org-wallet> \
                          --individual <admin-wallet> --comment "live gate"
```

`show` first, deliberately: it renders the operation and prints the digest the client derives. If
`approve` later fails signature verification, the digest from `show` is what distinguishes "the client
derived the wrong digest" from "the wrong key signed".

**Evidence required:** HTTP **202**, then the approval transaction sealed into a docket, chained to
the proposal, carrying `"type": "governance-approval"` — and the validator log showing it **validated,
not rejected**:

```bash
docker logs --since 3m sorcha-validator-service | grep -E "validated|rejected|not found in roster"
```

> `200`/`202` is **accepted**, never **enacted**. Check the docket and the validator verdict; never
> the response body.

## 6. Confirm the enactment fires by itself

Nothing is submitted by hand here. The `docket:confirmed` subscriber re-evaluates open proposals when
the approval's docket seals.

**Evidence required:**

1. An **enactment transaction** on the register carrying a roster and `enactsProposalId` = the
   proposal, which nobody submitted manually.
2. `GET .../governance/roster` now shows the change applied, and `lastControlTxId` is the enactment.
3. The validator log shows the enactment **validated**.

**If the enactment never appears**, the subscriber is the first suspect — check that
`GovernanceEnactmentSubscriber` logged its startup line, since a subscription that failed to register
is silent.

## 7. 🔴 The substitution gate (T085)

The gate that distinguishes independent approval from something that merely looks like it.

Review and sign an `AddValidator` for validator **A**, then submit the approval with validator **B**'s
entry in the proposal. It **MUST** be rejected, and the rejection **MUST** appear in the validator log
rather than being absorbed.

This is the one that proves statement v2 binds `ValidatorEntry` — the field a v1 approval left
unbound, so "add a validator" did not bind *which*.

## 8. 🔴 The no-regression gate (T086)

A **single-owner** register completes a governance change unattended: no approval, no pairing, no
human interaction. The Owner override still takes its single propose-and-enact transaction, exactly as
before the split (FR-031).

**If this fails, the split broke the common case** and matters more than anything above.

---

## 9. 🔴 Per-node accountability verification (T079) — RUN 2026-08-08, ALL PASS

The Validator now re-verifies each approval's `authorisation` before counting it, so the gate that
matters is the **false-negative** one: if that check is wrong, valid approvals silently stop counting,
quorum is never met, and governance quietly stops working. The unit tests prove the refusals; only a
live run proves the acceptances.

Deployed to n1 as **both** `register-service` and `validator-service` (unmerged branch: build → save →
scp → load → `up -d --force-recreate --no-deps`). Confirmed both containers were on images built
minutes earlier before testing anything.

Run on two independent ordinary registers — `812dd9230c3a…` and `9ce0eb8874a6…`, never the SSR — each
raised by the **non-Owner admin** so the Owner override could not apply and quorum had to be collected.

| Evidence | Result |
|---|---|
| Proposal `202`, `roster: null`, head still the genesis | PASS |
| Both organisations approve with a real `authorisation`, `202` at intake | PASS |
| **`Quorum check for Add … 2/2 (pool=2, met=True)`** — the approvals COUNTED at the Validator | PASS |
| Enactment raised by nothing but the `docket:confirmed` reaction; roster 2 → 3, head moved | PASS |
| `validated 1 transactions, rejected 0` | PASS |
| An authorisation naming one individual but signed with another's key → `422 IndividualMismatch` | PASS |

Sealed chain on `812dd9230c3a…`, which is the shape the design predicts:

```
docket 0   control              roster=PRESENT                       genesis
docket 1   control              roster=null   status=Pending         proposal
docket 2   governance-approval  authMethod=service                   owner
docket 2   governance-approval  authMethod=service                   admin
docket 3   control              roster=PRESENT status=Recorded       enacts=7d6b5ae1983f
```

**Why `2/2` is the load-bearing line.** `VerifyAccountabilityAsync` is called for every approval whose
organisation signature verifies, and it returns false when no verifier is available. So `2/2` is only
reachable if the verifier resolved from DI *and* accepted both — the check cannot be silently skipped.
The `422` proves the same shared class refusing in the deployed image, and the Register Service's new
refusal log line (`Sorcha.Register.Service.Governance … refused: IndividualMismatch`) proves the
logging moved out of the verifier is reaching an operator rather than being lost.

Add to the falsification table below: **`Quorum check … 0/2` with approvals sealed** would mean the
accountability check is refusing valid approvals — governance broken in the quiet direction.

---

## What would falsify the design

Worth naming in advance, so a failure is recognised rather than explained away:

| Observation | What it means |
|---|---|
| Proposal carries a roster | The pending path did not deploy; FR-011b will invalidate it on seal |
| Roster head moves on the proposal | Same, and every approval will be refused `roster-changed` |
| Approval refused `VAL_SIG_002` | The detached signature was filed as a transaction signature |
| Approval refused "not found in roster" | The carry signed with the wrong key or slot |
| Enactment never appears | The subscriber never registered, or `ListOpenAsync` finds nothing |
| Two enactments on one proposal | The deterministic id or the byte-determinism is broken |
| Substitution accepted | Statement v2 is not binding what it claims to |
