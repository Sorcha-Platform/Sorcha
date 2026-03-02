# Quickstart: 046 UI Polish & Blueprint Designer

## Prerequisites
- .NET 10 SDK, Docker Desktop running
- Branch: `046-ui-polish-designer`

## Build & Test
```bash
dotnet build src/Apps/Sorcha.UI/Sorcha.UI.Web/Sorcha.UI.Web.csproj
dotnet build src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Sorcha.UI.Web.Client.csproj
dotnet build src/Apps/Sorcha.UI/Sorcha.UI.Core/Sorcha.UI.Core.csproj
dotnet test tests/Sorcha.UI.Core.Tests/
```

## Manual Verification
```bash
docker-compose up -d
# Open http://localhost/app
# 1. Login → verify welcome shows display name (not GUID)
# 2. Create wallet → verify wizard doesn't loop
# 3. Toggle dark mode in Settings → verify all pages readable
# 4. Open Designer → save blueprint → navigate away → load it back
# 5. Close notification panel → verify no horizontal scroll
# 6. Switch language to French → verify navigation labels change
```

## Key Files (by user story)
| Story | Primary Files |
|-------|--------------|
| US1 Dashboard | `Pages/Home.razor`, `Pages/Wallets/CreateWallet.razor` |
| US2 Notification | `Components/Layout/MainLayout.razor`, `Components/Layout/ActivityLogPanel.razor` |
| US3 Dark Mode | `Pages/Wallets/CreateWallet.razor`, `Pages/Wallets/WalletDetail.razor`, `Components/Designer/*.razor` |
| US4 Coming Soon | `Pages/Wallets/CreateWallet.razor`, `Pages/Wallets/WalletDetail.razor`, `Pages/Settings.razor` |
| US5 Designer Save/Load | `Pages/Designer.razor`, `Services/IBlueprintApiService.cs`, `Services/BlueprintApiService.cs` |
| US6 Notifications | `Services/EventsHubConnection.cs` (new), `Components/Layout/ActivityLogPanel.razor` |
| US7 Localization | `Components/Layout/MainLayout.razor`, all pages with hardcoded text |
