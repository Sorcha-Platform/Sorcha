# Contract — Form Renderer Family

## Components

| Component | Source path (pre-migration) | Target path (post-migration) |
|-----------|----------------------------|------------------------------|
| `SorchaFormRenderer.razor` | `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Forms/` | `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Components/Forms/` |
| `ControlDispatcher.razor` | same source | same target |
| `ReviewSummaryRenderer.razor` | same source | same target |
| `PersonaFillSummary.razor` | same source | same target |
| `Controls/*` | `Sorcha.UI.Core/Components/Forms/Controls/` | `Sorcha.UI.Components.User/Components/Forms/Controls/` |
| `Layouts/*` (incl. `IdCardLayout.razor`) | `Sorcha.UI.Core/Components/Forms/Layouts/` | `Sorcha.UI.Components.User/Components/Forms/Layouts/` |
| `Panels/*` | `Sorcha.UI.Core/Components/Forms/Panels/` | `Sorcha.UI.Components.User/Components/Forms/Panels/` |

## Parameters (preserved verbatim)

The migration preserves every existing `[Parameter]` exposed by these components — name, type, default value, attribute order. Any change to the parameter surface during the migration is a regression and must be reverted before the PR merges.

Key parameters worth naming explicitly (the wider parameter surface stays as-is):

- `SorchaFormRenderer.Schema` — JSON schema (`JsonElement` or schema model) defining the form
- `SorchaFormRenderer.Value` — the form's current value (two-way bound)
- `SorchaFormRenderer.ValueChanged` — change callback (two-way bound)
- `SorchaFormRenderer.ReadOnly` — display-only flag
- `SorchaFormRenderer.PersonaAutofill` — whether to apply persona-based autofill
- `ReviewSummaryRenderer.LayoutConfig` — Feature 107 `x-review` layout configuration

## Injected services

| Service | Owner | Registration |
|---------|-------|--------------|
| `IFormSchemaService` | `Sorcha.UI.Components.User/Services/Forms/` | Library-registered via `AddSorchaUserComponents()` |
| `ReviewSummaryDataSource` | `Sorcha.UI.Components.User/Services/Forms/` | Library-registered |
| `ILogger<SorchaFormRenderer>` | framework | Host-registered (both shells already do) |
| `IJSRuntime` | framework | Host-registered (both shells already do) |
| `IServiceProvider` | framework | Host-registered (both shells already do) |

`ControlDispatcher` consumes `IServiceProvider` to resolve the right control type for each schema field — this is the existing dispatch pattern and survives the migration unchanged.

## Callbacks

- `ValueChanged` (two-way binding) — every value mutation by any control bubbles up via this single callback. Host pages bind it to their own state.
- Field-level validation results surface through the standard MudBlazor validation pipeline — no additional callback needed at the renderer surface.

## Host responsibilities

A host shell consuming `SorchaFormRenderer`:

1. Calls `services.AddSorchaUserComponents(configuration)` in `Program.cs` to register `IFormSchemaService`, `ReviewSummaryDataSource`, persona services, credential services, and address-lookup.
2. Ensures `MudBlazor` is registered (both shells already do).
3. Ensures an `HttpClient` is registered (both shells already do).
4. Supplies a `Schema` and binds `Value` to its own page state.
5. Optionally provides persona autofill by also registering an `IPersonaService` implementation — falling back to the library default if not.

The PWA's host must replicate the same registrations. No PWA-specific code paths inside the components.

## Out of contract

- Action submission. The form renderer surfaces validated form values via `ValueChanged`; submitting the form to a Sorcha workflow action is the host page's concern. The library does not own the submission HTTP call, the signing path, or the receipt UX.
- Custody-mode-specific behaviour. The form renderer does not know whether the host signs locally or server-side.
- The `IUserSigner` seam. Not introduced by this feature.

## Verification

1. **Given** a host page with a JSON schema, **When** it renders `SorchaFormRenderer` with that schema, **Then** the same fields and validation behaviour appear as in the pre-migration web app — verified by an existing bUnit test (moved into the new test project) that renders a representative schema and asserts the produced HTML structure.
2. **Given** the PWA registers the library's default services via `AddSorchaUserComponents()`, **When** the PWA renders `SorchaFormRenderer` on a test page, **Then** the form renders without runtime exceptions and accepts user input — verified by a Playwright smoke test (added in a later phase if PWA E2E is in scope; otherwise verified by manual confirmation during the migration commit 3).
3. **Given** the migrated codebase, **When** the existing `Sorcha.UI.Core.Tests` form-renderer tests run, **Then** all previously passing tests still pass with no test code change beyond `@using` updates.
