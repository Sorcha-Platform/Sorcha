# Quickstart: AI Blueprint Builder Enhancement

**Branch**: `063-ai-builder-schemas-vc` | **Date**: 2026-03-18

## Implementation Order

This feature has 4 workstreams that should be implemented in this order due to dependencies:

### Phase A: UI Fixes (no dependencies, quick win)
1. Fix `ChatPanel.razor` — ensure `input-area` has `flex-shrink: 0` and is truly pinned
2. Add `chat-scroll.js` — IntersectionObserver-based auto-scroll with sentinel element
3. Wire JS interop in `ChatPanel.razor` for auto-scroll on `OnAfterRenderAsync`
4. Verify height calculation in `BlueprintChat.razor` container

### Phase B: Schema Library (foundation for everything else)
1. Create `blueprints/schemas/` directory structure with category subdirectories
2. Author all 26 schema JSON files (19 data schemas + 7 credential schemas)
3. Create `SchemaSeedService` following `TemplateSeedService` pattern
4. Register `SchemaSeedService` in `Program.cs`
5. Write tests for `SchemaSeedService`

### Phase C: AI Tools & System Prompt (depends on Phase B)
1. Add `search_schemas` tool to `BlueprintToolExecutor`
2. Add `use_standard_schema` tool to `BlueprintToolExecutor`
3. Add `require_credential` tool to `BlueprintToolExecutor`
4. Add `issue_credential` tool to `BlueprintToolExecutor`
5. Add `search_templates` tool to `BlueprintToolExecutor`
6. Rewrite system prompt in `ChatOrchestrationService`
7. Make `BuildSystemPrompt()` dynamic — inject schema/template summaries
8. Write tests for all 5 new tools
9. Write tests for system prompt generation

### Phase D: Preview Enhancements (depends on Phase C)
1. Add credential badges to `BlueprintPreview.razor` action cards
2. Write E2E tests for chat UI layout

## Key Files to Modify

| File | Change |
|------|--------|
| `src/Services/Sorcha.Blueprint.Service/Services/SchemaSeedService.cs` | NEW |
| `src/Services/Sorcha.Blueprint.Service/Services/ChatOrchestrationService.cs` | System prompt rewrite + dynamic building |
| `src/Services/Sorcha.Blueprint.Service/Services/BlueprintToolExecutor.cs` | 5 new tools |
| `src/Services/Sorcha.Blueprint.Service/Program.cs` | Register SchemaSeedService |
| `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Components/Chat/ChatPanel.razor` | Fixed input + auto-scroll |
| `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Components/Chat/BlueprintPreview.razor` | Credential badges |
| `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/BlueprintChat.razor` | Height calc fix |
| `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/wwwroot/js/chat-scroll.js` | NEW: auto-scroll JS |

## Testing Strategy

| Test Type | Target | Count |
|-----------|--------|-------|
| Unit | SchemaSeedService (seed, skip, error) | ~8 |
| Unit | search_schemas tool (by query, by category, no results) | ~5 |
| Unit | use_standard_schema tool (apply, merge, missing schema, missing action) | ~5 |
| Unit | require_credential tool (add, with issuers, with claims, missing action) | ~5 |
| Unit | issue_credential tool (configure, mappings, missing action) | ~5 |
| Unit | search_templates tool (by query, by category, no results) | ~4 |
| Unit | System prompt generation (includes schemas, includes templates, dynamic) | ~4 |
| Unit | Credential validation in validate_blueprint | ~4 |
| E2E | Chat UI layout (input pinned, auto-scroll, viewport fill) | ~3 |
| **Total** | | **~43** |

## Build & Test

```bash
# Build the solution
dotnet build --force

# Run Blueprint Service tests
dotnet test tests/Sorcha.Blueprint.Service.Tests/

# Run UI Core tests
dotnet test tests/Sorcha.UI.Core.Tests/

# Run E2E tests (requires Docker)
dotnet test tests/Sorcha.UI.E2E.Tests/ --filter "BlueprintChat"
```
