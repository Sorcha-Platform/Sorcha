# Mutation matrix — Feature 195

Every guard in this feature is mutation-tested, because a guard written after the code is green never
ran red naturally and proves nothing until a discriminating change kills it.

Each row records the mutation applied, the tests it killed, and — where relevant — what it *failed*
to kill and why that mattered. The tree is restored and re-verified green after every mutation.

Run with `specs/195-blueprint-definition-identity/` scratch scripts; source restored from git.

---

## Phase 2 — canonical form and publication identity (T009)

Suite under test: `Sorcha.Blueprint.Models.Tests.Canonical`, 27 tests, green before and after.

| # | Mutation | Discriminating test killed | Also killed |
|---|---|---|---|
| 1 | Remove recursive key sorting from `BlueprintCanonicalJson.WriteObject` | `Canonicalise_SortsObjectKeys_Recursively` | both golden vectors |
| 2 | Drop the domain tag from the preimage | `Compute_IsDomainSeparatedFromTheInstanceIdentityConstruction` | `Compute_MatchesTheDocumentedPreimage`, both golden vectors |
| 3 | Drop `registerId` from the preimage | `Compute_SameDefinitionOnTwoRegisters_ProducesTwoIds` | `Compute_MatchesTheDocumentedPreimage`, both golden vectors |
| 4 | Drop `blueprintId` from the preimage | `Compute_SameContentDifferentBlueprint_ProducesTwoIds` | `Compute_MatchesTheDocumentedPreimage`, both golden vectors |
| 5 | Remove **every** separator (plain concatenation) | `Compute_FieldBoundariesAreUnambiguous` | `Compute_MatchesTheDocumentedPreimage`, both golden vectors |
| 6 | Accept duplicate object keys instead of rejecting | `Canonicalise_RejectsDuplicateKeys`, `Canonicalise_RejectsDuplicateKeys_Nested` | — |

### ⚠ One mutation was weak, and saying so is the point

The first attempt at row 5 changed the separator from `0x1F` to `0x20` (a space). It killed
`Compute_MatchesTheDocumentedPreimage` and the golden vectors — but **not**
`Compute_FieldBoundariesAreUnambiguous`, and it never could: a space is still a separator, so
`("ab","c")` and `("a","bc")` still produce different preimages. The mutation looked like it
exercised the boundary property and did not.

Replaced with "remove every separator", which makes the preimage a plain concatenation and kills the
boundary test. **A mutation that fails to kill its target test has not proven the guard weak — it has
proven the mutation wrong**, and the distinction is the whole reason for running these individually
rather than trusting a count.

### On the golden vectors killing almost everything

Both golden vectors die under five of the six mutations. That is correct and expected — they are a
broad content address, so any change to the bytes moves them. It does mean "each mutation kills
exactly one test" is not literally true here, and the useful claim is the narrower one recorded in
the table: **each mutation kills its own discriminating test**, which no other mutation kills.

The one guard the golden vectors uniquely provide is the one nothing else can: a rename of a
`[JsonPropertyName]` on the blueprint graph. That is exercised by
`GoldenVector_ModelWireShapeIsFrozen` and is the reason the model round-trip vector exists separately
from the fixture vector.

### A non-mutation finding, caught by writing the guard

`GoldenVector_ModelRoundTripIsDeterministic` was added after two consecutive runs of unchanged code
produced different ids. Cause: `Blueprint.CreatedAt` and `Blueprint.UpdatedAt` default to
`DateTimeOffset.UtcNow`, so a fixture omitting them re-dated itself on every deserialisation.

The fixture now pins both. The wider consequence is deliberate: those timestamps are part of a
definition's content and therefore its identity. That is sound because they are stamped on **draft
save** (`InMemoryBlueprintStore.UpdateAsync` / `AddAsync`), not at publish — so republishing an
untouched draft is genuinely idempotent, while any edit produces a new publication with the same
`execDefHash`, which is exactly the designed behaviour.

Had the fixture not pinned them, the golden vector would have been **flaky rather than frozen**, and
the most likely response to a random failure is to regenerate the constant — which would have quietly
destroyed the only guard protecting the ledger contract.

---

## T010 — the ownership gate, and a hole found in the gate itself

