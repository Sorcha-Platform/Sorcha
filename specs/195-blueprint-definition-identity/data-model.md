# Data Model — Blueprint Definition Identity (Feature 195)

**Phase 1 output.** Entities, the fields that change, and the state each can be in. Sources are cited
where the current shape matters.

---

## The four concepts, and their one job each

Seven "version" concepts collapse to four (research R-010). Each has exactly one owner and one job.

| Concept | Job | Owner | Stable across restart? |
|---|---|---|---|
| `blueprintId` | the **thing** — survives every republish | author | yes |
| **publication txId** | the **definition** — pinned, anchored, resolved | the ledger, produced by Register Service | yes, by construction |
| `execDefHash` | *did this republish change behaviour?* | `ExecutableDefinitionHasher` | yes |
| ordinal `v1`/`v2` | display label | derived on read from ledger order | yes (docket order is stable) |

---

## Entity: Blueprint (unchanged identity, two fields removed)

`src/Common/Sorcha.Blueprint.Models/Blueprint.cs`

| Field | Change | Reason |
|---|---|---|
| `Id` | unchanged | the stable identity of the *thing* |
| `Version` (int) | **no longer an input to anything** | author-settable; F194 already removed it from the hash. Retained on the model as an author annotation, but nothing resolves, selects or orders by it |
| `VersionMajor` | **DELETE** | wholly dead — written by the amend clone and the designer properties panel, read by nothing |
| `VersionMinor` | **DELETE** | wholly dead — same |

**Invariant (new, and load-bearing):** every property on the serialized `Blueprint` / `Action` /
`Route` / `Participant` graph is part of the **ledger contract**. Its `[JsonPropertyName]` and its
`JsonIgnoreCondition` both contribute to a definition's identity. Changing either re-identifies every
definition on every register (research R-003).

---

## Entity: Publication (new as a first-class concept)

One immutable, executable version of a blueprint as published to one register. Recorded as a single
`BlueprintPublish` transaction on that register.

| Field | Source | Notes |
|---|---|---|
| **`publicationTxId`** | `SHA-256("sorcha:blueprint-publication:v1" ␟ registerId ␟ blueprintId ␟ canonicalDefinitionJson)` | the identity. Produced by the Register Service only |
| `registerId` | route | in the preimage — the same definition on two registers is two publications |
| `blueprintId` | request | in the preimage |
| payload | request | the **`$ref`-flattened** definition; what a recovering or validating node executes |
| `previousTransactionId` | `roster.LastControlTxId` | unchanged — publications chain on the control chain |
| metadata `Type` | `BlueprintPublish` | unchanged; drives the recovery type filter and the fork bypass |
| ~~metadata `contentHash`~~ | — | **DELETE** — absorbed. The id *is* the digest, so verification is self-anchoring |

**States.** A publication has no lifecycle: it is submitted, sealed, and thereafter immutable. It is
never superseded, retracted or deleted — a later publication of the same blueprint is a *sibling*, not
a replacement. This is the change that makes #1563 impossible.

**Uniqueness.** `publicationTxId` is unique by construction. Publishing byte-identical content to the
same register yields the same id and is an idempotent no-op (FR-004); any difference yields a new id
and a new record (FR-005).

---

## Entity: PublishedBlueprint (a cache of the ledger)

`src/Services/Sorcha.Blueprint.Service/Program.cs:4114-4140`

| Field | Change | Reason |
|---|---|---|
| **`PublicationTxId`** | **ADD** | the field whose absence caused the whole problem. Recorded from the Register Service's response; **never computed locally** (R-004) |
| `Version` (int) | **derived on read**, not stored | today assigned `versions.Count + 1` in memory and re-derived on every recovery, so it is not stable. Derived from ledger order instead (FR-019) |
| `ExecDefHash` | retained, job narrowed | now only answers "did behaviour change" for the rehearsal gate |
| `Blueprint` | unchanged | the deep-copied, flattened snapshot |
| `RegisterId`, `PublishedAt` | unchanged | |

**Resolution changes.**

- `GetByExecDefHashAsync` → **`GetByPublicationAsync(blueprintId, publicationTxId)`**. The
  `OrderByDescending(v => v.Version).First()` tie-break (`Program.cs:2904`) is **deleted** — a
  publication id has nothing to tie-break, which removes the path by which a pinned instance was
  handed the newest definition.
- `PublishedBlueprintSelector.SelectLatest` is retained for **authoring surfaces only** and must not
  be reachable from the execution path (FR-011).

---

## Entity: Instance (pin retyped)

