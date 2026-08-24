# Blueprint lifecycle — what is true today

**Date:** 2026-08-24
**Companion to:** `2026-08-24-blueprint-lifecycle-investigation-KICKOFF.md`
**Status:** deliverable A, first half ("what is true today"). The "what should be true" half and the
#1563 decision are NOT in this document — they were the next step when the session was interrupted.

Every claim below was checked against source on branch `docs/blueprint-lifecycle-kickoff` on
2026-08-24. Findings 1–7 are the kickoff's, spot-checked. Findings 8–15 are new. Where a claim was
narrowed or withdrawn during checking, that is recorded — the point is that the check happened.

---

## The unifying shape

**Every defect in this lifecycle is a hand-maintained projection of a model.** The same trap, four
times, in one feature area:

| Projection | Hand-written | Guarded by |
|---|---|---|
| `ExecutableDefinitionHasher.BuildExecutableDefinition` | ✅ field-by-field | a hand-written test list — **gap proven, finding 8** |
| `RoutingDecision.ComputeSignableBytes` | ✅ field-by-field | reflection (F194 T011) — **the one that is guarded** |
| docket→register `TransactionMetaData` mapper | ✅ ×2, divergent | nothing — **#1370** |
| `EfCoreInstanceStore.UpdateAsync` model→entity copy | ✅ field-by-field | `EfCoreInstanceStoreUpdateRoundTripTests` |

Three of the four are unguarded or under-guarded, and all four fail **silently and in the permissive
direction** — the value is written, reported saved, and lost.

---

## 1–7 — the kickoff's findings, spot-checked

All seven hold. Two narrowings:

- **#6 (`IBlueprintVersionResolver` dead)** — confirmed deleted by F194. The remaining dead
  machinery is `InstanceIdentity.Derive` (`Services/Implementation/InstanceIdentity.cs`), which has
  **zero production callers** — only `tests/Sorcha.Blueprint.Service.Tests/Services/InstanceIdentityTests.cs`.
- **#4 (two publish paths)** — confirmed, and see finding 13: #1563 is currently *masking* it.

---

## 8. The pin does not cover the executable definition — **proven by execution**

`ExecutableDefinitionHasher.BuildExecutableDefinition`
(`src/Core/Sorcha.Blueprint.Engine/Implementation/ExecutableDefinitionHasher.cs:78-96`) is a
hand-written projection. Its `BuildActions` (`:126`) and `BuildRoutes` (`:170`) list the fields that
participate in the hash.

A throwaway probe asserted that behaviour-changing edits change the hash. **9 of 9 cases failed** —
i.e. all of these produce an *identical* pin. (Probe deleted; results recorded here.)

| Omitted from the pin | Why it is execution-affecting |
|---|---|
| `Action.RejectionConfig` (whole object) | The validator reads `RejectionConfig.TargetActionId` as a structural successor in `VAL_ROUTING_001` (`ValidationEngine.cs:1035`) and in `VAL_BP_003` reachability (`:1582`). `IsTerminal` decides whether rejection ends the workflow. |
| `Action.Participants` (legacy `Condition` routing) | Live routing — `RoutingEngine.cs:246`. A blueprint routed this way has **zero routing coverage in its pin**. |
| `Action.RequiredActionData` | Live validation fallback when an action declares no `dataSchemas` — `ActionExecutionService.cs:1581`. |
| `Route.BranchDeadline` | Parallel-branch deadline. |
| `Route.DecisionNotice` | F184/F186 — the citizen-facing outcome catalogue, and F186 resolves it *from the pinned definition*. |
| `Blueprint.PresentationConfig` | `PresentationLifecycleService.ResolveConfig` (`:992`) — validity window, abandonment recording, outcome detail level. |
| `Blueprint.InstanceReference` | Generates the instance's public metadata. |

Also omitted, lower value: `Action.Notification`, `Action.Target`, `Action.Condition`,
`Blueprint.Metadata` (including `hasCycles`), `Blueprint.DataSchemas`.

> **Withdrawn during checking:** the probe also failed for `Action.AdditionalRecipients`, and I
> initially wrote it up as a disclosure-scope defect. It is **inert** — the only readers are
> `McpServer/Tools/Designer/BlueprintGetTool.cs` (display) and a doc comment. Omitting it from the
> hash is correct. Recorded because the check is the point.

### The compounding half

`InMemoryPublishedBlueprintStore.GetByExecDefHashAsync` (`Program.cs:2904`) resolves a hash
collision with `OrderByDescending(v => v.Version).First()`, justified by this comment:

