# Feature Specification: Shared User-Facing UI Component Library

**Feature Branch**: `122-shared-user-components`
**Created**: 2026-05-10
**Status**: Draft
**Input**: User description: "Extract user-facing UI components from Sorcha.UI.Core into a new shared library consumed by both the Sorcha.UI web apps and the Sorcha.Citizen.Wallet PWA. Today the citizen wallet cannot reuse the credential, form, consent, persona, and participant components that already exist because UI.Core also carries admin, designer, and explorer concerns plus heavy designer-grade transitive dependencies (Blazor.Diagrams, YamlDotNet) that have no place in a mobile wallet bundle. As the PWA evolves from credential-hold-only into a full end-user agent (per the 2026-05-10 user-agent unification design note), it needs access to the same user-facing component library as the main web app without inheriting admin/designer code or transitive deps."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - PWA users gain access to the rich user-facing experience already in the web app (Priority: P1)

End users on the citizen wallet PWA (citizens, employees on the move, field workers, applicants) currently cannot interact with Sorcha forms, credential id-cards, consent sheets, persona panels, or participant pickers because those components live exclusively in the main web app. As the PWA grows beyond credential-hold-and-present into the full end-user agent role, these flows are blocked. After this feature, the PWA can render the same user-facing surface the web app already offers, with no duplicated implementation effort and no UX drift between the two shells.

**Why this priority**: This is the headline outcome that unblocks every subsequent PWA expansion (data entry, photo upload, mobile data collection, presentation, persona management). Without it, every new user flow requires either duplicating components or accepting a worse PWA experience. P1 because it is the unblocking precondition for the broader user-agent unification roadmap.

**Independent Test**: A PWA user can open a workflow action requiring a Sorcha form, see the same form-rendering experience (field types, validation, persona autofill, file uploads) they would see in the web app, complete the form, and have it submitted successfully — using only components surfaced through the shared library, not components duplicated inside the PWA.

**Acceptance Scenarios**:

1. **Given** a Sorcha workflow action with a JSON schema form, **When** the same action is rendered in the web app and in the PWA, **Then** both shells produce the same visible form layout, the same validation behaviour, and the same persona-autofill outcomes for the signed-in user.
2. **Given** a credential held by a citizen, **When** the wallet renders the credential id-card on the PWA and the same credential is opened in the web app, **Then** both renderings use the same id-card component and present identical visual content (subject to platform chrome differences).
3. **Given** a user submits a presentation request, **When** the consent sheet is shown on either shell, **Then** the same component renders the consent sheet with the same disclosed-fields summary and the same accept/decline affordances.

---

### User Story 2 - The PWA stays lean and installable (Priority: P1)

The PWA's identity as an installable, mobile-friendly, offline-first wallet depends on a compact bundle that loads quickly on a phone. If sharing components means inheriting admin/designer code paths and the libraries that support them (visual diagram editors, YAML processors, large analyser packages), the PWA degrades into a desktop-flavoured app on a phone. After this feature, the PWA references only the user-facing component subset; admin, designer, explorer, and blueprint-authoring components and their supporting libraries do not appear in the PWA bundle.

**Why this priority**: This is a non-negotiable counterpart to User Story 1. Sharing components is only valuable if sharing them does not break the PWA's mobile-app character. P1 because a "shared but bloated" outcome would make the feature a net regression, not an improvement. The earlier spike (2026-05-10) confirmed that direct project-reference of the current UI.Core drags in roughly 3.2 MB of irrelevant assembly content plus designer-grade transitive dependencies — proof that bundle hygiene is a real concern, not a theoretical one.

**Independent Test**: After the PWA consumes the shared library, the published PWA bundle is inspected and confirmed to omit (a) all admin/designer/explorer/blueprint-authoring components and (b) the third-party libraries those components depend on, including but not limited to the Blazor diagram editor and the YAML processing library.

**Acceptance Scenarios**:

1. **Given** the PWA has been built for publication, **When** the resulting bundle's assembly list is inspected, **Then** no designer-canvas library, no YAML processing library, and no admin/designer/explorer/blueprint-authoring component assemblies are present.
2. **Given** the shared library, **When** its declared dependencies are inspected, **Then** only libraries appropriate to user-facing flows (form rendering, credential display, authentication state, MudBlazor, the standard Sorcha service-client and model libraries) are present; designer-grade transitive dependencies are absent.
3. **Given** the PWA published bundle, **When** the total compressed size is compared to its pre-feature baseline, **Then** the increase is justified solely by the user-facing components newly available, not by inherited admin/designer payload.

---

### User Story 3 - The existing web app keeps working unchanged for its users (Priority: P1)

Every Sorcha.UI web app (Admin, App, Designer, Explorer, Web/Web.Client) currently consumes the components targeted for extraction. After the move, those apps must continue to render every page, complete every flow, and pass every existing test as they do today. Users of the web app see no UX change, no missing components, no behavioural regression — only the components' physical location has changed.

**Why this priority**: A migration that breaks the existing web app for the sake of enabling the PWA is unacceptable. This is the regression-safety user story. P1 because it is the gating quality bar for the work — if web users notice anything, the migration was done wrong.

