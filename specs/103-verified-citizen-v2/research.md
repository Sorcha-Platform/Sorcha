# Phase 0 Research: Verified Citizen v2

**Date**: 2026-04-13
**Plan**: [plan.md](plan.md)
**Authoritative design**: [`docs/superpowers/specs/2026-04-13-verified-citizen-v2-design.md`](../../docs/superpowers/specs/2026-04-13-verified-citizen-v2-design.md)

## Why this document is short

Every design decision in this feature was resolved in a brainstorming session before specification authoring. The full alternatives, rationale, and rejected options live in the design spec linked above — this research document is an *index* into that work, structured per the standard Decision / Rationale / Alternatives format the plan template expects, with codebase citations to prove the substrate is largely already present.

There are **no unresolved unknowns**. The brainstorming session, the existing codebase analysis, and the explicit user decisions on every Q1-Q6 question removed the need for a fresh research pass.

## Decisions

### 1. Identity floor for the open submitter

**Decision**: Authenticated public-org user, no participant record. The user holds a JWT and a wallet from the existing public-org sign-up flow. Submitting Action 1 binds them to the participant role on this instance.

**Rationale**: The public-org account flow already exists and provides the JWT, the wallet, and an audit trail. It maps to the GDS / GOV.UK pattern of "you have a One Login account, then you start a service journey that binds you to a case". It avoids the fully-anonymous email-claim plumbing that's its own can of worms.

**Alternatives considered**:
- Fully anonymous (account created on-the-fly from form input). Rejected — too magical, requires email-claim plumbing.
- Pre-existing participant identity (call `/me/register-participant` first). Rejected — too heavy; forces an extra step the public user shouldn't care about.

### 2. Per-instance binding storage

**Decision**: Ledger-canonical with a Redis read-through cache (TTL 1h, sliding on read). The signed Action 1 transaction *is* the binding; subsequent actions resolve via the cached `instance.ParticipantWallets` map; cold cache rebuilds by walking the ledger.

**Rationale**: The ledger is already the authoritative source of truth for action history; using it as the binding source removes a class of state-divergence bugs and lets peers replicating the register rebuild the binding for free. Redis caching keeps the hot-path action submission under 10ms per binding lookup. Starts simple (read from ledger) and layers cache as needed.

**Alternatives considered**:
- Pure Instance document in Blueprint Service (no ledger source-of-truth). Rejected — introduces authoritative state outside the ledger; sync becomes hard for replicating peers.
- Pure ledger walk per action submission with no cache. Rejected — every Action 2+ pays a register round-trip; latency cost on the hot path.

### 3. Open-flag semantics

**Decision**: `IsStartingAction = true` is the open flag. No new field on Action or Participant. Participants targeted by a starting action MUST have `walletAddress = null` in the published blueprint.

**Rationale**: The runtime already implements this end-to-end:
- `ValidationEngine.cs:1027` — comment: *"Starting actions accept any wallet (binding happens in ActionExecutionService). Non-starting actions validate against blueprint wallet or instance bindings."*
- `ActionExecutionService.cs:184` — starting actions exempt from "must be a current action".
- `ActionExecutionService.cs:297` — starting actions auto-chain from blueprint publish tx.
- `ActionExecutionService.cs:309-332` — late-bind block: looks up participant in `instance.ParticipantWallets`, binds first sender, throws on attempted re-bind.
- `Participant.cs:50-55` — XML doc: *"Wallet address of the participant (optional — resolved dynamically at execution time when absent)."*

The bug we hit lives at `ActionExecutionService.cs:196-216` — strict-equality auth check fires *only* when `WalletAddress` is non-null. The walkthrough was pre-filling it. Fix is to stop pre-filling, not to add new flags.

**Alternatives considered**:
- New `openSubmission` boolean on Action. Rejected — duplicates `IsStartingAction`.
- New `bindingPolicy` block on Participant. Rejected — overlaps with `credentialRequirements`.

### 4. Gating an open action

**Decision**: Reuse existing `credentialRequirements` for credential-bootstrapped flows. Bare `IsStartingAction` (no requirements) for truly open flows like Verified Citizen v2.

**Rationale**: The HAIP presentation pipeline already runs at `ActionExecutionService.cs:218-269` *before* the late-bind block at line 309. Whoever holds a valid credential becomes the bound applicant. No new code needed for the gate. Driving Licence's identity-verification step fits this pattern naturally — it's `isStartingAction: true` plus `credentialRequirements: [{ type: "VerifiedCitizenCredential", ... }]`, and the late-bind happens after the HAIP gate fires.

**Alternatives considered**:
- A new `bindingPolicy` allow-list (org type, attributes, etc.). Rejected — the existing `credentialRequirements` already expresses everything we need at the per-action level; a separate policy block would overlap.

### 5. Schema component composition

