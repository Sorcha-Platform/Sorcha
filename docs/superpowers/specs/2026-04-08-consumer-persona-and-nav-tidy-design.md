# Consumer Persona & Nav Tidy — Design

**Date:** 2026-04-08
**Status:** Approved for planning
**Scope:** Consumer-grade form-filling experience via a per-user "My Profile / Persona" with autofill, plus a small navigation tidy-up.

---

## 1. Goal

Give consumers a fast, trustworthy form-filling experience. When a blueprint action form asks for identity fields (name, date of birth, address, email, phone, nationality), the user should see them prefilled from their personal profile, clearly marked as self-asserted, and easily editable. The recipient of the form must know which values came from the user's persona versus which were typed by hand.

The design is built around Sorcha's DAD (Disclosure, Alteration, Destruction) model: disclosure is the consumer's choice, alteration is traceable through form provenance, and the persona is stored encrypted so destruction of the store does not leak plaintext.

This phase intentionally ships self-asserted attributes only. Verifiable-credential-backed attributes (blue tick) and cross-user delegation (Power of Attorney) are out of scope, but the contracts and data shapes are designed so they slot in without breaking the surface.

---

## 2. Scope

### In scope

- **Persona data model** — 12 identity essentials covering name, date of birth, email(s), phone(s), address(es), nationality(ies)
- **Encrypted storage** — ciphertext in Tenant Service, encryption key derived by Wallet Service under a new `sorcha:persona-vault` derivation purpose
- **Client API** — `IPersonaService` with session-lifetime caching, `actingAs` parameter designed in but restricted to `self` for v1
- **`/profile` page** — new Blazor page for managing persona, reached exclusively via `UserProfileMenu` (not the side nav)
- **Form renderer autofill** — `SorchaFormRenderer` consults the persona on load, applies cream-tinted fills with a `self` tick, supports edit-to-release
- **`x-persona` schema extension** with conservative inference fallback (`format: "email"`, `format: "tel"`, standard date-of-birth field names)
- **Global autofill toggle** — user preference, ON by default, with a manual "Fill from profile" button when toggled off
- **Nav tidy** — drop drawer "Navigation" header; remove Settings and Notifications from side nav; add "My Profile" to `UserProfileMenu`; move `settings/notifications` page content into a tab inside the main Settings page

### Out of scope (explicit follow-ups)

- Wallet delegation / PoA "filling on behalf of" banner and key-wrapping
- VC-backed persona attributes and the blue "verified" tick
- Migration to client-side (zero-knowledge) persona decryption
- Per-form autofill override property on `SorchaFormRenderer`
- Per-form alternate-value picker for multi-value attributes
- Freeform "remembered answers" key/value bag (growth path from typed essentials)

Each follow-up is tracked as a task in the brainstorming session.

---

## 3. Principles and constraints

- **Consent-first disclosure.** The user must be able to see at a glance which fields came from their persona and must be able to override or clear them before submitting.
- **Provenance never silent.** Every autofilled field is visually distinct and carries a source tick. Recipients render the same provenance when viewing the submitted form.
- **Honest provenance.** Once a user edits an autofilled field, the `self` claim is removed — even if they revert to the exact persona value. The tick is a statement about provenance, not about value equivalence.
- **Contract stability across the upgrade path.** The `IPersonaService` surface and the `PersonaAttribute<T>` DTO must not change when verified credentials or delegation arrive. Both extensions are additive.
- **Identity belongs to the person, not the wallet.** The persona is attached to `PlatformUser` (Tenant Service), because a delegate filling a PoA form needs to read the principal's attributes, not their own.
- **Encryption-first storage.** The ciphertext lives where the user data lives (Tenant). The key lives where the key material lives (Wallet). No service holds both.
- **Pre-release migration hygiene.** Schema changes fold into the existing initial setup migration rather than accumulating an incremental migration chain.

---

## 4. Data model

### 4.1 Tenant Service entity

