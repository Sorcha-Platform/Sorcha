# Blueprint lifecycle — what should be true

**Date:** 2026-08-24
**Companion to:** `2026-08-24-blueprint-lifecycle-investigation-KICKOFF.md` (direction)
and `2026-08-24-blueprint-lifecycle-current-state-FINDINGS.md` (what is true today, with citations)
**Status:** design. Deliverables B, C and the "what should be true" half of A. **No implementation.**

This document does not repeat the current-state findings. Read the FINDINGS companion first; every
"today" claim here is a one-line reference back to it.

---

## The decision (deliverable B) — Option D

> **A blueprint definition's identity is the transaction that published it.**
> The instance pins to that transaction id. A starting action chains from that same transaction.
> Anchor and pin are one value because they are one fact.

Four options were evaluated. A (a separate stable anchor transaction) and B (content-address the
publish transaction, derive the anchor from the pin) both argue about *which formula to recompute*.
C (anchor on genesis) removes a real precondition to delete one formula. **D removes the reason the
formula exists at all**: the anchor is recomputed only because `PublishedBlueprint` never recorded
the transaction id it was published as (FINDINGS §"the anchor", `Blueprint/Program.cs:4114-4140`).
Record it, and the derivation and its four copies delete.

**#1563 is not fixed by D; it stops existing.** One identifier, one job.

### Why D beats B on more than tidiness

| | Consequence |
|---|---|
| **Finding 8 stops being load-bearing** | The pin addresses the whole published definition, so `RejectionConfig`, legacy `Participants` routing, `RequiredActionData`, `BranchDeadline`, `DecisionNotice`, `PresentationConfig` and `InstanceReference` are all inside it. The `ExecutableDefinitionHasher` coverage gap drops from *"a pinned instance is silently handed the newest definition"* to *"a rehearsal pass is over-valid"* — back to the soft-gate nuisance F142 could tolerate. Still fixed (below); no longer a correctness hole. |
| **The tie-break deletes** | `GetByExecDefHashAsync`'s `OrderByDescending(Version).First()`, justified by a false premise, has nothing to tie-break. |
| **The pin becomes self-resolving from the ledger** | A validator's last-resort fallback becomes "read transaction `{pin}` from the register" — which every node holding the register can always do. That is precisely the `pinState=unresolvable` symptom, closed. |
| **Idempotency is fixed rather than removed** | Republishing byte-identical content still dedupes to the same transaction — correctly. Different content writes a new one. #1563's dedupe was right in intent and wrong in key. |

### The construction

```
publicationTxId = hex( SHA-256(
      "sorcha:blueprint-publication:v1"   ␟
      registerId                          ␟
      blueprintId                         ␟
      canonicalDefinitionJson ) )

      ␟ = 0x1F (ASCII unit separator)
```

Three deliberate choices, each earned:

- **Register-scoped.** A definition published to two registers is byte-identical *by construction*
  (same template, same model, same serializer). Without `registerId` in the preimage, one
  transaction id names two ledger facts, and every `(registerId, txId)` lookup, receipt, inclusion
  proof and log line becomes ambiguous.
- **Domain-tagged.** `InstanceIdentity.Derive`
  (`Blueprint/Services/Implementation/InstanceIdentity.cs:35-53`) is already
  `SHA-256(registerId ␟ blueprintId ␟ startingActionTxHash)`. An untagged publication id would be
  the *same preimage construction sharing its first two fields* — two kinds of identity
  indistinguishable by shape. The tag closes that class permanently and costs nothing.
- **Canonical means canonical.** `BlueprintContentHash.Compute`
  (`ServiceClients.Http/Register/BlueprintContentHash.cs:30-37`) parses to `JsonDocument` and
  re-serializes: whitespace is normalised, **property order is preserved as it arrived**. That is a
  *serializer-output* address, not a content address. Under D this value is the identity, so the
  canonical form must be RFC 8785-style: recursively key-sorted, no insignificant whitespace, UTF-8.

