# Implementation Plan: Blueprint Version Pinning

**Branch**: `194-blueprint-version-pinning` | **Date**: 2026-08-23 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/194-blueprint-version-pinning/spec.md`

**Issue**: #1559 | **Design contract**:
`docs/superpowers/specs/2026-08-23-blueprint-version-pinning-design.md`

---

## Summary

Make a workflow instance run the blueprint definition it started on, permanently, by turning "which
definition" into a **sealed ledger fact** rather than a per-node lookup.

The pin is the F142 executable-definition hash. It is stamped on the sender-signed `RoutingDecision`
that already rides every forward-routing action transaction, so it is authenticated and replicated
with no new transport. The projector folds it onto the instance; the validator resolves the pinned
definition instead of the latest; the blueprint cache is re-keyed by it; and recovery restores every
published version instead of the newest per id.

Five changes the design did not anticipate are folded in: the hasher currently includes the ordinal
version (contradicting the pin's whole premise), the "immutable" published snapshot is a live
reference to the mutable draft, the cache key format has a second home as a string literal in the
publish path, a dead per-version resolver must be removed rather than left beside the real one, and
there are five hardcoded instance-version writes rather than two. All five are in
[research.md](./research.md).

---

## Technical Context

**Language/Version**: C# 14 / .NET 10

**Primary Dependencies**: `Sorcha.Register.Models` (transaction + routing models, zero-dependency
leaf), `Sorcha.Blueprint.Engine` (`ExecutableDefinitionHasher`, WASM-safe — no HttpClient, no
platform APIs), `Sorcha.Blueprint.Service`, `Sorcha.Validator.Service`, `StackExchange.Redis`
(blueprint cache), EF Core + Npgsql (instance store)

**Storage**: PostgreSQL (`sorcha_blueprint` — the `Instance` row gains one column, folded into the
existing `InitialCreate`); Redis (the validator blueprint cache, re-keyed); MongoDB (the register —
**read-only for this feature**, and deliberately not recreated, which is why the pre-feature
fallback is load-bearing)

**Testing**: xUnit v3 4.x under Microsoft.Testing.Platform (`global.json`) + FluentAssertions 8.x +
Moq 4.20.x. `--filter-class "*Name*"`, `--project x.csproj`; `--collect` is dead under MTP.

**Target Platform**: Linux containers (`blueprint-service`, `validator-service`); the hasher change
also ships inside the Blazor WASM bundle, so it must stay WASM-safe

**Project Type**: Distributed service platform — two services plus two shared libraries

**Performance Goals**: No regression. The cache re-key adds one string segment; resolution cost is
unchanged. Recovery restores N versions per blueprint instead of 1 — bounded by real publish
activity, not by editing.

**Constraints**: The pin must be derivable from sealed transactions alone (F145 determinism — two
nodes must never disagree). Every new failure mode must fail **closed**: this feature's degraded
state is the pre-existing defect, so silence is not evidence of success.

**Scale/Scope**: 2 services, 2 shared libraries, ~14 production files. Live acceptance on n1
(2 nodes).

---

## Constitution Check

*GATE: evaluated before Phase 0 and re-evaluated after Phase 1 design. No violations.*

| Principle | Assessment |
|---|---|
| **I. Microservices-First** | Both services stay independently deployable. Dependencies flow downward only: the new field lives in `Sorcha.Register.Models`, a zero-dependency leaf both services already reference. No upward dependency introduced. ⚠ Deployment **order** matters (validator first) — documented in [quickstart.md](./quickstart.md), not enforced by coupling. |
| **II. Security First** | Strengthens the posture. The pin is inside the signed material (`ComputeSignableBytes`), so it cannot be altered in transit; an unresolvable or foreign pin is **refused**, never silently downgraded. No secrets, no new external boundary. Input validation: hash format asserted on read. |
| **III. API Documentation** | Three existing responses gain fields; each needs XML docs and OpenAPI descriptions. No new endpoints, so no new Scalar surface. |
| **IV. Testing** | Above the bar by design — every guard must be **mutation-tested and observed failing** (SC-008), which is stricter than the coverage minimum. Determinism and isolation preserved: no test may depend on a live node. |
| **V. Code Quality** | Nullable enabled; the new field is `string?` on the wire and non-null on the instance. No new warnings. Async throughout the resolution path. |
| **VI. Blueprint Standards** | Unaffected — blueprints stay JSON. The live test's v1/v2 pair are JSON files. |
| **VII. Domain-Driven Design** | Uses the platform's ubiquitous language: Blueprint, Action, Participant, Publish. "Definition" is introduced deliberately for the executable projection of a Blueprint — it is not a synonym for Blueprint and the spec defines it. |
| **VIII. Observability** | Two new counters on an existing meter (`sorcha_instance_pin_fallback_total`, `sorcha_instance_pin_mismatch_total`), structured logging, opaque identifiers only. The fallback counter is what makes the feature's removal trigger checkable rather than aspirational. |

**Complexity**: nothing to justify. The feature removes a dead abstraction (R-006) and adds no new
service, project, or layer.

---

## Project Structure

### Documentation (this feature)

```text
specs/194-blueprint-version-pinning/
├── spec.md                              # done
├── plan.md                              # this file
├── research.md                          # done — 11 decisions, 6 of them NEW findings
├── data-model.md                        # done
├── quickstart.md                        # done
├── contracts/
│   ├── routing-decision-pin.md          # done — the load-bearing wire contract
│   └── instance-pin-read.md             # done — FR-018/FR-019 read surface
├── checklists/requirements.md           # done
└── tasks.md                             # /speckit.tasks — NOT created by this command
```

### Source code

```text
src/Common/Sorcha.Register.Models/
└── Transactions/RoutingDecision.cs              # + BlueprintExecDefHash, + rebuild line

