# Handoff — Feature 195 live run (T059–T061)

Paste everything below the line into a fresh session.

---

Continue Feature 195 (blueprint definition identity). The code is **done and committed**; only the
live acceptance run remains. Branch `195-blueprint-definition-identity`, head `5c3e14b1c`, pushed,
**58/61 tasks complete**.

Load these skills first:

```
/n1-deploy                 — deploy scope, re-genesis, how to verify a deploy honestly
/walkthrough-builder       — the harness and its cadence rules
/blueprint-builder         — publish rules and the F195 section
/sorcha-architecture       — the Feature 195 identity model
```

Recall memory: `blueprint-lifecycle-investigation`, `f194-version-pinning-live`,
`feedback_verification_discipline`, `n1-current-state`, `replica-commits-unlinked-chain`.

Read, in order:
1. `specs/195-blueprint-definition-identity/quickstart.md` — the acceptance run, step by step
2. `specs/195-blueprint-definition-identity/mutations.md` — what is already proven, and how
3. `specs/195-blueprint-definition-identity/tasks.md` — T059/T060/T061 are the only open items

## What you are doing

**Stuart has authorised a full n1 re-genesis and validation.** Deploy the branch, re-genesise, run the
acceptance, and only then close the issues.

- **T059** — deploy + re-genesis + live acceptance per `quickstart.md`.
- **T060** — update `.specify/MASTER-TASKS.md` with the outcome, **including anything found live that
  the design did not have**. F194 found five such things; expect some.
- **T061** — close **#1563, #1566, #1567, #1568, #1570** with the live evidence, not with the merge.
  Then open the PR.

## ⚠ Deploy order is load-bearing

```
validator-service  →  blueprint-service  →  register-service
```

`RoutingDecision.BlueprintExecDefHash` became `BlueprintDefinitionTxId` (wire name too) and it is
inside `ComputeSignableBytes`, so **old and new producers compute different canonical bytes and refuse
each other**. `register-service` is in scope because it persists and serves `TransactionMetaData` —
F194's design omitted it and the pin was silently dropped from the typed field.

## The acceptance run

`walkthroughs/VersionPinning/` has two scripts. **Both must pass.**

- `run-acceptance.ps1` — Feature 194's guarantee (an in-flight instance keeps its definition across a
  republish and a restart). Unchanged; still the baseline.
- `run-f195-acceptance.ps1` — **NEW and never executed against a live node.** Parse-checked only.
  Expect to fix it; it is the instrument, not the evidence.

It checks four things, of which the first is the one that fails on the pre-195 platform:

1. A behavioural republish **writes a second transaction**. Before this feature the count stayed equal
   while the endpoint answered `200` and the caller logged success (#1563).
2. A byte-identical republish is a recognisable no-op (`alreadyPublished: true`, no new transaction).
3. A presentational republish writes a new publication but leaves `execDefHash` unchanged.
4. The same definition on a **second register** gets a **different** identity.

⚠ **Checks 3 and 4 pass vacuously if you let them.** 3 asserts something is *unchanged*, which is the
default outcome of doing nothing — it is paired with an explicit counterfactual that the behavioural
republish *did* move `execDefHash`. 4 needs both registers to receive **byte-identical** definitions,
so it asserts the payloads match before comparing ids. If you edit either, keep the pairing.

## The positive check that outranks everything

```bash
curl -s <gateway>/metrics | grep pin_fallback
```

**It must read ZERO.** Every failure mode of this feature degrades to the OLD behaviour, not to an
error — a cache re-keyed on one side only, a producer that stops stamping, a pin dropped from a copy
list. A clean log proves nothing.

## Traps

- **`dotnet test` reports "Zero tests ran"** here (exit 5, both invocation forms). Build the test
  project, then run the **exe**: `./tests/X/bin/Debug/net10.0/X.exe`. `dotnet build` takes one project
  per invocation.
- Run `dotnet ef migrations has-pending-model-changes` before deploying. **The container reports
  HEALTHY when `MigrateAsync` fails** — read the log, not the health status.
- `-WaitForSeal` waits for the **seal**, not the **fold**. The `AwaitingInbox` gate is mandatory between
  actors or you get `400 "Action N is not a current action"`, indistinguishable at the status line from
  a schema refusal. **Capture response bodies, not status lines.**
- A docket-write 409 puts the docket builder on a **~10-minute** retry. A 90s seal wait times out on
  transactions that do seal. Do not read a seal timeout as a refusal.
- **Clear a node before assessing it.** Stale pre-re-genesis residue has previously looked exactly like
  a P0 chain-splice. Delete `walkthroughs/**/state.json` after any wipe.
- `Publish-SorchaBlueprint` always mints a new blueprint id and cannot republish — use
  `PUT /blueprints/{id}` then `POST /publish`.
- Never `git add -A`; stage explicit paths.

## What is already proven, so you need not re-derive it

4,362 tests green across six suites; solution builds clean; ownership gate OK with 4 permitted call
sites; no pending model changes. Mutation matrices for Phases 2, 3, 5 and 6 are recorded in
`mutations.md`, including the two findings worth carrying:

- **The golden vector fired on its first real change** — deleting two *dead but serialized* version
  fields moved every publication id on every register, and nothing else in 4,300 tests noticed.
- **Adding a property and omitting it from the projection** killed only the reflection guard, while
  every hand-written hasher test stayed green.

## If the live run finds something

Expect it to. Record it in `mutations.md` and `MASTER-TASKS.md`, fix it, and re-run — a live finding
is the point of the exercise, not a setback. Do not close the issues until the run is green and
`pin_fallback` reads zero.