> *"Several publications can share one hash (a presentational-only republish produces the same
> executable definition). They are the same definition by construction…"*

**The premise is false.** Two publications sharing a hash can differ in every field in the table
above. So for exactly those fields, a pinned instance is silently handed the **newest** publication —
which is the defect F194 exists to remove, reappearing inside F194's own resolution path.

### Why F142 could tolerate this and F194 cannot

Under F142 the hash keyed a *soft* gate (a rehearsal pass). An over-valid rehearsal is a nuisance.
Under F194 the same hash means "this is the definition the instance runs" and is enforced by the
validator. The value's meaning was widened without widening its coverage.

**Guard shape when fixed:** reflection-driven over `Blueprint`/`Action`/`Route`/`Participant`
properties with an explicit presentational deny-list, failing on a property it cannot classify —
the `RoutingDecisionSigningCoverageTests` shape, which is the one projection in this area that is
actually guarded. Mutation to run: **add a property and omit it from the projection**; the hand-written
tests in `ExecutableDefinitionHasherTests.cs` all stay green for that case, which is the only case
that matters.

---

## 9. The pin is enforced at seal but not at submit

`IActionResolverService.GetBlueprintAsync(string blueprintId, CancellationToken)`
(`Services/Interfaces/IActionResolverService.cs:19`) **takes no pin.** Its implementation
(`Services/Implementation/ActionResolverService.cs:45-104`) resolves **draft store first**, then
`PublishedBlueprintSelector.SelectLatest`, and caches under a bare `blueprint:{blueprintId}` key
(`:54`, 10-minute TTL) plus a static `_actionIndexCache` keyed by the same bare id (`:30`).

`ActionExecutionService.ExecuteAsync` resolves through it at `:238`, then stamps the instance's pin
onto the routing decision at `:1293` / `:1771` via `ResolveInstancePin` (`:1804`).

So the engine **validates the payload, evaluates calculations and computes the route against one
definition, then signs a decision labelled with a different definition's hash.** The validator then
checks `VAL_ROUTING_001` against the pinned definition. Where the two disagree the submission returns
202 and never seals.

F194 research R-007 listed this call site as in scope ("execution path — must resolve the pin"). It
did not land. The distributed-cache key and the static action-index cache would both need the pin
segment too — `Sorcha.Blueprint.Models.BlueprintCacheKey` already exists for exactly this and is used
by the publish path and the validator's `BlueprintCache`, but not here.

---

## 10. The amend loop mints a new blueprint id — amending is not versioning

`BlueprintFromPublishedEndpoint.cs:152` sets `clone.Id = Guid.NewGuid().ToString()`. So the F142
"Amend" produces a **different blueprint**, not a new version of the existing one:

- `GET /blueprints/{id}/versions` never shows an amendment.
- Instances of the source are unaffected — but by disjointness, not by pinning.
- Lineage exists only as `x-source-register` / `x-source-blueprint-id` / `x-source-version` in
  `Blueprint.Metadata` (`:163-166`), which nothing resolves — the designer rail reads it for display.

