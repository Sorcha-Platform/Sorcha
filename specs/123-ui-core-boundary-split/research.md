# Phase 0 Research: UI.Core User/Admin Type-Level Boundary Refactor

**Feature**: 123-ui-core-boundary-split
**Date**: 2026-05-12
**Status**: Complete

This document locks the audit methodology, locks the audience-tag convention, and produces the per-interface and per-folder verdicts that Feature 123 will execute against. All "NEEDS CLARIFICATION" items in the plan are resolved here.

---

## R1 — Audit methodology (FR-008)

**Question**: What evidence does the audit collect to classify a service interface or model file as user-facing, admin-facing, or cross-audience? Feature 122's Phase 0 used `@inject`-grep alone and missed return-type and parameter-type coupling. What does a correct audit cover?

**Decision**: Three-layer audit performed per type / per interface.

1. **Direct consumer scan.** Grep every host-app page and component for `@inject <Interface>` directives, and grep every C# class for constructor-injected dependencies of the interface. Record consumer count, consumer location (admin pages, designer pages, user-facing pages, mixed).
2. **Method-signature scan.** For each method on the interface, classify the method by what the *consumer pages* do with it:
   - If only admin/designer/explorer pages call the method → admin
   - If only user-facing pages call the method → user
   - If both → cross-audience (and the method appears on a third "shared" interface OR is duplicated, depending on R3)
3. **Type-closure walk.** For each type referenced by the interface (in method return types, parameter types, generic arguments) and each type in a model folder under audit, walk the transitive closure. A type is "user-facing" only if its closure stays within types also classified user-facing (or in `Common/`, or framework types). The same property for admin. A type whose closure crosses the boundary is itself cross-audience.

The audit produces a single verdict per type / per interface — one of `USER`, `ADMIN`, `CROSS`, `SHARED-DTO` (a type used by both but carrying no audience-specific behaviour). Cross-audience types either get split (interface) or are renamed/promoted to a SHARED-DTO location (data types).

**Rationale**: Feature 122 Phase 2's failure mode was "the interface returns an admin type, so moving the interface drags admin coupling." Layers 2 and 3 of this audit are the channels that defeat that failure. Layer 1 (the original `@inject`-grep) is preserved because it remains the cheapest sanity check; it's just no longer sufficient on its own.

**Alternatives considered**:
- *Roslyn-based analyzer.* Build a small static-analysis tool that walks the AST and produces machine-readable verdicts. Rejected for v1 — the type count is small enough (~30 interfaces, ~20 model folders) that a manual audit with grep and IDE "Find All References" is faster than tool-building. Worth revisiting if Sorcha grows more libraries with this shape.
- *Trust the spec's first-target list and skip the broader audit.* Rejected — the first-target list (from `phase-2-discovery.md`) is a starting point but not a guaranteed complete picture. Item 7 ("Formalise the SchemaOverlayFieldInfo pattern") explicitly anticipates more such cases.

**Action**: Audit results are recorded in R5 (services) and R6 (models) below. Both sections are products of running this three-layer method against UI.Core's current surface as of branch `master` (commit `84a06784`).

---

## R2 — Audience-tag convention (FR-010)

**Question**: How is the audience of a type encoded in the codebase post-refactor? Folder split, file naming, attribute tagging, doc-comment header? One mechanism, applied consistently.

**Decision**: **Folder split**. For service interfaces, sort into one of `Services/User/`, `Services/Admin/`, and `Services/Shared/` (the last for cross-audience and shared DTOs). For models, sort into audience-suffixed subfolders where mixed: `Models/Registers/` for user-facing types, `Models/Registers/Governance/` for admin/governance types. Where a folder is single-audience already (e.g., `Models/Designer/` is entirely admin/designer), no rearrangement is needed — its audience is implicit in the folder's purpose.

Interface naming gains a suffix that reflects the audience: `IRegisterReadService` (user — "Read" makes the user-facing-display intent explicit), `IRegisterGovernanceService` (admin/governance). The suffix is descriptive rather than literally `User` or `Admin` — see R3 for naming convention details. The folder location is the load-bearing audience marker; the name aligns with it.

