# Quickstart: Designer & Blueprint Instructions Upgrade

**Feature**: 059-designer-blueprint-upgrade
**Date**: 2026-03-16

## Build Order

Implementation should follow this dependency order. Each phase can be developed and tested independently.

### Phase A: Model Layer (no UI dependencies)

1. **BlueprintInstructions.cs** — New model class
2. **InstructionSet.cs** — New model class
3. **BlueprintVersion.cs** — New model class
4. **Blueprint.cs** — Add `Instructions` and `VersionMajor`/`VersionMinor` properties
5. **Action.cs** — Add `Instructions` property
6. **Control.cs** — Add `Instructions` property
7. **Participant.cs** — Add `Instructions` property
8. **Unit tests** for new models and serialization backwards-compatibility

### Phase B: Backend Services (depends on Phase A)

9. **StructuralDiffService.cs** — Structural hash computation and comparison
10. **SchemaDescriptionExtractor.cs** — Extract property descriptions from JSON Schema documents
11. **SystemRegisterService.cs** — Extend publish with version metadata, add version history query
12. **SystemRegisterEndpoints.cs** — Add `/versions` and `/classify-change` endpoints
13. **TemplateSeedService.cs** — Auto-seed templates on Blueprint Service startup
14. **Blueprint Publishing Blueprint** — `blueprint-publishing-v1.json` template file
15. **Unit + integration tests** for services and endpoints

### Phase C: UI Services (depends on Phase A, partially B)

16. **SchemaFieldResolver.cs** — Parse schema fields for editor dropdowns
17. **InstructionExportService.cs** — Export/import instruction strings as JSON
18. **BlueprintLayoutService.cs** — Extend with swimlane column assignment and improved spacing

### Phase D: UI Components — Stub Fixes (minimal dependencies)

19. **BlueprintChat.razor** — Fix export to download file via JSInterop
20. **BlueprintJsonView.razor** — Fix clipboard copy via JSInterop
21. **PropertiesPanel.razor** — Add route and disclosure editing
22. **Condition/Calculation editors** — Replace hardcoded fields with SchemaFieldResolver

### Phase E: UI Components — New Features (depends on C, D)

23. **BlueprintDiagram.razor** — Unified diagram component (Edit/Preview/Compact modes)
24. **BlueprintPreview.razor** — Replace flat list with BlueprintDiagram in Preview mode
25. **BlueprintViewerDiagram.razor** — Delegate to BlueprintDiagram in Preview mode
26. **Designer.razor** — Accept `?blueprint=` query param for handoff
27. **BlueprintChat.razor** — Pass blueprint ID on "Open in Visual Designer" handoff
28. **InstructionsTab.razor** — Instructions editor in properties panel
29. **InstructionsPreview.razor** — Participant-view preview toggle
30. **Templates.razor** — Dual-source tabs (templates + published blueprints)
31. **PublishedBlueprintList.razor** — System register browser component

### Phase F: E2E Tests (depends on all above)

32. **DesignerWorkflowTests.cs** — Handoff between designers, publish flow
33. **Update existing E2E tests** — Adjust selectors for new components

## Key Files to Create

| File | Project | Purpose |
|------|---------|---------|
| `BlueprintInstructions.cs` | Sorcha.Blueprint.Models | Instructions container model |
| `InstructionSet.cs` | Sorcha.Blueprint.Models | Linked translation model |
| `BlueprintVersion.cs` | Sorcha.Blueprint.Models | Semantic version model |
| `StructuralDiffService.cs` | Sorcha.Register.Service | Hash comparison for version classification |
| `TemplateSeedService.cs` | Sorcha.Blueprint.Service | Auto-seed templates on startup |
| `SchemaFieldResolver.cs` | Sorcha.UI.Core | Parse schema properties for editors |
| `InstructionExportService.cs` | Sorcha.UI.Core | Export/import instruction strings |
| `BlueprintDiagram.razor` | Sorcha.UI.Core | Unified diagram (Edit/Preview/Compact) |
| `InstructionsTab.razor` | Sorcha.UI.Core | Instructions editor panel |
| `InstructionsPreview.razor` | Sorcha.UI.Core | Participant-view preview |
| `RouteEditor.razor` | Sorcha.UI.Core | Route CRUD in properties panel |
| `DisclosureEditor.razor` | Sorcha.UI.Core | Disclosure CRUD in properties panel |
| `PublishedBlueprintList.razor` | Sorcha.UI.Core | System register blueprint browser |
| `blueprint-publishing-v1.json` | blueprints/templates | Publishing governance workflow |

## Key Files to Modify

| File | Project | Change |
|------|---------|--------|
| `Blueprint.cs` | Sorcha.Blueprint.Models | Add Instructions, VersionMajor, VersionMinor |
| `Action.cs` | Sorcha.Blueprint.Models | Add Instructions property |
| `Control.cs` | Sorcha.Blueprint.Models | Add Instructions property |
| `Participant.cs` | Sorcha.Blueprint.Models | Add Instructions property |
| `SystemRegisterService.cs` | Sorcha.Register.Service | Version metadata in publish |
| `SystemRegisterEndpoints.cs` | Sorcha.Register.Service | Version history + classify-change endpoints |
| `Program.cs` | Sorcha.Blueprint.Service | Register TemplateSeedService |
| `BlueprintLayoutService.cs` | Sorcha.UI.Core | Swimlanes, improved spacing |
| `PropertiesPanel.razor` | Sorcha.UI.Core | Add Instructions tab, route/disclosure editing |
| `Designer.razor` | Sorcha.UI.Web.Client | Accept ?blueprint= query param |
| `BlueprintChat.razor` | Sorcha.UI.Web.Client | Pass ID on handoff, fix export download |
| `BlueprintPreview.razor` | Sorcha.UI.Web.Client | Replace with BlueprintDiagram Preview |
| `Templates.razor` | Sorcha.UI.Web.Client | Dual-source tabs |
| `BlueprintJsonView.razor` | Sorcha.UI.Core | Fix clipboard copy |

## Verification Commands

```bash
# Build all affected projects
dotnet build src/Common/Sorcha.Blueprint.Models --force
dotnet build src/Services/Sorcha.Register.Service --force
dotnet build src/Services/Sorcha.Blueprint.Service --force
dotnet build src/Apps/Sorcha.UI/Sorcha.UI.Core --force
dotnet build src/Apps/Sorcha.UI/Sorcha.UI.Web.Client --force

# Run model tests
dotnet test tests/Sorcha.Blueprint.Models.Tests

# Run service tests
dotnet test tests/Sorcha.Register.Service.Tests
dotnet test tests/Sorcha.Blueprint.Service.Tests

# Run UI tests
dotnet test tests/Sorcha.UI.Core.Tests

# Full build + test
dotnet build && dotnet test
```
