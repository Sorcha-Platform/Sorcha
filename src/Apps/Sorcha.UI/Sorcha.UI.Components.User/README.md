# Sorcha.UI.Components.User

Shared user-facing Razor component library consumed by both the Sorcha.UI web app family and the `Sorcha.Wallet.Pwa` PWA.

## Why this library exists

The PWA cannot afford to ship admin / designer / explorer code. Before Feature 122, `Sorcha.UI.Core` was the only Razor library and bundled `Z.Blazor.Diagrams`, `YamlDotNet`, and ~3.26 MB of designer-flavoured assembly into anything that referenced it. This library carries only what user-facing components need so the PWA's bundle stays clean.

The web app family transitively picks up this library via `Sorcha.UI.Core`'s ProjectReference — no host-app csproj changes were needed when the extraction landed.

## What lives here

| Surface | Examples |
|---|---|
| Components | `Forms/` (full schema-driven form renderer), `Credentials/` (13 cards / dialogs), `Wallet/` (transaction detail, receipt proof, lifecycle ticks), `Participants/` (6 components), `Shared/` (`ConfirmDialog`, `EmptyState`, `JsonTreeView`, etc.) |
| Services | `Services/User/*` (AddressLookup, Credentials, Forms, Participants, Persona, Wallet), `Services/Shared/*` (Authentication, Blueprints, Configuration, Encryption, Http, Identity, Navigation, Organization) |
| Models | `Models/User/*` and `Models/Shared/*` |
| Helpers | `Extensions/Shared/JsonDefaults`, `Utilities/Shared/{MimeTypeIconHelper, UrlValidator}` |

## What deliberately does NOT live here

- Admin / Designer / Explorer / Configuration / Encryption admin pages and their services — these stay in `Sorcha.UI.Core` because they justify UI.Core's continued existence.
- `Z.Blazor.Diagrams` and `YamlDotNet` — designer-only transitive deps. The csproj **must not** add these even if a moved file happens to compile-time reference them; that signals a misclassification.
- `BreadcrumbNav`, `UserProfileMenu`, `LogoutConfirmDialog` — web-chrome with auth assumptions the PWA does not share.

## Namespace policy (load-bearing)

The csproj sets `<RootNamespace>Sorcha.UI.Core</RootNamespace>` so every file moved from `Sorcha.UI.Core.*` namespaces keeps its declarations verbatim. The audience signal lives in the **folder**, not in the namespace. Consumer `using` directives across the six web host apps stay valid.

When you add a new file here, declare the same `Sorcha.UI.Core.{Components,Services,Models}.{Foo}` namespace you would have declared in UI.Core; place the file under the audience subfolder it belongs to (`User/`, `Shared/`).

## Audience-tag convention (Feature 123)

Inherited from `Sorcha.UI.Core/README.md`. Recap:

- `Services/User/` — services consumed only by user-facing components/pages.
- `Services/Shared/` — services consumed by both audiences (both web admin code and PWA user code).
- `Services/Admin/` — admin-only services. Stays in `Sorcha.UI.Core`.

Same partition for `Models/`, `Extensions/`, `Utilities/`.

### Bi-modal smell detector

If you find yourself writing a service interface that mixes a user-read operation with an admin governance operation, **split it**. The 2026-05-11 attempt at Feature 122 Phase 2 collapsed under exactly this pattern with `IRegisterService`; the recovery (Feature 123) split it into `IRegisterReadService` + `IRegisterGovernanceService`. The same smell test applies to new interfaces.

## Bundle hygiene

The PWA bundle must not contain `Blazor.Diagrams*`, `YamlDotNet*`, or `Sorcha.UI.Core*` assemblies. CI asserts this — see `scripts/check-pwa-bundle.ps1`. When you add a new PackageReference here, ask: does the PWA need this? If no, the dependency belongs in `Sorcha.UI.Core` not here.

## InternalsVisibleTo

This library grants `InternalsVisibleTo` to:

- `Sorcha.UI.Components.User.Tests` — for component-test access
- `Sorcha.UI.Core` — for cross-library internal helpers like `ReviewSummaryDataSource.PointerFromFieldName` that UI.Core still calls
- `Sorcha.UI.Core.Tests` — for legacy test access during the migration

The UI.Core grant is intentional: UI.Core is privileged here because it shipped as Components.User's sibling and its consumers transitively expect the same access surface they had pre-extraction.

## Where to add new code

| You're adding... | Put it here |
|---|---|
| A user-facing Razor component | `Components/{Forms,Credentials,Wallet,Participants,Shared}/` |
| A service consumed only by user-facing code | `Services/User/{subject}/` |
| A service consumed by both audiences | `Services/Shared/{subject}/` |
| A service used only by admin/designer/explorer | `Sorcha.UI.Core/Services/Admin/{subject}/` |
| A view-model record used by user-facing components | `Models/User/{subject}/` |
| A protocol or domain model shared by both audiences | `Models/Shared/{subject}/` |