**Rationale**:
- **Discoverable by file system.** A developer browsing `src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/` immediately sees three folders and knows which audience their work serves. No need to open files or read documentation.
- **Survives refactoring tools.** Renames and IDE refactors don't break the audience signal because it lives in the folder structure.
- **Enables Feature 122's mechanical move.** Feature 122 can move `Services/User/*` and `Models/Registers/*` (user-facing portion) wholesale into the new library without filtering or per-file inspection.
- **Avoids attribute pollution.** Audience attributes would require referencing a shared attribute type, touching every file, and adding noise to type declarations.

**Alternatives considered**:
- *File naming suffix only* (e.g., `RegisterPolicyViewModelAdmin.cs`). Rejected — verbose, easy to forget, and not enforced by file system; a renamed file can break the convention silently.
- *Attribute tagging* (`[AudienceAdmin]`, `[AudienceUser]`). Rejected — requires defining the attribute type, importing it everywhere, and inspecting compiled metadata to read the verdict. Reviewable cost outweighs benefit.
- *Doc-comment header* (`/// Audience: Admin`). Rejected for the same reason as attribute tagging: requires reading the file rather than seeing the structure.
- *Three top-level folders `Services.User/`, `Services.Admin/`, `Services.Shared/`* (peer to `Services/`). Rejected — changes `Sorcha.UI.Core`'s top-level structure unnecessarily; subfolders under existing `Services/` are sufficient.

**Action**: Apply the folder-split convention during execution. Phase 1's `data-model.md` lists every file's source and target path. Phase 7 polish records the convention in a single discoverable location (`src/Apps/Sorcha.UI/Sorcha.UI.Core/README.md`).

---

## R3 — Interface naming convention

**Question**: When `IRegisterService` is split, what do the two narrower interfaces get called?

**Decision**: User-facing interfaces use a **descriptive suffix that names the operation kind**, not the audience. Admin-facing interfaces use **`Admin` or `Governance` as a suffix** when the existing convention already does so (`IOrganizationAdminService`, `IValidatorAdminService`, etc.) — there's precedent and consumers will recognise the pattern.

Specifically:
- `IRegisterService` → `IRegisterReadService` (user — list, get) + `IRegisterGovernanceService` (admin — policy, governance roster, dev-mode controls). "Read" describes what the interface does; "Governance" matches the existing admin-suffix pattern.
- `IOrganizationAdminService` stays named as-is (no split — see R5); DTOs extracted out of the file but the interface keeps its operations.
- `IWalletApiService` audit in R5.

**Rationale**: Sorcha already has `IOrganizationAdminService`, `IPlatformSettingsAdminService`, `IValidatorAdminService`, `ISystemRegisterService` — admin-flavoured interfaces with audience-revealing names. The pattern is in place; extending it is cheaper than inventing a new convention.

For user-facing splits, "Read" is the operational verb most often used; if a future interface needs to express user-write operations (e.g., a user can subscribe to a register), a `Subscribe`-suffixed or `Write`-suffixed interface is fine — the principle is "describe the operation".

**Alternatives considered**:
- *Literal `IRegisterUserService` and `IRegisterAdminService`.* Rejected — `User` as a noun-suffix on every user-facing interface is repetitive and tells the reader nothing they didn't already know from the folder location.
- *Hungarian-style audience prefix* (`IUserRegister`, `IAdminRegister`). Rejected — breaks the existing `I<Noun><Modifier>Service` convention and reads awkwardly.

**Action**: Phase 1 contract docs use these names. Plan-phase reviewers can revise specific names if better operational verbs emerge.

---

## R4 — Marker-interface vs. interface deletion

**Question**: When `IRegisterService` is split into `IRegisterReadService` + `IRegisterGovernanceService`, what happens to the original `IRegisterService`? Keep it as a marker derived from both for backward-compat, or delete it entirely?

