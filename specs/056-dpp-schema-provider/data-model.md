# Data Model: DPP Schema Provider

**Feature**: 056 - DPP Schema Provider

## Entities

### 1. SchemaSector (Modified — add "sustainability")

**File**: `src/Services/Sorcha.Blueprint.Service/Models/SchemaSector.cs`

| Field | Type | Change |
|-------|------|--------|
| Id | string | `"sustainability"` |
| DisplayName | string | `"Sustainability & ESG"` |
| Description | string | `"Environmental sustainability, digital product passports, carbon footprint, and ESG compliance standards"` |
| Icon | string | `Icons.Material.Filled.Eco` |

Add to existing `All` list. `ValidIds` auto-computes.

### 2. DppSchemaDefinition (Internal — per provider)

Each provider maintains a curated list of schema definitions. Not persisted — used at runtime to drive `GetCatalogAsync()`.

| Field | Type | Description |
|-------|------|-------------|
| Name | string | Display name (e.g., "Battery Pass - General Product Information") |
| Description | string | Schema description |
| SchemaUrl | string | Raw content URL for fetching |
| SourceUri | string | Version-encoded unique URI (e.g., `batterypass/GeneralProductInformation/1.2.0`) |
| Version | string | Schema version (e.g., "1.2.0") |
| SectorTags | string[] | Sector assignments (e.g., ["sustainability", "commerce"]) |
| GoverningBody | string | Standards body (e.g., "Battery Pass Consortium") |
| SpecificationUrl | string | Link to specification documentation |

### 3. ExternalSchemaResult (Existing — no changes)

Providers return `ExternalSchemaResult` records via the existing interface. No model changes needed.

### 4. SchemaProviderStatus (Existing — no changes)

Each of the 3 DPP providers gets its own `SchemaProviderStatus` entry automatically via the existing refresh infrastructure. No model changes needed.

## Relationships

```
SchemaSector "sustainability" ←─tags─── DppSchemaDefinition ───fetches──→ Raw GitHub URL
                                              │
                                              ▼
                                    ExternalSchemaResult ───indexes──→ SchemaIndexEntryDocument (MongoDB)
                                              │
                                    SchemaProviderStatus ───tracks──→ Health/Backoff state
```

## No New Database Entities

All DPP provider data flows through existing infrastructure:
- Schema metadata → `SchemaIndexEntryDocument` (MongoDB `schemaIndex` collection)
- Provider health → `SchemaProviderStatus` (in-memory, exposed via API)
- Schema content → In-memory cache per provider instance
