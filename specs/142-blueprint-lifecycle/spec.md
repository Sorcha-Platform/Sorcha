# Feature Specification: Blueprint Design Lifecycle Overhaul

**Feature Branch**: `142-blueprint-lifecycle`
**Created**: 2026-05-27
**Status**: Draft
**Input**: User description: "Blueprint design lifecycle overhaul: a staged golden-path designer workspace (Describe → Understand → Rehearse → Go live) with a lifecycle rail, guided AI, journey-first visualisation, form-layout authoring, safe rehearsal on an auto-provisioned test register, governed promote to live, and an amend-and-republish loop."

**Authoritative design**: `docs/superpowers/specs/2026-05-27-blueprint-lifecycle-overhaul-design.md`

## Clarifications

### Session 2026-05-27

- Q: Where is the rehearsal→Go-live gate enforced? → A: Hybrid — the UI disables Go live, and the publish operation enforces a **soft** server-side gate (blocks an unrehearsed version with a warning) that a user holding the register's publish-governance authority may explicitly override; every override is recorded for audit.
- Q: How does "act as each participant" sign during a full rehearsal? → A: System-managed ephemeral identities — the platform auto-creates per-role sandbox wallets and signs as the acting role on the administrator's behalf; they are discarded with the disposable sandbox. The administrator needs no wallet knowledge.
- Q: What does the quick dry-run exercise? → A: Flow only — schema validation, calculations, routing, and disclosure. Credential prerequisites and credential issuance are NOT exercised in dry-run (clearly marked "checked in full rehearsal"); they are owned by the full rehearsal.
- Q: What invalidates a passing rehearsal? → A: Only changes to the **executable definition** (participants, actions, routes, data schemas, calculations, disclosures, credential prerequisites/issuance, and behavioural form keywords such as file upload or credential-offer) re-lock Go live. Purely **presentational** layout edits (sectioning, wizard paging, width, introduction, review-summary, address-lookup, profile-autofill binding) do NOT re-lock — so a published service's layout may have been refined after its rehearsal.

## User Scenarios & Testing *(mandatory)*

The persona throughout is a **non-technical authority/control administrator** — e.g. a council officer who wants certified individuals to be able to apply for a service or grant. They are not expected to understand Blueprints, registers, credentials, or schemas as technical artefacts. The feature's job is to let them turn a plain-English intent into a live, governed public service, and to amend it over time, while the experience teaches the underlying model as they go.

### User Story 1 - The staged golden path teaches and gates the work (Priority: P1)

The administrator opens the designer and sees a single workspace organised as one coherent staged path — **Describe → Understand → Rehearse → Go live** — shown as an always-visible lifecycle rail. The rail shows where they are, what they have completed, what is available next, and what is locked. Critically, **Go live is locked until the service has been rehearsed successfully**, with a plain-language explanation on hover. The default way they *see* their service is a journey: a left-to-right, plain-language story of who does what, in order, with badges that surface "must prove X" and "issues Y"; a toggle reveals the technical flow for power users; clicking a step shows what that participant sees, decides, and the screen they fill.

**Why this priority**: This is the spine that removes the "mental leap". Even with no other story, replacing the incoherent parallel-tabs-plus-separate-pages experience with one staged, self-teaching workspace and a safety gate is a standalone improvement that delivers value.

**Independent Test**: Open the designer with an existing Blueprint; confirm the rail renders the four stages with correct done/current/available/locked states, that Go live is locked with an explanatory tooltip until a rehearsal has passed, that the journey view renders as a plain-language story with "must prove"/"issues" badges, that the technical-flow toggle switches views, and that clicking a step opens its detail (what is seen, the decision, the form).

**Acceptance Scenarios**:

1. **Given** a draft Blueprint with at least one Action, **When** the administrator opens the designer, **Then** the lifecycle rail shows the four stages with the current stage highlighted and Go live shown as locked.
2. **Given** the Go live stage is locked, **When** the administrator hovers the lock, **Then** a plain-language reason is shown ("rehearse a successful end-to-end run before publishing to a live register").
3. **Given** the Understand stage, **When** the administrator views the canvas, **Then** the service is shown as a journey with role-labelled steps and badges for any credential prerequisite ("Must prove …") and any credential issued ("Issues …").
4. **Given** the journey view, **When** the administrator toggles "Show technical flow", **Then** the node/route graph is shown, and toggling back returns to the journey.
5. **Given** the journey view, **When** the administrator clicks a step, **Then** a detail panel shows what that participant sees (the disclosure), the decision they make, and the form they fill.