### One owner, not one shared home

Only the **Register Service** computes this. Blueprint Service pushes the definition and **records
the returned txId** on `PublishedBlueprint`. Nothing else recomputes it — not recovery (it reads
real transaction ids), not instance creation (it reads the store), not the starting action (it reads
the instance's pin). CLAUDE.md §15/§16 exist because this class of value keeps getting two homes;
D gets the stronger form, which is *one producer*.

Recovery's provenance check (`BlueprintRecoveryService.TryVerifyProvenance:310-330`) becomes
"recompute the id from the received bytes and compare it to the transaction's own id" — self-
anchoring, so a tampered payload cannot match its own transaction id.

### Migration

**None.** Pre-release; a wipe is authorised (Stuart, 2026-08-24). Every legacy-id fallback that A
and B required is dropped, and `RoutingDecision.BlueprintExecDefHash` can be renamed to
`blueprintDefinitionTxId` despite being inside `ComputeSignableBytes` — old and new producers would
otherwise compute different canonical bytes and refuse each other.

**The acceptance bar is a live re-genesis followed by the positive checks in "What proves it, live"
below.** A wipe removes the
migration risk; it does not remove the obligation to prove the system still works.

---

## The identity model — seven concepts to four

Today there are seven (FINDINGS §14). After D:

| Concept | Job | Owner | Stable? |
|---|---|---|---|
| `blueprintId` | the **thing** — survives every republish | author | yes |
| **publication txId** (absorbs `contentHash`) | the **definition** — the exact bytes an instance runs and a starting action chains from | the ledger, via Register Service | yes, by construction |
| `execDefHash` | *did this republish change behaviour?* — the F142 rehearsal-gate key and the author's "you need a fresh rehearsal" signal | `ExecutableDefinitionHasher` | yes |
| ordinal `v1`, `v2` | **display label**, derived from ledger order | derived on read | yes (docket order is stable) |

Deleted: `Blueprint.Version`, `Blueprint.VersionMajor`, `Blueprint.VersionMinor` (the last two
wholly dead), `PublishedBlueprint.Version` as a stored insert-order value, and the SSR-catalogue
`version` echoed in the publish response (which is always `1` for a tenant blueprint and means
something else entirely).

**The two hashes now have genuinely different jobs and must not be merged.** The publication txId
answers *"which bytes"*; `execDefHash` answers *"did behaviour change"*. A presentational-only
republish writes a **new publication** (so relabels ship) while leaving `execDefHash` unchanged (so
the F142 rehearsal pass stays valid and the author is not asked to re-rehearse a relabelling). Today
those two concerns are conflated in one value, which is why widening its meaning in F194 silently
widened nothing.

---

## The lifecycle, stage by stage

Each stage states the rule. Where today differs, the gap is named with its FINDINGS reference.

### 1. Authoring

**Rule.** A draft is node-local, mutable, and **never reaches the ledger**. Exactly one surface
promotes a draft to a definition: `POST /api/blueprints/{id}/publish`.

The authoring surfaces — chat/designer, template seeding, the fluent API, direct JSON `PUT` — all
produce drafts and are peers. None of them is a publication path.

**Gap:** instance creation pushes the draft straight to the register (FINDINGS §13, §4). It must
not. See stage 3.

### 2. Validation

**Rule.** Publish-time validation is **one rule set with one severity table**. The chat tool and the
publish endpoint are two *callers* of it, not two implementations of it.

A rule is an **error** when a blueprint carrying it can never execute correctly, and a **warning**
when it can execute but the author probably did not mean it. That test — not history — decides
severity, and every divergence from it is justified in the table or removed.

**Gaps:**
- Two implementations that disagree in both directions (FINDINGS §11). The publish path is stricter
  on some rules and **weaker on reachability**; neither is a superset, which contradicts the
  `blueprint-builder` skill's standing advice.
- "Unreachable action" is two algorithms under one name: a set-union over all actions' route targets
  (publish, so a detached cycle passes) versus a real BFS from starting actions (chat). #1558
  addresses only the severity; promoting the publish rule to an error without fixing the traversal
  leaves detached cycles publishable.
- The publish path has no equivalent of `NO_ROUTING_DEFINED`, `STARTING_ACTION_NO_ROUTES`,
  `NO_TERMINAL_PATH` or `DUPLICATE_PARTICIPANT_ID`.

**Sequencing note.** This is separable from D and should stay separate — it is the one part of this
investigation that can ship on its own.

### 3. Publication

**Rule.** A publication is a single transaction whose payload is the **flattened** definition,
serialized by **one** serializer with **one** naming policy, canonicalised by the Register Service,
and identified by the construction above.

Flattening is not an optimisation: the ledger copy is what a recovering or validating node
*executes*, and an unflattened one needs the core-schema catalogue to resolve its `$ref`s. A node
that has the catalogue and a node that does not would execute different definitions from the same
bytes.

**Gap (expanded from FINDINGS §13 — new).** The two paths differ in more than flattening:
`PublishService` serializes the ledger payload with **default (PascalCase)** options
(`Blueprint/Program.cs:3203`) while caching the *same* definition for the validator in **camelCase**
(`:3253-3256`); the instance-creation path serializes in **camelCase** (`:2306-2310`). Under content
addressing, casing alone yields two different definition ids for one blueprint. So the collapse of
the two paths must pin **three** things — which path writes, flattening, and the naming policy —
not two.

**This collapse is a prerequisite of D, in the same change.** Content-addressing without it trades a
silent drop for a silent fork: two shapes of one blueprint would both land under different ids and
recovery would restore both as distinct definitions.

### 4. Identity and versioning

The table above. Two further rules:

- **Nothing resolves a definition by ordinal.** Today the amend loop does
  (`BlueprintFromPublishedEndpoint.cs:116`), against a value re-derived from insert order on every
  recovery — so amending "v2" before and after a restart may clone different definitions
  (FINDINGS §10).
- **The ordinal is derived on read**, from the order of publication transactions on the register. It
  is presentation, and it is never an input to anything.

### 5. Execution

**Rule.** *Every* definition lookup on the execution path is by pin. The engine that validates a
payload, evaluates calculations and computes a route must be looking at **the same definition** the
validator will enforce at seal.

Concretely:

- `IActionResolverService.GetBlueprintAsync(blueprintId, pin, ct)` — the pin is a parameter, not
  optional.
- **Both** caches carry the pin: the distributed `blueprint:{id}` key and the static
  `_actionIndexCache` (`ActionResolverService.cs:30`, `:54`).
- The **draft store is not on the execution path at all.** Authoring surfaces resolve latest-or-
  draft; the engine resolves the pin, and only the pin.
- Instance creation initialises `CurrentActionIds`, `ParticipantWallets` and `BlueprintTitle` from
  **the definition it pins**, not from the draft.

**Gaps:** FINDINGS §9 (the resolver takes no pin, resolves draft-first, caches by bare id — so the
engine signs a routing decision labelled with a definition it did not use) and §12 (instance
creation reads the draft and pins the published). §9 is the most consequential defect in this
lifecycle after #1563 itself: where the two definitions disagree the submission returns 202 and
never seals, with no error anywhere.

**This is the direct answer to "how does the Blueprint Service track the version it is working on
and ensure inflow action data is tied to the correct blueprint?" — today it does not.**

### 6. Amendment

**Rule.** There is **one** upgrade path: republish the same `blueprintId`, producing a new
definition alongside the old. Instances keep the definition they started on.

"Amend" is a **UI affordance over that one path** — load a published definition into a draft of the
*same* blueprint id — not a second mechanism.

**Gap:** `BlueprintFromPublishedEndpoint.cs:152` sets `clone.Id = Guid.NewGuid().ToString()`, so
amending produces a *different blueprint* (FINDINGS §10). Two unrelated upgrade paths exist with no
stated relationship: republish (F194's subject) and amend-to-a-new-id (which sidesteps versioning
entirely). Amendments never appear in `GET /blueprints/{id}/versions`, and lineage survives only as
`x-source-*` metadata that nothing resolves.

**Decide explicitly:** either amend becomes same-id republish (recommended — one path, one story,
and the F142 "Amending vN" rail keeps working), or forking to a new id is retained as a *separate,
named* operation ("Duplicate as new service") that is honestly not versioning. What must not
continue is one button that looks like versioning and is not.

### 7. Recovery and durability

**Rule.** The ledger is the only durable copy. A node's published store is a cache of it, and a cold
node rebuilds **every** definition from the register.

- **Survives a restart:** everything published (rebuilt from the ledger).
- **Survives a re-genesis:** nothing — by design.
- **Never durable:** drafts. This is a deliberate, stated property, not an oversight: a draft is
  work-in-progress on one node.

Under D recovery gains for free — definitions arrive already keyed by their transaction id, so there
is no ordinal to re-derive and no ambiguity about which definition is which.

**Gap:** `GET /api/registers/{registerId}/blueprints/published` is `.AllowAnonymous()` with a
comment claiming it *"returns only metadata"* while returning full `blueprintJson` for every
blueprint on the register (FINDINGS §15). Gateway auth means this is any authenticated user of the
installation, not the internet — but there is no org or subscription check, and the comment is
flatly false. It should require a service principal.

---

## Deliverable C — the accidents worth keeping

Not everything undocumented is wrong. These are correct and should be **written down rather than
changed**:

1. **A starting action forks from a Control-typed predecessor, and the validator permits N children
   of one.** (`ValidationEngine.cs:1218-1221`.) This is what makes "many instances of one blueprint"
   representable at all, and it is the same bypass governance quorum depends on. Keep, and state it
   as a platform invariant rather than a fork-detection exemption.

2. **`WaitForTransactionConfirmationAsync` on the anchor** (`ActionExecutionService.cs:460`) is a
   real precondition — *this definition is genuinely sealed on this register* — not just a chain
   filler. It was the strongest argument against option C. Keep it, and document what it guarantees.

3. **Presentational edits do not invalidate a rehearsal pass.** F142's split of schema keywords into
   presentational and behavioural (`FormKeywordClassifier`), with unknown `x-*` treated as
   behavioural, is a good decision that was simply asked to carry more weight than it was built for.
   Keep the mechanism, narrow its job back to the rehearsal gate.

4. **Unknown `x-*` extensions are fail-safe behavioural.** Correct default; keep.

5. **The pin travels on the sender-signed `RoutingDecision` rather than being resolved per node.**
   Under F145 a value two nodes cannot both derive from sealed facts is a value they can diverge on.
   This was the right call and D does not change it — only what the value *is*.

6. **`RoutingDecision.ComputeSignableBytes` is guarded by reflection** and fails on a property type
   it cannot mutate rather than skipping it. This is the one hand-maintained projection in the area
   that is properly guarded, and it is the template for the others.

7. **Drafts being node-local and non-durable.** Reads as a gap; is a decision. Say so.

---

## What must be specified and guarded before any C# is written

Ordered by what would silently break if skipped.

1. **The canonicaliser** — one home, RFC 8785-style key-sorted, one naming policy (camelCase), with
   a serialize → parse → serialize → compare round-trip guard. D's entire identity rests on it, and
   the current code already contains three different serializer configurations for the same content.

2. **The publication rule** — one writer (`PublishService.PublishAsync`), flattened payload, one
   naming policy, and the dedupe predicate ("identical canonical bytes ⇒ identical id ⇒ idempotent
   no-op"). Written as a contract test, not prose.

3. **`PublishedBlueprint.PublicationTxId`** — the field whose absence created the whole problem.
   Recorded from the Register Service's response; never computed locally.

4. **The execution resolution contract** — pin threaded through `GetBlueprintAsync` and **both**
   caches; draft store off the execution path; instance creation initialising from what it pins.

5. **The exec-def hasher coverage fix, with a reflection guard** over
   `Blueprint`/`Action`/`Route`/`Participant` properties and an explicit presentational deny-list,
   **failing on a property it cannot classify**. Mutation to run: *add a property and omit it from
   the projection.* Every hand-written test in `ExecutableDefinitionHasherTests.cs` survives that
   mutation, which is the only mutation that matters.

6. **The ordinal's new definition** — derived on read from ledger order; delete `VersionMajor`,
   `VersionMinor`, and the stored insert-order `Version`.

7. **The amendment decision** (stage 6) — same-id republish, or a separately-named fork.

8. **Reachability** (#1558 + FINDINGS §11) — one algorithm, one severity table. Separable; ship
   independently.

9. **The recovery endpoint's authorization** (FINDINGS §15).

---

## What proves it, live

Per verification discipline: **the check must be positive.** Absence of errors is not evidence in a
lifecycle whose every defect degrades to plausible behaviour.

On a re-genesised node:

1. Publish a blueprint; start an instance; advance one action.
2. **Behaviourally** republish (a new `required` field on a later action) while that instance is in
   flight. Assert a **second publication transaction exists on the register** — the check #1563
   fails today.
3. Restart `blueprint-service`. Assert the in-flight instance still advances against the **old**
   definition, and `GET /api/instances/{id}/definition` reports `pinned` (not `unresolvable`).
4. Start a second instance; assert it pins to the **new** definition and enforces the new rule.
5. **Presentationally** republish (relabel a field). Assert: a new publication transaction exists,
   `execDefHash` is unchanged, and the F142 rehearsal pass is still valid.
6. Assert `pin_fallback` reads **zero** across the whole run.
7. Assert the same blueprint published to a **second** register yields a **different** publication
   txId.

Steps 5 and 7 are the ones that would pass vacuously if written carelessly — 5 because "unchanged"
is the default outcome of doing nothing, and 7 because two registers rarely get the same blueprint
by accident in a scripted run. Both need the counterfactual executed, not described.

---

## Issues (deliverable D)

All filed / updated 2026-08-24.

| Issue | Action |
|---|---|
| **#1563** | Decision recorded: **Option D**, with the construction, the one-owner rule, and the two-publish-path collapse as in-scope. |
| **#1558** | Severity is half the defect; the publish path's reachability *algorithm* is the other half. The `blueprint-builder` "chat is the stricter gate" correction is recorded there. |
| **#1370** | Larger blast radius: the pin now rides `TransactionMetaData`, whose projection exists twice with divergent coverage. The four-instance "hand-maintained projection" table and the reflection-guard evidence are recorded there. |
| **#1566** *(new)* | `ExecutableDefinitionHasher` omits execution-affecting fields (FINDINGS §8) — severity reduced by D, still required, reflection guard + the discriminating mutation specified. |
| **#1567** *(new)* | The execution path resolves without the pin (FINDINGS §9, §12) — the engine validates one definition and signs a decision labelled with another. |
| **#1568** *(new)* | Amend mints a new blueprint id (FINDINGS §10) — two unrelated upgrade paths; source selected by the unstable ordinal. |
| **#1569** *(new)* | `GET /registers/{id}/blueprints/published` is `AllowAnonymous`, returns full definitions, comment claims metadata only (FINDINGS §15). |
| **#1570** *(new)* | The two publish paths differ in property **casing** as well as `$ref` flattening — masked by #1563, a silent fork the moment it is fixed. |

**Sequencing.** #1570 must land *with* #1563, not after it. #1558 and #1569 are independent and
shippable now. #1566 sequences after #1563 (D reduces its severity) but can ship independently if
#1563 slips. #1567 is the highest-value fix that is not blocked by anything.
