# Implementation Plan: Citizen "My Applications" View

**Branch**: `186-citizen-my-applications` | **Date**: 2026-08-02 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/186-citizen-my-applications/spec.md`

## Summary

Give a citizen one durable web page answering *"what did I submit, and what happened?"*, including finished and refused applications, with the decision reason shown against each.

The mechanism: carry the taken route id and the non-sensitive reason code from the signed ledger metadata into the deterministic instance fold, then resolve them to citizen-facing wording at read time through the blueprint's own `x-decision-notice` catalogue — the same resolution the existing decision-notification dispatcher uses, so the page and the notification can never disagree. Wording is deliberately *not* folded, because that would put node-local blueprint state inside a fold Feature 145 requires to be identical on every node.

Phase 0 turned up three things that change the build (full detail in [research.md](./research.md)):

- The client method the issue described as a ready data layer returns an id and two timestamps — its view model does not match what the server sends, and `state` is an integer named differently from `Status`.
- `ProjectedTransaction.IsRejection` is never set true by any production code, so `InstanceState.Rejected` is unreachable through the fold.
- A refusal is not a state, it is a **route carrying a decision notice**. A refused application ends as `Completed`, indistinguishable from an approved one — which is why the citizen-facing outcome must be derived from the recorded decision (spec FR-027).

## Technical Context

**Language/Version**: C# 14 / .NET 10

**Primary Dependencies**: ASP.NET Core Minimal APIs, EF Core (Npgsql), Blazor WebAssembly, MudBlazor

**Storage**: PostgreSQL via `EfCoreInstanceStore`; two new nullable text columns on `Instances`

**Testing**: xUnit v3 + FluentAssertions 8 + Moq (unit/service), bUnit (`Sorcha.UI.Core.Tests`), Playwright/NUnit (`Sorcha.UI.E2E.Tests`)

**Target Platform**: Blueprint Service (Linux container) + `Sorcha.UI.Web.Client` (Blazor WASM under `/app`)

**Project Type**: Web application — existing service backend plus an existing WASM front end

**Performance Goals**: List renders inside the platform's existing citizen-list budget; the read path adds at most one blueprint lookup per distinct blueprint on a page, memoised per request

**Constraints**: The fold must stay deterministic across nodes and identical under rebuild; the read path must not require payload decryption; `/api/instances*`, `/my-actions`, and every PWA surface must be behaviourally unchanged

**Scale/Scope**: Two new endpoints, two new pages, two new persisted fields, one nav addition, one nav rename across four locales

## Constitution Check

*GATE: passed before Phase 0, re-checked after Phase 1 design. No violations; Complexity Tracking omitted.*

| Principle | Assessment |
|---|---|
| I. Microservices-First | New endpoints live in the service that already owns instances. No new upward dependency; the UI reaches Blueprint Service through the gateway as it already does. |
| II. Security First | Read path is participation-gated by the same resolver the existing instance reads use. Refusal for a non-participant is deliberately indistinguishable from not-found (FR-021), matching the existing `Forbidden()` treatment introduced by #1183. Only non-sensitive, already-clear ledger metadata is projected — no payload decryption anywhere in this feature (FR-011). |
| III. API Documentation | Both endpoints get `.WithSummary()` / `.WithDescription()` and XML docs on every public type. Contract recorded under `contracts/`. |
| IV. Testing | Red-first throughout. Coverage targeted above 85% on new code. The projection change is guarded at the **resolver→fold join**, not only at the fold, because R2 shows a fold-only test proves nothing about production. |
| V. Code Quality | Nullable enabled, async I/O, DI, no new warnings. |
| VI. Blueprint Standards | No blueprint authoring changes. Test fixtures declare `x-decision-notice` as JSON, not fluent code. |
| VII. Domain-Driven Design | Ubiquitous language holds at the domain boundary: *Blueprint*, *Action*, *Participant*, *Instance*. The citizen-facing surface deliberately says "application" and "step" — that is the citizen's language, and it is confined to the presentation layer and the `/api/me/*` projection, never leaking into domain types. |
| VIII. Observability | Structured logging on the read path and on reason resolution; no string interpolation. Existing projection metrics unchanged. |

**Post-Phase-1 re-check**: unchanged. The one judgement worth restating is the Principle VII reading above — a deliberate, bounded exception at the presentation boundary, not a redefinition of domain terms.

## Project Structure

### Documentation (this feature)

```text
specs/186-citizen-my-applications/
├── spec.md
├── plan.md              # this file
├── research.md          # Phase 0 findings
├── data-model.md        # Phase 1
├── quickstart.md        # Phase 1
├── contracts/
│   └── me-applications.md
├── checklists/
│   └── requirements.md
└── tasks.md             # /speckit.tasks output
```

### Source Code (repository root)

```text
src/Services/Sorcha.Blueprint.Service/
├── Data/
│   ├── BlueprintDbContext.cs                   # + 2 property mappings
│   ├── Entities/InstanceEntity.cs              # + DecisionRouteId, DecisionReasonCode
│   └── Migrations/                             # InitialCreate amended in place (see research R5)
├── Endpoints/
│   └── MeApplicationEndpoints.cs               # NEW — GET /api/me/applications[/{id}]
├── Models/
│   ├── Instance.cs                             # + DecisionRouteId, DecisionReasonCode
│   └── MyApplicationDto.cs                     # NEW — summary + detail projections
├── Services/Implementation/
│   ├── InstanceProjection.cs                   # ProjectedTransaction + RouteId/ReasonCode; stamped in ApplyInPlace
│   ├── InstanceProjectionResolver.cs           # pass the resolved decision's route + reason through
│   └── MyApplicationProjector.cs               # NEW — instance + blueprint -> DTO, incl. outcome derivation
├── Storage/EfCoreInstanceStore.cs              # + 2 entries in the hand-written copy list
└── Program.cs                                  # map the new endpoint group

src/Apps/Sorcha.UI/Sorcha.UI.Components.User/
├── Models/User/Workflows/WorkflowInstanceViewModel.cs   # rewritten to match the DTO
├── Services/User/IWorkflowService.cs                    # GetMyWorkflowsAsync retargeted + detail method
├── Services/User/WorkflowService.cs
└── wwwroot/i18n/{en,de,es,fr}.json                      # + nav.myApplications, nav.pendingActions -> nav.workQueue

src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/
├── Pages/MyApplications.razor                  # NEW  /my-applications
├── Pages/MyApplicationDetail.razor             # NEW  /my-applications/{InstanceId}
├── Pages/MyWorkflows.razor                     # redirect target retargeted
├── Pages/MyActions.razor                       # heading rename only
├── Components/Layout/MainLayout.razor          # nav entry + rename
├── Components/Layout/StatusFooter.razor        # link text
├── Services/WebInboxDetailRouter.cs            # NEW — /api/instances/{id} -> my-applications/{id}
└── Program.cs                                  # register the router override

tests/
├── Sorcha.Blueprint.Service.Tests/
│   ├── Projection/InstanceProjectionTests.cs               # decision projection + determinism
│   ├── Projection/InstanceProjectionResolverTests.cs       # NEW — the resolver->fold join
│   ├── Endpoints/MeApplicationEndpointsTests.cs            # NEW — gate, outcome, reason resolution
│   └── Storage/EfCoreInstanceStoreUpdateRoundTripTests.cs  # extended, not supplemented
├── Sorcha.UI.Core.Tests/Pages/MyApplicationsTests.cs       # NEW — bUnit
└── Sorcha.UI.E2E.Tests/
    ├── PageObjects/MyApplicationsPage.cs                   # NEW
    └── Docker/MyApplicationsTests.cs                       # NEW
```

**Structure Decision**: No new projects. Server work lands in `Sorcha.Blueprint.Service`, which already owns instances and the projection; shared client models and services land in `Sorcha.UI.Components.User` per the Feature 122/123 audience convention (user-facing, and the PWA may want the same models later); the two pages land in `Sorcha.UI.Web.Client` because this pass is web-only.

## Design

### Server — projection

`ProjectedTransaction` gains `RouteId` and `ReasonCode`. `InstanceProjectionResolver.ResolveAsync` already holds the resolved `RoutingDecision` in a local; it passes both through. `InstanceProjection.ApplyInPlace` stamps them onto the instance beside the existing state derivation, and **clears them when the folded transaction carries no decision**, so a later step cannot leave a stale reason attached to a still-running application.

Both values originate in signed clear metadata (`RoutingDecision.RouteId` / `ReasonCode`, both inside `ComputeSignableBytes`), so determinism and rebuild parity hold by construction. The resolved wording is not folded.

### Server — read

`MyApplicationProjector` turns an `Instance` plus its blueprint into the DTO, resolving per research R4. Outcome derivation, in order:

1. No recorded decision → the instance state's own name.
2. Decision present, taken route resolvable, route declares a notice → outcome from the notice's `Severity` (`Warning`/`Error` → refused; otherwise the state's name), plus title and resolved reason.
3. Decision present but route or blueprint not resolvable on this node → the state's name, no reason. Never an error.

`MeApplicationEndpoints` resolves caller wallets via `ParticipantWalletResolver.ResolveUserWalletAddressesAsync`, dedupes across wallets, orders newest-first with an id tiebreak, and pages. The detail handler reuses `InstanceParticipantGate` and returns the same indistinguishable refusal the sibling endpoints use.

### Client

`WorkflowInstanceViewModel` is rewritten to the DTO. `GetMyWorkflowsAsync` retargets; a detail method is added. `MyApplications.razor` renders state chip, reference, reason, and a Continue affordance on `needsYou` rows routed into the existing action flow. `MyApplicationDetail.razor` renders the step timeline plus decision. `WebInboxDetailRouter` subclasses `DefaultInboxDetailRouter` and maps instance hrefs to the new detail route using the base class's `TryReadInstanceId`.

### Navigation

Add `nav.myApplications`; rename `nav.pendingActions` to `nav.workQueue` in all four locales, with the `MyActions` page heading and the `StatusFooter` link text following. `/my-actions` keeps its route, scope, and behaviour.

## Risks

| Risk | Mitigation |
|---|---|
| A new `Instance` field is dropped by the hand-written copy list, exactly as `LastAppliedTxId` was | Extend the whole-model round-trip test; it fails on any unlisted field, not just these two |
| A fold-only test passes while production never exercises the path (the R2 shape) | Test the resolver→fold join with a real `RoutingDecision`, not a hand-built `ProjectedTransaction` |
| Outcome derivation guard passes on first run and proves nothing | Mutation-test it: invert the severity check and confirm a test goes red |
| Reason wording drifts between page and notification | Both call `DecisionNotice.ResolveMessage`; no second implementation |
| The rename misses a locale or an in-app reference | Grep-driven task; E2E asserts the rendered label |
