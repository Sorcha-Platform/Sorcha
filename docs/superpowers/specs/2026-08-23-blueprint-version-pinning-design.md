# Blueprint version pinning — an instance runs the definition it started on

**Date:** 2026-08-23
**Issue:** #1559
**Status:** Design agreed, not implemented
**Feature number (proposed):** 194

---

## 1. The problem

Republishing a blueprint to a register it is already published to is **accepted**, increments a
version number, and then **silently replaces the executable definition for every instance of that
blueprint id — including instances already in flight**.

Confirmed live on n1 (2026-08-23), same blueprint id, same register:

```
attempt 2:  HTTP 200  {"version":2, ...}
attempt 3:  HTTP 200  {"version":3, ...}

GET /blueprints/{id}/versions
    version=1  publishedAt=07:53:57
    version=2  publishedAt=08:22:11
    version=3  publishedAt=08:22:11
```

An instance mid-flow is then validated against actions and schemas it never saw. Nothing errors.
Both sides are individually correct — publish does what it is asked, the validator resolves the
current definition — and the join is unverified and silent when wrong.

### 1.1 Why the version is inert today

Four links, all keyed on blueprint id alone:

| Link | Location | Behaviour |
|---|---|---|
| Validator resolution | `ValidationEngine.cs:2380` | `ResolveBlueprintAsync(transaction.BlueprintId!, ct)` — no version parameter |
| Validator cache key | `BlueprintCache.cs:134` | `sorcha:validator:blueprint:{blueprintId}` — one entry per id |
| Recovery from register | `BlueprintRecoveryService.cs:326` | `.GroupBy(b => b.BlueprintId).Select(g => g.OrderByDescending(b => b.PublishedAt).First())` — newest wins, one per id |
| Version assignment | `InMemoryPublishedBlueprintStore.AddAsync` | `Version = versions.Count + 1` — derived from in-memory insert order |

### 1.2 The field that looks authoritative and is a constant

`Instance.BlueprintVersion` (`Models/Instance.cs:28`) is `required int` and is **written by two paths
that disagree**:

```
Program.cs:2340                 BlueprintVersion = resolvedVersion   // imperative: latest at creation
InstanceProjector.cs:170        blueprintVersion: 1,                 // HARDCODED
InstanceRebuildService.cs:104   blueprintVersion: 1,                 // HARDCODED
```

Since F145 made the **projector the single instance writer on every node**, in practice almost every
instance records `1` regardless of what was published. Its only readers are display surfaces
(`Sorcha.Cli/Commands/InstanceCommands.cs` prints `v{BlueprintVersion}`).

### 1.3 Why the chain does not already encode it

A starting action anchors on the blueprint publish transaction — but
`ActionExecutionService.ComputeBlueprintPublishTxId(registerId, blueprintId)` is **deterministic from
register + blueprint and version-blind**. Every version of a blueprint shares one anchor.

`TransactionMetaData` (`src/Common/Sorcha.Register.Models/TransactionMetaData.cs`) carries
`BlueprintId`, `InstanceId`, `ActionId`, `RoutingDecision` — and **no blueprint version**.

**So the projector genuinely cannot derive which definition an instance started under.** Under F145,
where an instance is a deterministic projection of the sealed ledger, that is fatal to pinning: a
value that cannot be folded from sealed transactions cannot be agreed on by two nodes.

---

## 2. Decisions taken

| # | Decision | Rationale |
|---|---|---|
| **D1** | **An in-progress instance always runs the definition it started on.** Not a per-upgrade choice — a hard rule. | Forcing a running instance onto a new definition can have serious consequences: a participant could face an action, schema or routing rule that did not exist when they joined. |
| **D2** | **Publishers may upgrade freely.** No platform-level multi-party gate on the upgrade itself. | Where multi-party sign-off is wanted, it is expressed by authoring a **governance blueprint** — the platform already has the primitive. Dogfooding beats bespoke machinery. |
| **D3** | **Never block an upgrade because instances are live.** | Long-running workflows may never have a quiet moment. Blocking would make upgrade unusable in exactly the cases that need it. |
| **D4** | **Pin on the executable-definition hash, not the ordinal version.** The ordinal remains a display label. | The ordinal is assigned from in-memory insert order and re-derived on recovery, so it is only stable if recovery restores every version in a deterministic order forever. The hash is content-addressed and stable by construction. It also already ignores presentational changes (F142 `FormKeywordClassifier`), so relabelling a field does not strand instances on an older definition for no reason. |

