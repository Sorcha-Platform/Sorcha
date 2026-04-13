# Implementation Plan: Verified Citizen v2

**Branch**: `103-verified-citizen-v2` | **Date**: 2026-04-13 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `specs/103-verified-citizen-v2/spec.md`
**Design**: [`docs/superpowers/specs/2026-04-13-verified-citizen-v2-design.md`](../../docs/superpowers/specs/2026-04-13-verified-citizen-v2-design.md)

## Summary

Deliver a single feature in four sequential, independently-shippable phases that together unblock open citizen submission, introduce a reusable identity primitive library, add postcode-driven address lookup, and rebuild the Verified Citizen workflow as the integration consumer.

The runtime substrate for Phase 1 (open starting actions) is **already 80% present in code**. The validator already accepts any wallet for starting actions (`ValidationEngine.cs:1027`), the action executor already binds the first sender to the participant role and persists immutably (`ActionExecutionService.cs:309-332`), and the Participant model already documents the contract (`Participant.cs:50-55` — *"resolved dynamically at execution time when absent"*). Phase 1 is mostly **finishing partially-written work, fixing the walkthrough that defeats the contract, and adding a publish-time guardrail and Redis cache**.

Phases 2 and 3 introduce new code sitting inside existing platform patterns. The schema component library mirrors the `TemplateSeedService` shape that already populates blueprint templates from disk; the address lookup providers mirror the `IExternalSchemaProvider` plug-in shape that already exists. No architectural patterns are invented in this feature — every new piece sits inside an existing pattern.

Phase 4 is the integration test: a rebuilt `HaipVerifiedCitizen` walkthrough end-to-end on n1 that exercises every other phase as a side effect.

## Technical Context

**Language/Version**: C# 13 / .NET 10
**Primary Dependencies**: .NET Aspire 13.0, ASP.NET Core, MudBlazor, JsonSchema.Net 7.4, EF Core (PostgreSQL), MongoDB.Driver, StackExchange.Redis, FluentValidation 11.10, Refit, OpenTelemetry 1.12, Scalar.AspNetCore 2.10
**Storage**: PostgreSQL (Tenant Service — persona, instance metadata), MongoDB (Blueprint Service — schema index, instance store), Redis (caching layer for participant bindings, schema resolution, address lookup responses)
**Testing**: xUnit + FluentAssertions + Moq for unit & integration; Playwright NUnit for Blazor E2E (Sorcha.UI.E2E.Tests via Docker)
**Target Platform**: Linux containers via Docker Compose; Blazor WASM client in modern browsers; Windows / Linux / macOS dev hosts
**Project Type**: Web — multi-service backend (microservices + API Gateway via YARP) plus Blazor WASM frontend
**Performance Goals**:
- Form initial render with full persona autofill: < 500ms after auth
- Schema `$ref` resolution per blueprint: < 100ms (fully in-process, references cached in Mongo/Redis after first load)
- Late-bind cache hit on participant binding lookup: < 10ms (Redis read)
- Address lookup round-trip (postcodes.io): < 1s p95
- Publish-time guardrail evaluation: < 50ms added to existing publish path
**Constraints**:
- Open submission MUST NOT bypass authentication — the public-org JWT is the floor (see Spec Assumptions)
- Schema component resolution MUST be deterministic (no live network fetches; URI handlers are file/database/register only)
- Address lookup MUST gracefully degrade to plain text if all providers are unavailable; never block form submission
- Persona schema migration (adding `middleName`) MUST be non-destructive (existing personas continue to work)
- Late binding MUST be immutable per instance (re-bind throws); recovery from cold cache MUST be deterministic via ledger replay
**Scale/Scope**:
- 5 new identity primitive JSON files
- ~400 LOC `Sorcha.AddressLookup` library
- ~200 LOC validator changes (publish guardrail + `$ref` resolver wiring)
- ~150 LOC blueprint-service changes (instance binding cache + persistence verification)
- ~600 LOC Verified Citizen v2 blueprint, walkthrough rewrite, and end-to-end test
- ~1500 LOC unit / integration / E2E test
- 4 pull requests
- Touches: Blueprint Service, Validator Service, Tenant Service, Sorcha.UI, walkthroughs, Sorcha.Tenant.Models, Sorcha.Blueprint.Models, Sorcha.Validator.Core

## Constitution Check

Evaluating this plan against `.specify/memory/constitution.md`. Re-evaluation after Phase 1 design at the bottom of this section.

