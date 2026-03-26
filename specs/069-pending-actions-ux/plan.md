# Implementation Plan: Pending Actions UX Overhaul & Instance Reference System

**Branch**: `069-pending-actions-ux` | **Date**: 2026-03-26 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/069-pending-actions-ux/spec.md`

## Summary

Transform the "My Pending Actions" page from developer-oriented (blueprint IDs, instance UUIDs) to user-oriented (workflow names, action titles, human-readable application references). Add auto-generated instance references defined by blueprint authors. Fix the empty Execute Action form dialog. Add card/table view toggle with persisted preference.

## Technical Context

**Language/Version**: C# 13 / .NET 10
**Primary Dependencies**: Sorcha.Blueprint.Models, Sorcha.Blueprint.Engine, Sorcha.Blueprint.Service, Sorcha.UI.Core, MudBlazor
**Storage**: PostgreSQL (EF Core for instances), Redis (blueprint cache)
**Testing**: xUnit, FluentAssertions, Moq
**Target Platform**: Docker containers (services), Blazor WASM (UI)
**Project Type**: Distributed microservices + SPA
**Performance Goals**: Pending actions page renders in <2s with 50 actions; reference generation <10ms per instance
**Constraints**: No new services; changes span Blueprint Models, Blueprint Engine, Blueprint Service, UI
**Scale/Scope**: 4 modified projects, ~15 files, ~800 lines new code + tests

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Microservices-First | PASS | No new services. Changes within existing service boundaries. Blueprint Service owns pending actions; UI is the client. |
| II. Security First | PASS | Instance reference is intentionally public metadata (not encrypted payload). Blueprint author controls what fields are referenced. No secrets exposed. |
| III. API Documentation | PASS | Modified endpoint documented. New `InstanceReferenceTemplate` model will have XML docs. |
| IV. Testing Requirements | PASS | Unit tests for reference generation, enrichment, UI view models. Integration test for full flow. |
| V. Code Quality | PASS | Async patterns, DI, nullable types. No new compiler warnings. |
| VI. Blueprint Creation Standards | PASS | `instanceReference` is part of the JSON blueprint definition — follows blueprint-as-JSON-first principle. |
| VII. Domain-Driven Design | PASS | Uses ubiquitous language: Blueprint, Action, Instance, Participant. |
| VIII. Observability | PASS | Structured logging for reference generation. No new health endpoints needed. |

## Project Structure

### Documentation (this feature)

```text
specs/069-pending-actions-ux/
├── plan.md              # This file
├── spec.md              # Feature specification
├── research.md          # Phase 0 research decisions
├── data-model.md        # Entity changes
├── quickstart.md        # Implementation guide
├── contracts/
│   ├── pending-actions-api.md      # API response changes
│   └── instance-reference-schema.md # Reference template schema
└── checklists/
    └── requirements.md  # Spec quality checklist
```

### Source Code (repository root)

```text
src/
├── Common/
│   └── Sorcha.Blueprint.Models/
│       ├── Blueprint.cs                    # Add InstanceReference property
│       └── InstanceReferenceTemplate.cs    # NEW: template model + ReferenceComponent
├── Core/
│   └── Sorcha.Blueprint.Engine/
│       └── Services/
│           └── InstanceReferenceGenerator.cs  # NEW: reference generation logic
├── Services/
│   └── Sorcha.Blueprint.Service/
│       ├── Endpoints/ActionEndpoints.cs     # Enrich pending actions response
│       ├── Models/PendingActionSummary.cs   # Add InstanceReference field
│       └── Storage/EfCoreInstanceStore.cs   # Enrich with blueprint lookups
└── Apps/
    └── Sorcha.UI/
        ├── Sorcha.UI.Core/
        │   ├── Models/Workflows/WorkflowInstanceViewModel.cs  # Add InstanceReference to PendingActionViewModel
        │   └── Services/WorkflowService.cs                    # Map new fields
        └── Sorcha.UI.Web.Client/
            └── Pages/MyActions.razor          # Card/table toggle, grouping, enriched cards

tests/
├── Sorcha.Blueprint.Models.Tests/
│   └── InstanceReferenceTemplateTests.cs    # NEW: template validation tests
├── Sorcha.Blueprint.Engine.Tests/
│   └── InstanceReferenceGeneratorTests.cs   # NEW: generation logic tests
├── Sorcha.Blueprint.Service.Tests/
│   └── PendingActionsEnrichmentTests.cs     # NEW: enrichment tests
└── Sorcha.UI.Core.Tests/
    └── PendingActionViewModelTests.cs       # NEW: UI mapping tests
```

**Structure Decision**: All changes fit within existing project boundaries. One new class per project (InstanceReferenceTemplate in Models, InstanceReferenceGenerator in Engine, tests in respective test projects). No new projects needed.

## Complexity Tracking

No constitution violations. All changes fit within existing patterns:
- Blueprint model extension (like CredentialIssuanceConfig was added)
- Engine service addition (like CalculationEngine)
- Endpoint enrichment (like BlueprintTitle was added to pending actions)
- UI component update (like WorkflowList table)