---

### User Story 2 - Rehearse a service safely before it is ever live (Priority: P1)

Before going live, the administrator can try the whole service end-to-end without affecting any public register. They get two ways to do this: a **quick dry-run** that simulates the flow in the designer (no register), for fast iteration; and a **full rehearsal** that runs the real pipeline on a private, disposable test register provisioned automatically for them. Because one person needs to experience a multi-party flow, a **role switcher** lets them act as each participant in turn (applicant, reviewer, etc.). A rehearsal log shows, in plain language, what really happened (e.g. application submitted, prerequisite proven, transaction sealed, routed to reviewer, credential delivered to the test wallet). Completing a successful full rehearsal **unlocks Go live**.

**Why this priority**: This is the keystone capability that does not exist today and the thing the administrator most needs to gain confidence. It is also what the Go-live gate depends on.

**Independent Test**: From a validated Blueprint, run a quick dry-run and step through every Action as each participant via the role switcher; then run a full rehearsal and confirm a private test register is provisioned, the real flow completes end-to-end (including any prerequisite check and any credential issuance), the rehearsal log reflects real events, the sandbox can be reset/deleted, and a successful full rehearsal flips Go live from locked to available.

**Acceptance Scenarios**:

1. **Given** a Blueprint that validates with no blocking errors, **When** the administrator starts a quick dry-run, **Then** they can step through each Action, switching the acting role, and see routing and disclosure outcomes, with no register created.
2. **Given** the same Blueprint, **When** the administrator starts a full rehearsal, **Then** a private test register in developer mode is provisioned automatically and the Blueprint is made runnable on it without the administrator choosing or configuring a register.
3. **Given** a full rehearsal in progress, **When** the administrator acts as each participant in turn, **Then** the flow advances exactly as the live service would (prerequisite proof, review decision/routing, credential issuance/delivery), and the rehearsal log records the real events.
4. **Given** a full rehearsal that reaches its end successfully, **When** it completes, **Then** Go live becomes available and the success is recorded against the current Blueprint version.
5. **Given** a completed or abandoned rehearsal, **When** the administrator chooses reset/delete, **Then** the test register and its sandbox identities are discarded and a fresh rehearsal can be started.
6. **Given** the administrator changes the Blueprint after a passing rehearsal, **When** they return to the rail, **Then** Go live is locked again until the changed version is rehearsed.

---

### User Story 3 - Publish to a live register through governance, with full visibility of where it goes (Priority: P1)

When the rehearsal has passed, the administrator promotes the **exact version they rehearsed** to a live register. They choose from a drop list of registers they are permitted to publish to; selecting one shows a system-information detail card so they understand precisely what they are committing into — who owns the register, how it is validated, whether it is public or private, its synchronisation state, whether it is in developer mode, how many services already publish there, and their own governance role on it. Registers they have no rights on are visibly unavailable. A review summary and a plain-language permanence/versioning notice precede the final publish, which goes through the existing register publish-governance gate and produces a versioned, immutable record.

**Why this priority**: Going live is the point of the whole journey; without it the path does not complete. Preserving and surfacing the existing governance is a hard requirement.

**Independent Test**: With a passed rehearsal, open Go live; confirm the register drop list shows only/clearly-distinguishes registers the administrator may publish to, that selecting one populates a system-info detail card from the register's own metadata, that a no-rights register cannot be published to, that the review reflects the rehearsed version, and that publishing creates a versioned immutable record on the chosen register via the existing governance check.

**Acceptance Scenarios**:

1. **Given** a passed rehearsal, **When** the administrator opens Go live, **Then** a drop list of candidate live registers is shown, with registers they lack publish rights on clearly marked unavailable.
2. **Given** the register drop list, **When** the administrator selects a register, **Then** a detail card shows ownership, validation (validators and required signatures), visibility, synchronisation state, developer-mode status, count of services already published there, and the administrator's governance role.
3. **Given** a selected register the administrator is authorised on, **When** they confirm publish, **Then** the exact rehearsed Blueprint version is published to that register as a new immutable version through the existing governance gate.
4. **Given** a selected register the administrator is NOT authorised on, **When** they attempt to publish, **Then** publication is refused with a clear reason and no record is written.
5. **Given** a successful publish, **When** it completes, **Then** the service is shown as live with its version, and the disposable test register may be discarded.