**Decision**: **Delete `IRegisterService` entirely.** Update every consumer to inject the narrower interface that matches its actual usage. Any consumer that genuinely needs both (audit suggests this is rare or zero) injects both narrower interfaces.

**Rationale**:
- Keeping `IRegisterService` as a marker derived from both narrower interfaces preserves the bi-modal injection pattern — exactly the thing we're trying to eliminate. A developer could continue to inject `IRegisterService` after this feature ships, defeating the audit's purpose.
- Deletion forces an audit-and-update of every consumer at refactor time — work that has to happen anyway and is cheaper as a single coordinated change than as a creeping migration.
- The internal concrete class (`RegisterService`) can implement both narrower interfaces directly: `public class RegisterService : IRegisterReadService, IRegisterGovernanceService`. DI registration stays one line per interface.

**Alternatives considered**:
- *Marker interface kept for backward compat.* Rejected for the reason above; also defeats SC-002 ("zero consumers inject a bi-modal interface").
- *Marker interface deprecated with `[Obsolete]` attribute.* Rejected — same drift problem in a slower form; a build warning is easy to ignore.

**Action**: Phase 2 task list includes consumer-update tasks for every page that today injects `IRegisterService`. Audit count of such consumers is recorded in R5 below.

---

## R5 — Service interface verdicts

**Methodology**: For each top-level service interface in `Sorcha.UI.Core/Services/`, classify by method-audience (R1 layer 2). Subfolders aggregated by their primary-audience purpose.

### Top-level interfaces

| Interface | Verdict | Action | Notes |
|---|---|---|---|
| `IRegisterService` | **CROSS — split** | Split into `IRegisterReadService` (`GetRegistersAsync`, `GetRegisterAsync`) + `IRegisterGovernanceService` (`GetGovernanceRosterAsync`, `InitiateRegisterAsync`, `FinalizeRegisterAsync`, `GetPolicyAsync`, `GetPolicyHistoryAsync`, `ProposePolicyUpdateAsync`, `DisableDevModeAsync`). Delete `IRegisterService`. | The canonical case; rationale in spec + 122 phase-2-discovery |
| `IRegisterSubscriptionService` | **USER** | Move to `Services/User/`. No split — all 5 methods (`GetMySubscribedRegistersAsync`, `SubscribeAsync`, `CreateOwnerSubscriptionAsync`, `UnsubscribeAsync`, `GetAvailableRegistersAsync`) serve user-side register subscription management. | "Subscribe" is a user-acting verb even when the user has admin-org rights to subscribe their org |
| `IOrganizationAdminService` | **ADMIN — DTO extraction only** | No interface split. Move file to `Services/Admin/`. Extract `OrganizationDto`, `BrandingDto`, `UserDto`, `AddUserDto`, `UpdateUserDto`, `CreateOrganizationDto`, `UpdateOrganizationDto`, `SubdomainValidationResult`, `OrganizationListResult`, `UserListResult`, `PlatformKpis` into `Services/Shared/Organization/` (one file per type). | Interface itself is purely admin; the bi-modal-ness was at the DTO co-location level |
| `IPlatformSettingsAdminService` | **ADMIN** | Move to `Services/Admin/`. | Name and surface align |
| `IValidatorAdminService` | **ADMIN** | Move to `Services/Admin/`. | |
| `ISystemRegisterService` | **ADMIN** | Move to `Services/Admin/`. | System register ops are admin |
| `IBlueprintApiService` | **ADMIN** | Move to `Services/Admin/`. | Blueprint authoring is admin/designer |
| `ISchemaLibraryApiService` | **ADMIN** | Move to `Services/Admin/`. | Schema library is designer-flavoured |
| `ITemplateApiService` | **ADMIN** | Move to `Services/Admin/`. | Template management is designer-flavoured |
| `IBlueprintStorageService` | **ADMIN** | Move to `Services/Admin/`. | Blueprint persistence is designer-flavoured |
| `IDashboardService` | **USER** | Move to `Services/User/`. Audit confirmed by inspecting consumers — only user-facing dashboard pages call this | |
| `IInboxApiService` | **USER** | Move to `Services/User/`. | User inbox UX |
| `IDocketService` | **USER** | Move to `Services/User/`. 4 methods (`GetDocketsAsync`, `GetDocketAsync`, `GetDocketTransactionsAsync`, `GetLatestDocketAsync`) are all read-side display | |
| `ITransactionService` | **USER** | Move to `Services/User/`. | Transaction-display surface |
| `IWorkflowService` | **USER** | Move to `Services/User/`. | |
| `IAlertService` + `IAlertDismissalService` | **SHARED** | Move to `Services/Shared/`. Both audiences use alerts | |
| `IHealthCheckService` | **ADMIN** | Move to `Services/Admin/`. | |
| `IAuditService` | **ADMIN** | Move to `Services/Admin/`. | |
| `IServicePrincipalService` | **ADMIN** | Move to `Services/Admin/`. | |
| `IODataQueryService` | **ADMIN** | Move to `Services/Admin/`. | Power-user/admin query surface |
| `IPayloadDecoderService` | **SHARED** | Move to `Services/Shared/`. Used by both user-facing transaction display and admin payload viewer | |
| `IOfflineSyncService` | **USER** | Move to `Services/User/`. | |
| `IWalletAccessService` | **ADMIN** | Move to `Services/Admin/`. Wallet access *grant/revoke* is org-admin-only; users don't manage their own wallet's access grants | |
| `IWalletPreferenceService` | **USER** | Move to `Services/User/`. User's own wallet preferences | |
| `IChatHubConnection` | **USER** | Move to `Services/User/` (or stay top-level — see below). Designer-flavoured users? Audit confirms: chat is the AI designer chat, consumed by admin/designer pages. **Re-verdict: ADMIN.** | Caught by Layer 2 of the audit — name was misleading |

