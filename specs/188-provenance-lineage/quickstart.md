# Quickstart: Provenance — trust-anchor and proof lineage

**Feature**: 188 | **Plan**: [plan.md](./plan.md)

How to exercise the feature once Phase 1 lands, and — more usefully — how to check it is telling the truth.

## Open a register's lineage

Sign in as an administrator of the owning organisation, then open the register from the admin explorer. You should see every docket from genesis in order, each with its proposer and signer count, and a marker at any docket where the validator set changed.

```bash
BASE=https://n1.sorcha.dev
TOK=$(curl -s $BASE/api/auth/login -H 'Content-Type: application/json' \
  -d '{"email":"admin@sorcha.local","password":"Dev_Pass_2025!"}' \
  | python3 -c 'import sys,json;print(json.load(sys.stdin)["access_token"])')

REG=<registerId>
curl -s "$BASE/api/provenance/registers/$REG?pageSize=20" -H "Authorization: Bearer $TOK" | python3 -m json.tool
```

## Verify one docket

```bash
curl -s "$BASE/api/provenance/registers/$REG/dockets/0" -H "Authorization: Bearer $TOK" | python3 -m json.tool
```

Each check reports `verified`, `failed` or `unverified`, and every one states what it compared against.

## What you should expect to see on a single-validator node

**n1 and local development both run a single validator**, so this is the normal case, not an edge:

- `signers` → **`unverified`**, with a reason naming the absence of quorum evidence
- `seal`, `chain`, `anchor`, `proposer` → `verified` on a healthy register

**A green tick on `signers` here would be a defect**, not good news — it would mean the check reported success without running. If you see one, that is the single most important bug this feature can have.

## Prove the checks actually check

A verification feature that always says "verified" is worse than none, because it manufactures confidence. Three ways to satisfy yourself:

**1. Tamper the stored contents.** On a scratch node, alter a docket's transaction id list in Mongo, then re-open that docket. `seal` must flip to `failed`.

```bash
# scratch environments only — this deliberately corrupts a register
ssh <node> 'docker exec sorcha-mongodb mongosh -u sorcha -p sorcha_dev_password \
  --authenticationDatabase admin --quiet --eval "
  db=db.getSiblingDB(\"sorcha_register_<REG>\");
  db.dockets.updateOne({_id:1},{\$set:{TransactionIds:[\"tampered\"]}});"'
```

**2. Check a docket sealed before Feature 187.** It has no stored Merkle root, so `seal` must read `unverified` with a reason — not `failed`. Absence of evidence is not evidence of tampering.

**3. Check a partially-held register.** On a node that does not hold a docket's predecessor, `chain` must read `unverified`, not `failed`. A subscribing node must not look compromised merely for holding less.

## The check most worth exercising by hand

Once a register has had a validator removed, open a docket **sealed before** the removal. Its signature from that validator must still read `verified`.

This is the failure mode that looks most like success: an implementation that checks signatures against the *current* roster passes every test on a register whose validator set never changed, and starts reporting false tampering the moment the network grows — which is exactly when this feature matters most.

## Related

- Spec: [spec.md](./spec.md) · Research: [research.md](./research.md) · Data model: [data-model.md](./data-model.md)
- Contract: [contracts/provenance-api.yaml](./contracts/provenance-api.yaml)
- Evidence source: Feature 187 (merged `bed2f044`) — persisted proposer, sealed Merkle root, consensus votes
- Issue #1372 — narrows per plan D4 · Issue #1374 — a mismatched anchor should surface as `anchor: unverified`