| Mutation | Outcome |
|---|---|
| Add a single-line illegal caller under `src/` | flagged, with path and line |
| Remove it | back to green |
| Add a **multi-line** illegal caller (`Type` on one line, `.Compute(` on the next) | **initially NOT flagged** |

⚠ **The gate's first version scanned line by line, and the very first real call it had to catch was
split across two lines:**

```csharp
var txId = Sorcha.Blueprint.Models.Canonical.BlueprintPublicationId
    .Compute(registerId, request.BlueprintId, canonicalJson);
```

It reported `OK` with the allowlist entry marked *stale* — i.e. it claimed the producer did not call
the producer. **A gate a line break defeats is worse than no gate, because it reports success.**

Fixed to scan whole-file with line comments blanked in place (so reported line numbers stay true) and
line numbers derived from the match offset. Re-verified against both the single-line and multi-line
probes.

The lesson generalises to the gates this one was modelled on: a grep-shaped rule must be tested
against the formatting real code actually uses, not only against the formatting the test author
happens to write.

---

## Phase 3 — a published definition survives (T022)

Suite under test: `Sorcha.Blueprint.Service.Tests`, 1233 tests, green before and after.

| Mutation | Discriminating test(s) killed |
|---|---|
| **Version-blind identity** — drop the canonical content from the preimage, reintroducing the #1563 defect exactly | `PublishAsync_BehaviouralRepublish_GetsADistinctIdentity`, `PublishAsync_PresentationalRepublish_NewIdentity_SameExecutableDefinition`, `RunRecoveryAsync_TwoDifferentDefinitionsOfOneBlueprint_RecoverBoth`, `TryVerifyProvenance_TamperedContent_Rejected` |
| **Recovery verifies nothing** — `TryVerifyProvenance` returns true unconditionally | all 7 provenance tests |
| **Store despite ledger refusal** — record locally even when the register returned nothing | `PublishAsync_RegisterRefuses_StoresNothingLocally` |

Worth noting about the first: it kills **four** tests across three classes, including a recovery test
that never mentions identity. That is the shape of the original defect — a version-blind id does not
fail where it is computed, it fails wherever something later needed two definitions to be
distinguishable. Before this feature, nothing failed at all.

The third kills exactly one test, and that test did not exist before this feature. It guards the
reordering that puts the ledger write ahead of the local store: without it, a refused publish leaves
a definition resolvable on one node and nowhere else — which is not a degraded mode, it is a
definition that looks healthy and is not.

---

## Phase 5 — behavioural signature coverage (T046)

Suite under test: `Sorcha.Blueprint.Engine.Tests`, 649 tests, green before and after.

| Mutation | Killed | Notes |
|---|---|---|
| Omit `RejectionConfig` from the projection (the flagship omission) | `RejectionConfig_ChangesTheSignature` — and **only** that | one omission, one named failure |
| **Add `Action.MutationProbe` to the model and omit it from the projection** | `EveryProperty_IsClassified(Action)` — the reflection guard | **every hand-written test in `ExecutableDefinitionHasherTests` stayed green** |

The second row is the entire argument for reflection over a list, executed rather than asserted. A
property added to the model and forgotten in the hand-written projection does not fail to compile and
does not fail any per-field test — the projection simply stops covering it, silently. Only a guard
that enumerates the model can see it.

### ⚠ A guard I wrote, ran, and then removed

The first version of the coverage half mutated **every** classified property by reflection and
required behavioural ones to move the signature. It reported **52 failures**, and they were not
findings: for collection and complex properties the generic mutator produced values that serialise
identically to the baseline (an empty list where the baseline held null), so the signature legitimately
did not move.

Replaced with concrete, hand-authored edits for the nine properties #1566 was actually raised about,
plus the presentational counter-direction. **A guard that cannot distinguish its own artefacts from
real findings is worse than a narrower one that can** — it trains the reader to discount its output,
which is how a real failure gets waved through.

What survives the narrowing is the division of labour worth keeping: **reflection guarantees
completeness** (no property escapes classification), **hand-authored edits guarantee correctness**
(each classification is implemented). Neither alone is sufficient.

### Findings from writing the guard

The classification test failed three times on my own lists before it went green — `Action.BlueprintId`
misnamed as `Blueprint`, and two `Route` entries (`FallbackMessage`, `Severity`, …) that belong to the
nested `DecisionNotice` type rather than to `Route`. Each was a claim about the model that reflection
disproved immediately.

