# Feature Specification: Wire both hosts onto the shared verify control + live HAIP transport (PR B3, relaunch)

**Feature Branch**: `164-verify-host-rewire`

**Created**: 2026-06-26

**Status**: Draft

**Parent design**: `docs/superpowers/specs/2026-06-25-verify-unification-design.md` (stage B3)

**Builds on**:
- **PR B1** (#1044 — `specs/160-verify-haip-vptoken`): the HAIP verifier result poll now returns the raw `vp_token` (+ delegation) so a client can re-validate locally.
- **PR B2-foundation** (#1045 — `specs/161-verify-shared-control`): the seams `IVerificationTransport` + `IVerificationPresetCatalogue`, the config-driven `DefaultPresetCatalogue`, and the `VerificationPreset` model in `Sorcha.UI.Components.User`.
- **PR B2-components** (#1048 — `specs/163-verify-shared-components`): the three shared Blazor components (`QuestionSelectionPanel`, `VerificationSessionQr`, `VerdictTrailPanel`), the relocated `VerdictViewModel` / `IRegisterAnchorClient` (+ impl) in the shared libraries, the client-side verdict via `IVerifiablePresentationValidator`, and the **default stub** `NotConfiguredVerificationTransport` registered in the library's own DI extension.

**Relaunch note**: B2 deliberately shipped the shared control wired only to the **`NotConfiguredVerificationTransport` stub** — when mounted today the shared `VerificationSessionQr` renders an explicit "verification is not yet wired here" state and never polls. This wave (B3) is the one that makes the shared control *live* on both hosts. The single most common way B3 fails is the same DI seam lesson that parked the first B2 attempt: a host renders the shared control but **never overrides the stub**, so the operator still sees "not yet wired" with no error. This relaunch makes **"the live HAIP transport actually replaces the stub on both hosts, and the end-to-end verify flow completes"** the headline, load-bearing acceptance condition.

**Input**: User description: "Verify B3 (relaunch): wire PWA + desk Verifier to the shared verify control, HAIP transport replacing the stub, retire old paste paths. Spec docs/superpowers/specs/2026-06-25-verify-unification-design.md"

## Summary

After B1 and B2, the platform has one shared verify control (question selector → request-QR + poll →
4-layer verdict trail) sitting in the shared user component library, computing a rich client-side
verdict — but it is **not wired into any host route** and its transport is a no-op stub. Meanwhile the
two real verify surfaces still run their own divergent, legacy machinery:

- The **Citizen Wallet PWA** (`/wallet/verify`) renders the v1 **paste-based `VerifyFlow`**: the citizen
  manually pastes an offer JSON envelope into a text box; there is no QR, no live presentation, no
  register-anchor cross-check.
- The **desk `Sorcha.Verifier` app** owns the only QR-based flow today, but on **bespoke local
  machinery**: an inline `PresentationRequestBuilder`, an `InMemoryVerifierSessionStore`, a self-hosted
  `POST /r/{sessionId}/response` + `GET /r/{sessionId}/status` callback pair, and a bespoke
  `Outcome.razor` verdict page.

This wave **collapses both surfaces onto the one shared control**, points the control's transport at the
**live HAIP verifier endpoints** (replacing the stub), keeps the verifier identity pluggable per host
(PWA = ephemeral peer/doorstep identity; desk = stable org identity), and **retires the legacy paths**
so there is exactly one verify experience that is upgraded once and flows through to both hosts. HAIP's
own server-side validation is untouched — it keeps serving blueprint automation; the human-verifier
verdict stays computed client-side in the shared control.

After this wave the verify experience is unified: a verifier on either host picks **what to verify** →
shows a **QR** → the holder scans it with their Present flow → a **rich 4-layer verdict** (selective
disclosure, live presentation / KB-JWT, issuer signature, revocation) plus an on-demand
register-anchor cross-check is shown, identically, on both hosts.

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Live HAIP transport replaces the stub (Priority: P1)

A real `IVerificationTransport` implementation backed by the HAIP verifier endpoints is registered in
place of the `NotConfiguredVerificationTransport` stub, so the shared `VerificationSessionQr` component
can actually create a presentation request, render a scannable QR, poll for the holder's submission, and
return the raw `vp_token` for client-side verdict computation. This is the load-bearing slice: without
it, every host that mounts the shared control just shows "not yet wired".

**Why this priority**: Nothing else in the wave delivers value until the transport is live. Both host
rewires (US2, US3) depend on it, and it is the documented failure mode of the relaunch. It is
independently testable end-to-end against a running HAIP node before any UI is touched.

**Independent Test**: With the live transport registered, run a round trip — create a request, drive a
holder `direct-post` of a `vp_token`, poll — and assert the transport returns a non-empty session id +
QR deep-link on create, a pending state while unanswered, and `IsComplete` with the raw `vp_token` once
the holder has posted. Assert that the stub is **no longer resolved** (the resolved
`IVerificationTransport` is the HAIP implementation, not `NotConfiguredVerificationTransport`).

**Acceptance Scenarios**:

1. **Given** a host configured for B3, **When** the DI container resolves `IVerificationTransport`,
   **Then** it resolves the live HAIP-backed implementation, not `NotConfiguredVerificationTransport`.
2. **Given** the live transport, **When** a session is started for a chosen question, **Then** it
   returns a non-empty session id and a scannable QR deep-link (no "not configured" sentinel).
3. **Given** an open session whose holder has not yet responded, **When** the transport is polled,
   **Then** it reports an incomplete/pending state and does not surface a `vp_token`.
4. **Given** an open session whose holder has completed a `direct-post`, **When** the transport is
   polled, **Then** it reports complete and returns the raw `vp_token` (and delegation, when present).
5. **Given** the verifier's own token tier (consumer for PWA, org/desk for the desk app), **When** it
   calls create-request and poll, **Then** both calls are accepted (not rejected on audience/tier).
6. **Given** a transport or network fault, **When** the transport is polled, **Then** it surfaces a
   terminal error state rather than hanging or silently completing.

---

### User Story 2 — Citizen Wallet PWA verify runs on the shared control (Priority: P1)

A citizen opening `/wallet/verify` in the PWA sees the unified shared verify control instead of the
paste box: they pick a preset question (or build a custom one), a QR is shown, the holder scans it with
their Present flow, and the citizen-verifier sees the rich 4-layer verdict plus the on-demand
register-anchor check. The PWA supplies its existing **ephemeral** P-256 verifier identity, so the flow
remains a peer/doorstep interaction with no stable requester identity.

**Why this priority**: This is the primary user-facing payoff of the whole unification effort — the PWA
gains the full rich verify experience it never had (it was paste-only). It is the surface most users
will touch.

**Independent Test**: Navigate to `/wallet/verify` in the PWA, select a preset, confirm a QR renders,
drive a holder presentation, and confirm the 4-layer verdict renders with a pass/warn/fail headline and
a working register-anchor affordance. Confirm the paste box is gone.

**Acceptance Scenarios**:

1. **Given** the PWA, **When** the citizen opens `/wallet/verify`, **Then** the shared question-selection
   panel renders (presets + custom option), and no free-text paste field is present.
2. **Given** a selected question, **When** the session starts, **Then** a scannable QR + deep-link is
   shown and the page enters a waiting-for-holder state.
3. **Given** the holder completes the presentation, **When** the verdict is computed, **Then** the
   4-layer verdict trail (selective disclosure, live presentation, issuer signature, revocation) renders
   with a pass / warn / fail headline.
4. **Given** a completed verdict, **When** the citizen invokes the register-anchor affordance, **Then**
   the anchor cross-check runs against the public register and its result is shown.
5. **Given** the PWA host, **When** a verify session is created, **Then** the request carries the PWA's
   ephemeral P-256 verifier identity (fresh per session), not a stable org identity.

---

### User Story 3 — Desk Verifier runs on the shared control with its stable org identity (Priority: P2)

An operator using the standalone `Sorcha.Verifier` desk app sees the same shared verify control the PWA
uses, with identical question selection, QR, polling, and verdict trail — but with the desk app's
**stable org verifier identity** so a holder's wallet shows a known, named requester. The desk app's
legacy bespoke flow is replaced, not run in parallel.

**Why this priority**: The desk app already has a working rich flow, so this is consolidation rather
than net-new capability; the user value is "upgraded once, flows to both" and the removal of divergence.
It depends on US1 and reuses the same components proven in US2.

**Independent Test**: Run the desk Verifier, start a verification, confirm the shared control renders the
QR and the verdict identically to the PWA, and confirm the holder-facing request shows the desk app's
stable org identity (not an ephemeral key).

**Acceptance Scenarios**:

1. **Given** the desk Verifier, **When** the operator starts a verification, **Then** the shared
   question selector, request-QR/poll, and verdict-trail render (the same components as the PWA).
2. **Given** a desk verify session, **When** the request is created, **Then** it carries the desk app's
   stable org verifier identity.
3. **Given** the holder completes the presentation, **When** the verdict renders, **Then** it is the
   same client-side 4-layer verdict + register-anchor affordance shown in the PWA.
4. **Given** the desk app after this wave, **When** the codebase is inspected, **Then** the desk app no
   longer hosts its own request builder, session store, response/status callback, or bespoke verdict
   page.

---

### User Story 4 — Legacy verify paths are retired (Priority: P2)

With both hosts on the shared control, the divergent legacy verify machinery is removed so there is one
verify code path to maintain and no stale surface a user can stumble onto. Specifically: the PWA paste
`VerifyFlow`, the desk `PresentationRequestBuilder`, `InMemoryVerifierSessionStore`, the
`POST /r/{sessionId}/response` + `GET /r/{sessionId}/status` local callback, the bespoke
`Outcome.razor`, and any now-duplicated `VerdictViewModel` / question-preset definitions that the shared
libraries already own.

**Why this priority**: Retirement is what makes the unification real (otherwise divergence persists),
but it must follow the rewires so it never removes a path still in use. It is lower than the rewires
because the user value is maintainability/consistency, not a new capability.

**Independent Test**: Inspect the repository after the rewires: confirm the listed legacy types,
components, and endpoints are gone (or no longer referenced by any host route), and that the solution
builds and the verify flow still works end-to-end on both hosts.

**Acceptance Scenarios**:

1. **Given** the PWA rewired (US2), **When** the codebase is inspected, **Then** the paste-based
   `VerifyFlow` is removed and `/wallet/verify` references only the shared control.
2. **Given** the desk app rewired (US3), **When** the codebase is inspected, **Then**
   `PresentationRequestBuilder`, `InMemoryVerifierSessionStore`, the `/r/{sessionId}/response` +
   `/r/{sessionId}/status` callback endpoints, and `Outcome.razor` are removed.
3. **Given** the shared libraries already own `VerdictViewModel` and the preset catalogue, **When** the
   codebase is inspected, **Then** no host retains a duplicate copy of those types.
4. **Given** the retirements, **When** the full solution is built and tested, **Then** the build
   succeeds and the verify flow works end-to-end on both hosts (no dead references, no orphaned DI
   registrations).

---

### Edge Cases

- **Stub leaks through**: a host renders the shared control but its DI still resolves
  `NotConfiguredVerificationTransport` (the registration was not overridden) — the operator sees "not
  yet wired" instead of a QR. This is the headline failure mode and must be caught by an explicit
  resolution assertion, not only by an end-to-end test.
- **Holder never responds**: the session poll never reaches complete — the control must show a stable
  waiting state and a way out (expiry / cancel), not hang or spin forever.
- **Session expires** before the holder responds — the control surfaces an explicit expired/timeout
  state.
- **Tier/audience rejection**: HAIP rejects the verifier's token on create-request or poll because the
  consumer/desk tier is not allowed — must surface as a clear error, and the tier allowance must be
  confirmed against a live node during implementation.
- **vp_token validation fails** (bad signature, revoked, anchor mismatch) — the verdict renders a
  fail/warn trail rather than erroring out; a warn state is preserved (HAIP's flat server verdict has no
  warn — the client-side verdict does).
- **Register-anchor read unavailable** (public register endpoint down) — the on-demand anchor affordance
  reports it could not complete, without invalidating the already-computed crypto layers.
- **PWA offline / cold start**: the preset catalogue falls back to the bundled default so question
  selection still renders.
- **Navigating away mid-poll**: the polling loop is cancelled cleanly (CancellationToken / async
  disposal) with no leaked timers.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST provide a live `IVerificationTransport` implementation backed by the HAIP
  verifier endpoints (create presentation request, poll result) that returns a session id + scannable QR
  deep-link on create and the raw `vp_token` (+ delegation when present) once the holder has completed a
  `direct-post`.
- **FR-002**: Both hosts MUST register the live HAIP transport in place of `NotConfiguredVerificationTransport`,
  such that resolving `IVerificationTransport` yields the live implementation and never the stub.
- **FR-003**: The PWA `/wallet/verify` route MUST render the shared verify control (question selection →
  request-QR/poll → verdict trail) and MUST NOT render the paste-based `VerifyFlow`.
- **FR-004**: The desk `Sorcha.Verifier` app MUST render the same shared verify control as the PWA.
- **FR-005**: The verifier identity MUST remain pluggable per host: the PWA MUST supply its existing
  ephemeral P-256 verifier identity; the desk app MUST supply its stable org verifier identity. The
  identity used MUST be reflected in the presentation request the holder sees.
- **FR-006**: On poll-complete, the verdict MUST be computed client-side from the returned `vp_token`
  using the shared validator (4 layers: selective disclosure, live presentation / KB-JWT, issuer
  signature, revocation), identically on both hosts, with the on-demand register-anchor cross-check
  available as the fourth-layer affordance.
- **FR-007**: HAIP's server-side validation behaviour MUST remain unchanged (it continues to serve
  blueprint automation / `HaipPresentationConsumer`); B3 MUST NOT alter it. The human-verifier verdict
  is the client-side computation only.
- **FR-008**: The HAIP verifier create-request and result-poll endpoints MUST accept the verifier token
  tier used by each host (consumer for the PWA, org/desk for the desk app); any required tier allowance
  MUST be confirmed against a live node and applied.
- **FR-009**: The system MUST retire the legacy PWA paste path: the paste-based `VerifyFlow` component
  is removed and no longer referenced.
- **FR-010**: The system MUST retire the legacy desk machinery: `PresentationRequestBuilder`,
  `InMemoryVerifierSessionStore`, the `POST /r/{sessionId}/response` and `GET /r/{sessionId}/status`
  callback endpoints, and the bespoke `Outcome.razor` verdict page are removed.
- **FR-011**: The system MUST NOT retain host-local duplicates of types the shared libraries already own
  (`VerdictViewModel`, the verification preset definitions); hosts MUST consume the shared versions.
- **FR-012**: The polling lifecycle MUST be cancellable and self-disposing — navigating away or
  cancelling stops the poll loop with no leaked timers, honouring the component's CancellationToken /
  async disposal contract.
- **FR-013**: The control MUST present explicit, recoverable states for: not-yet-responded (waiting),
  session expiry/timeout, transport/tier error, and validation failure (fail/warn) — none of which may
  hang or silently complete.
- **FR-014**: After the rewires and retirements, the full solution MUST build and the verify flow MUST
  work end-to-end on both hosts with no dead references or orphaned DI registrations.

### Key Entities *(include if feature involves data)*

- **Verification session**: the in-flight verify exchange — a session id, the chosen question, the QR
  deep-link / request URI, the current state (pending / complete / expired / error), and, on
  completion, the raw `vp_token` (+ optional delegation). Created and polled via `IVerificationTransport`;
  no longer stored in a host-local in-memory store.
- **Verification preset (question)**: the "what to verify" definition (key, label, purpose, credential
  type, required/optional claims), sourced from the shared `IVerificationPresetCatalogue` (bundled
  default fallback), identical on both hosts.
- **Verifier identity**: the requester identity embedded in the presentation request — ephemeral P-256
  (PWA) or stable org (desk) — pluggable per host.
- **Verdict trail**: the client-side 4-layer outcome (selective disclosure, live presentation / KB-JWT,
  issuer signature, revocation) + register-anchor cross-check, with a pass / warn / fail headline,
  rendered by the shared verdict component on both hosts.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: There is exactly **one** verify control rendered by both hosts — `/wallet/verify` (PWA)
  and the desk Verifier render the same shared components, and no second/legacy verify surface remains
  reachable.
- **SC-002**: On both hosts, the resolved `IVerificationTransport` is the live HAIP implementation;
  `NotConfiguredVerificationTransport` is resolved on **zero** production host routes.
- **SC-003**: A verifier on either host can complete a full verify journey — pick a question, show a QR,
  have the holder present, and see a 4-layer verdict with the register-anchor affordance — with no
  manual paste step anywhere.
- **SC-004**: The verdict shown for the same `vp_token` is identical on both hosts (same layer outcomes
  and same pass/warn/fail headline).
- **SC-005**: The legacy verify code is gone — the paste `VerifyFlow`, the desk request builder /
  session store / response+status callback / bespoke outcome page, and any duplicated verdict/preset
  types are removed, and the solution still builds and passes its tests.
- **SC-006**: No verify session leaks resources — navigating away mid-poll leaves no active polling
  loop or timer.

## Assumptions

- B1 (#1044), B2-foundation (#1045), and B2-components (#1048) are merged on `master`; B3 branches from
  that state, so the shared components, seams, `DefaultPresetCatalogue`, relocated `VerdictViewModel` /
  `IRegisterAnchorClient`, and the B1 `vp_token`-returning poll are all present. (This worktree's local
  HEAD predates them; that is a worktree-staleness artifact, not a scope question.)
- The shared rich verdict is computed client-side via the already-WASM-safe
  `IVerifiablePresentationValidator`, and the register-anchor read endpoints are public/anonymous — no
  new server-side verdict surface is introduced by B3.
- A single live `HaipVerificationTransport` implementation serves both hosts; the only per-host variation
  is the injected verifier identity provider (ephemeral vs stable org).
- The verification preset catalogue continues to use B2's `DefaultPresetCatalogue` (bundled default). A
  central HTTP-backed preset endpoint (`GET /api/v1/verifier/presets`) described in the design's §5 is
  **out of scope** for B3 unless already shipped by B2.
- The verifier token tier may need a HAIP-side allowance; the exact status codes and any required change
  are confirmed against a live node during implementation (per the design's "verify during impl" note).

## Out of Scope

- Camera on the verify surface — the verifier *shows* a QR; camera-first scanning stays a Present
  concern.
- The PWA visual / wording refresh (deferred tidy-up).
- Any change to HAIP's server-side validation behaviour (untouched; B1 only added the additive `vp_token`
  return).
- A central HTTP-backed verification-preset catalogue endpoint / admin UI (design §5) — B3 keeps the
  bundled default catalogue.
