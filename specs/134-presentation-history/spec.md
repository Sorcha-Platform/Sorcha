# Feature Specification: Cross-Device Citizen Presentation History

**Feature Branch**: `134-presentation-history`
**Created**: 2026-05-20
**Status**: Draft
**Input**: F114 US5 PR3 — cross-device citizen presentation history. Source design: `docs/superpowers/specs/2026-05-20-f114-us5-offline-presentation-reconciliation-design.md`.

> **Context**: This is PR3 of Feature 114 User Story 5. PR1 shipped the citizen's on-device activity log; PR2 shipped the transport that reports those entries to the platform (currently accepted and discarded). This feature gives those reported entries a durable, citizen-owned home so the citizen's presentation history follows them across devices. It also reconciles a stale earlier design: a reported presentation is **citizen-owned convenience data**, not a ledger/register event — see the source design for why the original "register-writing consumer" model was structurally impossible.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - See my presentation history on a new device (Priority: P1)

A citizen has shown credentials to verifiers from their phone (e.g. presenting an identity credential to a council). Later they pair a second device — a new phone, or a desktop. When they open their activity history on the new device, they see the presentations they made earlier from the first device.

**Why this priority**: This is the entire point of the feature. Without it, the citizen's history is trapped on whichever device made each presentation, and a lost or replaced device loses the record. PR2 already moves the data off-device; this story makes that investment visible to the citizen.

**Independent Test**: Make a presentation on device A, let it synchronise, pair device B, open the activity history on B — the presentation from A appears.

**Acceptance Scenarios**:

1. **Given** a citizen made a presentation on device A that has synchronised, **When** they open the activity history on a freshly-paired device B, **Then** that presentation is listed (credential, verifier label, disclosed claim names, time, outcome).
2. **Given** a citizen has made no presentations, **When** they open the activity history on any device, **Then** they see an empty history, not an error.
3. **Given** a presentation was reported more than once (e.g. a retry after a network blip), **When** the citizen views their history, **Then** it appears exactly once.

---

### User Story 2 - Remove a presentation from my history everywhere (Priority: P2)

A citizen wants to remove an entry from their presentation history. They delete it from one device, and it is gone from their history on all their devices and does not come back.

**Why this priority**: History the citizen cannot prune is a privacy liability. PR1 shipped a per-row delete; now that history is cross-device, delete must behave coherently across devices rather than silently reappearing on the next sync.

**Independent Test**: Delete an entry on device B, then open the activity history on device A — the entry is absent and stays absent through subsequent syncs.

**Acceptance Scenarios**:

1. **Given** a synchronised presentation appears in the citizen's history, **When** they delete it on one device, **Then** it disappears from that device and from every other device, and does not reappear after later synchronisation.
2. **Given** the citizen deletes an entry, **When** they read the delete confirmation messaging, **Then** it states the entry is removed from their history on all their devices and that this does not affect the verifier's own records.
3. **Given** an entry exists only on the device that made it and has not yet synchronised, **When** the citizen deletes it, **Then** it is removed locally with no adverse effect.

---

### User Story 3 - Immediate, consistent history without flicker (Priority: P3)

A citizen makes a presentation and immediately checks their activity. The just-made presentation is there at once, before any network round-trip. After it synchronises, it still appears exactly once — it does not duplicate or visibly flicker between a local copy and a server copy.

**Why this priority**: Instant feedback is expected UX, and the cross-device merge must not introduce duplicates or disappearing/reappearing rows. This story protects the quality of the merge that US1 and US2 depend on.

**Independent Test**: Make a presentation while offline; confirm it appears in activity immediately; restore connectivity; after sync, confirm it still appears exactly once.

**Acceptance Scenarios**:

1. **Given** a citizen has just made a presentation on a device, **When** they open the activity history on that device before any synchronisation, **Then** the presentation is listed.
2. **Given** that presentation later synchronises to the platform, **When** the citizen views the history again, **Then** it appears exactly once.

---

### Edge Cases

