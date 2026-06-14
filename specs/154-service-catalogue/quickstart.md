# Quickstart: PWA Service Catalogue

**Feature**: 154-service-catalogue | **Date**: 2026-06-14

## Build & test
```bash
dotnet build src/Services/Sorcha.Blueprint.Service/Sorcha.Blueprint.Service.csproj
dotnet test  tests/Sorcha.Blueprint.Service.Tests/Sorcha.Blueprint.Service.Tests.csproj
dotnet test  tests/Sorcha.Wallet.Pwa.Tests/Sorcha.Wallet.Pwa.Tests.csproj
```

## Manual verification (Docker / n1)
1. Publish a blueprint whose first action has an OPEN sender participant (citizen-startable) to a register.
2. Sign into the PWA as a citizen; open the "applications" / catalogue surface.
3. Confirm the service appears with name + description; a non-startable blueprint does NOT appear.
4. Tap it → a new application starts → land in the first step's form → fill + submit (reuses A).
5. Search narrows the list; empty catalogue shows a friendly empty state; a load failure shows a notice.

## Guardrails
- Consumer-tier; list only startable services; base-relative nav; no `ISnackbar`; reuse CreateInstance.
