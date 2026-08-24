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
