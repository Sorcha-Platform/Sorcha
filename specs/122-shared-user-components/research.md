# Phase 0 Research: Shared User-Facing UI Component Library

**Feature**: 122-shared-user-components
**Date**: 2026-05-10 (refreshed 2026-05-13 after Feature 123 merged)
**Status**: Refreshed — verdict tables updated to reflect post-Feature-123 audience folders

> **2026-05-13 refresh note:** Feature 123 (PR #641) split `Sorcha.UI.Core/Services/` and `Sorcha.UI.Core/Models/` into `User/`, `Admin/`, `Shared/` audience folders before this feature resumed. The migration verdict is now folder-level rather than file-by-file: every `Services/User/*` and `Models/User/*` folder moves wholesale; every `Services/Shared/*` and `Models/Shared/*` folder also moves (both audiences need them — admin reaches them transitively via UI.Core → Components.User ProjectReference, PWA reaches them directly); every `Admin/*` folder stays in UI.Core. R2 (host service coupling) is superseded by the folder-level verdicts in §R1' below; the original R2 grep methodology missed return-type and parameter-type coupling, which the audience folders now encode structurally. The pre-F123 file-by-file verdicts (R1, R3) are retained as historical context.

This document resolves the technical unknowns identified in `plan.md` Technical Context and consolidates evidence for the Phase 1 design.

---

## R1 — Component inventory and migration verdicts

**Question**: Which specific components in `Sorcha.UI.Core` are user-facing and should move, vs. which are admin/designer/explorer and should stay?

**Decision**: Verdicts by folder, grounded in a 2026-05-10 file-by-file inventory.

### Forms (`Sorcha.UI.Core/Components/Forms/`) — MOVE entire folder

| File | Verdict | Note |
|------|---------|------|
| `_Imports.razor` | MOVE | will be co-edited with the new library's `_Imports.razor` |
| `ControlDispatcher.razor` | MOVE | central dispatch for form controls |
| `PersonaFillSummary.razor` | MOVE | Feature 092 persona autofill banner |
| `ReviewSummaryRenderer.razor` | MOVE | Feature 107 `x-review` summary page |
| `SorchaFormRenderer.razor` | MOVE | the headline component — full schema-driven form rendering |
| `Controls/*` | MOVE | all field controls user-facing |
| `Layouts/IdCardLayout.razor` | MOVE | Feature 107 credential id-card layout |
| `Layouts/*` | MOVE | all layouts user-facing |
| `Panels/*` | MOVE | all form panels user-facing |

### Credentials (`Sorcha.UI.Core/Components/Credentials/`) — MOVE entire folder (13 files)

`CredentialAcceptCard`, `CredentialCard`, `CredentialCardList`, `CredentialDetailView`, `CredentialLifecycleDialog`, `DisclosurePicker`, `IssuanceSummaryPanel`, `PresentationRequestDetail`, `PresentationRequestDialog`, `PresentationRequestList`, `PresentationSubmitDialog`, `QrPresentationDisplay`, `VerificationTrustView` — all user-facing credential UX.

### Wallet (`Sorcha.UI.Core/Components/Wallet/`) — MOVE entire folder

`ReceiptProofCard.razor` (Feature 079 receipt + Merkle proof viewer), `TransactionDetailDrawer.razor`, `TransactionLifecycleTicks.razor` + `TransactionTickStatus.cs` (Feature 079 WhatsApp-style status). All four files user-facing.

### Wallets (`Sorcha.UI.Core/Components/Wallets/`) — STAY in UI.Core

`WalletAccessTab.razor` and `WalletListPanel.razor` are **org-wallet administration** (multi-org switching, access management) and belong to the desk-bound web app. Citizen wallets are single-tenant per-citizen.

### Participants (`Sorcha.UI.Core/Components/Participants/`) — MOVE entire folder (6 files)

`ParticipantDetail`, `ParticipantForm`, `ParticipantList`, `ParticipantSearch`, `PublishParticipantDialog`, `WalletLinkForm` — all user-facing participant identity surface. The PWA needs them for verifying identity in workflow contexts and for the user's own participant profile.

### Shared (`Sorcha.UI.Core/Components/Shared/`) — SPLIT

| File | Verdict | Note |
|------|---------|------|
| `BreadcrumbNav.razor` | STAY | web navigation chrome only |
| `UserProfileMenu.razor` | STAY | web header menu only |
| `LogoutConfirmDialog.razor` | STAY | tied to web auth flow |
| `ConfirmDialog.razor` | MOVE | generic confirmation prompt |
| `EmptyState.razor` | MOVE | generic empty-list illustration |
| `JsonTreeNode.razor` | MOVE | used by credential viewer |
| `JsonTreeView.razor` | MOVE | used by credential viewer |
| `JwtViewerDialog.razor` | MOVE | used by credential / presentation flows |
| `ResizableSplitter.razor` | MOVE | layout primitive, useful both sides |
| `ServiceUnavailable.razor` | MOVE | generic outage placeholder |
| `TruncatedId.razor` | MOVE | generic wallet-address truncation |

### Admin / Blueprints / Configuration / Designer / Encryption / Explorer / Registers / Templates / Workflows — STAY entirely

These are administration, blueprint authoring, register exploration, and template management. They are the desk-bound web app's reason for existing as a separate shell, and their transitive dependencies (`Z.Blazor.Diagrams`, `YamlDotNet`) are exactly the bundle weight the PWA must not inherit.

### Citizen wallet's local components — REVIEW + PARTIALLY MOVE

`Sorcha.Citizen.Wallet/Components/`:

| File | Verdict | Note |
|------|---------|------|
| `ConfirmRevokeDialog.razor` | STAY in wallet | device-revocation specific |
| `RenameDeviceDialog.razor` | STAY in wallet | device-rename specific |
| `ConsentSheet.razor` | MOVE TO LIBRARY | PWA-grown component; the web app's presentation flow should use the same consent UX |
| `CredentialPickerDialog.razor` | MOVE TO LIBRARY | same |
| `NoMatchingCredentialDialog.razor` | MOVE TO LIBRARY | same |

The three movable wallet-local components illustrate the inverse migration direction worth handling in the same change set — the wallet has been the home for some user-facing UX that should always have been shared. Moving them now establishes the boundary in both directions.

**Rationale**: Inventory was done by listing each user-facing folder and assessing each file individually. The split tracks the "field/PWA-mobile-evidence-capture vs. desk/admin/designer" framing locked in the 2026-05-10 design note. The 11→8 split inside `Shared` (8 move, 3 stay) is the only non-trivial decision; the other folders split cleanly.

**Alternatives considered**:
- *Move all of `Shared`*: rejected — `BreadcrumbNav` / `UserProfileMenu` / `LogoutConfirmDialog` are web-chrome with auth assumptions the PWA does not share. Moving them would force the PWA's host to register web-chrome services it does not need.
- *Move all of UI.Core wholesale and rely on trimming*: rejected — Blazor WASM trimming is reflection-unfriendly for Razor components (2026-05-10 spike evidence: `Z.Blazor.Diagrams` and `YamlDotNet` shipped in the PWA bundle even though no user code referenced them). Physical absence beats trimming.
- *Move just `SorchaFormRenderer`*: rejected — would still leave the PWA unable to render credential cards, consent sheets, persona panels. The user value (US1) requires the headline form renderer **and** its supporting cast.

---

## R1' — Refreshed verdicts after Feature 123 (2026-05-13)

Feature 123 reorganised `Sorcha.UI.Core` so that the audience signal lives in the folder, not in individual file inspection. Phase 2's atomic move becomes a folder-level operation:

### Services — what moves

| Folder | Verdict | Reason |
|--------|---------|--------|
| `Services/User/AddressLookup` | MOVE | user form helpers |
| `Services/User/Credentials` | MOVE | credential UX services |
| `Services/User/Forms` | MOVE | form schema + rendering services |
| `Services/User/Participants` | MOVE | participant publishing |
| `Services/User/Persona` | MOVE | persona autofill |
| `Services/User/Wallet` | MOVE | user wallet operations |
| `Services/User/*` (top-level files) | MOVE | TransactionService, WalletPreferenceService, WorkflowService, RegisterSubscriptionService, etc. |
| `Services/Shared/Authentication` | MOVE | TokenRefreshService and friends — both audiences |
| `Services/Shared/Blueprints` | MOVE | SchemaOverlayFieldInfo (F123 extraction) + schema helpers |
| `Services/Shared/Encryption` | MOVE | both audiences |
| `Services/Shared/Http` | MOVE | cross-cutting HTTP helpers |
| `Services/Shared/Identity` | MOVE | identity context |
| `Services/Shared/Navigation` | MOVE | both audiences |
| `Services/Shared/Organization` | MOVE | F123 extraction — OrganizationDto, BrandingDto |
| `Services/Admin/*` | STAY | admin / designer / configuration — UI.Core's reason for existing |

### Models — what moves

| Folder | Verdict | Reason |
|--------|---------|--------|
| `Models/User/Actions` | MOVE | action models |
| `Models/User/Authentication` | MOVE | user-side auth models |
| `Models/User/Credentials` | MOVE | credential view models |
| `Models/User/Dashboard` | MOVE | dashboard view models |
| `Models/User/Forms` | MOVE | form models |
| `Models/User/Participants` | MOVE | participant view models |
| `Models/User/Registers` | MOVE | user-facing register view models (F123 split out from mixed folder) |
| `Models/User/Wallet` | MOVE | wallet view models |
| `Models/User/Workflows` | MOVE | workflow view models |
| `Models/Shared/Authentication` | MOVE | both audiences |
| `Models/Shared/Common` | MOVE | OperationResult, paging, etc. |
| `Models/Shared/Encryption` | MOVE | both audiences |
| `Models/Admin/*` | STAY | admin / blueprints / chat / configuration / designer / explorer / registers (governance) / schema library / templates / wallet (org wallets) |

### Components — unchanged from R1

Feature 123 did not touch `Components/`. The R1 verdict per-folder still applies:

- MOVE: `Components/Forms`, `Components/Credentials`, `Components/Wallet`, `Components/Participants`, `Components/Shared` (8 of 11 files — `BreadcrumbNav`, `UserProfileMenu`, `LogoutConfirmDialog` stay).
- STAY: `Components/Admin`, `Components/Blueprints`, `Components/Configuration`, `Components/Designer`, `Components/Encryption`, `Components/Explorer`, `Components/Registers`, `Components/Templates`, `Components/Wallets`, `Components/Workflows`.

### Namespace policy — unchanged

The new csproj sets `<RootNamespace>Sorcha.UI.Core</RootNamespace>` so files moved from `Sorcha.UI.Core.*` namespaces keep their type names identical. Consumer `using` directives in the six host apps need no edits.

### Reference graph — unchanged

The Phase 2 attempt's empirical observation stands: UI.Core gains a single `ProjectReference` to Components.User; PWA gains a single direct `ProjectReference`; the six host apps remain untouched. Admin code in UI.Core that needs Shared/* types still reaches them through the UI.Core → Components.User edge.

### Type-level coupling check

The `IRegisterService` bi-modality that broke the 2026-05-11 attempt is resolved: Feature 123 split it into `IRegisterReadService` (in `Services/User/`) and `IRegisterGovernanceService` (in `Services/Admin/`). User-facing components inject the read interface; the governance interface stays in UI.Core. Same pattern applied to `OrganizationDto`/`BrandingDto` (moved to `Services/Shared/Organization/`) and `SchemaOverlayFieldInfo` (moved to `Services/Shared/Blueprints/`).

Any residual type coupling will surface as build errors against the new library; the Phase 2 attempt's wave-fix methodology (83 → 37 → 22 → 24 errors) is unnecessary because the audience folders pre-classify the surface.

---

## R2 — Host service coupling

**Question**: What host-registered services do the migrating components depend on, and which of those need to become interfaces vs. moving wholesale?

**Decision**: The coupling is much shallower than feared. A grep across all candidate Razor files for `@inject` produced eight injection points:

| Injected type | Owner | Migration disposition |
|---------------|-------|----------------------|
| `IFormSchemaService` | UI.Core/Services/Forms | MOVE — pure service, both shells need it |
| `ReviewSummaryDataSource` | UI.Core/Services/Forms | MOVE — pure service |
| `IQrPresentationService` | UI.Core/Services/Credentials | MOVE — pure service |
| `ICredentialApiService` | UI.Core/Services/Credentials | MOVE — pure service |
| `ILogger<T>` | framework | host-supplied; trivial |
| `IJSRuntime` | framework | host-supplied; trivial |
| `IServiceProvider` | framework | host-supplied; trivial |
| `HttpClient` | host-registered | both shells already register this for service-client use |

Notably absent: **no `AuthenticationStateProvider`, no `NavigationManager`, no `IAuthorizationService`, no admin-specific services** in the candidate components. The user-facing surface is auth-agnostic at the component level — auth is enforced at the page/host level above the components.

**Rationale**: A focused grep across `Forms/`, `Credentials/`, `Wallet/`, `Participants/` Razor files returned only the eight injections above. The clean surface means we don't need a new "component contract" abstraction for auth state in this feature — auth is the host's concern, not the component's.

**Alternatives considered**:
- *Introduce `IAuthenticationStateClient` / `INavigationContext` interfaces preemptively*: rejected — YAGNI. No migrating component needs them. If a future component does need them, introduce the interface at that time with a real use case.
- *Pull `HttpClient` injections behind a shared `IHttpClientFactory` abstraction*: rejected — both shells already register `HttpClient` correctly. No abstraction needed.

**Action**: Move `Services/Forms/`, `Services/Persona/`, `Services/Credentials/`, `Services/AddressLookup/` into `Sorcha.UI.Components.User/Services/`. Move `Services/Identity/` and `Services/Navigation/` **as interfaces only** (concrete impls stay per-host, since the PWA's identity/navigation context differs structurally). Move `Services/Admin/`, `Services/Designer/`, `Services/Configuration/`, `Services/Encryption/` — **stay in UI.Core**, irrelevant to user surface.

---

## R3 — Model groups that need to move

**Question**: `Sorcha.UI.Core/Models/` has 20 subfolders. Which subset must move with the user-facing components?

**Decision**: Move the model groups that the migrating components reference directly.

**Move** (user-facing display/input models):
- `Models/Actions/` — action models the form renderer consumes
- `Models/Common/` — generic UI primitives (`OperationResult`, paging models, etc.)
- `Models/Credentials/` — used by credential card / presentation components
- `Models/Forms/` — form rendering model graph
- `Models/Participants/` — participant entity UI models
- `Models/Wallet/` — wallet UI display models (tick state, receipt models)

**Stay in UI.Core** (admin/designer/explorer concerns):
- `Models/Admin/`, `Models/Blueprints/`, `Models/Chat/`, `Models/Configuration/`, `Models/Dashboard/`, `Models/Designer/`, `Models/Encryption/`, `Models/Explorer/`, `Models/Registers/`, `Models/SchemaLibrary/`, `Models/Templates/`, `Models/Workflows/`

**Move** (authentication context type if used by user-facing components):
- `Models/Authentication/` — review file-by-file in execution; move only the types referenced by migrating components, leave the rest in UI.Core.

**Rationale**: Model groups follow component groups. Each model group's home is determined by who consumes it. The principle: a migrating component must not introduce a dangling reference back to UI.Core's model surface; if it would, the model moves too.

**Alternatives considered**:
- *Move all 20 model groups*: rejected — pulls admin/designer/explorer model types into the PWA bundle for no consumer.
- *Leave all models in UI.Core, library re-references them*: rejected — creates a circular dependency once UI.Core gains a ProjectReference to the new library.

---

## R4 — Library naming and project structure

**Question**: What should the new library be called, and where does it sit physically?

**Decision**: `Sorcha.UI.Components.User`, placed at `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/`, sibling to `Sorcha.UI.Core`.

**Rationale**:
- The `Sorcha.UI.*` prefix matches the existing namespace convention.
- `Components.User` distinguishes from a future `Components.Admin` if the admin/designer/explorer surface gets its own dedicated library later.
- Sibling-to-`Sorcha.UI.Core` placement keeps the change geographically tight — both libraries are visible side-by-side in any tree view.

**Alternatives considered**:
- `Sorcha.UI.Shared` — already exists as an empty project; rejected because the name is ambiguous about which audience is "shared" with what.
- `Sorcha.UI.Components` (no qualifier) — rejected because future audience-split libraries become awkward to name retroactively.
- `Sorcha.UserExperience.Components` — rejected as needlessly verbose and breaks the `Sorcha.UI.*` convention.

---

## R5 — Dependency direction and reference graph

**Question**: How do `Sorcha.UI.Core`, the six web apps, the PWA, and the new library reference each other after the migration?

**Decision**:

```text
                    Sorcha.UI.Components.User                 (NEW)
                      ▲                ▲
                      │                │
       ┌──────────────┘                └──────────────┐
       │                                              │
  Sorcha.UI.Core                            Sorcha.Citizen.Wallet
  (admin/designer/explorer/                 (PWA)
   blueprints/configuration)
       ▲
       │ (already referenced)
       │
  Sorcha.UI.{Admin, App, Designer, Explorer, Web, Web.Client}
```

- The new library is a leaf — it depends only on `Common/Models/*` and `Sorcha.ServiceClients.Http` (which both UI.Core and the PWA already depend on).
- `Sorcha.UI.Core` adds a single ProjectReference to the new library, so its six existing consumers transparently see the moved components without any of them updating their own csproj.
- The PWA adds a single direct ProjectReference to the new library.
- `Sorcha.UI.Shared` (currently empty) is left untouched.

**Rationale**: Single new reference edge from UI.Core, single new reference edge from the PWA — total of two csproj changes for consumer wiring. All six Sorcha.UI host apps remain untouched at the csproj level (they continue to reference UI.Core, which now transitively re-exports the moved components).

**Alternatives considered**:
- *Have each of the six Sorcha.UI host apps reference the new library directly*: rejected — six unnecessary csproj edits for no behaviour change, plus a maintenance burden for any future host app added to the family.
- *Replace UI.Core's role entirely and have everyone depend only on the new library*: rejected — admin/designer/explorer components stay in UI.Core and have consumers; UI.Core remains a real project, not a vestige.

---

## R6 — Bundle hygiene verification

**Question**: How do we verify that the PWA bundle is genuinely free of admin/designer payload after the migration, beyond the spike's manual inspection?

**Decision**: Add a verification step at the end of Phase 2 that lists every assembly in the published PWA wwwroot and asserts:
1. No assembly named `Blazor.Diagrams*` is present.
2. No assembly named `YamlDotNet*` is present.
3. No assembly named `Sorcha.UI.Core*` is present.
4. The new `Sorcha.UI.Components.User` assembly IS present.
5. The pre-feature baseline assembly list and the post-feature assembly list are diffed, with every added entry traceable to a user-facing component requirement.

The verification can be a small PowerShell script committed to the repo (e.g., `scripts/check-pwa-bundle.ps1`), runnable locally and in CI.

**Rationale**: The spike already proved the PWA bundle is observable via `bin/Debug/net10.0/wwwroot/_framework/` listing. Codifying that observation as a script makes the bundle-hygiene success criterion (SC-003) automatically verifiable rather than visually inspected.

**Alternatives considered**:
- *Trust trimming to remove unwanted code*: rejected — spike evidence shows reflection-discovered Razor components and their transitive dependencies do ship even when unreferenced.
- *Manual inspection at release*: rejected — would not catch a regression introduced months after the migration.

---

## R7 — Test surface

**Question**: Where do tests for the new library live, and what existing tests need to move?

**Decision**: New project `tests/Sorcha.UI.Components.User.Tests/` with xUnit + bUnit + FluentAssertions, mirroring the test pattern in existing `tests/Sorcha.UI.Core.Tests/`. Any existing tests in `Sorcha.UI.Core.Tests` that exercise migrating components move into the new test project in the same change set.

**Rationale**: Tests follow code. `Sorcha.UI.Core.Tests` retains tests for components that stay in UI.Core (admin/designer/explorer). Splitting at this point also lets the new library hit its own ≥85% coverage target (Constitution IV) without diluting UI.Core's coverage metric.

**Alternatives considered**:
- *Leave all tests in UI.Core.Tests*: rejected — creates a circular-feeling dependency (test project for "old library" testing "new library" components) and complicates coverage reporting.

---

## R8 — Migration sequencing to keep every commit buildable

**Question**: How is the move staged so that no commit on the feature branch is in an unbuildable state? (FR-009)

**Decision**: Five-commit sequence executed on the feature branch, each commit independently buildable:

1. **Create the new project shell.** Add `Sorcha.UI.Components.User.csproj` with dependencies declared but no source files. Add the project to the solution. Builds cleanly (empty library).
2. **Move files in one atomic commit, file paths only.** Move components, services, models from `Sorcha.UI.Core` into `Sorcha.UI.Components.User`. Namespace changes preserved verbatim *within the new project* via a `RootNamespace` override matching the old namespace temporarily, OR all consumer `@using` directives updated in the same commit. The second option is preferred — explicit and grep-friendly. Add the ProjectReference from `Sorcha.UI.Core` to the new library so transitively-consuming web apps continue to find the components. Update consumer pages' `@using` lines coherently.
3. **Add PWA reference and prove integration.** Add ProjectReference from `Sorcha.Citizen.Wallet` to the new library. Render one shared component (`SorchaFormRenderer` or `IdCardLayout`) in a wallet test page to verify end-to-end. Confirm bundle hygiene with `scripts/check-pwa-bundle.ps1`.
4. **Move wallet-local user-facing components into the library.** `ConsentSheet`, `CredentialPickerDialog`, `NoMatchingCredentialDialog` move from `Sorcha.Citizen.Wallet/Components/` into `Sorcha.UI.Components.User/Components/Presentation/`. Update wallet pages' `@using` lines.
5. **Wire the developer documentation and CI check.** Add `Sorcha.UI.Components.User/README.md` documenting the boundary rule (Step Five of `quickstart.md`). Wire `scripts/check-pwa-bundle.ps1` into the relevant CI job.

**Rationale**: Each commit leaves the solution buildable and runnable. Reviewing the PR commit-by-commit gives a clear narrative: scaffold → move files → prove PWA integration → fold in inverse migration → wire CI. The atomic file-move commit (step 2) is the largest, but its scope is mechanical — git tracks renames, so the diff is reviewable.

**Alternatives considered**:
- *Single big-bang commit*: rejected — hard to review, hard to revert, hard to bisect if a regression surfaces later.
- *Move one component at a time across many commits*: rejected — would leave the codebase in flux for the duration; consumer `@using` statements would churn across every commit.

---

## R9 — Out-of-scope confirmations

The following items remain explicitly out of scope per `spec.md` and are NOT addressed by this research:

- The `IUserSigner` signing-seam contract. No signing-related interface is introduced by the migration.
- Custody-mode implementations (managed vs. self-custody). The library carries no custody-specific code.
- Co-signed multisig (MOB-009 v2 backlog).
- Visual redesign or theming changes. Components migrate visually identical.
- Renaming of `Sorcha.UI.Core` or any host app at the project level.

These confirmations are recorded so future readers can verify Phase 1/2 work has not silently expanded scope.
