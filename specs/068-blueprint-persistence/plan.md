# Implementation Plan: Blueprint Service Persistence & Validator Crash Recovery

**Branch**: `068-blueprint-persistence` | **Date**: 2026-03-24 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/068-blueprint-persistence/spec.md`

## Summary

Migrate Blueprint Service from volatile in-memory storage to durable PostgreSQL (drafts, templates, actions, instances) + Redis cache (published blueprints, instance execution state). The register remains the single source of truth for published blueprints — the Blueprint Service caches from it. Add validator startup reconciliation to drain pending transactions from the unverified pool after a crash.

## Technical Context

**Language/Version**: C# 13 / .NET 10.0
**Primary Dependencies**: EF Core 10.0.5, Npgsql 10.0.1, StackExchange.Redis 2.12.4, .NET Aspire 13.2.0
**Storage**: PostgreSQL (drafts, templates, actions, instances), Redis (published blueprint cache, instance state cache), MongoDB (register — source of truth, read-only)
**Testing**: xUnit 3.2.2, FluentAssertions 8.9.0, Moq 4.20.72
**Target Platform**: Linux containers (Docker), Windows dev
**Project Type**: Distributed microservices
**Performance Goals**: <50ms cache hit for published blueprints, <10s instance state reconstruction
**Constraints**: Register is source of truth for published data; PostgreSQL for local working state only
**Scale/Scope**: ~12 new files, ~5 modified files, 1 new database

## Constitution Check

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Microservices-First | PASS | Blueprint Service owns its PostgreSQL database. Register read via service client. |
| II. Security First | PASS | Draft access restricted to owner. No secrets in connection strings. |
| III. API Documentation | PASS | No API changes — storage layer only. |
| IV. Testing Requirements | PASS | Tests for each store implementation, cache hit/miss, reconstruction. |
| V. Code Quality | PASS | Async/await, DI, nullable enabled, IDbContextFactory for singleton stores. |
| VI. Blueprint Standards | N/A | No blueprint format changes. |
| VII. Domain-Driven Design | PASS | Entities follow existing naming (Blueprint, Instance, Action). |
| VIII. Observability | PASS | Structured logging on cache hits/misses, reconstruction, seed operations. |

## Project Structure

### Documentation (this feature)

```text
specs/068-blueprint-persistence/
├── plan.md              # This file
├── spec.md              # Feature specification
├── research.md          # Research findings
├── data-model.md        # Entity definitions
├── quickstart.md        # Implementation order
├── contracts/
│   └── blueprint-persistence-changes.md
├── checklists/
│   └── requirements.md
└── tasks.md             # Created by /speckit.tasks
```

### Source Code (repository root)

```text
src/Services/Sorcha.Blueprint.Service/
├── Data/
│   ├── BlueprintDbContext.cs                    # NEW — EF Core context
│   └── Entities/
│       ├── BlueprintDraftEntity.cs              # NEW
│       ├── BlueprintDraftAccessEntity.cs        # NEW (schema only)
│       ├── BlueprintTemplateEntity.cs           # NEW
│       ├── ActionEntity.cs                      # NEW
│       ├── FileMetadataEntity.cs                # NEW
│       └── InstanceEntity.cs                    # NEW
├── Migrations/                                  # NEW — EF Core migrations
├── Storage/
│   ├── EfCoreBlueprintStore.cs                  # NEW — IBlueprintStore impl
│   ├── EfCoreTemplateStore.cs                   # NEW — IDocumentStore impl
│   ├── EfCoreActionStore.cs                     # NEW — IActionStore impl
│   ├── EfCoreInstanceStore.cs                   # NEW — IInstanceStore impl
│   ├── RedisCachedPublishedBlueprintStore.cs    # NEW — IPublishedBlueprintStore impl
│   ├── InMemoryActionStore.cs                   # KEEP — fallback
│   ├── InMemoryInstanceStore.cs                 # KEEP — fallback
│   ├── IActionStore.cs                          # KEEP — unchanged
│   └── IInstanceStore.cs                        # KEEP — unchanged
├── Program.cs                                   # MODIFY — DI registration switch

src/Apps/Sorcha.AppHost/AppHost.cs               # MODIFY — add BlueprintDb
docker-compose.yml                               # MODIFY — add connection string
docker/postgres-init.sql                         # MODIFY — add database

src/Services/Sorcha.Validator.Service/
└── Services/DocketBuildTriggerService.cs         # MODIFY — add reconciliation

tests/
├── Sorcha.Blueprint.Service.Tests/              # MODIFY — persistence tests
└── Sorcha.Validator.Service.Tests/              # MODIFY — reconciliation tests
```

## Complexity Tracking

No constitution violations. All changes use established patterns:
- EF Core DbContext follows Tenant/Wallet service patterns
- `IDbContextFactory` for singleton stores follows Peer Service pattern
- Redis cache follows Validator `BlueprintCache` pattern
- Auto-migration follows Tenant `DatabaseInitializer` pattern
- InMemory fallback follows Wallet/Peer conditional registration pattern
