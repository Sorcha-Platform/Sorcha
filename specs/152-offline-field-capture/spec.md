# Feature Specification: PWA Offline / Field Capture

**Feature Branch**: `152-offline-field-capture`

**Created**: 2026-06-13

**Status**: Draft

**Input**: User description: "PWA offline / field capture (sub-project C): offline-capable, field-first workflow capture in the Citizen Wallet PWA — encrypted local drafts with autosave/resume, pre-cache of pending actions for offline open, queued/deferred submit that flushes on reconnect, detect/hold/ask conflict handling, and photo/media capture submitted via the existing Files/file-chunk mechanism. Consumer-tier; depends on sub-project A."

**Source design**: `docs/superpowers/specs/2026-06-13-pwa-offline-field-capture-design.md` (sub-project C of the PWA workflow-participation programme; depends on A).

## User Scenarios & Testing *(mandatory)*

Sub-project A lets a citizen discover and complete the actions waiting on them — but only online, in
one sitting, and with no way to attach photos. This feature makes participation **field-first**: a
person can open their pending workflow actions with no connectivity, fill them in, capture photos,
save the work locally, and have it submit automatically when back online — without ever silently
losing what they captured.

### User Story 1 - Resume and submit an offline draft (Priority: P1)

A citizen opens an action with no connectivity, fills in part of the form, and closes the app. Later
they reopen the app, find their work exactly as they left it, finish it, and — once back online — it
submits.

**Why this priority**: This is the core offline loop and the minimum viable proof that work survives
across sessions and connectivity gaps. Without it there is no "field-first".

**Independent Test**: With no connectivity, open a cached action, enter some data, close and reopen
the app, confirm the data is restored, complete it, restore connectivity, and confirm it submits.

**Acceptance Scenarios**:

1. **Given** a citizen filling an action with no connectivity, **When** they navigate away or close
   the app, **Then** their entered data is saved locally and is not lost.
2. **Given** a saved offline draft, **When** the citizen reopens that action, **Then** the form is
   restored to exactly what they had entered.
3. **Given** a completed draft and restored connectivity, **When** the citizen submits (or the app
   flushes), **Then** the action is submitted and the draft is cleared.
4. **Given** locally saved drafts, **When** the citizen views their work, **Then** each item shows a
   clear state (e.g. "Saved offline", "Ready to submit").

---

### User Story 2 - Open any pending action offline (Priority: P1)

A citizen, while they still have signal, lets the app prepare; later, with no connectivity, they can
open **any** of the actions waiting on them — not just ones they opened earlier — fill them, and
save them.

**Why this priority**: True field work means a citizen can walk into a no-signal area and still start
any outstanding action. It is the foundation that makes US1's "open offline" possible for actions
not previously opened.

**Independent Test**: With connectivity, let the app prepare; go offline; open a pending action that
was never opened before; confirm the form renders and can be filled and saved.

**Acceptance Scenarios**:

1. **Given** connectivity, **When** the citizen has pending actions, **Then** the app prepares each
   one so it can be opened later without connectivity.
2. **Given** no connectivity, **When** the citizen opens a prepared pending action, **Then** its form
   renders fully and can be filled and saved.
3. **Given** no connectivity, **When** the citizen opens an action that was **not** prepared, **Then**
   they see a clear "available when you're back online" state rather than a broken form.
4. **Given** the prepared set is stale, **When** connectivity returns, **Then** the prepared actions
   refresh from the current server state.

---

### User Story 3 - Queued submit that flushes on reconnect (Priority: P2)

A citizen completes one or more actions offline; the app holds them and submits them automatically
when connectivity returns, showing progress.

**Why this priority**: Turns "saved locally" into "actually submitted" without the citizen
babysitting it. Builds on US1.

**Independent Test**: Complete an action offline, confirm it shows as queued, restore connectivity,
and confirm it transitions to submitted without manual action.

**Acceptance Scenarios**:

1. **Given** an action completed with no connectivity, **When** the citizen finishes it, **Then** it
   is queued for submission and shown as "Queued".
2. **Given** queued submissions, **When** connectivity returns (or the app is reopened), **Then** the
   app submits them automatically and updates each to "Submitted".
3. **Given** multiple queued submissions, **When** one fails transiently, **Then** the others still
   proceed and the failed one retries.
4. **Given** a queued submission already accepted by the server, **When** it is retried, **Then** it
   is not duplicated.

---

### User Story 4 - Conflict handling: detect, hold, ask (Priority: P2)

A citizen submits an action that, by the time it reaches the server, is no longer valid (the workflow
moved on, it was already submitted from another device, or the instance closed). The app does not
silently drop it — it keeps the work, explains what changed, and asks what to do.

**Why this priority**: The safety guarantee that makes offline trustworthy. Without it, field work
can be silently lost.

**Independent Test**: Queue a submission, change the underlying action state server-side, restore
connectivity, and confirm the citizen is shown what changed with discard vs. re-open-fresh choices —
and their captured data is retained until they choose.

**Acceptance Scenarios**:

1. **Given** a queued submission that is no longer applicable, **When** the app tries to submit it,
   **Then** it is held (not discarded) and marked "Needs attention".
2. **Given** a held submission, **When** the citizen views it, **Then** they are told why it could
   not be applied (already submitted / step moved on / closed).
3. **Given** a held submission, **When** the citizen decides, **Then** they can discard it or re-open
   it against the current action state, and their captured data is retained until they choose.

---

### User Story 5 - Capture and submit photos/media offline (Priority: P3)

A citizen captures one or more photos as part of an action while offline; the photos are saved with
the draft and submitted along with the action when back online.

