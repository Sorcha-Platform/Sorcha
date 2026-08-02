# Feature Specification: Citizen "My Applications" View

**Feature Branch**: `186-citizen-my-applications`

**Created**: 2026-08-02

**Status**: Draft

**Input**: User description: "Citizen 'My Applications' view (issue #1163) — a durable web surface answering 'what did I submit, and what happened?', with the decision reason projected from the ledger. Web only this pass; the reason code is projected onto the instance from the ledger and the citizen-facing wording is resolved on read. Adds the missing 'My Applications' nav entry and renames the overlapping 'Pending Actions' entry to 'Work Queue'. Closes #1163, closes #1267's remaining ask, and dissolves #1268. Email-on-decision is split out." *(The full brief, including settled technical decisions, is preserved in "Settled Design Constraints" below.)*

## User Scenarios & Testing *(mandatory)*

### User Story 1 - See everything I have submitted and where it stands (Priority: P1)

A citizen who has applied for one or more services signs in and wants a single, durable answer to *"what did I submit, and what is happening to it?"* — including applications that have already finished. They open **My Applications** and see every application they are a participant in, newest first, each showing what service it was, a human-readable reference, its current state, and when it was submitted and last changed.

**Why this priority**: This is the missing surface. Today a citizen has no place that answers this question at all — the only list they have shows outstanding *actions*, which goes empty the moment they submit, so a submitted application appears to vanish. Everything else in this feature hangs off this list existing.

**Independent Test**: Sign in as a citizen with at least one submitted application and open the My Applications page. The application appears with the correct service name, reference, and state. Shipping only this story already replaces "my application disappeared" with a truthful answer.

**Acceptance Scenarios**:

1. **Given** a citizen who has submitted two applications, one still progressing and one already decided, **When** they open My Applications, **Then** both appear, newest first, each showing the service name, its reference, its state, and its submission date.
2. **Given** a citizen who has never submitted anything, **When** they open My Applications, **Then** they see an empty state that explains what will appear there, not an error and not a blank page.
3. **Given** a citizen whose application has reached a terminal state (completed, rejected, timed out, or cancelled), **When** they open My Applications, **Then** that application is still listed — terminal applications are not hidden.
4. **Given** a citizen who participates in an application through more than one of their wallets, **When** they open My Applications, **Then** that application is listed exactly once.

---

### User Story 2 - Understand a decision and the reason behind it (Priority: P1)

A citizen whose application was rejected wants to know *why*, durably — not only in the moment a notification arrives, and not only until that notification is cleared. They open My Applications, see the application marked as rejected, and read the reason in the citizen-facing wording the service defined.

**Why this priority**: Equal-first with the list, because a state without a reason is the complaint #1267 recorded. A rejection that says only "Rejected" leaves the citizen unable to act. This is the ask that makes the page worth building rather than merely informative.

**Independent Test**: Drive an application to a rejection on a route that declares a citizen-facing decision notice, then open My Applications as the applicant. The row shows the rejected state *and* the defined reason text.

**Acceptance Scenarios**:

1. **Given** an application rejected via a route that declares a citizen-facing decision notice and carries a reason code, **When** the citizen views it, **Then** the reason is shown in the wording the service defined for that code.
2. **Given** an application rejected via a route that declares no citizen-facing notice, **When** the citizen views it, **Then** the rejected state is shown with no reason text — the system does not invent, guess, or substitute a generic explanation.
3. **Given** an application rejected with a reason code the service's notice does not define, **When** the citizen views it, **Then** the notice's default wording is shown if one exists, and no reason otherwise; the raw code is never shown to the citizen.
4. **Given** a citizen who has dismissed or never read the original decision notification, **When** they open My Applications at any later time or on any other device, **Then** the decision and its reason are still shown.

---

### User Story 3 - Pick up an application that is waiting on me (Priority: P2)

A citizen with an application that cannot progress without them sees that clearly on the same list, marked against the *application*, and continues it from there.

**Why this priority**: Depends on Story 1 existing, and is the mechanism that dissolves #1268 — a list of applications cannot strand a citizen on an action that has already been taken, because the row reports the state of the application rather than the state of an action.

**Independent Test**: Advance an application to a point where the citizen is the next actor, then open My Applications. The row is marked as needing them and offers a way to continue that lands in the existing form-filling flow.

