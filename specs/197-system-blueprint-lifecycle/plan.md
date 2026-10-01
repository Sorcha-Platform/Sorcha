# Implementation Plan: System Blueprint Lifecycle

**Branch**: `197-system-blueprint-lifecycle` | **Date**: 2026-10-01 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/197-system-blueprint-lifecycle/spec.md`. Design:
`docs/superpowers/specs/2026-10-01-system-blueprint-lifecycle-design.md`. Research: [research.md](research.md).

## Summary

Make the platform's own blueprints on the system register (SSR) upgradeable without silent failure:
"current" by ledger order; four-state drift reported on every node; a catalogue-only, SystemAdmin-only,
audited operator publish (+ CLI) replacing the raw-body endpoint; governance proposals pin the governance
definition they were raised under (approvals and enactment inherit it), resolved from the SSR by the
validator; and the validator evicts its latest-by-id cache when an SSR docket seals on any node.

## Technical Context

**Language/Version**: C# 14 / .NET 10

**Primary Dependencies**: ASP.NET Core Minimal APIs, System.Text.Json, JsonSchema.Net, StackExchange.Redis
(via existing `BlueprintCache` / `IEventSubscriber`), System.CommandLine (CLI), OpenTelemetry

**Storage**: Existing register ledger (MongoDB via `IReadOnlyRegisterRepository`), Redis (validator cache,
event bus). No new tables, no EF migration.

**Testing**: xUnit v3 (MTP mode), FluentAssertions, Moq; `Sorcha.Cli.ContractTests`; live gate on n1+tiny

**Target Platform**: Linux containers (docker compose), Aspire for local

**Project Type**: Multi-service backend + CLI

**Performance Goals**: Drift computation once at startup + every 10 min (4 blueprints) — negligible.
Proposal validation adds one uncached Register Service call (current publication) and, for approvals, one
transaction fetch — proposals/approvals are rare.

**Constraints**: Existing canonical bytes of every `ControlTransactionPayload` producer MUST stay identical
(genesis, enactment, pre-signed SSR genesis). Validator deploys before register-service. Fail closed on any
unresolvable pin; never fall back to latest for a pinned transaction.

**Scale/Scope**: 4 system blueprints; 3 services touched (Register, Validator, CLI) + 1 shared library
(`Sorcha.Register.Models`).

## Constitution Check

| Principle | Status |
|---|---|
| I Microservices | ✅ No new cross-service dependency direction; validator → register client already exists. |
| II Security | ✅ Narrows an over-broad endpoint; removes caller-supplied definition content; fails closed; audited refusals. |
| III API docs | ✅ New/changed endpoints get `.WithSummary/.WithDescription` + XML docs; contracts in `contracts/`. |
| IV Testing | ✅ Every guard has a counterfactual test (SC-006); contract tests updated. |
| V Code quality | ✅ No new projects; nullable on; no warnings. |
| VI Blueprints | ✅ Governance definition stays a JSON template; schema change is in the JSON file. |
| VII DDD | ✅ "Publish", "Blueprint", "Action" used consistently. |
| VIII Observability | ✅ Health check, two gauges/counters, structured logs (no interpolation). |

CLAUDE.md patterns touched: §13 (tier policies), §16 (code stays local), §22 (publication id = identity;
Register Service remains its sole producer — the operator publish reuses `SystemRegisterService`), §23
(no exemption logic changes), #1648 refusal audit, §18 (CLI DTO contract tests), §14 untouched.

**Post-design re-check:** ✅ — no violations; Complexity Tracking empty.

## Project Structure

### Documentation (this feature)

```text
specs/197-system-blueprint-lifecycle/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── system-register-operator.openapi.yaml
│   └── governance-definition-pin.md
├── checklists/requirements.md
└── tasks.md            # /speckit.tasks
```

### Source Code (touched)

```text
src/Common/Sorcha.Register.Models/
├── SystemBlueprintCatalog.cs                 # NEW — the 4 ids, one home
├── GovernanceModels.cs                        # + ControlTransactionPayload.GovernanceDefinitionTxId
└── GovernanceDefinitionPin.cs                 # NEW — pure "which definition governs this step" (beside GovernanceAuthorisationValidator)


src/Services/Sorcha.Register.Service/
├── Services/SystemBlueprintCurrency.cs        # NEW — ledger-order current/version
├── Services/SystemBlueprintDriftReporter.cs   # NEW — 4-state classifier + periodic hosted run
├── Services/SystemBlueprintCatalogSource.cs   # NEW — load catalogue JSON (extracted from bootstrapper)
├── Services/SystemRegisterService.cs          # use currency resolver; publishedBy/seedReason
├── Services/SystemRegisterBootstrapper.cs     # use catalog + catalogue source
├── Services/SystemBlueprintsHealthCheck.cs    # NEW
├── Endpoints/SystemRegisterEndpoints.cs       # remove POST /publish; add drift + operator publish
└── Program.cs                                 # stamp pin at the two proposal producers; DI

src/Services/Sorcha.Validator.Service/
├── Services/ValidationEngine.cs               # governance pin in ValidateSchemaAsync; SSR arm; VAL_GOV_DEF_001
└── Services/SystemBlueprintCacheEvictionService.cs  # NEW — docket:confirmed subscriber

src/Apps/Sorcha.Cli/Commands/SystemRegisterCommands.cs   # + drift, publish commands

blueprints/templates/register-governance-v1.json         # action 1: optional governanceDefinitionTxId

tests/ (Register.Models.Tests, Register.Service.Tests, Validator.Service.Tests, ServiceDefaults.Tests,
        Sorcha.Cli.Tests, Sorcha.Cli.ContractTests)
```

**Structure Decision**: Existing service folders only; two pure helpers placed in the shared leaves the
validator and register service already reference, so neither service depends on the other's internals.

## Implementation Phases (input to /speckit.tasks)

1. **Foundation** — `SystemBlueprintCatalog`; `SystemBlueprintCatalogSource`; `SystemBlueprintCurrency`
   (ledger order) wired into `SystemRegisterService`.
2. **US1 Drift** — reporter, hosted periodic run, health check, gauge, `GET /drift`.
3. **US2 Pinning** — payload property (omit-when-null) + schema + contract tests; `GovernanceDefinitionPin`;
   stamp at the two producers; validator: pin in `ValidateSchemaAsync`, SSR arm, `VAL_GOV_DEF_001`, legacy
   counter; proposal audit view shows the definition.
4. **US3 Operator publish** — endpoint (auth, precondition, catalogue-only, dryRun, expectedCurrent, refusals,
   audit, ledger `publishedBy`), remove old route, CLI drift/publish + contract tests.
5. **US4 Propagation** — validator `docket:confirmed` eviction service.
6. **Docs + live gate** — READMEs, API docs, sorcha-architecture skill, MASTER-TASKS; n1+tiny live run.

US2 and US3 are independent of each other after Phase 1; US4 is independent of all but Phase 1.

## Rollout

Validator → register-service → operator publishes the new `register-governance-v1` (R1: old definition
tolerates the stamped field, so step 2 may precede step 3).

## Complexity Tracking

None.
