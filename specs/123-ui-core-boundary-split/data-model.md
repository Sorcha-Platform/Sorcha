# Phase 1 Data Model: UI.Core User/Admin Type-Level Boundary Refactor

**Feature**: 123-ui-core-boundary-split
**Date**: 2026-05-12

This feature introduces no new persistent entities. The "data model" here is the **structural model of the refactor**: which existing types move where, which interfaces become which narrower interfaces, and which folders absorb which files.

The verdicts in this document derive from `research.md` Phase 0 (specifically R5, R6, R8). This document is the executable plan that Phase 2 task generation reads from.

---

## Interface mapping (services)

### Bi-modal interface splits

| Original | New | Method allocation |
|---|---|---|
| `IRegisterService` *(deleted)* | `IRegisterReadService` *(new — `Services/User/`)* | `GetRegistersAsync`, `GetRegisterAsync` |
| | `IRegisterGovernanceService` *(new — `Services/Admin/`)* | `GetGovernanceRosterAsync`, `InitiateRegisterAsync`, `FinalizeRegisterAsync`, `GetPolicyAsync`, `GetPolicyHistoryAsync`, `ProposePolicyUpdateAsync`, `DisableDevModeAsync` |

`RegisterService.cs` (the concrete) implements both narrower interfaces in a single class. DI registration changes from one line to two:
```csharp
// Before
services.AddScoped<IRegisterService, RegisterService>();
// After
services.AddScoped<IRegisterReadService, RegisterService>();
services.AddScoped<IRegisterGovernanceService, RegisterService>();
```

### Non-split interface moves (audience folder reassignment)

| Interface | Original location | New location |
|---|---|---|
| `IRegisterSubscriptionService` | `Services/` | `Services/User/` |
| `IDashboardService` | `Services/` | `Services/User/` |
| `IInboxApiService` | `Services/` | `Services/User/` |
| `IDocketService` | `Services/` | `Services/User/` |
| `ITransactionService` | `Services/` | `Services/User/` |
| `IWorkflowService` | `Services/` | `Services/User/` |
| `IOfflineSyncService` | `Services/` | `Services/User/` |
| `IWalletPreferenceService` | `Services/` | `Services/User/` |
| `IOrganizationAdminService` | `Services/` | `Services/Admin/` |
| `IPlatformSettingsAdminService` | `Services/` | `Services/Admin/` |
| `IValidatorAdminService` | `Services/` | `Services/Admin/` |
| `ISystemRegisterService` | `Services/` | `Services/Admin/` |
| `IBlueprintApiService` | `Services/` | `Services/Admin/` |
| `ISchemaLibraryApiService` | `Services/` | `Services/Admin/` |
| `ITemplateApiService` | `Services/` | `Services/Admin/` |
| `IBlueprintStorageService` | `Services/` | `Services/Admin/` |
| `IHealthCheckService` | `Services/` | `Services/Admin/` |
| `IAuditService` | `Services/` | `Services/Admin/` |
| `IServicePrincipalService` | `Services/` | `Services/Admin/` |
| `IODataQueryService` | `Services/` | `Services/Admin/` |
| `IWalletAccessService` | `Services/` | `Services/Admin/` |
| `IChatHubConnection` | `Services/` | `Services/Admin/` |
| `IAlertService` | `Services/` | `Services/Shared/` |
| `IAlertDismissalService` | `Services/` | `Services/Shared/` |
| `IPayloadDecoderService` | `Services/` | `Services/Shared/` |

### New SHARED-READ interface (R7 convention)

| Interface | Purpose | Location |
|---|---|---|
| `IOrganizationReadService` *(new)* | `GetOrganizationAsync(Guid)` — used by user-facing org-card renders that need name+branding without inheriting the admin-service surface | `Services/Shared/Organization/` |

User-facing org-card consumers inject `IOrganizationReadService`. Admin consumers that need both inject `IOrganizationAdminService` AND `IOrganizationReadService` (explicit dual-inject per R7).

### Subfolder moves