---

### User Story 4 - Guided AI on-ramp lowers the starting mental leap (Priority: P2)

A newcomer who does not know what is possible is not faced with a blank prompt. The assistant opens as a **guided interviewer**: it offers a few recognisable starting points to pick from (e.g. "Apply for a grant", "Apply for a permit/licence", "Certify, then apply"), or it refines the idea by asking about sector, purpose, who applies, who decides, and whether applicants must prove something first. As the administrator answers, the journey builds up live in the canvas so they watch their service take shape. The assistant silently translates plain-language answers into the underlying constructs (open starting Action, prerequisite proof, reviewer, credential issued) without exposing jargon.

**Why this priority**: The existing AI chat already produces Blueprints; this story improves the *entry* experience and directly addresses "help someone new", but the path is usable without it.

**Independent Test**: Start a new service; confirm the assistant opens with directed-build choices and/or guided questions rather than a blank box, that choosing a directed-build option or answering the questions produces a coherent starting journey rendered live, and that the resulting Blueprint contains the correct constructs (e.g. a credential-gated open starting Action when the administrator said applicants must be certified) without the administrator naming those constructs.

**Acceptance Scenarios**:

1. **Given** a brand-new service, **When** the assistant opens, **Then** it presents directed-build starting points and/or a guided line of questions (sector, purpose, participants, prerequisites), not an empty prompt.
2. **Given** a directed-build choice is selected, **When** it is applied, **Then** a recognisable starting journey appears live in the canvas.
3. **Given** the administrator states a prerequisite in plain language (e.g. "they must be a certified resident"), **When** the assistant updates the Blueprint, **Then** the starting Action carries the corresponding credential prerequisite and the journey shows a "Must prove …" badge, without the administrator using technical terms.

---

### User Story 5 - Author the forms people fill, including imported/AI schemas with no layout (Priority: P2)

The administrator can shape the screens applicants and reviewers fill, using the very same renderer the live service uses (what they arrange is exactly what citizens get). A data schema that arrives with no layout — imported or AI-generated — still renders immediately with sensible fields inferred from each field's type, so the administrator is never blocked. From there they can redo the layout and apply richer behaviours: group fields into sections (including side-by-side), split a long form into wizard pages, set width and an introduction, bind fields to profile autofill (or opt a field out), mark a field as a file/photo upload, or mark a page as a review/ID-card summary. They can do this by direct manipulation or by asking the assistant; both stay in sync.

**Why this priority**: Important for real services (especially imported schemas), but the lifecycle is demonstrable without bespoke layout authoring, so it sits below the core path.

**Independent Test**: Import or generate a schema with no layout; confirm it renders with type-inferred fields in the production renderer; apply a section grouping and a wizard-page split and confirm the rendered form changes accordingly; bind a field to profile autofill and confirm the binding takes; confirm the assistant can perform the same layout changes and that direct-manipulation and assistant edits converge on the same definition.

**Acceptance Scenarios**:

1. **Given** an Action whose data schema has no layout, **When** the administrator opens its form, **Then** the form renders with fields inferred from their data types using the same renderer the live service uses.
2. **Given** a rendered form, **When** the administrator groups fields into a section or splits the form into wizard pages, **Then** the form preview updates to reflect the new layout.
3. **Given** a field whose shape suggests autofill (e.g. an email field), **When** the administrator enables profile autofill, **Then** the field is bound to the corresponding profile attribute, and they can also opt a field out.
4. **Given** a layout change made by direct manipulation, **When** the administrator instead asks the assistant to make an equivalent change, **Then** both produce the same underlying form definition.

---

### User Story 6 - Amend a live service and re-publish a new version (Priority: P3)

The administrator can reopen a service that is already live, make changes, and publish a new version. Opening a live service produces a new draft version; the administrator walks it through Understand and Rehearse exactly as for a new service; Go live then re-publishes the new version to the same register through governance. The lifecycle loops cleanly: a live service is never edited in place — every change is a new, rehearsed, governed version.

**Why this priority**: Essential for the long-term coherence of the lifecycle, but the first three stories deliver a complete create-to-live path; amendment can follow.

