# Phase 1 Data Model: Shared User-Facing UI Component Library

**Feature**: 122-shared-user-components
**Date**: 2026-05-10

This feature is a code-organisation change rather than a data-model change. No persistent entities, no new database schema, no new transaction shape, no new wire format. The "data model" here describes the **structural model of the library system itself** — the project boundaries, the component groupings, the service-interface surface, and the consumer reference graph. These are the entities the planning and execution phases reason about.

---

## Structural entities

### Library projects (after migration)

| Entity | Role | Lifecycle |
|--------|------|-----------|
| `Sorcha.UI.Components.User` | New shared Razor class library. Holds user-facing components, their supporting services, and the model types they consume. | Created in this feature. |
| `Sorcha.UI.Core` | Existing Razor class library. After the migration, retains only admin / designer / explorer / blueprint-authoring / configuration / register-admin / templates / workflow components and their supporting services and models. Adds a ProjectReference to `Sorcha.UI.Components.User` so its existing six consumers transitively see the moved components. | Survives the migration with reduced scope. |
| `Sorcha.UI.{Admin, App, Designer, Explorer, Web, Web.Client}` | Existing Blazor WASM host apps. No csproj change. Continue to reference `Sorcha.UI.Core`, which now transitively re-exports the moved components. | Unchanged. |
| `Sorcha.Citizen.Wallet` | Existing Blazor WASM PWA host. Adds a single ProjectReference to `Sorcha.UI.Components.User`. Three of its local user-facing components migrate INTO the new library. | Gains a reference; loses three local files. |
| `Sorcha.UI.Shared` | Existing empty project. Left untouched by this feature. | No change. |

### Component groupings inside `Sorcha.UI.Components.User`

| Grouping | Source | Headline members | Notes |
|---------|--------|------------------|-------|
| `Components/Forms/` | moved from `Sorcha.UI.Core/Components/Forms/` | `SorchaFormRenderer`, `ControlDispatcher`, `ReviewSummaryRenderer`, `PersonaFillSummary`, plus `Controls/`, `Layouts/IdCardLayout`, `Panels/` | Headline payload of the library — the form-rendering engine |
| `Components/Credentials/` | moved from `Sorcha.UI.Core/Components/Credentials/` | `CredentialCard`, `CredentialAcceptCard`, `CredentialDetailView`, `DisclosurePicker`, `IssuanceSummaryPanel`, `PresentationRequest*`, `PresentationSubmitDialog`, `QrPresentationDisplay`, `VerificationTrustView` | 13 components, all user-facing credential UX |
| `Components/Wallet/` | moved from `Sorcha.UI.Core/Components/Wallet/` | `ReceiptProofCard`, `TransactionDetailDrawer`, `TransactionLifecycleTicks` + `TransactionTickStatus` | Feature 079 receipt + tick-status surface |
| `Components/Participants/` | moved from `Sorcha.UI.Core/Components/Participants/` | `ParticipantList`, `ParticipantSearch`, `ParticipantDetail`, `ParticipantForm`, `PublishParticipantDialog`, `WalletLinkForm` | 6 components |
| `Components/Presentation/` | moved from `Sorcha.Citizen.Wallet/Components/` | `ConsentSheet`, `CredentialPickerDialog`, `NoMatchingCredentialDialog` | Inverse migration — wallet-grown components elevated to the shared library |
| `Components/Shared/` | partial move from `Sorcha.UI.Core/Components/Shared/` | `ConfirmDialog`, `EmptyState`, `JsonTreeNode`, `JsonTreeView`, `JwtViewerDialog`, `ResizableSplitter`, `ServiceUnavailable`, `TruncatedId` | Generic primitives. Web-chrome members (`BreadcrumbNav`, `UserProfileMenu`, `LogoutConfirmDialog`) stay in UI.Core. |

### Service groupings inside `Sorcha.UI.Components.User/Services/`

