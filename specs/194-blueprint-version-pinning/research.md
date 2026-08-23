# Phase 0 Research: Blueprint Version Pinning (Feature 194)

**Branch**: `194-blueprint-version-pinning` | **Date**: 2026-08-23 | **Spec**: [spec.md](./spec.md)

**Design contract**: `docs/superpowers/specs/2026-08-23-blueprint-version-pinning-design.md`

Every claim below was checked against the source on this branch on 2026-08-23. Where a finding
contradicts or extends the design, it is marked **NEW** and the file is cited so the next reader can
re-check rather than re-derive.

---

## R-001 — The pin is the executable-definition hash, not the ordinal

**Decision**: Pin on `ExecutableDefinitionHasher.ComputeHash(blueprint)`. The ordinal `Version`
stays a display label.

**Rationale**: Settled as D4 in the design and restated as FR-002. Confirmed in source: the ordinal
is assigned by `InMemoryPublishedBlueprintStore.AddAsync` as `versions.Count + 1`
(`Program.cs:2806`) — derived from in-memory insert order, and therefore re-derived from scratch on
every recovery. It is only stable if recovery restores every version in a deterministic order
forever, which is a much stronger property than we want to depend on. The hash is content-addressed
and stable by construction.

**Alternatives considered**: pinning on the publish transaction id (stable and already on the
ledger, but a presentational-only republish would produce a new one and strand instances for no
reason); pinning on the ordinal (rejected above).

---

## R-002 — The pin travels on `RoutingDecision`, inside `ComputeSignableBytes`

**Decision**: Add `blueprintExecDefHash` to `RoutingDecision`
(`src/Common/Sorcha.Register.Models/Transactions/RoutingDecision.cs`) and copy it into
`ComputeSignableBytes()`.

**Rationale**: Under F145 an instance is a deterministic projection of the sealed ledger, so the pin
must be a sealed fact rather than a per-node lookup. `RoutingDecision` already rides every
forward-routing action transaction in the clear, is sender-signed, and is verified by the validator
(`VAL_ROUTING_002`). This is exactly the route F184 took for `routeId` / `reasonCode`, so it costs no
new validator code and no new plumbing.

**The trap, confirmed present**: `ComputeSignableBytes()` is a hand-written field-by-field rebuild
(`RoutingDecision.cs:79-90`) listing four properties. Its own XML doc already warns that an omitted
field "rides the wire unauthenticated while appearing signed". F189 lost `ValidatorEntry` to this
exact shape.

> **Correction to an earlier draft of this note, which said there was no test guarding it.** There
> are two: `RoutingDecisionTests.ComputeSignableBytes_IncludesRouteId_…` and
> `…_IncludesReasonCode_…`. They are correct and they stay. But they are a **hand-written list**, and
> a hand-written list rots in the same direction as the bug — the developer who forgets the rebuild
> is the developer who forgets to add the matching test. **Every one of them stays green when a new,
> uncovered property is added**, which is the only case that matters here. So the gap is real; it is
> narrower and more specific than "unguarded".

**The discriminating mutation** is therefore *not* "remove an existing field" — the hand-written
tests catch that too. It is **"add a new property and omit it from the rebuild"**: the reflection
guard must fail while every hand-written test stays green. That is the mutation that proves this
guard earns its place, and it is run as part of T011.

**Consequence for the plan**: the reflection-driven guard is not optional and is not merely for the
new field. It is the first thing to write, it protects four existing fields as well as the new one,
and it must be mutation-tested by removing an existing field from the rebuild and watching it fail.

---

## R-003 — **NEW**: the hasher currently includes the ordinal version, which contradicts FR-002

**Finding**: `ExecutableDefinitionHasher.BuildExecutableDefinition` puts
`["version"] = blueprint.Version` into the hashed projection
(`src/Core/Sorcha.Blueprint.Engine/Implementation/ExecutableDefinitionHasher.cs`).

**Why it matters**: FR-002 says the pin is identified by the content of the definition and not by
its ordinal. As written, the content address contains the ordinal. `Blueprint.Version` is a plain
settable `int` defaulting to 1 (`Sorcha.Blueprint.Models/Blueprint.cs:61`), so an author who edits it
changes the pin with no behavioural change — stranding every in-flight instance on the previous
definition for a cosmetic reason, which is precisely what FR-014 and Story 4 exist to prevent.