**Acceptance Scenarios**:

1. **Given** an application whose next step is assigned to the signed-in citizen, **When** they view My Applications, **Then** the row is marked as needing them and offers a way to continue.
2. **Given** an application the citizen has just submitted and which is now with someone else, **When** they view My Applications, **Then** the row reports that it is submitted and progressing, and offers no action to take.
3. **Given** a citizen who follows the continue affordance, **When** the flow opens, **Then** they land in the existing form-filling experience for that step, with no separate or duplicated submission path.

---

### User Story 4 - Arrive here from a notification (Priority: P2)

A citizen who receives a decision or progress notification taps it and lands on the application it refers to, inside the app.

**Why this priority**: Notifications currently have nowhere to send a web citizen, so they are rendered non-navigable. This story turns an inert notice into a route to the answer, and it needs Stories 1 and 2 to have somewhere worth landing.

**Independent Test**: Trigger a decision notification for a citizen and follow it from the web activity surface. It opens that application's detail view within the app.

**Acceptance Scenarios**:

1. **Given** a notification referring to a specific application, **When** the citizen follows it on the web, **Then** the app opens that application's detail view without leaving the application.
2. **Given** an application detail view opened this way, **When** it renders, **Then** it shows the application's progress, its state, and — where one exists — the decision and its reason.
3. **Given** a citizen who follows a link to an application they do not participate in, **When** the request is made, **Then** access is refused, and the refusal reveals nothing about whether that application exists.

---

### User Story 5 - Tell the two lists apart (Priority: P3)

A signed-in user reading the navigation can tell at a glance which entry answers *"what did I submit?"* and which answers *"what must I do?"*, and no longer has to guess which one their missing application is under.

**Why this priority**: Cosmetic relative to the page itself, but it is the confusion #1267 and #1268 both recorded — a tester checked the actions list, saw "All Caught Up!", and concluded the application had been lost. Last because the page must exist before naming it matters.

**Independent Test**: Sign in and read the "My Activity" navigation section. Two distinctly-named entries are present, and following each lands on the list its name implies.

**Acceptance Scenarios**:

1. **Given** any signed-in user, **When** they open the navigation, **Then** "My Activity" contains a My Applications entry and an entry for outstanding work whose name is not a near-synonym of it.
2. **Given** a user who processes other people's applications, **When** they follow the outstanding-work entry, **Then** they reach the same working queue as before, with the same behaviour and the same scope — only its label has changed.
3. **Given** a user in any supported display language, **When** they open the navigation, **Then** both entries are shown translated, with no untranslated key or fallback string visible.
4. **Given** a user following an old bookmark to the retired workflows route, **When** it loads, **Then** they arrive at My Applications rather than at the start-a-new-application catalogue.

---

### Edge Cases

- **A service definition this node does not hold.** An application whose service definition is not replicated locally must still be listed, identified by whatever is known, rather than omitted or shown as an error.
- **An application created by ledger projection rather than by this node.** Such applications may lack the locally-cached service title; the list must still name the service or fall back gracefully.
- **Terminal states other than rejection.** Completed, timed out, and cancelled applications must each be presented distinguishably, not collapsed into "not active".
- **A shell application awaiting an open participant.** An application created but not yet claimed must not be attributed to a citizen who is merely eligible to claim it.
- **Reason present, decision not a rejection.** A route may carry a citizen-facing notice on a non-rejection outcome; the reason must display against whatever state the application actually reached.
- **A page of results beyond the last.** Requesting a page past the end returns an empty page with a truthful total, not an error.
- **Concurrent progress.** An application that advances while the citizen is reading the list must not cause the page to show a state that never existed; a refresh shows the newer state.
- **No reason ever recorded.** Applications decided before reason recording existed must display their state with no reason, and must not be presented as though the reason were missing due to an error.
- **A refusal that ends the application.** An application refused on its final step finishes with nothing left to do. Its internal lifecycle state is indistinguishable from a favourable finish, so the outcome shown must come from the recorded decision (FR-027) or the citizen is told their refused application "completed".
- **Multi-node delivery.** A decision made on another node must still be visible here once its transaction is folded locally, independent of whether a notification was ever delivered.

## Requirements *(mandatory)*

### Functional Requirements

**The list**