**Independent Test**: Open a published service; confirm it becomes a new draft version, that Go live is locked until the new version is rehearsed, that rehearsing and publishing produces an incremented version on the same register through governance, and that the original live version remains the record until the new one is published.

**Acceptance Scenarios**:

1. **Given** a published service, **When** the administrator opens it to amend, **Then** a new draft version is derived and the rail shows Go live locked pending a fresh rehearsal.
2. **Given** an amended draft that has passed rehearsal, **When** the administrator publishes, **Then** the new version is published to the same register as an incremented, immutable version through the existing governance gate.
3. **Given** an amendment in progress, **When** it has not yet been published, **Then** the previously live version remains the authoritative published record.

---

### Edge Cases

- **Blueprint has blocking validation errors**: Rehearse is unavailable; the rail communicates what must be fixed before rehearsal.
- **Administrator edits the Blueprint after a passing rehearsal**: an executable-definition change invalidates the passing state and re-locks Go live until re-rehearsed (FR-023); a purely presentational layout edit does not re-lock.
- **Administrator has no register they may publish to**: Go live explains there is no eligible register and how to obtain rights, rather than presenting an empty, confusing picker.
- **Selected live register is owned by another node / not caught up / in developer mode**: the system-info card surfaces this so the administrator can make an informed choice (e.g. avoid publishing a public service to a developer-mode register).
- **Test-register provisioning fails or is slow**: the rehearsal surfaces a clear status and a retry; the quick dry-run remains available for iteration meanwhile.
- **Rehearsal abandoned midway**: the sandbox can be reset/deleted; no partial state leaks into any live register.
- **Imported schema is malformed (not just layout-less)**: the form surface reports what is wrong rather than rendering nothing.
- **Concurrent edit of the same service** by two administrators: out of scope for this feature (single-author assumption) — see Assumptions.

## Requirements *(mandatory)*

### Functional Requirements

**Lifecycle shell & rail**
- **FR-001**: The designer MUST present a single workspace organised as one staged path with a persistent rail showing the stages Describe, Understand, Rehearse, and Go live.
- **FR-002**: The rail MUST indicate, per stage, whether it is completed, current, available, or locked, and MUST keep itself to a compact footprint with explanatory copy presented on hover rather than as permanent text.
- **FR-003**: The administrator MUST be able to move between available stages freely (non-linear), while locked stages remain inaccessible until their precondition is met.
- **FR-004**: Go live MUST remain locked in the UI until the current Blueprint version has passed a full rehearsal, and the lock MUST explain the reason in plain language. (UI gate; the server-side soft gate and override are FR-032.)
- **FR-005**: A first-time administrator MUST be offered a dismissible guidance overlay introducing the staged path.

**Understand / visualisation**
- **FR-006**: The default visualisation MUST be a plain-language, role-labelled journey of the service in sequence.
- **FR-007**: The journey MUST surface credential prerequisites as a "Must prove …" badge and credential issuance as an "Issues …" badge, derived from the Blueprint.
- **FR-008**: The administrator MUST be able to toggle from the journey to a technical flow view (Actions and routes, including conditional and rejection branches) and back.
- **FR-009**: Selecting a journey step MUST reveal that step's detail: what the participant sees (the Disclosure), the decision they make, and the form they fill.

**Describe / guided AI**
- **FR-010**: When starting a new service, the assistant MUST open as a guided interviewer — offering recognisable directed-build starting points and/or asking about sector, purpose, participants, and prerequisites — rather than a blank prompt.
- **FR-011**: The journey MUST update live in the canvas as the assistant builds or changes the Blueprint.
- **FR-012**: The assistant MUST translate plain-language intent into the correct underlying constructs (e.g. an open starting Action, a credential prerequisite, a reviewer Action, a credential issuance) without requiring the administrator to use technical terms.

**Form authoring**
- **FR-013**: The form surface MUST use the same renderer that the live service uses, so that the authored layout matches what applicants and reviewers see.
- **FR-014**: A data schema with no layout MUST render immediately with controls inferred from field types, and MUST NOT block the administrator.
- **FR-015**: The administrator MUST be able to apply layout and behaviour to a form — at minimum: grouping into sections (including side-by-side), splitting into wizard pages, setting width and an introduction, binding fields to profile autofill (with per-field opt-out), marking file/photo uploads, and marking a review/summary page.
- **FR-016**: Layout authoring MUST be possible both by direct manipulation and by asking the assistant, and the two MUST converge on the same underlying form definition.
- **FR-017**: Authored layout MUST be persisted on the Blueprint in the platform's standard form-layout representation, such that a hand-edited and a UI-edited Blueprint are equivalent.