| Principle | Status | Notes |
|---|---|---|
| **I. Microservices-First** | ✅ Pass | All changes sit inside existing services (Blueprint / Validator / Tenant / UI). The new `Sorcha.AddressLookup` library is folded into Tenant Service per the design decision; no new microservice. No upward dependencies introduced. |
| **II. Security First** | ✅ Pass with notes | Open submission expands attack surface — mitigated by (a) public-org JWT floor, (b) per-user rate limit on action submission, (c) optional `credentialRequirements` gate, (d) immutable binding prevents takeover. Address lookup endpoint is auth-gated and rate-limited. Persona ciphertext path unchanged. JSON Schema validation continues to apply per primitive. |
| **III. API Documentation** | ✅ Pass | All new endpoints (`/api/address-lookup/*`) use Minimal APIs with `.WithSummary()` / `.WithDescription()` / Scalar OpenAPI (constitution mandates Scalar, never Swagger). The validator publish-time error code is documented in the contracts directory. |
| **IV. Testing Requirements** | ✅ Pass | Each phase ships with: unit tests for new logic (xUnit + FluentAssertions + Moq), integration tests for new endpoints, an E2E Playwright test for the v2 form rendering and the address lookup control. Coverage target ≥ 85% for new code (constitution baseline 80%). |
| **V. Code Quality** | ✅ Pass | All new code targets net10.0, nullable reference types enabled, async/await for I/O, DI for all services. License header on every new file. |
| **VI. Blueprint Creation Standards** | ✅ Pass | Schema components ship as JSON files (constitution: "Always create blueprints as JSON or YAML documents"). The Verified Citizen v2 blueprint also ships as JSON. No fluent-API additions. |
| **VII. Domain-Driven Design** | ✅ Pass | Uses the established vocabulary: Blueprint, Action, Participant, Disclosure. Adds two new ubiquitous terms: **Identity Primitive** (a reusable schema component) and **Bound Participant** (a participant whose wallet was set by late binding rather than at publish time). Both align with existing language. |
| **VIII. Observability by Default** | ✅ Pass | All new endpoints emit OpenTelemetry traces and structured logs (no string interpolation). Instance binding cache emits hit/miss metrics. Address lookup provider selection emits a span tag identifying the chosen provider. |

**No constitution violations.** No entries in the Complexity Tracking section.

### Re-evaluation after Phase 1 design

Phase 1 design produces no new architectural surfaces — every new piece sits inside an existing pattern (TemplateSeedService → CoreSchemaSeedService; IExternalSchemaProvider → IAddressLookupProvider; existing Redis storage → InstanceBindingCache; existing publish-time validator → publish-time open-participant guardrail). Constitution check confirmed unchanged. ✅

## Project Structure

### Documentation (this feature)

```text
specs/103-verified-citizen-v2/
├── plan.md                     # This file
├── spec.md                     # Feature specification (already written)
├── research.md                 # Phase 0 — design decisions distilled (links to design spec)
├── data-model.md               # Phase 1 — entities and shapes
├── quickstart.md               # Phase 1 — developer onboarding
├── contracts/                  # Phase 1 — endpoint and file-format contracts
│   ├── address-lookup-api.yaml         # OpenAPI for /api/address-lookup/*
│   ├── persona-middlename-api.yaml     # OpenAPI delta for PUT /me/persona
│   ├── identity-primitive-format.md    # Schema component file format
│   ├── validator-publish-errors.md     # New publish-time error contract
│   └── instance-binding-cache.md       # Redis key/value contract
└── checklists/
    └── requirements.md         # Spec quality checklist (already written)
```

### Source Code (repository root)

This feature touches **existing services**; no new top-level projects beyond `Sorcha.AddressLookup`. Concrete source paths:

```text
src/
├── Common/
│   ├── Sorcha.AddressLookup/                                  # NEW project (Workstream 3)
│   │   ├── Sorcha.AddressLookup.csproj
│   │   ├── IAddressLookupProvider.cs
│   │   ├── AddressLookupCapability.cs
│   │   ├── AddressLookupResult.cs
│   │   ├── AddressLookupService.cs
│   │   ├── Providers/
│   │   │   ├── PostcodesIoProvider.cs
│   │   │   └── OsPlacesProvider.cs
│   │   └── ServiceCollectionExtensions.cs
│   ├── Sorcha.Blueprint.Models/
│   │   ├── SchemaLayoutParser.cs                              # MOD: extract x-persona declarative bindings
│   │   └── SchemaPersonaBinding.cs                            # NEW: model for declared bindings
│   ├── Sorcha.Validator.Core/
│   │   └── Tokens/
│   │       └── SorchaDateTokenResolver.cs                     # NEW: today / today±N{D|M|Y}
│   └── Sorcha.Tenant.Models/Persona/
│       └── PersonaAttributesV1.cs                             # MOD: add middleName?
│
├── Services/
│   ├── Sorcha.Blueprint.Service/
│   │   ├── Services/
│   │   │   ├── CoreSchemaSeedService.cs                       # NEW: scan blueprints/schemas/sorcha-core/*.json
│   │   │   ├── InstanceBindingCache.cs                        # NEW: Redis read-through cache
│   │   │   └── Implementation/
│   │   │       └── ActionExecutionService.cs                  # MOD: wire cache into late-bind block
│   │   ├── Models/
│   │   │   └── SchemaSector.cs                                # MOD: add 'core' sector
│   │   └── Program.cs                                         # MOD: register seed + cache + DI
│   │
│   ├── Sorcha.Validator.Service/
│   │   └── Services/
│   │       ├── ValidationEngine.cs                            # MOD: invoke resolver before validate; new VAL_BP_010 publish guardrail
│   │       └── SchemaRefResolver.cs                           # NEW: $ref → flattened schema (URI handlers + cycle detection)
│   │
│   └── Sorcha.Tenant.Service/
│       ├── Endpoints/
│       │   ├── AddressLookupEndpoints.cs                      # NEW: POST /postcode, GET /providers
│       │   └── PersonaEndpoints.cs                            # MOD: middleName in PUT /me/persona DTO
│       └── Program.cs                                         # MOD: AddSorchaAddressLookup() DI
│
└── Apps/
    └── Sorcha.UI/
        └── Sorcha.UI.Core/
            ├── Components/Forms/
            │   ├── PostcodeLookupField.razor                  # NEW: x-address-lookup dispatch target
            │   └── SorchaFormRenderer.razor                   # MOD: dispatch to PostcodeLookupField when x-address-lookup
            └── Services/Forms/
                └── PersonaAutofillResolver.cs                 # MOD: prefer declarative x-persona over heuristics

blueprints/
└── schemas/
    └── sorcha-core/                                            # NEW directory
        ├── PersonName.v1.json
        ├── DateOfBirth.v1.json
        ├── EmailAddress.v1.json
        ├── EmailAddressList.v1.json
        └── PostalAddress.v1.json

walkthroughs/
├── HaipVerifiedCitizen/
│   ├── blueprints/
│   │   └── verified-citizen.json                              # MOD: rewrite to use $refs, bump version
│   ├── setup.ps1                                              # MOD: remove citizen from $walletMap
│   └── run.ps1                                                # MOD: align persona claims to nested paths
└── HaipDrivingLicence/
    └── setup.ps1                                              # MOD: remove applicant from $walletMap

tests/
├── Sorcha.Blueprint.Service.Tests/
│   ├── InstanceBindingCacheTests.cs                           # NEW
│   └── CoreSchemaSeedServiceTests.cs                          # NEW
├── Sorcha.Validator.Service.Tests/
│   ├── SchemaRefResolverTests.cs                              # NEW (cycle detection, URI handlers, layout merge)
│   ├── PublishGuardrailTests.cs                               # NEW (VAL_BP_010 enforcement)
│   └── SorchaDateTokenResolverTests.cs                        # NEW
├── Sorcha.AddressLookup.Tests/                                # NEW project
│   ├── PostcodesIoProviderTests.cs
│   ├── OsPlacesProviderTests.cs
│   └── AddressLookupServiceTests.cs
├── Sorcha.UI.E2E.Tests/Docker/
│   ├── VerifiedCitizenV2Tests.cs                              # NEW E2E
│   └── PostcodeLookupFieldTests.cs                            # NEW E2E
└── Sorcha.Tenant.Service.IntegrationTests/
    ├── AddressLookupEndpointsTests.cs                         # NEW
    └── PersonaMiddleNameTests.cs                              # NEW
```

**Structure Decision**: Web microservices + Blazor WASM frontend. The feature touches three existing services (Blueprint, Validator, Tenant), one shared model assembly (Persona), one validator core (date tokens), and the UI. One new shared library (`Sorcha.AddressLookup`) is added under `src/Common/` to keep the address-lookup providers reusable and testable in isolation, but it is consumed only from Tenant Service per the design decision. No new top-level applications, no new microservices, no new database technologies.

## Phase 0: Outline & Research

### Why this is light

Every design unknown was resolved in the brainstorming session and is documented in [`docs/superpowers/specs/2026-04-13-verified-citizen-v2-design.md`](../../docs/superpowers/specs/2026-04-13-verified-citizen-v2-design.md) with rationale and rejected alternatives. Phase 0 of this plan is an *index* into that resolution work, not a fresh research pass.

