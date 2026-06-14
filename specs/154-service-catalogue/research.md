# Phase 0 Research: PWA Service Catalogue

**Feature**: 154-service-catalogue | **Date**: 2026-06-14

## Decision 1 — New consumer-tier catalogue endpoint (the one backend addition)

`/api/blueprints` GET exists but is gated `CanManageBlueprints` (designer/admin) — NOT consumer. So
B adds `GET /api/catalogue` in Blueprint Service, `.RequireAuthorization()` (cross-tier "any-human",
like `/api/actions/pending`), reading `IPublishedBlueprintStore.GetAllAsync()`. Returns
`CatalogueItem { BlueprintId, Title, Description, RegisterId }` for each **startable** published
blueprint. No new store.

## Decision 2 — "Startable by a citizen" rule (v1)

A published blueprint is citizen-startable when its **first action's sender participant is open**
(no hard-coded `WalletAddress` in the published blueprint) — i.e. a citizen can initiate by binding
their own wallet (the same open-participant model A/D rely on). First action = the action with the
lowest `Id` (or first in order). If sender/first-action can't be resolved, exclude (conservative —
don't list something the citizen can't actually start). Curation flags are a later refinement.

## Decision 3 — Title/description/register source

From `PublishedBlueprint`: `RegisterId` (to start the instance) + `Blueprint` (Sorcha.Blueprint.
Models.Blueprint) → `Title`, `Description`, `Participants` (Id/WalletAddress), `Actions` (Id/Sender).
A published blueprint without a `RegisterId` is excluded (can't be started).

## Decision 4 — Start flow reuses CreateInstance

On tap → `POST /api/instances/` with `{ BlueprintId, RegisterId }` (`CreateInstanceRequest`) → on
success navigate base-relative to `applications/{instanceId}` → existing `ApplicationInstance` renders
the first action (A's fill/submit). No change to CreateInstance or ApplicationInstance. The PWA gets a
small create call (reuse `IApplicationActionClient` host or a tiny method on the catalogue client).

## Decision 5 — Scoping

v1: list published + startable services. Deep org/home scoping (only services the citizen's context
can run) is a refinement; the create/execute path already enforces access, so listing a non-runnable
service degrades to a start failure (FR-006) rather than a security issue. Keep v1 simple.

## Decision 6 — Testing

- Blueprint Service: `IsCitizenStartable` filter (open vs hard-coded first-action sender; no
  actions; missing register) + endpoint returns mapped items. xUnit.
- PWA: `ICatalogueClient` JSON mapping (stub handler); `Applications.razor` bUnit (list, empty,
  search-narrows, tap→create→navigate, load-failure notice).
