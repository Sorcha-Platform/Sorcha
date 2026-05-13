# Implementation Plan: Shared User-Facing UI Component Library

**Branch**: `122-shared-user-components` | **Date**: 2026-05-10 (revised 2026-05-11) | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/122-shared-user-components/spec.md`

> **2026-05-13 status:** Phase 1 (scaffold) complete. Phase 2 unblocked — Feature 123 (UI.Core audience-folder split) merged to master via PR #641 on 2026-05-13. `Sorcha.UI.Core` now partitions Services and Models into `User/`, `Admin/`, `Shared/` audience folders; `IRegisterService` is split into `IRegisterReadService` + `IRegisterGovernanceService`; `OrganizationDto`/`BrandingDto`/`SchemaOverlayFieldInfo` live in `Services/Shared/`. Phase 2 resumes with a refreshed Phase 0 research pass — see [phase-2-discovery.md](./phase-2-discovery.md) for the original forensic narrative, and the updated `research.md` for the verdict tables that reflect the post-Feature-123 codebase.

## Summary

Extract the user-facing component subset of `Sorcha.UI.Core` into a new `Sorcha.UI.Components.User` Razor class library that both the Sorcha.UI web app family and the `Sorcha.Citizen.Wallet` PWA reference. The new library carries Forms, Credentials, Wallet (user-facing parts), Participants, and the user-facing Shared components — plus the services and models those components actually need. The existing `Sorcha.UI.Core` retains the admin / designer / explorer / blueprint-authoring surface and its heavy transitive dependencies (`Blazor.Diagrams`, `YamlDotNet`). Host shells (web vs. PWA) wire concrete implementations of the services that shared components consume via interfaces.

The 2026-05-10 spike established empirically that referencing `Sorcha.UI.Core` whole introduces ~3.26 MB of irrelevant assembly plus designer-grade transitive deps into the PWA bundle, which makes the extraction approach load-bearing rather than aesthetic.

## Technical Context

**Language/Version**: C# 14 on .NET 10 (per Sorcha constitution v1.1.0)
**Primary Dependencies**: `Microsoft.NET.Sdk.Razor`, Blazor WebAssembly, MudBlazor, `Microsoft.AspNetCore.Components.Authorization`, the `Sorcha.Blueprint.Models` / `Sorcha.Register.Models` / `Sorcha.Tenant.Models` / `Sorcha.ServiceClients.Http` model + client packages already consumed by `Sorcha.UI.Core`. The new library MUST NOT take `Z.Blazor.Diagrams` or `YamlDotNet` as direct or transitive dependencies.
**Storage**: N/A (UI-only feature; storage is the consuming hosts' concern)
**Testing**: xUnit + bUnit for Razor component tests, FluentAssertions for assertions, optional Playwright for end-to-end shell-level checks (consumed via existing `tests/Sorcha.UI.E2E.Tests` project for the web shell; PWA equivalent deferred per Feature 114 deferred items)
**Target Platform**: Browser via Blazor WebAssembly — components must render under both the Sorcha.UI shell and the Sorcha.Citizen.Wallet PWA shell without host-specific branching inside the component
**Project Type**: Web — single front-end component library consumed by multiple Blazor WASM host applications
**Performance Goals**: Bundle-size impact for the PWA bounded by genuinely user-facing component payload only; runtime parity with current Sorcha.UI behaviour (no visible perf regression); no new transitive dependency exceeds 200 KB compressed without explicit justification
**Constraints**: Blazor WASM trimming is reflection-unfriendly for Razor components — library boundaries must be drawn so unused admin/designer code is *physically absent* rather than relying on trimming to remove it. Components consume host-registered services through declared interfaces (registered separately by each shell's `Program.cs`); components do not depend on a specific host's service composition.
**Scale/Scope**: ~50 user-facing components across Forms (root + Controls/Layouts/Panels), Credentials (13 components), Wallet (user-facing subset of 4), Participants (6), and Shared (user-facing subset of 11). Roughly 12–15 supporting services. Roughly 10–12 supporting model groups. Six web-app references to retarget (Admin, App, Designer, Explorer, Web, Web.Client) plus one PWA reference to add.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

The Sorcha constitution targets microservices and service-shaped concerns. Several principles do not apply to a pure Razor-class-library extraction. Applicable gates:

- **I. Microservices-First** — N/A (no service introduced; existing layered dependency direction preserved: `Sorcha.UI.Components.User` depends only on `Common/*` models and `ServiceClients.Http`, never upward into any service).
- **II. Security First** — Applies: input validation, no secrets in code, JSON Schema validation (via existing `SorchaFormRenderer`). The migration does NOT change validation surfaces; it relocates them.
- **III. API Documentation** — N/A (no APIs added). XML doc requirement applies to any new public types introduced by the extraction.
- **IV. Testing Requirements** — Applies: existing component tests (where they exist) must continue to pass; any new abstractions introduced (component contracts / service interfaces) require xUnit + bUnit coverage targeting >85% on new code.
- **V. Code Quality** — Applies: C# 14 / .NET 10, nullable enabled, no new Release-build warnings, async/await for I/O, DI throughout.
- **VI. Blueprint Standards** — N/A.
- **VII. Domain-Driven Design** — Applies: shared component naming MUST preserve ubiquitous language (Blueprint, Action, Participant, Disclosure, Publish). No renaming during the migration.
- **VIII. Observability** — Applies weakly: any new structured logging follows the no-string-interpolation rule. UI components do not typically own telemetry, but any new host-facing service interface that wraps an existing OTel-instrumented operation must preserve the activity span.

**Gate verdict**: PASS — no constitution violations. The migration is mechanical with strict preservation of validation, naming, telemetry, and dependency direction.

## Project Structure

### Documentation (this feature)

```text
specs/122-shared-user-components/
├── plan.md                    # this file
├── spec.md                    # feature specification
├── research.md                # Phase 0 — component inventory + migration verdicts
├── data-model.md              # Phase 1 — library boundaries, component contracts
├── quickstart.md              # Phase 1 — developer workflow for the new library
├── contracts/                 # Phase 1 — component contract docs (one per major component family)
│   ├── README.md              # contract pattern overview
│   ├── form-renderer.md
│   ├── credential-card.md
│   ├── consent-sheet.md
│   ├── persona-panel.md
│   ├── participant-picker.md
│   └── file-upload.md
├── checklists/
│   └── requirements.md        # specification quality checklist (already exists, all pass)
└── tasks.md                   # Phase 2 — (created by /speckit.tasks, not here)
```

### Source Code (repository root)

The new library lives alongside existing `Sorcha.UI.Core` under `src/Apps/Sorcha.UI/`. Both files-and-folders moves and host references are coordinated.

```text
src/Apps/Sorcha.UI/
├── Sorcha.UI.Components.User/        # NEW — shared user-facing library
│   ├── Components/
│   │   ├── _Imports.razor
│   │   ├── Forms/                    # MOVED FROM Sorcha.UI.Core/Components/Forms
│   │   │   ├── ControlDispatcher.razor
│   │   │   ├── PersonaFillSummary.razor
│   │   │   ├── ReviewSummaryRenderer.razor
│   │   │   ├── SorchaFormRenderer.razor
│   │   │   ├── Controls/
│   │   │   ├── Layouts/              # IdCardLayout etc.
│   │   │   └── Panels/
│   │   ├── Credentials/              # MOVED — 13 components
│   │   ├── Wallet/                   # MOVED — TransactionDetailDrawer, TransactionLifecycleTicks, ReceiptProofCard
│   │   ├── Participants/             # MOVED — 6 components
│   │   ├── Presentation/             # NEW — ConsentSheet, CredentialPickerDialog,
│   │   │                             #         NoMatchingCredentialDialog (MOVED FROM Sorcha.Citizen.Wallet/Components,
│   │   │                             #         replacing the wallet-local versions)
│   │   └── Shared/                   # SUBSET MOVED FROM Sorcha.UI.Core/Components/Shared
│   │                                 # (BreadcrumbNav, UserProfileMenu, LogoutConfirmDialog stay web-only;
│   │                                 #  ConfirmDialog, EmptyState, JsonTreeNode, JsonTreeView, JwtViewerDialog,
│   │                                 #  TruncatedId, ResizableSplitter, ServiceUnavailable move)
│   ├── Services/                     # MOVED subset of Sorcha.UI.Core/Services
│   │   ├── Forms/                    # IFormRenderingService etc.
│   │   ├── Persona/                  # IPersonaService + autofill resolver
│   │   ├── Credentials/              # credential cache + presenter helpers
│   │   ├── AddressLookup/
│   │   ├── Identity/                 # interfaces only (e.g., IAuthenticationStateClient);
│   │   │                             #   concrete impls remain per-host
│   │   └── Navigation/               # interfaces only; concrete impls per-host
│   ├── Models/                       # MOVED subset of Sorcha.UI.Core/Models
│   │   ├── Actions/                  # action models the form renderer + credential card use
│   │   ├── Common/
│   │   ├── Credentials/
│   │   ├── Forms/
│   │   ├── Participants/
│   │   └── Wallet/                   # subset — UI display models, not infrastructure
│   ├── Extensions/                   # AddSorchaUserComponents(IServiceCollection) DI helper
│   ├── wwwroot/                      # static assets (scoped CSS bundle entry, fonts, images)
│   └── Sorcha.UI.Components.User.csproj   # references only MudBlazor + Components.Authorization
│                                          # + Common/Models + ServiceClients.Http
│                                          # NO Z.Blazor.Diagrams, NO YamlDotNet, NO Blazored.LocalStorage
│
├── Sorcha.UI.Core/                   # RETAINED — admin/designer/explorer/blueprints/configuration surface
│   ├── Components/
│   │   ├── Admin/                    # unchanged
│   │   ├── Blueprints/               # unchanged
│   │   ├── Configuration/            # unchanged
│   │   ├── Designer/                 # unchanged — pulls Z.Blazor.Diagrams + YamlDotNet
│   │   ├── Encryption/               # unchanged (admin-flavoured)
│   │   ├── Explorer/                 # unchanged
│   │   ├── Registers/                # unchanged (admin-flavoured)
│   │   ├── Templates/                # unchanged
│   │   ├── Wallets/                  # unchanged (org wallet admin)
│   │   ├── Workflows/                # unchanged
│   │   └── Shared/                   # web-shell-specific shared (BreadcrumbNav etc.)
│   └── Sorcha.UI.Core.csproj         # ADDS ProjectReference → Sorcha.UI.Components.User
│                                     # (UI.Core consumers transitively get user-facing components)
│
├── Sorcha.UI.Admin/                  # already references UI.Core; no csproj change needed
├── Sorcha.UI.App/                    # already references UI.Core; no csproj change needed
├── Sorcha.UI.Designer/               # already references UI.Core; no csproj change needed
├── Sorcha.UI.Explorer/               # already references UI.Core; no csproj change needed
├── Sorcha.UI.Web/                    # already references UI.Core; no csproj change needed
├── Sorcha.UI.Web.Client/             # already references UI.Core; no csproj change needed
└── Sorcha.UI.Shared/                 # CURRENTLY EMPTY — treat as a no-op for this feature

src/Apps/Sorcha.Citizen.Wallet/
├── Sorcha.Citizen.Wallet.csproj      # ADDS ProjectReference → Sorcha.UI.Components.User
├── Components/                       # ConfirmRevokeDialog, RenameDeviceDialog stay local;
│                                     # ConsentSheet, CredentialPickerDialog, NoMatchingCredentialDialog
│                                     # MOVED INTO Sorcha.UI.Components.User/Components/Presentation
├── Pages/
└── Program.cs                        # CHANGED — register host impls of the interfaces the shared
                                      # library declares (IAuthenticationStateClient,
                                      # INavigationContext, etc.). Concrete impls remain wallet-local.

tests/
├── Sorcha.UI.Components.User.Tests/  # NEW — xUnit + bUnit for the shared library
├── Sorcha.UI.Core.Tests/             # existing; tests for migrated components move here-into-Components.User.Tests
└── Sorcha.Citizen.Wallet.Tests/      # existing; integration tests for the wallet's use of shared components
```

**Structure Decision**: Single new Razor class library (`Sorcha.UI.Components.User`) under `src/Apps/Sorcha.UI/`, alongside the existing `Sorcha.UI.Core`. `Sorcha.UI.Core` adds a ProjectReference to the new library so all existing Sorcha.UI consumers transparently get the moved components without each of them updating their own references. The PWA adds a single direct ProjectReference to the new library. This keeps the change set surgical: one new project, one new reference from each end of the consumer graph, and a coordinated move of files between two existing projects.

## Complexity Tracking

> **Fill ONLY if Constitution Check has violations that must be justified**

No constitution violations. Section intentionally empty.