**Rehearse**
- **FR-018**: The administrator MUST be able to run a quick dry-run that simulates the flow in the designer without creating any register. The dry-run MUST exercise schema validation, calculations, routing, and disclosure; it MUST NOT exercise credential prerequisites or credential issuance, and any such step MUST be clearly marked as "checked in full rehearsal" so the dry-run is not mistaken for full fidelity.
- **FR-019**: The administrator MUST be able to run a full rehearsal that exercises the real pipeline on a private test register that is provisioned automatically, without the administrator selecting or configuring a register.
- **FR-020**: The full rehearsal's test register MUST be isolated from any live/public register, clearly marked as a private sandbox, and disposable (resettable/deletable).
- **FR-021**: Both rehearsal modes MUST let one administrator act as each participant in turn (role switching) to walk a multi-party flow end-to-end. For a full rehearsal, the platform MUST mint ephemeral per-role sandbox identities and sign as the acting role on the administrator's behalf (the administrator needs no wallet of their own per role); these identities MUST be discarded when the sandbox is reset/deleted.
- **FR-022**: The full rehearsal MUST exercise the real behaviours the live service would (including any credential prerequisite check, routing on decisions, and credential issuance/delivery) and MUST present a plain-language log of the real events.
- **FR-023**: A successful full rehearsal MUST be recorded against the rehearsed **executable definition** and MUST unlock Go live. A subsequent change that affects the executable definition (participants, actions, routes, data schemas, calculations, disclosures, credential prerequisites/issuance, or behavioural form keywords such as file upload or credential-offer) MUST invalidate that state and re-lock Go live. Purely presentational layout changes (sectioning, wizard paging, width, introduction, review-summary, address-lookup, profile-autofill binding) MUST NOT re-lock.

**Go live / governance**
- **FR-024**: Go live MUST publish the current Blueprint version, whose **executable definition** MUST match a passing full rehearsal; presentational layout may have been refined after that rehearsal without re-locking (FR-023).
- **FR-025**: The register picker MUST present candidate live registers as a selectable list and MUST clearly distinguish registers the administrator may not publish to.
- **FR-026**: Selecting a register MUST present a system-information detail card sourced from that register's own metadata, including ownership, validation (validators and required signatures), visibility, synchronisation state, developer-mode status, count of services already published, and the administrator's governance role.
- **FR-027**: Publication MUST enforce the existing register publish-governance check and MUST refuse, with a clear reason and no record written, when the administrator is not authorised.
- **FR-028**: A successful publication MUST create a new immutable, versioned record on the chosen register and MUST reflect the service as live with its version.
- **FR-032**: The publish operation MUST verify server-side that the **executable definition** of the Blueprint version being published matches a recorded passing full rehearsal (presentational-only differences do not fail this check). If it does not, publication MUST be blocked with a clear warning; a user holding the register's publish-governance authority MAY override the block by explicit confirmation. Every override (who, when, which version, which register, and the reason if given) MUST be recorded for audit.

**Amend loop**
- **FR-029**: The administrator MUST be able to reopen an already-published service, which MUST derive a new draft version.
- **FR-030**: An amended draft MUST pass a fresh rehearsal before Go live is available, and publishing it MUST produce an incremented version on the same register through governance.
- **FR-031**: While an amendment is unpublished, the previously published version MUST remain the authoritative live record.

### Key Entities *(include if feature involves data)*