**Independent Test**: The full existing Sorcha.UI test suite runs successfully against the migrated codebase with no test modifications, and a manual walkthrough of representative web flows (login, create a wallet, render a workflow form, present a credential, manage a participant) shows no visible difference from the pre-feature behaviour.

**Acceptance Scenarios**:

1. **Given** the migrated codebase, **When** the existing Sorcha.UI test suite runs, **Then** all tests that previously passed still pass, with no test code modified to accommodate the move.
2. **Given** a user signed in to the web app, **When** they perform any user-facing flow that previously used a migrated component, **Then** the flow completes identically to its pre-feature behaviour with no missing components, no styling regressions, and no script errors in the browser console.
3. **Given** the web app's blueprint designer, admin console, and explorer surfaces, **When** those flows are exercised, **Then** they continue to function unchanged, because no admin/designer/explorer components were moved.

---

### User Story 4 - Sorcha developers can extend the user surface once and see it everywhere (Priority: P2)

Today, a developer adding a new user-facing component must either (a) put it in UI.Core and accept that it cannot be used in the PWA, or (b) put it in the PWA and accept that it cannot be used in the web app, or (c) duplicate it. After this feature, a developer adding a user-facing component places it once in the shared library and both shells benefit. Duplication and drift between the two user-facing surfaces stop being a recurring cost.

**Why this priority**: This is the developer-experience and maintenance outcome that compounds over time. P2 because it is realised gradually — the immediate user-visible value is in User Story 1; this story is what keeps that value intact as the product grows. The feature must be designed so that adding a future component is obvious and low-friction; otherwise developers will duplicate by default.

**Independent Test**: A new user-facing component is added to the shared library following the documented pattern, and within the same change, both the web app and the PWA can render it without further library changes — proving the library is genuinely the natural place to put new shared work, not a one-off extraction.

**Acceptance Scenarios**:

1. **Given** a new user-facing component is added to the shared library, **When** both shells reference the unchanged shared library, **Then** the new component is available to both with no per-shell glue code beyond the consuming page.
2. **Given** a developer searches for where to place a new user-facing component, **When** they consult the library's documented scope, **Then** the answer is unambiguous: user-facing flows belong in the shared library, admin/designer/explorer flows belong in the existing web-only library.

---

### Edge Cases

- **Components with implicit service dependencies.** Some components today rely on services registered in the web app's host (authentication state, organisation context, signal-r hub clients). When such a component is consumed by the PWA, the PWA's host must register equivalent services — or the component must be redesigned so its service dependencies are declared rather than implicit. The feature must surface and address these implicit couplings rather than carrying them invisibly.
- **Styling and theming differences.** The PWA and the web app may use slightly different MudBlazor themes, layout chromes, or scoped-CSS contexts. Shared components must render correctly in both visual environments, not just the one they originated from.
- **Authentication / authorisation context differences.** The web app supports multi-org switching and several role models; the PWA today is single-citizen, single-tenant. Components that branch on auth context must either be parameterised or scoped to behaviour that works in both contexts.
- **Components that touch the wallet or the participant registry.** Some current components implicitly assume an org-scoped wallet or a multi-org participant list. The PWA has a citizen-scoped wallet model and a different participant footprint. The feature must distinguish between components that are genuinely shell-agnostic and those whose data assumptions are silently web-only.
- **Razor component reflection-discovery and trimming.** Blazor WASM publishing trims unused code, but Razor components are reflection-discovered and resist trimming. Library boundaries must be drawn so that "unused" admin/designer code is not silently shipped to the PWA on the assumption that trimming will remove it.
- **Backward-incompatible namespace or type moves.** Moving a component changes its namespace. Every existing consumer in the web app family must be updated coherently in the same change set; partial moves leave the codebase in an unbuildable state.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST provide a shared component library that the Sorcha.UI web app family (Admin, App, Designer, Explorer, Web/Web.Client) and the Sorcha.Citizen.Wallet PWA both reference and consume.
- **FR-002**: The shared library MUST contain the user-facing components currently used by both end users in the web app and intended to be used by end users in the PWA — at minimum the form-rendering family, the credential id-card family, the consent sheet, the persona panel and autofill surface, the participant picker, the presentation picker, and the file/photo upload surface.
- **FR-003**: The shared library MUST NOT contain components whose role is administration, blueprint authoring, tenant configuration, register exploration, or any function exclusive to the desk-bound web app audience.
- **FR-004**: The shared library MUST NOT introduce transitive third-party dependencies that are inappropriate for a mobile PWA bundle. Specifically, no diagram-editor library and no YAML processing library may appear in the shared library's dependency closure.
- **FR-005**: After the migration, the existing Sorcha.UI web app family MUST render every user-facing component identically to its pre-feature behaviour. All user-facing flows previously available in the web app remain available; no flow regresses.
- **FR-006**: After the migration, the Sorcha.Citizen.Wallet PWA MUST be able to instantiate and render at least one core user-facing component from the shared library, proving the integration is end-to-end functional and not merely a successful build.
- **FR-007**: The shared library MUST expose components through interfaces or component contracts that allow the PWA and the web app to satisfy implicit dependencies (authentication state, service clients, signing capability) through host-specific registrations, without the component itself assuming a particular host's services exist.
- **FR-008**: The library boundary MUST be documented such that a developer adding a new user-facing component can determine, without ambiguity, whether the new component belongs in the shared library or in the web-only library.
- **FR-009**: The migration MUST be performed coherently — every web-app reference to a moved component is updated in the same change set, leaving no consumer in an unbuildable state at any commit on the feature branch.
- **FR-010**: The PWA's published bundle MUST be verifiably free of administration, designer, explorer, and blueprint-authoring component assemblies after the integration.