| Grouping | Source | Members | Migration kind |
|---------|--------|---------|----------------|
| `Services/Forms/` | moved from `Sorcha.UI.Core/Services/Forms/` | `IFormSchemaService` + concrete, `ReviewSummaryDataSource`, autofill resolver | Whole subfolder moves |
| `Services/Persona/` | moved from `Sorcha.UI.Core/Services/Persona/` | `IPersonaService` + concrete, persona attribute resolver | Whole subfolder moves |
| `Services/Credentials/` | moved from `Sorcha.UI.Core/Services/Credentials/` | `ICredentialApiService`, `IQrPresentationService` + concretes | Whole subfolder moves |
| `Services/AddressLookup/` | moved from `Sorcha.UI.Core/Services/AddressLookup/` | postal-address autocomplete used by the form renderer | Whole subfolder moves |
| `Services/Identity/` | partially moved from `Sorcha.UI.Core/Services/Identity/` | interfaces only (`IAuthenticationStateClient` if needed) | Interfaces in library; concretes per-host |
| `Services/Navigation/` | partially moved from `Sorcha.UI.Core/Services/Navigation/` | interfaces only | Same |
| (admin, configuration, designer, encryption, http) | not moved | — | stay in UI.Core |

### Model groupings inside `Sorcha.UI.Components.User/Models/`

| Grouping | Verdict | Note |
|---------|---------|------|
| `Actions`, `Common`, `Credentials`, `Forms`, `Participants`, `Wallet` | MOVE | consumed directly by migrating components |
| `Authentication` | REVIEW | move only the subset referenced by migrating components |
| `Admin`, `Blueprints`, `Chat`, `Configuration`, `Dashboard`, `Designer`, `Encryption`, `Explorer`, `Registers`, `SchemaLibrary`, `Templates`, `Workflows` | STAY | admin/designer/explorer-only |

### Reference graph (post-migration)

```text
                        Sorcha.UI.Components.User
                          (NEW shared library)
                          ▲                  ▲
                          │                  │
           ┌──────────────┘                  └─────────────┐
           │ ProjectReference                              │ ProjectReference
           │                                               │
    Sorcha.UI.Core                                Sorcha.Citizen.Wallet
   (admin/designer/etc.)                                (PWA)
           ▲
           │ ProjectReference (already present, unchanged)
           │
   Sorcha.UI.{Admin, App, Designer, Explorer, Web, Web.Client}
```

Two new reference edges total. Six existing host apps see no csproj change.

---

## Naming and namespace policy

- The new library uses root namespace `Sorcha.UI.Components.User` for new types it introduces.
- **Migrated types preserve their existing namespaces.** Files moved from `Sorcha.UI.Core/Components/Credentials/` retain the namespace `Sorcha.UI.Core.Components.Credentials` (or whatever the file currently declares). This is the only mechanism that keeps consumer `@using` directives valid across the move without touching them.
- Consumer pages in the six web host apps are updated to add `@using` lines only when the namespace change cannot be avoided. The migration prefers preserving namespaces to minimise consumer churn.
- Future net-new components added to the library use `Sorcha.UI.Components.User` namespaces and contribute to the long-term naming convergence.

---

## Component contract policy

A "component contract" is the declared interface (parameters, callbacks, expected host-registered services) through which a shared component communicates with its host. The policy:

- **Parameters** are declared on the Razor component via `[Parameter]`. Host pages bind real values.
- **Callbacks** are declared via `EventCallback<T>` or `EventCallback`. Host pages bind real handlers.
- **Services** the component depends on come via `@inject`. The host's `Program.cs` registers concrete implementations. The library does NOT register services itself — registration is each host's responsibility, exposed by an `Extensions/IServiceCollection.AddSorchaUserComponents(...)` helper that registers the library's own internal services (Forms, Persona, Credentials, AddressLookup) but leaves host-specific services (auth, navigation) to the host.

This policy lets the same component work under different host service compositions without the component branching on host identity. The R2 finding — that the migrating components inject only library-owned services plus framework primitives (`HttpClient`, `IJSRuntime`, `ILogger`, `IServiceProvider`) — means very few contracts need new interface design in this feature.

---

## What this feature does NOT introduce

The following entities are explicitly NOT part of this feature's data model and would require a future spec:

- `IUserSigner` or any signing-seam interface.
- Custody-mode markers, enums, or configuration types.
- New persistence entities, migrations, or wire formats.
- New telemetry meters or activity sources (existing component telemetry preserved).
- New auth claims, scopes, or roles.

Recording these here so a reader of the data model can see immediately what is and is not in scope.