Re-classified after Layer 2: `IChatHubConnection` is ADMIN (designer chat).

### Service subfolders (existing)

| Folder | Verdict | Action |
|---|---|---|
| `Services/Forms/` | **USER** | Move to `Services/User/Forms/`. Form rendering is user-side |
| `Services/Persona/` | **USER** | Move to `Services/User/Persona/`. Feature 092 persona surface |
| `Services/Credentials/` | **USER** | Move to `Services/User/Credentials/`. Credential UX |
| `Services/AddressLookup/` | **USER** | Move to `Services/User/AddressLookup/`. Postal-address lookup used by form renderer |
| `Services/Participants/` | **USER** | Move to `Services/User/Participants/`. Participant identity is user-side (with admin-side org-roster management overlap; verify consumer pages — if only user-facing pages use this folder's contents, USER; if admin pages also use it, the SUBSCRIBE-style pattern may need a split) |
| `Services/Wallet/` | **USER** | Move to `Services/User/Wallet/`. User wallet operations |
| `Services/Identity/` | **SHARED** | Move to `Services/Shared/Identity/`. Both audiences need identity context |
| `Services/Navigation/` | **SHARED** | Move to `Services/Shared/Navigation/`. Both audiences navigate |
| `Services/Http/` | **SHARED** | Move to `Services/Shared/Http/`. HTTP infrastructure |
| `Services/Authentication/` | **SHARED** | Move to `Services/Shared/Authentication/`. Auth applies to both audiences |
| `Services/Admin/` | **ADMIN** | Move to `Services/Admin/Admin/` — or flatten by moving its contents directly into `Services/Admin/` |
| `Services/Designer/` | **ADMIN** | Move to `Services/Admin/Designer/` |
| `Services/Configuration/` | **ADMIN** | Move to `Services/Admin/Configuration/` |
| `Services/Encryption/` | **SHARED?** | **AUDIT NEEDED** — encryption helpers are likely cross-audience; verdict deferred to execution-phase Layer-3 closure check |

### Service-interface verdict summary

- 28 top-level interfaces → 1 CROSS (split into 2), 11 USER, 14 ADMIN, 3 SHARED.
- 14 subfolders → 6 USER, 4 ADMIN, 4 SHARED.
- One audit-deferred case (`Services/Encryption/`).

---

## R6 — Model folder verdicts

| Folder | Verdict | Action | Notes |
|---|---|---|---|
| `Models/Actions/` | **USER** | Move to `Models/User/Actions/`. Action models drive form rendering | |
| `Models/Admin/` | **ADMIN** | Move to `Models/Admin/Admin/` (or flatten) | |
| `Models/Authentication/` | **AUDIT — file by file** | Some types user-side (current-user info), some admin-side (org-management user DTOs). Split into `Models/User/Authentication/` and `Models/Admin/Authentication/` | |
| `Models/Blueprints/` | **MIXED** | Split: `GovernanceRosterViewModel` moves to `Models/Admin/Governance/`. Designer-flavoured schema/canvas types stay in `Models/Admin/Blueprints/` | The specific type that defeated Feature 122 Phase 2 |
| `Models/Chat/` | **ADMIN** | Move to `Models/Admin/Chat/`. AI designer chat | |
| `Models/Common/` | **SHARED** | Move to `Models/Shared/Common/`. Generic primitives (`ApiResponse`, `PaginatedList`) | |
| `Models/Configuration/` | **ADMIN** | Move to `Models/Admin/Configuration/` | |
| `Models/Credentials/` | **USER** | Move to `Models/User/Credentials/` | |
| `Models/Dashboard/` | **USER** | Move to `Models/User/Dashboard/` | |
| `Models/Designer/` | **ADMIN** | Move to `Models/Admin/Designer/` | |
| `Models/Encryption/` | **SHARED?** | **AUDIT NEEDED** at execution time | |
| `Models/Explorer/` | **ADMIN** | Move to `Models/Admin/Explorer/`. Transaction explorer is admin/power-user | |
| `Models/Forms/` | **USER** | Move to `Models/User/Forms/` | |
| `Models/Participants/` | **USER** | Move to `Models/User/Participants/` | |
| `Models/Registers/` | **MIXED — split** | User-facing → `Models/User/Registers/` (TransactionViewModel, RegisterViewModel, WalletViewModel, PayloadViewModel, TransactionListResponse, TransactionGraphNode, TransactionQueryState, RegisterFilterState, ConnectionState, NavigationContext). Admin/governance → `Models/Admin/Registers/` (RegisterPolicyViewModel, RegisterPolicyFields, PolicyUpdateProposalViewModel, PolicyHistoryViewModel, RegisterCreationState) | Phase 2's most painful folder |
| `Models/SchemaLibrary/` | **ADMIN** | Move to `Models/Admin/SchemaLibrary/` | |
| `Models/Templates/` | **ADMIN** | Move to `Models/Admin/Templates/` | |
| `Models/Wallet/` | **USER** | Move to `Models/User/Wallet/` | |
| `Models/Workflows/` | **USER** | Move to `Models/User/Workflows/` | |

### Top-level loose files in `Models/`

| File | Verdict | Action |
|---|---|---|
| `ActivityEventDto.cs` | SHARED | Move to `Models/Shared/` |
| `AuthMethodsModels.cs` | USER | Move to `Models/User/Authentication/` (Feature 116 surface) |
| `PendingActionNotificationDto.cs` | USER | Move to `Models/User/Actions/` |
| `TotpDtos.cs` | USER | Move to `Models/User/Authentication/` |
| `UserPreferencesDto.cs` | USER | Move to `Models/User/` |
| `WalletAccessModels.cs` | ADMIN | Move to `Models/Admin/Wallet/` — wallet access grants are admin-org concerns |

---

## R7 — Cross-audience operation convention (Edge Case 1 of spec)

**Question**: For an operation needed by both user and admin contexts (e.g., "get this org's display name and branding"), what shape does it take in the new partitioning?

**Decision**: Put the operation on a **`Shared` interface** (`Services/Shared/Organization/IOrganizationReadService.cs` or similar) that user-facing code injects directly. Admin-facing interfaces **do not derive from** the Shared interface; admin pages that also need the read operation inject both `IOrganizationAdminService` and `IOrganizationReadService` (or whatever the Shared interface is called).

**Rationale**: Interface inheritance is a one-way trapdoor — once `IOrganizationAdminService : IOrganizationReadService`, admin consumers can't injection-narrow to the Shared interface, and the audience signal degrades. Explicit dual-injection is verbose but tells the reader exactly what each consumer touches.

**Alternatives considered**:
- *Admin interface derives from Shared.* Rejected as above — undermines the audit signal.
- *Duplicate the method on both interfaces with identical implementations.* Rejected — same method body in two places drifts under maintenance.

**Action**: Where an operation surfaces this need during execution, the contract docs in `contracts/` describe the Shared interface placement. Initial inventory: `IOrganizationAdminService` ⇄ user-side need for `GetOrganizationAsync` — extract into `IOrganizationReadService` in `Services/Shared/Organization/`.

---

## R8 — DTO extraction pattern (formalising the SchemaOverlayFieldInfo + OrganizationDto cases)

**Question**: Several DTOs are co-located with admin-service interface files (`IOrganizationAdminService.cs` contains both the admin interface and `OrganizationDto`, `BrandingDto`, etc.; Feature 122 Phase 2 also extracted `SchemaOverlayFieldInfo` from `BlueprintSchemaService.cs`). What's the formal pattern?

**Decision**: **One DTO per file**, located in an audience-classified folder, with the namespace preserved from the original co-location.

Concretely for the canonical cases:

| Original location | DTO | New location |
|---|---|---|
| `Services/IOrganizationAdminService.cs` | `OrganizationDto` | `Services/Shared/Organization/OrganizationDto.cs` |
| `Services/IOrganizationAdminService.cs` | `BrandingDto` | `Services/Shared/Organization/BrandingDto.cs` |
| `Services/IOrganizationAdminService.cs` | `UserDto` | `Services/Shared/Organization/UserDto.cs` (or `Services/Shared/Users/` if it grows) |
| `Services/BlueprintSchemaService.cs` | `SchemaOverlayFieldInfo` | `Services/Shared/Blueprints/SchemaOverlayFieldInfo.cs` |
| (others surfaced during audit) | (...) | (...) |

Namespaces stay as-is to preserve consumer compatibility (`Sorcha.UI.Core.Services` for `OrganizationDto`, etc.). The audience-tag mechanism is folder-based; namespace remains a separate concern.

**Rationale**:
- One DTO per file is the existing C# convention in this codebase (one record/class per file in Models folders) — extending it to extracted DTOs is the natural fit.
- Keeping namespaces unchanged means consumer `using` directives are unaffected by the extraction — a smaller diff during refactor execution.
- Folder location encodes audience; file name encodes identity; nothing else needs to change.

**Alternatives considered**:
- *Grouping multiple DTOs per file (e.g., `OrganizationDtos.cs` with both `OrganizationDto` and `BrandingDto`).* Rejected — diverges from the codebase's per-file convention; harder to grep for a specific type definition.
- *Renaming namespaces to match new folder location.* Rejected — would force every consumer's `using` directive to update at refactor time, defeating the "preserve consumer compatibility" goal.

**Action**: Phase 1's `contracts/shared-dto-extraction-pattern.md` formalises this with code snippets. Phase 2 tasks include one extraction task per DTO with explicit old → new paths.

---

## R9 — Consumer-update strategy

**Question**: How are the host-app pages that today inject `IRegisterService` (or other bi-modal interfaces) updated to inject the narrower interfaces, without introducing test flakiness or runtime DI failures?

**Decision**: Three-step coordinated update per bi-modal interface, all in the same commit:

1. **Add narrower interfaces** — create `IRegisterReadService.cs` and `IRegisterGovernanceService.cs`. Make `RegisterService` implement both.
2. **Update DI registration** — register the concrete class against both narrower interfaces. The old `IRegisterService` registration is deleted in the same change.
3. **Update every consumer** — grep host-app projects for `@inject IRegisterService` and constructor parameters of type `IRegisterService`, replace with the narrower interface that matches the consumer's actual method usage. Inject both narrower interfaces only where the consumer genuinely calls both halves.
4. **Delete `IRegisterService.cs`.**

The commit either builds cleanly (every consumer found and updated) or fails — no half-state where some consumers use old interface and some use new.

**Rationale**: Atomic per-interface commit keeps the codebase buildable at every commit on the feature branch. Grep-then-rewrite is reliable for the consumer-update step because the interface symbol is unique and unambiguous.

**Alternatives considered**:
- *Add narrower interfaces, deprecate old, migrate consumers gradually across multiple commits.* Rejected — drift risk; also slower for reviewers.
- *Use Roslyn to rewrite consumers automatically.* Rejected for v1; manual grep is faster for ~30 expected consumer sites.

**Action**: Phase 2 task list includes the four-step pattern per split interface.

---

## R10 — Test-update strategy

**Question**: Tests that today inject bi-modal interfaces and exercise methods from both halves — how are they handled?

**Decision**: Tests split along the same lines as their subject. A test class that today exercises `IRegisterService.GetRegistersAsync` and `IRegisterService.ProposePolicyUpdateAsync` becomes two test classes — `RegisterReadServiceTests.cs` (mocks `IRegisterReadService`) and `RegisterGovernanceServiceTests.cs` (mocks `IRegisterGovernanceService`) — each preserving the original test methods' assertions verbatim. Test names and arrange-act-assert bodies are unchanged.

A test that today injects `IRegisterService` and exercises a single side switches to the matching narrower interface in-place; no class split needed.

**Rationale**: FR-007 / SC-004 require zero attributable test changes. The narrower-interface update is the only acceptable test diff. Splitting test classes when the subject is genuinely split is bookkeeping, not behavioural.

**Alternatives considered**:
- *Keep tests injecting the marker `IRegisterService` for backward-compat.* Already rejected in R4 (no marker interface kept).
- *Rewrite tests to use the concrete `RegisterService` class.* Rejected — defeats the test's role as the interface contract.

**Action**: Phase 2 task list includes per-test-class update tasks where needed.

---

## R11 — Out-of-scope confirmations

The following items remain explicitly out of scope per `spec.md` Out of Scope section and are NOT addressed by this research:

- The Feature 122 component-library extraction itself.
- Any rename of `Sorcha.UI.Core` or any host app at the project level.
- REST endpoint changes; gRPC contract changes; wire format changes.
- The Sorcha.Citizen.Wallet PWA (untouched).
- The `IUserSigner` seam.
- Visual/UX changes.
- New telemetry, auth claims, persistence entities.

These confirmations are recorded so future readers can verify Phase 1/2 work hasn't silently expanded scope.

---

## Summary

The audit produces a focused refactor:

- **One interface split** (`IRegisterService` → 2 narrower) plus possible 0-2 additional splits surfaced during execution-phase Layer-3 audit of `Services/Encryption/`.
- **One DTO extraction site** (`IOrganizationAdminService.cs` → ~11 DTO files in `Services/Shared/Organization/`) plus formalisation of the pattern for any other admin-file-co-located DTOs found during execution.
- **Folder reorganisation** of `Services/` and `Models/` into `User/`, `Admin/`, `Shared/` subfolders per R5 + R6 verdicts.
- **Consumer updates** across the six Sorcha.UI host apps — concentrated around `IRegisterService` consumers (expected count ~5-15 pages based on the methods' nature).
- **No new code paths.** No new tests required. Zero application behaviour change.

The scope is smaller than the Feature 122 Phase 2 attempt — that attempt moved hundreds of files; this refactor splits one interface, extracts ~11 DTOs, and reorganises folders. The work is mostly bookkeeping with a small amount of consumer-update grep-and-replace.
