# Implementation Plan: PWA Service Catalogue

**Branch**: `154-service-catalogue` | **Date**: 2026-06-14 | **Spec**: [spec.md](./spec.md)

**Source design**: `docs/superpowers/specs/2026-06-14-pwa-service-catalogue-design.md` | **Depends on**: A (merged).

## Summary

Turn the empty `Applications.razor` stub into a working "start something new" surface: a new
consumer-tier `GET /api/catalogue` lists the services a citizen can start; the PWA browses them and,
on tap, starts a new application via the **existing** `POST /api/instances/` (`CreateInstance`) and
navigates into the existing `ApplicationInstance` fill/submit. One backend read endpoint; the rest is
front-end reuse.

## Technical Context

**Language/Version**: C# 14 / .NET 10 (Blueprint Service minimal API + Blazor WASM PWA).
**Primary Dependencies**: Blueprint Service `IPublishedBlueprintStore` (lists `PublishedBlueprint`
{BlueprintId, RegisterId, Blueprint=Sorcha.Blueprint.Models.Blueprint}); PWA HttpClient (+ bearer
chain), `Applications.razor`, `ApplicationInstance` (A), `IApplicationActionClient` create path.
**Storage**: none new (reads the published-blueprint store).
**Testing**: xUnit (Blueprint Service endpoint + the startable filter) + bUnit (catalogue page) +
client mapping test.
**Target Platform**: PWA `/wallet/` (consumer); Blueprint Service.
**Project Type**: one backend read endpoint + PWA front-end.
**Constraints**: consumer-tier; **only startable services listed**; base-relative nav; no `ISnackbar`;
reuse `CreateInstance` (no change).
**Scale/Scope**: 1 endpoint + a startable-filter helper + `ICatalogueClient` + catalogue UI + start flow.

## Constitution Check

| Principle | Status |
|-----------|--------|
| I. Microservices-First | ✅ one read endpoint in the owning service; no upward coupling |
| II. Security First | ✅ consumer-tier; lists only startable services; no secrets; reuses create-instance authz |
| III. API Documentation | ✅ new endpoint gets `.WithSummary/.WithDescription` + XML; OpenAPI auto |
| IV. Testing (>85% new) | ✅ endpoint + startable filter + client + page tests |
| V. Code Quality | ✅ nullable, async, DI, no warnings |
| VI. Blueprint Standards | ✅ N/A (reads published blueprints) |
| VII. DDD | ✅ Blueprint/Action/Participant terms; "service/catalogue" is the citizen-facing label |
| VIII. Observability | ✅ structured logs; existing telemetry |

**Result**: PASS.

## Project Structure

```text
src/Services/Sorcha.Blueprint.Service/
└── Endpoints/CatalogueEndpoints.cs        # NEW — GET /api/catalogue (consumer-tier) + startable filter
    (mapped in Program.cs via app.MapCatalogueEndpoints())

src/Apps/Sorcha.Wallet.Pwa/
├── Services/Catalogue/ICatalogueClient.cs # NEW — typed client + CatalogueItem
├── Pages/Applications.razor               # REPLACE stub — browse + search + start
└── Extensions/ServiceCollectionExtensions.cs # MODIFY — register ICatalogueClient

tests/
├── Sorcha.Blueprint.Service.Tests/        # NEW — startable filter + endpoint shape
└── Sorcha.Wallet.Pwa.Tests/Catalogue/     # NEW — client mapping + catalogue page bUnit
```

**Structure Decision**: one consumer-tier read endpoint in Blueprint Service; PWA catalogue page +
client; start reuses `CreateInstance` + `ApplicationInstance`.

## Open Decisions (research.md)

- "Startable by a citizen" rule (v1: first action's sender participant is open / no hardcoded wallet).
- Catalogue scoping to the caller's context (v1: published + startable; keep simple).
- Endpoint auth (plain `RequireAuthorization`, cross-tier like `/api/actions/pending`).

## Complexity Tracking
> No violations.