| Original | New |
|---|---|
| `Services/Forms/` | `Services/User/Forms/` |
| `Services/Persona/` | `Services/User/Persona/` |
| `Services/Credentials/` | `Services/User/Credentials/` |
| `Services/AddressLookup/` | `Services/User/AddressLookup/` |
| `Services/Participants/` | `Services/User/Participants/` |
| `Services/Wallet/` | `Services/User/Wallet/` |
| `Services/Admin/` | `Services/Admin/` (flatten into parent — single folder hierarchy) |
| `Services/Designer/` | `Services/Admin/Designer/` |
| `Services/Configuration/` | `Services/Admin/Configuration/` |
| `Services/Identity/` | `Services/Shared/Identity/` |
| `Services/Navigation/` | `Services/Shared/Navigation/` |
| `Services/Http/` | `Services/Shared/Http/` |
| `Services/Authentication/` | `Services/Shared/Authentication/` |
| `Services/Encryption/` | **AUDIT at execution** — likely `Services/Shared/Encryption/` or `Services/Admin/Encryption/` depending on Layer-3 closure check |

---

## DTO extraction (R8)

| DTO | Original | New location | Audience |
|---|---|---|---|
| `OrganizationDto` | `Services/IOrganizationAdminService.cs` | `Services/Shared/Organization/OrganizationDto.cs` | SHARED |
| `BrandingDto` | same | `Services/Shared/Organization/BrandingDto.cs` | SHARED |
| `UserDto` | same | `Services/Shared/Organization/UserDto.cs` | SHARED (review at execution — may belong in `Services/Shared/Users/`) |
| `AddUserDto` | same | `Services/Admin/OrganizationAdminDtos.cs` (stays admin) | ADMIN |
| `UpdateUserDto` | same | `Services/Admin/OrganizationAdminDtos.cs` | ADMIN |
| `CreateOrganizationDto` | same | `Services/Admin/OrganizationAdminDtos.cs` | ADMIN |
| `UpdateOrganizationDto` | same | `Services/Admin/OrganizationAdminDtos.cs` | ADMIN |
| `SubdomainValidationResult` | same | `Services/Admin/OrganizationAdminDtos.cs` | ADMIN |
| `OrganizationListResult` | same | `Services/Admin/OrganizationAdminDtos.cs` | ADMIN |
| `UserListResult` | same | `Services/Admin/OrganizationAdminDtos.cs` | ADMIN |
| `PlatformKpis` | same | `Services/Admin/OrganizationAdminDtos.cs` | ADMIN |
| `SchemaOverlayFieldInfo` | `Services/BlueprintSchemaService.cs` | `Services/Shared/Blueprints/SchemaOverlayFieldInfo.cs` | SHARED |

After extraction, `IOrganizationAdminService.cs` contains only the `IOrganizationAdminService` interface declaration. Admin-only DTOs that stay co-located get split out of the interface file into a sibling `OrganizationAdminDtos.cs` for consistency (one file holds the admin interface, another holds the admin DTOs — both stay in `Services/Admin/`).

---

## Model folder mapping

| Original folder | Verdict | Action |
|---|---|---|
| `Models/Actions/` | USER | → `Models/User/Actions/` |
| `Models/Admin/` | ADMIN | → `Models/Admin/Admin/` *(or flatten into `Models/Admin/` if no name collision)* |
| `Models/Authentication/` | MIXED | Split per-file: user-side → `Models/User/Authentication/`, admin-side → `Models/Admin/Authentication/`. Audit at execution. |
| `Models/Blueprints/` | MIXED | `GovernanceRosterViewModel` → `Models/Admin/Governance/GovernanceRosterViewModel.cs`. Designer-flavoured remainder → `Models/Admin/Blueprints/`. |
| `Models/Chat/` | ADMIN | → `Models/Admin/Chat/` |
| `Models/Common/` | SHARED | → `Models/Shared/Common/` |
| `Models/Configuration/` | ADMIN | → `Models/Admin/Configuration/` |
| `Models/Credentials/` | USER | → `Models/User/Credentials/` |
| `Models/Dashboard/` | USER | → `Models/User/Dashboard/` |
| `Models/Designer/` | ADMIN | → `Models/Admin/Designer/` |
| `Models/Encryption/` | TBD | **AUDIT at execution** — likely SHARED |
| `Models/Explorer/` | ADMIN | → `Models/Admin/Explorer/` |
| `Models/Forms/` | USER | → `Models/User/Forms/` |
| `Models/Participants/` | USER | → `Models/User/Participants/` |
| `Models/Registers/` | MIXED | Split: user-facing types → `Models/User/Registers/`; admin/governance types → `Models/Admin/Registers/`. File-list in next sub-section. |
| `Models/SchemaLibrary/` | ADMIN | → `Models/Admin/SchemaLibrary/` |
| `Models/Templates/` | ADMIN | → `Models/Admin/Templates/` |
| `Models/Wallet/` | USER | → `Models/User/Wallet/` |
| `Models/Workflows/` | USER | → `Models/User/Workflows/` |

