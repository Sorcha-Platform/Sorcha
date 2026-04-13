# Identity Primitive File Format

**Status**: New file format introduced by this feature.
**File location**: `blueprints/schemas/sorcha-core/{Name}.v{N}.json`
**Loader**: `CoreSchemaSeedService` (IHostedService, mirrors `TemplateSeedService`)
**Indexer**: existing `MongoSchemaIndexRepository`

## Overview

An identity primitive is a versioned, URI-identified JSON Schema fragment that describes one piece of personal information (name, date of birth, email, postal address). Primitives are referenced from blueprints via standard JSON Schema `$ref`. Their layout, validation, and persona-autofill bindings are inherited by every consuming blueprint via transclusion at resolve time.

This document specifies the file format, the validation rules the seed service applies, and the resolver merge semantics.

## Required structure

```jsonc
{
  "$id": "https://schemas.sorcha.dev/core/{Name}/v{N}",
  "type": "object",
  "title": "Human-readable name",
  "description": "Optional explanatory text",

  "properties": {
    "fieldName": {
      "type": "string",
      "title": "Field label",
      "x-persona": "persona.attribute.path"
    }
  },

  "required": ["fieldName"]
}
```

| Field | Required | Notes |
|---|---|---|
| `$id` | yes | Must be an HTTPS URI under `https://schemas.sorcha.dev/core/`. The path's last two segments (`{Name}/v{N}`) become the primitive's identity. |
| `type` | yes | Always `"object"` for v1 primitives. |
| `title` | yes | Human-readable name shown by the renderer. |
| `description` | no | Free-form description. |
| `properties` | yes | Standard JSON Schema property map. |
| `required` | no | Standard JSON Schema required-field list. |

## Optional layout extensions (transcluded)

These keywords live at the schema root and are inherited by consuming blueprints unless the consumer overrides them as siblings to `$ref`.

### `x-introduction` (string)

Callout text rendered above the form. Supports plain text only.

### `x-pages` (array)

Wizard pages. Each page may declare its own `x-sections`. Format:

```jsonc
"x-pages": [
  {
    "title": "Your Name",
    "description": "Optional page description",
    "x-sections": [ ... ]
  }
]
```

### `x-sections` (array)

Field grouping inside a page (or at the schema root if there are no pages). Format:

```jsonc
"x-sections": [
  {
    "title": "Section heading",
    "description": "Optional section description",
    "layout": "vertical | horizontal | grid",
    "fields": ["field1", "field2"]
  }
]
```

### `x-width` (per-property)

Width hint on a property. Allowed values: `"full" | "half" | "third"`. The renderer uses this to pack fields onto rows.

## Optional per-property extensions

### `x-persona` (string)

Declarative binding to a persona attribute path. The `PersonaAutofillResolver` reads this binding when populating the form. Replaces the legacy name-heuristic matching for any property that declares it.

Examples:
- `"x-persona": "givenName"` — bind to the persona's given name
- `"x-persona": "defaultEmail"` — bind to the persona's default email
- `"x-persona": "address.line1"` — bind to a sub-field of the persona's default address

If a property does NOT declare `x-persona`, the legacy name-heuristic matching kicks in for backwards compatibility.

### `x-address-lookup` (boolean)

Marks a postcode field as eligible for the postcode lookup control. When `true`, `SorchaFormRenderer` renders the field as `PostcodeLookupField` instead of a plain text input. The control degrades gracefully when no provider is configured.

Only valid on string-typed properties whose name suggests a postcode field; the renderer ignores the keyword on incompatible properties.

### Date constraints with token vocabulary

For `format: "date"` properties, the standard `formatMinimum` and `formatMaximum` JSON Schema 2020-12 keywords are honoured by the validator. The Sorcha **date token vocabulary** lets primitives express relative cutoffs:

| Token | Meaning |
|---|---|
| `today` | Current date in the user's timezone |
| `today+{N}{D|M|Y}` | N days/months/years from today |
| `today-{N}{D|M|Y}` | N days/months/years before today |

