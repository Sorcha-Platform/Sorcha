# Research: DPP Schema Provider Sources

**Date**: 2026-03-14
**Feature**: 056 - DPP Schema Provider

## Source 1: UNTP (UN Trade Facilitation)

### Decision
Fetch JSON Schema files directly from the `test.uncefact.org/vocabulary/untp/` endpoint. Contrary to initial assumption, this endpoint serves **both** JSON-LD context files and standalone JSON Schema files at distinct URLs.

### Rationale
- `test.uncefact.org` hosts JSON Schema files at predictable URLs: `untp-{type}-schema-{version}.json`
- Schemas are already in **JSON Schema Draft 2020-12** — no normalization needed (unlike Battery Pass and Catena-X)
- Uses `$defs` for type definitions (20+ types in DPP schema)
- All credential types release in lockstep at the same version numbers
- 4 credential types confirmed, all at v0.6.1 (pre-release, < 1.0.0)

### Source Details
- **Base URL**: `https://test.uncefact.org/vocabulary/untp/`
- **Schema URL pattern**: `https://test.uncefact.org/vocabulary/untp/{type}/untp-{type}-schema-{version}.json`
- **Version listing**: `https://test.uncefact.org/vocabulary/untp/{type}/0/versions`
- **Version**: 0.6.1 (pre-release, all types in lockstep)
- **Format**: JSON Schema Draft 2020-12 (no normalization needed!)
- **Auth**: None required — public HTTP GET
- **Approach**: Curated list of 4 known credential types, version extracted from URL pattern

### Alternatives Considered
- GitHub repo (`uncefact/spec-untp`): Has moved to GitLab; `test.uncefact.org` is the canonical source
- Auto-discovery via version listing endpoint: Possible but overkill for 4 known types

### Schemas Available (4)

| Type | Abbreviation | Schema URL |
|------|-------------|------------|
| Digital Product Passport | `dpp` | `test.uncefact.org/vocabulary/untp/dpp/untp-dpp-schema-0.6.1.json` |
| Digital Conformity Credential | `dcc` | `test.uncefact.org/vocabulary/untp/dcc/untp-dcc-schema-0.6.1.json` |
| Digital Traceability Event | `dte` | `test.uncefact.org/vocabulary/untp/dte/untp-dte-schema-0.6.1.json` |
| Digital Facility Record | `dfr` | `test.uncefact.org/vocabulary/untp/dfr/untp-dfr-schema-0.6.1.json` |

---

## Source 2: Battery Pass

### Decision
Fetch `*-schema.json` files from the Battery Pass GitHub repository (`batterypass/BatteryPassDataModel`) using raw content URLs. Each data model category has a versioned directory with a `gen/` folder containing generated JSON Schema files.

### Rationale
- Well-structured repository with predictable paths
- Each category has a `{Name}-schema.json` in the `gen/` folder
- All schemas use JSON Schema Draft 4 (requires normalization to 2020-12)
- Uses SAMM URNs for schema identification (`urn:samm:io.BatteryPass.{Category}:{version}`)
- Version 1.2.0 is current across all categories

### Source Details
- **Repository**: `batterypass/BatteryPassDataModel`
- **Branch**: `main`
- **Base URL**: `https://raw.githubusercontent.com/batterypass/BatteryPassDataModel/main/`
- **Schema pattern**: `BatteryPass/io.BatteryPass.{Category}/{version}/gen/{Name}-schema.json`
- **Version**: 1.2.0 (stable, ≥1.0.0)
- **Format**: JSON Schema Draft 4
- **URN pattern**: `urn:samm:io.BatteryPass.{Category}:{version}#{Name}`

### Schemas Available (7)

| Category | Schema File | Sector Tags |
|----------|------------|-------------|
| GeneralProductInformation | `GeneralProductInformation-schema.json` | sustainability, commerce |
| CarbonFootprint | `CarbonFootprintForBatteries-schema.json` | sustainability, government |
| Circularity | `Circularity-schema.json` | sustainability, commerce |
| MaterialComposition | `MaterialComposition-schema.json` | sustainability, commerce |
| Labels | `Labeling-schema.json` | sustainability, commerce |
| Performance | `Performance-schema.json` | sustainability, commerce |
| SupplyChainDueDiligence | `SupplyChainDueDiligence-schema.json` | sustainability, commerce |

