# Feature Specification: PWA Citizen Workflow Inbox

**Feature Branch**: `151-citizen-workflow-inbox`

**Created**: 2026-06-13

**Status**: Draft

**Input**: User description: "PWA citizen workflow inbox (sub-project A): consumer-tier Things-to-do inbox in the Citizen Wallet PWA listing the actions currently waiting on the citizen, an In-review banner from the existing pending-application notice, and a live nav count badge. Tapping an action routes into the existing fill-and-submit flow. No backend changes; consumer-tier only; reuse the shared form renderer."

**Source design**: `docs/superpowers/specs/2026-06-13-pwa-citizen-workflow-inbox-design.md` (sub-project A of the PWA workflow-participation programme A/B/C/D).

## User Scenarios & Testing *(mandatory)*

A citizen uses the wallet on their phone today to hold and present credentials. When a workflow
needs something from them — for example, completing or progressing an application they are part of
— there is currently no way for them to discover that on the phone; they must already hold a direct
link. This feature gives the citizen a place to see and act on the things waiting for them.

### User Story 1 - Discover and complete an action waiting on me (Priority: P1)

A citizen opens the wallet, sees a "Things to do" area listing the actions that are currently
waiting on **them** (it is their turn), taps one, fills in the form (including any data or details
requested), submits it, and returns to find the item cleared from the list.

**Why this priority**: This is the whole point of the feature and the minimum viable slice. Without
discovery, the citizen cannot participate in a workflow on the phone at all. With just this story,
a citizen can find their outstanding work and complete it end-to-end — a complete, demonstrable
value loop on its own.

**Independent Test**: With a citizen who has at least one outstanding action, open the inbox, see
the action listed with a meaningful title, open it, complete and submit the form, and confirm the
action no longer appears. Fully testable without the count badge or the in-review banner.

**Acceptance Scenarios**:

1. **Given** a signed-in citizen with one or more actions currently awaiting their input, **When**
   they open the "Things to do" inbox, **Then** they see each such action listed with a meaningful
   title and, where present, a due date and an urgency indicator.
2. **Given** the inbox is showing an action, **When** the citizen taps it, **Then** they are taken
   into the existing form for that action and can fill and submit it.
3. **Given** the citizen has just submitted an action, **When** they return to the inbox, **Then**
   that action is no longer listed and the list reflects their current outstanding work.
4. **Given** a citizen has no actions awaiting their input, **When** they open the inbox, **Then**
   they see a clear, friendly "nothing needs you right now" state rather than an empty or broken
   screen.
5. **Given** a workflow action belongs to a different participant in an instance the citizen is also
   part of, **When** the citizen opens the inbox, **Then** that other participant's action is **not**
   shown as if it were the citizen's.

---

### User Story 2 - Know at a glance how many things need me (Priority: P2)

A citizen can see, from the wallet's navigation, a count of how many actions are currently waiting
on them, and that count updates on its own while the app is open when a new action arrives or after
they complete one.

**Why this priority**: Strongly improves the "do I need to act?" awareness that makes the inbox
useful day-to-day, but the inbox is fully usable without it. Builds directly on Story 1.

**Independent Test**: With a citizen who has a known number of outstanding actions, confirm the
navigation shows that number; trigger a new action while the app is open and confirm the number
increases without a manual refresh; complete an action and confirm it decreases.

**Acceptance Scenarios**:

1. **Given** a citizen with N actions awaiting their input, **When** they look at the wallet
   navigation, **Then** they see the count N (and no badge when N is zero).
2. **Given** the app is open, **When** a new action becomes the citizen's to do, **Then** the count
   updates on its own within a few seconds without the citizen refreshing.
3. **Given** the citizen completes an action, **When** the submission succeeds, **Then** the count
   decreases to reflect the remaining work.

---

### User Story 3 - See what I've submitted and is in review (Priority: P3)

A citizen who has submitted an application can see a lightweight indication that it has been
received and is in review / awaiting another party, so they understand that there is nothing more
for them to do right now.

**Why this priority**: Reduces "did it go through / what now?" anxiety, but is informational only
and reuses an existing signal. Lowest priority of the three.

**Independent Test**: With a citizen who has submitted an application that is awaiting another
party, open the inbox and confirm an "in review" indication is shown distinctly from the actions
that still need them.

**Acceptance Scenarios**:

1. **Given** a citizen has submitted an application that is now awaiting another party, **When**
   they open the inbox, **Then** they see an "in review" indication that is visually distinct from
   the "needs you" actions.
2. **Given** there is nothing in review, **When** the citizen opens the inbox, **Then** no "in
   review" indication is shown.

---

### Edge Cases

- **Refresh failure / no connectivity**: When the list cannot be refreshed (transient network
  failure), the citizen sees a non-blocking notice and the last-known list is retained — never a
  blank or broken screen. (Persistent offline working is a later sub-project.)
- **Stale action**: If an action was already completed elsewhere (e.g. on another device) and the
  citizen opens it, the system explains there is nothing to do for that item rather than presenting
  a broken or empty form.