**Why this priority**: Completes the field-evidence headline, but is the only part that touches the
submission/attachment plumbing, so it is sequenced last.

**Independent Test**: With no connectivity, capture a photo on an action, save and reopen the draft,
confirm the photo persists, restore connectivity, submit, and confirm the photo is attached to the
submitted action.

**Acceptance Scenarios**:

1. **Given** no connectivity, **When** the citizen captures a photo on an action, **Then** the photo
   is saved with the draft and visible on reopen.
2. **Given** a draft with captured photos and restored connectivity, **When** it is submitted,
   **Then** the photos are submitted as attachments of the action.
3. **Given** a photo exceeds the allowed size, **When** the citizen captures it, **Then** they are
   warned at capture time, not at submission.

---

### Edge Cases

- **Offline open of an un-prepared action** — clear "available online" state, never a broken form.
- **Local-storage / encryption failure** — fail safe: surface a notice, never silently lose the
  in-memory form or crash.
- **Partial queue flush** — one item failing does not block the rest; transient errors retry, stale
  items move to "Needs attention".
- **Device loss** — locally saved drafts are device-bound and not recoverable elsewhere (consistent
  with how held credentials work); this is communicated honestly, not implied as backed up.
- **Duplicate submission** on retry — must not create a duplicate.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: A citizen MUST be able to fill an action with no connectivity and have their entered
  data saved locally so it is not lost when they navigate away or close the app.
- **FR-002**: A citizen MUST be able to reopen a saved draft and find the form restored to exactly
  what they entered.
- **FR-003**: Locally saved work MUST be encrypted at rest on the device.
- **FR-004**: When connectivity is available, the app MUST prepare every pending action so it can be
  opened and filled later without connectivity, and MUST refresh that prepared set when connectivity
  returns.
- **FR-005**: With no connectivity, opening a prepared pending action MUST render its form fully;
  opening an un-prepared action MUST show a clear "available when you're back online" state.
- **FR-006**: Completed actions submitted with no connectivity MUST be queued and submitted
  automatically when connectivity returns, without the citizen having to retry manually.
- **FR-007**: Each draft and queued submission MUST show a clear state (e.g. saved / ready / queued /
  submitted / needs attention).
- **FR-008**: A retry of an already-accepted submission MUST NOT create a duplicate.
- **FR-009**: A queued submission that is no longer applicable when it reaches the server MUST be
  held (not silently discarded), marked "needs attention", and accompanied by an explanation of what
  changed.
- **FR-010**: For a held submission, the citizen MUST be able to discard it or re-open it against the
  current action state, with their captured data retained until they choose.
- **FR-011**: A citizen MUST be able to capture photos/media on an action with no connectivity; the
  media MUST be saved with the draft and visible on reopen.
- **FR-012**: Captured media MUST be submitted as attachments of the action when the submission is
  sent.
- **FR-013**: Media that exceeds the allowed size MUST be rejected/warned at capture time, not at
  submission.
- **FR-014**: Partial failure of a queue flush MUST NOT block other queued items; transient failures
  retry, non-recoverable conflicts move to "needs attention".
- **FR-015**: The feature MUST be available to a citizen in their personal (consumer) capacity and
  MUST NOT require any organisational role.

### Scope Constraints (carried from the design)

- **SCOPE-001**: Closed-app background submission / push (submitting while the app is not open) is
  out of scope; queue flush happens on foreground signals (app open / reconnect).
- **SCOPE-002**: Drafts are device-local only; there is no server-side copy of in-progress work.
- **SCOPE-003**: Browsing/starting a brand-new application from a catalogue is out of scope
  (sub-project B); org-role/platform-tier work is out of scope (sub-project D).
- **SCOPE-004**: Attachment submission reuses the platform's existing file mechanism; this feature
  brings it to the citizen submit path rather than inventing a new one.

### Key Entities

- **Draft** — a citizen's in-progress action: which action it belongs to, the entered form data, any
  captured media, when it was saved, and its state.
- **Prepared action** — a locally cached copy of a pending action's form definition that lets it be
  opened offline; has a freshness/cached-at marker.
- **Queued submission** — a completed action awaiting send: its payload, attachment references, a
  state (queued / submitting / submitted / needs attention), and a reason when held.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A citizen can complete an entire action — open, fill, capture a photo, save — with **no
  connectivity at any point**, and lose **none** of it across an app close/reopen.
- **SC-002**: With no connectivity, a citizen can open **any** of their pending actions that were
  prepared while online (not only ones opened earlier).
- **SC-003**: When connectivity returns, queued submissions are sent **without the citizen taking any
  manual action**, and each reaches a terminal state (submitted or needs-attention).
- **SC-004**: **Zero** captured work is silently lost: every queued submission that cannot be applied
  ends in a "needs attention" state with an explanation, never a silent drop.
- **SC-005**: A retried submission **never** results in a duplicate on the server.
- **SC-006**: A captured photo saved offline is present after an app close/reopen in **100%** of
  cases and is attached to the action when finally submitted.

## Assumptions

- The capabilities to list, open, and submit an action exist (delivered by sub-project A) and are
  reused; this feature adds offline persistence, pre-caching, queuing, conflict handling, and media.
- Local encryption reuses the device-bound mechanism already used for held credentials; consequently
  drafts are not recoverable if the device is lost — communicated honestly.
- Attachment submission reuses the platform's existing file/attachment mechanism; the work is routing
  the citizen submit path through it.
- Queue flushing is driven by foreground connectivity signals (app open / reconnect); closed-app push
  is out of scope.
- Server-side idempotency/replay protection exists and makes a retried submission safe from
  duplication.
- A citizen is signed in to the wallet in their personal capacity.
