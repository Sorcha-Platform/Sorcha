# Research: Designer & Blueprint Instructions Upgrade

**Feature**: 059-designer-blueprint-upgrade
**Date**: 2026-03-16

## R1: Unified Diagram Component Strategy

**Decision**: Create a single `BlueprintDiagram.razor` component in `Sorcha.UI.Core` with a `Mode` parameter (Edit, Preview, Compact). Internally composes the existing `BlueprintLayoutService` for positioning and `BlazorDiagram` for rendering.

**Rationale**: The codebase already has two separate rendering paths — `BlueprintViewerDiagram` (read-only auto-layout) and `ActionNodeWidget` in the visual designer (edit-mode). The AI chat `BlueprintPreview` is a third path using a flat timeline. Unifying reduces maintenance and ensures visual consistency. The `Mode` parameter controls:
- `Edit`: Unlocked nodes, toolbar buttons, selection events (wraps existing ActionNodeWidget)
- `Preview`: Locked nodes, auto-layout via Sugiyama, no toolbar (wraps existing ReadOnlyActionNodeWidget)
- `Compact`: Simplified nodes (title-only, no detail summary), smaller spacing, for embedding in cards/chat

**Alternatives considered**:
- Keep three separate components: Rejected — divergent rendering, triple maintenance burden.
- SVG-only rendering (no Blazor.Diagrams): Rejected — would lose drag/drop editing capability.

## R2: Structural Diff Detection for Versioning

**Decision**: Compute a SHA-256 hash of the blueprint JSON with the `instructions` property stripped at all levels (blueprint, action, control, participant). Compare hashes of old and new versions. If identical, change is documentation-only (minor bump). If different, change is structural (major bump).

**Rationale**: The blueprint model is a JSON document. Stripping `instructions` at all levels and hashing the remainder gives a deterministic structural fingerprint. This is simpler and more reliable than comparing individual fields, which would need updating whenever new structural fields are added.

**Implementation**:
1. Deep-clone the blueprint JSON
2. Remove `instructions` property at root, within each action, each control (recursive), and each participant
3. Serialize to canonical JSON (sorted keys, no whitespace)
4. SHA-256 hash
5. Compare with previous version's structural hash (stored in register metadata)

**Alternatives considered**:
- Field-by-field comparison: Rejected — fragile, must be updated when model changes.
- User manually selects version type: Rejected — error-prone, defeats automation purpose.

## R3: Instructions Model Placement

**Decision**: Add `BlueprintInstructions` as a top-level property on `Blueprint`, plus individual `Instructions` string properties on `Action`, `Control`, and `Participant`. The top-level model holds overview text, locale, per-action instructions (dictionary), per-participant instructions (dictionary), and linked instruction sets.

**Rationale**: Dual approach serves different needs:
- Top-level `BlueprintInstructions` provides the overview, locale, translation links, and a central editing target
- Per-entity `Instructions` properties enable the rendering chain: explicit instruction > schema fallback > nothing
- `[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]` ensures backwards compatibility — old blueprints without instructions deserialize cleanly

**Alternatives considered**:
- Instructions only at top level (dictionary keyed by action ID): Rejected — forces indirection when rendering individual actions.
- Instructions only at entity level: Rejected — no place for overview, locale, or translation links.

## R4: Schema Description Extraction for Field Help

**Decision**: Create `SchemaFieldResolver` service that parses `JsonDocument` data schemas to extract property names, types, and `description` fields. Returns `List<SchemaField>` with `Name`, `Type`, `Description`, `IsRequired`.

**Rationale**: JSON Schema standard already supports `description` at the property level. The DPP schemas (Battery Pass, Catena-X, UNTP) include rich descriptions. Extracting these provides free help text without any authoring effort. The resolver also feeds the condition/calculation editors (replacing hardcoded field names).

**Implementation**: Parse `properties` object from each schema, extract `description` from each property, merge across multiple schemas bound to an action. Handle nested `$ref` by resolving within the schema document.

## R5: Template Auto-Seeding Strategy

**Decision**: Create a `TemplateSeedService` (IHostedService) in Blueprint Service that loads all JSON files from `blueprints/templates/` on startup and upserts them into the in-memory document store. Idempotent — existing templates with the same ID are updated only if version is higher.

**Rationale**: Currently the in-memory store is empty on startup. The template JSON files exist but require manual POST API calls. Auto-seeding makes the catalogue useful immediately. The bootstrapper in Register Service already demonstrates the pattern (loading JSON from file, checking existence, publishing if missing).

