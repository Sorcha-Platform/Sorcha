# Feature Specification: Shared verify components — question panel, session QR, verdict trail (PR B2-components, relaunch)

**Feature Branch**: `163-verify-shared-components`

**Created**: 2026-06-25 (relaunched 2026-06-26)

**Status**: Draft

**Parent design**: `docs/superpowers/specs/2026-06-25-verify-unification-design.md` (stage B2)

**Builds on**: PR B2-foundation (#1045 — `specs/161-verify-shared-control`: the verification seams `IVerificationTransport` + `IVerificationPresetCatalogue`, the config-driven `DefaultPresetCatalogue`, and the canonical `VerificationPreset` model already shipped into `Sorcha.UI.Components.User`).

**Relaunch of**: prior prodexec attempt `5c4ae08c10b2`, which **parked** because `VerificationSessionQr` injects `IVerificationTransport` and **no implementation was registered in DI** — so the component could not activate (and therefore could not be bUnit-tested). This relaunch makes self-contained, resolvable DI the headline acceptance condition.

**Input**: User description: "Verify unification B2-components (RELAUNCH of 5c4ae08c10b2, which parked because VerificationSessionQr injects IVerificationTransport with no registered implementation). Per specs/161-verify-shared-control, foundation merged #1045. Create the 3 shared Razor components in Sorcha.UI.Components.User/Components/Verify: QuestionSelectionPanel (reads IVerificationPresetCatalogue), VerificationSessionQr (renders QR + polls via IVerificationTransport), VerdictTrailPanel (4-layer trail + on-demand register-anchor). CRITICAL: also add a DEFAULT/STUB IVerificationTransport implementation in Sorcha.UI.Components.User (e.g. NotConfiguredVerificationTransport returning a clear not-yet-wired state) and register it in DI, AND ensure every injected dependency of the new components (IVerificationPresetCatalogue->DefaultPresetCatalogue, IVerificationTransport->stub, IRegisterAnchorClient) has a registered implementation so the components activate and are bUnit-testable. New components must accept a CancellationToken and implement IAsyncDisposable where they hold scan/poll loops. Relocate VerdictViewModel and IRegisterAnchorClient(+impl) from Sorcha.Verifier into shared Sorcha.Verifier.Engine; add Components.User->Verifier.Engine project ref; compute the rich verdict client-side via IVerifiablePresentationValidator. Do NOT rewire host pages or retire old paste/builder paths (that is B3). Add bUnit tests for all 3 components (they must activate under DI)."

## Summary

Today the desk `Sorcha.Verifier` app owns the only rich verify experience: pick a preset question →
show an OID4VP QR → poll → render a four-layer verdict trail (live presentation, issuer signature,
revocation, register anchor). The B2-foundation wave (#1045) already extracted the two seams
(`IVerificationTransport`, `IVerificationPresetCatalogue`) and the config-driven preset catalogue into
the shared `Sorcha.UI.Components.User` library, **without** any UI.

This wave (B2-components) completes the foundation by building the **three shared Blazor components**
that consume those seams, by **relocating the remaining rich-verdict types** (`VerdictViewModel`,
`IRegisterAnchorClient` + implementation) out of the desk-only `Sorcha.Verifier` app into shared
libraries so the verdict is computed **client-side** on any host, and — the relaunch's load-bearing
addition — by shipping a **default, resolvable DI registration** so every component can activate the
moment a host (or a bUnit test) mounts it.

The prior attempt parked because `VerificationSessionQr` depended on an `IVerificationTransport` that
nothing registered: the component threw on activation and could not be tested. This wave fixes that by
adding a **default stub transport** (`NotConfiguredVerificationTransport`) that returns an explicit
"verification transport is not yet wired" state, registering it (and the catalogue and the anchor
client) in the shared library's own DI extension, so the shared components are **self-contained and
bUnit-testable** and a host can later override the stub with the live HAIP transport in B3.

Critically, this wave lands the **building blocks, their default wiring, and their tests only**. It does
**not** rewire the PWA `/wallet/verify` page or the desk `Sorcha.Verifier` pages onto the new
components, and it does **not** retire the old paste-based `VerifyFlow` / `PresentationRequestBuilder` /
`InMemoryVerifierSessionStore` paths. That host rewiring and retirement is PR B3.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Shared question-selection panel (Priority: P1)

A verification UI on any host can present a catalogue of preset verification questions (e.g. "Age over
18?", "Confirm identity") plus a custom-question option, from a single shared component, rather than
each host re-implementing its own picker. The panel reads its presets from the shared
`IVerificationPresetCatalogue` so the same configured catalogue drives every host.

**Why this priority**: The question picker is the entry point of the verify flow and the most
self-contained of the three components — it has no transport/network dependency, only the catalogue
seam. It is the smallest independently shippable slice that proves the shared-component approach works.

**Independent Test**: Mount `QuestionSelectionPanel` in a bUnit test through the shared library's DI
registration (not a hand-built service collection); assert each preset renders as a selectable option,
the custom-question affordance renders, and selecting a preset raises the expected selection callback
carrying the chosen question.

**Acceptance Scenarios**:

1. **Given** a catalogue with three presets, **When** the panel renders, **Then** all three presets
   appear as selectable options alongside a custom-question option.
2. **Given** the rendered panel, **When** the operator selects a preset, **Then** the panel raises a
   selection event carrying that preset's question definition (key, label, required VCT/claims).
3. **Given** the rendered panel, **When** the operator chooses the custom option and supplies a valid
   custom question, **Then** the panel surfaces a custom verification question for the caller to start.

---

### User Story 2 - Shared session QR + polling, with a resolvable transport (Priority: P1)

A verification UI on any host can start a verification session for a chosen question, render the OID4VP
QR / deep link for the citizen to scan, and poll for completion — all from a single shared component
that drives its backend through the `IVerificationTransport` seam. The component is transport-agnostic:
the host supplies the concrete transport (the live HAIP-backed transport arrives in B3), but the shared
library **always registers a default transport** so the component activates out of the box. When that
default (the not-configured stub) is in effect, the component renders a clear "verification is not yet
wired here" state instead of throwing.

**Why this priority**: This is the interactive heart of the flow **and** the exact failure point that
parked the prior attempt. Making the component activatable under default DI — and renderable in a
"not-configured" state — is the central correction of this relaunch, so it is now P1 (raised from the
prior P2).

**Independent Test**: Mount `VerificationSessionQr` in bUnit through the shared library's DI
registration with no host override; assert it activates (does not throw) and renders the
not-configured state from the default stub transport. Then mount it with a test transport that returns
a known session id + QR deep link and a "pending → complete" poll sequence; assert the QR/deep link
renders, the component polls, and on completion it raises a "session complete" event carrying the
presentation token. Finally, dispose the component mid-poll and assert the poll loop is cancelled with
no post-disposal render.

**Acceptance Scenarios**:

1. **Given** only the default DI registration (no host override), **When** `VerificationSessionQr`
   is mounted, **Then** it activates without throwing and renders an explicit "not yet wired"/
   not-configured state from `NotConfiguredVerificationTransport`.
2. **Given** a selected question and a working transport, **When** the component starts a session,
   **Then** it renders the QR / deep link returned by the transport.
3. **Given** an in-progress session, **When** the transport reports "not complete", **Then** the
   component shows a waiting state and continues polling.
4. **Given** an in-progress session, **When** the transport reports "complete" with a presentation
   token, **Then** the component stops polling and signals completion to its caller.
5. **Given** an in-progress poll loop, **When** the component is disposed, **Then** the loop is
   cancelled cleanly (the component cancels its `CancellationToken` and completes its async disposal)
   with no post-disposal render or unobserved exception.

---

### User Story 3 - Shared verdict trail with on-demand register anchor (Priority: P1)

A verification UI on any host can render the rich four-layer verdict trail (live presentation, issuer
signature, revocation, register anchor) from one shared component, with the register-anchor layer
(layer 4) checked **on demand** via the relocated, shared register-anchor client. The verdict view
model is built **client-side** from a wallet presentation validated by the shared
`IVerifiablePresentationValidator`, so the desk verifier and the PWA can show byte-identical verdicts.

**Why this priority**: The shared, identical verdict is the core promise of the verify-unification
effort — it is the reason the rich-verdict types are being relocated to shared libraries.

**Independent Test**: Build a `VerdictViewModel` from a representative validation outcome and mount
`VerdictTrailPanel` in bUnit through the shared library's DI; assert the headline, disclosed-vs-withheld
claim split, and the first three trail layers render; then assert the layer-4 register-anchor affordance
triggers the (registered, resolvable) shared register-anchor client and renders the returned anchor
status when invoked.

**Acceptance Scenarios**:

1. **Given** a completed validation outcome, **When** the verdict view model is built client-side,
   **Then** it exposes the overall pass/fail headline, the disclosed and withheld claim sets, and the
   four validation layers.
2. **Given** a rendered verdict trail, **When** the trail first displays, **Then** the three offline
   layers (live presentation, issuer signature, revocation) render from the validation outcome without
   any further network call.
3. **Given** a rendered verdict trail with a register-anchor reference, **When** the operator requests
   the layer-4 register-anchor check, **Then** the shared register-anchor client runs and the trail
   updates with the anchor result (anchored / proof-invalid / unverified).

---

### User Story 4 - Components activate under DI (the parked-attempt fix) (Priority: P1)

Every dependency the three components inject — `IVerificationPresetCatalogue`,
`IVerificationTransport`, and `IRegisterAnchorClient` — has a default implementation registered in the
shared library's own DI extension. Mounting any of the three components needs only that single
registration call; nothing throws for a missing service. This is the precondition that the prior
attempt failed and the reason this relaunch exists.

**Why this priority**: Self-contained, resolvable DI is the explicit, blocking acceptance condition of
the relaunch. Without it the components are not testable and not droppable into a host, which is what
stopped the prior attempt.

**Independent Test**: Build a service collection, call the single shared registration extension, build
the provider, and resolve each of `IVerificationPresetCatalogue`, `IVerificationTransport`,
`IRegisterAnchorClient` — each resolves to a concrete implementation (catalogue → `DefaultPresetCatalogue`,
transport → `NotConfiguredVerificationTransport`, anchor → `RegisterAnchorClient`). Then mount each of
the three components through that provider and assert all three activate without a missing-service
exception.

**Acceptance Scenarios**:

1. **Given** a fresh service collection with only the shared registration extension applied, **When**
   the provider is built, **Then** all three injected seams resolve to concrete implementations.
2. **Given** that provider, **When** each of the three components is mounted in bUnit, **Then** each
   activates without a dependency-resolution exception.
3. **Given** the default registration, **When** a host later registers its own `IVerificationTransport`
   before/after the shared call, **Then** the host implementation is used (the default is overridable,
   not forced).

---

### User Story 5 - Type relocation keeps existing hosts green (Priority: P1)

Moving `VerdictViewModel` to `Sorcha.UI.Components.User` and `IRegisterAnchorClient` (+ implementation)
to `Sorcha.Verifier.Engine`, plus adding the `Sorcha.UI.Components.User → Sorcha.Verifier.Engine`
project reference, must not change the behaviour of the live desk verifier or PWA. The existing
`Sorcha.Verifier` and UI test suites must remain green, because no host page is rewired in this wave.

**Why this priority**: Relocations through a working, in-production verify app are the principal risk of
this wave; a regression here breaks the live desk verifier. Guarding the move is a release gate.

**Independent Test**: Build the full solution and run the existing `Sorcha.Verifier` and
`Sorcha.UI.Core` / Components test suites; all build and pass with the types resolving from their new
shared homes and the desk app still consuming them.

**Acceptance Scenarios**:

1. **Given** the types are relocated, **When** the solution builds, **Then** there are no broken
   references and no duplicate type definitions remain in `Sorcha.Verifier`.
2. **Given** the relocation, **When** the existing desk-verifier and UI test suites run, **Then** they
   stay green with no behavioural change to the live pages.

---

### Edge Cases

- **No transport wired (default stub)**: `VerificationSessionQr` renders a clear "verification transport
  is not yet wired" state and does not throw, start a session, or poll. This is the explicit
  not-configured outcome, not an error.
- **No presets configured**: the question panel falls back to the builtin bundled preset set provided by
  `IVerificationPresetCatalogue` (foundation behaviour) and renders normally.
- **Transport unreachable on start/poll** (a real transport that errors): the session-QR component
  surfaces an error/retry state rather than hanging silently; polling stops on a terminal transport
  error.
- **Credential carries no register-anchor reference**: the verdict trail shows the register-anchor layer
  as "unverified / not applicable" rather than failing the overall verdict.
- **Register unreachable or anchor not found**: the layer-4 check resolves to "unverified" (not "fail");
  an unverified layer never vetoes an otherwise-passing verdict.
- **Component disposed mid-poll / mid-scan**: the component cancels its `CancellationToken`, awaits its
  in-flight loop, and completes `IAsyncDisposable` with no post-disposal render and no unobserved task
  exception.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST provide a shared `QuestionSelectionPanel` component in
  `Sorcha.UI.Components.User/Components/Verify/` that renders the preset questions and a custom-question
  option, reading its presets from `IVerificationPresetCatalogue`, and raises a selection event carrying
  the chosen verification question.
- **FR-002**: The system MUST provide a shared `VerificationSessionQr` component in
  `Sorcha.UI.Components.User/Components/Verify/` that starts a session, renders the OID4VP QR / deep
  link, and polls for completion entirely through the `IVerificationTransport` seam, signalling
  completion (with the presentation token) to its caller.
- **FR-003**: The system MUST provide a shared `VerdictTrailPanel` component in
  `Sorcha.UI.Components.User/Components/Verify/` that renders the four-layer verdict trail and exposes an
  on-demand register-anchor (layer-4) check.
- **FR-004**: The system MUST provide a default stub implementation of `IVerificationTransport` in
  `Sorcha.UI.Components.User` (e.g. `NotConfiguredVerificationTransport`) that returns an explicit
  "verification transport is not yet wired" state from start/poll (no exception), so a host or test that
  has not supplied a real transport can still activate and render `VerificationSessionQr`.
- **FR-005**: The shared library MUST register, via a single DI extension, a default concrete
  implementation for **every** dependency the three components inject — `IVerificationPresetCatalogue`
  → `DefaultPresetCatalogue`, `IVerificationTransport` → `NotConfiguredVerificationTransport`,
  `IRegisterAnchorClient` → `RegisterAnchorClient` — such that mounting any component requires only that
  one registration call and nothing throws for a missing service.
- **FR-006**: The default transport registration MUST be **overridable** by a host (a host that registers
  its own `IVerificationTransport`, e.g. the B3 HAIP transport, wins over the default stub).
- **FR-007**: Each component that owns an asynchronous scan/poll loop (at minimum `VerificationSessionQr`)
  MUST accept a `CancellationToken`, implement `IAsyncDisposable`, and on disposal cancel and await its
  loop so there is no post-disposal render and no unobserved task exception.
- **FR-008**: The system MUST relocate `VerdictViewModel` (and its `From(...)` factory) from
  `Sorcha.Verifier` into the shared `Sorcha.UI.Components.User` library so both hosts can build and
  render the rich verdict.
- **FR-009**: The system MUST relocate `IRegisterAnchorClient` and its HTTP implementation
  (`RegisterAnchorClient`) from `Sorcha.Verifier` into the shared `Sorcha.Verifier.Engine` library so the
  register-anchor cross-check runs client-side on either host, with no coupling back to the desk verifier
  app.
- **FR-010**: The system MUST add a project reference from `Sorcha.UI.Components.User` to
  `Sorcha.Verifier.Engine` (for the validation outcome / layer models and the relocated register-anchor
  client) without introducing a reference cycle.
- **FR-011**: The verdict view model MUST be computable **client-side** from a wallet presentation using
  the shared `IVerifiablePresentationValidator`, rather than depending on a desk-only server session
  store, so the same verdict can be produced in a WASM host.
- **FR-012**: This wave MUST NOT rewire the live PWA `/wallet/verify` page or the desk `Sorcha.Verifier`
  pages onto the shared components, and MUST NOT retire the existing paste-based `VerifyFlow`,
  `PresentationRequestBuilder`, or `InMemoryVerifierSessionStore` paths (deferred to PR B3).
- **FR-013**: The relocations MUST leave no duplicate type definitions; `Sorcha.Verifier` MUST consume
  the relocated types from their new shared homes and continue to build and behave unchanged.
- **FR-014**: All three shared components MUST have bUnit tests that **activate the component through the
  shared DI registration** (proving the registrations resolve) and cover the primary interactions: preset
  render + selection; not-configured render + QR render + poll-to-completion + dispose-mid-poll
  cancellation; verdict-trail render + on-demand layer-4 affordance.
- **FR-015**: All new public members across the relocated types, the new components, the stub transport,
  and the DI extension MUST carry `/// <summary>` XML documentation to satisfy the project's
  build-warning convention.

### Key Entities *(include if feature involves data)*

- **QuestionSelectionPanel** — shared component; presents the catalogue's preset questions + custom
  option; output is the selected verification question.
- **VerificationSessionQr** — shared component; owns the start-session → render-QR → poll lifecycle via
  `IVerificationTransport`; accepts a `CancellationToken`, implements `IAsyncDisposable`; output is a
  completed presentation token; renders a not-configured state under the default stub transport.
- **VerdictTrailPanel** — shared component; renders the four validation layers and the disclosed /
  withheld claim split, with an on-demand register-anchor (layer-4) trigger.
- **NotConfiguredVerificationTransport** (new) — default `IVerificationTransport` stub; start/poll return
  an explicit not-yet-wired state with no live session; the safe default so components activate.
- **VerdictViewModel** (relocated) — overall pass + headline + issuer identity + disclosed/withheld
  claims + four `ValidationLayerResult`s + register-anchor id + credential id, built from a validation
  outcome.
- **IRegisterAnchorClient / RegisterAnchorClient** (relocated) — resolves and re-verifies a credential's
  issuance anchor (Merkle inclusion proof) against the public register; returns the layer-4 result
  (anchored / proof-invalid / unverified).
- **Shared verify DI extension** (new) — single registration entry point that wires the catalogue, the
  default transport, and the anchor client so the components are self-contained.
- **IVerificationPresetCatalogue / IVerificationTransport / IVerifiablePresentationValidator** (existing
  seams, consumed by the new components) — preset source, session backend, and client-side validator
  respectively.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: All three shared verify components **activate under the shared DI registration** and pass
  their bUnit tests (preset selection; not-configured + QR + poll completion + dispose-mid-poll;
  verdict trail + layer-4 affordance) — i.e. the exact failure that parked the prior attempt is gone.
- **SC-002**: From a service collection with only the single shared registration extension applied, all
  three injected seams (`IVerificationPresetCatalogue`, `IVerificationTransport`, `IRegisterAnchorClient`)
  resolve to concrete implementations, and a host can override the default transport.
- **SC-003**: The rich verdict view model and the register-anchor client compile and run inside a
  WASM-referencing shared library (client-side capable), with the verdict produced from a presentation
  via the shared validator.
- **SC-004**: The full solution builds and the existing `Sorcha.Verifier` and UI / Components test suites
  stay green after the relocations — zero behavioural regression in the live hosts.
- **SC-005**: No host page (`/wallet/verify`, desk verifier) is rewired and no legacy paste/builder path
  is removed in this wave — the scope boundary to B3 is preserved.

## Assumptions

- `Sorcha.Verifier.Engine` (validator + `VerificationOutcome` / `ValidationLayer*` models) is WASM-safe
  and already referenced by both hosts, as confirmed in the B2 design analysis and foundation wave.
- The B2-foundation seams and config-driven catalogue (`IVerificationTransport`,
  `IVerificationPresetCatalogue`, `DefaultPresetCatalogue`, canonical `VerificationPreset`) from #1045 are
  the contracts the new components consume; this wave builds the UI on top, it does not redesign the
  seams.
- The default stub transport is the placeholder until the live HAIP-backed transport and verifier-tier
  auth land in PR B3; a host that wants real verification overrides the default registration.
- The shared components follow the established `Sorcha.UI.Components.User` user-facing component
  conventions (MudBlazor, inline feedback rather than snackbar) so they drop into either host in B3.
- Building the verdict client-side reuses the existing `VerdictViewModel.From` mapping logic, adapted so
  its inputs come from the shared validator's outcome and the chosen question rather than a desk-only
  `VerifierSession` store.

## Out of Scope

- Rewiring the PWA `/wallet/verify` page and the desk `Sorcha.Verifier` pages onto the shared components
  (→ PR B3).
- Retiring the legacy paste-based `VerifyFlow`, `PresentationRequestBuilder`, and
  `InMemoryVerifierSessionStore` paths (→ PR B3).
- The live HAIP-backed `IVerificationTransport` implementation and verifier-tier authentication (→ PR B3).
- Any change to HAIP's server-side validation behaviour.
