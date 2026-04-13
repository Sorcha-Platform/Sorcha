# Tasks: Verified Citizen v2

**Input**: Design documents from `/specs/103-verified-citizen-v2/`
**Prerequisites**: plan.md ✓, spec.md ✓, research.md ✓, data-model.md ✓, contracts/ ✓, quickstart.md ✓
**Tests**: Included per Sorcha constitution Principle IV (≥85% coverage on new code)
**Organization**: Tasks are grouped by user story so each phase ships as an independent PR.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Different files, no incomplete dependencies — safe to run in parallel
- **[Story]**: Maps to a user story (US1, US2, US3, US4)
- All file paths are absolute from repo root `C:\Projects\Sorcha`

## Path Conventions

Web app (microservices backend + Blazor WASM frontend):
- Backend services: `src/Services/Sorcha.{Service}.Service/`
- Shared libraries: `src/Common/Sorcha.{Library}/`
- Validator core: `src/Common/Sorcha.Validator.Core/`
- Frontend: `src/Apps/Sorcha.UI/Sorcha.UI.Core/`
- Schemas: `blueprints/schemas/sorcha-core/`
- Walkthroughs: `walkthroughs/{Name}/`
- Tests: `tests/Sorcha.{Project}.Tests/`

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Per-feature workspace prep. The repository, tooling, and CI are already configured.

- [x] T001 Verify local Sorcha dev environment is healthy by running `docker-compose up -d` and confirming Blueprint, Validator, Tenant, Wallet, Register, and UI services all reach Healthy state in the Aspire dashboard or via `docker compose ps`. Ensures the rest of the feature work has a working baseline.
- [x] T002 [P] Confirm `dotnet build` of the entire solution from `C:\Projects\Sorcha` completes with no warnings on a clean checkout of branch `103-verified-citizen-v2`. This is the cold-start baseline against which subsequent build runs are diffed.
- [x] T003 [P] Confirm `dotnet test --filter "Category=Smoke"` passes against the Docker stack so we know the existing test infrastructure is green before any new tests land.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Cross-cutting prerequisites that block multiple user stories.

**⚠️ CRITICAL**: This phase is intentionally minimal — Sorcha already has all the cross-cutting infrastructure (DI, EF Core, MongoDB, Redis, OpenTelemetry, JWT, rate limiting, Scalar OpenAPI). The only foundational task is reserving the new validator error code so that downstream guardrail work has a stable contract from day one.

- [x] T004 Reserve a new validator publish-time error code `VAL_BP_010` (or next free in the series) in `src/Services/Sorcha.Validator.Service/Models/ValidationErrorCodes.cs`. Add a constant and an XML doc summary referencing `specs/103-verified-citizen-v2/contracts/validator-publish-errors.md`. No behaviour wiring yet — that lands in T021.

**Checkpoint**: Foundation ready — all four user-story phases can now begin.

---

## Phase 3: User Story 1 — Open citizen submission for a public service (Priority: P1) 🎯 MVP

**Goal**: A public-org user can submit the first action of a citizen-facing service without pre-existing participant records, and the platform records them as the bound applicant for the rest of the instance. Includes the publish-time guardrail and the Redis cache layer.

**Independent Test**: Build any trivial blueprint with one open starting action and one reviewer action. Sign up as a public user, submit Action 1, verify the Instance records the binding and the reviewer action sees the bound applicant. The existing HaipVerifiedCitizen v1 walkthrough also passes after the walletMap fix (without any of the schema component or address lookup work).

### Tests for User Story 1 ⚠️

> **NOTE**: Write these tests FIRST, ensure they FAIL, then implement.

- [X] T005 [P] [US1] Write `InstanceBindingCacheTests` in `tests/Sorcha.Blueprint.Service.Tests/InstanceBindingCacheTests.cs` covering the six test cases from `contracts/instance-binding-cache.md` § Tests required: hit path, miss → instance store, miss → ledger replay, re-bind throws, Redis-down fallthrough, sliding TTL behaviour. Use `Testcontainers.Redis` or the existing Redis test helper. Tests MUST fail before T026.
- [X] T006 [P] [US1] Write `PublishGuardrailTests` in `tests/Sorcha.Validator.Service.Tests/PublishGuardrailTests.cs` covering the seven cases from `contracts/validator-publish-errors.md` § Tests required: pass with null wallet, pass with empty wallet, fail with populated wallet, multi-starting-action targeted reporting, non-starting-action passes, no-sender passes, non-existent participant id (existing rule fires first). Tests MUST fail before T021.
- [ ] T007 [P] [US1] Write `LateBindingIntegrationTest` in `tests/Sorcha.Blueprint.Service.IntegrationTests/LateBindingIntegrationTest.cs` covering the user-facing scenarios from spec.md US1 acceptance scenarios 1, 2, 4, and 5: first sender binds, second sender rejected, credential-bootstrapped happy path, publish-time guardrail rejection. Uses `WebApplicationFactory<Program>` against an in-memory test instance store + a faked Redis. Tests MUST fail before T026.
- [ ] T008 [P] [US1] Write `WalkthroughHaipVerifiedCitizenE2ETest` in `tests/Sorcha.UI.E2E.Tests/Docker/HaipVerifiedCitizenLateBindingTests.cs` covering the existing v1 walkthrough's end-to-end flow against Docker, asserting that submission succeeds without the wallet-not-authorized error after the walletMap fix. This test MUST fail before T009/T010 land.

### Implementation for User Story 1