- **Reported twice**: the same presentation reported more than once produces a single history entry (identity is the citizen's reported entry id).
- **Stale local copy after remote delete**: a device that still holds a local copy of an entry deleted from another device must not resurface it in the displayed history.
- **Delete of an unsynchronised-only entry**: removed locally; no platform record exists yet to remove.
- **Cross-citizen access**: a request to read or delete another citizen's entry is indistinguishable from the entry not existing.
- **Empty history**: returns an empty list, never an error.
- **Rare lost report**: if a single report fails to land on the platform after the device considered it delivered, that entry may be missing from cross-device history. This is acceptable for convenience-grade history (see Assumptions); the citizen's own device still shows it locally.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST retain a durable, per-citizen record of each presentation the citizen reports having made, capturing the credential identifier, the verifier's display label, the **names** of the disclosed claims, the time of presentation, and the observed outcome.
- **FR-002**: A presentation record MUST NOT contain the **values** of any disclosed claim — only the claim names. (Mirrors the privacy contract of the on-device log.)
- **FR-003**: When a citizen opens their activity history on any device they have paired, the system MUST present all presentations they have reported from any of their devices.
- **FR-004**: The citizen's reported entry identifier MUST be the unit of identity; reporting the same presentation more than once MUST NOT create duplicate history entries.
- **FR-005**: A citizen MUST be able to delete a presentation from their history. Once deleted, it MUST NOT appear on any of their devices and MUST NOT reappear after subsequent synchronisation.
- **FR-006**: A citizen MUST be able to read and delete only their own presentation history. Access to another citizen's entries MUST be indistinguishable from the entry not existing.
- **FR-007**: A presentation the citizen has just made MUST appear in their activity history on that device immediately, before any platform synchronisation.
- **FR-008**: After a presentation has synchronised, the activity history MUST show it exactly once — no duplication between the device-local copy and the platform copy.
- **FR-009**: The delete confirmation messaging MUST state that the entry is removed from the citizen's history on all their devices and that this does not affect the verifier's own records.
- **FR-010**: The system MUST NOT write any register/ledger transaction for a reported presentation. These are citizen-owned convenience records, not on-register lifecycle events.
- **FR-011**: Presentation history MUST survive a device being un-paired and re-paired, and MUST survive one device clearing its local data — the platform copy is authoritative for cross-device history.

### Key Entities *(include if feature involves data)*

- **Presentation history record**: One presentation the citizen reported. Belongs to a single citizen. Attributes: reported entry identifier (identity), credential identifier, verifier display label, disclosed claim names, time presented, outcome. Holds no claim values and no register correlation.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A citizen who made a presentation on one device sees it in their history on a newly-paired device on the first open of the activity history (after that device's initial synchronisation).
- **SC-002**: A presentation reported multiple times appears exactly once in history (0 duplicates).
- **SC-003**: A deleted presentation does not reappear in history on any device across subsequent synchronisations (0 reappearances).
- **SC-004**: No register/ledger transaction is produced for any reported presentation (0 register writes).
- **SC-005**: A citizen can only ever see their own presentation history (0 cross-citizen disclosures).
- **SC-006**: A just-made presentation is visible in the device's activity history with no network round-trip required.

## Assumptions

- **No originating register**: free-standing offline presentations (to a reference verifier or in person) gate no Sorcha workflow action and therefore have no originating register; there is no ledger record to anchor to. (Central finding of the source design.)
- **Convenience-grade delivery**: a rare lost report is acceptable; the feature does not promise exactly-once delivery of every reported presentation to the platform. Stronger (outbox-grade) delivery is explicitly out of scope.
- **Identity & pairing**: citizen identity and device pairing are provided by existing Feature 114 enrolment; this feature does not introduce new identity or pairing mechanics.
- **Disclosed claim names only**: consistent with the on-device log shipped in PR1 and the report contract shipped in PR2.

## Dependencies

- **PR2 (shipped)**: the report transport — the device-to-platform reporting path, its de-duplication, and the forwarding seam — already exists. This feature gives that seam a durable destination and adds the read/delete surface.
- **Feature 114 enrolment (shipped)**: citizen authentication and multi-device pairing.

## Out of Scope

- Any Blueprint Service change, presentation consumer, or register/ledger transaction for reported presentations.
- Verifier-organisation visibility into citizen presentations.
- Exactly-once / outbox-grade delivery guarantees.
- Pagination of history beyond a simple cap.