Examples:
- `"formatMaximum": "today"` — the date must be in the past (or today)
- `"formatMinimum": "today"` — the date must be in the future (or today)
- `"formatMaximum": "today-18Y"` — the date must be at least 18 years before today

Substitution happens at validation time and at render time via `SorchaDateTokenResolver`. Literal ISO-8601 dates remain valid (e.g. `"formatMinimum": "2020-01-01"`).

## Validation rules applied by the seed service

The `CoreSchemaSeedService` rejects any primitive that fails these rules at startup. A failed primitive prevents the service from starting (loud failure).

1. **`$id` must be a valid HTTPS URI** under `https://schemas.sorcha.dev/core/`.
2. **File name must match `$id`**: for `$id: "https://schemas.sorcha.dev/core/PostalAddress/v1"`, the file must be `blueprints/schemas/sorcha-core/PostalAddress.v1.json`.
3. **`type` must be `"object"`** in this version.
4. **`title` must be present and non-empty**.
5. **`properties` must be present and contain at least one property**.
6. **No `$ref` cycles**: a primitive that `$ref`s another primitive that transitively references the first will be rejected at resolve time. The seed service does not detect cycles at load time; the resolver detects them and surfaces an error.
7. **`x-persona` paths must resolve** against the known `PersonaAttributesV1` shape. Unknown paths cause a startup error.
8. **`x-address-lookup` only on string-typed properties** with names matching `^postcode$|^postCode$|^post_code$` (case-insensitive). Other usage logs a warning and is ignored.
9. **Date token strings must parse** via `SorchaDateTokenResolver`. Invalid tokens (e.g. `"tomorrow"`) cause a startup error.

## Resolver merge semantics (consumer side)

When a blueprint references a primitive via `$ref`, the resolver merges as follows:

| Field class | Source | Override allowed? |
|---|---|---|
| `properties`, `required`, `type` | Component | **No** — overriding properties defeats reuse and would introduce subtle validation drift. Override attempts are silently dropped. |
| `x-pages`, `x-sections`, `x-introduction`, `x-width` | Child wins; component is the default | **Yes** — declare the extension as a sibling to `$ref` in the consuming blueprint. |
| `$id`, `title`, `description` | Component | No |
| Per-property `x-persona`, `x-address-lookup`, `formatMinimum`, `formatMaximum` | Component (cannot be overridden inline) | No — change them by referencing a different version of the primitive. |

### Worked override example

Default usage (component's layout):
```jsonc
"address": { "$ref": "https://schemas.sorcha.dev/core/PostalAddress/v1" }
```

Override with a compact one-row layout:
```jsonc
"address": {
  "$ref": "https://schemas.sorcha.dev/core/PostalAddress/v1",
  "x-sections": [
    { "title": "Address", "layout": "horizontal", "fields": ["line1", "town", "postcode", "country"] }
  ]
}
```

The override `x-sections` wins; the component's `properties`, `required`, persona bindings, and address-lookup behaviour all carry through unchanged.

## Resolution scopes

| URI form | Resolved against | Status |
|---|---|---|
| `https://schemas.sorcha.dev/core/...` | MongoDB schema index, populated at startup by `CoreSchemaSeedService` | Implemented in this feature |
| `did:sorcha:register:.../schemas/...` | Register Service | **Reserved** — resolver throws `NotImplementedException` with a clear message |
| Any other URI | (rejected) | The resolver does NOT make live network fetches under any circumstance |

## Versioning

A primitive is identified by the `vN` segment in its `$id`. To change a primitive after publication, ship a new file with `vN+1` and a new `$id`. Migration tooling for blueprints that consume `vN` is **out of scope** for this feature.

The single-segment version (`v1`, `v2`, …) is intentional. SemVer (`v1.0.1`) was rejected because primitives are atomic — a breaking change is a new major; a non-breaking change should still be a new file because consuming blueprints may want to opt in deliberately.
