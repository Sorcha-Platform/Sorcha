# Research — Blueprint Definition Identity (Feature 195)

**Phase 0 output.** Every decision below was checked against source on branch
`195-blueprint-definition-identity`. Where a claim from the design document or an issue turned out to
be wrong or narrower than stated, that is recorded — two claims were already retracted during the
investigation and the same discipline applies here.

Prior art, in order of authority: the Option D decision on **#1563**;
`docs/superpowers/specs/2026-08-24-blueprint-lifecycle-design.md` (the design);
`docs/superpowers/specs/2026-08-24-blueprint-lifecycle-current-state-FINDINGS.md` (evidence base with
citations); `specs/194-blueprint-version-pinning/` (the feature this corrects).

---

## R-001 — The identity construction

**Decision.**

```
publicationTxId = hex( SHA-256(
      "sorcha:blueprint-publication:v1"   0x1F
      registerId                          0x1F
      blueprintId                         0x1F
      canonicalDefinitionJson ) )
```

**Rationale.** Register-scoping: a definition published to two registers is byte-identical *by
construction* (same template, same model, same serializer), so without `registerId` one transaction
id would name two ledger facts and every `(registerId, txId)` lookup, receipt and inclusion proof
would be ambiguous. Domain tag: `InstanceIdentity.Derive`
(`Blueprint/Services/Implementation/InstanceIdentity.cs:35-53`) is already
`SHA-256(registerId 0x1F blueprintId 0x1F startingActionTxHash)` — an untagged publication id is the
**same preimage construction sharing its first two fields**, so a tag separates two kinds of identity
that are otherwise indistinguishable by shape. `0x1F` reuses the platform's existing separator
convention rather than inventing a second one.

**Alternatives considered.** Option A (a separate stable anchor transaction plus content-addressed
definition transactions) — costs a new transaction type and still needs a legacy path; Option B
(content-address the publish transaction and *derive* the anchor from the pin) — relocates a formula
that should not exist; Option C (anchor on register genesis) — deletes one formula at the cost of a
real precondition and the pure-function property. Full comparison on #1563.

---

## R-002 — Canonicalisation: only what survives a parse can vary

**Decision.** Canonical form is **parse → re-serialize with fixed options, with recursively sorted
object keys**. A new component owns it; `RegisterSerializationOptions.Canonical` is *not* it.

**Rationale.** `RegisterSerializationOptions.Canonical`
(`src/Common/Sorcha.Register.Models/RegisterSerializationOptions.cs:45-60`) sets
`WriteIndented=false`, `PropertyNamingPolicy=CamelCase`, `DefaultIgnoreCondition=WhenWritingNull`,
`Encoder=UnsafeRelaxedJsonEscaping` — but **does not sort keys**, because nothing has needed it yet.
`BlueprintContentHash.Compute` (`ServiceClients.Http/Register/BlueprintContentHash.cs:30-37`)
re-serializes a parsed `JsonDocument`, which preserves input order. So today's "canonical" hash
addresses the *serializer's output*, not the content.

The parse normalises whitespace and string escaping for free (`&` and `&` parse to the same
string), which is why the producer's encoder cannot affect the id and why recovery's provenance check
passes today. Only these survive a parse and must therefore be pinned:

| Degree of freedom | Survives a parse? | Decision |
|---|---|---|
| Property **order** | yes | **sort recursively** (RFC 8785). The one substantive gap. |
| **Number** representation (`1` / `1.0` / `1e0`) | yes — STJ writes a parsed number from its raw text | normalise, or document and pin by golden vector. Low risk: values originate from a typed model. |
| Duplicate keys | resolved at parse, last wins | **reject** rather than silently pick |
| Property **names/casing** | yes | already pinned by `[JsonPropertyName]` on the model — see R-003 |
| Null omit-vs-emit | yes | already pinned by `JsonIgnoreCondition` on the model — see R-003 |
| Whitespace, string escaping | **no** | normalised by the parse; nothing to decide |

**Correction recorded.** The design document originally listed the encoder difference between
`BlueprintContentHash` (relaxed) and the publish paths (default) as an inconsistency. **It is not** —
retracted 2026-08-24. Escaping does not survive a parse.