src/Core/Sorcha.Blueprint.Engine/
└── Implementation/ExecutableDefinitionHasher.cs # − ordinal version from the projection (R-003)

src/Services/Sorcha.Blueprint.Service/
├── Program.cs                                   # publish: deep copy → flatten → hash → store;
│                                                #   PublishedBlueprint.ExecDefHash;
│                                                #   IPublishedBlueprintStore.GetByExecDefHashAsync;
│                                                #   the duplicated cache-key literal (R-005);
│                                                #   instance creation (:2224) resolves by pin
├── Models/Instance.cs                           # + BlueprintExecDefHash
├── Data/Entities/InstanceEntity.cs              # + column
├── Data/Migrations/…InitialCreate{,.Designer}.cs
├── Data/Migrations/BlueprintDbContextModelSnapshot.cs
├── Storage/EfCoreInstanceStore.cs               # hand-written model→entity copy (:122) MUST include it
├── Endpoints/MeApplicationEndpoints.cs          # F186 notice text from the PINNED definition
├── Services/Implementation/
│   ├── ActionExecutionService.cs                # stamp latest-at-start / pin-thereafter (step 10d)
│   ├── ActionResolverService.cs                 # resolve-by-pin overload
│   ├── InstanceProjection.cs                    # ProjectedTransaction + pin; Apply asserts equality
│   ├── InstanceProjectionResolver.cs            # read the pin off the sealed decision
│   ├── InstanceProjector.cs                     # drop hardcoded version; fold the pin
│   ├── InstanceRebuildService.cs                # identical path (F145 parity)
│   ├── BlueprintRecoveryService.cs              # restore ALL versions, not newest-per-id
│   ├── PresentationLifecycleService.cs          # 2 of the 5 hardcoded version writes (R-008)
│   └── RehearsalOrchestrationService.cs         # 1 of the 5
└── Services/Implementation/EncryptionBackgroundService.cs  # 2nd RoutingDecision producer

src/Services/Sorcha.Validator.Service/
├── Services/ValidationEngine.cs                 # ResolveBlueprintAsync + 3 call sites (570/972/1369)
├── Services/BlueprintCache.cs                   # re-key by (id, hash)
├── Services/Interfaces/IBlueprintCache.cs       # interface change, not a key-string change
├── Services/BlueprintVersionResolver.cs         # DELETE (R-006)
├── Services/Interfaces/IBlueprintVersionResolver.cs   # DELETE
├── Extensions/BlueprintVersionResolverExtensions.cs   # drop the registration
└── Services/ControlDocketProcessor.cs           # drop the no-op InvalidateCache call

