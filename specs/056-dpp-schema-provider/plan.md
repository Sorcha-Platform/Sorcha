# Implementation Plan: DPP Schema Provider

**Branch**: `056-dpp-schema-provider` | **Date**: 2026-03-14 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/056-dpp-schema-provider/spec.md`

## Summary

Implement three independent external schema providers (`dpp-untp`, `dpp-batterypass`, `dpp-catenax`) that fetch Digital Product Passport JSON schemas from UNTP, Battery Pass, and Catena-X GitHub repositories. Add a new "Sustainability" sector to the platform. Providers follow the existing `FhirSchemaProvider` pattern — curated schema lists, in-memory caching, raw GitHub URL fetching, with Draft 4 → 2020-12 normalization.

## Technical Context

**Language/Version**: C# 13 / .NET 10
**Primary Dependencies**: `Sorcha.Blueprint.Schemas` (provider interface, normaliser), `System.Net.Http` (HttpClient), `Microsoft.Extensions.Logging`
**Storage**: MongoDB (existing `schemaIndex` collection — no changes)
**Testing**: xUnit + FluentAssertions + Moq
**Target Platform**: Linux server (Docker containers)
**Project Type**: Microservices (existing Blueprint Service + Schemas library)
**Performance Goals**: Schema discovery < 2s, provider refresh < 30s
**Constraints**: JSON Schema format only, in-memory caching, raw.githubusercontent.com URLs
**Scale/Scope**: ~17 schemas across 3 providers, 9 sectors (adding sustainability)

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Microservices-First | PASS | Providers added to existing Blueprint Schemas library, no new microservice |
| II. Security First | PASS | Fetching public schemas, no secrets, input validation on fetched content |
| III. API Documentation | PASS | No new API endpoints (uses existing provider/schema endpoints) |
| IV. Testing Requirements | PASS | Unit tests for all 3 providers + normalization verification |
| V. Code Quality | PASS | async/await, DI, .NET 10, nullable reference types |
| VI. Blueprint Standards | N/A | Not creating blueprints |
| VII. DDD | PASS | Using correct terminology (Schema, Provider, Sector) |
| VIII. Observability | PASS | Structured logging via ILogger, existing health check infrastructure |

No violations. No complexity tracking needed.

## Project Structure

### Documentation (this feature)

```text
specs/056-dpp-schema-provider/
├── plan.md              # This file
├── spec.md              # Feature specification
├── research.md          # Phase 0: Source research findings
├── data-model.md        # Phase 1: Entity definitions
├── quickstart.md        # Phase 1: Build order guide
└── checklists/
    └── requirements.md  # Spec quality checklist
```

### Source Code (repository root)

```text
src/Common/Sorcha.Blueprint.Schemas/
└── Services/
    ├── BatteryPassSchemaProvider.cs    # NEW: 7 curated Battery Pass schemas
    ├── CatenaXSchemaProvider.cs        # NEW: 5 curated Catena-X DPP schemas
    ├── UntpSchemaProvider.cs           # NEW: ~5 curated UNTP schemas
    ├── IExternalSchemaProvider.cs      # EXISTING: No changes
    ├── FhirSchemaProvider.cs           # EXISTING: Pattern reference
    └── JsonSchemaNormaliser.cs         # EXISTING: May need components.schemas handling

src/Services/Sorcha.Blueprint.Service/
├── Models/
│   └── SchemaSector.cs                # MODIFY: Add "sustainability" sector
└── Program.cs                         # MODIFY: Register 3 DPP providers in DI

tests/Sorcha.Blueprint.Schemas.Tests/
└── Services/
    ├── BatteryPassSchemaProviderTests.cs   # NEW
    ├── CatenaXSchemaProviderTests.cs       # NEW
    └── UntpSchemaProviderTests.cs          # NEW
```

**Structure Decision**: All provider implementations go in `Sorcha.Blueprint.Schemas/Services/` alongside existing providers. No new projects needed.

## Implementation Phases

### Phase 1: Sustainability Sector + Base Provider Pattern
1. Add `sustainability` sector to `SchemaSector.cs`
2. Create `BatteryPassSchemaProvider` with 7 curated schemas
3. Register in `Program.cs` DI with `AddHttpClient<BatteryPassSchemaProvider>()`
4. Write `BatteryPassSchemaProviderTests`
5. Verify Draft 4 normalization works with `components.schemas` pattern

### Phase 2: Catena-X Provider
1. Create `CatenaXSchemaProvider` with 5 curated DPP schemas
2. Register in `Program.cs` DI
3. Write `CatenaXSchemaProviderTests`

### Phase 3: UNTP Provider
1. Create `UntpSchemaProvider` with ~5 curated schemas
2. Register in `Program.cs` DI
3. Write `UntpSchemaProviderTests`

### Phase 4: Integration Verification
1. Verify all 3 providers appear on Schema Providers admin page
2. Verify schemas appear under "Sustainability" sector filter
3. Verify cross-sector tagging works (schemas appear under commerce, construction, government)
4. Verify version deprecation logic when a schema version changes
5. Run full test suite

## Key Implementation Details

### Provider Constructor Pattern
```csharp
public BatteryPassSchemaProvider(HttpClient httpClient, ILogger<BatteryPassSchemaProvider> logger)
```

### DI Registration Pattern
```csharp
builder.Services.AddHttpClient<BatteryPassSchemaProvider>(client =>
{
    client.BaseAddress = new Uri("https://raw.githubusercontent.com/batterypass/BatteryPassDataModel/main/");
    client.DefaultRequestHeaders.Add("User-Agent", "Sorcha-Blueprint-Service/1.0");
    client.Timeout = TimeSpan.FromSeconds(30);
}).AddTransientHttpErrorPolicy(p => p.WaitAndRetryAsync(2, attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt))));

builder.Services.AddSingleton<IExternalSchemaProvider>(sp =>
    sp.GetRequiredService<BatteryPassSchemaProvider>());
```

### Schema URL Pattern (Battery Pass Example)
```
https://raw.githubusercontent.com/batterypass/BatteryPassDataModel/main/BatteryPass/io.BatteryPass.GeneralProductInformation/1.2.0/gen/GeneralProductInformation-schema.json
```

### Normalization Note
All three sources use JSON Schema Draft 4. The existing `JsonSchemaNormaliser` handles draft-04 → 2020-12. However, these schemas use `components.schemas` (OpenAPI-style) instead of `definitions`. Verify the normaliser handles this — if not, add `components.schemas → $defs` transformation.

## Risk Register

| Risk | Impact | Mitigation |
|------|--------|------------|
| UNTP schema URLs change (pre-release standard) | Medium | Curated list is configurable; admin can update URLs |
| `components.schemas` not handled by normaliser | Low | Add transformation rule if needed |
| GitHub raw content intermittently unavailable | Low | Existing backoff + cache handles this |
| UNTP doesn't have standalone JSON Schema files | Medium | Fall back to building schemas from JSON-LD context, or curate from spec pages |
