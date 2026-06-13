# Quickstart: PWA Citizen Workflow Inbox

**Feature**: 151-citizen-workflow-inbox | **Date**: 2026-06-13

## What this feature adds

A "Things to do" inbox in the Citizen Wallet PWA: a consumer-tier list of the workflow actions
currently waiting on the citizen, a live nav count badge, and an "In review" banner — tapping an
action opens the existing fill-and-submit form.

## Where the code lives

```
src/Apps/Sorcha.Wallet.Pwa/
├── Services/Actions/IMyActionsClient.cs        (new)
├── Services/Actions/HttpMyActionsClient.cs     (new)
├── Services/Actions/Models/PendingActionItem.cs, PendingActionsCount.cs (new)
├── Pages/Actions.razor                          (new — route: actions)
├── MainLayout.razor                             (modify — FloatingTabBar 5th tab + badge)
└── Program.cs                                   (modify — register IMyActionsClient)

shared (unchanged, reused):
src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Components/Wallet/FloatingTabBar.razor  (add tab)
src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Components/Forms/SorchaFormRenderer.razor
src/Apps/Sorcha.Wallet.Pwa/Pages/ApplicationInstance.razor
```

## Build & test

```bash
# Build the PWA + shared component lib
dotnet build src/Apps/Sorcha.Wallet.Pwa/Sorcha.Wallet.Pwa.csproj

# Run the PWA test project (component + client tests)
dotnet test tests/Sorcha.Wallet.Pwa.Tests/Sorcha.Wallet.Pwa.Tests.csproj
```

## Manual verification (against Docker / n1)

1. `docker-compose up -d`; sign into the PWA (`/wallet`) as a citizen who has at least one
   outstanding action (their turn) — e.g. a citizen mid-application in a seeded blueprint.
2. Confirm the **FloatingTabBar** shows a "To do" destination with a **count badge** matching the
   number of outstanding actions.
3. Open the inbox; confirm each outstanding action lists with a title (and due date / urgency chip
   where present), ordered most-pressing first.
4. Tap an action → confirm it opens the existing `ApplicationInstance` form; fill + submit.
5. Return to the inbox → confirm the completed action is gone and the badge decremented.
6. With another participant's action in a shared instance, confirm it does **not** appear.
7. Kill connectivity, reopen the inbox → confirm last-known list is retained with a non-blocking
   "couldn't refresh" notice (no blank/error screen).
8. While the inbox is open, cause a new action to arrive → confirm the list/badge update without a
   manual refresh.

## Guardrails (from research)

- **Base-relative navigation only** (`NavigateTo("applications/{id}")`, `NavigateTo("actions")`) —
  never origin-absolute, under the `/wallet/` prefix.
- **No `ISnackbar`** — use `IInlineFeedback` for refresh-failure / stale-action messages.
- **No backend changes** — consume existing endpoints only; do not add a consumer guard to
  `/api/actions/pending` (the web shares it).
- **Consumer-tier only** — the feature must not require any org role.