`src/Services/Sorcha.Blueprint.Service/Models/Instance.cs:52`

| Field | Change |
|---|---|
| `BlueprintExecDefHash` | **rename + retype** → `BlueprintDefinitionTxId`. Same role (the pin), different value: a publication id rather than a behavioural hash |

**Invariants.**

- Set at instance creation, from the definition the instance is **initialised from** — the two must be
  the same definition (FR-009). Today creation initialises from the *draft* and pins the *latest
  published* (FINDINGS §12).
- Immutable for the life of the instance (FR-008).
- Never null for an instance created after this feature. A null pin is the pre-feature case only, and
  under the authorised wipe should not occur — the `pin_fallback` counter reading **zero** is the
  acceptance signal (SC-003).

---

## Entity: RoutingDecision (signed carrier — field renamed)

`src/Common/Sorcha.Register.Models/Transactions/RoutingDecision.cs:84-86`

| Field | Change |
|---|---|
| `BlueprintExecDefHash` (`blueprintExecDefHash`) | → `BlueprintDefinitionTxId` (`blueprintDefinitionTxId`) |

⚠ **`ComputeSignableBytes()` rebuilds the record field by field** (`:112`). The renamed field must be
carried in that rebuild, or it rides the wire **unauthenticated while appearing signed** — the
transaction signature covers only `{TxId}:{PayloadHash}`. `RoutingDecisionSigningCoverageTests` is
reflection-driven and fails on a property type it cannot mutate, so it catches the omission; the
hand-written per-field tests would not.

Old and new producers compute different canonical bytes and refuse each other — deploy validator
before blueprint (research R-012).

---

## Entity: Draft (unchanged, and deliberately so)

Node-local, mutable, in Blueprint Postgres. **Never recorded on a register. Never used to execute
anything** (FR-011, FR-021). Currently `ActionResolverService` resolves it *first* on the execution
path (`:45-104`) — that is the defect #1567 removes, not a property to preserve.

Non-durability is a decision, not a gap: a draft is work-in-progress on one node.

---

## Cache keys

`src/Common/Sorcha.Blueprint.Models/BlueprintCacheKey.cs`

| Key | Shape | Change |
|---|---|---|
| Validator definition cache | `sorcha:validator:blueprint:{blueprintId}:{pin}` | unchanged shape; `pin` now denotes a publication id. Parameter renamed so the name does not lie |
| Validator by-id cache | `sorcha:validator:blueprint:{blueprintId}` | **retained** — system blueprints have no instance and therefore no pin. F194 proved this tier is required by breaking 40 tests when it was removed |
| Engine action-resolver cache | `blueprint:{blueprintId}` (`ActionResolverService.cs:54`) | **must carry the pin** |
| Engine static action index | keyed by bare id (`ActionResolverService.cs:30`) | **must carry the pin, or be removed** — a process-wide dictionary keyed by bare id serves the wrong definition to a *different instance* than the one that populated it |

Content-addressed entries are immutable, so they are **evicted, never invalidated**.

---

## What recovery reconstructs

`BlueprintRecoveryService`

| Aspect | Today | After |
|---|---|---|
| Source | `GET /registers/{id}/blueprints/published`, filtered by transaction **type** | unchanged |
| Dedupe key | recomputed `execDefHash` (`:~393-400`) | **`publicationTxId`**, read from the transaction |
| Provenance check | recompute `BlueprintContentHash` and compare to the sealed `contentHash` (`TryVerifyProvenance:310-330`) | recompute the **publication id** from the received bytes and compare to the transaction's **own id** — self-anchoring, one fewer sealed field |
| Ordering | oldest-first | unchanged — it is what makes the derived ordinal stable |

---

## Deletions

| Removed | Why |
|---|---|
| `ActionExecutionService.ComputeBlueprintPublishTxId` (`:2989`) + its 2 call sites | the anchor is read from the instance's pin, not computed |
| The Register Service's inline txId derivation (`Program.cs:2018`) | replaced by the publication id |
| `ActionExecutionServiceTests.cs:1271` formula copy | a guard that duplicates what it guards |
| `BlueprintContentHash` (`ServiceClients.Http/Register/`) | absorbed into the publication id |
| The `contentHash` transaction metadata key | same |
| `Blueprint.VersionMajor`, `Blueprint.VersionMinor` | wholly dead |
| Stored `PublishedBlueprint.Version` | derived on read instead |
| The instance-creation publish branch (`Blueprint/Program.cs:2305-2318`) | one writer (FR-021) |
| `GetByExecDefHashAsync`'s ordinal tie-break (`:2904`) | nothing to tie-break |
