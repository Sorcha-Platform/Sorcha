# Kickoff prompt — Feature 194, blueprint version pinning

Paste everything below the line into a fresh session.

---

Load these skills before anything else:

```
/speckit.specify           — this is a speckit feature; spec → plan → tasks → implement
/blueprint-builder         — blueprint model, publish validation, routes, credential config
/sorcha-architecture       — F142 exec-def hash, F145 ledger-derived instances, F184 RoutingDecision
/n1-deploy                 — deploy + the live acceptance test
/walkthrough-builder       — the live test harness, cadence rules, and its traps
```

Also read memory: `n1-current-state`, `designer-cold-probe-findings`, `feedback_verification_discipline`, `migration-policy-release-switch`.

**Read the design first — it is the contract for this work, and it was written from verified code, not assumption:**

`docs/superpowers/specs/2026-08-23-blueprint-version-pinning-design.md`

Issue: **#1559**. Proposed feature number: **194**.

---

## What you are building, in one sentence

An instance must run the blueprint definition it started on, forever, even after the blueprint is
republished to the same register.

## Why it is not a small change

Republishing a blueprint to a register it is already on is accepted, increments a version, and
**silently replaces the executable definition for every instance of that id, including ones in
flight**. Confirmed live on n1 — three versions on one register, all accepted.

The version cannot simply be read at fold time, because F145 makes an instance a **deterministic
projection of the sealed ledger**: a value two nodes cannot both derive from sealed transactions is a
value they can diverge on. So the pin has to become a sealed fact on the transaction.

## The four decisions — already taken, do not relitigate

1. **An in-progress instance always runs the definition it started on.** A hard rule, not a
   per-upgrade choice. Migrating an instance forward is explicitly out of scope.
2. **Publishers upgrade freely.** No platform-level multi-party gate. Where sign-off is wanted it is
   authored as a **governance blueprint** — the platform already has the primitive. Do not build a
   bespoke gate.
3. **Never block an upgrade because instances are live.**
4. **Pin on the executable-definition hash, not the ordinal version.** The ordinal stays as a display
   label only.

## The three things the investigation established, so you do not re-derive them

- The chain does **not** already encode the version. `ActionExecutionService.ComputeBlueprintPublishTxId(registerId, blueprintId)`
  is **version-blind** — every version of a blueprint shares one anchor transaction.
- `TransactionMetaData` (`src/Common/Sorcha.Register.Models/TransactionMetaData.cs`) carries
  `BlueprintId`, `InstanceId`, `ActionId`, `RoutingDecision` — and **no blueprint version**.
- `Instance.BlueprintVersion` is written by two paths that disagree: `Program.cs:2340` uses the latest
  published version, while `InstanceProjector.cs:170` and `InstanceRebuildService.cs:104` both
  **hardcode `1`**. Under F145 the projector is the single instance writer, so in practice almost
  every instance records `1`. The field looks authoritative and is a constant.

## The load-bearing implementation trap

The pin rides on `RoutingDecision`, which already travels on every forward-routing action transaction
and is sender-signed and validator-verified. This follows F184's `routeId` / `reasonCode` precedent
exactly — **including its warning**:

> A field added to the record but omitted from `ComputeSignableBytes()`'s field-by-field rebuild
> **rides the wire unauthenticated while appearing signed.**

Guard that binding with a **reflection-driven test over the type's properties**. A hand-written field
list rots in the same direction as the bug. (F189 lost `ValidatorEntry` this exact way.)

## Definition of done

- Spec, plan and tasks under `specs/194-*/` via speckit.
- Implementation per the design's §3 (pin on `RoutingDecision`, projector reads it, validator resolves
  by `(id, hash)`, cache keyed by hash, recovery restores **all** versions, both hardcoded `1`s gone).
- Every guard **mutation-tested** — a guard written after the fix proves nothing until it has been
  watched to fail. Report which named test each mutation kills.
- Schema change folded into `InitialCreate`, **not** a new migration (CLAUDE.md §19, pre-release).
- `MASTER-TASKS.md` updated; skills updated if any documented behaviour changes.
- **The live acceptance test on n1 passes** — design §6, and step 6 (restart `blueprint-service`, then
  advance the old instance) is the one most likely to fail and the one most worth having. Deploy scope
  is `blueprint-service` **and** `validator-service`.
- PR per logical change, merged on green.

## Standing rules that will bite you if ignored

- **Verify by executing, not by asserting.** A green suite is not a live run; a RED test can be a
  WRONG test. This feature's entire existence is owed to a live run finding what 2,500 green tests did
  not.
- **Dry-run any new validation rule against the shipped blueprint corpus before writing the C#** —
  but know its limit: doing exactly that on the sibling feature validated a rule against blueprints
  that *exist* and still missed a shape none of them had. The live run caught it.
- `dotnet test` is **MTP mode** (global.json): `--filter-class "*Name*"`, `--project x.csproj`;
  `--collect` is dead.
- Use the **Write/Edit tools for `.ps1` and `.cs` files** — bash heredocs mangle PowerShell backticks
  and `$vars`, and a silently-unapplied patch costs a whole cycle. Verify the edit landed before
  running.
- n1 traps: the gateway `authentication` limiter is a sliding 1-min window per IP; delete
  `walkthroughs/**/state.json` after any node wipe; an `/execute` 202 means accepted, **not sealed** —
  verify by docket; never select a credential by type in a wallet that accumulates them.
- `admin@sorcha.local` is multi-org — `/api/auth/login` returns `requires_org_selection` and a
  `platform_login_token` to exchange at `/api/auth/select-org`.

## Open questions the design deliberately left for you

1. Should `Instance.BlueprintVersion` be dropped rather than retained as display-only? Recommendation
   in the design is retain, but source it from the published store **by hash** so the two can never
   disagree.
2. Sweep for anything outside the validator that resolves a blueprint by id and assumes "latest" —
   the MCP designer tools and the UI both read blueprints — before changing the resolve signature.
3. Whether the pre-feature fallback (instances whose transactions carry no hash) should survive past
   the first re-genesis of n1. Leaving it is a permanent silent path back to the old behaviour.

Start by reading the design document, then run `/speckit.specify` for feature 194.