---

## R-003 — Property names are already pinned, and that makes them ledger contract

**Decision.** Do not add a naming policy. Add a **golden-vector test** instead.

**Rationale.** All 30 files in `src/Common/Sorcha.Blueprint.Models` were checked: on every type that
reaches the wire, the count of `{ get; set; }` properties equals the count of `[JsonPropertyName]`
attributes. The three exceptions — `SchemaLayoutInfo`, `XReviewExtension`, `BlueprintPageDefinition`
— are `SchemaLayoutParser` parse results and never serialize. **An attribute overrides
`PropertyNamingPolicy`**, so both publish paths already emit identical camelCase bytes and the
instance-creation path's `PropertyNamingPolicy = CamelCase` is a **no-op**.

**Correction recorded.** The design document's claim that the two publish paths differ in property
**casing** was **wrong** and was retracted the same day, before any code. The error was inferring the
wire shape from serializer options without checking the model for attributes. #1570 survives on its
original grounds (flattened snapshot vs unflattened draft) and was retitled.

**What this leaves is a sharper hazard.** Because names come from attributes on the model, **every
`[JsonPropertyName]` on the blueprint graph becomes part of the ledger contract** once definitions are
content-addressed: renaming one is a refactor with no compile-time consequence and no existing test
that would notice, and it changes every definition id on every register. Same for adding a
`JsonIgnoreCondition`. A golden-vector test over a fixed fixture asserting a known id is the only
guard that catches all six rows of R-002 at once.

---

## R-004 — One owner: the Register Service computes, everyone else reads

**Decision.** Only `Sorcha.Register.Service` computes a publication id.
`PublishedBlueprint` gains `PublicationTxId`, recorded from the Register Service's response.

**Rationale.** The current derivation has **four homes**:
`ActionExecutionService.ComputeBlueprintPublishTxId:2989`, consumed at
`ActionExecutionService.cs:459` and `Blueprint/Program.cs:1622`; the Register Service's inline copy
(`Register/Program.cs:2018`); and `ActionExecutionServiceTests.cs:1271`, which hand-writes the
formula a fifth time — so the guard is itself a duplicate. All four exist **only because
`PublishedBlueprint` (`Blueprint/Program.cs:4114-4140`) never recorded the transaction id it was
published as.** Nothing needs to recompute it: recovery reads real transaction ids, instance creation
reads the store, the starting action reads the instance's pin.

This is stronger than CLAUDE.md §15/§16's "one shared leaf" pattern — there is one *producer*, not
one shared formula. The publish push is already synchronous
(`Blueprint/Program.cs:3269-3271` awaits `PublishBlueprintToRegisterAsync`), so the id is available
before `PublishAsync` returns.

