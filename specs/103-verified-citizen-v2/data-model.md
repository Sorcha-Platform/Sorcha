# Phase 1 Data Model: Verified Citizen v2

**Date**: 2026-04-13
**Plan**: [plan.md](plan.md)

This document captures the domain entities, value objects, validation rules, and state transitions introduced or modified by this feature. Storage and serialization details are deliberately abstracted — see the design spec and the contracts/ directory for those.

## Entities

### InstanceParticipantBinding

Records the runtime mapping from a participant role (e.g. `citizen`) to a specific wallet address for a given workflow instance. Created by the late-binding code at `ActionExecutionService.cs:309-332` when an open starting action receives its first sender.

| Field | Type | Required | Notes |
|---|---|---|---|
| `instanceId` | string (Guid) | yes | The workflow instance identifier |
| `participantId` | string | yes | The participant role id from the blueprint (e.g. `"citizen"`) |
| `walletAddress` | string | yes | The wallet that submitted the starting action |
| `boundAt` | DateTimeOffset | yes | When the binding was recorded |
| `boundByActionId` | int | yes | The action id whose submission caused the binding (typically the starting action's id) |

**Validation**:
- One binding per `(instanceId, participantId)` tuple. Attempting to insert a second is a runtime error (immutability).
- `walletAddress` must be a valid wallet form (`ws1...`).
- `boundByActionId` must reference a starting action in the parent blueprint.

**State transitions**:
- **Unbound → Bound**: only allowed via the late-bind block on first qualifying submission.
- **Bound → (anything)**: forbidden. Re-binding throws `InvalidOperationException` per the existing immutability guarantee.
- **Recovery**: if the in-memory Instance loses the binding (e.g. cold cache after restart), the binding is recoverable by reading the sender of the originating action from the canonical ledger.

**Storage**:
- Authoritative source: the signed Action 1 transaction in the register (the sender field).
- Hot path: `Instance.ParticipantWallets` dictionary in the Blueprint Service instance store.
- Cache: Redis key `instance:{instanceId}:bindings`, value = serialized dictionary, TTL 1h sliding.

**Relationships**: belongs to `Instance`; references a `Participant` defined in the parent `Blueprint`.

---

### IdentityPrimitive

A reusable, URI-identified JSON Schema fragment that describes one piece of personal information (a name, a date, an email, a postal address). File-backed in this feature; register-published in a future feature.

| Field | Type | Required | Notes |
|---|---|---|---|
| `$id` | string (HTTPS URI) | yes | Stable identifier, e.g. `https://schemas.sorcha.dev/core/PostalAddress/v1` |
| `type` | string | yes | Always `"object"` for v1 primitives |
| `title` | string | yes | Human-readable name shown in form headings |
| `description` | string | no | Optional explanatory text |
| `properties` | map | yes | The JSON Schema `properties` block |
| `required` | string[] | no | Required field names |
| `x-pages` | array | no | Wizard pages (renderer hint, transcluded) |
| `x-sections` | array | no | Field grouping inside a page (renderer hint, transcluded) |
| `x-introduction` | string | no | Callout text above the form (transcluded) |

**Per-property optional extensions**:
- `x-persona` — string, declarative binding to a persona attribute path (e.g. `"address.line1"`)
- `x-address-lookup` — boolean, marks a postcode field as eligible for address lookup
- `formatMinimum` / `formatMaximum` — JSON Schema 2020-12 constraints, may use the Sorcha date token vocabulary (`today`, `today+/-N{D|M|Y}`)
- `x-width` — string, field width hint (`full | half | third`)

**Validation**:
- `$id` must be a valid HTTPS URI.
- File location must follow `blueprints/schemas/sorcha-core/{LastSegment}.{LastSegment-version}.json`.
- The version segment in the `$id` must match the file name's version segment.
- A primitive cannot reference itself or transitively form a cycle through `$ref`. The resolver detects cycles at resolve time and surfaces an error.
- All `x-persona` paths must resolve to a known persona attribute (validated against `PersonaAttributesV1`).

**Storage**:
- Source of truth: JSON files under `blueprints/schemas/sorcha-core/`
- Loaded at startup by `CoreSchemaSeedService` (mirrors `TemplateSeedService`)
- Indexed in MongoDB via the existing schema index (`MongoSchemaIndexRepository`)
- Resolver caches resolved (flattened) forms in memory or Redis (planning decision)

**Relationships**: referenced by `Blueprint` action data schemas via JSON Schema `$ref`.

**Initial set** (all version 1):

| `$id` | Properties | Notes |
|---|---|---|
| `https://schemas.sorcha.dev/core/PersonName/v1` | `givenName`, `middleName?`, `familyName`, `fullName?` | `fullName` derived when omitted |
| `https://schemas.sorcha.dev/core/DateOfBirth/v1` | `dateOfBirth: { format: date, formatMaximum: "today" }` | Past-only |
| `https://schemas.sorcha.dev/core/EmailAddress/v1` | `email: { format: email }` | Single |
| `https://schemas.sorcha.dev/core/EmailAddressList/v1` | `emails: array of {email, isDefault}` | Min 1, max 5 |
| `https://schemas.sorcha.dev/core/PostalAddress/v1` | `line1`, `line2?`, `town`, `region?`, `postcode`, `country` | `x-address-lookup: true` on postcode |

---

### PersonaAttributesV1 (modified)

Extension to the existing persona model from Feature 092 (Consumer Persona).

**Existing fields** (unchanged): `givenName`, `familyName`, `fullName`, `dateOfBirth`, `emails`, `phones`, `addresses`, `nationalities`.

**New field**:

| Field | Type | Required | Notes |
|---|---|---|---|
| `middleName` | string | no | Optional. Used by `PersonName/v1` autofill. Existing personas have null until the citizen sets it. |

**Validation**:
- Same rules as `givenName` / `familyName` (max length, character class).
- Non-destructive: existing personas continue to work with `middleName = null`.

**Encryption pipeline**: unchanged. The new field is inside the existing AEAD ciphertext.

---

### AddressLookupResult

Value object returned from `IAddressLookupProvider.LookupAsync`. Wire shape exposed via `POST /api/address-lookup/postcode`.

| Field | Type | Required | Notes |
|---|---|---|---|
| `postcode` | string | yes | Normalised (uppercase, single space) |
| `isValid` | boolean | yes | True iff the postcode is recognised by the provider |
| `provider` | string | yes | Provider name, e.g. `"postcodes.io"` |
| `capability` | enum | yes | `ValidateOnly` | `FullAddress` |
| `metadata` | object | no | Town, region, country, lat/long when capability is `ValidateOnly` |
| `candidates` | array | no | Full address candidates when capability is `FullAddress` |

**`AddressCandidate`** (when capability is `FullAddress`):

| Field | Type | Required | Notes |
|---|---|---|---|
| `line1` | string | yes | First address line |
| `line2` | string | no | Second address line (e.g. flat number) |
| `town` | string | yes | Town/city/locality |
| `region` | string | no | Region/state/county |
| `postcode` | string | yes | Same as the looked-up postcode |
| `country` | string | yes | ISO 3166-1 alpha-2 (e.g. `"GB"`) |
| `displayLabel` | string | yes | Human-readable label for the picker UI |

---

### AddressLookupProviderInfo

Value object returned from `GET /api/address-lookup/providers`. Used by the form renderer to decide which control to show.

| Field | Type | Required | Notes |
|---|---|---|---|
| `name` | string | yes | Provider identifier, e.g. `"postcodes.io"`, `"os-places"` |
| `capability` | enum | yes | `ValidateOnly` | `FullAddress` |
| `supportedCountries` | string[] | yes | ISO 3166-1 alpha-2 codes |
| `available` | boolean | yes | Result of the provider's most recent health check |

---

### VerifiedCitizenCredential v2 claims

The Verifiable Credential issued by the Verified Citizen v2 workflow. SD-JWT VC format, OpenID4VCI pre-authorized code flow, delivered to the citizen's external HAIP wallet via QR. Claims:

| Claim | Source field | Notes |
|---|---|---|
| `givenName` | `/name/givenName` | From `PersonName/v1` |
| `middleName` | `/name/middleName` | New in v2; SD claim, selectively disclosable |
| `familyName` | `/name/familyName` | From `PersonName/v1` |
| `dateOfBirth` | `/dob/dateOfBirth` | From `DateOfBirth/v1` |
| `email` | `/email/email` | From `EmailAddress/v1` |
| `address` | `/address` | The full `PostalAddress/v1` value object as a single nested claim |

All claims are selectively disclosable. The credential carries a `cnf` (key binding) holder proof bound to the citizen's HAIP wallet key, enabling `kb-jwt` presentation downstream.

## Cross-entity invariants

1. **One bound applicant per instance.** A given `instanceId` has at most one `InstanceParticipantBinding` per participant role. Re-binding throws.
2. **Persona-aware components only autofill the bound user's persona.** The autofill resolver reads from the JWT-resolved `PlatformUser`'s `PersonaAttributesV1` ciphertext; cross-user leakage is not possible.
3. **Identity primitives never reference instance state.** Components are pure schema fragments — no joins to `Instance` or `Participant`.
4. **The `did:sorcha:register:.../schemas/...` URI form is reserved for future use.** The resolver MUST refuse to resolve it in this feature; attempting to use it surfaces a clear "not yet implemented" error rather than failing silently.
5. **Address lookup endpoints are auth-gated.** Anonymous calls receive 401. The submitter is identified by their public-org JWT for telemetry and rate limiting.

## State transitions

### Instance binding lifecycle

```text
[Unbound]
   │
   │ first qualifying submitter signs Action 1
   ▼
[Bound: walletAddress]  ←── (immutable for the life of the instance)
   │
   │ instance completes / archives
   ▼
[Archived: walletAddress retained for audit]
```

The transition from `Unbound` to `Bound` happens exactly once per `(instanceId, participantId)`. The `Archived` state is a logical state for completed instances; the binding remains queryable for audit purposes via the canonical ledger.

### Identity primitive lifecycle

Primitives are versioned via the `vN` segment in their `$id`. A primitive cannot be modified in place after publication; a change ships as `vN+1` with a new `$id`. Migration tooling for v1 → v2 is out of scope for this feature.

## Edge case mappings

| Edge case (from spec) | Entity / invariant | Behaviour |
|---|---|---|
| Re-binding attempt | InstanceParticipantBinding immutability | Throws with "already bound" message |
| Cold cache for bindings | Recovery from canonical ledger | Cache miss → instance store → ledger walk → repopulate cache |
| Persona missing fields | `PersonaAttributesV1` optional fields | Autofill populates what it can; user fills the gaps; next save updates persona |
| Layout override attempt with property override | Component-wins rule for properties | Properties override silently dropped (or surfaced as publish-time warning per planning) |
| `$ref` cycle | IdentityPrimitive validation | Resolver detects, surfaces clear error |
| Invalid postcode after candidates returned | AddressLookupResult, AddressCandidate | Field re-validates postcode; already-populated address parts unchanged |
| Provider rate-limited | AddressLookupProviderInfo `available = false` | Falls back to plain text for that submission |
| Form schema doesn't include middleName | `PersonaAttributesV1.middleName` optional | Hidden in form; persona stores it without affecting current form |
| DoB in future stored in persona | `formatMaximum: "today"` on `DateOfBirth/v1` | Autofill populates; validator rejects on submit |
| Pre-existing pre-bound walkthroughs | Walkthrough rewrite scope (Assumptions) | Both Verified Citizen and Driving Licence walkthroughs rewritten; no compat shim |