```
PlatformUserPersona
├── PlatformUserId    PK, FK → PlatformUser
├── CiphertextBlob    bytea    — XChaCha20-Poly1305 encrypted PersonaAttributesV1 JSON
├── Nonce             bytea    — 24 bytes
├── WrappedKeyRef     text     — opaque identifier the Wallet Service uses to unwrap PersonaContentKey
├── SchemaVersion     int      — starts at 1
├── UpdatedAt         timestamptz
└── CreatedAt         timestamptz
```

- Exactly one row per `PlatformUser`.
- `WrappedKeyRef` is opaque to Tenant — it is a handle the Wallet Service uses to locate the per-user wrapped `PersonaContentKey`. For v1, where the Wallet Service always derives the content key from `sorcha:persona-vault` on demand, this field exists for forward compatibility with per-recipient key wrapping (delegation).
- **Wallet lookup.** Tenant `PersonaService` resolves the target wallet address via the existing `IWalletServiceClient` path that maps `PlatformUserId → primary wallet address`. For v1 the resolved address is both the URL path component of the crypto call and the value stored in `WrappedKeyRef`. If the user has no wallet provisioned, `GET /me/persona` returns an empty persona (200) and writes return 409 `wallet_not_provisioned`.
- The EF Core change is folded into the existing Tenant Service initial setup migration. No new incremental migration file.

### 4.2 Plaintext schema (`PersonaAttributesV1`)

```
PersonaAttributesV1
├── GivenName?         string
├── FamilyName?        string
├── FullName?          string                             — fallback when given/family not split
├── DateOfBirth?       date (ISO 8601)
├── Emails             List<PersonaEmail>                 (0..n)
├── Phones             List<PersonaPhone>                 (0..n)
├── Addresses          List<PersonaAddress>               (0..n)
└── Nationalities      List<string>                       (0..n, ISO 3166-1 alpha-2)
```

Each multi-value list element:

```
PersonaEmail      { Value, IsDefault, Label? }
PersonaPhone      { Value, IsDefault, Label?, Kind? }    — Kind: Mobile | Home | Work
PersonaAddress    { Line1, Line2?, City, Region?, PostalCode, Country, IsDefault, Label? }
```

**Invariants** (enforced on write in Tenant `PersonaService`):

- If a list is non-empty, exactly one entry has `IsDefault = true`. On write, if the caller provides zero or more than one default, the service promotes the first entry and returns 400 only when more than one is explicitly marked.
- `Country` and each `Nationalities` entry validate as ISO 3166-1 alpha-2.
- `Emails[*].Value` validates as RFC 5322 basic shape.
- `Phones[*].Value` validates as E.164.

### 4.3 Read-side DTO (`PersonaAttribute<T>`)

Every scalar or structured attribute returned from the Tenant Service `GET /me/persona` endpoint is wrapped:

```
PersonaAttribute<T>
├── Value           T
├── Source          enum { SelfAsserted, VerifiedCredential }
├── VerifiedBy?     string   — credential issuer DID (null for v1)
└── LastUpdated     timestamptz
```

For v1, `Source` is always `SelfAsserted` and `VerifiedBy` is always null. The DTO shape is locked so `SorchaFormRenderer` can render the grey tick today and the blue tick later with no contract change.

For multi-value attributes, the wrapper carries the default entry and the list is exposed as a sibling field on the parent DTO. The exact shape used on the wire is:

```
PersonaReadModelV1
├── GivenName?        PersonaAttribute<string>
├── FamilyName?       PersonaAttribute<string>
├── FullName?         PersonaAttribute<string>
├── DateOfBirth?      PersonaAttribute<date>
├── DefaultEmail?     PersonaAttribute<PersonaEmail>
├── AllEmails         List<PersonaEmail>
├── DefaultPhone?     PersonaAttribute<PersonaPhone>
├── AllPhones         List<PersonaPhone>
├── DefaultAddress?   PersonaAttribute<PersonaAddress>
├── AllAddresses      List<PersonaAddress>
├── DefaultNationality?  PersonaAttribute<string>
└── AllNationalities  List<string>
```

