# Implementation Plan: Identity & Credentials Admin

**Branch**: `050-identity-credentials-admin` | **Date**: 2026-03-05 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/050-identity-credentials-admin/spec.md`

## Summary

Add admin UI components and CLI commands for credential lifecycle management (suspend/reinstate/revoke/refresh), participant identity operations (suspend/reactivate/publish), W3C Bitstring status list viewing, and OID4VP verifiable presentations (holder + verifier). All backend endpoints exist; this feature builds the user-facing layer.

## Technical Context

**Language/Version**: C# 13 / .NET 10
**Primary Dependencies**: MudBlazor (UI components), System.CommandLine 2.0.2 (CLI), Refit (HTTP clients), Moq + FluentAssertions (testing)
**Storage**: N/A (all data via existing backend API endpoints)
**Testing**: xUnit + FluentAssertions + Moq (unit), Playwright (E2E)
**Target Platform**: Blazor WASM (UI) + cross-platform CLI
**Project Type**: Multi-project solution (existing)
**Performance Goals**: UI interactions complete in < 2s, CLI commands complete in < 5s
**Constraints**: No new backend endpoints needed, no database changes, no new NuGet packages (except possibly QR code generation)
**Scale/Scope**: ~10 new Blazor components/dialogs, 8 CLI commands, ~90 tests

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Microservices-First | PASS | No new services; UI/CLI call existing endpoints via API Gateway |
| II. Security First | PASS | All operations require auth; wallet picker ensures explicit authorization |
| III. API Documentation | PASS | No new API endpoints; existing endpoints already documented |
| IV. Testing Requirements | PASS | Target >85% coverage with ~90 tests |
| V. Code Quality | PASS | Follows existing patterns (Feature 049), C# 13, nullable enabled |
| VI. Blueprint Creation | N/A | No blueprint changes |
| VII. Domain-Driven Design | PASS | Uses correct terminology: Participant, Credential, Disclosure |
| VIII. Observability | PASS | UI services use ILogger; no new backend logging needed |

No violations. No complexity tracking needed.

## Project Structure

### Documentation (this feature)

```text
specs/050-identity-credentials-admin/
├── spec.md
├── plan.md              # This file
├── research.md          # Phase 0: technology decisions
├── data-model.md        # Phase 1: view models and entities
├── quickstart.md        # Phase 1: integration scenarios
├── contracts/           # Phase 1: UI service and CLI contracts
│   ├── credential-lifecycle-service.md
│   ├── participant-admin-service.md
│   ├── status-list-service.md
│   ├── presentation-admin-service.md
│   ├── credential-cli-commands.md
│   └── participant-cli-commands.md
└── checklists/
    └── requirements.md
```

### Source Code (repository root)

```text
# UI Components (Blazor WASM)
src/Apps/Sorcha.UI/Sorcha.UI.Core/
├── Components/
│   ├── Credentials/
│   │   ├── CredentialDetailView.razor      # MODIFY: add lifecycle action buttons
│   │   ├── CredentialLifecycleDialog.razor  # NEW: wallet picker + action confirmation
│   │   ├── PresentationRequestList.razor    # NEW: holder pending requests
│   │   ├── PresentationRequestDetail.razor  # NEW: holder review + approve/deny
│   │   └── PresentationSubmitDialog.razor   # NEW: selective disclosure checkboxes
│   └── Participants/
│       ├── ParticipantDetail.razor          # MODIFY: add suspend/reactivate/publish buttons
│       ├── ParticipantList.razor            # MODIFY: add status chips
│       └── ParticipantPublishDialog.razor   # NEW: publish form with wallet selection
├── Models/
│   ├── Credentials/
│   │   ├── CredentialLifecycleRequest.cs    # NEW: suspend/reinstate/refresh request models
│   │   ├── StatusListViewModel.cs           # NEW: status list display model
│   │   └── PresentationAdminModels.cs       # NEW: verifier-side models
│   └── Participants/
│       └── PublishParticipantViewModel.cs    # EXISTS: verify/extend if needed
├── Services/
│   ├── Credentials/
│   │   ├── ICredentialApiService.cs         # MODIFY: add lifecycle methods
│   │   ├── CredentialApiService.cs          # MODIFY: implement lifecycle methods
│   │   ├── IStatusListService.cs            # NEW: status list viewer service
│   │   ├── StatusListService.cs             # NEW: implementation
│   │   ├── IPresentationAdminService.cs     # NEW: verifier-side service
│   │   └── PresentationAdminService.cs      # NEW: implementation
│   └── Participants/
│       ├── IParticipantPublishingService.cs # EXISTS: already complete
│       └── ParticipantPublishingService.cs  # EXISTS: already complete

# Admin UI Pages
src/Apps/Sorcha.Admin/Sorcha.Admin.Client/
├── Pages/
│   ├── StatusLists.razor                    # NEW: /admin/status-lists page
│   └── PresentationAdmin.razor              # NEW: /admin/presentations page

# CLI Commands
src/Apps/Sorcha.Cli/
├── Commands/
│   ├── CredentialCommands.cs                # MODIFY: add suspend/reinstate/refresh/status-list
│   └── ParticipantCommands.cs               # MODIFY: add suspend/reactivate/publish/unpublish
├── Services/
│   ├── ICredentialServiceClient.cs          # MODIFY: add Refit methods
│   └── IParticipantServiceClient.cs         # MODIFY: add Refit methods

# Tests
tests/
├── Sorcha.UI.Core.Tests/Services/
│   ├── CredentialApiServiceLifecycleTests.cs    # NEW
│   ├── StatusListServiceTests.cs                # NEW
│   └── PresentationAdminServiceTests.cs         # NEW
├── Sorcha.Cli.Tests/Commands/
│   ├── CredentialLifecycleCommandTests.cs       # NEW
│   ├── CredentialStatusListCommandTests.cs      # NEW
│   ├── ParticipantSuspendCommandTests.cs        # NEW
│   └── ParticipantPublishCommandTests.cs        # NEW
└── Sorcha.UI.E2E.Tests/
    ├── CredentialLifecycleTests.cs              # NEW
    ├── ParticipantLifecycleTests.cs             # NEW
    └── PresentationFlowTests.cs                 # NEW
```

**Structure Decision**: Follows existing project structure exactly. No new projects needed — all work fits into existing Sorcha.UI.Core, Sorcha.Admin.Client, Sorcha.Cli, and their test projects.
