# Phase 1 Data Model: PWA Shared Persona/Profile Editor

**No new persistent entities or model types are introduced.** This feature reuses the existing
persona model layer (`Sorcha.Tenant.Models.Persona`) and the existing client/service layer
(`Sorcha.UI.Core.Services.Persona`). This document records the entities the shared editor binds to,
their fields, validation rules, and the read↔write↔form transitions, so the component can be built
without re-deriving them.

---

## Entity: Persona (read form) — `PersonaReadModelV1`

Source: `src/Common/Sorcha.Tenant.Models/Persona/PersonaReadModelV1.cs`. Loaded via
`IPersonaService.GetAsync()`. Each scalar/default attribute is wrapped in `PersonaAttribute<T>`
(carries provenance). Returned **empty (not null/404)** when the citizen has no persona yet.

| Field | Type | Notes |
|-------|------|-------|
| `GivenName` | `PersonaAttribute<string>?` | nullable |
| `MiddleName` | `PersonaAttribute<string>?` | nullable; added F103; not shown on current web page |
| `FamilyName` | `PersonaAttribute<string>?` | nullable |
| `FullName` | `PersonaAttribute<string>?` | fallback; used by autofill only if given+family both blank |
| `DateOfBirth` | `PersonaAttribute<DateOnly>?` | nullable |
| `DefaultEmail` / `AllEmails` | `PersonaAttribute<PersonaEmail>?` / `IReadOnlyList<PersonaEmail>` | |
| `DefaultPhone` / `AllPhones` | `PersonaAttribute<PersonaPhone>?` / `IReadOnlyList<PersonaPhone>` | |
| `DefaultAddress` / `AllAddresses` | `PersonaAttribute<PersonaAddress>?` / `IReadOnlyList<PersonaAddress>` | |
| `DefaultNationality` / `AllNationalities` | `PersonaAttribute<string>?` / `IReadOnlyList<string>` | ISO 3166-1 alpha-2 |

### `PersonaAttribute<T>`
`Value: T`, `Source: PersonaAttributeSource` (`SelfAsserted`=0 in v1), `VerifiedBy: string?`
(null in v1), `LastUpdated: DateTimeOffset`. The editor reads only `.Value`.

---

## Entity: Persona (write form) — `PersonaAttributesV1`

Source: `src/Common/Sorcha.Tenant.Models/Persona/PersonaAttributesV1.cs`. Submitted via
`IPersonaService.UpdateAsync(PersonaAttributesV1)`. **Full-replace** semantics (not patch).

| Field | Type | Default |
|-------|------|---------|
| `GivenName` / `MiddleName` / `FamilyName` / `FullName` | `string?` | null |
| `DateOfBirth` | `DateOnly?` | null |
| `Emails` | `IReadOnlyList<PersonaEmail>` | `[]` |
| `Phones` | `IReadOnlyList<PersonaPhone>` | `[]` |
| `Addresses` | `IReadOnlyList<PersonaAddress>` | `[]` |
| `Nationalities` | `IReadOnlyList<string>` | `[]` (ISO 3166-1 alpha-2) |

### Multi-value entry types
- `PersonaEmail(string Value, bool IsDefault, string? Label = null)` — `Value` RFC 5322 basic shape.
- `PersonaPhone(string Value, bool IsDefault, string? Label = null, PersonaPhoneKind? Kind = null)` —
  `Value` E.164; `Kind` ∈ {`Mobile`=0, `Home`=1, `Work`=2}.
- `PersonaAddress(string Line1, string? Line2, string City, string? Region, string PostalCode, string Country, bool IsDefault, string? Label = null)` —
  `Country` ISO 3166-1 alpha-2 (uppercase).

---

## Validation rules (enforced server-side; surfaced inline by the editor)

These are **not** redefined by this feature — they are enforced by the Tenant Service on `PUT` and
returned as a `400` with field-level codes (mapped to `PersonaValidationException.Errors`):

- Each multi-value list capped at **5** entries.
- If a list is non-empty, **exactly one** entry has `IsDefault = true` (service promotes the first if
  none marked; rejects if more than one marked → e.g. `multiple_defaults`).
- Email values must match RFC 5322 basic shape (e.g. `invalid_email`).
- Phone values must be E.164.
- Country / nationality codes must be ISO 3166-1 alpha-2 (2-letter, uppercase, alphabetic).

A `409` (wallet not provisioned) maps to `PersonaWalletNotProvisionedException` — a distinct,
non-validation rejection.

---

## State / data flow inside `PersonaEditor`

```text
OnInitializedAsync
  ├─ autofillEnabled ← IPersonaService.GetAutofillEnabledAsync()
  ├─ read ← IPersonaService.GetAsync()        // empty form if no persona (not an error)
  └─ HydrateFromRead(read)                     // unwrap PersonaAttribute<T>.Value → mutable fields
                                               //   DateOnly ↔ DateTime for MudDatePicker

[user edits mutable form fields: scalars + dynamic lists, ≤5 each, one default each]

HandleSave
  ├─ payload ← PersonaAttributesV1 { scalars (NullIfBlank), filtered non-blank list entries }
  ├─ updated ← IPersonaService.UpdateAsync(payload)
  │     ├─ 400 → PersonaValidationException     → inline field errors, input preserved
  │     ├─ 409 → PersonaWalletNotProvisioned…   → distinct provisioning message
  │     └─ other/network → "save did not complete, retry"
  ├─ IPersonaService.SetAutofillEnabledAsync(autofillEnabled)
  ├─ HydrateFromRead(updated)                   // re-bind to server-canonical values
  └─ IInlineFeedback.ShowSuccess("Profile saved.")

HandleDelete (optional, parity with web)
  └─ IPersonaService.DeleteAsync(); HydrateFromRead(empty); ShowInfo("Profile deleted.")
```

**Form field mapping** (mutable component state → write model): given/family/full name + DOB are
scalar; emails/phones/addresses/nationalities are dynamic lists capped at 5 with a single default
selector each. Middle name is present in the model but not surfaced on the current web page — keep
parity (do not add it as part of this feature unless the web page already shows it).
