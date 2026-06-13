# Quickstart: PWA Offline / Field Capture

**Feature**: 152-offline-field-capture | **Date**: 2026-06-13

## What this adds

Field-first workflow capture in the Citizen Wallet PWA: open pending actions offline, fill them,
capture photos, save encrypted local drafts, and auto-submit on reconnect — with detect/hold/ask
conflict handling. Builds on sub-project A.

## Where the code lives

```
src/Apps/Sorcha.Wallet.Pwa/
├── Services/Drafts/IDraftStore.cs, IndexedDbDraftStore.cs, Models/        (new — US1)
├── Services/Drafts/IActionContextCache.cs, …                              (new — US2)
├── Services/Drafts/ISubmitQueue.cs, …                                     (new — US3)
├── Services/Drafts/SubmitConflictClassifier.cs                           (new — US4)
├── Services/IConnectivity.cs                                             (new — US1/US3)
├── Pages/ApplicationInstance.razor, Pages/Actions.razor                  (modify — A pages)
├── Services/ISyncService.cs                                              (modify — drain queue)
├── wwwroot/js/indexeddb-bridge.js                                        (modify — new stores)
└── Extensions/ServiceCollectionExtensions.cs                            (modify — DI)

src/Services/Sorcha.Blueprint.Service/  (US5 only — honor Files on /execute, reusing BuildFileTransactionsAsync)
```

## Build & test

```bash
dotnet build src/Apps/Sorcha.Wallet.Pwa/Sorcha.Wallet.Pwa.csproj
dotnet test  tests/Sorcha.Wallet.Pwa.Tests/Sorcha.Wallet.Pwa.Tests.csproj
# US5 backend slice:
dotnet test  tests/Sorcha.Blueprint.Service.Tests/Sorcha.Blueprint.Service.Tests.csproj
```

## Manual verification (Docker / n1)

1. Sign into the PWA as a citizen with pending actions; while online, let the app prepare (US2).
2. Go offline (devtools). Open a pending action **not opened before** → form renders (US2).
3. Fill it, capture a photo (US5), navigate away, reopen → data + photo restored (US1, SC-001/006).
4. Complete it offline → shows "Queued" (US3).
5. Restore connectivity → it submits automatically → "Submitted"; the photo is attached (US3, US5).
6. For conflict (US4): queue a submit, advance the action server-side, reconnect → item is held
   "Needs attention" with a reason; choose discard or re-open-fresh; captured data retained until chosen.
7. Open an **un-prepared** action offline → clear "available online" state, not a broken form.

## Guardrails

- Drafts/media/queue **encrypted at rest** (device key); device-bound (lost on device loss — say so).
- **Base-relative** navigation; **no `ISnackbar`** (use `IInlineFeedback`/inline state).
- Foreground-only drain (no Background Sync); idempotency prevents duplicate submits.
- US5 reuses the existing `Files`/`BuildFileTransactionsAsync` mechanism — do not invent a new
  attachment endpoint.
