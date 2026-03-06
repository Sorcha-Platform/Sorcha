# Research: Operations & Monitoring Admin (Feature 051)

**Branch**: `051-operations-monitoring-admin` | **Date**: 2026-03-06

## 1. Dashboard & Alerts — Current State

### Decision: Wire existing services, add auto-refresh and alerts panel to Home page
### Rationale
The Home dashboard (`Sorcha.UI.Web.Client/Pages/Home.razor`) already calls `IDashboardService.GetDashboardStatsAsync()` which fetches from `/api/dashboard`. The `DashboardStatisticsService` in the API Gateway aggregates stats from all services. The gap is:
- No auto-refresh (stats are fetched once on page load)
- AlertsPanel component exists (`Sorcha.UI.Core/Components/Admin/AlertsPanel.razor`) but is not integrated into the Home page
- Alert dismissal is not implemented (per-user, per clarification)

### Alternatives Considered
- SignalR push for dashboard stats: Rejected — polling every 30s is simpler and sufficient for admin dashboards
- Custom dashboard service: Rejected — `DashboardStatisticsService` and `AlertAggregationService` already exist in the API Gateway

### Key Files
- `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/Home.razor` — Dashboard page
- `src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/IDashboardService.cs` / `DashboardService.cs`
- `src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/IAlertService.cs` / `AlertService.cs`
- `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Admin/AlertsPanel.razor`
- `src/Apps/Sorcha.UI/Sorcha.UI.Core/Models/Admin/ServiceAlertModels.cs`
- `src/Services/Sorcha.ApiGateway/Services/DashboardStatisticsService.cs`
- `src/Services/Sorcha.ApiGateway/Services/AlertAggregationService.cs`

---

## 2. Wallet Access Delegation — Current State

### Decision: Add UI Access tab + CLI commands; backend is complete
### Rationale
The Wallet Service has fully implemented delegation endpoints:
- `POST /api/v1/wallets/{walletAddress}/access` — Grant access
- `GET /api/v1/wallets/{walletAddress}/access` — List grants
- `DELETE /api/v1/wallets/{walletAddress}/access/{subject}` — Revoke
- `GET /api/v1/wallets/{walletAddress}/access/{subject}/check` — Check access

The `AccessRight` enum defines: `Owner`, `ReadWrite`, `ReadOnly`. Per the clarification, permission is configurable per-grant. The spec's "read/sign/admin" maps to:
- read → `ReadOnly`
- sign → `ReadWrite`
- admin → `Owner`

The `WalletDetail.razor` page has 4 tabs (Overview, Addresses, Sign Data, Transactions). An "Access" tab needs to be added.

### Alternatives Considered
- New standalone delegation page: Rejected — fits naturally as a tab on wallet detail

### Key Files
- `src/Services/Sorcha.Wallet.Service/Endpoints/DelegationEndpoints.cs`
- `src/Services/Sorcha.Wallet.Service/Models/GrantAccessRequest.cs`
- `src/Services/Sorcha.Wallet.Service/Models/WalletAccessDto.cs`
- `src/Common/Sorcha.Wallet.Core/Domain/Enums.cs` — `AccessRight` enum
- `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/Wallets/WalletDetail.razor`

---

## 3. Schema Provider Admin — Current State

### Decision: Page is already functional — scope reduced to CLI commands only
### Rationale
The Schema Provider Health page (`/admin/schema-providers`) is NOT a placeholder — it is fully functional:
- Displays providers as cards with health status chips (Healthy/Degraded/Unavailable)
- Shows schema count, last fetch timestamp, error details
- Manual refresh button with loading state and snackbar notifications
- Uses `ISchemaLibraryApiService` with `GetProviderStatusesAsync()` and `RefreshProviderAsync()`

Backend endpoints exist: `GET /api/v1/schemas/providers` and `POST /api/v1/schemas/providers/{providerName}/refresh`.

The only gap is CLI commands (`schema providers list|refresh`).

### Alternatives Considered
- Rebuild the UI page: Rejected — it already works correctly

### Key Files
- `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/Admin/SchemaProviderHealth.razor`
- `src/Services/Sorcha.Blueprint.Service/Endpoints/SchemaLibraryEndpoints.cs`
- `src/Services/Sorcha.Blueprint.Service/Models/SchemaProviderStatus.cs`

---

## 4. Events Admin — Current State

### Decision: Build admin Events page + CLI commands; backend endpoints exist
### Rationale
Event endpoints exist in the Blueprint Service:
- `GET /api/events/admin` — Admin list (supports userId, severity, pagination, since filters)
- `DELETE /api/events/{id}` — Delete single event
- `GET /api/events` — User-scoped events
- `POST /api/events/mark-read` — Mark as read

