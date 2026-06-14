# Endpoint Contracts: PWA Service Catalogue

**Feature**: 154-service-catalogue | **Date**: 2026-06-14

## NEW — list startable services

```
GET /api/catalogue            (Blueprint Service — Endpoints/CatalogueEndpoints.cs)
  auth: RequireAuthorization() (consumer-tier capable; cross-tier "any-human")
  returns 200: [ { blueprintId, title, description, registerId } ]   # startable + has register only
```
Reads `IPublishedBlueprintStore`; filters via the startable predicate (data-model). `.WithSummary`/
`.WithDescription` + XML docs; appears in OpenAPI.

## REUSED (unchanged)

```
POST /api/instances/   { blueprintId, registerId }   # CreateInstance (Program.cs:2153) — start a service
GET  /api/instances/{id}, GET /api/blueprints/{id}, POST /…/execute   # ApplicationInstance fill/submit (A)
```

**Drift guard:** if `PublishedBlueprint` / `CreateInstanceRequest` shapes change, the catalogue
endpoint test + client mapping test should fail.