- **FR-001**: The system MUST provide a citizen-facing view listing every application the signed-in user participates in, including applications in terminal states.
- **FR-002**: The system MUST identify each listed application by the service it was submitted against and by its human-readable reference where one was generated.
- **FR-003**: The system MUST present each application's state in words a citizen can read, never as an internal code or numeric value.
- **FR-004**: The system MUST order the list newest-first and MUST return a stable, deterministic order across repeated requests so that paging cannot skip or duplicate an application.
- **FR-005**: The system MUST list an application exactly once even when the signed-in user participates in it through more than one wallet.
- **FR-006**: The system MUST show each application's submission time and the time it last changed.
- **FR-007**: The system MUST show, for an application still in progress, which step it is at and how many steps the service has.
- **FR-008**: The system MUST restrict the list to applications the signed-in user participates in, and MUST resolve that participation without depending on any claim that consumer-tier sign-ins omit.

**The decision and its reason**

- **FR-009**: The system MUST record, for each decided application, the route the decision took and the reason code it carried, taking both from the signed, non-secret record on the ledger.
- **FR-010**: The system MUST derive that record deterministically, such that two nodes folding the same sealed transactions hold the same recorded decision, and such that rebuilding an application from its transactions reproduces it.
- **FR-011**: The system MUST NOT require access to encrypted application content in order to record or display a decision reason.
- **FR-012**: The system MUST present the reason to the citizen in the wording the service defined for that reason code.
- **FR-013**: The system MUST omit the reason entirely when the service defined no citizen-facing notice for the route taken, and MUST NOT substitute generic or invented wording.
- **FR-014**: The system MUST NOT expose an internal reason code to the citizen.
- **FR-015**: The system MUST show the decision and its reason durably — independent of whether any notification was delivered, read, or dismissed, and on every device the citizen signs in from.
- **FR-027**: Where an application carries a recorded decision, the system MUST derive the outcome it shows the citizen from that decision — not from the application's internal lifecycle state alone. An application that finished because it was refused MUST NOT be presented to the citizen as merely "completed".

**Continuing an application**

- **FR-016**: The system MUST mark applications whose next step is assigned to the signed-in user, and MUST offer a way to continue them.
- **FR-017**: The system MUST route that continuation into the existing form-filling experience, and MUST NOT introduce a second submission path.
- **FR-018**: The system MUST NOT offer an action on an application that is not waiting on the signed-in user.

**Detail and arrival**

- **FR-019**: The system MUST provide a per-application detail view showing that application's progress, its state, and — where one exists — its decision and reason.
- **FR-020**: The system MUST allow a notification about an application to be followed to that application's detail view without leaving the app.
- **FR-021**: The system MUST refuse access to an application the caller does not participate in, and MUST make that refusal indistinguishable from the response for an application that does not exist.

**Navigation**

- **FR-022**: The system MUST offer a navigation entry to the applications list from the "My Activity" section.
- **FR-023**: The system MUST rename the outstanding-work navigation entry so it is not a near-synonym of the applications entry, in every supported display language, with the page heading and any other in-app references to it renamed consistently.
- **FR-024**: The system MUST leave the outstanding-work list's route, scope, and behaviour unchanged — it serves users who process other people's applications as well as citizens, and that use MUST keep working.
- **FR-025**: The system MUST redirect the retired workflows route to the applications list rather than to the start-a-new-application catalogue.

**Compatibility**

- **FR-026**: The system MUST leave the existing application-instance read interfaces unchanged in shape, so that the citizen wallet app and the command-line tool are unaffected by this feature.

### Key Entities

- **Application**: A citizen's submission against a service. Identified by an id and, where generated, a human-readable reference. Carries the service it runs, its current state, which step or steps it is at, who participates in it, when it was created, when it last changed, and when it completed.
- **Decision**: The outcome recorded against an application when it reaches a route the service marked as a decision. Carries the route taken and a non-secret reason code, both sourced from the signed ledger record. Holds no citizen-facing wording itself.
- **Decision Notice**: The service's definition of what to tell a citizen for a given reason code, including a default for codes it does not enumerate. Lives with the service definition, not with the application.
- **Application Step**: A stage of the service that an application passes through. Carries a title, its position in the service, and who is expected to act on it.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A citizen can find any application they have ever submitted — including finished and rejected ones — from the moment they sign in, in at most two navigation steps.
- **SC-002**: 100% of applications decided through a route carrying a citizen-facing reason display that reason to the applicant, unprompted, with no notification required.
- **SC-003**: A citizen returning after any interval, on any device they sign in from, sees the same decision and reason they would have seen the day it was made.
- **SC-004**: Zero applications appear to the citizen as offering an action that cannot be taken — the defect recorded in #1268 is not reproducible against this view.
- **SC-005**: A citizen shown the two "My Activity" list entries can state correctly which one holds a submitted application, without opening either.
- **SC-006**: Users who process other people's applications complete their existing queue tasks with no change in steps, scope, or results.
- **SC-007**: No citizen can retrieve, or infer the existence of, an application they do not participate in.
- **SC-008**: Two nodes holding the same sealed transactions report identical state and identical recorded decisions for every application, and a full rebuild from transactions reproduces both.