- **Service (Blueprint)**: The multi-participant process the administrator is authoring. Carries participants, ordered Actions (with data schemas, form layout, disclosures, credential prerequisites/issuance, routing), and a version. "Service" is the administrator-facing framing of a Blueprint.
- **Lifecycle State**: The per-Blueprint authoring state that drives the rail — current stage, whether the current version has passed a full rehearsal, and version/amend context. Not a public ledger record; it governs the workspace and the Go-live gate.
- **Journey View Model**: A plain-language, read-only projection of a Blueprint into role-labelled steps with prerequisite/issuance badges and per-step detail (disclosure, decision, form). Derived from the Blueprint; not separately authored.
- **Rehearsal**: A run of the service in either dry-run (no register) or full (test register) mode, with an acting-role context and a plain-language event log, producing a pass/fail outcome bound to a Blueprint version.
- **Test Register (sandbox)**: A private, developer-mode, disposable register provisioned automatically for a full rehearsal, isolated from live registers, with sandbox participant identities for role-switched walking.
- **Live Register (target)**: An existing register the service is published to, exposing system information (ownership, validation, visibility, sync state, mode, published count) and a governance roster that determines publish rights.
- **Published Version**: The immutable, versioned record created on a live register at Go live; amendments create incremented versions on the same register.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A non-technical administrator, given only a plain-English intent, can produce a working service and take it live following the staged path without external help or documentation, in a single session.
- **SC-002**: Every service taken live has either passed a full rehearsal of the published version's executable definition or carries a recorded, attributable override by an authorised user — publishing an unrehearsed executable definition is impossible without such a logged override.
- **SC-003**: An administrator can complete a full rehearsal of a typical three-step service (apply → review → issue) end-to-end, acting as each participant, in under 5 minutes from a validated Blueprint.
- **SC-004**: A schema imported with no layout is usable in a form (renders with sensible fields) with zero manual steps, and can be re-laid-out without editing raw schema text.
- **SC-005**: Before confirming Go live, an administrator can identify the target register's owner, validation, visibility, mode, and their own publish rights from the on-screen detail without leaving the workspace.
- **SC-006**: At least 90% of first-time administrators in usability testing can correctly state, unprompted, the order of the lifecycle (describe → understand → rehearse → go live) and why Go live is initially locked, after one pass through the workspace.
- **SC-007**: Amending a live service and publishing a new version requires no steps the administrator did not already learn creating the first version (same Understand → Rehearse → Go live loop), and the previously live version stays authoritative until the new version is published.
- **SC-008**: No rehearsal activity ever results in data written to a live/public register (full isolation of the sandbox).

## Assumptions

- **Single-author editing**: One administrator authors a given service at a time; collaborative/concurrent multi-author editing is out of scope.
- **Test-register provisioning exists or is built as part of this feature**: a mechanism to auto-provision a private developer-mode register plus sandbox participant identities for a rehearsal, and to tear it down, is assumed available to the workspace; its precise ownership (which service provisions it) is an implementation decision deferred to planning.
- **Sandbox signing**: during a full rehearsal the platform mints ephemeral per-role sandbox identities and signs as the acting role on the administrator's behalf; the administrator does not supply or manage any participant wallet, and the identities are discarded with the sandbox (resolved — Clarifications 2026-05-27).
- **Register system information is obtainable**: ownership/relationship, validator roster and required signatures, visibility, synchronisation state, and developer-mode status are available to surface in the Go-live detail card (possibly via a new aggregate read over existing data).
- **Existing governance is authoritative**: this feature surfaces and enforces the existing register publish-governance roster; it does not introduce new governance mechanics.
- **Reusing the live form and run components**: the form surface and the full-rehearsal run reuse the production renderer and execution path rather than re-implementing them, so rehearsal fidelity matches production.
- **Wiring prerequisites**: reopening an existing Blueprint into the workspace (currently not wired) is in scope as a dependency of the amend loop.

## Dependencies

- The currently non-functional "open/load an existing Blueprint into the designer" path must be made to work (blocks the amend loop and reopening drafts).
- Automatic provisioning and teardown of a disposable developer-mode test register, with sandbox participant identities.
- A register system-information source sufficient to populate the Go-live detail card (ownership, validation roster + required signatures, visibility, sync state, developer-mode, published count, caller's governance role).
- A defined classification of Blueprint form/layout keywords as **presentational** (do not re-lock the rehearsal gate) versus **behavioural** (part of the executable definition; re-lock), used by FR-023/FR-032. Initial split: presentational = sectioning, wizard paging, width, introduction, review-summary, address-lookup, profile-autofill binding; behavioural = file upload, credential-offer (and anything that changes data submitted, transactions produced, or credentials consumed/issued).

## Out of Scope

- Changes to the citizen-facing run/submission experience beyond reusing its components inside rehearsal.
- New credential formats, trust mechanics, or governance models (these are provided by upstream features and are unchanged here).
- Collaborative/multi-author simultaneous editing of a single service.
- Any change to register genesis or cross-node federation.