### `Models/Registers/` per-file split

| File | New location |
|---|---|
| `TransactionViewModel.cs` | `Models/User/Registers/` |
| `RegisterViewModel.cs` | `Models/User/Registers/` |
| `WalletViewModel.cs` | `Models/User/Registers/` |
| `PayloadViewModel.cs` | `Models/User/Registers/` |
| `TransactionListResponse.cs` | `Models/User/Registers/` |
| `TransactionGraphNode.cs` | `Models/User/Registers/` |
| `TransactionQueryState.cs` | `Models/User/Registers/` |
| `RegisterFilterState.cs` | `Models/User/Registers/` |
| `ConnectionState.cs` | `Models/User/Registers/` |
| `NavigationContext.cs` | `Models/User/Registers/` |
| `RegisterPolicyViewModel.cs` | `Models/Admin/Registers/` |
| `RegisterPolicyFields.cs` | `Models/Admin/Registers/` |
| `PolicyUpdateProposalViewModel.cs` | `Models/Admin/Registers/` |
| `PolicyHistoryViewModel.cs` | `Models/Admin/Registers/` |
| `RegisterCreationState.cs` | `Models/Admin/Registers/` |

### Loose top-level files in `Models/`

| File | New location |
|---|---|
| `ActivityEventDto.cs` | `Models/Shared/ActivityEventDto.cs` |
| `AuthMethodsModels.cs` | `Models/User/Authentication/AuthMethodsModels.cs` |
| `PendingActionNotificationDto.cs` | `Models/User/Actions/PendingActionNotificationDto.cs` |
| `TotpDtos.cs` | `Models/User/Authentication/TotpDtos.cs` |
| `UserPreferencesDto.cs` | `Models/User/UserPreferencesDto.cs` |
| `WalletAccessModels.cs` | `Models/Admin/Wallet/WalletAccessModels.cs` |

---

## Namespace policy

**Folder reorganisation does NOT change namespaces.** Files that move from `Sorcha.UI.Core.Services` to `Sorcha.UI.Core.Services.User.Forms` keep the namespace declared in the file header. This is achieved one of two ways depending on per-file existing state:

1. Files that explicitly declare `namespace Sorcha.UI.Core.Services;` keep that declaration. The new folder location has no namespace effect — explicit namespaces always win.
2. Files that rely on the implicit project-root-namespace + folder-path mapping need an explicit `@namespace` (Razor) or `namespace` (C#) declaration added if the folder move would change their implicit namespace.

Practically: every moved C# file should already have an explicit `namespace` declaration (Sorcha convention). Razor files (the few in `Services/`) get explicit `@namespace` directives added during the move if relying on implicit namespacing.

**Rationale**: Consumers across all six host apps reference the moved types by namespace via `using` directives. Changing namespaces would force a parallel update to every consumer's `using` block — a much larger diff than just updating the few consumers that switch from old bi-modal interface to new narrower interface.

---

## Reference graph

The refactor does not change any project reference edge. All edits are internal to `Sorcha.UI.Core`. The six host apps continue to reference `Sorcha.UI.Core` exactly as today.

```text
   Sorcha.UI.Core             (refactored internally — same name, same csproj)
        ▲
        │ (already referenced — no change)
        │
   Sorcha.UI.{Admin, App, Designer, Explorer, Web, Web.Client}
   (consumer @inject directives updated where they injected IRegisterService)
```

---

## What this feature does NOT introduce

- New persistence entities, EF migrations, or wire formats.
- New REST endpoints, gRPC services, or external APIs.
- New auth scopes, claims, or roles.
- New telemetry meters, activity sources, or hosted services.
- New third-party packages.
- New host applications or new component libraries.

Recording these here so a Phase 2 reviewer can verify scope hasn't drifted.
