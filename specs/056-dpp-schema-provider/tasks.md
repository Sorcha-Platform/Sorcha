# Tasks: DPP Schema Provider

**Input**: Design documents from `/specs/056-dpp-schema-provider/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, quickstart.md

**Tests**: Included — spec requires unit tests for all 3 providers (plan.md Phase 1-3 each include tests).

**Organization**: Tasks grouped by user story. US1 (Discover DPP Schemas) is the MVP — providers must exist before health monitoring or cross-sector discovery can work.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- Include exact file paths in descriptions

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Add the sustainability sector and verify normaliser handles DPP schema patterns

- [ ] T001 Add "sustainability" sector to `src/Services/Sorcha.Blueprint.Service/Models/SchemaSector.cs` — Id="sustainability", DisplayName="Sustainability & ESG", Description="Environmental sustainability, digital product passports, carbon footprint, and ESG compliance standards", Icon="Icons.Material.Filled.Eco"
- [ ] T002 Verify `JsonSchemaNormaliser` handles `components.schemas` pattern (OpenAPI-style) used by Battery Pass and Catena-X schemas — test in `tests/Sorcha.Blueprint.Schemas.Tests/JsonSchemaNormaliserTests.cs`. If not handled, add `components.schemas → $defs` transformation to `src/Common/Sorcha.Blueprint.Schemas/Services/JsonSchemaNormaliser.cs`

**Checkpoint**: Sustainability sector visible in `SchemaSector.All`. Normaliser confirmed for DPP schema patterns.

---

## Phase 2: User Story 1 — Discover DPP Schemas in Schema Library (Priority: P1) MVP

**Goal**: Users can filter by "Sustainability" sector and see DPP schemas from all 3 sources with correct metadata, field hierarchies, and source attribution.

**Independent Test**: Navigate to Schema Library → filter by "Sustainability" → confirm 16 schemas from 3 providers with titles, descriptions, versions, and sector tags.

### Tests for User Story 1

> **NOTE: Write these tests FIRST, ensure they FAIL before implementation**

- [ ] T003 [P] [US1] Create `BatteryPassSchemaProviderTests` in `tests/Sorcha.Blueprint.Schemas.Tests/Providers/BatteryPassSchemaProviderTests.cs` — test ProviderName, DefaultSectorTags, GetCatalogAsync returns 7 schemas, SearchAsync matches keywords, GetSchemaAsync returns content with correct metadata, all schemas have Draft 2020-12 after normalization. Use Moq for HttpClient via `HttpMessageHandler` mock pattern.
- [ ] T004 [P] [US1] Create `CatenaXSchemaProviderTests` in `tests/Sorcha.Blueprint.Schemas.Tests/Providers/CatenaXSchemaProviderTests.cs` — test ProviderName, DefaultSectorTags, GetCatalogAsync returns 5 schemas, SearchAsync, GetSchemaAsync, Draft 2020-12 normalization. Mock HttpMessageHandler.
- [ ] T005 [P] [US1] Create `UntpSchemaProviderTests` in `tests/Sorcha.Blueprint.Schemas.Tests/Providers/UntpSchemaProviderTests.cs` — test ProviderName, DefaultSectorTags, GetCatalogAsync returns 4 schemas, SearchAsync, GetSchemaAsync, confirm no normalization needed (already 2020-12). Mock HttpMessageHandler.

### Implementation for User Story 1

- [ ] T006 [P] [US1] Create `BatteryPassSchemaProvider` in `src/Common/Sorcha.Blueprint.Schemas/Services/BatteryPassSchemaProvider.cs` — implement `IExternalSchemaProvider` with 7 curated schemas from `raw.githubusercontent.com/batterypass/BatteryPassDataModel/main/`. Follow FhirSchemaProvider pattern: `SemaphoreSlim` cache, double-check lock, `HttpClient` injection. ProviderName="Battery Pass", DefaultSectorTags=["sustainability","commerce"]. Fetch each schema URL, normalize Draft 4→2020-12 via `JsonSchemaNormaliser.Normalise()`. SourceUri pattern: `batterypass/{Category}/{version}`. Include GoverningBody, SpecificationUrl in description. Schema list: GeneralProductInformation, CarbonFootprintForBatteries, Circularity, MaterialComposition, Labeling, Performance, SupplyChainDueDiligence — all at v1.2.0.
- [ ] T007 [P] [US1] Create `CatenaXSchemaProvider` in `src/Common/Sorcha.Blueprint.Schemas/Services/CatenaXSchemaProvider.cs` — implement `IExternalSchemaProvider` with 5 curated DPP schemas from `raw.githubusercontent.com/eclipse-tractusx/sldt-semantic-models/main/`. ProviderName="Catena-X", DefaultSectorTags=["sustainability","commerce"]. Normalize Draft 4→2020-12. SourceUri pattern: `catenax/{domain}/{version}`. Schemas: generic.digital_product_passport/7.0.0, battery.battery_pass/6.1.0, pcf/9.0.0, transmission.transmission_pass/3.1.0, manufacturing_capability/3.1.0.
- [ ] T008 [P] [US1] Create `UntpSchemaProvider` in `src/Common/Sorcha.Blueprint.Schemas/Services/UntpSchemaProvider.cs` — implement `IExternalSchemaProvider` with 4 curated schemas from `test.uncefact.org/vocabulary/untp/{type}/`. ProviderName="UNTP", DefaultSectorTags=["sustainability","commerce"]. No normalization needed (already Draft 2020-12). SourceUri pattern: `untp/{type}/{version}`. Mark version 0.6.1 as pre-release (< 1.0.0). Schemas: dpp, dcc, dte, dfr.
- [ ] T009 [US1] Register all 3 DPP providers in `src/Services/Sorcha.Blueprint.Service/Program.cs` — add `AddHttpClient<T>()` with Polly retry for each provider (BatteryPassSchemaProvider with base address `https://raw.githubusercontent.com/batterypass/BatteryPassDataModel/main/`, CatenaXSchemaProvider with `https://raw.githubusercontent.com/eclipse-tractusx/sldt-semantic-models/main/`, UntpSchemaProvider with `https://test.uncefact.org/vocabulary/untp/`), then `AddSingleton<IExternalSchemaProvider>()` for each. Follow existing pattern at lines 144-166.

