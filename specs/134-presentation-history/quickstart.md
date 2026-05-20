# Quickstart: Cross-Device Citizen Presentation History

This feature has no standalone runtime of its own — it extends the running Sorcha stack and the citizen wallet PWA. The fastest way to exercise it end-to-end.

## Prerequisites

- The Sorcha stack running (`docker-compose up -d`) with the Wallet Service on a real PostgreSQL connection (so the durable store is persistent, not the in-memory fallback).
- An enrolled citizen wallet (Feature 114): sign in on the PWA at `/wallet/`, enrol a device.
- A way to make a presentation — the reference verifier desk at `/verify/` (F125), or the demo-mint flow.

## Build & test (developer loop)

```bash
# Server
dotnet build src/Services/Sorcha.Wallet.Service/Sorcha.Wallet.Service.csproj
dotnet test  tests/Sorcha.Wallet.Service.Tests/Sorcha.Wallet.Service.Tests.csproj \
  # focus: CitizenPresentationStoreTests, CitizenPresentationStoreForwarderTests, PresentationHistoryEndpointTests

# PWA
dotnet build src/Apps/Sorcha.Wallet.Pwa/Sorcha.Wallet.Pwa.csproj
dotnet test  tests/Sorcha.Wallet.Pwa.Tests/Sorcha.Wallet.Pwa.Tests.csproj \
  # focus: ActivityMergeTests

# EF migration for CitizenPresentationRecord (Windows/pwsh): set the connection string env first
$env:ConnectionStrings__Sorcha__Postgres = "Host=localhost;Database=sorcha;Username=postgres;Password=postgres"
dotnet ef migrations add AddCitizenPresentationRecord \
  --project src/Core/Sorcha.Wallet.Portable \
  --startup-project src/Services/Sorcha.Wallet.Service
# (do NOT pass --no-build; it produces an empty migration)
```

## Manual end-to-end (the P1 story)

1. **Device A — present.** On device A's PWA, present a credential to the reference verifier. The presentation appears immediately in **Activity** (local, `SyncedToServer=false`).
2. **Sync.** Return to Home; the next `SyncService.SyncAsync` drains the local log → `POST /api/v1/wallet/presentations/log` → reporter dedupes → forwarder upserts to the store. The Activity entry's `SyncedToServer` flips to `true`.
3. **Device B — pair & read.** Pair a second device for the same citizen. Open **Activity** on B → it calls `GET /api/v1/wallet/presentations` → device A's presentation is listed. ✅ (SC-001)
4. **Delete (server-authoritative).** Delete the entry on B → `DELETE /api/v1/wallet/presentations/{id}`. Reopen Activity on A → the entry is gone and does not return on subsequent syncs. ✅ (SC-003, US2)
5. **No register write.** Inspect the citizen's registers — there is **no** `presentation-initiated` / `presentation-outcome` / any tx for this presentation. ✅ (SC-004, FR-010)

## What "done" looks like

- A freshly-paired device shows past presentations (US1 / SC-001).
- Delete removes everywhere and stays gone (US2 / SC-003).
- A just-made presentation shows instantly and exactly once after sync — no duplicate, no flicker (US3 / SC-002, SC-006).
- No register transaction is produced (SC-004).
- A citizen sees only their own history (SC-005).