The full research artifact is generated as `research.md` and contains:
- The eight design decisions, each in the standard Decision / Rationale / Alternatives format
- File:line citations from the existing codebase that prove the substrate is partially present
- The three open planning questions that were resolved by informed-default during specification authoring (and are now in the spec's Assumptions section)
- Pointers to the existing patterns each new piece mirrors (`TemplateSeedService`, `IExternalSchemaProvider`, etc.)

### NEEDS CLARIFICATION markers

**None.** All design questions resolved during brainstorming. Tactical questions deferred during specification (address-lookup endpoint auth, x-address-lookup PR placement, AddressLookup csproj vs folded) are resolved in this plan as informed defaults, with the rationale captured in research.md.

## Phase 1: Design & Contracts

**Prerequisites:** research.md complete (this section produces it alongside data-model.md, contracts/, quickstart.md).

### Contracts (in `contracts/`)

1. **`address-lookup-api.yaml`** — OpenAPI 3.1 for two new Tenant Service endpoints:
   - `POST /api/address-lookup/postcode` — request `{ postcode, countryHint? }`, response `AddressLookupResult` (validity, candidates[], provider name, capability)
   - `GET /api/address-lookup/providers` — response `AddressLookupProviderInfo[]` (name, capability, supported countries, availability)
   Both endpoints auth-gated to public-org users and rate-limited via `RateLimitPolicies.Api`.

2. **`persona-middlename-api.yaml`** — OpenAPI delta for `PUT /me/persona`. Adds optional `middleName` field to the existing `PersonaAttributesV1` request DTO. No response shape change beyond the new field.

3. **`identity-primitive-format.md`** — Schema component file format spec:
   - Required: `$id` (HTTPS URI), `type`, `title`, `properties`
   - Optional: `x-pages`, `x-sections`, `x-introduction`, `x-width`
   - Per-property optional: `x-persona`, `x-address-lookup`, `formatMinimum`/`formatMaximum` (with token vocabulary)
   - File location convention: `blueprints/schemas/sorcha-core/{Name}.v{N}.json`

4. **`validator-publish-errors.md`** — New publish-time error contract:
   - Code: `VAL_BP_010` (or next available; final number assigned at implementation)
   - Trigger: a participant referenced as `sender` of an `isStartingAction: true` action has a non-null `walletAddress`
   - Message template, severity, suggested fix

5. **`instance-binding-cache.md`** — Redis key/value contract:
   - Key: `instance:{instanceId}:bindings`
   - Value: serialized `Dictionary<string, string>` (participant id → wallet address)
   - TTL: 1 hour, sliding on read
   - Read path: cache → instance store → ledger walk
   - Write path: late-bind block writes through

### Data Model (in `data-model.md`)

Entities, fields, relationships, validation rules, and (where applicable) state transitions:

- **InstanceParticipantBinding** — instance-scoped record mapping participant id → wallet address. Created at first sender on a starting action; immutable thereafter. Relationship: belongs to `Instance`.
- **IdentityPrimitive** — file-backed schema component with `$id`, version, properties, layout extensions, persona bindings. Indexed in `MongoSchemaIndexRepository`. Relationship: referenced by `Blueprint` via `$ref`.
- **PersonaAttributesV1** — extended with optional `middleName`. Existing fields unchanged. Encryption pipeline unchanged.
- **AddressLookupResult** — value object: postcode, validity, candidates[], provider name, capability.
- **AddressLookupProviderInfo** — value object: name, capability (`ValidateOnly` | `FullAddress`), supported countries, availability bool.
- **VerifiedCitizenCredential v2 claims** — given name, middle name, family name, date of birth, email, structured postal address. Wire format unchanged from v1 except for the addition of middle name.

### Quickstart (in `quickstart.md`)

A developer onboarding doc covering:

- How to add a new identity primitive (the file format, where to put it, how it gets seeded at startup)
- How to consume a primitive from a new blueprint (with a worked example)
- How to override layout while keeping the primitive's properties
- How to add a new address lookup provider for a new country
- How to run the Verified Citizen v2 walkthrough end-to-end against `localhost` and against `n1.sorcha.dev`
- How to debug late binding (where the binding lives, how to inspect the cache, how to force a ledger replay)

### Agent context update

After Phase 1 artifacts are written, run the agent context update script to refresh CLAUDE.md / agent-specific files with new technology mentions.

## Phase 2 (out of scope — `/speckit.tasks` produces tasks.md)

Tasks generation is **not** part of `/speckit.plan`. After this plan is written and reviewed, run `/speckit.tasks` to produce `tasks.md` with dependency-ordered work items grouped by phase.

## Complexity Tracking

No constitution violations. No entries.

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| _none_ | _none_ | _none_ |
