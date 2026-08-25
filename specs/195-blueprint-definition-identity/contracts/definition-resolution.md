# Contract — Definition Resolution

**Feature 195.** Which definition each component resolves, by what key, and what it must do when it
cannot. Every rule here is a rule about *not substituting a plausible answer*.

---

## 1. The resolution table

| Caller | Resolves | By | On failure |
|---|---|---|---|
| **Engine — action submission** | the instance's definition | `instance.BlueprintDefinitionTxId` | **refuse the submission**, diagnosable reason |
| **Engine — instance creation** | the definition to pin | latest **published** on the target register | refuse creation |
| **Validator — pinned transaction** | the pinned definition | `RoutingDecision.blueprintDefinitionTxId` | **refuse the transaction** (`VAL_BP_VERSION_001`) |
| **Validator — unpinned transaction** | latest by id | `blueprintId` | existing pre-feature fallback, counted |
| **Recovery** | every definition on the register | transaction **type** filter | mark register offline |
| **Authoring surfaces** (designer, versions list, amend picker) | latest, or a named definition | `blueprintId`, or `publicationTxId` | 404 |

**The draft store appears in exactly one row: none of them on the execution path.** It is reachable
only from authoring surfaces (FR-011). Today `ActionResolverService.cs:45-104` resolves it **first**
for execution, which is the defect.

---

## 2. Never substitute

The single most important rule in this feature, and the one every failure mode violates by default:

> **A component that cannot resolve the definition it was asked for MUST refuse, and MUST NOT resolve
> any other definition — in particular not the most recent one.**

This is already correct in the validator's pinned path
(`ValidationEngine.ResolvePinnedBlueprintAsync:2482-2516`, whose comment states there is deliberately
no fallback to latest) and must be extended to the submit path.

Rationale: every defect this feature addresses degrades to *plausible behaviour*, not to an error. A
substituted definition produces a sealed transaction, a valid-looking instance and a green suite. A
refusal produces a diagnosable failure a human can act on.

---

## 3. Validator resolution order (changed)

```
resolve(blueprintId, pin):
    if pin is null:                       # system blueprints; pre-feature transactions
        → existing latest-by-id path, counted as pin_fallback
    → content-keyed cache          sorcha:validator:blueprint:{blueprintId}:{pin}
    → Blueprint Service by publication
    → NEW: read transaction {pin} from the register        ← the addition
    → REFUSE  (VAL_BP_VERSION_001). Never latest.
```

**The new arm is what closes #1563's downstream symptom.** Under Option D the pin *is* a transaction
id on a register every validating node holds, so a definition that reached the ledger is always
resolvable — `pinState=unresolvable` becomes unreachable for any definition the register actually
holds. That arm was not available before, because the pin was a hash of a projection and named no
ledger fact.

The by-id cache tier is **retained**: system blueprints have no instance and therefore no pin. F194
proved this tier is required by removing it and breaking 40 validator tests.

---

## 4. Engine submit-path resolution (changed)

```csharp
// Interfaces/IActionResolverService.cs:19 — today
Task<BlueprintModel?> GetBlueprintAsync(string blueprintId, CancellationToken ct);

// after
Task<BlueprintModel?> GetBlueprintAsync(string blueprintId, string definitionTxId, CancellationToken ct);
```

**The pin is required, not optional.** An optional parameter preserves the defect for every caller
that omits it — which is exactly how it survived Feature 194, whose research listed this call site as
in scope.

Both caches carry it:

| Cache | Today | After |
|---|---|---|
| distributed (`ActionResolverService.cs:54`, 10-min TTL) | `blueprint:{blueprintId}` | `blueprint:{blueprintId}:{definitionTxId}` |
| static action index (`:30`) | bare `blueprintId` | keyed by `(blueprintId, definitionTxId)`, **or removed** |

⚠ The static index is process-wide, so a bare-id key serves the wrong definition **to a different
instance than the one that populated it**. It is not a per-request cache and cannot be reasoned about
as one.

Content-addressed entries are immutable: **evict, never invalidate.**

---

## 5. Instance creation

```
create(blueprintId, registerId):
    definition ← latest PUBLISHED on registerId     # NOT the draft
    instance.CurrentActionIds     ← from definition
    instance.ParticipantWallets   ← from definition
    instance.BlueprintTitle       ← from definition
    instance.BlueprintDefinitionTxId ← definition.PublicationTxId
```

**One definition, used for both initialisation and the pin** (FR-009). Today creation initialises from
the **draft** (`Blueprint/Program.cs:2251`, `:2346-2365`) and pins the **latest published**
(`:2372-2376`) — two different definitions whenever the author has edited since publishing.

---

## 6. The starting-action anchor

```
if action.IsStartingAction and no prior transaction:
    anchor ← instance.BlueprintDefinitionTxId      # READ, not computed
    await WaitForTransactionConfirmation(registerId, anchor)
    previousTransactionId ← anchor
```

Two properties are preserved deliberately:

- **The confirmation wait stays.** It is a genuine precondition — *this definition is sealed on this
  register* — not a chain filler. It was the strongest argument against anchoring on genesis. Under
  this feature it asserts something **stronger**: the exact definition, not merely the blueprint.
- **A Control-typed predecessor may have N children** (`ValidationEngine.cs:1218-1221`, types
  `{Control, BlueprintPublish, null}`). This is what makes "many instances of one blueprint"
  representable at all, and it is the same bypass register-governance quorum depends on. Unchanged.

`ComputeBlueprintPublishTxId` and both its call sites are deleted.

---

## 7. Ordinal version labels

- **Derived on read** from the order of publication transactions on the register (oldest first).
- **Never an input.** No component may select, resolve or order a definition by it (FR-018).
- Stable across restart, because docket order is. Today `versions.Count + 1` in memory is not.

The amend picker selects by **publication txId**, not by ordinal
(`BlueprintFromPublishedEndpoint.cs:116` today selects by ordinal — so amending "v2" before and after
a restart may clone different definitions).

---

## 8. Amendment

Amending a published definition produces **a new version of the same blueprint**:

```
amend(blueprintId, publicationTxId):
    draft ← clone of that definition
    draft.Id ← blueprintId          # NOT a new GUID
```

`BlueprintFromPublishedEndpoint.cs:152` currently sets `clone.Id = Guid.NewGuid().ToString()`, making
amendment a fork that never appears in the blueprint's version history. The F142 lineage metadata is
retained for the designer rail's "Amending vN" display, with the version key carrying the publication
id.

---

## 9. Observability

| Signal | Meaning | Acceptance |
|---|---|---|
| `pin_fallback{path}` | a transaction resolved without a pin | **zero** on a register created after this feature (SC-003) |
| `pin_mismatch` | a fold saw a foreign definition | zero |
| refusal log — unresolvable pin | a definition is genuinely absent | must name the blueprint **and** the publication id |
| refusal log — submit-path resolution failure | new; the submit-side equivalent | same |

**The acceptance check is the positive one.** Absence of errors is not evidence in this area, because
every failure mode degrades to plausible behaviour rather than to an exception.
