# Contract — Persona Panel & Autofill

## Components

| Component | Source path | Target path |
|-----------|-------------|-------------|
| `PersonaFillSummary.razor` | `Sorcha.UI.Core/Components/Forms/` | `Sorcha.UI.Components.User/Components/Forms/` |

The user-facing persona surface beyond the fill summary lives in host-owned pages (e.g., the web app's `MyProfile.razor`). This contract scopes only the embeddable summary banner that `SorchaFormRenderer` renders above autofilled forms.

## Parameters (preserved verbatim)

- `PersonaFillSummary.Fills` — `IReadOnlyList<PersonaFillResult>` describing each autofilled field (path, attribute, value, source)
- `PersonaFillSummary.OnReview` — callback to open the per-field review UX
- `PersonaFillSummary.OnClearAll` — callback to revert all autofill
- `PersonaFillSummary.AutofillEnabled` — whether silent autofill is active (controls "Fill from profile" button rendering when false)

## Injected services

| Service | Owner | Registration |
|---------|-------|--------------|
| `IPersonaService` | `Sorcha.UI.Components.User/Services/Persona/` | Library-registered via `AddSorchaUserComponents()` |

`IPersonaService` is the session-cached client facade documented under Feature 092 (`sorcha-architecture` skill). It reads `/me/persona` and exposes `GetAsync`, `UpdateAsync`, `SetAutofillEnabledAsync`. The library carries this service so both shells share the same caching and toggle behaviour.

## Host responsibilities

1. Register `IPersonaService` via `AddSorchaUserComponents()` (no host-specific persona registration needed).
2. Provide an authenticated `HttpClient` configured to reach the persona endpoint (both shells already do this for service-client use).
3. Handle the `OnReview` callback by navigating to a review surface — the review page itself is host-owned (the web app's `MyProfile.razor` already exists; the PWA's equivalent is part of the broader user-agent unification roadmap).

## Out of contract

- Persona encryption / decryption. Owned by Wallet Service (Feature 092). The component receives plaintext attribute values from `IPersonaService` after server-side decryption.
- Persona update transactions. Owned by Tenant Service; the host page calls `IPersonaService.UpdateAsync` directly from its profile-editing UX.
- `x-persona` schema-extension parsing. Owned by `IFormSchemaService` and `PersonaAutofillResolver` — both library-internal.

## Verification

1. **Given** three autofilled fields, **When** `PersonaFillSummary` renders, **Then** the banner shows three entries with field labels and a self-asserted source tick — verified by bUnit test.
2. **Given** `AutofillEnabled = false`, **When** the component renders, **Then** the banner shows a single "Fill from profile" call-to-action button instead of the per-field summary — verified by bUnit test.
3. **Given** the PWA registers `AddSorchaUserComponents()`, **When** the PWA's form-host page binds `PersonaFillSummary` to a populated `Fills` list, **Then** the banner renders identically to the web app — visually verified during commit 3.