---

## Phase 6 — the amend loop (T053)

The guards fired on their own, without a mutation being needed, and that is the record worth keeping.

| Change made | Guard that caught the consequence |
|---|---|
| Deleted `Blueprint.VersionMajor` / `VersionMinor` (wholly dead — written by two call sites, read by nothing) | **`GoldenVector_ModelWireShapeIsFrozen`** — the ledger-contract guard, firing for the first time on a real change |
| Same | `NoClassifiedProperty_HasBeenRemovedFromTheModel` — the classification list still named them |
| Same | `EveryBlueprint_UsesOnlyPropertiesTheModelsStillBind` — one shipped corpus file still declared them |
| Amend now keeps the blueprint id | `FromPublished_..._Returns201...` asserted the id must DIFFER — the test encoded the fork |

**The golden vector's first real firing is the argument for having built it.** Deleting two unused
properties is about as innocuous as a change gets: it compiled cleanly, and nothing else in ~4,300
tests noticed. But the properties were still *serialized*, so removing them changed the canonical
bytes of every definition and therefore every publication id on every register. Regenerating the
constant is correct here — the removal is intended and a wipe is authorised — and the reason is
recorded beside it, because regenerating it to make a red test go green *without knowing what moved
it* is precisely the failure it exists to prevent.

### ⚠ A self-inflicted bug worth recording, because the shape recurs

The amend endpoint 404'd on every request after the change. The cause was **two of my own edit
scripts targeting the same anchor**: the first did a broad `public int Version` → `public string
PublicationTxId` rename, which consumed the text the second script's block replacement was matching
on. The property ended up renamed but still carrying `[JsonPropertyName("version")]` and
`[Range(1, int.MaxValue)]`, so `publicationTxId` never bound, the lookup ran with an empty string,
and `[Range]` on a string does not fail validation.

Diagnosis took four rounds of printing state — store contents, register ids, null checks, and finally
a direct call to the resolver with the endpoint's own arguments, which succeeded and proved the
endpoint was not receiving what the test sent. **The lesson is the same one as the earlier regex
over-reach: a scripted edit that silently matches nothing is indistinguishable from one that worked,
so assert on the result, not on the script running.**

---

## T059 — the live run on a re-genesised n1 (2026-08-24)

Deployed to n1 as locally-built `:f195` images (branch `195-blueprint-definition-identity`, which is
master `f91da69fd` plus this feature). Every other service kept the image already on the box, so the
only variable in the run was Feature 195. Full `down -v`, volumes removed by name, genesis re-ingested
from the compiled-in anchor `cb1817467b2e87c2e5ae494a8eeac456`; docket 0 sealed with `nTx=1`, zero
`VAL_TIME_002`, migrations applied (read from the log, not the health status).

**Deploy order stopped being a risk, and that is worth knowing before the next one.** It is
load-bearing for a *rolling* upgrade, because the `RoutingDecision` field rename sits inside
`ComputeSignableBytes` and old and new producers refuse each other — but a re-genesis brings all three
services up together on an empty ledger, so the mixed-version window never exists.

### The result

| Harness | Result |
|---|---|
| `run-f195-acceptance.ps1` (this feature) | **16 / 16** |
| `run-acceptance.ps1` (Feature 194 baseline) | **19 / 21** — both failures traced to #1573, below |
| `pin_fallback` — the positive check | **ZERO**, with `pin_mismatch` and unresolvable-pin also zero |

The #1563 check is green on a real register: a behavioural republish moved the ledger's publication
count from 1 to 2. Before this feature it stayed at 1 while the endpoint answered 200 and the caller
logged success. Both vacuity-prone checks discriminate — the presentational republish left
`execDefHash` unchanged *while* the paired behavioural one moved it, and the two registers received
byte-identical definitions (asserted before comparing ids) and got different identities.

The only validator refusals on the node across the whole run were six `VAL_BP_003`, every one of them
the harness re-submitting an action the instance had already advanced past — a consequence of #1573,
not of this feature.

### The positive check could not be read the way the design says

