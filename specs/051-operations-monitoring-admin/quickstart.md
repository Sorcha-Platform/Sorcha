# Quickstart: Operations & Monitoring Admin (Feature 051)

**Branch**: `051-operations-monitoring-admin` | **Date**: 2026-03-06

## Prerequisites

- .NET 10 SDK
- Docker Desktop (for full stack)
- Node.js (for Playwright E2E tests)

## Development Setup

```bash
# Checkout feature branch
git checkout 051-operations-monitoring-admin

# Restore and build
dotnet restore && dotnet build

# Start all services
docker-compose up -d

# Access points
# API Gateway: http://localhost:80
# Main UI: http://localhost/app
# Aspire Dashboard: http://localhost:18888
```

## Feature Scope Summary

| Area | Backend | UI | CLI | Tests |
|------|---------|-----|-----|-------|
| Dashboard auto-refresh + alerts | Exists | Modify Home.razor | N/A | Unit + E2E |
| Wallet access delegation | Exists | Add Access tab | New commands | Unit + E2E |
| Schema provider CLI | Exists | Exists | New commands | Unit |
| Events admin | Exists | New page | New commands | Unit + E2E |
| Push notifications | Exists | New settings tab | N/A | Unit + E2E |
| Encryption progress | Exists | New component | New command | Unit |
| UI reliability (status constants, error handling, structured inputs) | N/A | Modify existing | N/A | Unit |

## Key Implementation Order

1. **Credential status constants** (FR-025) — foundational, other UI work depends on consistent status references
2. **Credential lifecycle error handling** (FR-024) — improves existing services before adding new ones
3. **Dashboard auto-refresh + alerts integration** (FR-001 to FR-004) — highest visibility
4. **Wallet access delegation UI + CLI** (FR-005 to FR-009) — P1 feature
5. **Events admin page + CLI** (FR-014 to FR-017) — new page
6. **Push notification settings** (FR-018 to FR-020) — standalone settings tab
7. **Schema provider CLI** (FR-013) — CLI only, UI already done
8. **Encryption progress indicator** (FR-021 to FR-023) — requires most integration work
9. **Structured inputs + auto-refresh** (FR-026 to FR-028) — polish and code quality

## Testing Strategy

```bash
# Run all UI Core tests
dotnet test tests/Sorcha.UI.Core.Tests/

# Run CLI tests
dotnet test tests/Sorcha.Cli.Tests/

# Run E2E tests (requires Docker stack running)
dotnet test tests/Sorcha.UI.E2E.Tests/

# Run specific test class
dotnet test --filter "FullyQualifiedName~WalletAccessServiceTests"
```

## Files to Create

### UI Services (in `src/Apps/Sorcha.UI/Sorcha.UI.Core/`)
- `Models/Admin/EventAdminModels.cs` — Event admin view models
- `Models/Admin/PushNotificationModels.cs` — Push notification view models
- `Models/Admin/EncryptionOperationModels.cs` — Operation status view models
- `Models/Credentials/CredentialStatus.cs` — Status constants class
- `Services/Admin/IEventAdminService.cs` + `EventAdminService.cs`
- `Services/Admin/IPushNotificationService.cs` + `PushNotificationService.cs`
- `Services/Admin/IOperationStatusService.cs` + `OperationStatusService.cs`
- `Services/IWalletAccessService.cs` + `WalletAccessService.cs`

### UI Pages/Components (in `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/`)
- `Pages/Admin/EventsAdmin.razor` — Events admin page
- `Pages/Settings/NotificationSettings.razor` — Push notification toggle

### UI Components (in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/`)
- `Wallets/WalletAccessTab.razor` — Access delegation tab
- `Admin/EncryptionProgressIndicator.razor` — Encryption progress display

### CLI Commands (in `src/Apps/Sorcha.Cli/Commands/`)
- Add to `WalletCommands.cs` — `wallet access grant|list|revoke|check`
- Add to `AdminCommands.cs` — `admin events list|delete`
- New `SchemaCommands.cs` — `schema providers list|refresh`
- New `OperationCommands.cs` — `operation status <id>`

### Tests
- `tests/Sorcha.UI.Core.Tests/Services/EventAdminServiceTests.cs`
- `tests/Sorcha.UI.Core.Tests/Services/PushNotificationServiceTests.cs`
- `tests/Sorcha.UI.Core.Tests/Services/OperationStatusServiceTests.cs`
- `tests/Sorcha.UI.Core.Tests/Services/WalletAccessServiceTests.cs`
- `tests/Sorcha.Cli.Tests/Commands/WalletAccessCommandTests.cs`
- `tests/Sorcha.Cli.Tests/Commands/EventAdminCommandTests.cs`
- `tests/Sorcha.Cli.Tests/Commands/SchemaCommandTests.cs`
- `tests/Sorcha.Cli.Tests/Commands/OperationCommandTests.cs`

## Files to Modify

- `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/Home.razor` — Add auto-refresh timer, integrate AlertsPanel
- `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/Wallets/WalletDetail.razor` — Add Access tab
- `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/Admin/PresentationAdmin.razor` — Auto-refresh, structured inputs
- `src/Apps/Sorcha.UI/Sorcha.UI.Core/Extensions/ServiceCollectionExtensions.cs` — Register new services
- `src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/Credentials/CredentialApiService.cs` — Typed error returns
- `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Credentials/CredentialLifecycleDialog.razor` — Use status constants, display specific errors
- `src/Apps/Sorcha.Cli/Commands/WalletCommands.cs` — Add access subcommands
- `src/Apps/Sorcha.Cli/Commands/AdminCommands.cs` — Add events subcommands