### Raw URL Examples
```
https://raw.githubusercontent.com/batterypass/BatteryPassDataModel/main/BatteryPass/io.BatteryPass.GeneralProductInformation/1.2.0/gen/GeneralProductInformation-schema.json
https://raw.githubusercontent.com/batterypass/BatteryPassDataModel/main/BatteryPass/io.BatteryPass.CarbonFootprint/1.2.0/gen/CarbonFootprintForBatteries-schema.json
```

---

## Source 3: Catena-X / Eclipse Tractus-X

### Decision
Fetch `*-schema.json` files from the Eclipse Tractus-X semantic models repository (`eclipse-tractusx/sldt-semantic-models`) using raw content URLs. Only DPP-related aspect models are included; the repository contains 100+ models but we curate the sustainability-relevant subset.

### Rationale
- Repository follows consistent structure: `io.catenax.{domain}/{version}/gen/{Name}-schema.json`
- Source files are SAMM/Turtle (`.ttl`) but generated JSON Schema files are available in `gen/`
- All schemas use JSON Schema Draft 4 (requires normalization to 2020-12)
- Uses SAMM URNs for identification (`urn:samm:io.catenax.{domain}:{version}`)
- Curating DPP-relevant subset avoids importing 100+ unrelated automotive models

### Source Details
- **Repository**: `eclipse-tractusx/sldt-semantic-models`
- **Branch**: `main`
- **Base URL**: `https://raw.githubusercontent.com/eclipse-tractusx/sldt-semantic-models/main/`
- **Schema pattern**: `io.catenax.{domain}/{version}/gen/{Name}-schema.json`
- **Metadata**: `io.catenax.{domain}/{version}/metadata.json` (contains URN, status)
- **Format**: JSON Schema Draft 4
- **URN pattern**: `urn:samm:io.catenax.{domain}:{version}#{Name}`

### DPP-Relevant Schemas (5)

| Model | Version | Schema File | Sector Tags |
|-------|---------|------------|-------------|
| generic.digital_product_passport | 7.0.0 | `DigitalProductPassport-schema.json` | sustainability, commerce |
| battery.battery_pass | 6.1.0 | `BatteryPass-schema.json` | sustainability, commerce, construction |
| pcf | 9.0.0 | `Pcf-schema.json` | sustainability, government |
| transmission.transmission_pass | 3.1.0 | `TransmissionPass-schema.json` | sustainability, commerce |
| manufacturing_capability | 3.1.0 | `ManufacturingCapability-schema.json` | sustainability, commerce |

### Raw URL Examples
```
https://raw.githubusercontent.com/eclipse-tractusx/sldt-semantic-models/main/io.catenax.generic.digital_product_passport/7.0.0/gen/DigitalProductPassport-schema.json
https://raw.githubusercontent.com/eclipse-tractusx/sldt-semantic-models/main/io.catenax.battery.battery_pass/6.1.0/gen/BatteryPass-schema.json
```

---

## Cross-Cutting Decisions

### JSON Schema Draft Normalization
- **Decision**: All three sources use JSON Schema Draft 4. Use existing `JsonSchemaNormaliser.Normalise()` to convert to 2020-12.
- **Rationale**: The normaliser already handles draft-04 → 2020-12 conversion (`id → $id`, `definitions → $defs`, `exclusiveMin/Max` normalization).
- **Risk**: Battery Pass and Catena-X schemas use `components.schemas` (OpenAPI-style) rather than `definitions`. The normaliser may need to handle this pattern. Verify during implementation.

### Provider Architecture
- **Decision**: Three independent `IExternalSchemaProvider` implementations, each as a curated static provider with HTTP fetching.
- **Pattern**: Similar to `FhirSchemaProvider` — curated list of known schemas, fetched on first access, cached in-memory with `SemaphoreSlim` thread safety.
- **ProviderType**: `LiveApi` (fetches from remote URLs at runtime)

### Schema Count Summary
| Provider | Schemas | Versions | Pre-release? | Schema Draft |
|----------|---------|----------|-------------|-------------|
| dpp-untp | 4 | 0.6.1 | Yes (< 1.0.0) | 2020-12 (no normalization) |
| dpp-batterypass | 7 | 1.2.0 | No (stable) | Draft 4 (needs normalization) |
| dpp-catenax | 5 | Various (3.1.0–9.0.0) | No (stable) | Draft 4 (needs normalization) |
| **Total** | **16** | | | |

This exceeds SC-002 requirement of ≥10 schemas across all sources.

### Version Extraction
- **Battery Pass**: Version from directory path (`1.2.0`)
- **Catena-X**: Version from directory path AND `metadata.json`
- **UNTP**: Version from schema URL or filename (e.g., `0.5.0`)
