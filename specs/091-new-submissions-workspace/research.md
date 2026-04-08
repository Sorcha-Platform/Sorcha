# Research: New Submissions & Action Workspace

**Feature**: 091-new-submissions-workspace
**Date**: 2026-04-08

## R-001: Schema Extension Parsing Pattern

**Decision**: Follow the `FileSchemaExtension.TryParseFromSchema(JsonElement, out T?)` pattern for parsing `x-pages`, `x-sections`, `x-width`, and `x-introduction` from action schemas.

**Rationale**: This is the established codebase convention (Feature 085). Uses `JsonElement.TryGetProperty()` for extraction, `JsonSerializer.Deserialize<T>()` for hydration. Consumers call a static `TryParse` method — no dependency injection needed.

**Alternatives considered**:
- Parse during blueprint publishing (rejected: adds complexity to publish pipeline, x-extensions are UI-only concerns)
- Parse in SorchaFormRenderer directly (rejected: mixing parsing logic with rendering, not testable in isolation)

## R-002: JSON Type for Schema Layout Models

**Decision**: Use `string[]` for field references, `string` for simple properties, and nested record types for sections. Do not use `JsonNode` or `JsonDocument` for the layout models themselves.

**Rationale**: The layout extensions are structured data with known shapes — they map cleanly to C# records. Unlike `Action.Condition` (arbitrary JSON Logic) or `Control.Schema` (arbitrary JSON Schema), pages and sections have a fixed structure. Using typed records gives compile-time safety, IntelliSense, and testability. The `FileSchemaExtension` precedent confirms this pattern — it's a typed class, not raw JSON.

**Alternatives considered**:
- `JsonNode` for flexibility (rejected: over-engineering for a fixed schema, loses type safety)
- `JsonDocument` (rejected: immutable document semantics don't fit mutable layout config)

## R-003: Form Rendering Integration

**Decision**: Extend `SorchaFormRenderer` with new parameters rather than replacing it. Add `OnFieldFocused` callback for help panel integration. Wizard/section logic lives in new parent components (`ActionWorkspace`, `FormSection`) that compose `SorchaFormRenderer`.

**Rationale**: `SorchaFormRenderer` already handles field rendering, validation, file uploads, credential gates, calculated fields, and disclosure filtering. These are complex and well-tested. The new wizard/section layout is an orchestration concern that wraps the existing renderer — not a replacement.

**Key integration points**:
- `SorchaFormRenderer` renders a subset of fields (current page/section) — filter via `Action.DataSchemas`
- New `OnFieldFocused` event propagated through `FormContext` cascading value
- Validation scoped to current page's `required` fields subset

**Alternatives considered**:
- Fork SorchaFormRenderer (rejected: duplicates 500+ lines of complex form logic)
- Build a new renderer from scratch (rejected: loses file upload, credentials, calculated fields, disclosure)

## R-004: Navigation State Between Pages

**Decision**: Implement `NavigationStateService` as a scoped service with typed `Set<T>/Get<T>` methods. `Get` removes the entry (one-shot).

**Rationale**: Blazor WASM scoped services live for the tab/circuit lifetime — perfect for transient navigation state. One-shot removal prevents stale data. This is simpler than URL state (blueprint data is too large for query strings) and more reliable than static fields (which survive beyond intended scope).

**Alternatives considered**:
- URL query parameters (rejected: published blueprint data is too large)
- Static service (rejected: lives beyond tab scope, stale data risk)
- Browser sessionStorage (rejected: requires serialisation, slower, JS interop overhead)

## R-005: Existing Layout Infrastructure

**Decision**: The existing `LayoutTypes` enum (`VerticalLayout`, `HorizontalLayout`, `Group`, `Categorization`) and `Control` model are for the explicit form system (`Action.Form`). The new `x-sections` layout is separate and independent — it controls how auto-generated form fields are grouped, not how explicit form controls are arranged.

**Rationale**: `x-sections` and `x-pages` operate on `properties` from the JSON Schema (auto-generated form), while `LayoutTypes` operates on `Control.Elements` (explicit form). They serve different purposes and should not share enums or models to avoid confusion.

**Alternatives considered**:
- Reuse `LayoutTypes` enum (rejected: semantically different — `Group` and `Categorization` don't map to section layout modes)
- Extend `Control` model with page/section concepts (rejected: conflates two separate rendering pipelines)

## R-006: Wallet Selection in Action Workspace

**Decision**: Use `WalletPreferenceService.GetSmartDefaultAsync()` for initial wallet selection. Show inline compact display with "change" link that expands to wallet picker.

**Rationale**: Matches the existing pattern used in `NewSubmissionDialog` and `MyWorkflows`. The smart default handles single-wallet auto-select, stored preference lookup, and fallback gracefully.

## R-007: Instance Creation API Contract

**Decision**: Use the existing two-step API — `POST /api/instances` then `POST /api/instances/{id}/actions/{actionId}/execute`. No new backend endpoints needed.

**Rationale**: The API contract is well-established and used by the current `NewSubmissionDialog`. The workspace just needs to call the same methods on `IWorkflowService` with the correct parameters. The `ActionExecuteRequest` record provides all required fields.

**Key detail**: The starting action is always `Action[0]` (index 0) in the blueprint's actions list. Its `Id` property is used as the `actionId` parameter.