**Why it has not bitten yet**: republishing does **not** bump it. `AddAsync` sets
`PublishedBlueprint.Version`, a different property on a different type; nothing in
`Sorcha.Blueprint.Service` writes `BlueprintModel.Version` except `BlueprintFromPublishedEndpoint`
setting a fresh amend-clone to 1. So the hazard is latent, not live.

**Decision**: remove `version` from the hashed projection.

**Cost, stated plainly**: this changes the hash of every blueprint, which invalidates every recorded
F142 `RehearsalPass` (they join on `(BlueprintId, ExecDefHash)`). Pre-release that is cheap — a
rehearsal is re-earnable by re-running one — but it means every blueprint in flight through the
designer must be re-rehearsed before its next go-live, and the live acceptance test must not be
confused by a `409 REHEARSAL_REQUIRED` on the first publish after deploy.

**Why it strengthens rather than weakens F142**: the F142 design states that a presentational
republish should not invalidate a rehearsal pass. Removing the ordinal makes that true in one more
case; it removes no discrimination, because two definitions that differ only in ordinal are
behaviourally identical by construction.

**Alternative rejected**: leave it in and accept the latent hazard. Rejected because the whole point
of D4 is that the ordinal must not be load-bearing, and leaving it inside the content address means
it still is.

---

## R-004 — **NEW**: the "immutable snapshot" is a live reference, so a pinned definition can change under the pin

**Finding**: `PublishService.PublishAsync` (`Program.cs:3064+`) does two things that break
content-addressing:

1. It calls `FlattenActionSchemas(blueprint)`, which **mutates the blueprint in place**. Its own
   comment claims "the draft store is unchanged because the caller does not write the mutated
   blueprint back" — which is false for `InMemoryBlueprintStore`, whose `GetAsync` returns the stored
   object itself rather than a copy.
2. It then stores `Blueprint = blueprint` on the `PublishedBlueprint` — the same live reference — and
   comments it as an "immutable snapshot". It also mutates it further afterwards
   (`blueprint.Metadata["hasCycles"] = "true"`).

**Why it matters for this feature specifically**: a pin is a promise that a given identifier always
denotes the same bytes. If a published definition can be mutated in place after its hash was
computed, that promise is void and the failure is silent — the instance resolves the pinned entry
and gets different content than was hashed.

**Decision**: at publish time, deep-copy the blueprint into the `PublishedBlueprint`, and compute the
hash **over the copy, after `$ref` flattening and after the `hasCycles` metadata write** — i.e. over
exactly the bytes that will be stored, cached and pushed to the register.

**Ordering is load-bearing** and is the whole content of this decision: flattening rewrites action
schemas, so a hash taken before it addresses a definition that is never stored anywhere.

**Alternative rejected**: rely on callers not mutating. Rejected — three call paths already do, and
"nobody mutates it" is exactly the kind of invariant nothing enforces.

---

## R-005 — **NEW**: the validator cache key has two homes, and only one of them is the cache

**Finding**: the key format `sorcha:validator:blueprint:{blueprintId}` exists twice:

| Where | How |
|---|---|
| `Sorcha.Validator.Service/Services/BlueprintCache.cs` | `GetBlueprintKey(blueprintId)` from a configured `KeyPrefix` |
| `Sorcha.Blueprint.Service/Program.cs` (in `PublishAsync`) | a hardcoded interpolated string literal, written straight to Redis, bypassing the cache type entirely |

Two projects, one wire format, one of them a literal — the shape CLAUDE.md §15 and §16 exist to
prevent. Re-keying by hash in the cache and missing the literal produces a validator that looks up
`…:{id}:{hash}` while publish writes `…:{id}`: every validation takes the cache-miss path, falls
through to the fetcher, and gets the **latest** definition. Green build, green tests, defect intact.

**Decision**: hoist the key format into a single shared helper both sides call, and re-key through
it. Treat it as a cross-boundary constant, not a string.

**Correction, made during implementation: re-keying the WHOLE cache by hash was the wrong design.**
The first attempt replaced every id-keyed method with a hash-keyed one, and it broke 40 validator
tests. That was a design signal, not churn: **system blueprints have no instance and therefore no
pin** — `register-governance-v1` and its siblings are resolved by id from the system register, and so
are transactions sealed before this feature. An id-keyed tier is genuinely required.