The form renderer uses `Default*` for autofill. The profile page uses `All*` for management.

### 4.4 Cryptography

- New derivation purpose constant in `Sorcha.Cryptography`:

  ```csharp
  public const string PersonaVault = "sorcha:persona-vault";
  ```

- AEAD: `XChaCha20-Poly1305` (same primitive used by Feature 085 file chunks).
- `PersonaContentKey` is derived per user from the user's system wallet under `sorcha:persona-vault`. For v1 it is deterministic per user — each encrypt/decrypt call re-derives it. This is acceptable because the `WrappedKeyRef` column makes a future move to per-recipient wrapping a pure storage swap with no contract change.

---

## 5. Services and endpoints

### 5.1 Tenant Service — Persona endpoints

Registered in `Endpoints/PersonaEndpoints.cs`, rate-limited under `RateLimitPolicies.Api`, require authenticated `PlatformUser` JWT.

| Method | Path | Behaviour |
|---|---|---|
| `GET`    | `/me/persona` | Returns `PersonaReadModelV1`. Empty persona is a 200 with all fields null/empty lists — never 404. Query parameter `actingAs` is accepted; only the literal value `self` is valid in v1, anything else returns 400 `actingAs_not_supported`. |
| `PUT`    | `/me/persona` | Full replace. Body is `PersonaAttributesV1`. Validates invariants, encrypts via Wallet Service, upserts row. |
| `PATCH`  | `/me/persona` | JSON merge-patch of individual attributes. Empty arrays in the patch are a signal to clear a list; omitted fields are untouched. |
| `DELETE` | `/me/persona` | Wipes the row. Idempotent. Returns 204 whether or not a row existed. |

Every successful `PUT`, `PATCH`, and `DELETE` writes an `IActivityLogService` entry. Reads are not audit-logged.

### 5.2 Wallet Service — Persona crypto endpoints (internal)

Not exposed via the API Gateway. Require S2S JWT carrying a new `persona:crypto` scope.

| Method | Path | Purpose |
|---|---|---|
| `POST` | `/api/v1/wallets/{address}/persona/encrypt` | Body: `{ plaintext }`. Returns `{ ciphertext, nonce, wrappedKeyRef }`. |
| `POST` | `/api/v1/wallets/{address}/persona/decrypt` | Body: `{ ciphertext, nonce, wrappedKeyRef }`. Returns `{ plaintext }`. |

A gateway-config test asserts these endpoints are not reachable through the public API Gateway.

### 5.3 Service client

`Sorcha.ServiceClients` gains `IPersonaClient` wrapping the Tenant Service `/me/persona` endpoints. Registered in `AddServiceClients`.

---

## 6. Client components

### 6.1 `IPersonaService` (client)

Located in `Sorcha.UI.Core/Services/Persona/`.

```csharp
public interface IPersonaService
{
    Task<PersonaReadModelV1?> GetAsync(PersonaReadOptions? options = null, CancellationToken ct = default);
    Task UpdateAsync(PersonaAttributesV1 persona, CancellationToken ct = default);
    Task DeleteAsync(CancellationToken ct = default);
    Task<bool> GetAutofillEnabledAsync(CancellationToken ct = default);
    Task SetAutofillEnabledAsync(bool enabled, CancellationToken ct = default);
    void InvalidateCache();
}

public sealed record PersonaReadOptions
{
    public string ActingAs { get; init; } = "self";
}
```

- `PersonaReadOptions` exists from day one so the delegation follow-up is a value change, not a signature change.
- Session-lifetime in-memory cache keyed by `ActingAs`. Cleared on `LogoutEvent`, `OrgSwitchEvent`, `UpdateAsync`, and explicit `InvalidateCache()`.
- `GetAutofillEnabledAsync` / `SetAutofillEnabledAsync` persist via whatever user-settings mechanism `Settings.razor` already uses — the plan phase must verify this and adopt it rather than invent a new store.

### 6.2 `PersonaAutofillResolver`

Located in `Sorcha.UI.Core/Services/Forms/`. Single responsibility: given a parsed form layout, the JSON schema, and a `PersonaReadModelV1`, produce a map of field path to `PersonaFillResult`.