tests/
├── Sorcha.Register.Models.Tests/                # the reflection-driven signing guard (write FIRST)
├── Sorcha.Blueprint.Engine.Tests/               # hasher stability: presentational vs behavioural
├── Sorcha.Blueprint.Service.Tests/              # projection, parity, recovery, publish, store
├── Sorcha.Validator.Service.Tests/              # resolution, refusal, cache key
│   └── Services/BlueprintVersionResolverTests.cs      # DELETE with its subject
└── (walkthrough) walkthroughs/…                 # the live acceptance harness
```

**Structure Decision**: no new projects. The pin field lands in the existing zero-dependency leaf
`Sorcha.Register.Models` so both services and the WASM client can name it without a new dependency
edge. The hasher change stays inside the already-portable `Sorcha.Blueprint.Engine`.

---

## Implementation phases

Ordered so that each phase is independently verifiable and the riskiest work is protected before it
is done.

### Phase A — the signing guard, before anything else

Write the reflection-driven `ComputeSignableBytes` test against the **existing** four fields.
Mutation-test it by removing `ReasonCode` from the rebuild; watch it fail and name the field;
restore. Only then is it safe to add a fifth field.

*Rationale: this is the one defect in the feature that produces a signature that looks valid and
is not. Building the guard afterwards would mean it had never run red.*

### Phase B — the hash is trustworthy

Remove the ordinal from the hashed projection (R-003). Make the published snapshot a genuine deep
copy taken **after** `$ref` flattening, and compute `ExecDefHash` over exactly the bytes that get
stored (R-004). Add `ExecDefHash` to `PublishedBlueprint` and `GetByExecDefHashAsync` to the store.

*Rationale: pinning to a hash is meaningless while the hashed content can change under it.*

### Phase C — the pin travels

Add the field to `RoutingDecision` and to the rebuild. Stamp it in both producers
(`ActionExecutionService`, `EncryptionBackgroundService`): latest-at-start for a starting action, the
instance's pin thereafter.

### Phase D — the pin is folded

`ProjectedTransaction` carries it; `InstanceProjectionResolver` reads it; `InstanceProjection.Apply`
asserts equality and refuses a foreign pin; `InstanceProjector` and `InstanceRebuildService` take the
identical path including the identical fallback. Remove the five hardcoded version writes (R-008).
Schema + `EfCoreInstanceStore.UpdateAsync` hand-copy.

*Verified by the existing F145 parity test, which must still pass — it is the guarantee this phase
is most likely to break.*

### Phase E — the pin is enforced

Validator: `ResolveBlueprintAsync(blueprintId, execDefHash)` at all three call sites; refuse an
unresolvable pin with `VAL_BP_VERSION_001` and **no fallback to latest**. Re-key the cache through a
single shared key helper, fixing both homes together (R-005). Recovery restores all versions.

*The cache re-key is the highest-risk change in the feature because getting it half-right fails to
the old behaviour rather than to an error.*

### Phase F — remove the dead resolver

Delete `IBlueprintVersionResolver`, its implementation, registration, test file and the no-op
`ControlDocketProcessor` call (R-006). File the "config change does not invalidate the blueprint
cache" observation separately rather than fixing it here.

### Phase G — reporting

The three additive read fields, the two counters, XML docs and OpenAPI descriptions.

### Phase H — mutation evidence

For each guard, break the source deliberately, record **which named test** catches it, restore,
confirm green. At minimum: remove the pin from `ComputeSignableBytes`; make the validator fall back
to latest; make recovery keep only the newest version; re-key the cache reader but not the writer.

### Phase I — the live acceptance test on n1

Deploy validator first, then the workflow service. Run the six-step test in
[quickstart.md](./quickstart.md). **Step 6 (restart, then advance the old instance) is the gate.**

### Phase J — documentation

`MASTER-TASKS.md`; CLAUDE.md if a pattern changes; the `blueprint-builder` and `sorcha-architecture`
skills if documented behaviour changes; service READMEs; `docs/reference/API-DOCUMENTATION.md` for
the additive response fields.

---

## Risks

| Risk | Why it is dangerous | Mitigation |
|---|---|---|
| A field is added to `RoutingDecision` and not to the rebuild | Rides the wire unauthenticated **while appearing signed**. No error, ever. | Phase A, reflection-driven, mutation-tested against an existing field |
| Cache re-keyed on one side only | Every lookup misses, falls through to the fetcher, gets **latest** — i.e. the exact defect, with a green suite | One shared key helper; both homes changed together; mutation test that re-keys the reader alone |
| Projector and rebuild take different fallback paths | Breaks F145 rebuild parity — two derivations of one instance disagree | Single shared resolution path; the existing parity test |
| Migration amended but the database not recreated | `MigrateAsync` silently no-ops; surfaces far away as raw Postgres `42703` | `dotnet ef migrations script --idempotent` proof; explicit recreate step in the runbook |
| Deploying the workflow service before the validator | **Every submission refused** — old validator computes different signable bytes | Documented order; validator-first is backward compatible, the reverse is not |
| The first publish after deploy returns `409 REHEARSAL_REQUIRED` | Looks like a publish-gate regression; would send an investigation the wrong way | Expected consequence of R-003, called out in the runbook |
| Every failure mode degrades to the **old behaviour** | Absence of errors is not evidence of success | The fallback counter must read zero on a register created after deploy — the positive check, not the absence of a negative |

---

## What this plan deliberately does not do

- **No instance migration.** D1 rules it out; it would need its own ledger representation.
- **No platform-level gate on upgrading a blueprint.** D2 — that is authored as a governance
  blueprint. Stated here as well as in the spec because it is the kind of thing a later reader adds
  in good faith.
- **No change to how ordinal versions are assigned.** They stay a display label; this feature stops
  anything depending on them.
- **No fix to the cache-invalidation gap** exposed by R-006 — filed separately rather than folded in
  silently.
- **No change to design-time surfaces.** The designer, catalogue, export and admin UI correctly
  resolve the latest definition and are out of scope.