**Alternative rejected.** A shared leaf both services call (Option B's shape). It keeps a formula
alive that has no reason to exist, and keeps the test's fifth copy honest only by convention.

---

## R-005 — The pin becomes self-resolving from the ledger

**Decision.** Add a **register fallback arm** to the validator's pinned resolution: cache → Blueprint
Service by-id → **read transaction `{pin}` from the register**. Only then refuse.

**Rationale.** `ValidationEngine.ResolvePinnedBlueprintAsync:2482-2516` today tries the content-keyed
cache, then `IBlueprintFetcher.FetchBlueprintByHashAsync`, then **refuses** — with an explicit comment
that there is deliberately no fallback to latest. That refusal is correct and stays. But under Option
D the pin *is* a transaction id on a register every validating node holds, so a third arm is available
that was not before, and it is the one that closes #1563's downstream symptom: `pinState=unresolvable`
becomes unreachable for any definition the register actually holds.

The existing structure absorbs this well — `ResolveBlueprintAsync(blueprintId, pin, ct)` already
branches pinned/unpinned, and the unpinned arm (system blueprints, which have no instance and
therefore no pin) is unaffected. The three call sites (`:572`, `:992`, `:1399`) need no signature
change beyond the value's meaning.

**Note.** `IBlueprintFetcher.FetchBlueprintByHashAsync` and its endpoint keep their shape; only what
the parameter denotes changes. Renaming it to `...ByPublicationAsync` is cosmetic and should ride the
same change to avoid a name that lies.

---

## R-006 — Resolve the pin at submit (#1567)

**Decision.** `IActionResolverService.GetBlueprintAsync(blueprintId, pin, ct)` — pin required, not
optional. Both caches carry it. The draft store leaves the execution path.

**Rationale.** `IActionResolverService.GetBlueprintAsync(string, CancellationToken)`
(`Services/Interfaces/IActionResolverService.cs:19`) takes no pin;
`ActionResolverService.cs:45-104` resolves **draft store first**, then
`PublishedBlueprintSelector.SelectLatest`, and caches under a bare `blueprint:{blueprintId}` key
(`:54`, 10-minute TTL) plus a **static** `_actionIndexCache` keyed by the same bare id (`:30`).
`ActionExecutionService.ExecuteAsync` resolves through it at `:238` and stamps the instance's pin at
`:1293`/`:1771` via `ResolveInstancePin:1804`.

Making the pin optional would preserve the defect for every caller that omits it, which is how it
survived F194 (research R-007 listed this call site; it did not land). Required, or not at all.

**The static cache is the subtle half.** A process-wide dictionary keyed by bare id serves the wrong
definition to a *different instance* than the one that populated it — so it must carry the pin or be
removed.

---

## R-007 — `RoutingDecision` field rename and the signing guard

**Decision.** `RoutingDecision.BlueprintExecDefHash` → a definition **publication txId**
(`blueprintDefinitionTxId`). The rename must be mirrored in `ComputeSignableBytes`.

**Rationale.** The field is inside `ComputeSignableBytes`, which is a **hand-written field-by-field
rebuild**: a property present on the record but absent from the rebuild rides the wire
unauthenticated while appearing signed. `RoutingDecisionSigningCoverageTests` is reflection-driven and
fails on a property type it cannot mutate, so it catches the omission — this is the one projection in
this area that is properly guarded, and F194 demonstrated it (the discriminating mutation failed
exactly one test while 391 stayed green, *including* the two hand-written per-field tests).

Old and new producers compute different canonical bytes and would refuse each other. The authorised
wipe removes that as a *deployment* concern but **not as a development-sequence one** — see R-012.

---

## R-008 — Recovery keys on the publication, and provenance becomes self-anchoring

**Decision.** `BlueprintRecoveryService` dedupes by **publication txId**, and verifies each definition
by recomputing its id from the received bytes and comparing to the transaction's own id.

**Rationale.** Recovery currently recomputes `_execDefHasher.ComputeHash(blueprint)` and dedupes on it
(`BlueprintRecoveryService.cs:~393-400`), and verifies provenance against the separately-sealed
`contentHash` via `TryVerifyProvenance:310-330`. Under Option D both collapse into one check: the id
**is** the digest, so a tampered payload cannot match its own transaction id and no second sealed
field is needed. `contentHash` is absorbed (R-010).

Recovery reads by transaction **type** (`Register/Program.cs:2136-2144`), not by traversing any
chain, so nothing about the anchor change affects it.

---

## R-009 — Hasher coverage, and what the behavioural signature is *for* (#1566)

**Decision.** Keep `ExecutableDefinitionHasher` (328 lines,
`src/Core/Sorcha.Blueprint.Engine/Implementation/ExecutableDefinitionHasher.cs`) and narrow its job to
the **F142 rehearsal gate**. Fix its coverage; guard it by reflection with an explicit presentational
deny-list, failing on a property it cannot classify.

**Rationale.** A probe asserting "behavioural edit ⇒ new hash" failed **9 of 9**
(FINDINGS §8). Omitted and execution-affecting: `Action.RejectionConfig` (read as a structural
successor at `ValidationEngine.cs:1035` and `:1582`), `Action.Participants` (legacy routing, live at
`RoutingEngine.cs:246` — such a blueprint has *zero* routing coverage in its signature),
`Action.RequiredActionData` (`ActionExecutionService.cs:1581`), `Route.BranchDeadline`,
`Route.DecisionNotice`, `Blueprint.PresentationConfig` (`PresentationLifecycleService:992`),
`Blueprint.InstanceReference`.

**Under Option D this is no longer a correctness defect** — the pin addresses the whole definition, so
a behavioural edit already produces a new identity. What remains is the *rehearsal* direction: a
behavioural edit that keeps a stale rehearsal pass valid. That is F142's original tolerance and the
severity drops accordingly, but the fix is cheap and the guard is the valuable part.

`FormKeywordClassifier` lives at `src/Common/Sorcha.Blueprint.Models/Forms/FormKeywordClassifier.cs`
and already treats unknown `x-*` as behavioural (fail-safe). That default is kept and the same
principle extends to model properties: **unclassified fails the build**, rather than defaulting either
way.

**Checked and withdrawn.** The probe also failed for `Action.AdditionalRecipients`, which was nearly
written up as a disclosure defect. It is **inert** — the only readers are
`McpServer/Tools/Designer/BlueprintGetTool.cs` (display) and a doc comment. Omitting it is correct.

---

## R-010 — Version concepts: seven to four

**Decision.**

| Concept | Job | Owner |
|---|---|---|
| `blueprintId` | the **thing** | author |
| **publication txId** (absorbs `contentHash`) | the **definition** — pinned, anchored, resolved | the ledger, via Register Service |
| `execDefHash` | *did behaviour change?* — rehearsal-gate key only | `ExecutableDefinitionHasher` |
| ordinal `v1`/`v2` | display label, **derived on read** from ledger order | derived |

Deleted: `Blueprint.Version` as an input, `Blueprint.VersionMajor`, `Blueprint.VersionMinor` (both
wholly dead — written by the amend clone and the designer properties panel, read by nothing),
`PublishedBlueprint.Version` as a stored insert-order value, and the SSR-catalogue `version` echoed in
the publish response (`Register/Program.cs:2007`, always `1` for a tenant blueprint and unrelated).

**Rationale.** F194 demoted the ordinal to a display label but nothing enforced it — the amend loop
still resolves by it. Ledger order is stable because docket order is; `versions.Count + 1` in memory
is not.

**The two hashes must not merge.** The publication id answers *which bytes*; `execDefHash` answers
*did behaviour change*. A presentational republish writes a **new publication** (so relabels ship)
while leaving `execDefHash` unchanged (so no re-rehearsal). Conflating them is what let F194 widen a
value's meaning without widening its coverage.

---

## R-011 — Amend becomes same-id republish (#1568)

**Decision.** Amending produces a **new version of the same blueprint**. The source is selected by
publication txId.

**Rationale.** `BlueprintFromPublishedEndpoint.cs:152` sets `clone.Id = Guid.NewGuid().ToString()`, so
an amendment is a different blueprint: it never appears in `GET /blueprints/{id}/versions`, and its
source is selected by ordinal at `:116` — a value re-derived from insert order on every recovery, so
amending "v2" before and after a restart may clone different definitions. Two unrelated upgrade paths
with no stated relationship is the defect; one path is the fix.

The F142 lineage metadata (`x-source-register` / `x-source-blueprint-id` / `x-source-version`,
`:163-166`) drives the designer rail's "Amending vN" display. It is retained, with the version key
carrying the publication id.

**Alternative considered.** Keep forking, but rename it "Duplicate as new service" so it is honestly
not versioning. Rejected as the default because it leaves the platform with two upgrade stories; worth
reconsidering only if a user need for forking emerges.

---

## R-012 — No migration, but the development sequence still matters

**Decision.** No backward-compatibility path. Register wipe at rollout. **Deploy order within the
development sequence is still load-bearing.**

**Rationale.** Pre-release and the maintainer has authorised recreating registers, so every legacy-id
fallback that Options A and B required is dropped — which is a large part of why D is cheaper. But the
`RoutingDecision` rename (R-007) means old and new producers compute different canonical bytes and
refuse each other, exactly as F194 found: **new validator + old producer is safe** (a null field is
omitted, canonical bytes unchanged); **old validator + new producer refuses every submission**. The
wipe removes the *installed-base* problem, not the *ordering* one during development and testing.

⚠ Deploy scope includes **`register-service`** — it persists and serves `TransactionMetaData`. F194's
design named only blueprint + validator and was wrong; an out-of-date register-service silently
dropped the new field from the typed metadata while it survived in the tracking JSON.

---

## R-013 — Collapse the two publish paths (#1570), and why it cannot be deferred

**Decision.** One writer (`PublishService.PublishAsync`), `$ref`-flattened payload. Instance creation
does not publish.

**Rationale.** `PublishService` pushes a deep-copied, flattened snapshot
(`Blueprint/Program.cs:3203-3205`, pushed `:3269-3271`); the instance-creation owner branch pushes the
**unflattened draft** (`:2306-2318`). Today the version-blind txId dedupes the second away, so the
divergence is invisible. **Content-addressing unmasks it**: two shapes of one blueprint would both
land under different ids and recovery would restore both as distinct definitions — a silent *fork*
replacing a silent *drop*, which is worse because a forked definition looks healthy.

Flattening is not an optimisation: the ledger copy is what a recovering or validating node
**executes**, and an unflattened one needs the core-schema catalogue to resolve its `$ref`s — so a
node with the catalogue and a node without would execute different definitions from the same bytes.

---

## R-014 — The anchor precondition is retained

**Decision.** Keep the confirmation wait before a starting action proceeds.

**Rationale.** `WaitForTransactionConfirmationAsync` on the anchor (`ActionExecutionService.cs:460`)
is a genuine precondition — *this definition is sealed on this register* — not a chain filler. It was
the strongest argument against Option C. Under D it reads the instance's pin instead of computing an
id, and asserts the same thing more precisely: the exact definition, not merely the blueprint.

Also retained and worth stating as a platform invariant rather than a fork-detection exemption: a
Control-typed predecessor may have **N children** (`ValidationEngine.cs:1218-1221`,
`{Control, BlueprintPublish, null}`). This is what makes "many instances of one blueprint"
representable at all, and it is the same bypass register governance quorum depends on.

---

## R-015 — Evidence strategy

**Decision.** Every guard added in this feature is **mutation-tested**, and acceptance is a live run
with **positive** checks.

**Rationale.** Project standing rule, earned repeatedly: a guard written after the feature is green
never ran red naturally and proves nothing until a discriminating mutation kills it. And every defect
in this area degrades to plausible behaviour, so absence of errors is not evidence — F194's acceptance
was `pin_fallback` reading **zero**, not the absence of exceptions.

Mutations that must each kill exactly their own named test:

| Mutation | Must fail |
|---|---|
| Remove key-sorting from the canonicaliser | golden-vector test |
| Rename one `[JsonPropertyName]` on the blueprint graph | golden-vector test |
| Drop `registerId` from the identity preimage | two-register distinctness test |
| Drop the domain tag | preimage-separation test |
| Recompute the publication id anywhere but the Register Service | architecture/grep gate |
| Make the pin parameter optional on `GetBlueprintAsync` | submit-path resolution test |
| Drop the pin from either execution cache key | cross-instance isolation test |
| Add a `Blueprint`/`Action`/`Route` property and omit it from the hasher projection | the new reflection guard — **and no hand-written test in `ExecutableDefinitionHasherTests.cs`**, which is the whole point |
| Restore ordinal-based resolution in the amend loop | amend-source stability test |
| Let the validator fall back to latest on an unresolvable pin | pinned-refusal test |

⚠ Two of the live checks pass **vacuously** if written carelessly: the presentational-republish check
("unchanged" is the default outcome of doing nothing) and the two-register check (two registers rarely
get the same blueprint by accident in a scripted run). Both need the counterfactual executed, not
described. F194's own acceptance run produced exactly one vacuous pass this way.

---

## Open questions carried into the plan

None blocking. Two to settle during implementation, both narrow:

1. **Number normalisation** (R-002) — normalise in the canonicaliser, or pin by golden vector and
   document? Low risk either way because values originate from a typed model. Decide when writing the
   canonicaliser.
2. **`IBlueprintFetcher.FetchBlueprintByHashAsync` naming** (R-005) — rename to match what the
   parameter now denotes, or leave. Cosmetic; a name that lies is a small cost with a long life.
