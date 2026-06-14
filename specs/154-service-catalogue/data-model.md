# Phase 1 Data Model: PWA Service Catalogue

**Feature**: 154-service-catalogue | **Date**: 2026-06-14

## Server response — catalogue item (new)

`GET /api/catalogue` returns `CatalogueItem[]`:

| Field | Type | Source | Notes |
|-------|------|--------|-------|
| `blueprintId` | string | `PublishedBlueprint.BlueprintId` | Which service |
| `title` | string | `Blueprint.Title` (fallback blueprintId) | Display name |
| `description` | string? | `Blueprint.Description` | Short description |
| `registerId` | string | `PublishedBlueprint.RegisterId` | Needed to start the instance |

Only items that are **citizen-startable** (open first-action sender) and have a `registerId` are
returned.

## Startable predicate (server, pure)

`IsCitizenStartable(Blueprint bp)`:
1. `bp.Actions` non-empty; take the first by `Id`.
2. Resolve its `Sender` participant in `bp.Participants`.
3. Startable ⇔ that participant's `WalletAddress` is null/empty (open / late-bound).
4. Unresolvable (no actions / no sender / no participant) ⇒ not startable (excluded).

## Client DTO (PWA)

`CatalogueItem(string BlueprintId, string Title, string? Description, string RegisterId)` — maps the
response 1:1.

## Start (reused)

`POST /api/instances/` body `{ BlueprintId, RegisterId }` (existing `CreateInstanceRequest`) →
returns the created instance (id) → navigate `applications/{instanceId}`.