```csharp
public sealed record PersonaFillResult(
    string FieldPath,             // JSON Pointer
    string AttributeName,         // e.g. "Email", "Address", "DateOfBirth"
    object Value,
    PersonaAttributeSource Source,
    PersonaMatchMode MatchedBy);  // ExplicitExtension | Inference
```

**Matching rules**, in order:

1. **Explicit `x-persona` extension wins.** If a schema field has `"x-persona": "<attributeName>"`, map to that attribute. If it has `"x-persona": false`, skip the field entirely (blocks inference even when it would match). Malformed values (not a string, not `false`) are ignored with a logged warning.
2. **Inference fallback allowlist.** Applied only when `x-persona` is absent:
   - `format: "email"` → `DefaultEmail.Value`
   - `format: "tel"` → `DefaultPhone.Value`
   - Field name exactly one of `dateOfBirth`, `dob`, `birthDate` (case-insensitive) with `type: "string"` and `format: "date"` → `DateOfBirth`
   - Field with JSON-LD `@type: PostalAddress` or a schema `$ref` resolving to a known address definition → `DefaultAddress`
   - Field name `givenName` / `firstName` → `GivenName`; `familyName` / `lastName` / `surname` → `FamilyName`; `fullName` / `name` (top-level of a person section only) → `FullName`
3. **Schema default wins over persona.** If the JSON Schema declares a `default` for the field, persona is not applied. Don't fight the blueprint author's explicit intent.
4. **Non-scalar fields are not autofilled in v1.** Array-of-objects / repeating sections are skipped even if a leaf would otherwise match.

The resolver is a pure function for testability — it takes its inputs, produces a map, and never touches Blazor state.

### 6.3 `SorchaFormRenderer` integration

Changes to `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Forms/SorchaFormRenderer.razor`:

- New injected `IPersonaService` and `PersonaAutofillResolver` (plus a logger).
- In `OnInitializedAsync`, after schema hydration but before first render:
  1. Read the global autofill toggle via `IPersonaService.GetAutofillEnabledAsync()`.
  2. Fetch persona via `IPersonaService.GetAsync()`.
  3. Call `PersonaAutofillResolver.Resolve(formLayout, schema, persona)` to produce the fill map.
  4. If the toggle is ON and the map is non-empty: apply values to the form data bag, add field paths to a new `HashSet<string> _personaFilledPaths`, and record for the summary line.
  5. If the toggle is OFF: stash the map in `_pendingPersonaFills` and render a "Fill from profile" button above the form (see §6.4).
- New field binding hook: whenever a bound field value changes, remove its path from `_personaFilledPaths` before rendering. The binding no longer advertises a `self` provenance.
- New scoped CSS class in `SorchaFormRenderer.razor.css`:

  ```css
  .sorcha-field.autofilled input,
  .sorcha-field.autofilled textarea {
      background: #fff8e1;
      border-color: #ffcc80;
  }
  .sorcha-field.autofilled .persona-tick {
      display: inline-block;
      font-size: 10px;
      padding: 2px 7px;
      border-radius: 10px;
      background: #fff3e0;
      color: #8d6e63;
      border: 1px solid #ffcc80;
  }
  ```

  The exact colour palette is adjustable during implementation provided the contrast meets WCAG AA and the cream-vs-neutral distinction is unmistakable in both light and dark MudBlazor themes.

### 6.4 `PersonaFillSummary` and "Fill from profile" button

New small component `Components/Forms/PersonaFillSummary.razor` rendered above the form body:

- When autofill is ON and one or more fields were filled: single-line summary *"{n} fields filled from your profile"* plus two actions:
  - **Review** — opens a compact popover listing each filled field path, the attribute name, and the current value, with a per-row "clear" action.
  - **Clear all** — clears every field path still in `_personaFilledPaths`. User-edited fields (removed from the set) are not touched.