- [x] T009 [US1] Edit `walkthroughs/HaipVerifiedCitizen/setup.ps1` to remove the `citizen` entry from `$walletMap`. Landed in PR #268.
- [x] T010 [US1] Edit `walkthroughs/HaipDrivingLicence/setup.ps1` to remove the `applicant` entry from its `$walletMap`. Landed in PR #268.
- [x] T011 [US1] Audit the remaining walkthroughs for other instances of pre-binding open participants. Produced `specs/103-verified-citizen-v2/audit-walkthroughs-t011.md` — SelfBuildHouse (2 blueprints) and HealthDeclaration flagged for a follow-up feature; ConstructionPermit, FormCoverage, PayloadTests, TradeFinance acceptable as-is. Landed in PR #268.
- [x] T012 [US1] XML doc on `Action.IsStartingAction` documenting the open contract. Landed in PR #268.
- [x] T013 [US1] XML doc on `Participant.WalletAddress` documenting the VAL_BP_010 rejection rule. Landed in PR #268.
- [x] T014a [US1] **Investigation**: persistence path is INTACT — produced `specs/103-verified-citizen-v2/investigation-t014a.md`. Landed in PR #268.
- [x] T014b [US1] Code comment on `ActionExecutionService.cs:309-343` confirming the persistence contract. Landed in PR #269.
- [x] T015 [US1] `InstanceBindingCacheOptions` (sliding TTL + key prefix). Landed in PR #269.
- [x] T016 [US1] `IInstanceBindingCache` interface (Get / Set / Invalidate). Landed in PR #269.
- [x] T017 [US1] `InstanceBindingCache` implementation using `IDistributedCache`. Two-tier fallback (cache → instance store); ledger replay deferred. Landed in PR #269.
- [x] T018 [US1] DI registration for `IInstanceBindingCache` + options in Blueprint Service `Program.cs`. Landed in PR #269.
- [x] T019 [US1] Wire cache write-through into `ActionExecutionService` late-bind block. Read-path intentionally stays in-process (rationale in code). Landed in PR #269.
- [x] T020 [US1] Publish-time guardrail rule 6a (`VAL_BP_010`) in Blueprint Service `ValidateBlueprint`. Landed in PR #269.
- [x] T021 [US1] Wire the new error code reserved in T004 into the validation engine at `src/Services/Sorcha.Validator.Service/Services/ValidationEngine.cs`. Ensure the error is emitted in the standard publish-error response shape with `code`, `severity`, `message`, `field`, `actionId`, `participantId`. (Depends on T004 + T020.)
- [x] T022 [P] [US1] Add a new section to `CLAUDE.md` under "Critical Patterns" titled "Open Participants & Late Binding" that summarises the contract and cross-references the blueprint-builder skill, the design spec, and `contracts/validator-publish-errors.md`. ~15 lines max. (Landed as section #9 under Critical Patterns.)
- [x] T023 [US1] Run `tests/Sorcha.Blueprint.Service.Tests/InstanceBindingCacheTests.cs` (T005) and verify all six cases now pass after T017 + T019.
- [x] T024 [US1] Run `tests/Sorcha.Validator.Service.Tests/PublishGuardrailTests.cs` (T006) and verify all seven cases now pass after T020 + T021.
- [ ] T025 [US1] Run `tests/Sorcha.Blueprint.Service.IntegrationTests/LateBindingIntegrationTest.cs` (T007) and verify all four scenarios pass.
- [ ] T026 [US1] Run `tests/Sorcha.UI.E2E.Tests/Docker/HaipVerifiedCitizenLateBindingTests.cs` (T008) against `docker-compose up -d` and verify the end-to-end Verified Citizen v1 walkthrough succeeds without `wallet not authorized` errors. (Depends on T009 + T017 + T019 + T020 + T021.)
- [ ] T027 [US1] Verify the OpenTelemetry metrics emitted by `InstanceBindingCache` (`sorcha.binding_cache.requests`, `sorcha.binding_cache.read_latency_ms`, etc. per `contracts/instance-binding-cache.md` § Telemetry) are visible in the Aspire dashboard during the E2E test run. Capture a screenshot or text dump of the relevant metrics in the PR description.

**Checkpoint**: At this point, User Story 1 is fully functional and shippable as PR 1. The Verified Citizen v1 walkthrough completes end-to-end. The publish guardrail blocks the foot-gun. The Redis cache reduces hot-path latency. The contract is documented in skills, CLAUDE.md, and code comments.

---

## Phase 4: User Story 2 — Reusable identity primitive library (Priority: P2)

**Goal**: A service designer can reference shared identity primitives by URI from a service definition. Each primitive arrives with its own validation, layout, and persona-autofill bindings. The new service is dramatically shorter than the v1 inline-schema equivalent.

**Independent Test**: Reference a single core component (e.g. `PostalAddress/v1`) from a throwaway blueprint. Render the form. Verify validation, layout, and persona autofill activate without per-blueprint setup.

### Tests for User Story 2 ⚠️

- [x] T028 [US2] Wrote `SchemaRefResolverTests` in `tests/Sorcha.Blueprint.Service.Tests/SchemaRefResolverTests.cs` (resolver lives in Blueprint Service, not Validator Service — see T040 note). 13 cases: simple flatten, no-mutation contract, layout transclusion + override (x-sections + x-pages), per-property metadata preserved, multiple refs in one schema, direct + transitive cycle detection, unknown URI, did:sorcha rejection, internal #/$defs ref pass-through, refless schema round-trip.
- [x] T029 [P] [US2] Write `SorchaDateTokenResolverTests` in `tests/Sorcha.Validator.Service.Tests/SorchaDateTokenResolverTests.cs` covering: `today` resolves to current date; `today+18Y` adds 18 years; `today-1D` subtracts 1 day; literal ISO date passes through; invalid token (`tomorrow`) throws; timezone handling. Tests MUST fail before T038.
- [x] T030 [P] [US2] Write `CoreSchemaSeedServiceTests` in `tests/Sorcha.Blueprint.Service.Tests/CoreSchemaSeedServiceTests.cs` covering: scans `blueprints/schemas/sorcha-core/` at startup; rejects primitive with mismatched filename and `$id`; rejects unknown `x-persona` path; rejects invalid date token; rejects `$ref` cycle (deferred to resolver actually — adjust accordingly); idempotent on re-run. Tests MUST fail before T036.
- [x] T031 [P] [US2] Write `PersonaMiddleNameTests` in `tests/Sorcha.Tenant.Service.IntegrationTests/PersonaMiddleNameTests.cs` covering: PUT /me/persona with middleName persists and round-trips; existing personas without middleName continue to read as null; PersonaReadModelV1 surfaces middleName when present. Tests MUST fail before T033.
- [ ] T032 [P] [US2] Write `IdentityPrimitiveRenderingE2ETest` in `tests/Sorcha.UI.E2E.Tests/Docker/IdentityPrimitiveRenderingTests.cs` covering: a blueprint that references `PostalAddress/v1` renders the address fields with the primitive's layout and persona autofill. Tests MUST fail before T044.

### Implementation for User Story 2

#### 2a. Persona model + migration

- [x] T033 [US2] Add optional `middleName` (string?, max 100 chars) to `src/Common/Sorcha.Tenant.Models/Persona/PersonaAttributesV1.cs`. Update the wire model `PersonaReadModelV1` similarly with `MiddleName : PersonaAttribute<string?>?`. Update `PersonaCryptoService` if it has explicit field projections (it shouldn't — the ciphertext is the whole payload).
- [x] T034 [US2] Generate an EF Core migration if `PlatformUserPersona` has a column-mapped middleName (it should not — middleName is inside the encrypted blob, no schema change). Verify by reading `src/Services/Sorcha.Tenant.Service/Data/SorchaTenantDbContext.cs` and confirming PlatformUserPersona is stored as a single ciphertext column. Document the verification in the PR.
- [x] T035 [US2] Update `src/Services/Sorcha.Tenant.Service/Endpoints/PersonaEndpoints.cs` to accept and return `middleName` in PUT /me/persona and GET /me/persona. Per `contracts/persona-middlename-api.yaml`. Existing endpoints — change is additive only. **(Complete without code edits — the endpoint already takes `PersonaAttributesV1` / returns `PersonaReadModelV1`, so `MiddleName` flows through automatically via T033.)**

#### 2b. Schema sector + seed service

- [x] T036 [P] [US2] Add a `core` entry to `src/Services/Sorcha.Blueprint.Service/Models/SchemaSector.cs` `All` static list. ID `core`, display name `"Sorcha Core Primitives"`, description `"Platform-managed reusable identity primitives"`.
- [x] T037 [US2] Implement `src/Services/Sorcha.Blueprint.Service/Services/CoreSchemaSeedService.cs` as an `IHostedService` modeled on `src/Services/Sorcha.Blueprint.Service/Templates/TemplateSeedService.cs:35-100`. Scan `blueprints/schemas/sorcha-core/*.json` at startup. Multi-path resolution (content root → base dir → walk up parent dirs). Validate each primitive per `contracts/identity-primitive-format.md` § Validation rules applied by the seed service (rules 1-9). Idempotent upsert into the Mongo schema index. Register in DI in Program.cs.

#### 2c. Date token resolver

- [x] T038 [P] [US2] Implement `src/Common/Sorcha.Validator.Core/Tokens/SorchaDateTokenResolver.cs`. Public static `DateOnly Resolve(string token, DateOnly today)`. Token grammar: `today | today[+-]N{D|M|Y}`. Throws `FormatException` on invalid tokens. Pure function; no DI.

#### 2d. Schema $ref resolver

- [x] T039 [US2] URI handler logic implemented inline in `SchemaRefResolver.FlattenObject` rather than as a separate handler class — three branches: HTTPS Sorcha-core (resolves), DID Sorcha (throws SchemaRefResolutionException), other ($defs / external) pass through. Resolver lives in Blueprint Service alongside `ICoreSchemaRepository` because that is where the seed service stores primitives — moving it to Validator Service would force a cross-service dependency for one lookup. Validator-side wiring (T041) is deferred to wave 6 with a different design (flatten at publish time in Blueprint Service, validator receives flat schema).
- [x] T040 [US2] Implemented `src/Services/Sorcha.Blueprint.Service/Services/SchemaRefResolver.cs` (with `ISchemaRefResolver` interface + `SchemaRefResolutionException`). Sync `Flatten(JsonNode)` (no async needed — repository lookup is in-memory). Layout merge per the contract: child wins for x-pages/x-sections/x-introduction/x-width; component wins for everything else including per-property x-persona, x-address-lookup, formatMaximum etc. DI registered in Blueprint Service Program.cs. 13 unit tests pass.
- [x] T041 [US2] **Wire `SchemaRefResolver` into the Blueprint Service publish path** (design revised from the original spec). The task breakdown said to wire into `Sorcha.Validator.Service.ValidationEngine`, but that would have required a cross-service dependency for a one-shot lookup. Instead, the resolver lives in Blueprint Service alongside `ICoreSchemaRepository`, and `PublishService.PublishAsync` now calls `FlattenActionSchemas` immediately after loading the draft and before validation. The published snapshot captures the flat form, so the validator and the register never see `$ref` markers — no validator-side work needed. A short-circuit scans each schema's raw JSON for the literal `"$ref"` token before paying the parse → flatten → re-serialise round-trip, so pre-existing blueprints that don't use primitives pass through with zero CPU cost. Superseded `JsonDocument` instances are disposed before replacement. Failures bubble up as publish-time errors with the offending URI surfaced in the error message. Three tests: happy path flatten, short-circuit for refless schema, unknown-ref publish failure. Landed in wave 6 as PR #273.

#### 2e. x-persona declarative bindings

- [x] T042 [US2] **Verified no code change needed.** Investigated `PersonaAutofillResolver` (line 122-148, shipped in Feature 092): the resolver already reads `x-persona` declaratively from each property, with name-heuristic as the explicit fallback. The original task assumption (that bindings need to flow through `SchemaLayoutParser.PersonaBindings`) is unnecessary because the resolver walks the schema directly. SchemaLayoutParser is unchanged.
- [x] T043 [US2] **Verified no code change needed.** `PersonaAutofillResolver.WalkProperties` already prefers explicit `x-persona` over name heuristics — see Priority order (1) author default, (2) x-persona explicit, (3) inference allowlist. The task spec described work that was already done in Feature 092. PersonaAutofillResolver is unchanged.

#### 2f. Initial primitive set

- [x] T044 [P] [US2] Create `blueprints/schemas/sorcha-core/PersonName.v1.json` with `$id: "https://schemas.sorcha.dev/core/PersonName/v1"`, properties `givenName` (required, x-persona "givenName"), `middleName` (optional, x-persona "middleName"), `familyName` (required, x-persona "familyName"), `fullName` (optional, x-persona "fullName"). Single x-section grouping all four fields under "Name" with horizontal layout for given/middle/family.
- [x] T045 [P] [US2] Create `blueprints/schemas/sorcha-core/DateOfBirth.v1.json` with `$id: "https://schemas.sorcha.dev/core/DateOfBirth/v1"`, single property `dateOfBirth` (required, format date, formatMaximum "today", x-persona "dateOfBirth").
- [x] T046 [P] [US2] Create `blueprints/schemas/sorcha-core/EmailAddress.v1.json` with `$id: "https://schemas.sorcha.dev/core/EmailAddress/v1"`, single property `email` (required, format email, x-persona "defaultEmail").
- [x] T047 [P] [US2] Create `blueprints/schemas/sorcha-core/EmailAddressList.v1.json` with `$id: "https://schemas.sorcha.dev/core/EmailAddressList/v1"`, single property `emails` (array, minItems 1, maxItems 5, items object with email + isDefault, x-persona "emails").
- [x] T048 [P] [US2] Create `blueprints/schemas/sorcha-core/PostalAddress.v1.json` with `$id: "https://schemas.sorcha.dev/core/PostalAddress/v1"`, properties `line1` (required, x-persona "address.line1"), `line2` (optional, x-persona "address.line2"), `town` (required, x-persona "address.town"), `region` (optional, x-persona "address.region"), `postcode` (required, x-persona "address.postcode", x-address-lookup true), `country` (required, x-persona "address.country"). Three x-sections: Street (line1+line2), Locality (town+region+postcode horizontal), Country (country). Required: line1, town, postcode, country.

#### 2g. Validation pass

- [x] T049 [US2] Run `tests/Sorcha.Validator.Service.Tests/SchemaRefResolverTests.cs` (T028) and verify all cases pass after T040 + T044-T048.
- [x] T050 [US2] Run `tests/Sorcha.Validator.Service.Tests/SorchaDateTokenResolverTests.cs` (T029) and verify all cases pass after T038.
- [x] T051 [US2] Run `tests/Sorcha.Blueprint.Service.Tests/CoreSchemaSeedServiceTests.cs` (T030) and verify all cases pass after T037 + T044-T048.
- [ ] T052 [US2] Run `tests/Sorcha.Tenant.Service.IntegrationTests/PersonaMiddleNameTests.cs` (T031) and verify the round-trip works end-to-end after T033 + T035.
- [ ] T053 [US2] Run `tests/Sorcha.UI.E2E.Tests/Docker/IdentityPrimitiveRenderingTests.cs` (T032) and verify the rendering test passes against Docker after all of US2 lands.

**Checkpoint**: At this point, User Story 2 is fully functional and shippable as PR 2. The five core primitives are loaded at startup, indexed in Mongo, resolvable via `$ref`, transcluded with layout merge, persona-bound declaratively, and rendered correctly. PersonaAttributesV1 carries `middleName`. The `SchemaRefResolver` and `SorchaDateTokenResolver` ship with full unit coverage.

---

## Phase 5: User Story 3 — Postcode-driven address autofill (Priority: P3)

**Goal**: A citizen filling in a postal address types a postcode and gets either full-address autocomplete (when an OS Places-class provider is configured) or postcode validation + town/region autofill (default postcodes.io provider). Graceful degradation to plain text if no provider is available.

**Independent Test**: Render any form containing `PostalAddress/v1`, type a known UK postcode, verify the address fields populate. Test the no-provider case by clearing config — the form must still work.

**Story-level dependency**: US3 depends on US2 having shipped the `x-address-lookup` keyword recognition in the form renderer. Implementation order: US2 → US3 (or US3 in parallel with US2 final integration).

### Tests for User Story 3 ⚠️

- [x] T054 [P] [US3] Write `PostcodesIoProviderTests` in `tests/Sorcha.AddressLookup.Tests/Providers/PostcodesIoProviderTests.cs` covering: valid UK postcode returns ValidateOnly result with town/region; invalid postcode returns isValid=false; HTTP 404 from provider returns isValid=false; HTTP 500 returns availability false. Use `Microsoft.AspNetCore.TestHost` or a stubbed `HttpMessageHandler`. Tests MUST fail before T058.
- [x] T055 [P] [US3] Write `OsPlacesProviderTests` in `tests/Sorcha.AddressLookup.Tests/Providers/OsPlacesProviderTests.cs` covering: valid postcode returns FullAddress candidates; missing API key throws on construction; rate-limit response returns availability false; malformed candidate response logged and skipped. Tests MUST fail before T059.
- [x] T056 [P] [US3] Write `AddressLookupServiceTests` in `tests/Sorcha.AddressLookup.Tests/AddressLookupServiceTests.cs` covering: provider selection prefers FullAddress over ValidateOnly for the country; falls back when preferred provider is unavailable; returns "none" provider when no provider supports the country. Tests MUST fail before T060.
- [ ] T057 [P] [US3] Write `AddressLookupEndpointsTests` in `tests/Sorcha.Tenant.Service.IntegrationTests/AddressLookupEndpointsTests.cs` covering both endpoints from `contracts/address-lookup-api.yaml`: POST /api/address-lookup/postcode happy path, 401 unauthenticated, 400 malformed postcode, 429 rate-limited; GET /api/address-lookup/providers returns provider info. Tests MUST fail before T062.

### Implementation for User Story 3

#### 3a. Library scaffolding

- [x] T058 [P] [US3] Create new csproj `src/Common/Sorcha.AddressLookup/Sorcha.AddressLookup.csproj` targeting net10.0, nullable enable, License header. Reference `Sorcha.ServiceDefaults`.
- [x] T059 [US3] Add `src/Common/Sorcha.AddressLookup/Sorcha.AddressLookup.csproj` to the solution `Sorcha.sln`. (Sequential after T058 — depends on the csproj file existing.)
- [x] T060 [P] [US3] Create `src/Common/Sorcha.AddressLookup/IAddressLookupProvider.cs` defining the interface from research.md decision 11: `ProviderName`, `Capability`, `SupportedCountries`, `IsAvailableAsync`, `LookupAsync`.
- [x] T061 [P] [US3] Create `src/Common/Sorcha.AddressLookup/AddressLookupCapability.cs` enum: `ValidateOnly`, `FullAddress`.
- [x] T062 [P] [US3] Create `src/Common/Sorcha.AddressLookup/AddressLookupResult.cs` and `AddressCandidate.cs` records matching the shapes in `contracts/address-lookup-api.yaml`. Records should be JSON-serializable for the wire and consumable directly by the Tenant Service endpoints.
- [x] T063 [P] [US3] Create `src/Common/Sorcha.AddressLookup/AddressLookupProviderInfo.cs` value object matching the contracts schema.

#### 3b. Providers

- [x] T064 [US3] Implement `src/Common/Sorcha.AddressLookup/Providers/PostcodesIoProvider.cs`. Constructor takes `HttpClient` (registered via IHttpClientFactory) + `ILogger<PostcodesIoProvider>`. ProviderName `"postcodes.io"`, Capability ValidateOnly, SupportedCountries `["GB"]`. `LookupAsync` calls `https://api.postcodes.io/postcodes/{postcode}`, parses validity + town + region + lat/long, returns `AddressLookupResult` with capability ValidateOnly. `IsAvailableAsync` does a HEAD or simple GET.
- [x] T065 [US3] Implement `src/Common/Sorcha.AddressLookup/Providers/OsPlacesProvider.cs`. Constructor takes `HttpClient`, `IOptions<OsPlacesOptions>` (ApiKey, BaseUrl), `ILogger<OsPlacesProvider>`. ProviderName `"os-places"`, Capability FullAddress, SupportedCountries `["GB"]`. `LookupAsync` calls OS Places API with the API key, parses candidates into `AddressCandidate` list. `IsAvailableAsync` checks API key presence + does a probe call.
- [x] T066 [US3] Implement `src/Common/Sorcha.AddressLookup/AddressLookupService.cs`. Constructor takes `IEnumerable<IAddressLookupProvider>` + `ILogger<AddressLookupService>`. Method `LookupAsync(postcode, countryHint, ct)` selects the most capable available provider for the country using the algorithm from research.md decision 11. Falls back gracefully to a "none" result.

#### 3c. DI extensions

- [x] T067 [US3] Implement `src/Common/Sorcha.AddressLookup/ServiceCollectionExtensions.cs` with `AddSorchaAddressLookup(IServiceCollection services, IConfiguration config)`. Registers `AddressLookupService`, `PostcodesIoProvider` (always), and `OsPlacesProvider` (only when `Tenant:AddressLookup:OsPlaces:ApiKey` is configured). Use `IHttpClientFactory` for both providers.

#### 3d. Tenant Service endpoints

- [x] T068 [US3] Create `src/Services/Sorcha.Tenant.Service/Endpoints/AddressLookupEndpoints.cs` mapping the two endpoints from `contracts/address-lookup-api.yaml`. Both auth-gated via the existing JWT bearer policy and rate-limited via `RateLimitPolicies.Api`. Use `.WithSummary()` / `.WithDescription()` / Scalar OpenAPI per the constitution.
- [x] T069 [US3] Wire `AddSorchaAddressLookup` into `src/Services/Sorcha.Tenant.Service/Program.cs` DI. Wire `MapAddressLookupEndpoints` into the endpoint mapping. Add `Tenant:AddressLookup:OsPlaces:ApiKey` to `appsettings.json` as a placeholder (empty string in dev; populated only in prod via secrets).

#### 3e. UI control + renderer dispatch

- [x] T070 [US3] Create `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Forms/PostcodeLookupField.razor` per quickstart.md and the design spec. Three states: no provider (plain text), ValidateOnly (postcode field with tick + town/region autofill), FullAddress (postcode field with "Find address" button → modal pick list → autofills siblings via JsonPointer-style lookup). Calls `/api/address-lookup/providers` once on init to determine state, calls `/api/address-lookup/postcode` on user action.
- [x] T071 [US3] Update `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Forms/SorchaFormRenderer.razor` to dispatch a property carrying `x-address-lookup: true` to `PostcodeLookupField` instead of a plain text input. Preserve all other dispatch paths (file upload, persona autofill, etc).
- [x] T072 [US3] Add `data-testid` attributes to `PostcodeLookupField.razor` for `postcode-input`, `postcode-validate-tick`, `postcode-find-address-button`, `postcode-candidate-modal`, `postcode-candidate-{i}` per the `sorcha-ui` skill's data-testid convention. (Sequential after T070 — edits the same file.)

#### 3f. Validation pass

- [x] T073 [US3] Run all tests in `tests/Sorcha.AddressLookup.Tests/` (T054-T056) and verify they pass after T058-T067.
- [ ] T074 [US3] Run `tests/Sorcha.Tenant.Service.IntegrationTests/AddressLookupEndpointsTests.cs` (T057) and verify the endpoints work end-to-end after T068 + T069.
- [ ] T075 [P] [US3] Write and run `tests/Sorcha.UI.E2E.Tests/Docker/PostcodeLookupFieldTests.cs` covering all five US3 acceptance scenarios from spec.md against Docker. Use Playwright via the existing `AuthenticatedDockerTestBase`.
- [x] T076 [P] [US3] Verify graceful degradation manually: stop Tenant Service, edit `appsettings.json` to remove all providers (or set Postcodes.io as unavailable), restart, render a form with `PostalAddress/v1`, confirm the postcode field renders as plain text with no lookup affordance and submission still works.

**Checkpoint**: At this point, User Story 3 is fully functional and shippable as PR 3. The `PostcodeLookupField` renders correctly under all four states (no provider, ValidateOnly, FullAddress, error). Tenant Service hosts both endpoints. The library has full unit coverage.

---

## Phase 6: User Story 4 — Verified Citizen v2 end-to-end (Priority: P4)

**Goal**: The Verified Citizen workflow uses all three platform improvements — open submission, reusable identity primitives, postcode lookup — and successfully issues a `VerifiedCitizenCredential` to the citizen's external HAIP wallet via QR. Includes the downstream Driving Licence check that this credential bootstraps another service.

**Independent Test**: Fresh public account on a fresh deployment. Run through the Verified Citizen application end-to-end. Verify the credential lands in an external HAIP wallet with all expected claims.

### Tests for User Story 4 ⚠️

- [ ] T077 [P] [US4] Write `VerifiedCitizenV2E2ETests` in `tests/Sorcha.UI.E2E.Tests/Docker/VerifiedCitizenV2Tests.cs` covering all five US4 acceptance scenarios from spec.md: persona autofill, postcode lookup, assessor review, credential issuance, downstream consumption. Uses `AuthenticatedDockerTestBase`. Page object in `tests/Sorcha.UI.E2E.Tests/PageObjects/VerifiedCitizenV2Page.cs`. Tests MUST fail before T078.

### Implementation for User Story 4

- [x] T078 [US4] Rewrite `walkthroughs/HaipVerifiedCitizen/blueprints/verified-citizen.json` to use `$ref`s to the five core primitives. Bump blueprint id to include `v2-` and version to 2. Action 1 schema becomes ~50 lines (down from ~150) using the worked example in the design spec § "Verified Citizen v2 — the worked example". Action 2 unchanged except for the `claimMappings` paths being nested (`/name/givenName`, `/dob/dateOfBirth`, `/email/email`, `/address`).
- [x] T079 [US4] Update `walkthroughs/HaipVerifiedCitizen/setup.ps1` to bump the blueprint id alias used in subsequent steps and to align the publish-time blueprint version. Verify the `walletMap` does NOT include `citizen` (already done in T009; this is a confirmation step in case T078 changes anything).
- [x] T080 [US4] Update `walkthroughs/HaipVerifiedCitizen/run.ps1` so that the persona claim mappings used in the test assertions match the new nested paths from T078. Verify the SD-JWT VC assertion still finds `givenName`, `middleName`, `familyName`, `dateOfBirth`, `email`, `address`.
- [x] T081 [P] [US4] Update `walkthroughs/HaipDrivingLicence/blueprints/driving-licence.json` so its `credentialRequirements` for the VerifiedCitizenCredential reference any new claim names (notably `middleName`). This proves the v2 credential bootstraps the downstream service correctly.
- [x] T082 [P] [US4] Update `walkthroughs/HaipDrivingLicence/run.ps1` if needed to align with the v2 credential claims. Should be minimal — v1 claims still work because middleName is additive.
- [x] T083 [US4] Update `walkthroughs/HaipVerifiedCitizen/README.md` to describe the v2 flow, cross-reference the design and spec, and explain the open-participant late-binding contract for any human reading the walkthrough.
- [x] T084 [US4] Run `walkthroughs/HaipVerifiedCitizen/setup.ps1 -Profile gateway` against local Docker, then `run.ps1`, and capture the credential delivery output. Verify the SD-JWT VC contains all six claims (givenName, middleName, familyName, dateOfBirth, email, address) with correct values.
- [x] T085 [US4] Run `walkthroughs/HaipDrivingLicence/setup.ps1 -Profile gateway` then `run.ps1` chained after T084 to verify the downstream credential bootstrap works end-to-end. The applicant should be late-bound by HAIP presentation of the VerifiedCitizenCredential issued in T084.
- [ ] T086 [US4] Run T077 (`VerifiedCitizenV2E2ETests`) and verify all scenarios pass against Docker.
- [ ] T087 [US4] Run the entire Verified Citizen v2 + Driving Licence chain against `n1.sorcha.dev` per the network-bootstrap skill. Reset n1, push the branch images via `docker-publish.yml`, redeploy, and run both walkthroughs in `n1` profile. Document the run in the PR description.

**Checkpoint**: User Story 4 ships. The Verified Citizen v2 blueprint runs end-to-end on local Docker and on n1. The credential is delivered to the citizen's HAIP wallet. The downstream Driving Licence service consumes the credential and binds the applicant via HAIP presentation. All four user stories are now shippable.

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Tidy, document, verify performance, run regression sweep across all walkthroughs.

- [ ] T088 [P] Update `walkthroughs/README.md` to describe the new open-participant pattern and reference the skills update.
- [ ] T089 [P] Update `docs/reference/development-status.md` to mark the four phases of this feature as complete (with their PR numbers). 
- [ ] T090 [P] Update `docs/reference/API-DOCUMENTATION.md` with the two new address-lookup endpoints and the persona middleName field addition.
- [ ] T091 [P] Update `src/Services/Sorcha.Tenant.Service/README.md` to describe the AddressLookup feature, hosted providers, and configuration.
- [ ] T092 [P] Update `src/Services/Sorcha.Blueprint.Service/README.md` to describe the CoreSchemaSeedService, the SchemaRefResolver, and the InstanceBindingCache.
- [ ] T093 Run a performance sanity check: the `sorcha.binding_cache.read_latency_ms` metric should show p99 < 10ms for cache-hit reads under a representative load (10 concurrent walkthrough runs). Capture in the PR.
- [x] T094 Run a regression sweep: execute every walkthrough under `walkthroughs/` (ConstructionPermit, SelfBuildHouse, HaipVerifiedCitizen, HaipDrivingLicence, etc.) for one cycle each against local Docker and verify none broke. Document results.
- [ ] T095 [P] Run `dotnet test` for the entire solution and verify no regressions; coverage on new code is ≥85% per the constitution.
- [x] T096 Verify the `verifiable-credentials` skill, `walkthrough-builder` skill, and `blueprint-builder` skill cross-reference the v2 work appropriately. Add any missing cross-references found during the regression sweep.
- [ ] T097 Run `quickstart.md` end-to-end as a checklist verification: a developer who has never seen this feature can follow the doc to add a new identity primitive, override layout, add a new address provider, and debug late binding.
- [x] T098 If anything tactical changed during implementation (endpoint auth, csproj packaging, x-address-lookup PR placement), update `docs/superpowers/specs/2026-04-13-verified-citizen-v2-design.md` § "Open questions for planning" to reflect the resolved decision.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 (Setup)**: No dependencies — can start immediately.
- **Phase 2 (Foundational)**: Depends on Setup. Single task (T004) reserves the error code; everything else in this feature is per-story.
- **Phase 3 (US1)**: Depends on Foundational. Independent of US2/US3/US4.
- **Phase 4 (US2)**: Depends on Foundational. Independent of US1/US3 in code, but US4 depends on US2.
- **Phase 5 (US3)**: Depends on Foundational + US2 having shipped `x-address-lookup` keyword recognition (T071 in US3 actually owns this — the renderer dispatch — so US3 is self-contained at the renderer layer; US2 just provides the `PostalAddress/v1` primitive that declares the keyword).
- **Phase 6 (US4)**: Depends on US1 + US2 + US3 all shipping. Is the integration consumer.
- **Phase 7 (Polish)**: Depends on all four user stories being complete.

### User Story Dependencies

- **US1 (P1, MVP)**: No dependencies on other stories. Ship first.
- **US2 (P2)**: No dependencies on other stories. Ship after or in parallel with US1.
- **US3 (P3)**: Architecturally independent, but US4 needs the `PostalAddress/v1` primitive from US2 to declare `x-address-lookup`. Ship after US2.
- **US4 (P4)**: Depends on US1 (open submission), US2 (primitives), and US3 (postcode lookup) all shipping. Ship last.

### Within Each User Story

- Tests (T005-T008, T028-T032, T054-T057, T077) MUST be written before implementation and MUST fail.
- Models / records / interfaces before services that consume them.
- Services before endpoints / DI registration.
- Implementation before integration tests.
- Each user story phase ends with a checkpoint that confirms the story is independently demoable.

### Parallel Opportunities

- **Setup**: T002, T003 are [P] within Phase 1.
- **US1**: Tests T005-T008 are all [P]. Within implementation, T011-T013 are [P] (different files); T015-T016 are [P] (different new files); T022 is [P] with everything else (CLAUDE.md, no other US1 task touches it). T014a → T014b is sequential (T014b is conditional on T014a's findings). T020 is sequential after T018 because both edit `Program.cs`.
- **US2**: All four test tasks T028-T031 are [P]. Within implementation, T036 + T038 are [P]; T044-T048 (the five primitive files) are all [P]; T039 is [P] with T038.
- **US3**: All four test tasks T054-T057 are [P]. Within implementation, T058 + T060-T063 (scaffolding) are all [P] — six different new files; T059 (add to sln) is sequential after T058 and therefore NOT [P]; T064 + T065 (different providers, different new files) are naturally parallel and could be marked [P] at execution time even though the file only lists them sequentially; T072 is sequential after T070 because both edit `PostcodeLookupField.razor`; T075 + T076 are [P].
- **US4**: T081 + T082 are [P] (different files in HaipDrivingLicence).
- **Polish**: T088-T092 are all [P]; T095 is [P] with T094.

### Cross-Story Parallelism

If multiple developers are available, US1 and US2 can be worked on in parallel after Phase 2 completes. US3 should wait for US2 to land the renderer changes (or coordinate on `T071` directly). US4 must wait for all three.

---

## Parallel Examples

### Example: US1 test wave (write all four tests before any implementation)

```text
Task: T005 [P] [US1] Write InstanceBindingCacheTests in tests/Sorcha.Blueprint.Service.Tests/InstanceBindingCacheTests.cs
Task: T006 [P] [US1] Write PublishGuardrailTests in tests/Sorcha.Validator.Service.Tests/PublishGuardrailTests.cs
Task: T007 [P] [US1] Write LateBindingIntegrationTest in tests/Sorcha.Blueprint.Service.IntegrationTests/LateBindingIntegrationTest.cs
Task: T008 [P] [US1] Write WalkthroughHaipVerifiedCitizenE2ETest in tests/Sorcha.UI.E2E.Tests/Docker/HaipVerifiedCitizenLateBindingTests.cs
```

All four touch different files and have no dependencies on each other.

### Example: US2 primitive creation wave

```text
Task: T044 [P] [US2] Create blueprints/schemas/sorcha-core/PersonName.v1.json
Task: T045 [P] [US2] Create blueprints/schemas/sorcha-core/DateOfBirth.v1.json
Task: T046 [P] [US2] Create blueprints/schemas/sorcha-core/EmailAddress.v1.json
Task: T047 [P] [US2] Create blueprints/schemas/sorcha-core/EmailAddressList.v1.json
Task: T048 [P] [US2] Create blueprints/schemas/sorcha-core/PostalAddress.v1.json
```

Five different files, no dependencies on each other.

### Example: US3 library scaffolding wave

```text
Task: T058 [P] [US3] Create new csproj src/Common/Sorcha.AddressLookup/Sorcha.AddressLookup.csproj
Task: T060 [P] [US3] Create src/Common/Sorcha.AddressLookup/IAddressLookupProvider.cs
Task: T061 [P] [US3] Create src/Common/Sorcha.AddressLookup/AddressLookupCapability.cs
Task: T062 [P] [US3] Create src/Common/Sorcha.AddressLookup/AddressLookupResult.cs
Task: T063 [P] [US3] Create src/Common/Sorcha.AddressLookup/AddressLookupProviderInfo.cs
```

Six different files (T059 to add to sln after T058 lands).

---

## Implementation Strategy

### MVP First (User Story 1 only)

The minimum shippable product is User Story 1: open citizen submission. After Phase 3 completes, the existing Verified Citizen v1 walkthrough succeeds end-to-end without any of the schema component or address lookup work, the publish guardrail blocks the foot-gun, and the Redis cache improves hot-path latency. This alone is a meaningful platform improvement and a working MVP.

1. Complete Phase 1 (Setup): T001-T003
2. Complete Phase 2 (Foundational): T004
3. Complete Phase 3 (US1): T005-T027
4. **STOP and VALIDATE**: run T026 against Docker and T087 against n1 (the walkthrough portion only)
5. Ship as PR 1

### Incremental Delivery

PR 1 → PR 2 → PR 3 → PR 4 in priority order:

1. **PR 1 (US1)**: Open starting actions. Ships the bug fix, the guardrail, the cache, and the walkthrough rewrite for HaipVerifiedCitizen v1.
2. **PR 2 (US2)**: Identity primitives. Ships the five core components, the resolver, the date token vocabulary, and the persona middleName addition. No consumers yet.
3. **PR 3 (US3)**: Address lookup. Ships the library, the providers, the Tenant endpoints, and the UI control. No consumers yet.
4. **PR 4 (US4)**: Verified Citizen v2 blueprint. Rewrites the blueprint and walkthroughs to consume PR 1 + PR 2 + PR 3. The integration test.

Each PR can be merged and demoed independently. PR 4 is the headline.

### Parallel Team Strategy

With multiple developers:

1. Whole team completes Phase 1 (Setup) + Phase 2 (Foundational) — trivially fast (just verifying env and reserving the error code).
2. Once Phase 2 is done:
   - Developer A: US1 (Phase 3) — open actions
   - Developer B: US2 (Phase 4) — identity primitives
   - Developer C: US3 (Phase 5) — address lookup. Coordinates with B on `T071` (renderer dispatch) — either B owns it as part of US2's renderer touch, or C owns it after B lands the keyword recognition.
3. After all three phases land, one developer assembles US4 (Phase 6).
4. Whole team contributes to Phase 7 polish.

---

## Notes

- [P] tasks = different files, no dependencies. Safe to dispatch in parallel.
- [Story] label maps task to the user story it serves (US1, US2, US3, US4). Foundational and Polish tasks have no story label.
- Each user story phase ends with a working, demoable increment.
- Tests written first; verified to fail before implementation. (xUnit + FluentAssertions + Moq for unit/integration; Playwright NUnit for E2E.)
- Constitution coverage target: ≥85% on new code. Sorcha baseline is 80%.
- All new endpoints emit OpenTelemetry traces and structured logs (no string interpolation) per Principle VIII.
- All new endpoints document via Scalar + `.WithSummary()` / `.WithDescription()` per Principle III. Never use Swagger.
- License header on every new file: `// SPDX-License-Identifier: MIT` + `// Copyright (c) 2026 Sorcha Contributors`.
- Each phase lands as one commit (or a small number of logically grouped commits) and ships as one PR.
- Pre-existing failing tests (`ParticipantTests.Constructor_ShouldInitializeWithDefaults`, `ValidatorRegistryApprovalTests.RejectValidatorAsync` — see project memory) are NOT touched by this feature and remain pre-existing. New tests are isolated from them.

---

## Task count summary

| Phase | Story | Task count | Includes tests? |
|---|---|---|---|
| Phase 1 — Setup | — | 3 | — |
| Phase 2 — Foundational | — | 1 | — |
| Phase 3 — US1 | Open starting actions | 24 (T014 split into T014a + T014b) | Yes (T005-T008) |
| Phase 4 — US2 | Identity primitives | 26 | Yes (T028-T032) |
| Phase 5 — US3 | Address lookup | 23 | Yes (T054-T057, T075) |
| Phase 6 — US4 | Verified Citizen v2 | 11 | Yes (T077) |
| Phase 7 — Polish | — | 11 | — |
| **Total** | | **99** | |

| Story | Tasks | Parallel-marked | Independent test |
|---|---|---|---|
| US1 | 24 | 8 | Run HaipVerifiedCitizen v1 walkthrough end-to-end after the walletMap fix; submission must succeed |
| US2 | 26 | 13 | Reference any core primitive from a throwaway blueprint and verify rendering / validation / autofill |
| US3 | 23 | 9 | Render PostalAddress/v1 with various provider configurations; verify graceful degradation |
| US4 | 11 | 2 | Run VerifiedCitizenV2E2ETests end-to-end against Docker and n1 |

**Suggested MVP scope**: US1 only (Phase 3). Ships the bug fix that motivated this entire feature plus the supporting infrastructure to keep the contract safe.