There are therefore **two unrelated upgrade paths with no stated relationship**: republish the same
id (F194's subject, blocked by #1563) and amend to a new id (works, sidesteps versioning entirely).

The amend source is resolved by `publishedStore.GetVersionAsync(body.BlueprintId, body.Version)`
(`:116`) — **by ordinal**, the identifier F194 established is assigned from insert order and
re-derived on recovery. Amending "v2" before and after a restart may clone different definitions.

---

## 11. "Unreachable action" is two different algorithms under one name

| | Publish path | Chat path |
|---|---|---|
| Where | `Program.cs:3824-3852` | `BlueprintToolExecutor.cs:1346-1375` |
| Algorithm | union of **every** action's route targets, including targets of actions that are themselves unreachable | BFS from starting actions |
| Detached cycle `{A→B, B→A}`, neither a starting action | **passes** | flagged |
| Severity | warning | error |

#1558 is about the severity. The algorithm is the deeper half and #1558 does not mention it —
promoting the publish rule to an error without also fixing the traversal leaves detached cycles
publishable.

The publish path also has no equivalent of the chat path's `NO_ROUTING_DEFINED`,
`STARTING_ACTION_NO_ROUTES`, `NO_TERMINAL_PATH` or `DUPLICATE_PARTICIPANT_ID`.

> Corrects the `blueprint-builder` skill, which still says *"treat the chat tool as the stricter
> gate"*. For structural correctness the publish path is stricter in some rules and **weaker in
> reachability**; neither is a superset. That note needs rewriting in both directions.

---

## 12. Instance creation reads the draft and pins the published

`POST /api/instances` (`Program.cs:2244-2415`):

- `:2251` resolves the blueprint from the **draft** store, falling back to latest published.
- `:2346-2365` derives `CurrentActionIds`, `ParticipantWallets` and `BlueprintTitle` from **that**.
- `:2372-2376` takes the pin from `PublishedBlueprintSelector.SelectLatest(published)` — a
  **different** definition whenever the author has edited the draft since publishing.

So an instance can be *initialised* from one definition and *pinned* to another.

---

## 13. #1563 is currently masking finding 4 (the second publish path)

The instance-creation branch (`Program.cs:2305-2318`) pushes the **draft**, with **no `$ref`
flattening** and its own serializer options, straight to the register — where `PublishService`
pushes a flattened deep-copied snapshot (`:3216-3273`).

Today the version-blind txId (`Register.Service/Program.cs:2028`) dedupes the second push away, so
the divergence is invisible. **Content-addressing the txId unmasks it**: two different shapes of the
same blueprint would then both land on the ledger under different ids, and recovery would restore
both as distinct definitions.

⚠ Any fix to #1563 must collapse the two publish paths **in the same change**, or it trades a silent
drop for a silent fork.

---

## 14. Seven "version" concepts, not four

| # | Thing | Where | Reality |
|---|---|---|---|
| 1 | `Blueprint.Version` (int) | `Sorcha.Blueprint.Models/Blueprint.cs:61` | author-settable; F194 removed it from the hash; display only |
| 2 | `Blueprint.VersionMajor` | `:106` | **wholly dead** — written by the amend clone and the designer properties panel, read by nothing |
| 3 | `Blueprint.VersionMinor` | `:112` | **wholly dead** — same |
| 4 | `PublishedBlueprint.Version` | assigned `versions.Count + 1`, `Program.cs:2879` | insert-order ordinal, re-derived on every recovery; still the amend loop's source selector (finding 10) |
| 5 | `ExecDefHash` | F194 | the pin — but see finding 8 |
| 6 | `contentHash` | sealed at `Register.Service/Program.cs:2084` | SHA-256 over canonical publish JSON; recovery's provenance check |
| 7 | `version` in the register publish **response** | `Register.Service/Program.cs:2007` | the **System Register catalogue** version — always `1` for a tenant blueprint. Unrelated to #4. |

---

## 15. The recovery endpoint is an unauthorized read surface

`GET /api/registers/{registerId}/blueprints/published`
(`Register.Service/Program.cs:2117-2190`) is `.AllowAnonymous()` with the comment *"Internal
recovery endpoint — no auth required (returns only metadata)"*. It returns full `blueprintJson` for
every blueprint on the register.

> **Narrowed during checking:** the gateway route `register-catchall` carries
> `AuthorizationPolicy: RequireAuthenticated` (`ApiGateway/appsettings.json`), so this is **not**
> internet-anonymous. The exposure is: *any authenticated user of the installation can dump every
> blueprint definition on any register*, with no org or subscription check. Lower severity than the
> comment's inaccuracy suggests, still wrong, and the comment is flatly false.

---

## Where the session stopped

The next step was the **#1563 decision**, framed as three options. Recorded here so it can be
re-entered without re-deriving:

**A — split the two jobs (was my recommendation).** Keep a stable per-`(register, blueprint)`
*anchor* transaction that starting actions chain from forever; record each **definition** as its own
content-addressed transaction chaining off the anchor. Nothing already anchored moves; republish
stops being silently dropped; the anchor derivation keeps one home and one job. Costs one new
transaction type and a recovery-path change.

**B — content-address both, one home.** Publish txId includes the exec-def hash; the derivation moves
into a shared leaf both services call (CLAUDE.md §15/§16); starting actions anchor on the publication
of the definition the instance is pinned to. Needs a legacy-id fallback so already-anchored instances
stay chainable.

**C — anchor on the register genesis.** Starting actions stop chaining from the publication
altogether; publication becomes purely a record. Smallest conceptual surface, largest blast radius on
chain validation and fork detection (N children of genesis).

Each needs: what breaks, what migrates, what an already-anchored instance does.

---

## Not yet done

- The "what should be true" half of deliverable A (all seven lifecycle stages).
- Deliverable B — the #1563 decision.
- Deliverable C — which accidents are worth keeping.
- Deliverable D — issues filed/updated (#1563, #1558, #1370, plus new ones for findings 8, 9, 10, 11, 15).
- `blueprint-builder` / `sorcha-architecture` skill corrections (finding 11 at minimum).
- `MASTER-TASKS.md`.
