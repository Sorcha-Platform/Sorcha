# Contracts — AI Designer Unified Shell

**Branch**: `109-designer-shell-redesign`
**Date**: 2026-04-21

## No new API contracts

This feature is a **pure UI refactor**. No new REST endpoints, no new gRPC services, no new SignalR hubs, no new message shapes on existing hubs.

| Interaction | Direction | Uses |
|---|---|---|
| Load a blueprint by ID | Client → Blueprint Service | Existing `IBlueprintApiService.GetAsync(id)` |
| Save a blueprint | Client → Blueprint Service | Existing `IBlueprintApiService.SaveAsync(bp)` |
| List saved blueprints (for Load dialog) | Client → Blueprint Service | Existing `IBlueprintApiService.ListAsync()` |
| Export blueprint JSON/YAML | Client-side serialisation | Existing `BlueprintSerializationService` |
| Create chat session | Client → Blueprint Service | Existing `IChatHubConnection.CreateSessionAsync` |
| Receive AI-generated blueprint update | Blueprint Service → Client | Existing hub event `OnBlueprintUpdated(Blueprint, ValidationResult)` |
| Receive AI chat message | Blueprint Service → Client | Existing hub events `OnMessageReceived`, `OnMessageComplete`, `OnMessageLimitWarning` |
| Validate blueprint | Client → Blueprint Service | Existing `IBlueprintApiService.ValidateAsync(bp)` (already called today on Diagram edits) |

## What DOES change

- One new component parameter on `SorchaFormRenderer`: `[Parameter] public bool PreviewMode { get; set; } = false;` — see design doc §R6 and `Panes/FormPreviewPane.razor` usage.
- One new `[JSInvokable]` test hook on `AiDesignerPane` under `#if DEBUG || E2E_TEST_HOOKS` to let Playwright inject synthetic SignalR events — see research.md §R3.

Neither of these is a backwards-incompatible change:
- `PreviewMode` defaults to `false`, preserving existing renderer behaviour for all existing callers.
- The test hook is compile-guarded and absent from release builds.

## Why this file exists at all

The speckit `/contracts/` directory is conventionally the home for new OpenAPI/GraphQL/gRPC specs a feature introduces. For UI-only features with no new server-side surface, the convention is to leave this README explaining the absence, so tasks generation and review can confirm the feature really doesn't have backend contract work to do.
