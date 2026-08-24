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