**Explicitly out of scope:** migrating a running instance forward onto a new definition. D1 rules it
out. If it is ever wanted it is a separate feature with its own ledger representation.

---

## 3. Architecture

### 3.1 The pin must be a sealed ledger fact

Under F145 every node folds the same transactions and must reach the same instance state. The pinned
definition therefore has to travel on the transaction, not be looked up per-node at fold time.

**Carry it on `RoutingDecision`**, which already rides every forward-routing action transaction, is
sender-signed, and is verified by the validator.

This is the F184 precedent exactly. `RoutingDecision` gained `routeId` and `reasonCode` the same way:

> Both are **copied into `ComputeSignableBytes()`'s field-by-field rebuild** — a field omitted there
> would ride the wire unauthenticated while appearing signed — so `VAL_ROUTING_002` verifies them
> with **zero new validator code**.

```
RoutingDecision {
  completedActionId : int
  nextActions       : ActionRef[]
  routeId           : string?
  reasonCode        : string?
  blueprintExecDefHash : string?     // NEW — the definition this action was executed against
  attestation       : Attestation
}
```

⚠ **`ComputeSignableBytes()` rebuilds field by field.** Adding `blueprintExecDefHash` to the record
without adding it there means it rides the wire **unauthenticated while appearing signed**. The F189
lesson applies: guard the binding with a **reflection-driven** test over the type's properties, never
a hand-written field list.

### 3.2 Where the pin is established and enforced

| Stage | Behaviour |
|---|---|
| **Publish** | `PublishService.PublishAsync` computes `ExecutableDefinitionHasher.ComputeHash(blueprint)` and stores it on the `PublishedBlueprint` alongside the ordinal `Version`. |
| **Starting action** | `ActionExecutionService` resolves the **latest** published definition for the register, and stamps its hash on the action's `RoutingDecision`. This is the moment the instance's definition is chosen. |
| **Projector — instance creation** | `InstanceProjector` reads the hash from the starting action's sealed `RoutingDecision` and sets `Instance.BlueprintExecDefHash`. The two hardcoded `blueprintVersion: 1` sites are removed. |
| **Subsequent actions** | `ActionExecutionService` stamps the **instance's pinned** hash, not the latest. The projector asserts the folded transaction's hash equals the instance's pin; a mismatch is a divergence and is refused. |
| **Validator** | `ResolveBlueprintAsync(blueprintId, execDefHash, ct)` resolves the pinned definition. A hash that resolves to nothing is a typed refusal, not a silent fallback to latest. |
| **Cache** | `BlueprintCache` keyed `sorcha:validator:blueprint:{blueprintId}:{execDefHash}`. |
| **Recovery** | `BlueprintRecoveryService` restores **every** published version, not `GroupBy(id).First()`. |

### 3.3 Model changes

```
Instance
  + BlueprintExecDefHash : string      // the pin. required going forward.
    BlueprintVersion     : int         // retained, display-only, sourced from the published store

PublishedBlueprint
  + ExecDefHash : string               // computed at publish

TransactionMetaData.RoutingDecision
  + BlueprintExecDefHash : string?     // inside ComputeSignableBytes
```

---

## 4. Behaviour under the decisions

### 4.1 The happy path

1. Blueprint `X` published to register `R` → `execDefHash = h1`, ordinal `v1`.
2. Instance `A` starts. Its starting action carries `h1`. Projector pins `A → h1`.
3. `X` republished with a changed schema → `h2`, ordinal `v2`.
4. Instance `A` continues. Every subsequent action carries `h1`; validator resolves `h1`. **`A` never
   sees `h2`.**
5. Instance `B` starts after the republish. Its starting action carries `h2`. `B` runs `h2`.

Both definitions live on the register simultaneously. Neither instance is disturbed.

### 4.2 A presentational-only republish

`ExecutableDefinitionHasher` ignores presentational keywords (F142 `FormKeywordClassifier`:
`title`, `description`, layout `x-*`, ordering hints). So relabelling a field produces the **same
hash** — no new pin, no new definition to resolve, running instances entirely unaffected, and the
F142 `RehearsalPass` is not invalidated either. This is the classifier paying for itself.

### 4.3 An instance whose action no longer exists in the new definition

**Cannot arise.** The instance is pinned to the definition that has the action. This is the whole
point of D1 and the reason no migration machinery is needed.

### 4.4 Governance over upgrades (D2)

Nothing is built. An organisation that wants multi-party sign-off before a blueprint upgrade authors
a governance blueprint whose terminal action is the upgrade approval, and publishes only once that
workflow completes. The platform neither knows nor cares.

Worth stating plainly in the spec so nobody later adds a bespoke gate.

