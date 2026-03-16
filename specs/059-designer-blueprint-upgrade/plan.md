# Implementation Plan: Designer & Blueprint Instructions Upgrade

**Branch**: `059-designer-blueprint-upgrade` | **Date**: 2026-03-16 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/059-designer-blueprint-upgrade/spec.md`

## Summary

Upgrade the blueprint designer ecosystem with: (1) unified diagram component replacing three separate rendering paths, (2) blueprint instructions model with Markdown content and schema-sourced fallback, (3) semantic major.minor versioning with structural diff detection, (4) dual-source catalogue (templates + system register), (5) designer context handoff, (6) Blueprint Publishing Blueprint governance workflow, and (7) stub fixes for export/clipboard/routes/disclosures/field resolution.

## Technical Context

**Language/Version**: C# 13 / .NET 10
**Primary Dependencies**: Blazor WASM, MudBlazor, Blazor.Diagrams, SignalR, JsonSchema.Net, NBitcoin
**Storage**: MongoDB (system register transactions), PostgreSQL (tenant/wallet), Redis (cache), In-Memory (templates)
**Testing**: xUnit 3.2.2, FluentAssertions 8.8.0, Moq 4.20.72, Playwright (E2E)
**Target Platform**: Blazor WebAssembly (browser) + .NET backend services (Linux containers)
**Project Type**: Distributed microservices with Blazor WASM frontend
**Performance Goals**: Diagram renders <2s, catalogue loads <3s, minor version publish <5min e2e
**Constraints**: Blueprint JSON must remain backwards-compatible; instructions section is additive only
**Scale/Scope**: ~15 files modified in models, ~20 new/modified UI components, 1 new blueprint template, 2 service modifications

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Microservices-First | PASS | Changes touch existing services (Register, Blueprint, UI) without new services. No upward dependencies. |
| II. Security First | PASS | Publishing workflow requires wallet signing. Blueprint-defined governance roles take precedence over org-admin. No secrets in templates. |
| III. API Documentation | PASS | New endpoints will use Scalar with XML docs. OpenAPI spec updated. |
| IV. Testing Requirements | PASS | Unit tests for models/services, integration tests for publishing flow, E2E for designer UI. >85% target for new code. |
| V. Code Quality | PASS | Async/await for all I/O. DI throughout. Nullable reference types enabled. |
| VI. Blueprint Creation Standards | PASS | Publishing Blueprint ships as JSON template. Instructions stored in JSON model. No C# blueprint generation. |
| VII. Domain-Driven Design | PASS | Uses canonical terms: Blueprint, Action, Participant, Disclosure, Publish. |
| VIII. Observability by Default | PASS | Structured logging on publish/version events. Health checks unchanged. |

No violations — complexity tracking not required.

## Project Structure

### Documentation (this feature)

```text
specs/059-designer-blueprint-upgrade/
├── plan.md              # This file
├── research.md          # Phase 0: technical research
├── data-model.md        # Phase 1: entity definitions
├── quickstart.md        # Phase 1: build order and key files
├── contracts/           # Phase 1: API contract changes
│   ├── blueprint-instructions.md
│   └── system-register-versioning.md
└── tasks.md             # Phase 2 output (via /speckit.tasks)
```

### Source Code (repository root)

```text
src/
├── Common/
│   └── Sorcha.Blueprint.Models/
│       ├── Blueprint.cs                    # MODIFY: add Instructions property
│       ├── Action.cs                       # MODIFY: add Instructions property
│       ├── Control.cs                      # MODIFY: add Instructions property
│       ├── Participant.cs                  # MODIFY: add Instructions property
│       ├── BlueprintInstructions.cs        # NEW: top-level instructions model
│       ├── InstructionSet.cs              # NEW: linked translation model
│       └── BlueprintVersion.cs            # NEW: major.minor version model
├── Core/
│   └── Sorcha.Blueprint.Schemas/
│       └── SchemaDescriptionExtractor.cs   # NEW: extract property descriptions from schemas
├── Services/
│   ├── Sorcha.Blueprint.Service/
│   │   ├── Program.cs                     # MODIFY: auto-seed templates on startup
│   │   └── Templates/
│   │       └── TemplateSeedService.cs     # NEW: hosted service for auto-seeding
│   └── Sorcha.Register.Service/
│       ├── Services/
│       │   ├── SystemRegisterService.cs   # MODIFY: version metadata in publish
│       │   └── StructuralDiffService.cs   # NEW: compare blueprint structural hashes
│       └── Endpoints/
│           └── SystemRegisterEndpoints.cs # MODIFY: version history query
├── Apps/
│   └── Sorcha.UI/
│       ├── Sorcha.UI.Core/
│       │   ├── Components/
│       │   │   ├── Designer/
│       │   │   │   ├── BlueprintDiagram.razor          # NEW: unified diagram (Edit/Preview/Compact)
│       │   │   │   ├── BlueprintViewerDiagram.razor    # MODIFY: delegate to BlueprintDiagram
│       │   │   │   ├── InstructionsTab.razor           # NEW: instructions editor panel
│       │   │   │   ├── InstructionsPreview.razor       # NEW: participant-view preview
│       │   │   │   ├── RouteEditor.razor               # NEW: route CRUD in properties
│       │   │   │   ├── DisclosureEditor.razor          # NEW: disclosure CRUD in properties
│       │   │   │   └── PropertiesPanel.razor           # MODIFY: add Instructions tab
│       │   │   └── Templates/
│       │   │       └── PublishedBlueprintList.razor     # NEW: system register browser
│       │   └── Services/
│       │       ├── BlueprintLayoutService.cs           # MODIFY: swimlanes, improved spacing
│       │       ├── SchemaFieldResolver.cs              # NEW: parse schema properties for editors
│       │       └── InstructionExportService.cs         # NEW: export/import instruction strings
│       └── Sorcha.UI.Web.Client/
│           ├── Pages/
│           │   ├── Designer.razor                      # MODIFY: accept ?blueprint= query param
│           │   ├── BlueprintChat.razor                 # MODIFY: pass ID on handoff, use diagram
│           │   └── Templates.razor                     # MODIFY: dual-source tabs
│           └── Components/
│               └── Chat/
│                   └── BlueprintPreview.razor           # MODIFY: replace with BlueprintDiagram Preview mode
└── blueprints/
    └── templates/
        └── blueprint-publishing-v1.json                # NEW: governance workflow template

tests/
├── Sorcha.Blueprint.Models.Tests/
│   ├── BlueprintInstructionsTests.cs       # NEW
│   ├── BlueprintVersionTests.cs            # NEW
│   └── StructuralDiffTests.cs              # NEW
├── Sorcha.Blueprint.Service.Tests/
│   └── TemplateSeedServiceTests.cs         # NEW
├── Sorcha.Register.Service.Tests/
│   └── VersionedPublishTests.cs            # NEW
├── Sorcha.UI.Core.Tests/
│   ├── SchemaFieldResolverTests.cs         # NEW
│   ├── InstructionExportServiceTests.cs    # NEW
│   └── BlueprintLayoutServiceTests.cs      # MODIFY: swimlane tests
└── Sorcha.UI.E2E.Tests/
    └── Docker/
        └── DesignerWorkflowTests.cs        # NEW: handoff + publish E2E
```

**Structure Decision**: Extends existing microservices architecture. No new projects — all changes fit within existing Sorcha.Blueprint.Models, Sorcha.UI.Core, Blueprint Service, and Register Service. The Blueprint Publishing Blueprint is a JSON template file, not a new service.
