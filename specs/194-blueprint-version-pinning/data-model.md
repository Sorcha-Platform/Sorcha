# Phase 1 Data Model: Blueprint Version Pinning (Feature 194)

**Branch**: `194-blueprint-version-pinning` | **Date**: 2026-08-23 | **Spec**: [spec.md](./spec.md)

Every change below is additive. No field is removed, no wire value is repurposed.

---

## 1. `RoutingDecision` — the carrier

`src/Common/Sorcha.Register.Models/Transactions/RoutingDecision.cs`

| Field | Type | Change | Notes |
|---|---|---|---|
| `CompletedActionId` | `int` | — | |
| `NextActions` | `List<ActionRef>` | — | |
| `RouteId` | `string?` | — | F184 |
| `ReasonCode` | `string?` | — | F184 |
| **`BlueprintExecDefHash`** | **`string?`** | **NEW** | Lowercase hex SHA-256. The executable definition this action was executed against. Nullable so pre-feature transactions deserialise; omitted from the wire when null (`JsonIgnoreCondition.WhenWritingNull`), so a null never changes an existing signature. |

**`ComputeSignableBytes()` MUST copy it.** The method is a hand-written field-by-field rebuild; a
field added to the record and not to the rebuild rides the wire unauthenticated while appearing
signed. See the guard below — this is the single most important line in the feature.

### Validation

- Set on every forward-routing action transaction the workflow service produces.
- Null is accepted only as the pre-feature case (R-009); never written as null by new code.
- Format: 64 lowercase hex characters when present.

---

## 2. `Instance` — the pin

`src/Services/Sorcha.Blueprint.Service/Models/Instance.cs` and
`Data/Entities/InstanceEntity.cs`

| Field | Type | Change | Notes |
|---|---|---|---|
| **`BlueprintExecDefHash`** | **`string`** | **NEW** | The pinned definition. Set once, at creation, from the starting action's sealed routing decision. |
| `BlueprintVersion` | `int` | **semantics change** | Retained as a display label (FR-019) but must now be **derived from the pin** rather than recorded independently, so the two cannot disagree. The five hardcoded `= 1` writes (R-008) are the thing being removed. |
| `LastAppliedTxId` | `string?` | — | Existing F145 fold watermark; unchanged. |

### State transitions

`BlueprintExecDefHash` has exactly one transition and it is not reversible:

```
(unset)  --[ starting action folds ]-->  <hash>   [terminal — immutable for the life of the instance]
```

Nothing else may write it. A fold that would change an already-set pin is a divergence and is
refused (FR-007), not applied.

### Persistence

- Column added to the existing `20260528205017_InitialCreate` migration, its `.Designer.cs` and
  `BlueprintDbContextModelSnapshot.cs` — **not** a new migration (R-011).
- `EfCoreInstanceStore.UpdateAsync` copies model → entity by hand; the field must be added to that
  list or it is written in memory, reported saved, and lost.

---

## 3. `PublishedBlueprint` — the resolvable definition

`src/Services/Sorcha.Blueprint.Service/Program.cs` (record) and `IPublishedBlueprintStore`

| Field | Type | Change | Notes |
|---|---|---|---|
| `BlueprintId` | `string` | — | |
| `Version` | `int` | — | Ordinal, display only. Assigned `versions.Count + 1`. |
| `Blueprint` | `BlueprintModel` | **semantics change** | Must become a genuine **deep copy** taken after `$ref` flattening, not the live draft reference it is today (R-004). |
| `PublishedAt` | `DateTimeOffset` | — | |
| `RegisterId` | `string?` | — | |
| **`ExecDefHash`** | **`string`** | **NEW** | Computed at publish over the stored copy. The resolution key. |

### Store interface

`IPublishedBlueprintStore` gains one method:

```
Task<PublishedBlueprint?> GetByExecDefHashAsync(string blueprintId, string execDefHash)
```

Existing methods are unchanged. `GetVersionsAsync` remains the version-history surface.

### Invariant

`(BlueprintId, ExecDefHash)` resolves to exactly one definition, or to nothing. Two publishes
producing the same hash (a presentational-only republish) are the same definition and may share one
entry or two identical ones — but must never resolve ambiguously to different content.

---

## 4. `ProjectedTransaction` — the fold carrier

`src/Services/Sorcha.Blueprint.Service/Services/Implementation/InstanceProjection.cs`

| Field | Change |
|---|---|
| `TxId`, `PreviousTransactionId`, `CompletedActionId`, `NextActionIds`, `ParticipantBindings`, `RouteId`, `ReasonCode` | — |
| **`BlueprintExecDefHash`** | **NEW**, `string?`. Read from the sealed routing decision by `InstanceProjectionResolver`; null for a pre-feature transaction. |

`InstanceProjection.Project(...)` loses its `blueprintVersion` parameter in favour of the pin
carried on the projected transactions. `Apply(instance, tx)` asserts pin equality before applying.

**Determinism requirement**: `Project` folds a transaction set in chain order and must be
order-independent. The pin comes from the **starting** transaction in the chain, so it is
order-independent by construction — but the assertion in `Apply` must compare against the instance's
established pin, never against "the first one seen in this batch".

---

## 5. Blueprint cache key

`src/Services/Sorcha.Validator.Service/Services/BlueprintCache.cs` + the duplicate literal in
`Sorcha.Blueprint.Service/Program.cs` (R-005).

```
before:  sorcha:validator:blueprint:{blueprintId}
after:   sorcha:validator:blueprint:{blueprintId}:{execDefHash}
```

The format moves to one shared helper both projects call. `IBlueprintCache`'s id-taking methods gain
the hash. The Redis invalidation channel payload becomes the composite key rather than a bare id.

**An entry keyed by content is immutable**, so most invalidation becomes unnecessary — but the
channel must still carry an unambiguous key or a subscriber cannot know what to drop.

---

## 6. Executable-definition hash — projection change

`src/Core/Sorcha.Blueprint.Engine/Implementation/ExecutableDefinitionHasher.cs`

`["version"] = blueprint.Version` is **removed** from the hashed projection (R-003). Nothing else
about the classifier or the canonicalisation changes.

Consequence: every blueprint's hash changes, invalidating every recorded F142 `RehearsalPass`.
Pre-release and re-earnable; expected, and must not be mistaken for a defect during the live test.

---

## Entity relationships

```
Blueprint (draft, mutable, node-local)
    │  publish  ── deep copy → flatten $refs → hash
    ▼
PublishedBlueprint  { BlueprintId, ExecDefHash, Version, Blueprint }
    │                        ▲
    │  latest at start       │  resolve by (BlueprintId, ExecDefHash)
    ▼                        │
RoutingDecision.BlueprintExecDefHash   ──signed, sealed──►  ledger
    │
    │  folded by InstanceProjector / InstanceRebuildService (identically)
    ▼
Instance.BlueprintExecDefHash   [immutable]
    │
    └─► every subsequent action asserts equality, and the validator resolves by it
```