- When autofill is OFF and one or more fields would have been filled: renders a single **"Fill from profile"** button. Clicking it applies the pending fills exactly as the automatic path would have, including populating `_personaFilledPaths` for the cream tint.

### 6.5 `/profile` page

New route `@page "/profile"` at `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/Profile.razor`.

- **Header**: page title, global autofill toggle ("Autofill forms from my profile" — ON by default), Save button.
- **Identity section**: GivenName, FamilyName, FullName (collapsible fallback), DateOfBirth, Nationalities chip list with default selector.
- **Contact section**: Emails, Phones, Addresses — each as an add/remove list with inline edit, a "default" radio, and an optional Label field.
- Save calls `IPersonaService.UpdateAsync`. On success, the client cache is invalidated and the page refetches to render canonical state.
- MudBlazor components throughout, following the existing `sorcha-ui` skill conventions.

### 6.6 Navigation changes (`MainLayout.razor` + `UserProfileMenu.razor` + `Settings.razor`)

**`MainLayout.razor`**

- Remove the `<MudText Typo="Typo.h6">@Loc.T("nav.navigation")</MudText>` block inside `<MudDrawerHeader>`. The drawer header stays present but empty (or optionally is removed entirely during implementation if the drawer renders cleanly without it).
- Remove the side-nav `<MudNavLink>` entries for `settings` and `settings/notifications`.
- The top app bar `MudIconButton` bound to `ToggleActivityLog` — which opens the activity log popover and is labelled "Activity Log" via `Title` — stays unchanged. It is not the same thing as notification preferences.

**`UserProfileMenu.razor`**

- Add a new `MudMenuItem` labelled **"My Profile"**, with icon `Icons.Material.Filled.Person`, positioned above the existing "View Token" item, navigating to `profile` (relative, resolves to `/app/profile`).
- Existing items (View Token, Settings, Logout) are unchanged.

**`Settings.razor`**

- If the page is not already tab-structured, wrap its content in `<MudTabs>` during this phase. Existing settings content becomes the first tab.
- Add a **Notifications** tab whose content is moved from the existing `Pages/Settings/Notifications.razor` (or equivalent — the plan phase identifies the exact file).
- The route `/settings/notifications` is removed. If existing bookmarks or deep links matter, add a server-side redirect from `/settings/notifications` to `/settings?tab=notifications`.

---

## 7. Data flow

### 7.1 Read (form load, autofill ON)

```
Form page mounts
    │
    ▼
IPersonaService.GetAsync()
    │
    ├── cache hit → return cached PersonaReadModelV1
    │
    └── cache miss:
          HTTP GET /api/me/persona
              │
              ▼
          Tenant PersonaService
              │
              ├── no row → return empty PersonaReadModelV1 (200)
              │
              └── row present:
                    POST Wallet Service /persona/decrypt (S2S, persona:crypto)
                        │
                        ▼
                    Wallet derives sorcha:persona-vault key, decrypts, returns plaintext
                        │
                        ▼
                    Tenant wraps scalars in PersonaAttribute<T>, returns PersonaReadModelV1
    │
    ▼
Client caches (session lifetime), returns to caller
    │
    ▼
SorchaFormRenderer.OnInitializedAsync
    ├── read global autofill toggle
    ├── PersonaAutofillResolver.Resolve(layout, schema, persona)
    └── apply or stash fills; render with cream tint + self tick
```

### 7.2 Write (profile save)

```
User edits Profile.razor → Save
    │
    ▼
IPersonaService.UpdateAsync(persona)
    │
    ▼
HTTP PUT /api/me/persona (plaintext body)
    │
    ▼
Tenant PersonaService validates invariants
    │
    ▼
POST Wallet Service /persona/encrypt
    │
    ▼
Wallet returns (ciphertext, nonce, wrappedKeyRef)
    │
    ▼
Tenant upserts PlatformUserPersona row
    │
    ▼
Client invalidates cache, refetches, renders canonical state
```

### 7.3 Edit of an autofilled field

