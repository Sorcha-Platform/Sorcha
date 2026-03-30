# Quickstart: DPP Schema Provider

## Build Order

1. **Add "sustainability" sector** to `SchemaSector.cs` (1 file, no dependencies)
2. **Create `BatteryPassSchemaProvider`** — simplest source, 7 well-documented schemas
3. **Create `CatenaXSchemaProvider`** — similar pattern, 5 curated schemas
4. **Create `UntpSchemaProvider`** — least certain source, ~5 schemas
5. **Register all 3 providers** in `Program.cs` DI
6. **Write tests** for each provider (unit + integration)
7. **Verify normalization** — Draft 4 → 2020-12 for `components.schemas` pattern

## Key Files to Create

| File | Purpose |
|------|---------|
| `src/Common/Sorcha.Blueprint.Schemas/Services/BatteryPassSchemaProvider.cs` | Battery Pass provider |
| `src/Common/Sorcha.Blueprint.Schemas/Services/CatenaXSchemaProvider.cs` | Catena-X provider |
| `src/Common/Sorcha.Blueprint.Schemas/Services/UntpSchemaProvider.cs` | UNTP provider |
| `tests/Sorcha.Blueprint.Schemas.Tests/Services/BatteryPassSchemaProviderTests.cs` | Tests |
| `tests/Sorcha.Blueprint.Schemas.Tests/Services/CatenaXSchemaProviderTests.cs` | Tests |
| `tests/Sorcha.Blueprint.Schemas.Tests/Services/UntpSchemaProviderTests.cs` | Tests |

## Key Files to Modify

| File | Change |
|------|--------|
| `src/Services/Sorcha.Blueprint.Service/Models/SchemaSector.cs` | Add sustainability sector |
| `src/Services/Sorcha.Blueprint.Service/Program.cs` | Register 3 DPP providers in DI |

## Pattern to Follow

Use `FhirSchemaProvider` as the template:
- Curated list of known schemas (not auto-discovery)
- `SemaphoreSlim` cache with double-check lock
- `HttpClient` injected via `AddHttpClient<T>()`
- `ILogger<T>` for structured logging
- `DefaultSectorTags => ["sustainability"]`

## Verification

```bash
dotnet test --filter "FullyQualifiedName~BatteryPass"
dotnet test --filter "FullyQualifiedName~CatenaX"
dotnet test --filter "FullyQualifiedName~Untp"
```
