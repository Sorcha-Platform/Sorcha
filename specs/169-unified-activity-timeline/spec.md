# Feature Specification: Unified Activity Timeline Read-Path

**Feature Branch**: `169-unified-activity-timeline`

**Created**: 2026-06-27

**Status**: Draft

**Input**: User description: "Unified activity timeline read-path (quick win): add ONE shared responsive ActivityFeed component on web /app + PWA that reads the Inbox spine and shows the combined activity timeline; add an Actionable|Informational category so the bell shows the Actionable subset and the Activity surface shows all; reroute the 2 legacy ActivityEvent producers (PersonaService save/delete, EncryptionBackgroundService complete/fail) to emit into the inbox spine so no events are lost. EXCLUDE /operations Encryption Operations. Do NOT drop the legacy ActivityEvent table or squash migrations (separate tidy run)."

## Overview

Today a signed-in person sees their activity scattered across two unrelated surfaces. The durable **Inbox spine** powers the bell drawer and carries wallet, credential, membership, security, workflow and blueprint-action events. A separate **legacy ActivityEvent log** carries two remaining producers — profile (persona) saves/deletes and background encryption completions/failures — which surface only in older admin-flavoured views and are easy to miss. There is no single "here is everything that has happened to me" timeline, and the bell mixes things that need a response with things that are purely informational.

This feature delivers a **single, shared, responsive Activity timeline** on both the web `/app` host and the Citizen Wallet PWA, reading from the one Inbox spine. It introduces a high-level **Actionable vs Informational** distinction so the bell becomes the "things that need me" surface (Actionable subset) while the Activity timeline shows the complete history (all entries). To guarantee no history is lost in the transition, the two legacy ActivityEvent producers are rerouted to emit into the Inbox spine.

This is scoped as a **read-path quick win**. The legacy ActivityEvent table and its migrations are deliberately retained (a later, separate tidy run will retire them), and the `/operations` Encryption Operations page is explicitly out of scope.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - See my complete activity in one timeline (Priority: P1)

A signed-in person (citizen on the PWA or `/app`, or an org user on `/app`) opens an **Activity** surface and sees a single reverse-chronological timeline of everything that has happened to them — wallet and credential events, membership and security changes, workflow and action notifications, profile saves/deletes, and background encryption outcomes — each with a clear title, summary, timestamp, and (where applicable) a link to the relevant detail.

**Why this priority**: This is the core user value of the feature — one trustworthy place to review history. It is independently demonstrable and delivers value even before the bell behaviour changes or the legacy producers are rerouted (it already unifies the inbox-backed events).

**Independent Test**: Sign in as a user with a mix of existing inbox entries, open the Activity surface on both web `/app` and the PWA, and confirm all entries appear in a single timeline, newest first, with consistent presentation on both hosts and on both narrow (mobile) and wide (desktop) viewports.

**Acceptance Scenarios**:

1. **Given** a user with several inbox entries spanning multiple categories, **When** they open the Activity surface, **Then** all entries are shown in one reverse-chronological list with title, summary, relative timestamp, and a category/severity indicator.
2. **Given** the same user on a mobile-width viewport, **When** they open the Activity surface, **Then** the same timeline renders in a responsive layout without horizontal scrolling or truncation of essential information.
3. **Given** an entry that has an associated detail destination, **When** the user activates that entry, **Then** they are navigated to the corresponding detail view.
4. **Given** the user is on web `/app` and then on the PWA, **When** they view the Activity surface on each, **Then** the same shared component and the same entries are presented (subject to the user's identity), confirming a single shared implementation.

---

### User Story 2 - The bell shows only what needs my attention (Priority: P1)

A signed-in person glances at the notification bell and its unread badge to understand "what needs me right now". The bell drawer shows only **Actionable** entries (things requiring a response or acknowledgement), while purely **Informational** entries (e.g. "profile saved", "encryption complete") no longer crowd the bell. The full set remains available on the Activity timeline.

**Why this priority**: The Actionable/Informational split is the second pillar of the feature and directly improves the signal-to-noise of the most-used notification surface. It is independently testable against the bell and unread count.

**Independent Test**: Seed a user with a mix of Actionable and Informational entries, open the bell drawer, and confirm only Actionable entries appear and the unread badge counts only unread Actionable entries — while the Activity surface continues to show all entries.

**Acceptance Scenarios**:

1. **Given** a user with both Actionable and Informational entries, **When** they open the bell drawer, **Then** only Actionable entries are listed.
2. **Given** the same user, **When** the bell's unread badge is shown, **Then** the count reflects only unread Actionable entries.
3. **Given** the same user, **When** they open the Activity surface, **Then** both Actionable and Informational entries are shown (the Activity surface is not filtered to Actionable).
4. **Given** an entry classified as Actionable, **When** the user acts on or acknowledges it such that it no longer needs attention, **Then** it stops contributing to the bell's unread Actionable count while remaining visible in the Activity timeline history.

---

### User Story 3 - No activity is lost when legacy producers move (Priority: P1)

The two remaining legacy ActivityEvent producers — profile (persona) **save** and **delete**, and background encryption **complete** and **fail** — now record their events into the Inbox spine, so they appear in the unified Activity timeline alongside everything else. No produced event is silently dropped during the change.

**Why this priority**: Without rerouting these producers, the "unified" timeline would be incomplete and the feature would regress visibility of profile and encryption events. Preserving every produced event is a correctness requirement, not a nice-to-have.

**Independent Test**: Trigger a profile save, a profile delete, a successful background encryption, and a failed background encryption; confirm each produces a corresponding entry in the Inbox spine that appears in the Activity timeline with an appropriate title, summary, and Actionable/Informational classification.

**Acceptance Scenarios**:

1. **Given** a user saves their profile, **When** the save completes, **Then** an Informational entry describing the profile save appears in the Activity timeline.
2. **Given** a user deletes their profile, **When** the delete completes, **Then** an entry describing the profile deletion appears in the Activity timeline with appropriate severity.
3. **Given** a background encryption operation completes successfully, **When** it finishes, **Then** an entry describing the successful encryption appears in the Activity timeline for the relevant user.
4. **Given** a background encryption operation fails, **When** the failure is recorded, **Then** an entry describing the failure appears in the Activity timeline, classified so the user is alerted appropriately.
5. **Given** any of the above producers fails to write its inbox entry, **When** the underlying operation (profile save/delete or encryption) is otherwise successful, **Then** the underlying operation is NOT rolled back and the write failure is logged (the inbox write is best-effort, consistent with existing inbox-writer behaviour).

---

### Edge Cases

- **Empty timeline**: A new user with no activity sees a clear, friendly empty state on the Activity surface rather than a blank screen or error.
- **Entry with no detail destination**: Entries that have no associated detail view are presented as non-navigable (no broken link / dead click).
- **Large history**: A user with a long history can review older entries via paging/incremental loading without the surface becoming unresponsive; the timeline does not silently cap visible history without indicating more is available.
- **Real-time arrival**: When a new entry arrives while the user is viewing the bell or Activity surface, it appears (or is reflected in the unread count) without requiring a manual full-page reload, consistent with the existing live-update behaviour of the bell.
- **Classification ambiguity**: An entry whose classification is not explicitly set defaults to a defined fallback (see Assumptions) so it always appears somewhere in the Activity timeline and never disappears.
- **Duplicate suppression**: Re-emission of the same source event (e.g. a retried encryption notification) does not create duplicate timeline entries, consistent with the spine's existing idempotency.
- **Cross-host identity**: The same person on web `/app` and on the PWA sees a consistent timeline for their identity; entries are scoped to the viewing user.

## Requirements *(mandatory)*

### Functional Requirements

#### Shared Activity timeline surface

- **FR-001**: The system MUST provide a single shared Activity timeline component used by both the web `/app` host and the Citizen Wallet PWA, presenting one reverse-chronological (newest-first) list of the signed-in user's activity.
- **FR-002**: The Activity timeline MUST read its entries from the existing Inbox spine (the same durable store that backs the bell drawer) and MUST NOT introduce a separate parallel store for activity.
- **FR-003**: Each timeline entry MUST display, at minimum, a title, a summary, a human-readable timestamp, and a category/severity indicator; where an entry has an associated detail destination, the entry MUST be navigable to that destination.
- **FR-004**: The Activity timeline MUST show the complete set of the user's entries — both Actionable and Informational — and MUST NOT be filtered to only Actionable.
- **FR-005**: The Activity timeline MUST render responsively, remaining usable and legible on both mobile-width and desktop-width viewports.
- **FR-006**: The Activity timeline MUST support reviewing history beyond the most recent entries (incremental loading or paging) and MUST indicate when additional entries are available rather than silently truncating.
- **FR-007**: The Activity timeline MUST present a clear empty state when the user has no activity.

#### Actionable vs Informational classification

- **FR-008**: The system MUST classify every inbox/activity entry into exactly one of two high-level groups: **Actionable** (needs a response or acknowledgement) or **Informational** (awareness only).
- **FR-009**: The bell drawer MUST present only Actionable entries.
- **FR-010**: The bell's unread indicator/badge MUST count only unread Actionable entries.
- **FR-011**: An entry that has no explicitly assigned classification MUST resolve to a defined default classification so that it always appears in the Activity timeline (see Assumptions for the default).
- **FR-012**: The classification MUST be derivable/visible at read time for both the bell (Actionable subset) and the Activity surface (all), without requiring a destructive change to the existing category and severity information already carried by inbox entries.

#### Rerouting legacy producers

- **FR-013**: The profile (persona) **save** and **delete** events MUST be emitted into the Inbox spine so they appear in the unified Activity timeline.
- **FR-014**: The background encryption **complete** and **fail** events MUST be emitted into the Inbox spine so they appear in the unified Activity timeline for the relevant user.
- **FR-015**: Rerouted producer writes MUST be best-effort and MUST NOT roll back or block the underlying operation if the inbox write fails; failures MUST be logged (consistent with the established inbox-writer error-handling pattern).
- **FR-016**: Rerouted entries MUST be assigned an appropriate Actionable/Informational classification (e.g. profile save = Informational; encryption failure surfaced so the user is alerted).
- **FR-017**: The reroute MUST NOT lose events relative to today's behaviour: every situation that currently produces a legacy ActivityEvent for these two producers MUST produce a corresponding Inbox-spine entry after this change.
- **FR-018**: Re-emission of the same underlying source event MUST NOT create duplicate timeline entries (idempotency preserved).

#### Scope guards

- **FR-019**: The legacy ActivityEvent table/store and its database migrations MUST be retained unchanged by this feature (no drop, no migration squash); their retirement is deferred to a separate tidy run.
- **FR-020**: The `/operations` Encryption Operations page MUST be left unchanged and is explicitly out of scope for this feature.

### Key Entities *(include if feature involves data)*

- **Inbox entry (the spine)**: The single durable record of something that happened to a user. Carries identity scope, a category, a severity, occurrence and read/dismiss timestamps, a title, a summary, an optional detail link, a source-event identifier (for idempotency), and channel hints. This feature reads from it for both the bell and the Activity timeline, and adds the Actionable/Informational dimension on top of the existing category/severity.
- **Actionable/Informational classification**: A high-level grouping applied to each inbox entry that determines whether it appears in the bell (Actionable) and how it is emphasised in the timeline. Derivable from the entry's existing category/severity with a defined default.
- **Legacy ActivityEvent**: The retained legacy event log with two remaining producers (profile save/delete, background encryption complete/fail). Its producers are rerouted to the spine; the entity and its migrations are kept in place for a later separate retirement.
- **Activity timeline surface**: The shared, responsive read-only presentation of a user's inbox-spine entries, hosted on both web `/app` and the PWA.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A signed-in user can view their complete activity history in one timeline on both web `/app` and the PWA, with 100% of their inbox-spine entries represented (no category systematically missing).
- **SC-002**: The bell drawer shows only Actionable entries and its unread badge counts only unread Actionable entries; in a seeded mix of Actionable and Informational entries, 0 Informational entries appear in the bell.
- **SC-003**: 100% of events from the two rerouted producers (profile save, profile delete, encryption complete, encryption fail) that occur after the change appear in the unified Activity timeline.
- **SC-004**: No produced event is lost during the transition — for the two rerouted producers, every occurrence that previously produced a legacy event now produces an inbox-spine entry (verified by parity testing).
- **SC-005**: A failure of a rerouted inbox write never causes the underlying profile or encryption operation to fail or roll back (verified by fault-injection of the inbox write).
- **SC-006**: The Activity timeline renders correctly and remains usable on mobile-width and desktop-width viewports on both hosts (no layout breakage, no essential information truncated).
- **SC-007**: The legacy ActivityEvent table and its migrations remain present and unaltered after the change, and the `/operations` Encryption Operations page behaves exactly as before.
- **SC-008**: The Activity timeline is implemented by a single shared component (one implementation referenced by both hosts), not duplicated per host.

## Assumptions

- **Spine is authoritative for the read-path**: The existing Inbox spine (the durable inbox that backs the bell, Feature 118 / 151) is the single source the unified Activity timeline reads from. This feature does not migrate historical legacy ActivityEvent rows into the spine; it reroutes the two live producers going forward. Historical legacy rows remain only in the legacy log until the separate tidy run.
- **Default Actionable/Informational mapping**: Unless the entry explicitly indicates otherwise, entries are classified as **Actionable** when they require user response or acknowledgement (e.g. an available workflow action, or a high-urgency security/critical event), and **Informational** otherwise (e.g. profile saved, credential received notice, encryption complete). The default fallback for an unclassified entry is **Informational** so it always appears in the Activity timeline but does not crowd the bell. The precise mapping from existing categories/severities to the two groups is to be finalised in planning; security-critical alerts should resolve to Actionable so they are not hidden from the bell.
- **Identity scope**: The timeline is scoped to the signed-in user, consistent with the existing per-user inbox read endpoint. Org-wide / admin aggregate activity views are out of scope.
- **Reuse of existing infrastructure**: This feature reuses the existing inbox read API, inbox-writer pattern, and the bell/live-update mechanism rather than introducing new transport. The shared component lives with the other shared user-facing components (per the project's shared user-facing component-library convention) so both the web host and the PWA consume the same implementation.
- **Encryption event recipient**: The "relevant user" for a background encryption complete/fail entry is the user on whose behalf the operation ran (the operation's owner/initiator).
- **Best-effort writes**: Rerouted producer writes follow the established best-effort inbox-writer convention (wrapped, logged on failure, never rolling back the underlying operation).
- **No legacy schema change**: Per the explicit instruction, no table is dropped and no migrations are squashed in this feature; that is a separate tidy run.
- **`/operations` excluded**: The web-only `/operations` Encryption Operations page is unrelated to this read-path unification and is left untouched.