The shipped shape is **two key shapes with different meanings**, which cannot collide because one
carries a hash segment and the other does not:

| Key | Means | Written by |
|---|---|---|
| `…:{id}` | the current/system definition, resolved by id | the validator, when it resolves an unpinned or system blueprint |
| `…:{id}:{hash}` | this exact definition | the Blueprint Service publish path, and the validator on a pinned fetch |

`GetDefinitionAsync` / `SetDefinitionAsync` are named so that nobody reaches for the id-keyed pair by
accident. The drift risk this decision exists to remove is unaffected: the format still has exactly
one home, and the publish path composes through it.

---

## R-006 — **NEW**: delete the dead per-version resolver rather than repurpose it

**Finding**: `IBlueprintVersionResolver` / `BlueprintVersionResolver`
(`Sorcha.Validator.Service/Services/`) is a complete per-version blueprint resolver — chain-walking
`ResolveForActionAsync`, `GetByPublicationTransactionAsync`, `GetVersionHistoryAsync`,
`GetLatestVersionAsync`, `GetVersionAsOfAsync`, two caches, a registered DI singleton and a test
file. **No production code calls any of its resolution methods.** Its sole production caller is
`ControlDocketProcessor.cs:271`, which calls `InvalidateCache(registerId)`.

**And it would be wrong even if wired**: `GetByPublicationTransactionAsync` resolves the version, then
fetches the definition with `_blueprintCache.GetBlueprintAsync(blueprintId)` — the id-keyed cache. It
returns the version's *number* attached to the *latest* definition.

**Correction, made during implementation (2026-08-23): it had ZERO callers, not one.**
`ControlDocketProcessor` injects `IControlBlueprintVersionResolver` — the unrelated CONTROL
resolver, which tracks governance configuration versions and is live. The earlier reading here came
from a grep matching the field name `_versionResolver`, not the declared type. Deleting the dead
resolver was therefore pure removal: no call site to migrate, and the follow-up issue this section
proposed (below) does not exist, because nothing was lost.

**Decision**: delete the interface, the implementation, the DI registration, the test file and the
`ControlDocketProcessor` call.

**Rationale**: it embodies the ordinal / publication-chain model that D4 explicitly rejected, so it
is not a foundation to build on. Leaving a dormant near-miss beside the real mechanism is how the
next person resolves the wrong one — the platform has been bitten by helper-with-no-callers before.

**Superseded by the correction above.** This paragraph originally proposed filing a follow-up
about `ControlDocketProcessor` no longer invalidating the blueprint cache on a configuration change.
It was based on the misreading: that call is to the CONTROL resolver, it still happens, and nothing
about it changed. No follow-up is owed.

---

## R-007 — **NEW**: three resolve-by-id call sites in the validator, and one live resolver in the workflow service

**Finding**: the design cites `ValidationEngine.cs:2380` (the declaration).
`ResolveBlueprintAsync(transaction.BlueprintId!, ct)` is called at **lines 570, 972 and 1369**. All
three need the pin.

On the workflow-service side the single live resolver is
`ActionResolverService.GetBlueprintAsync(blueprintId)`
(`Services/Implementation/ActionResolverService.cs:75-79`): draft store first, then
`PublishedBlueprintSelector.SelectLatest(published)`. It is what the projector, the reaction
dispatcher and the action endpoints all resolve through.

**Worth noting**: it prefers the *draft* over any published version. For a node that holds an
editable draft, that is worse than "latest published" — it resolves whatever the author is currently
editing. Pinning fixes this on the execution path as a side effect.

**In scope** (execution path — must resolve the pin): the three `ValidationEngine` sites;
`ActionResolverService.GetBlueprintAsync`; instance creation at `Program.cs:2224-2229`; the
F186 citizen application detail at `Endpoints/MeApplicationEndpoints.cs:201`, which resolves a
blueprint to render a decision-notice message and must render it from the pinned definition's
catalogue.

**Out of scope** (design-time and administrative — correctly latest): the MCP designer tools,
the admin UI blueprint services, `ChatOrchestrationService`, `PublishGate`,
`RehearsalOrchestrationService`, and the version-list endpoints.

---

