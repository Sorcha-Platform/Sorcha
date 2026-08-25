# Quickstart: Blueprint Version Pinning (Feature 194)

**Branch**: `194-blueprint-version-pinning` | **Spec**: [spec.md](./spec.md) |
**Research**: [research.md](./research.md)

---

## What this feature does, in one line

An instance runs the blueprint definition it started on, forever, even after the blueprint is
republished to the same register.

---

## The one thing to get right

`RoutingDecision.ComputeSignableBytes()` is a hand-written field-by-field rebuild. **Add the pin to
the record and forget the rebuild and it rides the wire unauthenticated while appearing signed.**
Write the reflection-driven guard first, mutation-test it against an *existing* field, and only then
add the new one. See [contracts/routing-decision-pin.md](./contracts/routing-decision-pin.md).

---

## Build and test

```bash
dotnet restore && dotnet build

# MTP mode (global.json) — VSTest args do not apply
dotnet test --project tests/Sorcha.Register.Models.Tests/Sorcha.Register.Models.Tests.csproj
dotnet test --filter-class "*RoutingDecision*"
dotnet test --filter-class "*InstanceProjection*"
dotnet test --filter-class "*ExecutableDefinitionHasher*"
dotnet test --project tests/Sorcha.Validator.Service.Tests/Sorcha.Validator.Service.Tests.csproj
dotnet test --project tests/Sorcha.Blueprint.Service.Tests/Sorcha.Blueprint.Service.Tests.csproj
```

`--collect:"XPlat Code Coverage"` is dead under MTP. Solution-wide `dotnet test` locally reports
hundreds of failures from **contention**, not regression — run projects individually to judge.

---

## The database must be recreated

`Instance` gains a column, folded into the existing `InitialCreate` (CLAUDE.md §19 — no new
migration). Amending an applied migration is **invisible** to a database that already recorded its
id: `MigrateAsync` sees `InitialCreate` present, does nothing, and the failure surfaces far away as

```
42703: column i.BlueprintExecDefHash does not exist
```

after a green build and a green suite.

```bash
# local
docker compose down -v && ./scripts/sorcha-setup.sh

# cheap proof the migration is right, without touching anything
dotnet ef migrations script --idempotent \
  --project src/Services/Sorcha.Blueprint.Service
# must name exactly ONE migration, and its CREATE TABLE must contain the column
```

On a node, recreating the **blueprint** Postgres database is sufficient — the register is untouched,
which is exactly why the pre-feature fallback is load-bearing rather than defensive (research R-009).

---

## Deployment order is load-bearing

Deploy scope is `blueprint-service` **and** `validator-service`. **Deploy the validator first.**

| Combination | Result |
|---|---|
| New validator, old workflow service | **Safe.** The producer omits the field; the validator's rebuild includes it as null; `WhenWritingNull` means the canonical bytes are identical and every signature still verifies. |
| Old validator, new workflow service | **Every submission refused.** The producer signs bytes that include the pin; the old validator's rebuild omits it, computes different bytes, and `VAL_ROUTING_002` fails. |

There is no genesis window — both are ordinary per-service recreates.

```bash
C="docker compose -f docker-compose.yml -f docker-compose.n1.yml -f docker-compose.ports.yml"
ssh sorcha@51.105.7.135 "cd /opt/sorcha && $C pull validator-service && $C up -d --force-recreate --no-deps validator-service"
# then, and only then:
ssh sorcha@51.105.7.135 "cd /opt/sorcha && $C pull blueprint-service && $C up -d --force-recreate --no-deps blueprint-service"
```

---

## Expected on first publish after deploy

Removing the ordinal from the hashed projection (research R-003) changes every blueprint's hash,
which invalidates every recorded F142 `RehearsalPass`. The first publish of any blueprint will
therefore return **`409 REHEARSAL_REQUIRED`**.

**This is expected, not a defect.** Re-rehearse, or use the documented override. Do not diagnose it
as a publish-gate regression.

---

## The live acceptance test (this is the gate — it cannot be skipped)

Run on n1. A green suite is not evidence: this feature exists because a live run found what ~2,500
green tests did not.

1. Publish blueprint `X` (v1) to register `R`.
2. Start instance `A`; execute its first action; leave it mid-flow.
3. Republish `X` with a **behavioural** change — add a required field to a later action's schema.
4. Advance `A`. It **must succeed** against v1's schema, without the new required field.
5. Start instance `B`. It **must be pinned to v2** and **must require** the new field.
6. **Restart `blueprint-service`, then advance `A` again.** This proves recovery restored v1, not
   just v2.

Step 6 is the one most likely to fail and the one most worth having.

### n1 traps that will cost you a cycle

- The gateway `authentication` limiter is a **sliding 1-minute window per IP**. Do not probe it and
  then start a run in the same window.
- Delete `walkthroughs/**/state.json` after any node wipe.
- An `/execute` **202 means accepted, not sealed.** Verify by docket. A schema refusal is
  `202 + txid` followed by a transaction that never seals; a credential refusal is a synchronous 400.
- **Never select a credential by type** in a wallet that accumulates them.
- `admin@sorcha.local` is multi-org — `/api/auth/login` returns `requires_org_selection` and a
  `platform_login_token` to exchange at `/api/auth/select-org`.
- Use the **Write/Edit tools for `.ps1` and `.cs` files**. Bash heredocs mangle PowerShell backticks
  and `$vars`, and a silently-unapplied patch costs a whole cycle. Verify the edit landed before
  running.

---

## Verifying the pin is actually doing something

Two checks that distinguish "working" from "looks like it is working":

```bash
# 1. The pin is ON THE WIRE and SEALED — not merely in a local database.
ssh sorcha@51.105.7.135 'docker exec sorcha-mongodb mongosh -u sorcha -p sorcha_dev_password \
  --authenticationDatabase admin --quiet --eval "
  db = db.getSiblingDB(\"sorcha_register_<REG>\");
  db.transactions.find({}, {TxId:1, \"MetaData.RoutingDecision\":1, _id:0}).limit(3)
    .forEach(t => print(JSON.stringify(t, null, 2)));"'
# RoutingDecision.blueprintExecDefHash must be PRESENT and must be v1's hash on A's actions.

# 2. The fallback counter is ZERO on a register created after the deploy.
#    A non-zero value there means the pin is not being written and everything is
#    quietly running on "latest" again — i.e. the defect, with a green test suite.
```

The second check is the important one. Every failure mode of this feature degrades to the **old
behaviour**, not to an error — so absence of errors is not evidence of success.