The EventsHub SignalR hub provides real-time event delivery (`EventReceived`, `UnreadCountUpdated`).

No admin Events page exists — needs to be created with filtering, pagination, and single-event deletion (bulk deletion out of scope per clarification).

### Key Files
- `src/Services/Sorcha.Blueprint.Service/Endpoints/EventEndpoints.cs`
- `src/Services/Sorcha.Blueprint.Service/Hubs/EventsHub.cs`

---

## 5. Push Notification Management — Current State

### Decision: Build Settings > Notifications UI; backend endpoints exist
### Rationale
Push subscription endpoints exist in the Tenant Service:
- `POST /api/push-subscriptions` — Register subscription (Web Push API keys)
- `DELETE /api/push-subscriptions` — Remove subscription (by endpoint query param)
- `GET /api/push-subscriptions/status` — Check active status (`hasActiveSubscription: boolean`)

No UI toggle exists. Needs a Notifications tab in Settings with enable/disable toggle and browser permission handling.

### Key Files
- `src/Services/Sorcha.Tenant.Service/Endpoints/PushSubscriptionEndpoints.cs`

---

## 6. Encryption Operation Status — Current State

### Decision: Build progress indicator component; backend polling endpoint exists
### Rationale
The operations endpoint exists: `GET /api/operations/{operationId}` returns operation status. This serves as a polling fallback for clients without SignalR.

The ActionsHub SignalR hub handles real-time action notifications. The UI needs a progress indicator component that shows encryption stages (queued, processing, complete).

CLI command: `operation status <operationId>`.

### Key Files
- `src/Services/Sorcha.Blueprint.Service/Endpoints/OperationsEndpoints.cs`
- `src/Services/Sorcha.Blueprint.Service/Hubs/ActionsHub.cs`

---

## 7. UI Service Reliability — Current State

### Decision: Extract credential status constants, improve error handling, add structured inputs
### Rationale

**Credential Status Magic Strings:**
Status values ("Active", "Suspended", "Revoked", "Expired", "Consumed") are used as inline strings throughout:
- `CredentialEndpoints.cs` — status checks like `Status is not ("Active" or "Suspended")`
- UI services return status as raw strings
- No `CredentialStatus` constants class exists

**Credential Lifecycle Error Handling:**
Current services return `null` for all failure cases (403, 409, 404, 500). Need to return distinguishable error information per FR-024.

**Presentation Request Multi-Value Inputs:**
`CreatePresentationRequestViewModel` has `AcceptedIssuers` and `RequiredClaims` as `List<string>`. The UI form likely uses comma-separated text input — needs structured tag/chip input (FR-026).

**Presentation Request Auto-Refresh:**
`QrPresentationDisplay.razor` already has 2-second timer-based polling. Per clarification, the presentation request admin page should auto-refresh at 5-second intervals (separate from the QR display polling).

**JSON Serialization:**
Need to check for duplicated `JsonSerializerOptions` across services — FR-028 requires shared configuration.

### Key Files
- `src/Services/Sorcha.Blueprint.Service/Endpoints/CredentialEndpoints.cs`
- `src/Apps/Sorcha.UI/Sorcha.UI.Core/Models/Credentials/CredentialLifecycleModels.cs`
- `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Credentials/QrPresentationDisplay.razor`
- `src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/Credentials/CredentialApiService.cs`
- `src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/Credentials/PresentationAdminService.cs`

---

## 8. Existing Patterns to Follow

### UI Service Registration
```csharp
services.AddScoped<IService>(sp =>
{
    var handler = sp.GetRequiredService<AuthenticatedHttpMessageHandler>();
    handler.InnerHandler = new HttpClientHandler();
    var httpClient = new HttpClient(handler) { BaseAddress = new Uri(baseAddress) };
    var logger = sp.GetRequiredService<ILogger<ServiceImpl>>();
    return new ServiceImpl(httpClient, logger);
});
```

### CLI Command Structure
- Main command class inherits from `Command`
- Subcommands as nested classes
- `this.SetAction(async (ParseResult parseResult, CancellationToken ct) => { ... })`
- `HttpClientFactory.Create*ServiceClientAsync(profileName)` for Refit clients
- `--output/-o` option for json/table/csv formats
- `ApiException` error handling with `StatusCode` checks

### SignalR Hub Connection
- JWT token via query parameter
- Subscribe/Unsubscribe pattern with groups
- Client methods for server→client events
