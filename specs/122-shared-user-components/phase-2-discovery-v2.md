# Phase 2 Discovery v2 — Audience-Folder Split Is Necessary but Not Sufficient

**Date:** 2026-05-13
**Status:** Second Phase 2 attempt rolled back; Feature 122 remains parked on Phase 1.
**Outcome:** Roll-back complete on top of merge with master (which carried Feature 123). Working tree clean. Plan needs a new pre-migration step.

## What was attempted

After Feature 123 merged (PR #641), the 122 branch was rebased onto master and Phase 2's atomic move re-attempted using folder-level wholesale moves consistent with F123's audience classification:

- `Services/User/*` → `Sorcha.UI.Components.User/Services/User/`
- `Services/Shared/*` → `Sorcha.UI.Components.User/Services/Shared/`
- `Models/User/*` → `Sorcha.UI.Components.User/Models/User/`
- `Models/Shared/*` → `Sorcha.UI.Components.User/Models/Shared/`
- UI.Core gains `ProjectReference` → `Sorcha.UI.Components.User`
- Components.User csproj gains `<RootNamespace>Sorcha.UI.Core</RootNamespace>` plus the packages user-facing code needs (`Blazored.LocalStorage`, `SignalR.Client`, `JWT`, `QRCoder`, `SimpleBase`).

Result: **180 compiler errors** against the new library standalone.

## Error pattern

Top missing-namespace / missing-type frequencies (CS0234 + CS0246, n=180):

| Count | Missing symbol | Source location |
|------:|----------------|-----------------|
|    24 | `IConfigurationService` | `Services/Admin/Configuration/` |
|    18 | `SyncQueueItem` | `Models/Admin/Designer/` |
|    16 | `Admin` (namespace) | top-level `Models/Admin/*.cs` |
|    14 | `DocketViewModel` | `Models/Admin/Explorer/` |
|    14 | `Configuration` (namespace) | `Models/Admin/Configuration/` |
|    12 | `ServiceAlert` | `Models/Admin/` (top-level alert types) |
|    12 | `Extensions` (namespace) | `Extensions/` top-level UI.Core |
|    12 | `EncryptionSignal` | `Models/Admin/Encryption/` |
|    10 | `AlertsResponse` | `Models/Admin/` |
|     4 | `Profile`, `SyncOperation`, `IOperationStatusService`, `Explorer`, `Designer`, `BlueprintConflict` | various Admin folders |
|     2 | `Utilities`, governance view models (`RegisterPolicyViewModel`, `RegisterPolicyFields`, `PolicyUpdateProposalViewModel`, `PolicyHistoryViewModel`) | UI.Core top-level + `Models/Admin/Registers/` |

## What this means

Feature 123's audience-folder split was a real improvement (it eliminated the original bi-modal `IRegisterService` problem and pulled `OrganizationDto`/`BrandingDto`/`SchemaOverlayFieldInfo` out of admin-service files) but it classified at the folder level without following every type dependency. Several services that F123 placed in `Services/Shared/` (alerts, encryption tracking, register service) and `Services/User/` (docket service, offline sync, configuration-aware credential services) still reach into the Admin namespace for individual types.

Concretely, the surface that needs further work:

1. **`IConfigurationService`** is in `Services/Admin/Configuration/` but every user-facing service that needs `appsettings`-derived values injects it. Either:
   - Split into `IUserConfigurationService` (user surface — endpoint URLs, feature flags) + `IAdminConfigurationService` (admin-only settings), or
   - Move the entire `Configuration` service to `Services/Shared/`, accepting that user code needs it.

2. **Alert types** (`ServiceAlert`, `AlertsResponse`) live in `Models/Admin/` but are consumed by `Services/Shared/AlertService.cs` and `Services/Shared/AlertDismissalService.cs` (which F123 classified as Shared). Either reclassify the alert services as Admin, or extract the user-visible alert types to a shared location.

3. **`DocketViewModel` + `SyncQueueItem` + `BlueprintConflict`** are in `Models/Admin/Explorer/` and `Models/Admin/Designer/` but consumed by `Services/User/DocketService.cs` and `Services/User/OfflineSyncService.cs`. These services are arguably admin-flavoured (docket = block explorer; offline-sync = designer authoring); reclassify them as Admin.

4. **`EncryptionSignal`** in `Models/Admin/Encryption/` is consumed by `Services/Shared/EncryptionOperationTracker.cs`. Either reclassify the tracker as Admin or extract the signal type.

5. **Governance view models** (`RegisterPolicyViewModel`, `RegisterPolicyFields`, `PolicyUpdateProposalViewModel`, `PolicyHistoryViewModel`) in `Models/Admin/Registers/` are still reached by `Services/Shared/RegisterService.cs`. The F123 split of `IRegisterService` was at the interface level; the concrete `RegisterService` apparently still implements both halves and reaches for governance types. Either split the implementation, or recognise that `RegisterService.cs` is admin-only and move it from `Services/Shared/` to `Services/Admin/`.

6. **Top-level `Sorcha.UI.Core.Extensions` and `Sorcha.UI.Core.Utilities`** never moved during F123. User-facing services like `CredentialApiService`, `HaipOfferService`, `IssuedCredentialService`, `InboxApiService`, `RegisterSubscriptionService`, `WorkflowService` all `using Sorcha.UI.Core.Extensions;` and Navigation needs `Sorcha.UI.Core.Utilities;`. These need to be either moved into Components.User (if their content is user-facing) or audited for audience and split.

## Decision

Feature 122 stays parked on Phase 1. A new feature is required, ahead of Phase 2:

**Feature 124 — UI.Core type-level coupling fixes for Components.User migration.** Scope: surgical fixes for the six categories above. Output: when complete, the atomic move described in `tasks.md` Phase 2 produces zero build errors against the new library.

This is smaller than Feature 123 was — most of the work is reclassification (move services from Shared/ to Admin/ where they consume Admin types, e.g., AlertService, DocketService, OfflineSyncService, RegisterService) plus two small extractions (alert types, encryption signal) plus an Extensions/Utilities audit. Estimated ~10–15 commits, similar shape to F123 but tighter scope.

## What is preserved on this branch

- `654744f9` — Phase 1 scaffold (intact, builds clean)
- `6761768a` — original "blocked on F123" docs (history)
- `5b87b9e1` — merge of master (F123) into 122
- `65aa0dc4` — research refresh (R1' verdict tables)
- This document — captures the second blocker

Phase 2 task list in `tasks.md` is structurally still valid; what was wrong was the *prerequisite*, not the *plan*. When Feature 124 lands, Phase 2 resumes with the same wholesale-folder-move approach, and the verdicts in `research.md` R1' need a small refresh to account for any services Feature 124 reclassifies from Shared/ to Admin/.

## Recommendation for the next session

Don't attempt Phase 2 a third time without first running the new-library standalone build against UI.Core's current shape. The 180-error report from this session is the fastest way to scope Feature 124 — every error points at one specific reclassification or extraction decision. Capture the error list from `dotnet build src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Sorcha.UI.Components.User.csproj` after re-doing the moves (then immediately rollback) and use it as Feature 124's task source.

The third-time-lucky version of Phase 2 should produce a clean build on the first try.