`quickstart.md` says `curl <gateway>/metrics | grep pin_fallback`. **There is no `/metrics` endpoint** —
not on the gateway (404), not on blueprint-service (404). The counter is an OpenTelemetry meter
exported over OTLP, not a Prometheus scrape target.

It is incremented at exactly one site, unconditionally paired with a log line, so the equivalent read
is the log:

```bash
docker logs sorcha-blueprint-service 2>&1 | grep -c 'pre-Feature-194 fallback'   # must be 0
```

That is a genuine read of the same event rather than an absence-of-errors argument: every increment
emits exactly one such line. The doc should be corrected, not the platform.

### Finding 1 — neither harness had ever run

The single largest finding, and it invalidates the assumption that F194's harness was a working
baseline. Both scripts were parse-checked only, and each of these is fatal on first contact:

| Defect | How it presents |
|---|---|
| Every URL carried a doubled `/api` | `BlueprintUrl`/`RegisterUrl` already end in `/api`. Proven: `/api/blueprints` gives 401, `/api/api/blueprints` gives 404 |
| `param([hashtable]$Args)` in `Try-Action` | PowerShell's automatic `$args` wins the binding; the cast throws on EVERY call, and that wrapper fronts every action submission |
| Four mandatory-parameter mismatches | `Connect-SorchaAdmin`, `Connect-SorchaUser`, `New-SorchaWallet` (`-FetchPublicKey`), `Confirm-SorchaUserEmail` — each aborts setup |
| Public org disabled after a fresh DB init | self-registration 403s with "Self-registration is not enabled for this organization" — reads as permissions, is a missing setup step. It must be enabled BEFORE the register is created, or `New-SorchaRegister`'s public-org auto-subscribe 403s too |
| The step-6 restart used a SHORTER compose `-f` list than the node was brought up with | silently reverts blueprint-service to whatever `image:` the base files name — a restart meant to prove recovery would deploy a *different build of the service under test* and still report a clean pass |
| `(pipeline).Count` under StrictMode | throws when nothing matches, so the summary blew up on precisely the run where every check passed |

**The transferable one is the compose `-f` list.** It is the only defect here that would have produced
a green, plausible, wrong result rather than a crash. A restart that swaps the artefact under test is
not a restart.

### Finding 2 — `isPinnedToLatest` compared two different value spaces (#1563, fixed)

A seam bug of the standard shape: both sides correct, the join unverified, degrading to a *plausible*
value rather than an error.

Under Feature 195 an instance is pinned to the **publication transaction id**. `InstanceReadEndpoints`
computed "latest" as the latest definition's **`ExecDefHash`** and compared the two. The comparison
could never be true, so `isPinnedToLatest` was hard-wired `false` for every pinned instance — and
`false` is exactly what a superseded instance *should* report, so nothing looked wrong.

**`InstancePinReadTests` asserted both `true` and `false` and passed throughout.** Its fixture assigned
the *same string* to `PublicationTxId` and `ExecDefHash`, and its `IPublishedBlueprintStore` stub
resolved on `ExecDefHash` while the real store resolves on `PublicationTxId`. With one value standing
in for two, the wrong field read matched anyway.

> **A fixture that cannot tell two fields apart cannot test the join between them.** When the thing
> under test is that two values AGREE, the fixture must give them deliberately different values — and
> a stub must resolve on the key production resolves on.

Both were corrected. Mutation check: re-introducing the `ExecDefHash` read now fails exactly one test,
the intended one.

Only the *paired* live assertions caught it — one expecting `true` before a republish, one expecting
`false` after. The `false` half had been passing vacuously all along.

### Finding 3 — `alreadyPublished` never reached the caller (#1563, fixed)

The Register Service has always returned the discriminator; it stopped at the blueprint-service log
line. That is the shape of #1563 itself — the endpoint answers 200 either way, so a caller that cannot
distinguish an idempotent no-op from a real publish records success for a publish that wrote nothing.
**A flag only the server can see is not a discriminator.** Now carried on `PublishResult` and returned
on both response shapes of `POST /api/blueprints/{id}/publish`.

### Finding 4 — a blueprint draft is orphaned from its organisation on the first save (#1572, fixed)