### Key Entities

- **Shared User-Facing Component Library**: The new physical home for components used by end users across both shells. Holds the form-rendering surface, credential display surface, consent and presentation surface, persona surface, participant and file/photo input surfaces, and any supporting models and services those components require.
- **Web-Only Component Library**: The remainder of the current UI.Core after the user-facing subset has been extracted. Holds administration, designer, explorer, blueprint-authoring, and tenant-configuration components and their supporting dependencies.
- **Component Contract**: The declared interface (parameters, callbacks, expected host-registered services) through which a shared component communicates with its host. Allows the same component to work under different host service registrations without the component branching on host identity.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A PWA user can complete a user-facing flow (e.g., filling out and submitting a Sorcha form, viewing a credential id-card, granting presentation consent) using only components served by the shared library, with no perceived difference from completing the same flow in the web app aside from platform chrome.
- **SC-002**: 100% of the user-facing components currently used by end users in the Sorcha.UI web app family are accessible to the PWA through the shared library after the migration completes.
- **SC-003**: The Sorcha.Citizen.Wallet PWA's published bundle, after consuming the shared library, contains zero designer-canvas-library assemblies, zero YAML-processing-library assemblies, and zero administration / designer / explorer / blueprint-authoring component assemblies.
- **SC-004**: The full existing Sorcha.UI test suite passes on the migrated codebase with no test modifications attributable to the migration.
- **SC-005**: A developer adding a user-facing component for the first time after the migration can place it in the correct library on the first attempt by following the library's documented scope, with no need for retrospective relocation.
- **SC-006**: The compressed size increase in the PWA bundle after consuming the shared library is bounded by the size of the genuinely user-facing components newly available, with no inherited admin/designer payload — verified by comparing the PWA bundle's pre- and post-feature assembly listing.

## Assumptions

- The PWA and the web app family will continue to use the same component framework (MudBlazor under Blazor WebAssembly / Razor class library packaging). This feature does not introduce a new UI framework choice.
- Components currently inside `Sorcha.UI.Shared` will be audited as part of the extraction and split between the shared library and the web-only library according to the same audience principle used for `Sorcha.UI.Core`.
- The naming of the new library (working title: `Sorcha.UI.Components.User`) is an implementation choice for the planning phase; the specification does not depend on a particular name.
- Components that today rely on web-app-host services and cannot be cleanly parameterised for the PWA's host will be flagged during the migration and either parameterised or temporarily left in the web-only library for a follow-up phase, rather than blocking the broader extraction.
- The styling, theming, and layout chrome differences between the two shells will be handled at the host level (each shell wraps shared components in its own layout) rather than inside the shared components themselves.

## Out of Scope

- The signing seam contract (working title `IUserSigner`) used to abstract custody modes behind a single component interface. This is intentionally deferred to a follow-up phase that introduces signing-aware components. The current feature concerns display and input components only.
- The implementations of the custody modes themselves (self-custody on-device signing, managed-with-recovery server-side signing). These are out of scope; the shared library being delivered here neither contains nor depends on a particular custody mode.
- Co-signed dual-key (2-of-2) multisig support, captured separately as v2 backlog item MOB-009.
- Any change to the existing administration, designer, explorer, or blueprint-authoring user experience. Those components stay where they are; this feature only changes the home of user-facing components.
- Renaming, restructuring, or otherwise modifying the existing Sorcha.UI web app family or the citizen wallet PWA at the host-project level. Only the component-library layer changes.
- A new design system, theme overhaul, or component visual redesign. The migration preserves current appearance and behaviour; visual evolution is a separate concern.

## References

- **Design note**: `docs/superpowers/specs/2026-05-10-user-agent-unification-design.md` — the broader user-agent unification design that motivates this feature.
- **Spike outcome (2026-05-10)**: direct project-reference of the current `Sorcha.UI.Core` from `Sorcha.Citizen.Wallet` compiled successfully but introduced 3.26 MB of UI.Core assembly content plus designer-grade transitive dependencies (Blazor.Diagrams, YamlDotNet) into the PWA bundle — establishing that direct reuse is technically possible but architecturally inappropriate and justifying the extraction approach this specification describes.
- **Related backlog item**: `MOB-009` in `.specify/MASTER-TASKS.md` Theme 8 (v2 co-signed multisig — explicitly out of scope here).