- **Action arrives while viewing**: A new action becoming the citizen's to do while the inbox is
  open is reflected without requiring the citizen to manually reload.
- **Large number of outstanding actions**: The list remains usable when the citizen has many
  outstanding actions (ordered so the most pressing surface first).
- **Mistaken identity of work**: Only actions where the citizen is the designated actor appear;
  actions assigned to other participants of a shared workflow never appear in the citizen's inbox.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The wallet MUST provide a "Things to do" inbox that lists the workflow actions
  currently awaiting the signed-in citizen's input (i.e. actions where it is the citizen's turn to
  act).
- **FR-002**: Each listed action MUST display a meaningful title and, where available, a due date
  and an urgency indicator; the list MUST be ordered so the most pressing actions surface first.
- **FR-003**: The inbox MUST exclude actions that belong to other participants of a workflow the
  citizen merely shares, showing only the citizen's own outstanding actions.
- **FR-004**: Tapping a listed action MUST take the citizen into the existing action form, where
  they can enter the requested data and submit, using the wallet's existing fill-and-submit
  capability unchanged.
- **FR-005**: After a successful submission, the citizen MUST be returned to the inbox and the inbox
  MUST reflect their updated outstanding work (the completed action no longer listed).
- **FR-006**: The wallet navigation MUST show a count of the citizen's outstanding actions, with no
  badge shown when the count is zero.
- **FR-007**: While the wallet is open, the count and list MUST update on their own (without a
  manual refresh) when a new action becomes the citizen's to do or when the citizen completes one.
- **FR-008**: The inbox MUST present a clear, friendly empty state when the citizen has no
  outstanding actions.
- **FR-009**: The inbox MUST show a distinct, lightweight "in review" indication when the citizen
  has a submitted application awaiting another party, reusing the existing post-submission signal.
- **FR-010**: On a transient failure to refresh the list, the wallet MUST retain the last-known list
  and surface a non-blocking notice, never a blank or error screen.
- **FR-011**: When the citizen opens an action that is no longer outstanding (already completed
  elsewhere), the wallet MUST explain there is nothing to do rather than present a broken form.
- **FR-012**: The feature MUST be available to a citizen acting in their personal (consumer)
  capacity and MUST NOT require the citizen to hold any organisational role or permission.

### Scope Constraints (carried from the design)

- **SCOPE-001**: This feature MUST NOT require changes to back-end services; it builds entirely on
  capabilities that already serve a citizen acting in their personal capacity.
- **SCOPE-002**: Browsing and starting a brand-new application from a catalogue is out of scope
  (sub-project B).
- **SCOPE-003**: Saving an unfinished action to resume later, working offline, and capturing
  photos/media are out of scope (sub-project C).
- **SCOPE-004**: Performing actions in an organisational role is out of scope (sub-project D).

### Key Entities

- **Outstanding action ("thing to do")**: A workflow action currently awaiting the citizen's input.
  Key attributes a citizen cares about: which application/workflow it belongs to, a human-readable
  title, an optional due date, and an urgency indicator.
- **Outstanding-action count**: The number of outstanding actions for the citizen, surfaced in
  navigation.
- **In-review indication**: A lightweight signal that the citizen has submitted something now
  awaiting another party (informational; reuses the existing post-submission notice).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A citizen with outstanding actions can, starting from opening the wallet, find and
  open the right action **without needing a direct link or knowing any identifier** — i.e. discovery
  is self-service.
- **SC-002**: 100% of the actions shown in a citizen's inbox are ones where it is the citizen's turn
  to act; **zero** actions belonging to other participants appear (correctness of "my turn").
- **SC-003**: A citizen can go from opening the inbox to a submitted action in **under 2 minutes**
  for a typical single-form action (excluding the time they spend entering their own data).
- **SC-004**: When a new action becomes the citizen's to do while the wallet is open, the navigation
  count reflects it **within 10 seconds** without a manual refresh.
- **SC-005**: On a transient refresh failure, **no** citizen sees a blank or error screen; the
  last-known list is retained in 100% of such cases.
- **SC-006**: After a successful submission, the completed action disappears from the inbox on the
  citizen's return **every time** (no stale entries).

## Assumptions

- The capability to list "actions currently awaiting me" and to count them already exists and serves
  a citizen acting in their personal (consumer) capacity; this feature consumes it without change.
- The capability to open, fill, and submit a single action already exists in the wallet and is
  reused unchanged; this feature only adds discovery and navigation around it.
- The "in review" indication reuses the existing post-submission notice (a single, lightweight
  signal). A complete "all my workflows and their states" tracker is a later sub-project.
- Live updates while the wallet is open ride on the wallet's existing real-time signal; background
  notifications when the wallet is closed are out of scope here.
- "Most pressing first" ordering uses the urgency indicator and due date already associated with an
  action; a separately computed urgent count is not required for this feature.
- A citizen is signed in to the wallet in their personal capacity; session handling and sign-in are
  existing behaviour and out of scope.