```
Field is in _personaFilledPaths
    │
    ▼
User types → MudBlazor value-changed
    │
    ▼
SorchaFormRenderer removes path from _personaFilledPaths
    │
    ▼
Scoped CSS re-evaluates → .autofilled class removed → cream tint gone, tick removed
    │
    ▼
Form data bag holds user-entered value; submission carries no persona provenance for that field
```

---

## 8. Error handling

Handled only at real boundaries. Internal code trusts its own invariants.

| Failure | Layer | Response |
|---|---|---|
| Tenant DB unavailable on `GET /me/persona` | Service | 503. Client surfaces a non-blocking snackbar "Profile unavailable — forms will be empty" and renders the form without autofill. |
| Wallet Service unavailable for decrypt | Tenant orchestration | 503 with a distinguishable error code. Client behaves the same as DB-down: form still fully functional. |
| Decryption failure (corrupt ciphertext) | Wallet | 500 with PII-free diagnostics. Client shows "Couldn't load your profile — please contact support". |
| Schema validation failure on write | Tenant | 400 with field-level errors. Profile page shows inline validation. |
| `actingAs` other than `self` in v1 | Tenant endpoint | 400 `actingAs_not_supported`. |
| Multiple `IsDefault=true` on write | Tenant validation | 400 `multiple_defaults`. |
| Malformed `x-persona` extension in blueprint | Resolver | Skip the field, log a warning. Form renders, field is manually filled. |
| Inference match but schema declares a `default` | Resolver | Schema default wins. |
| Autofill toggle missing from user settings | Client | Default to ON, no error. |
| Empty persona (new user) | Tenant | 200 with empty `PersonaReadModelV1`. Never 404. |
| User has no primary wallet yet | Tenant write path | 409 `wallet_not_provisioned`. Read path returns empty persona. |

---

## 9. Testing

### Unit tests

**`Sorcha.Tenant.Service.Tests/PersonaServiceTests.cs`**

- Get returns empty persona for new user.
- Get decrypts via Wallet client and wraps attributes in `PersonaAttribute<T>`.
- Update validates exactly-one-default per list; rejects multiple; promotes first when zero explicit.
- Update rejects malformed email, phone, country codes.
- Delete wipes row and is idempotent (204 whether or not a row existed).
- `actingAs != "self"` returns 400.
- User with no primary wallet: read returns empty 200; write returns 409 `wallet_not_provisioned`.
- Audit log entry written on every write, never on read.

**`Sorcha.UI.Core.Tests/PersonaAutofillResolverTests.cs`**

- Explicit `x-persona` match fills field.
- Explicit `x-persona: false` blocks inference.
- Inference: `format: "email"` → `DefaultEmail`.
- Inference: `format: "tel"` → `DefaultPhone`.
- Inference: field name `dateOfBirth` / `dob` / `birthDate` → `DateOfBirth`.
- Schema default beats persona match.
- Empty persona returns empty map.
- Nested fields resolve via JSON Pointer paths.
- Array-of-objects fields are not autofilled.
- Malformed `x-persona` value is skipped with a warning.

**`Sorcha.Cryptography.Tests/` additions**

- `sorcha:persona-vault` derivation is deterministic per seed.
- Derived key is distinct from `sorcha:docket-signing`, `sorcha:register-control`, and the root key.
- Round-trip encrypt/decrypt with XChaCha20-Poly1305 using the derived key.

### Integration tests

**`Sorcha.Tenant.Service.IntegrationTests/PersonaEndpointsTests.cs`**

- Full GET → PUT → GET round-trip preserves all attribute types.
- PATCH merges without clobbering untouched fields.
- DELETE then GET returns empty, not 404.
- Anonymous request returns 401.
- Rate limit policy `RateLimitPolicies.Api` applies.
- Audit log entries written on every write.

**`Sorcha.Wallet.Service.IntegrationTests/PersonaCryptoEndpointsTests.cs`**

- Encrypt/decrypt round-trip yields identical plaintext.
- Tampered ciphertext returns 500 with sanitized error.
- `persona:crypto` scope required; other scopes return 403.
- Endpoints not reachable through API Gateway (gateway-config assertion).

