# Feature Specification: UI.Core User/Admin Type-Level Boundary Refactor

**Feature Branch**: `123-ui-core-boundary-split`
**Created**: 2026-05-12
**Status**: Draft
**Input**: User description: "Split bi-modal service interfaces and classify mixed-audience model folders in Sorcha.UI.Core so that a future component-library extraction (Feature 122) becomes a mechanical file move without dragging admin/governance types into a user-facing library. Required prerequisite for Feature 122. Audit must extend beyond `@inject` directives to cover method return types and parameter types — the channels that defeated Feature 122 Phase 2's first attempt."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - A future component extraction can move user-facing components without dragging admin types (Priority: P1)

When someone next attempts the Feature 122 extraction (`Sorcha.UI.Components.User` shared library), every user-facing component they move into the new library compiles standalone after the move, because every type the component transitively references is either also user-facing (and moves with it) or is in `Common/` (and was already shared). No admin or governance types come along for the ride. The new library's published bundle contains zero admin/governance code.

**Why this priority**: This is the only reason Feature 123 exists. Feature 122's Phase 2 failed precisely because this property did not hold. P1 because if Feature 123 does not deliver this property, it does not deliver anything useful — there is no other consumer for the refactor in v1.

**Independent Test**: After Feature 123 merges, a reviewer can take the Feature 122 spec, mentally walk through the file moves it prescribes, and find that every moved type's transitive closure stays inside the planned migration set. A spike build of the new library standalone (no UI.Core references, only the planned new-library content) succeeds with zero errors.

**Acceptance Scenarios**:

1. **Given** the migrated set of user-facing components, services, and models that Feature 122 plans to move, **When** the dependency closure of every type in that set is inspected, **Then** every transitively-referenced type is either inside the migrated set or inside `Common/` projects that both libraries reference.
2. **Given** the user-facing components after Feature 123, **When** a developer inspects which service interfaces those components inject, **Then** each injected interface is scoped to user-facing operations only — no admin or governance methods present on the interface.
3. **Given** the Models folders after Feature 123, **When** a developer inspects each folder, **Then** for any folder that crosses the user/admin boundary, the audience of each file is unambiguous (encoded in folder name, file name, or explicit documentation comment).

---

### User Story 2 - The Sorcha.UI web app continues to work unchanged (Priority: P1)

Splitting bi-modal interfaces and reclassifying model folders has zero user-visible impact on the existing Sorcha.UI web app family. Every flow that worked before Feature 123 — admin pages, designer, explorer, user-facing pages — works identically afterwards. The refactor is a code-organisation improvement, not a behavioural change.

**Why this priority**: A refactor that breaks the existing app is unacceptable. This is the regression-safety gate. P1 because if this fails, Feature 123 is not mergeable regardless of how clean the new boundaries look.

**Independent Test**: The full existing Sorcha.UI test suite passes with no test code modifications attributable to Feature 123. A representative manual walkthrough of web flows (login, blueprint authoring, admin org config, viewing transactions, presenting credentials, managing participants) shows no visible difference from pre-feature behaviour.

**Acceptance Scenarios**:

1. **Given** the refactored codebase, **When** the full Sorcha.UI test suite runs, **Then** every previously-passing test still passes with zero test-source-code modifications attributable to this feature.
2. **Given** a user signed into the web app, **When** they perform any flow that previously consumed a bi-modal interface (registers, organizations, wallets), **Then** the flow completes identically with no missing functionality, no UI regressions, and no console errors.
3. **Given** the admin pages and the blueprint designer, **When** those flows are exercised end-to-end, **Then** they continue to function unchanged — because the admin half of every split interface remains available to admin consumers under a new (narrower) name.

---

### User Story 3 - Future developers can identify the right interface to inject without guessing (Priority: P2)

After Feature 123, when a developer adds a new component or service consumer that needs register data, organization data, or wallet operations, the available interfaces telegraph their intended audience by name and shape. A user-facing page injects `IRegisterReadService` (or a similarly-narrow name) and gets exactly the methods user-facing pages need. An admin page injects `IRegisterGovernanceService` and gets exactly the admin methods. No developer accidentally injects a bi-modal interface and pulls in admin coupling they did not want.

**Why this priority**: This is the long-term value the refactor compounds. P2 because the immediate Feature 122 unblocking outcome (User Story 1) is what justifies the work; this is the dividend that pays off whenever the codebase grows. The refactor must leave the interface surface obviously partitioned, with a documentation pattern that future contributors recognise.

**Independent Test**: A new developer onboarding to the codebase, given the brief "I need to add a page that lists registers a user is subscribed to," picks `IRegisterReadService` on the first attempt without senior review. Same exercise for an admin-flavoured task picks the admin interface on the first attempt.

**Acceptance Scenarios**:

1. **Given** a developer searches the codebase for register-related interfaces, **When** they consult the available service interfaces, **Then** the interface names and XML doc comments unambiguously indicate which audience each one serves.
2. **Given** the codebase after Feature 123, **When** any consumer is found that injects an old bi-modal interface, **Then** zero such consumers remain — every consumer has been updated to inject the narrower interface that matches its actual usage.
3. **Given** the model folders after Feature 123, **When** a developer adds a new model type for a user-facing flow, **Then** the correct folder location is unambiguous from the existing folder names and any audience-tagging convention introduced.

---

### Edge Cases

- **Genuinely cross-audience operations.** Some operations may serve both user and admin contexts (e.g., "get my organization's name" is needed by both an admin page and a user-facing org-card render). The refactor must surface a clear convention for these — either put the operation on both interfaces, put it on the read-side interface and have the admin interface derive from or include it, or extract it to a third "shared read" interface. Picking one convention and applying it consistently is required.
- **DTOs referenced by both audiences.** Several DTOs (`OrganizationDto`, `BrandingDto`, `UserDto`) appear in both admin-service files and user-facing components. The refactor must extract these so neither audience pulls in the other's surface as a side-effect of needing the shared shape.
- **Existing tests that exercise bi-modal interfaces.** Tests that today inject `IRegisterService` and call both user-side and admin-side methods must be split, kept on one interface (whichever matches the test's intent), or rewritten to inject both narrower interfaces. The refactor cannot silently change test semantics.
- **Generated client code or external API contracts.** If any HTTP client code or generated client exposes the old interface shape externally, the rename must not break wire-level compatibility — the underlying REST endpoints stay the same. Only the C# interface partition changes.
- **Interface implementations that span both halves.** The concrete classes (`RegisterService`, `OrganizationAdminService`) today implement the whole bi-modal interface. After the split, each class either implements both narrower interfaces or is itself split into two classes. Either choice is acceptable; the choice must be made deliberately and applied consistently.
- **Hidden coupling via parameter types.** Some methods on user-side interfaces may take parameter types that today live in admin-flavoured folders (e.g., a filter type that originated in an admin context but is now used user-side). The audit must catch these and either move the parameter type to a shared location or refactor the method signature.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Every service interface in `Sorcha.UI.Core/Services/` MUST be classified as user-facing, admin-facing, or genuinely cross-audience. The classification MUST be recorded so it is later auditable.
- **FR-002**: For every interface classified as bi-modal, the interface MUST be split into narrower interfaces whose methods serve a single audience each.
- **FR-003**: Every consumer in the Sorcha.UI host apps (Admin, App, Designer, Explorer, Web, Web.Client) that today injects a bi-modal interface MUST be updated to inject the narrower interface that matches its actual usage. After the refactor, zero consumers inject the old bi-modal interface.
- **FR-004**: Every DTO that is referenced by both user-facing and admin code MUST be extracted out of admin-service files into a location where user-facing components can reference it without inheriting the admin service surface.
- **FR-005**: Every model folder in `Sorcha.UI.Core/Models/` MUST be classified per audience. For folders that mix audiences, the contents MUST be split (by sub-folder, by file rename, by audience-tagging comment, or by some other unambiguous mechanism) so that each file's audience is identifiable without reading its content.
- **FR-006**: After the refactor, the existing Sorcha.UI web app family MUST render and behave identically to its pre-feature behaviour. No user-visible regressions; no missing functionality; no styling drift; no script errors in the browser console.
- **FR-007**: After the refactor, the full existing Sorcha.UI test suite MUST pass with zero test-source-code modifications attributable to this feature. Tests that today exercise bi-modal interfaces MUST be updated only where the bi-modal interface itself no longer exists — and even then, the test's behavioural intent MUST remain identical.
- **FR-008**: The audit performed during this refactor MUST extend beyond `@inject` directive scanning to cover method return types, method parameter types, and transitive type closures. The audit's findings MUST be recorded in a per-interface or per-folder verdict table so the next attempt at Feature 122 can verify its migration set without re-doing the analysis.
- **FR-009**: The refactor MUST NOT change the REST endpoints exposed by underlying services, the wire format of HTTP requests/responses, or any external contract. Only the C# interface partition inside Sorcha.UI.Core changes.
- **FR-010**: The audience classification convention (whether by interface naming, folder naming, attribute tagging, or documentation comment) MUST be applied consistently across the codebase and documented in a single location that a future contributor can discover. New contributors writing the next user-facing service or model file MUST be able to choose the right location without senior-developer review on first attempt.

### Key Entities

- **Bi-modal interface**: A service interface in `Sorcha.UI.Core/Services/` whose methods serve more than one audience. After this refactor, no bi-modal interfaces exist in production; every interface is single-audience.
- **Mixed model folder**: A folder under `Sorcha.UI.Core/Models/` whose files include types for more than one audience. After this refactor, every model folder is unambiguous (single-audience, or sub-folders / file-naming convention encode audience).
- **Shared DTO**: A data-transfer-object type that is used by both user-facing and admin consumers. After this refactor, shared DTOs live in a location that does not force consumers to inherit a particular service surface.
- **Audience tag**: The mechanism (folder name, sub-folder, file name, attribute, doc comment) by which the audience of a type is declared in the codebase post-refactor. The mechanism is chosen during this feature and applied consistently.
- **Type-level closure**: The set of all types reachable from a given type via field types, property types, method return types, method parameter types, and generic arguments. The Feature 122 Phase 2 attempt failed because this closure was not analysed for the migrating components. Feature 123 audits the closure and ensures it stops at the user/admin boundary.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: After Feature 123 merges, when a developer attempts the Feature 122 extraction by moving the planned set of user-facing components, services, and models into a new library project, the new library's standalone build succeeds with zero errors — meaning no type in the moved set transitively references a type that stayed in `Sorcha.UI.Core` (excluding `Common/` types both libraries already share). Demonstrable via a build-verification spike before the migration commits.
- **SC-002**: Zero consumers in the Sorcha.UI host app family inject a bi-modal interface after this feature. Verifiable by repository-wide search for the old interface names — every match either is a definition (in the marker-interface case) or has been updated to inject the narrower interface.
- **SC-003**: Every model folder under `Sorcha.UI.Core/Models/` has a verdict in the documented audit (recorded in this feature's research artefacts and reflected in the codebase via the chosen audience-tag convention).
- **SC-004**: The full Sorcha.UI test suite passes on the refactored codebase with zero test-source-code changes attributable to this feature. (Test changes attributable to bi-modal-interface tests being split or updated are acceptable, but the test's behavioural assertions remain identical.)
- **SC-005**: A new contributor opening the codebase for the first time can locate the correct interface or model location for a new user-facing register operation on the first attempt, by reading the audience-tag convention documentation alone.
- **SC-006**: The audit's per-interface and per-folder verdicts are committed to the repository alongside the code changes, so a future revisit (e.g., when Feature 122 resumes) can verify its migration set against the audit without redoing the analysis.

## Assumptions

- The existing `Sorcha.UI.Core` project structure (`Components/`, `Services/`, `Models/`, `Extensions/`) stays intact at the top level. Only contents are reorganised; the project itself is not renamed or removed.
- The six Sorcha.UI host apps (Admin, App, Designer, Explorer, Web, Web.Client) continue to reference `Sorcha.UI.Core` as today. No host-csproj changes are required by Feature 123.
- The Sorcha.Citizen.Wallet PWA is not touched by Feature 123. It continues to live with its current local components; Feature 122 is what eventually wires it to a shared library.
- The REST endpoints exposed by services (Tenant, Register, Wallet, Blueprint, etc.) are not changed by Feature 123. Only the client-side C# interface partition changes.
- Interface naming follows the convention currently in use (`I<Noun>Service` / `I<Noun><Modifier>Service`). New narrower interfaces use this same shape (e.g., `IRegisterReadService`, `IRegisterGovernanceService`).
- The audience-tag convention chosen during this feature (folder split vs. file naming vs. attribute) is a design choice for the plan phase. The spec does not lock a particular mechanism; it only requires that one is chosen and applied consistently.
- Tests that today inject bi-modal interfaces and exercise both halves are rare. Where they exist, splitting them is part of this feature's scope.

## Out of Scope

- The actual extraction of `Sorcha.UI.Components.User` from `Sorcha.UI.Core`. That is Feature 122's job. Feature 123 leaves UI.Core in a state where Feature 122's extraction is mechanical, but does not perform the extraction itself.
- Renaming or splitting `Sorcha.UI.Core` itself. The project keeps its name and its top-level structure.
- Changing the REST endpoints, gRPC services, or any external contract.
- Reworking the Sorcha.Citizen.Wallet PWA.
- Introducing the `IUserSigner` seam, custody-mode implementations, or co-signed multisig (those are MOB-009 and other deferred items).
- Visual or UX changes in any host app. The refactor is purely code-organisational.
- New telemetry, new auth, new persistence — none of these are part of this feature.

## References

- **Discovery rationale**: `specs/122-shared-user-components/phase-2-discovery.md` — the forensic narrative of Feature 122's Phase 2 attempt, the architectural finding that motivated this feature, and the concrete first-target list that this spec generalises into FRs and SCs.
- **Dependent feature**: `specs/122-shared-user-components/` — Feature 122 is blocked on Feature 123 merging. After Feature 123 merges, Feature 122's Phase 0 research is re-run with the extended methodology (FR-008) and Phase 2 onwards resumes.
- **Methodology lesson**: the Feature 122 Phase 0 research used `@inject`-grep, which is necessary but insufficient. Feature 123's audit MUST also inspect method return types, parameter types, and transitive type closures. This requirement is captured as FR-008.