## Settled Design Constraints

These were decided before specification and are inputs to planning, not open questions.

**Surface**

- Web only in this pass, in the platform web client. The citizen wallet app is untouched: it already has both a service catalogue and a per-application detail page, and its navigation bar has no free slot. Its notification-routing stopgap remains in place.

**Reason handling**

- Project the reason **code**; resolve the citizen-facing **text** on read.
- The route id and reason code are carried onto the application by the deterministic ledger fold, sourced from signed, clear transaction metadata — preserving byte-identical projection across nodes and rebuild parity.
- The resolved wording is **not** folded. Doing so would place node-local service-definition state inside a deterministic fold, so two nodes holding different revisions of a service definition would project different applications.
- Wording is resolved at read time through the same resolution path the existing decision-notification dispatcher uses, so the page and the notification cannot disagree.

**Read surface**

- New citizen-scoped read endpoints under the established personal-scope route convention, returning a purpose-built projection with state as a readable string, the service title, the reference, step position, a needs-you marker, and the resolved reason.
- The existing application-instance endpoints are left untouched, keeping the blast radius on the wallet app and command-line tool at zero.

**Persistence**

- The new fields must be added to the persistent store's hand-maintained field-copy list as well as its schema. That copy list previously dropped a field silently, and the only implementation with reference semantics cannot exhibit the fault — so the existing whole-model round-trip test is the guard and must be extended rather than supplemented with a field-specific test.

**Navigation**

- Both entries stay; the outstanding-work entry is renamed so the two stop reading as synonyms. Its route, page, and wallet-scoped (not role-scoped) behaviour are unchanged.

**Test approach**

- Red first. Reason projection, rebuild parity, and a determinism assertion; the extended store round-trip; endpoint tests covering the participation gate, readable state, and reason resolution including the no-notice and unknown-code paths; component tests for loading, empty, populated, rejected-with-reason, and needs-you states; end-to-end tests for the navigation entry, a clean page load, and the rename. Any guard that passes on its first run is mutation-tested before being trusted.

## Assumptions

- The existing per-application form-filling experience is reused for continuation; this feature adds no new way to submit.
- The existing personal-notification surface remains the delivery mechanism for decisions; this feature adds the durable record, it does not replace the notification.
- Applications decided before reason recording existed will show state without a reason. No backfill is attempted, because the reason code sits on ledger records that are already sealed and can be re-derived later by a rebuild if ever needed.
- Page size and paging behaviour follow the platform's existing conventions for citizen-facing lists.
- The four currently supported display languages are the full set requiring the rename.
- Participation, not role, determines what a citizen sees — consistent with how the platform already scopes personal application reads.

## Out of Scope

- **Email on decision.** Independent of this view: it needs the transactional email facade and a new template, and gates nothing here. Raise separately.
- **A citizen wallet app applications list.** The wallet app keeps its current surfaces and its notification-routing stopgap.
- **A pre-existing command-line wire mismatch.** The instance-listing client declares a bare list against an endpoint that returns a paged envelope, and sends a filter the server never binds. It is untouched here because the endpoint is untouched; raise separately under the CLI wire-contract rule.

## Dependencies

- The ledger-derived application projection (Feature 145) — this feature extends its fold and its rebuild.
- The decentralised decision-notice mechanism (Feature 184) — this feature reuses its reason-code plumbing and its wording resolution.
- The unified activity timeline and personal inbox — the source of the notification this view is navigated from.
- The participation resolution used by the existing personal application reads, including its handling of sign-ins that carry no wallet claim.
