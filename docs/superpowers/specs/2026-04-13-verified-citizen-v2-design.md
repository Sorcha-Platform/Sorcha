# Verified Citizen v2 — Combined Design

**Date:** 2026-04-13
**Status:** Draft for review
**Author:** Stuart Fraser (with Claude)

## Why this exists

The Verified Citizen walkthrough rewrite (PR #267) hit a runtime authorization error when a public-org user tried to submit Action 1 of an HAIP Verified Citizen blueprint:

> Wallet `ws11qz4dj...` is not authorized to execute action 1. This action requires participant `'citizen'` with wallet `'ws11qz9v25...'`.

The walkthrough was pre-binding the `citizen` participant to a specific wallet at publish time. That defeated the late-binding contract that the runtime *already* implements for starting actions. While digging into this, three related gaps surfaced:

1. The "open starting action" model is half-implemented and undocumented.
2. The Verified Citizen blueprint inlines its own JSON Schema for name / DOB / email / address, with no reusable component story. Every future blueprint that needs a person's name reinvents it.
3. There is no postcode lookup or address autocomplete anywhere — every form is plain text inputs.

Each of these gaps is independently real, but they all enable each other for a credible Verified Citizen v2: an open citizen-facing flow that uses reusable, persona-aware identity components, with a postcode-driven address control. This spec covers all three workstreams and the consuming blueprint together.

## Goal

Deliver three pieces of platform plumbing and one re-skinned blueprint, all enabling each other:

1. **Open starting actions actually work end-to-end.** Anyone can submit a citizen-facing first action; the runtime binds them as the participant for the rest of the instance.
2. **Reusable, persona-aware schema components.** Local file-based today, register-published later, referenced via standard JSON Schema `$ref`. Identity primitives (name, DOB, email, address) carry their own UX, validation, and persona bindings.
3. **Pluggable address lookup.** Postcode-driven autofill via a provider abstraction, default-on with postcodes.io, optional PAF tier via OS Places.
4. **Verified Citizen blueprint v2** — the consumer that proves all of the above.

## Design decisions (with the alternatives we considered)

### Identity floor for the open submitter

**Decision:** Authenticated public-org user, no participant record. The user has signed up for a public-org account (email/password or social), holds a JWT, and has a wallet. Submitting Action 1 binds them to the participant role on this instance via late binding. No participant record needs to exist beforehand.

**Rejected alternatives:**
- Fully anonymous (account created on-the-fly from form input) — too magical, requires email-claim plumbing we don't want yet.
- Pre-existing participant identity (call `/me/register-participant` first) — too heavy; forces an extra step that the public user shouldn't care about.

### Where per-instance bindings live

**Decision:** Ledger is canonical, Redis caches with TTL.

The signed Action 1 transaction *is* the binding — by definition, whoever signed Action 1 is the citizen. Subsequent actions resolve the binding by reading back Action 1's sender from the register. Redis caches `instance.ParticipantWallets` for the hot path; a cold cache rebuilds by walking the action chain from the register.

**Rejected alternatives:**
- Pure Instance document in Blueprint Service (no ledger source-of-truth) — introduces authoritative state outside the ledger; sync becomes hard for replicating peers.
- Pure ledger walk per action submission — every Action 2+ pays a register round-trip; latency cost on the hot path.

### Open-flag semantics

**Decision:** `IsStartingAction = true` *is* the open flag. No new field on Action or Participant.

The codebase confirms this is already the design intent:
- `ValidationEngine.cs:1027` — comment: *"Starting actions accept any wallet (binding happens in ActionExecutionService). Non-starting actions validate against blueprint wallet or instance bindings."*
- `ActionExecutionService.cs:184` — starting actions exempt from the "must be a current action" check.
- `ActionExecutionService.cs:297` — starting actions chain from the blueprint publish transaction.
- `ActionExecutionService.cs:309-332` — late-binding block: looks up participant in `instance.ParticipantWallets`, binds first sender, throws on attempted re-bind.
- `Participant.cs:50-55` — XML doc: *"Wallet address of the participant (optional — resolved dynamically at execution time when absent)."*

The bug we hit lives at `ActionExecutionService.cs:196-216` — the strict-equality auth check fires only when `WalletAddress` is non-null on the participant. Our walkthrough was pre-filling it. The fix is to *stop* pre-filling, not to add new flags.

**Rejected alternatives:**
- New `openSubmission` boolean on Action — duplicates `IsStartingAction`.
- New `bindingPolicy` block on Participant — overlaps with `credentialRequirements` which already gates on credential possession.

### Gating an open action

**Decision:** Use existing `credentialRequirements` for credential-bootstrapped flows. Bare `IsStartingAction` (no requirements) for truly open flows like Verified Citizen v2.

Driving Licence Action 1 becomes:
```jsonc
{
  "id": 1,
  "isStartingAction": true,
  "sender": "applicant",
  "credentialRequirements": [
    { "type": "VerifiedCitizenCredential",
      "presentationSource": "HaipExternalWallet",
      "requiredClaims": [ ... ] }
  ]
}
```

The HAIP presentation pipeline already runs at `ActionExecutionService.cs:218-269` — the gate fires *before* the late-bind block. Whoever holds a valid credential becomes the bound applicant. No new code needed for the gate; only a publish-time guardrail (below) to make "no gate at all" loud and intentional.

### Schema component composition

**Decision:** Standard JSON Schema `$ref`. Resolver lives in the validator pipeline next to existing x-* stripping. `JsonSchema.Net` supports custom URI handlers natively.

**Rejected alternatives:**
- A Sorcha extension keyword (`x-sorcha-component`) — loses standards compliance; can't be consumed by external JSON Schema tools.

### Layout inheritance through `$ref`

**Decision:** **Transclusion (option ii)** — the resolver inlines the referenced component's properties *and* its layout extensions (`x-pages`, `x-sections`, `x-introduction`, `x-width`) into the consuming schema before validation/render. The consuming blueprint can override layout by declaring extensions as siblings to the `$ref`. JSON Schema 2020-12 allows siblings to `$ref` (the older "ref invalidates everything" rule was dropped).

**Merge rule:**
- Child (consumer) wins for `x-pages`, `x-sections`, `x-introduction`, `x-width`.
- Component wins for `properties`, `required`, `type`. Properties cannot be overridden inline — that would defeat reuse.

**Rejected alternatives:**
- "Component is a black box" (option i) — the consuming page can't customise the inner UX.
- "Layout is consumer-only" (option iii) — components define properties + validation but no layout, so we lose the headline "x-pretty" win.

### Resolution strategy

**Decision:** **Flatten at resolve time.** The resolver inlines all referenced components into the consuming schema before handing it downstream. The renderer and validator never see `$ref` — they see a fully merged schema.

Net effect: `SchemaLayoutParser` stays root-scoped; the renderer needs no recursive descent; existing single-blueprint use is unchanged.

### Component identifier format

**Decision:** **HTTPS URI as `$id`**, e.g. `https://schemas.sorcha.dev/core/PostalAddress/v1`.

The same identifier becomes `did:sorcha:register:.../schemas/core/PostalAddress/v1` once register publication ships. Both are URIs; only the resolver changes. The local file resolver in dev/staging intercepts `https://schemas.sorcha.dev/...` and serves from disk.

**Rejected alternatives:**
- Custom URI scheme `sorcha:core/PostalAddress@1` — not a real URI scheme; external tooling can't dereference it.
- Short id `core.PostalAddress.v1` — loses the URI intuition that JSON Schema authors expect.

### `x-persona` parsing

**Decision:** Move from name-heuristic matching (current behaviour in `PersonaAutofillResolver`) to **explicit declarative bindings** at the schema level. Components declare `x-persona: "address.line1"` on each property; consuming blueprints inherit the bindings via transclusion. Name-heuristic fallback stays in place for legacy schemas.

### Date constraints

**Decision:** Use **standard `formatMinimum` / `formatMaximum`** from JSON Schema 2020-12, with a tiny **token vocabulary** the renderer and validator both substitute at evaluation time.

Token vocabulary for `format: date` constraints:
- `today` — current date in the user's timezone
- `today+{N}{D|M|Y}` — N days/months/years from today (e.g. `today+18Y`)
- `today-{N}{D|M|Y}` — N days/months/years before today

A single helper (`SorchaDateTokenResolver`) handles substitution. The vocabulary is reserved-but-empty for `format: date-time` until a real consumer needs it.

The same component shape powers `DateOfBirth` (`formatMaximum: "today"`) and a future `AppointmentDate` (`formatMinimum: "today"`). An age-gated component could use `formatMaximum: "today-18Y"`.

### Address lookup architecture

**Decision:** Provider abstraction with two starter implementations, hosted in **Tenant Service**.

- `IAddressLookupProvider` interface in a new `Sorcha.AddressLookup` library (~400 LOC).
- `PostcodesIoProvider` — default-on, no key, no rate limit, UK only, validate-only capability.
- `OsPlacesProvider` — opt-in via config, requires OS Places API key, UK only, full-address capability.
- Provider selection at request time prefers `FullAddress` capability for the target country, falls back to `ValidateOnly`, falls back to plain text input.

**Rejected alternatives:**
- Single hardcoded provider (postcodes.io only) — postcodes.io doesn't return full street addresses, only postcode metadata. Misses the "type postcode → pick from a list of houses" UX.
- Defer entirely to a follow-up spec — misses the headline UX win and means the renderer ships twice.
- New microservice `Sorcha.AddressLookup.Service` — too heavy for ~400 LOC of HTTP wrappers with no DB.
- Folded into Wallet Service — Wallet should stay crypto-only.

## Workstream 1 — Open Starting Actions

### What already works (no changes)

| Stage | Behaviour | Source |
|---|---|---|
| Validator (chain) | Starting actions accept any wallet — strict participant check skipped | `ValidationEngine.cs:1027` |
| Submission gate | Starting actions exempt from "must be a current action" check | `ActionExecutionService.cs:184` |
| Chain anchor | Starting action with no prior tx auto-chains from blueprint publish tx | `ActionExecutionService.cs:297` |
| Late binding | First sender's wallet bound to participant, persisted via `_instanceStore.UpdateAsync`, immutable | `ActionExecutionService.cs:309-332` |
| Credential gate | `credentialRequirements` enforced before binding (HAIP or internal verifier) | `ActionExecutionService.cs:218-269` |

### What we change

1. **Walkthrough fix.** `walkthroughs/HaipVerifiedCitizen/setup.ps1` lines 250-253 currently include `"citizen" = $citizenWallet.Address` in the `walletMap`. Remove the citizen entry. The citizen participant ships with `walletAddress = null`. Same fix in `walkthroughs/HaipDrivingLicence/setup.ps1` for the `applicant` participant.

2. **Publish-time guardrail.** Validator rejects publication of a blueprint where any participant referenced as `sender` of an `isStartingAction: true` action has a non-null `walletAddress`. New error code (e.g. `VAL_BP_010` — name TBD by validator owner). Error message: *"Participant '{id}' is the sender of starting action {n} and must have a null walletAddress (open / late-bound). Remove walletAddress from the participant or remove the starting-action flag."*

3. **Verify Instance persistence is end-to-end.** During planning, confirm that `_instanceStore.UpdateAsync(instance)` at line 327 actually writes through in the production submission path. The Explore agent flagged a possible disconnect between `Program.cs:883` (action submission endpoint that may skip Instance creation) and `ActionExecutionService` (which assumes Instance exists). If a gap exists, close it; if not, document the path.

4. **Redis cache layer for `instance.ParticipantWallets`.** New cache via `Sorcha.Storage.Redis`:
   - Key: `instance:{instanceId}:bindings`
   - Value: serialized `Dictionary<string, string>` (participant id → wallet address)
   - TTL: 1 hour, sliding on read
   - Read path: cache → instance store → ledger walk (rebuild from Action 1's sender if both miss)
   - Write path: late-bind block writes through to both instance store and cache

5. **Documentation.** `blueprint-builder` skill and `ChatOrchestrationService` AI prompt updated (already done in this session). Add a CLAUDE.md entry under "Critical Patterns" cross-linking to the open-participant rules.

### What we explicitly do not do

- No new `openSubmission` boolean on Action.
- No new `bindingPolicy` block on Participant.
- No new "anonymous" auth tier — public-org JWT is the floor.
- No re-binding capability — once bound, immutable for the life of the instance.

## Workstream 2 — Reusable Schema Components

### Storage and discovery

- **Location**: `blueprints/schemas/sorcha-core/` — a new sector folder mirroring the existing `blueprints/schemas/{sector}/` structure that `LocalSchemaProvider` already reads.
- **Sector**: new `core` entry in `SchemaSector.All` for platform-defined primitives. Domain-specific reusable components stay in their existing sector folders (`identity`, `government`, etc.).
- **Loading**: a `CoreSchemaSeedService` IHostedService modeled on `TemplateSeedService.cs:35-100`. Scans `blueprints/schemas/sorcha-core/*.json` at startup, idempotent upsert into the schema index, version-aware via the `$id`.
- **Identifier format**: HTTPS URI as `$id`, e.g. `https://schemas.sorcha.dev/core/PostalAddress/v1`. Same shape as W3C / Schema.org / FHIR.

### Composition

`$ref` is the composition mechanism. Consuming blueprints reference components like:

```jsonc
"properties": {
  "address": { "$ref": "https://schemas.sorcha.dev/core/PostalAddress/v1" }
}
```

### Resolver

The resolver lives in the validator pipeline beside the existing x-* stripping. Three URI handlers:

- `https://schemas.sorcha.dev/...` → MongoDB schema index (populated at startup by `CoreSchemaSeedService` and `LocalSchemaProvider`)
- `did:sorcha:register:.../schemas/...` → Register Service lookup (**out of scope for this spec** — placeholder for register publication)
- Anything else → reject (no live network fetches; prevents accidental external dependencies)

The resolver flattens at resolve time: the consuming schema's `$ref`s are inlined before the schema is handed to the validator or the renderer. Layout merge rule applies.

### `x-persona` migration

- `PersonaAutofillResolver` reads the explicit `x-persona` binding on a property *first*. Falls back to name-heuristics for legacy schemas without explicit bindings. No breaking change to existing blueprints.
- `SchemaLayoutParser` (or a new `SchemaPersonaParser`) extracts `x-persona` bindings during the same walk that handles `x-pages` / `x-sections` / `x-width`.

### Date constraints

- Components use **standard** `format: "date"` plus `formatMinimum` / `formatMaximum`.
- Token vocabulary lives in a single helper (`SorchaDateTokenResolver` or similar) that both renderer and validator call: `today`, `now`, `now+{N}{Y|M|D}`, `now-{N}{Y|M|D}`.
- The validator substitutes tokens before evaluating `formatMinimum`/`formatMaximum` against submitted dates. The renderer substitutes tokens before configuring the date picker's min/max.

### Initial component set

| `$id` | Properties | Notes |
|---|---|---|
| `https://schemas.sorcha.dev/core/PersonName/v1` | `givenName`, `middleName?`, `familyName`, `fullName?` | `fullName` optional. When omitted by the user, the renderer auto-derives from given+middle+family. `x-persona` bindings on each. |
| `https://schemas.sorcha.dev/core/DateOfBirth/v1` | `dateOfBirth: { format: date, formatMaximum: "today" }` | Single property. `x-persona: "dateOfBirth"`. |
| `https://schemas.sorcha.dev/core/EmailAddress/v1` | `email: { format: email }` | Single. `x-persona: "defaultEmail"`. |
| `https://schemas.sorcha.dev/core/EmailAddressList/v1` | `emails: array of {email, isDefault}` | Min 1, max 5; exactly one default. `x-persona: "emails"`. |
| `https://schemas.sorcha.dev/core/PostalAddress/v1` | `line1`, `line2?`, `town`, `region?`, `postcode`, `country` | `x-address-lookup: true` on `postcode`. `x-persona` on each. Internal layout: 3 sections (street, locality, country). |

### Persona model gap

`PersonaAttributesV1` (Tenant Service, see CLAUDE.md → Consumer Persona API) currently has `givenName` and `familyName` but **not `middleName`**. Add `middleName` (optional string). The persona vault encryption pipeline doesn't change — it's a new field inside the existing ciphertext.

## Workstream 3 — Address Lookup Providers

### Library

New project `src/Common/Sorcha.AddressLookup` (~400 LOC). Contents:

- `IAddressLookupProvider` interface
- `AddressLookupCapability` enum: `ValidateOnly | FullAddress`
- `AddressLookupResult` record (postcode, validity, candidates[], metadata)
- `PostcodesIoProvider` — uses `HttpClient` to call `https://api.postcodes.io/postcodes/{postcode}`. No key. Returns validity, town, region, country, lat/long. Capability: `ValidateOnly`.
- `OsPlacesProvider` — uses `HttpClient` + API key from config to call OS Places API. Returns full PAF address candidates. Capability: `FullAddress`.
- `AddressLookupService` — composition root that selects the best available provider for a given country.

### Hosting

The library is registered in **Tenant Service** (`Sorcha.Tenant.Service`):

- DI registration in `Program.cs` — register `IAddressLookupProvider` instances, register `AddressLookupService`.
- New endpoints under `/api/address-lookup/*`:
  - `POST /api/address-lookup/postcode` — body `{ postcode, countryHint? }`, returns `AddressLookupResult`. Public endpoint (no auth) so anonymous Verified Citizen submitters can use it during form fill, OR auth-gated to public-org users only — **decide during planning**.
  - `GET /api/address-lookup/providers` — returns capability matrix per country so the renderer knows what UI to show.
- Rate limited via the existing `RateLimitPolicies.Api` policy (or a new `AddressLookup` policy with tighter limits).
- Routed through API Gateway via the existing Tenant Service route.

### UI control

New Razor component `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Forms/PostcodeLookupField.razor`. Consumed automatically by `SorchaFormRenderer` whenever a property carries `x-address-lookup: true`.

States:

- **No provider available** — plain text input. Form still works.
- **Validate-only provider** — postcode field with green tick on valid postcode. Autofills `town` / `region` / `country` of the parent address object via JsonPointer-style sibling lookup.
- **Full-address provider** — postcode field with "Find address" button → modal pick list of candidates → autofills all sibling fields (`line1`, `line2`, `town`, `region`, `postcode`, `country`).

The control is field-level, not component-level. It looks at its sibling fields in the parent object and writes to them by name. This means it works for `PostalAddress@v1` *and* any future component that opts into `x-address-lookup` on a postcode-shaped field.

## Verified Citizen v2 — the worked example

```jsonc
{
  "id": "haip-verified-citizen-v2",
  "title": "HAIP Verified Citizen v2",
  "version": 2,
  "participants": [
    { "id": "citizen",  "name": "Citizen", "organisation": "Public" },
    { "id": "assessor", "name": "Government Assessor",
      "organisation": "Government Identity Authority",
      "walletAddress": "ws1...assessor" }
  ],
  "actions": [
    {
      "id": 1,
      "title": "Submit Verified Citizen Application",
      "isStartingAction": true,
      "sender": "citizen",
      "dataSchemas": [
        {
          "type": "object",
          "x-introduction": "Submit your details so a government assessor can verify your identity.",
          "x-pages": [
            { "title": "Your Name",     "x-sections": [{ "fields": ["name"] }] },
            { "title": "Date of Birth", "x-sections": [{ "fields": ["dob"] }] },
            { "title": "Contact",       "x-sections": [{ "fields": ["email"] }] },
            { "title": "Address",       "x-sections": [{ "fields": ["address"] }] },
            { "title": "Check Your Answers" }
          ],
          "properties": {
            "name":    { "$ref": "https://schemas.sorcha.dev/core/PersonName/v1" },
            "dob":     { "$ref": "https://schemas.sorcha.dev/core/DateOfBirth/v1" },
            "email":   { "$ref": "https://schemas.sorcha.dev/core/EmailAddress/v1" },
            "address": { "$ref": "https://schemas.sorcha.dev/core/PostalAddress/v1" }
          },
          "required": ["name", "dob", "email", "address"]
        }
      ],
      "routes": [{ "id": "to-review", "nextActionIds": [2], "isDefault": true }]
    },
    {
      "id": 2,
      "title": "Review & Issue Verified Citizen Credential",
      "sender": "assessor",
      "requiredPriorActions": [1],
      "dataSchemas": [
        {
          "type": "object",
          "properties": {
            "verificationDecision": { "type": "string", "enum": ["approved", "rejected"] },
            "reviewerNotes":        { "type": "string", "maxLength": 500 }
          },
          "required": ["verificationDecision"]
        }
      ],
      "credentialIssuanceConfig": {
        "credentialType": "VerifiedCitizenCredential",
        "targetAudience": "HaipExternalWallet",
        "recipientParticipantId": "citizen",
        "claimMappings": [
          { "claimName": "givenName",  "sourceField": "/name/givenName" },
          { "claimName": "middleName", "sourceField": "/name/middleName" },
          { "claimName": "familyName", "sourceField": "/name/familyName" },
          { "claimName": "dateOfBirth","sourceField": "/dob/dateOfBirth" },
          { "claimName": "email",      "sourceField": "/email/email" },
          { "claimName": "address",    "sourceField": "/address" }
        ]
      },
      "routes": [{ "id": "complete", "nextActionIds": [], "isDefault": true }]
    }
  ]
}
```

The blueprint is now ~50 lines instead of ~150 because the schema components carry their own validation, layout, persona bindings, and address-lookup behaviour. Future blueprints (Driving Licence, Passport, Council Tax) reuse the same components and inherit the same UX.

The walkthrough's `walletMap` now contains only `assessor`. The citizen participant ships with `walletAddress = null` and gets late-bound to the public-org user who submits Action 1.

## Sequencing

Hard dependency graph:

1. **Workstream 1 (Open starting actions)** — independent. Ships first. Unblocks the bug we hit.
2. **Workstream 2 (Schema components)** — independent of W1 in code; depends on W1 only because the consuming blueprint needs both. The `x-persona` schema migration is a sub-task here.
3. **Workstream 3 (Address lookup)** — independent of W1 and W2 in code. The `PostalAddress` component depends on `x-address-lookup` being a recognised renderer keyword, but the keyword can ship in either workstream.
4. **Verified Citizen v2 blueprint + walkthrough** — consumer of all three. Ships last as the integration test.

Four PRs:

- **PR 1**: Workstream 1 — open actions infrastructure (publish guardrail + Instance persistence verification + Redis cache + walkthrough fix for HaipVerifiedCitizen and HaipDrivingLicence).
- **PR 2**: Workstream 2 — `core` sector + `CoreSchemaSeedService` + `$ref` resolver + flatten-at-resolve-time pipeline + initial five components + `x-persona` schema migration + `middleName` on `PersonaAttributesV1`.
- **PR 3**: Workstream 3 — `Sorcha.AddressLookup` library + Tenant Service endpoints + `PostcodeLookupField.razor` UI control + provider selection logic.
- **PR 4**: Verified Citizen v2 blueprint + walkthrough rewrite + e2e verification on n1.

## Out of scope (deliberately deferred)

- **Register publication of components.** Components are file-based for v1. The `$id` is HTTPS-shaped so the migration is non-breaking when register publication ships. The `did:sorcha:register:...` resolver is stubbed but not implemented.
- **Nationality component.** Not asked for; flagged as a likely v3 add.
- **Non-UK postcode lookup providers.** UK-first; the abstraction is country-aware so providers per country plug in later.
- **`DerivedSchemaDto` wiring.** Defined-but-unused today; we leave it that way until a real consumer wants field-subset derivation.
- **Email-as-account-claim flow.** Fully anonymous citizens whose accounts get created on the fly stays out of scope. Public-org auth is the floor.
- **Component editing UI.** Components are author-curated JSON files. No in-app editor.
- **Component versioning beyond `v1`.** A migration story for `v1 → v2` of a component is out of scope; the format `https://schemas.sorcha.dev/core/{Name}/v{N}` is reserved but no migration tooling ships in this spec.
- **Backwards-compat shim for the `walletMap[citizen]` walkthrough pattern.** PR 1 deletes the entry and updates the walkthrough; we don't ship a compatibility flag.

## Touch points / files likely to change

This is for orientation only — the implementation plan will be authoritative.

### Workstream 1
- `walkthroughs/HaipVerifiedCitizen/setup.ps1` — remove citizen from walletMap
- `walkthroughs/HaipDrivingLicence/setup.ps1` — remove applicant from walletMap
- `src/Services/Sorcha.Validator.Service/Services/ValidationEngine.cs` — new publish-time check
- `src/Services/Sorcha.Blueprint.Service/Services/Implementation/ActionExecutionService.cs` — add Redis cache write at line 327
- `src/Services/Sorcha.Blueprint.Service/Program.cs` line 883 area — verify/fix Instance creation in submission path
- New: `src/Services/Sorcha.Blueprint.Service/Services/InstanceBindingCache.cs` — Redis read-through cache
- `CLAUDE.md` — new "Critical Patterns" entry cross-linking to open-participant rules

### Workstream 2
- `blueprints/schemas/sorcha-core/PersonName.v1.json`
- `blueprints/schemas/sorcha-core/DateOfBirth.v1.json`
- `blueprints/schemas/sorcha-core/EmailAddress.v1.json`
- `blueprints/schemas/sorcha-core/EmailAddressList.v1.json`
- `blueprints/schemas/sorcha-core/PostalAddress.v1.json`
- `src/Services/Sorcha.Blueprint.Service/Models/SchemaSector.cs` — new `core` entry
- New: `src/Services/Sorcha.Blueprint.Service/Services/CoreSchemaSeedService.cs`
- `src/Services/Sorcha.Blueprint.Service/Program.cs` — register seed service
- New: `src/Services/Sorcha.Validator.Service/Services/SchemaRefResolver.cs` — flatten-at-resolve-time
- `src/Services/Sorcha.Validator.Service/Services/ValidationEngine.cs` — wire resolver into the pipeline
- `src/Common/Sorcha.Blueprint.Models/SchemaLayoutParser.cs` — extract `x-persona` bindings
- `src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/Forms/PersonaAutofillResolver.cs` — prefer explicit `x-persona` over name heuristics
- `src/Common/Sorcha.Tenant.Models/Persona/PersonaAttributesV1.cs` — add `middleName`
- New: `src/Common/Sorcha.Validator.Core/Tokens/SorchaDateTokenResolver.cs` — `today` / `now+/-N{Y|M|D}` substitution

### Workstream 3
- New project: `src/Common/Sorcha.AddressLookup/` (csproj + interface + 2 providers + DI extensions)
- `src/Services/Sorcha.Tenant.Service/Program.cs` — register address lookup
- New: `src/Services/Sorcha.Tenant.Service/Endpoints/AddressLookupEndpoints.cs`
- New: `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Forms/PostcodeLookupField.razor`
- `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Forms/SorchaFormRenderer.razor` — dispatch to `PostcodeLookupField` when `x-address-lookup: true`
- `appsettings.json` (Tenant Service) — config block for OS Places API key

### Workstream 4 (Verified Citizen v2)
- `walkthroughs/HaipVerifiedCitizen/blueprints/verified-citizen.json` — rewrite to use `$ref`s
- `walkthroughs/HaipVerifiedCitizen/setup.ps1` — bump blueprint version, remove citizen from walletMap, update HAIP claim mappings to nested paths
- `walkthroughs/HaipVerifiedCitizen/run.ps1` — verify still works end-to-end with persona autofill

## Acceptance criteria

The spec is satisfied when:

1. A public-org user can navigate to the Verified Citizen walkthrough in the UI, fill in their details using persona autofill (with the new persona-bound components), submit Action 1, and have the runtime late-bind them to the `citizen` participant. No "wallet not authorized" error.
2. The same user's wallet is recorded in the Instance's `ParticipantWallets` and is readable from the Redis cache on subsequent reads within TTL.
3. A government assessor (different user, pre-bound participant) submits Action 2; the credential is issued to the citizen via HAIP and the SD-JWT VC contains the full set of claims (givenName / middleName / familyName / dateOfBirth / email / address).
4. The `PostalAddress` component renders the postcode field as a `PostcodeLookupField` and either validates via postcodes.io (default) or returns full-address candidates via OS Places (when key configured).
5. Attempting to publish a blueprint with a starting action whose participant has a non-null `walletAddress` is rejected at the validator with a clear error message.
6. The `walletMap` in `HaipVerifiedCitizen/setup.ps1` no longer contains the `citizen` entry, and the walkthrough's `setup.ps1` + `run.ps1` complete cleanly against n1.
7. End-to-end test on n1 runs the full Verified Citizen v2 flow without manual fix-up.

## Open questions for planning — resolved

- **Address lookup endpoint auth:** Resolved as **auth-gated**. Both endpoints (`GET /api/address-lookup/providers` and `POST /api/address-lookup/postcode`) require a Bearer JWT and apply the standard API rate-limit policy. The `PostcodeLookupRenderer` runs inside an authenticated form context, so the citizen is already signed in by the time the lookup fires.
- **`x-address-lookup` keyword placement:** Resolved as **wave 4 (PR #271)** for the backing library + Tenant endpoints, and **wave 7 (PR #274)** for the UI dispatch. Splitting turned out to be natural: wave 4 could ship the library and endpoints independently, and wave 7 added the `FormSchemaService` dispatch logic plus `PostcodeLookupRenderer` once the backend was live. Graceful degradation (plain text when no provider) means wave 7 would have worked even if wave 4 had been deferred.
- **`Sorcha.AddressLookup` as a separate csproj:** Resolved as **separate csproj**. Shipped as `src/Common/Sorcha.AddressLookup` with its own test project (`tests/Sorcha.AddressLookup.Tests`). The Tenant Service references it as a library. Separate csproj won the call because (a) it kept the provider abstraction isolated and testable without spinning up the full Tenant Service, and (b) a future CLI or internal batch caller can adopt it without pulling in Tenant dependencies.

All three questions were tactical; none changed the spec.
