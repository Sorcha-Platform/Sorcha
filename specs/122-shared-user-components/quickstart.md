# Quickstart — Sorcha.UI.Components.User

This document is the developer entry point for working with the shared user-facing component library after Feature 122 ships. It covers three workflows: **consuming** a shared component from a host shell, **adding** a new user-facing component, and **deciding** whether a new component belongs in the shared library or in `Sorcha.UI.Core`.

A condensed version of this document lives at `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/README.md` for in-repo discovery.

## Consuming a shared component from a host shell

Both `Sorcha.UI.Core` and `Sorcha.Citizen.Wallet` already reference `Sorcha.UI.Components.User` after Feature 122. The remaining wiring is at the host application level.

### Register the library's services

In the host's `Program.cs`:

```csharp
using Sorcha.UI.Components.User.Extensions;

builder.Services.AddSorchaUserComponents(builder.Configuration);
```

This registers `IFormSchemaService`, `IPersonaService`, `ICredentialApiService`, `IQrPresentationService`, the persona autofill resolver, and address-lookup. It does **not** register identity/navigation services — those remain host-specific.

### Add `@using` for the library

In `_Imports.razor`:

```razor
@using Sorcha.UI.Components.User.Components.Forms
@using Sorcha.UI.Components.User.Components.Credentials
@using Sorcha.UI.Components.User.Components.Wallet
@using Sorcha.UI.Components.User.Components.Participants
@using Sorcha.UI.Components.User.Components.Presentation
@using Sorcha.UI.Components.User.Components.Shared
```

Or import only the sub-namespaces a particular page uses.

### Use the component

```razor
<SorchaFormRenderer @bind-Value="formValue" Schema="schema" />
<CredentialCard Credential="credential" OnSelected="HandleSelected" />
<ConsentSheet PresentationRequest="request"
              MatchingCredentials="candidates"
              OnAccepted="HandleAccepted"
              OnDeclined="HandleDeclined" />
```

Parameters and callbacks are as documented in `specs/122-shared-user-components/contracts/`.

## Adding a new user-facing component

### Step 1 — Confirm the component belongs in the shared library

The library's scope is **user-facing components used by end users in either shell**. Apply this test:

> Would a citizen, an applicant, a field worker, or an employee using the PWA on their phone reasonably encounter this component as part of their day?

- **Yes** → the component belongs in `Sorcha.UI.Components.User`.
- **No** (it's part of tenant administration, blueprint authoring, register exploration, designer canvas, system configuration) → the component belongs in `Sorcha.UI.Core`.

If the answer is unclear, ask: "would this make sense on a phone screen?" That's the field-vs-desk heuristic from the 2026-05-10 user-agent unification design.

### Step 2 — Place the file in the right folder

Map the component's purpose to an existing folder:

| Folder | Purpose |
|--------|---------|
| `Components/Forms/` | Form rendering, controls, layouts, panels, autofill summary |
| `Components/Credentials/` | Credential cards, lists, detail views, lifecycle dialogs |
| `Components/Wallet/` | Transaction lifecycle ticks, receipts, transaction detail |
| `Components/Participants/` | Participant identity display, search, edit, wallet-link |
| `Components/Presentation/` | Consent sheets, credential pickers, presentation dialogs |
| `Components/Shared/` | Generic primitives (confirm dialog, empty state, JSON viewer, truncated id) |

If none of the above fits, create a new sibling folder under `Components/` rather than overloading an existing one.

### Step 3 — Honour the component-contract policy

- Parameters declared with `[Parameter]`.
- Callbacks declared with `EventCallback<T>` or `EventCallback`.
- Service dependencies declared with `@inject`.
- Services owned by the library are registered in `AddSorchaUserComponents()` — extend that helper to register a new internal service. Do not introduce `services.Add...` calls inside the component.
- Host-specific dependencies (authentication state, navigation context, signing capability) should be declared as **interfaces in the library** and implemented per-host. Today no such interface exists; if your component is the first to need one, define the interface in `Services/<area>/I<...>Client.cs`, then register concrete implementations in each host's `Program.cs`.

### Step 4 — Write the bUnit test

Add an xUnit + bUnit test under `tests/Sorcha.UI.Components.User.Tests/Components/<area>/<ComponentName>Tests.cs`. Coverage target ≥85% per Sorcha constitution. Follow the Arrange-Act-Assert and `MethodName_Scenario_ExpectedBehavior` naming conventions.

### Step 5 — Make sure the bundle stays clean

If your component introduces a new third-party dependency, **stop**. Confirm:

- Is the dependency genuinely needed for the user-facing flow, or is it a designer/admin convenience?
- Is the dependency under 200 KB compressed?
- Does it bring transitive deps that would also need scrutiny?

If the dependency would put the PWA in the same bind as `Z.Blazor.Diagrams` or `YamlDotNet` did pre-Feature-122, redesign without it. The library's value depends on staying lean.

Run `scripts/check-pwa-bundle.ps1` after building to confirm the new assembly does not introduce admin/designer/explorer artefacts. The script asserts (a) no `Blazor.Diagrams*`, (b) no `YamlDotNet*`, (c) no `Sorcha.UI.Core*` are present in the PWA bundle.

## Deciding where a component belongs — worked examples

| Imagined component | Verdict | Reasoning |
|--------------------|---------|-----------|
| "Tenant org branding editor" | `Sorcha.UI.Core` | tenant admin, desk-bound |
| "Blueprint canvas with drag-and-drop nodes" | `Sorcha.UI.Core` | designer surface; needs Z.Blazor.Diagrams |
| "Field-inspector site photo uploader" | `Sorcha.UI.Components.User` | end-user evidence capture; mobile context |
| "Multi-org switcher dropdown" | `Sorcha.UI.Core` | web-shell-specific navigation chrome |
| "Receipt-confirmed badge for a docket" | `Sorcha.UI.Components.User` | user-facing; both shells display this |
| "System-admin validator-roster editor" | `Sorcha.UI.Core` | admin tier |
| "Pending-acceptance credential card" | `Sorcha.UI.Components.User` | end-user inbox UX |
| "YAML blueprint validator with syntax highlighting" | `Sorcha.UI.Core` | designer-side; pulls YamlDotNet |

## Running the library locally

Standard `dotnet build` from the solution root builds the library and its consumers transitively. To exercise the PWA against the library:

```bash
dotnet build src/Apps/Sorcha.Citizen.Wallet/Sorcha.Citizen.Wallet.csproj
```

The published `wwwroot/_framework/` should contain `Sorcha.UI.Components.User.*.wasm` and **not** contain `Sorcha.UI.Core.*.wasm`, `Blazor.Diagrams*.wasm`, or `YamlDotNet*.wasm`. Confirm with:

```powershell
.\scripts\check-pwa-bundle.ps1
```

## What this library does NOT do

A non-exhaustive list of behaviours that are explicitly out of scope for the library, so a developer doesn't try to put them here:

- Signing transactions. The library carries no `IUserSigner` or equivalent. Signing belongs to the host's wallet service integration.
- Authentication. Auth state is host-managed; the library does not contain login pages, OAuth flows, or token handling.
- Navigation menus / app shells. Each host owns its own chrome, layout, and routing.
- Tenant administration UI. Stays in `Sorcha.UI.Core`.
- Blueprint designer / canvas. Stays in `Sorcha.UI.Core`.
- Register administration. Stays in `Sorcha.UI.Core`.

If your work requires any of the above, you are not building a shared component — you are building a host-specific feature, and it belongs in the host project.
