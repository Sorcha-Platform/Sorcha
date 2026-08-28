# Baseline & Verification Log — Feature 196

## T003 — Pre-change baseline (2026-08-28, at `cfc2e48aa` + docs)

**Sorcha.Validator.Service.Tests**

```text
total:     1097
failed:        0
succeeded:  1076
skipped:      21
```

### ⚠ Tooling note — `dotnet test --project` does not work for this project

```bash
dotnet test --project tests/Sorcha.Validator.Service.Tests/Sorcha.Validator.Service.Tests.csproj
# → "Zero tests ran", exit code 5, "error: 1", no diagnostic
```

The reliable route is to build and run the test host directly:

```bash
dotnet build tests/Sorcha.Validator.Service.Tests/Sorcha.Validator.Service.Tests.csproj
cd tests/Sorcha.Validator.Service.Tests/bin/Debug/net10.0 && ./Sorcha.Validator.Service.Tests.exe
```

This matters beyond convenience: **"zero tests ran" and "all tests passed" look alike when piped
through `tail`**, and a green-looking pipeline that ran nothing is exactly the failure mode this
feature's test obligations exist to prevent. Quote the total, never just the absence of failures.

CLAUDE.md's testing section states one project is run with `--project x.csproj`; that does not hold
here. Worth correcting once the cause is known — not chased as part of this feature.

---

## Post-change results (2026-08-28)

| Suite | Before | After | Δ |
|---|---|---|---|
| Sorcha.Validator.Service.Tests | 1097 total / 0 failed | **1124 total / 0 failed** | +27 tests |
| Sorcha.Register.Service.Tests | 483 / 0 failed | **483 total / 0 failed** | anchor relocation + roster provisioning |
| Sorcha.Register.Models.Tests | — | **396 total / 0 failed** | roster uniqueness rule |
| `dotnet build` (solution) | — | **succeeded, 0 errors** | |

---

## Mutation results (SC-002) — every guard fails when its own check is removed

Each mutation was applied to the source, the suite re-run, then the file restored from a byte-exact
backup (`MUTANT` occurrence count verified back to 0 after each).

| # | Check removed | Tests that went red | Killed |
|---|---|---|---|
| 1 | Anchor fingerprint comparison in the genesis rule | `Genesis_CorrectTransactionIdButAttackerKey_IsRefused`, `Genesis_ClaimedViaTypeLabelByAnAttacker_IsRefused`, `Genesis_ClaimedViaBlueprintIdentifierByAnAttacker_IsRefused` | ✅ 3 |
| 2 | The `BlueprintId == "genesis"` claim route | `Genesis_ClaimedViaBlueprintIdentifierByAnAttacker_IsRefused` + 3 others | ✅ 4 |
| 3 | Governance-roster key match (Control) | `Control_SignedByANonMember_IsRefused` | ✅ 1 |
| 4 | Derivation-context filter on publication authority | `Publication_WhenTheRosterCarriesOnlyDocketSigningKeys_IsRefused` | ✅ 1 |
| 5 | The `BlueprintPublish` switch arm (unclassified kind) | `ExemptionKindCoverageTests.EveryExemptionKind_IsClassifiedByTheResolver` + 2 | ✅ 3 |
| 6 | Signed-field agreement comparison | `BlueprintIdDisagreeingWithTheSignedPayload_IsRefused`, `ActionIdDisagreeingWithTheSignedPayload_IsRefused` | ✅ 2 |
| 7 | Roster uniqueness back to `ValidatorId` alone | `ValidatorRosterValidationTests.SameValidator_WithTwoDifferentPurposeKeys_IsValid` | ✅ 1 |

**No surviving mutants.** Mutation 5 matters most of the seven: it proves the coverage guard actually
guards, so a future `ExemptionKind` added without an authority rule fails the build rather than
silently defaulting.

### ⚠ Mutation 7 appeared to survive, and the reason is a trap worth naming

The first run of mutation 7 reported **14 tests, 0 failed** — i.e. the guard looked vacuous. It was
not. The filter was wrong:

```bash
./Sorcha.Register.Models.Tests.exe --filter-class "*ValidatorRosterTests*"   # ran 14 UNRELATED tests
./Sorcha.Register.Models.Tests.exe --filter-method "*SameValidator_WithTwoDifferentPurposeKeys*"
#   → failed, as it should
```

The class is `ValidatorRosterValidationTests`, not `ValidatorRosterTests`. The filter silently
matched a different class and reported green. **A filtered run that matches nothing you intended is
indistinguishable from a passing run** — the same failure shape as the `dotnet test --project`
"Zero tests ran" note above. Always check the total against what you expect to have run.

---

## ⚠ Two existing tests asserted the vulnerability as correct behaviour

Worth recording, because it is why a green suite never caught #1591:

- `RightsEnforcementServiceTests.NoRoster_GenesisTx_IsStillAdmitted` set
  `Metadata["Type"] = "Genesis"` on a transaction signed by `UnknownPublicKey` and asserted
  `IsValid == true`. That is the exploit, written down as the expected result.
- `ValidationEngineTests.ValidateBlueprintConformanceAsync_Genesis/ControlTransaction_ReturnsSuccess`
  asserted that an unsigned label alone skips blueprint conformance — which includes `VAL_BP_002`
  sender authorisation.

All three now assert the opposite, and each is paired with a counterfactual plus a legitimate-signer
case so the refusal is shown to track authority rather than the label.

---

## Roster provisioning (T037) — done in code, pending re-genesis

Publication authority is matched against an ACTIVE validator-roster entry under
`sorcha:blueprint-publish`. Provisioned in both places that create a roster:

| Where | What it now emits |
|---|---|
| `RegisterCreationOrchestrator` (ordinary registers) | docket-signing entry **+ blueprint-publish entry**, same `ValidatorId` (one node, two purpose keys) |
| `SystemRegisterCommands` genesis ceremony (system register) | same pair, inside `BuildControlRecord` — the **signed payload**, which is what `GovernanceRosterService` reconstructs from |

Both publish paths were also unified onto `sorcha:blueprint-publish`. They previously disagreed:
`Program.cs` signed per-register publishes with `sorcha:register-control` while
`SystemRegisterService` seeded with `sorcha:blueprint-publish`, so **no single roster entry could
have authorised both**. Unifying was free only because the estate can be wiped.

## Live verification (T050–T052)

**NOT YET RUN.** Merged is not proven; this section stays empty until both nodes have been
exercised. Requires the genesis ceremony re-run + re-genesis of n1 and tiny — see the
`network-bootstrap` skill. ⚠ **1-hour genesis freshness window**: mint, publish images, deploy and
bootstrap must all complete inside it or genesis is refused `VAL_TIME_002`.