**File discovery**: Scan `{AppContext.BaseDirectory}/blueprints/templates/*.json` (same path pattern as SystemRegisterBootstrapper).

## R6: Blueprint Publishing Blueprint Design

**Decision**: Create `blueprint-publishing-v1.json` as a standard Sorcha blueprint template with 3 participants (Author, Reviewer, Publisher) and 5 actions:

1. **Submit Draft** (Author, starting action) — Attach blueprint JSON + instructions. Data schema: blueprint submission schema.
2. **Classify Change** (System/Author) — Automatic structural diff. Routes: structural → action 3, documentation-only → action 4.
3. **Full Review** (Reviewer) — Review blueprint structure + instructions. Routes: approve → action 5, reject → action 1 (cycle).
4. **Documentation Review** (Reviewer) — Lighter review for instruction-only changes. Routes: approve → action 5, reject → action 1 (cycle).
5. **Sign & Publish** (Publisher) — Sign with wallet, publish to system register. Terminal action.

**Rationale**: Uses the platform's own routing, signing, disclosure, and cycle capabilities. The structural diff classification (action 2) uses a calculation to determine change type. Rejection cycles back to action 1 (resubmission). The dual review path (full vs documentation) demonstrates conditional routing based on computed data.

**Alternatives considered**:
- Single review path for all changes: Rejected — documentation-only changes need lighter governance.
- 4 participants (separate classifier role): Rejected — classification is automated, doesn't need a human role.

## R7: Context Handoff Between Designers

**Decision**: Both designers accept a `?blueprint={id}` query parameter. On load, they fetch the blueprint from the Blueprint API by ID. The AI chat designer already supports `ExistingBlueprintId` as a route parameter — extend this to also work as a query param. The visual designer's `Designer.razor` adds query param parsing on init.

**Rationale**: The blueprint API is the shared persistence layer. Both designers already save to it. The only missing piece is loading by ID on navigation. Using a query parameter (not route parameter) avoids breaking existing routes.

**Implementation**:
- AI Chat: Already has `/designer/chat/{ExistingBlueprintId}` — keep this and also parse `?blueprint=` query param.
- Visual Designer: Add `[SupplyParameterFromQuery]` for `blueprint` parameter. On init, if present, call `BlueprintApiService.GetBlueprintDetailAsync(id)`.
- "Open in Visual Designer" button: Change `Href="designer"` to `Href=$"designer?blueprint={blueprintId}"`.
- "Open in AI Chat" button: Add to visual designer toolbar, navigates to `/designer/chat/{blueprintId}`.

## R8: Swimlane Layout Enhancement

**Decision**: Extend `BlueprintLayoutService` to assign horizontal positions based on sender participant. Each unique sender gets a column (swimlane). Actions with the same sender are vertically stacked in their lane. Cross-lane edges show directional arrows.

**Rationale**: Current layout places all nodes in a single column or simple grid. Swimlanes make the participant ownership of each action visually obvious, which is critical for multi-participant workflows.

**Implementation**: After BFS layer assignment, group actions by sender. Assign each sender a column index. Position: `X = StartXOffset + senderColumnIndex * HorizontalSpacing`. Keep vertical position from layer assignment. Add participant header labels above each column.

## R9: Markdown Rendering in Instructions

**Decision**: Use `Markdig` library (already a transitive dependency via MudBlazor) to render instruction Markdown to HTML in the UI. Sanitize output to prevent XSS (strip `<script>`, event handlers).

**Rationale**: MudBlazor uses Markdig internally. Adding direct Markdig rendering for instruction text is zero-dependency-cost. Sanitization is critical since instruction text could come from imported files or external sources.

## R10: Instruction Export/Import Format

**Decision**: Export as JSON with structure:
```json
{
  "blueprintId": "...",
  "locale": "en-GB",
  "exportedAt": "2026-03-16T...",
  "strings": {
    "blueprint.overview": "This workflow...",
    "action.0.instructions": "Fill in all required...",
    "action.1.instructions": "Review the submission...",
    "control.action0./fieldName.instructions": "Enter the...",
    "participant.applicant.instructions": "You are the..."
  }
}
```

**Rationale**: JSON is native to the platform, human-readable, and easily diff-able. Flat key-value structure with dot-notation keys makes it simple for translators to work with. CSV alternative rejected because multiline Markdown content doesn't map well to CSV cells.