**Checkpoint**: All 3 providers registered, tests pass, 16 schemas discoverable under "Sustainability" sector.

---

## Phase 3: User Story 2 — Monitor DPP Provider Health (Priority: P2)

**Goal**: Administrators see 3 independent DPP provider cards on Schema Providers admin page with health status, schema counts, and manual refresh.

**Independent Test**: Navigate to admin Schema Providers page → confirm `dpp-untp`, `dpp-batterypass`, `dpp-catenax` cards → verify each shows status, count, last fetch time → trigger manual refresh.

### Implementation for User Story 2

> No new code required — existing `SchemaIndexRefreshService` and admin UI automatically pick up registered `IExternalSchemaProvider` implementations. Each provider gets its own `SchemaProviderStatus` entry.

- [ ] T010 [US2] Verify `IsAvailableAsync` works correctly in all 3 providers — ensure each performs a HEAD request to its source URL and handles failures gracefully (log warning, return false). If not already implemented in T006-T008, add now.
- [ ] T011 [P] [US2] Add `IsAvailableAsync` tests to each provider test class — test both success (200 OK) and failure (timeout/500) scenarios using mock HttpMessageHandler in `tests/Sorcha.Blueprint.Schemas.Tests/Providers/BatteryPassSchemaProviderTests.cs`, `CatenaXSchemaProviderTests.cs`, `UntpSchemaProviderTests.cs`.

**Checkpoint**: Three provider cards visible on admin page. Manual refresh triggers independent fetches.

---

## Phase 4: User Story 3 — Cross-Sector Schema Discovery (Priority: P2)

**Goal**: DPP schemas appear under non-sustainability sectors (Commerce, Construction, Government) via cross-sector tags.

**Independent Test**: Filter Schema Library by "Commerce & Trade" → see DPP schemas alongside commerce schemas. Filter by "Construction & Planning" → see battery/construction DPP schemas.

### Implementation for User Story 3

- [ ] T012 [US3] Add per-schema sector tag overrides to `BatteryPassSchemaProvider` — CarbonFootprintForBatteries: ["sustainability","government"], SupplyChainDueDiligence: ["sustainability","commerce"]. Other Battery Pass schemas: ["sustainability","commerce"]. Ensure `GetCatalogAsync` returns the per-schema tags, not just provider defaults.
- [ ] T013 [P] [US3] Add per-schema sector tag overrides to `CatenaXSchemaProvider` — pcf: ["sustainability","government"], battery.battery_pass: ["sustainability","commerce","construction"], others: ["sustainability","commerce"].
- [ ] T014 [P] [US3] Add per-schema sector tag overrides to `UntpSchemaProvider` — dpp: ["sustainability","commerce"], dcc: ["sustainability","government"], dte: ["sustainability","commerce"], dfr: ["sustainability","commerce"].
- [ ] T015 [US3] Add sector tag tests to each provider test class — verify specific schemas return correct per-schema sector tags (not just default). Add to existing test files in `tests/Sorcha.Blueprint.Schemas.Tests/Providers/`.

**Checkpoint**: DPP schemas appear under at least 3 different sector filters (sustainability, commerce, government/construction).

---

## Phase 5: User Story 4 — DPP Schema Versioning Awareness (Priority: P3)

**Goal**: Schema detail pages show version info and pre-release/stable maturity indicators. Version changes deprecate old entries rather than deleting them.

**Independent Test**: View UNTP DPP schema detail → see "0.6.1" version and "Pre-release" badge. View Battery Pass schema → see "1.2.0" and "Stable" indicator.