Pre-existing; F195 touches none of the code involved. The owning organisation lives on the **row**; the
model is rebuilt from the stored **document**. Nothing verified they agree, and they stopped agreeing on
the first `PUT`: no client echoes `organizationId` in a body, so the column was nulled and the
re-serialized content dropped it with it. Every later org-scoped read and write then answered 404 for
the org that owns the draft. `OwnerId` was already immutable-after-creation for exactly this reason;
`OrganizationId` was not, and it is the one the reads filter on.

Fixed here because it blocked the acceptance — the presentational-republish step needs GET-then-PUT.

### Finding 5 — no schema validation runs on an encrypted register (#1573, deliberately NOT fixed)

The most serious finding, and out of scope on purpose.

`ActionExecutionService` gates on `Action.DataSchemas`; `ExecutionEngine.ValidateAsync` validates
`Action.Form.Schema`. Blueprints declare `dataSchemas` and never `form`, and `Form` defaults to a
layout-only control whose `Schema` is null — so validation returns `Valid()` for every payload. The
Validator then skips schema checks on encrypted payloads *because it trusts that pre-validation*,
saying so in a comment. Net effect: on a Normal (encrypted) register, **nothing validates action
payloads at all**.

Proven with a case that has nothing to do with pinning — action 1's schema is identical in every
definition of the blueprint:

```
POST /api/instances/{id}/actions/1/execute   payload = {}   ->  202, and it SEALED
```

This is why `run-acceptance.ps1` step 5's counterfactual fails: "instance B is pinned to v2" is only
meaningful if v2 is *enforced*, and a pin nobody enforces is a recorded value, not a rule. The check is
annotated as known-blocked rather than removed, because deleting it would delete the evidence.

Not fixed on this branch: switching schema validation on for every encrypted-register submission is a
behaviour change with real blast radius, and that is a scoping decision, not a drive-by.

### An observation, not a defect — the pinned-definition cache is written twice

The publish path writes the definition to Redis under `(blueprintId, execDefHash)`; the validator looks
it up under `(blueprintId, publicationTxId)`. So the publish-time key is never read, the first lookup
per definition misses and fetches from the Blueprint Service, and the read-through then caches it under
the key that *is* read. All four keys are present on n1 for a two-definition blueprint, which is how
this was spotted.

Correctness is unaffected — the definition resolves, and `pin_fallback` is zero — so this is a tidiness
and one-round-trip issue, recorded rather than filed.

### Quickstart step 8 (replica node, SC-009) — NOT EXECUTED

`tiny` was cleared (`down -v`, volumes removed by name, `.env` preserved because its ports are
non-default), redeployed on the same `:f195` images, and **replicated n1's re-genesised system register
byte-for-byte** — `dockets=1 sealed=1 txs=1`, matching n1, with 16/16 containers healthy. So the F195
build is replication-compatible.

But SC-009 asks for an instance to run on a node that only *replicated* a register and never published
to it, and that needs cross-node federation provisioning the VersionPinning harness does not have: an
**advertised** register on n1, a public-org subscription on tiny, and an org/user/participant
provisioned on tiny's (now empty) tenant database. The registers this run created are n1-local, not
advertised, and encrypted. The system register would have been the cheap substitute, but on a freshly
re-genesised node it holds only its genesis transaction — no blueprint publications to resolve.

Recorded as outstanding rather than quietly dropped: the validator's read-the-definition-off-the-register
arm is exercised on n1 (every pinned lookup misses the publish-time cache key and fetches), but the
"never published here" property specifically is unproven.

### What the two harnesses now do differently

- Publication counts are read **off the ledger** and **polled**, because a publish returns before its
  transaction seals. Reading once turns "not sealed yet" into a false FAIL on the check that matters most.
- An explicit `AwaitingInbox` gate before every actor switch, scored as its own check. `-WaitForSeal`
  waits for the seal; the instance advances a beat later when the projector folds. Two consecutive runs
  of the same code disagreed with each other before this was added — one of them scoring a **vacuous
  PASS** on the enforcement counterfactual because B was merely too early to accept anything.
- Seal waits 90s to 300s: a docket-write 409 puts the docket builder on a ~10-minute retry, so 90s times
  out on transactions that do seal.
- The response **body** is captured on the first attempt. That single change turned "400 (Bad Request)"
  into `VAL_BP_003: Action 2 is not reachable from action 2` and identified three separate causes in one
  run instead of three.