**Decision**: Standard JSON Schema `$ref`. Resolver lives in the validator pipeline beside the existing x-* stripping. JsonSchema.Net 7.4 supports custom URI handlers natively.

**Rationale**: Standards-clean; the components remain consumable by external JSON Schema tooling; the validator infrastructure already supports it; the same `$id` URI form will resolve to a `did:sorcha:register:.../schemas/...` once register publication ships, with only the resolver changing.

**Alternatives considered**:
- A Sorcha extension keyword `x-sorcha-component`. Rejected — loses standards compliance; can't be consumed by external tools without a custom processor.

### 6. Layout inheritance through `$ref`

**Decision**: Transclusion (component's properties *and* layout extensions inlined into the consuming schema), with the consuming blueprint able to override layout by declaring extensions as siblings to the `$ref`. JSON Schema 2020-12 allows siblings to `$ref`.

**Merge rule**:
- **Child wins** for `x-pages`, `x-sections`, `x-introduction`, `x-width`
- **Component wins** for `properties`, `required`, `type` (cannot be overridden inline — that would defeat reuse)

**Rationale**: Transclusion gives consuming blueprints the headline "x-pretty" win (every blueprint inherits the component's beautiful UX by default) while the override mechanism preserves designer flexibility for compact / specialized layouts. Allowing properties override would defeat the entire reuse story and introduce subtle validation drift between primitives.

**Alternatives considered**:
- "Component is a black box" — the consuming page treats the reference as one logical field. Rejected — the consuming page can't customise inner UX even when it should.
- "Layout is consumer-only" — components define properties + validation but no layout. Rejected — loses the headline x-pretty win.

### 7. Resolution strategy

**Decision**: Flatten at resolve time. The resolver inlines all referenced components into the consuming schema *before* handing it to the validator or renderer. Downstream code never sees `$ref`.

**Rationale**: Keeps `SchemaLayoutParser` root-scoped (no recursive descent needed); the renderer needs no recursive descent either; existing single-blueprint behaviour is unchanged. The flatten step happens once at the validator boundary and the result can be cached.

**Alternatives considered**:
- Lazy resolution at render/validate time. Rejected — forces both renderer and validator to be `$ref`-aware, multiplying the surface area.

### 8. Component identifier format

**Decision**: HTTPS URI as `$id`, e.g. `https://schemas.sorcha.dev/core/PostalAddress/v1`.

**Rationale**: The same identifier becomes `did:sorcha:register:.../schemas/core/PostalAddress/v1` once register publication ships. Both are URIs; only the resolver changes. The local file resolver in dev/staging intercepts `https://schemas.sorcha.dev/...` and serves from disk. URI form is what JSON Schema authors expect — same shape as W3C / Schema.org / FHIR / etc. No future migration of IDs needed.

**Alternatives considered**:
- Custom URI scheme `sorcha:core/PostalAddress@1`. Rejected — not a real URI scheme; external tooling can't dereference it.
- Short id `core.PostalAddress.v1`. Rejected — loses URI intuition; not standards-shaped.

### 9. `x-persona` parsing

**Decision**: Move from name-heuristic matching (current behaviour in `PersonaAutofillResolver`) to **explicit declarative bindings** at the schema level. Components declare `x-persona: "address.line1"` on each property; consuming blueprints inherit the bindings via transclusion. Name-heuristic fallback stays in place for legacy schemas.

**Rationale**: More precise; survives field renames; self-documents the autofill contract; removes heuristic-mismatch bugs. The fallback ensures backwards compatibility for legacy blueprints with no declared bindings.

**Alternatives considered**:
- Keep name heuristics as the primary mechanism. Rejected — fragile and undocumented.
- Replace heuristics entirely. Rejected — would break legacy blueprints.

### 10. Date constraints

**Decision**: Use **standard `formatMinimum` / `formatMaximum`** (JSON Schema 2020-12) with a Sorcha **token vocabulary** for `format: date`:

| Token | Meaning |
|---|---|
| `today` | Current date in user's timezone |
| `today+{N}{D|M|Y}` | N days/months/years from today |
| `today-{N}{D|M|Y}` | N days/months/years before today |

A single helper `SorchaDateTokenResolver` substitutes tokens at validation time and at render time. The same component shape powers `DateOfBirth` (`formatMaximum: "today"`), a future `AppointmentDate` (`formatMinimum: "today"`), and an `AgeGate18` (`formatMaximum: "today-18Y"`).

**Rationale**: Standards-rooted (no new schema keyword); minimal vocabulary; one helper; reusable across past / future / age-gated date fields. The `today`-rooted vocabulary is unambiguous for `format: date`. `now` is reserved for future `format: date-time` use.

**Alternatives considered**:
- Author-side literal dates (e.g. `formatMaximum: "2026-04-13"`). Rejected — wrong on day two.
- A new Sorcha-specific `x-max: today` keyword. Rejected — needless invention when the standard keyword does the job with a small substitution helper.

### 11. Address lookup architecture

**Decision**: Provider abstraction (`IAddressLookupProvider`) with two starter implementations, hosted in **Tenant Service**:
- `PostcodesIoProvider` — default-on, no key, no rate limit, UK only, validate-only capability
- `OsPlacesProvider` — opt-in via config (requires OS Places API key), UK only, full-address capability

Provider selection at request time prefers `FullAddress` for the country, falls back to `ValidateOnly`, falls back to plain text input.

**Rationale**: Mirrors the `IExternalSchemaProvider` plug-in shape that already exists for SchemaLibrary — a proven pattern in the codebase, not a new architecture. Postcodes.io is genuinely useful even without full street addresses (postcode validity, town/region autofill) and is free + key-less + unlimited so it ships as default-on. The abstraction means HMG-only deployments can plug OS Places, commercial deployments can plug Loqate, country-specific providers plug in later — all without breaking compatibility.

**Alternatives considered**:
- Single hardcoded postcodes.io. Rejected — misses the "type postcode → pick address from list" UX.
- Defer entirely to a follow-up feature. Rejected — the renderer would ship twice.

### 12. Address lookup hosting

**Decision**: Folded into Tenant Service.

**Rationale**: Persona/PII already lives in Tenant Service; the address lookup providers are stateless HTTP wrappers with no DB; ~400 LOC doesn't justify a new microservice.

**Alternatives considered**:
- New `Sorcha.AddressLookup.Service` microservice. Rejected — too heavy for ~400 LOC of stateless HTTP wrappers; violates YAGNI.
- Folded into Wallet Service. Rejected — Wallet should stay crypto-only; addresses are persona-adjacent, not crypto-adjacent.

## Existing patterns to mirror

| New piece | Existing pattern | Source |
|---|---|---|
| `CoreSchemaSeedService` | `TemplateSeedService` (IHostedService that scans `blueprints/templates/*.json` at startup, idempotent upsert) | `src/Services/Sorcha.Blueprint.Service/Templates/TemplateSeedService.cs:35-100` |
| `IAddressLookupProvider` plug-in shape | `IExternalSchemaProvider` (10 providers wired today: SchemaStore.org, FHIR, UBL, ISO 20022, NIEM, IFC, W3C-VC, DPP, etc.) | `src/Services/Sorcha.Blueprint.Service/...` (registered in `Program.cs:191-238`) |
| `SchemaRefResolver` URI handlers | JsonSchema.Net's native custom `SchemaResolver` registry | `Json.Schema` namespace, used in `ValidationEngine.cs:9` |
| `InstanceBindingCache` | `Sorcha.Storage.Redis` + existing rate-limiting cache patterns | `src/Common/Sorcha.Storage.Redis/` |
| Validator publish guardrail | Existing publish-time validation rules in `ValidationEngine.cs` (Rule 6: starting action validation; new rule sits next to it) | `src/Services/Sorcha.Blueprint.Service/Program.cs:2640-2720` |
| `Sorcha.AddressLookup` library hosted in Tenant Service | `Sorcha.Cryptography` consumed from Wallet Service — same shape (shared library, single consumer) | `src/Common/Sorcha.Cryptography/` |
| `PostcodeLookupField.razor` x-* dispatch | `SorchaFormRenderer` already dispatches to `FileUploadField` when `x-file` is present | `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Forms/SorchaFormRenderer.razor` |

Every new piece sits inside an existing pattern. No architectural patterns are invented in this feature.

## Tactical decisions resolved during specification

These were the three "open planning questions" flagged at the bottom of the design spec. They're tactical and don't change architecture, so they were resolved as informed defaults during specification authoring rather than asked as `[NEEDS CLARIFICATION]` markers.

1. **Address lookup endpoint authentication**: Auth-gated to public-org users, rate-limited per user.
   - **Why**: By the time the citizen reaches the form, they're already authenticated. The "anonymous form-fill" use case doesn't exist — Q1's identity floor is "authenticated public-org user". Auth-gating prevents abuse and ties usage to a known principal for telemetry.

2. **`x-address-lookup` keyword recognition**: PR 2 (with the components that use it).
   - **Why**: The renderer needs to *recognise* the keyword whether or not a provider is configured. The graceful-degradation path (plain text input when no provider) requires the renderer to handle the keyword from day one. PR 3 then provides the providers; PR 2 owns the keyword.

3. **`Sorcha.AddressLookup` library packaging**: Separate csproj under `src/Common/`, consumed only from Tenant Service.
   - **Why**: Test isolation (the providers are testable with a faked HttpClient without spinning up Tenant Service); future reuse (a CLI consumer or an integration test could call the providers directly); follows the precedent of `Sorcha.Cryptography` being a shared library with a single consumer.

## Open items

**None.** All decisions resolved. Ready for Phase 1.