---

## 5. Edge cases and failure modes

| Case | Behaviour |
|---|---|
| **Instance predating this feature** — its transactions carry no hash | Fall back to "latest published", log at Warning, and **take the identical fallback in both the online projector and `InstanceRebuildService`** or F145 rebuild parity breaks. Pre-release, so no migration is owed; the fallback exists to keep existing n1 data foldable. |
| **Transaction claims a hash that resolves to nothing** | Validator refuses with a typed code (e.g. `VAL_BP_VERSION_001`). Silent fallback to latest would reintroduce the exact defect. |
| **Subsequent action claims a hash ≠ the instance's pin** | Projector refuses the fold. A sender cannot move an instance onto another definition by claiming a different hash. |
| **Two publishes produce the same hash** (presentational-only change) | Same pin; nothing changes. Ordinal increments for human bookkeeping only. |
| **Cache miss on a pinned hash after restart** | Recovery must have restored all versions. If a hash cannot be resolved the instance is stuck — this is the failure mode to watch in the live test. |
| **Governance / control / lifecycle transactions** | Carry no `RoutingDecision` and are exempt from `VAL_ROUTING_*`. They are not instance-scoped and need no pin. Do not add one. |

---

## 6. Test strategy

**Unit**

- Reflection-driven test that **every** `RoutingDecision` property is covered by
  `ComputeSignableBytes()` — the F189 lesson; a hand-written list rots in the same direction as the bug.
- `ExecutableDefinitionHasher` stability: a presentational-only edit yields the same hash; a
  behavioural edit yields a different one.
- Cache key includes the hash.
- Recovery restores all versions, not newest-per-id.
- Validator refuses an unresolvable hash rather than falling back.

**Projection / F145**

- Folding the same transactions in any order yields the same pin.
- `InstanceRebuildService.RebuildAsync` and the online projector agree on the pin (parity is an
  existing F145 guarantee and must not regress).
- A subsequent action carrying a foreign hash is refused.

**Mutation-test every guard.** A guard written after the fix proves nothing until it has been watched
to fail. At minimum: remove the hash from `ComputeSignableBytes`; make the validator fall back to
latest; make recovery keep only the newest version. Each should kill exactly its own named test.

**Live, on n1 — this is the acceptance test and it cannot be skipped**

1. Publish blueprint `X` (v1) to register `R`.
2. Start instance `A`, execute its first action, leave it mid-flow.
3. Republish `X` with a **behavioural** change (add a required field to a later action's schema).
4. Advance instance `A` — it must succeed against **v1's** schema, without the new required field.
5. Start instance `B` — it must be pinned to v2 and must **require** the new field.
6. Restart `blueprint-service`, then advance `A` again — proves recovery restored v1, not just v2.

Step 6 is the one most likely to fail and the one most worth having.

---

## 7. Rollout

Pre-release, so CLAUDE.md §19 applies: **fold any schema change into each service's
`InitialCreate`** — do not add a migration — and recreate the database. `Instance` gains a column, so
a dev box or node that predates the change needs `docker compose down -v` and re-genesis, or the
column will simply not appear and the failure will surface far away as a raw Postgres error.

Deploy scope is `blueprint-service` **and** `validator-service` (the validator's resolve signature and
cache key change). Both are per-service recreates — no genesis window.

---

## 8. Open questions for implementation

1. Should `Instance.BlueprintVersion` be **dropped** rather than retained as display-only? Keeping a
   second identifier that is not the pin invites exactly the confusion this feature exists to remove.
   Recommendation: retain, but source it from the published store by hash so the two can never
   disagree.
2. Does anything outside the validator resolve a blueprint by id and assume "latest"? A sweep is
   needed before changing the resolve signature — the MCP designer tools and the UI both read
   blueprints.
3. Is the fallback in §5 (pre-feature instances) worth keeping past the first re-genesis of n1, or
   should it be deleted once no un-pinned instance exists? Leaving it is a permanent silent path back
   to the old behaviour.

---

## 9. Sources

Every claim above was verified against the code or produced by a live run on n1 on 2026-08-23:
`ValidationEngine.cs:2380`, `BlueprintCache.cs:134`, `BlueprintRecoveryService.cs:326`,
`InstanceProjector.cs:170`, `InstanceRebuildService.cs:104`, `Program.cs:2225-2340`,
`Models/Instance.cs:28`, `src/Common/Sorcha.Register.Models/TransactionMetaData.cs:26-43`,
`ExecutableDefinitionHasher.ComputeHash`, and the three-version republish transcript in §1.
