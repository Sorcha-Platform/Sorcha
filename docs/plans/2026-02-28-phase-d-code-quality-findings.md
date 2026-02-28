# Phase D: Code Quality Findings — Applications

**Date:** 2026-02-28
**Scope:** `src/Apps/` — CLI, McpServer, UI.Core, UI.Web.Client, Demo

## Summary

| Category | Found | Fixed | Deferred |
|----------|-------|-------|----------|
| Bare catch blocks | 31 | 31 | 0 |
| #region blocks | 7 files | 7 files | 0 |
| Console.WriteLine (non-CLI) | 2 files | 2 files | 0 |
| Sync-over-async | 2 | 1 | 1 |
| SemaphoreSlim without IDisposable | 1 | 1 | 0 |
| new HttpClient() without factory | 1 | 0 | 1 |

## Fixes Applied

### CLI (6 files)

- **AuthCommands.cs** — Bare `catch` → `catch (FormatException)` + `catch (JsonException)` for JWT decode
- **QueryCommands.cs** — Bare `catch` → `catch (JsonException)` for OData response parsing
- **LinuxEncryption.cs** — Bare `catch` → `catch (IOException)` for /etc/machine-id read
- **AuthenticationService.cs** — Bare `catch` → `catch (HttpRequestException)` + `catch (InvalidOperationException)` for token refresh
- **ITenantServiceClient.cs** — Removed 4 #region blocks
- **IRegisterServiceClient.cs** — Removed 7 #region blocks

### McpServer (18 files)

All 18 MCP tool files had bare `catch` blocks changed to `catch (JsonException)` for HTTP error response body parsing. These catches were already returning error strings to the AI client (not silently swallowing), but the bare catch was too broad.

Files: TenantCreateTool, TenantUpdateTool, TokenRevokeTool, UserManageTool, BlueprintDiffTool, BlueprintExportTool, BlueprintSimulateTool, BlueprintUpdateTool, BlueprintValidateTool, DisclosureAnalysisTool, WorkflowInstancesTool, ActionDetailsTool, ActionSubmitTool, DisclosedDataTool, RegisterQueryTool, WalletInfoTool, WalletSignTool, WorkflowStatusTool

### UI.Core (10 files)

- **PropertiesPanel.razor** — Removed 4 #region blocks
- **OrganizationAdminService.cs** — Removed 3 #region blocks
- **IOrganizationAdminService.cs** — Removed 3 #region blocks
- **Profile.cs** — Removed 2 #region blocks
- **PreviousDataPanel.razor** — Bare catch documented as resilience pattern
- **CredentialApiService.cs** — Added resilience comments to 2 catch blocks
- **AuthenticationService.cs** — 3 bare catches → `catch (Exception)` with documentation; `IsAuthenticated` → `IsAuthenticatedAsync` (sync-over-async fix)
- **IAuthenticationService.cs** — Interface updated for IsAuthenticatedAsync
- **CustomAuthenticationStateProvider.cs** — Bare catch → `catch (Exception)` with comment
- **FormSchemaService.cs** — Bare catch → `catch (ArgumentException)` for regex parsing
- **ConfigurationService.cs** — Added IDisposable for SemaphoreSlim

### UI.Web.Client (4 files)

- **Detail.razor** — 2 Console.WriteLine → ILogger.LogWarning; bare catch → `catch (Exception)`
- **Designer.razor** — 9 Console.WriteLine → structured ILogger calls
- **MainLayout.razor** — Bare catch → `catch (Exception)` with comment
- **BlueprintPreview.razor** — 2 bare catches → `catch (Exception)` with comments

### Demo (3 files)

- **BlueprintApiClient.cs** — Removed 2 #region blocks
- **ApiClientBase.cs** — Bare catch → `catch (Exception ex)` with Console.Error.WriteLine
- **BlueprintFlowExecutor.cs** — Bare catch → `catch (Exception ex)` with Console.Error.WriteLine

## Deferred Issues

| Issue | Location | Reason |
|-------|----------|--------|
| Sync-over-async `.GetAwaiter().GetResult()` | CLI `BaseCommand.cs:85` | Requires changing base class contract; separate refactoring task |
| `new HttpClient()` without factory | CLI `ConfigCommand.cs:327` | Short-lived connectivity probe, properly disposed, static context |
| Duplicate `ErrorResponse` classes | McpServer 4 admin tools | Code smell, not a quality bug; consolidation is separate work |
| `ServiceState` thread-safety | McpServer `GracefulDegradation.cs` | Benign race condition; proper fix requires substantial refactoring |
