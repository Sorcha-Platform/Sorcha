# Quickstart: Validator Exemption Authority (Feature 196)

**Feature**: 196 · **Date**: 2026-08-28 · **Issue**: #1591

## What this feature does in one paragraph

The validator waives six of its thirteen rules for administrative transactions. It decides who gets
that waiver by reading a field the submitter sets freely and no signature covers. This feature makes
the waiver depend on **who signed**, which is already proved, instead of **what the transaction
claims**, which is not. Nothing about what a waiver does changes.

## Read these first, in this order

1. `docs/superpowers/specs/2026-08-28-validator-exemption-authority-design.md` — the verified
   findings and why the obvious fix (sign the metadata / move the discriminator into the payload)
   does not work for two of the three values.
2. [`spec.md`](./spec.md) — requirements and the four hard constraints.
3. [`research.md`](./research.md) — **§R2 is unresolved and gates the last user story.**
4. [`contracts/authority-resolution.md`](./contracts/authority-resolution.md) — the rule itself.

## The two decisions to confirm before executing tasks

1. **R2 — what proves publication authority.** The spec says "the register's control key"; the code
   shows the publisher is the *node's* system wallet (slot 101), which is deliberately not on the
   governance roster. Recommendation: match against the register's validator roster, using the
   existing register-control derivation context rather than migrating publication onto its own.
2. **FR-007 — fail closed.** Where authority cannot be resolved the exemption is withheld. This turns
   an authority-resolution outage into refused administrative traffic rather than a silent security
   downgrade. Deliberate, and the cheapest point at which to reverse it is now.

## Orientation in the code

| What | Where |
|---|---|
| The grant routes being replaced | `src/Services/Sorcha.Validator.Service/Services/TransactionTypeClassifier.cs` |
| Where the grant is consumed | `ValidationEngine.cs` — sequence replay, schema, blueprint conformance, routing, crypto policy |
| The compensating check that exists today | `RightsEnforcementService.IsGovernanceTransaction` |
| The precedent to follow | `SignedPayloadType()` / `HasUncorroboratedLifecycleMetadata()` — same defect, fixed for the lifecycle predicates in the 2026-07-29 review |
| What is signed | `ValidationEngine.VerifySignaturesAsync` — `"{TransactionId}:{PayloadHash}"`, and both are the same value |
| The trust anchor to relocate | `src/Services/Sorcha.Register.Service/Provenance/INodeTrustAnchor.cs` |
| Test fixture the original probe used | `ValidationEngineChainBindingTests` |

## Building and testing

```bash
dotnet build
dotnet test --project tests/Sorcha.Validator.Service.Tests/Sorcha.Validator.Service.Tests.csproj
dotnet test --filter-class "*ExemptionAuthority*"
```

`dotnet test` runs in Microsoft.Testing.Platform mode (opted in via `global.json`). VSTest-style
arguments do not apply — filters are `--filter-class` / `--filter-method`, and the coverlet
`--collect` collector does not run.

## Three ways this feature can ship without working

Each has already happened on this codebase, which is why they are called out rather than assumed
away:

1. **A guard that passes vacuously.** #1587's tests stubbed the hash provider to a fixed array, so
   every hash compared equal by construction and the defect was invisible through a green suite.
   Do not stub hashing; add the counterfactual to every guard; verify each guard goes red when its
   own check is removed.
2. **Closing one route of two.** The genesis exemption has two independent routes. Closing the
   metadata route alone closes nothing, because the blueprint-identifier route grants the same six
   waivers without touching metadata.
3. **Green locally, unchanged in the deployment.** A restart with a short compose file list swaps
   the artefact under test and still passes. Verify the running image is the one built.

## Definition of done

- Every route refused for an unauthorised signer, each with its counterfactual in the same run.
- Every guard fails when its own check is removed.
- Genesis bootstrap, blueprint publication, and governance propose→approve→enact all still complete.
- Live on **both** n1 and tiny, including a replica pull of sealed history. Merged is not proven.
- `.specify/MASTER-TASKS.md` updated; scratch files cleaned up.