### End-to-end tests (Playwright, Docker infrastructure)

**`Sorcha.UI.E2E.Tests/PersonaAutofillTests.cs`**

- Fresh user fills persona at `/profile`, saves, then opens a blueprint action form with matching fields — fields render with cream tint and `self` tick.
- Edit an autofilled field → tint and tick disappear.
- "Review" popover lists filled field → attribute mapping.
- "Clear all" wipes autofilled fields; user-typed fields are untouched.
- Toggle autofill OFF → form loads empty with a "Fill from profile" button present.
- Click "Fill from profile" → same tinted result as automatic.
- Multi-value: user with three emails has the default email filled into the form.

**`Sorcha.UI.E2E.Tests/NavTidyTests.cs`**

- Drawer has no "Navigation" header at the top.
- Side nav has no "Settings" or "Notifications" entries.
- `UserProfileMenu` contains "My Profile" and "Settings".
- Clicking "My Profile" navigates to `/app/profile`.
- Settings page has a "Notifications" tab containing the notification preferences content.

### Contract guard tests

**`Sorcha.UI.Core.Tests/PersonaServiceContractTests.cs`**

- `IPersonaService.GetAsync` accepts `PersonaReadOptions?` (reflection assertion — prevents silent regression when delegation lands).
- `PersonaAttribute<T>` DTO shape is stable; any property change requires an explicit test update.

---

## 10. Follow-ups (tracked, out of scope)

1. **Per-form autofill override** — new `AutofillFromPersona` parameter on `SorchaFormRenderer` and/or an equivalent `x-autofill` blueprint extension, so specific forms can opt out of autofill regardless of the global toggle.
2. **Wallet delegation / PoA unwrap** — per-recipient `PersonaContentKey` wrapping plumbed through the delegation grant, enabling a delegate to read the principal's persona when filling a form on their behalf.
3. **"Filling on behalf of" banner** — UI surface when a delegate is filling a form under an active delegation grant.
4. **VC-backed attributes and blue tick** — when verifiable credential issuance is mature, attribute `Source` can flip to `VerifiedCredential` with the issuer DID captured in `VerifiedBy`.
5. **Per-form alternate-value picker** — for users with multiple emails/phones/addresses, let them pick a non-default entry for a specific form without changing their persona default.
6. **A→C migration to client-side decryption** — when self-custody mode lands, move the decryption boundary from Tenant↔Wallet (server) to Client↔Wallet (WASM). The `IPersonaService` contract stays stable.
7. **Freeform "remembered answers" bag** — growth path beyond the 12 typed essentials, to capture jurisdiction-specific fields the user has typed before.

---

## 11. Files affected (indicative)

New:

- `src/Services/Sorcha.Tenant.Service/Endpoints/PersonaEndpoints.cs`
- `src/Services/Sorcha.Tenant.Service/Services/PersonaService.cs`
- `src/Services/Sorcha.Tenant.Service/Data/` — new entity; existing initial migration edited
- `src/Services/Sorcha.Wallet.Service/Endpoints/PersonaCryptoEndpoints.cs`
- `src/Common/Sorcha.Tenant.Models/Persona/*` — `PersonaAttributesV1`, `PersonaReadModelV1`, `PersonaAttribute<T>`, etc.
- `src/Common/Sorcha.ServiceClients.Http/IPersonaClient.cs`
- `src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/Persona/IPersonaService.cs` + implementation
- `src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/Forms/PersonaAutofillResolver.cs`
- `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Forms/PersonaFillSummary.razor`
- `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/Profile.razor`
- Test projects as listed in §9

Modified:

- `src/Common/Sorcha.Cryptography/` — add `PersonaVault` derivation constant
- `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Forms/SorchaFormRenderer.razor` + `.razor.css`
- `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Components/Layout/MainLayout.razor`
- `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Shared/UserProfileMenu.razor`
- `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/Settings.razor`
- Tenant Service initial setup migration (folded, not new file)
- `CLAUDE.md` — add a Persona API section following the existing pattern