### Implementation for User Story 4

- [ ] T016 [US4] Ensure version info is encoded in `ExternalSchemaResult.Url` or `Versions` field for all 3 providers — verify `SourceUri` includes version (e.g., `untp/dpp/0.6.1`). Existing `SchemaIndexEntryDocument` stores version from provider. Review and confirm each provider correctly populates version metadata during `GetCatalogAsync`.
- [ ] T017 [US4] Add pre-release detection logic — in each provider, determine if version < 1.0.0 (SemVer pre-release) and include this in schema description or metadata. UNTP 0.6.1 = pre-release, Battery Pass 1.2.0 = stable, Catena-X varies. Add "(Pre-release)" suffix to description for pre-release schemas.
- [ ] T018 [US4] Add version metadata tests — verify pre-release detection for UNTP (0.6.1 → pre-release), Battery Pass (1.2.0 → stable), and Catena-X (various). Test in existing provider test files.

**Checkpoint**: Schema detail pages show version + maturity indicators. Pre-release schemas clearly labelled.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Documentation, normalization verification, and integration validation

- [ ] T019 [P] Run full test suite (`dotnet test`) and verify no regressions across all 30 test projects
- [ ] T020 [P] Update `docs/reference/development-status.md` with DPP Schema Provider feature completion
- [ ] T021 Update `.specify/MASTER-TASKS.md` — mark DPP Schema Provider tasks complete
- [ ] T022 Run quickstart verification commands: `dotnet test --filter "FullyQualifiedName~BatteryPass"`, `dotnet test --filter "FullyQualifiedName~CatenaX"`, `dotnet test --filter "FullyQualifiedName~Untp"`

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — start immediately
- **US1 (Phase 2)**: Depends on Phase 1 (sector must exist, normaliser confirmed)
- **US2 (Phase 3)**: Depends on Phase 2 (providers must exist for health monitoring)
- **US3 (Phase 4)**: Depends on Phase 2 (providers must exist for cross-tagging)
- **US4 (Phase 5)**: Depends on Phase 2 (providers must exist for versioning)
- **Polish (Phase 6)**: Depends on all prior phases

### User Story Dependencies

- **US1 (P1)**: Depends on Phase 1 only — MUST complete first (creates all 3 providers)
- **US2 (P2)**: Depends on US1 — can run in parallel with US3/US4 after US1 completes
- **US3 (P2)**: Depends on US1 — can run in parallel with US2/US4 after US1 completes
- **US4 (P3)**: Depends on US1 — can run in parallel with US2/US3 after US1 completes

### Within Each User Story

- Tests written FIRST, confirmed to FAIL
- Models/schemas before services
- Core implementation before integration
- Story complete before moving to next priority

### Parallel Opportunities

- T003, T004, T005 (all US1 tests) can run in parallel
- T006, T007, T008 (all US1 providers) can run in parallel
- After US1 completes: US2, US3, US4 can all proceed in parallel
- T012, T013, T014 (US3 sector tags) can run in parallel
- T019, T020 (polish) can run in parallel

---

## Parallel Example: User Story 1 (MVP)

```bash
# Launch all US1 tests together (they test different providers):
Task: "BatteryPassSchemaProviderTests in tests/.../Providers/BatteryPassSchemaProviderTests.cs"
Task: "CatenaXSchemaProviderTests in tests/.../Providers/CatenaXSchemaProviderTests.cs"
Task: "UntpSchemaProviderTests in tests/.../Providers/UntpSchemaProviderTests.cs"

# Launch all US1 provider implementations together (different files):
Task: "BatteryPassSchemaProvider in src/.../Services/BatteryPassSchemaProvider.cs"
Task: "CatenaXSchemaProvider in src/.../Services/CatenaXSchemaProvider.cs"
Task: "UntpSchemaProvider in src/.../Services/UntpSchemaProvider.cs"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup (sector + normaliser check)
2. Complete Phase 2: US1 tests → US1 providers → DI registration
3. **STOP and VALIDATE**: 16 schemas from 3 providers discoverable under Sustainability
4. Commit and create PR for review

### Incremental Delivery

1. Setup + US1 → 3 providers working, 16 schemas → Deploy/Demo (MVP!)
2. Add US2 → Health monitoring for each provider → Deploy
3. Add US3 → Cross-sector discovery (commerce, construction, government) → Deploy
4. Add US4 → Version awareness + pre-release badges → Deploy
5. Polish → Docs, full test suite, cleanup → Final PR

---

## Notes

- [P] tasks = different files, no dependencies
- [Story] label maps task to specific user story for traceability
- Each user story is independently testable after US1 (the foundation)
- All providers follow the `FhirSchemaProvider` pattern — curated list, SemaphoreSlim cache, HttpClient via DI
- UNTP schemas need NO normalization (already Draft 2020-12)
- Battery Pass + Catena-X need Draft 4 → 2020-12 normalization
- Total: 22 tasks across 6 phases