## R-008 — **NEW**: five hardcoded instance-version writes, not two

**Finding**: beyond the two the design names (`InstanceProjector.cs:170`,
`InstanceRebuildService.cs:104`) there are three more literal `BlueprintVersion = 1` writes:
`PresentationLifecycleService.cs:560`, `PresentationLifecycleService.cs:879`, and
`RehearsalOrchestrationService.cs:760`.

**Decision**: sweep all five. Two of the three extra sites construct throwaway shim instances
(one is literally tenanted `"abandonment-shim"`) and may legitimately keep a placeholder, but that
must be a decision taken per site and written down, not an omission.

**Rationale**: a defect that turns out to be a class should be swept at the moment it is understood
— that is the cheapest time to find the rest, and the platform has shipped a sibling defect by
fixing only the instance in front of it.

---

## R-009 — The pre-feature fallback is required, not optional

**Finding**: the design's rollout section is internally inconsistent — it says `Instance` gains a
column so a node "needs `docker compose down -v` and re-genesis", and also that deploy scope is two
per-service recreates with "no genesis window". Only the second is what will actually happen.

**What follows**: recreating the workflow service's Postgres database satisfies the new column and
destroys every materialised instance row. It does **not** touch the register, which lives in Mongo.
So every sealed submission predating this feature survives, carries no pin, and will be re-folded.
The fallback is therefore load-bearing on day one, not a defensive nicety.

**Decision**: fall back to "latest published definition", take the **identical** fallback in
`InstanceProjector` and `InstanceRebuildService` (F145 rebuild parity is an existing guarantee and
must not regress), log at Warning, and count it on a metric. It must never apply to a transaction
that does carry a pin.

**Removal trigger** (a condition, not a date): the fallback can be deleted once no un-pinned
submission remains foldable on any register — in practice, after the next full re-genesis. Recorded
so it is a scheduled removal rather than a permanent silent path back to the old behaviour.

---

## R-010 — Refusal codes stay validator-local for now

**Decision**: the new refusal for an unresolvable pin (`VAL_BP_VERSION_001`) is declared and consumed
inside the Validator Service, alongside its ~70 siblings.

**Rationale**: CLAUDE.md §16's trigger for promoting a code into the shared
`Sorcha.Blueprint.Models` leaf is that **a second project needs to name it** — not family
membership. Nothing outside the validator matches on this code. `VAL_BP_CRED_004` is the standing
precedent for a code that deliberately stays local while its siblings moved.

**Re-evaluate if**: the workflow service ever branches on it (for example to present "this
application is pinned to a definition this node cannot resolve" to an operator). At that moment it
must be promoted, because a renamed literal then breaks a comparison with no compile error.

---

## R-011 — Schema change is folded into `InitialCreate`

**Decision**: `Instance.BlueprintExecDefHash` is added to the entity, the existing
`20260528205017_InitialCreate` migration, its `.Designer.cs` and `BlueprintDbContextModelSnapshot.cs`
together. No new migration.

**Rationale**: CLAUDE.md §19 — pre-release, the platform carries exactly one migration per service
and out-of-date databases are recreated rather than migrated.

**The trap to respect**: amending an applied migration is invisible to a database that already
recorded its id. `MigrateAsync` sees `InitialCreate` present and does nothing; the column never
appears and the failure surfaces far away as a raw Postgres `42703: column … does not exist` on the
first query that touches it, after a green build and a green suite. The blueprint database must be
recreated on every target, and `dotnet ef migrations script --idempotent` should name exactly one
migration whose `CREATE TABLE` contains the new column.

**Also**: `EfCoreInstanceStore.UpdateAsync` copies model → entity **by hand** (`:122` region). A
field missing from that list is written in memory, reported saved, and lost. The new field must be
added there, and `EfCoreInstanceStoreUpdateRoundTripTests` is the existing guard that proves it.

---

## Open items carried into planning

None blocking. Two things to watch during implementation:

- **The first publish after deploy will demand a fresh rehearsal** (R-003). Expected, not a defect.
- **Cache re-keying is the highest-risk change** (R-005) because getting it half-right fails to the
  *old behaviour* rather than to an error. The mutation test for it is "re-key the reader and not the
  writer, and watch the pinned-definition test fail" — if it still passes, the test is not reading
  through the cache.
