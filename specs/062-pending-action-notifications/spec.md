# Feature Specification: Pending Action Notifications & User Communications

**Feature Branch**: `062-pending-action-notifications`
**Created**: 2026-03-17
**Status**: Draft
**Input**: User description: "Pending Action Notifications — transform raw transaction events into meaningful user-facing pending action notifications with blueprint-defined templates, a pending action inbox, and real-time delivery"

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Real-Time Pending Action Alert (Priority: P1)

A blueprint participant receives a notification when another participant completes an action that requires their response. The notification appears in real-time within the UI, presenting the action in business terms — not as a raw transaction.

**Example**: Sarah, a quality inspector, sees a toast notification: "Inspection requested by Acme Logistics — Order #4421, Batch 7B" with a button to "Review & Sign". She clicks it and lands directly on the action page.

**Why this priority**: This is the core value proposition. Without meaningful real-time notifications, users must manually poll for pending work. Every multi-participant blueprint depends on timely action awareness.

**Independent Test**: Can be tested by submitting an action in a two-participant blueprint and verifying the second participant sees a meaningful notification within 5 seconds.

**Acceptance Scenarios**:

1. **Given** a two-participant blueprint where Participant A completes Action 1, **When** the transaction is confirmed, **Then** Participant B sees a notification within 5 seconds containing the blueprint name, action title, sender's display name, and a summary extracted from the transaction payload.
2. **Given** a notification is displayed, **When** the user clicks the notification CTA, **Then** they navigate directly to the pending action page with the action details pre-loaded.
3. **Given** a notification is received while the user is on a different page, **When** the notification arrives, **Then** a non-intrusive toast/badge appears without disrupting the user's current work.
4. **Given** the user is offline when the action arrives, **When** they reconnect, **Then** they receive all missed notifications in chronological order.

---

### User Story 2 - Pending Action Inbox (Priority: P1)

Users have a dedicated inbox showing all their pending actions across all blueprints and registers. Actions are grouped by blueprint, sorted by urgency, and show meaningful business context rather than transaction hashes.

**Example**: David opens his inbox and sees: "3 pending inspections (Acme Logistics)", "1 delivery confirmation (Global Shipping)", "1 approval needed — urgent, due in 2 hours (Finance Team)". Each group expands to show individual actions with CTAs.

**Why this priority**: Equal to real-time alerts — users need a single place to see everything pending. The toast notification draws attention; the inbox provides the complete picture.

**Independent Test**: Can be tested by creating multiple blueprint instances with pending actions for a user, then verifying the inbox displays them grouped and sorted correctly.

**Acceptance Scenarios**:

1. **Given** a user has 5 pending actions across 3 different blueprints, **When** they open the pending action inbox, **Then** actions are grouped by blueprint with counts, sorted by urgency (deadline-approaching first).
2. **Given** a pending action has a deadline defined in the blueprint, **When** the deadline is within 4 hours, **Then** the action shows an "urgent" badge with time remaining.
3. **Given** the user completes a pending action, **When** they return to the inbox, **Then** the completed action is removed or moved to a "completed" section.
4. **Given** the inbox is open, **When** a new pending action arrives, **Then** it appears at the top of the appropriate group without requiring a page refresh.
5. **Given** a user has no pending actions, **When** they view the inbox, **Then** they see an empty state with a helpful message.

---

### User Story 3 - Blueprint-Defined Notification Content (Priority: P2)

Blueprint designers configure how notifications appear for each action — defining summary templates, urgency rules, and grouping keys. This ensures notifications show business-relevant information rather than generic descriptions.

**Example**: A blueprint designer adds notification configuration to the "Quality Inspection" action: summary template shows order reference and batch ID from the payload, urgency is based on the inspection deadline field, and notifications group by supplier.

**Why this priority**: Depends on the notification infrastructure from P1 stories. Adds the "smart" layer that makes notifications genuinely useful rather than generic.

**Independent Test**: Can be tested by creating a blueprint with notification templates, executing a workflow, and verifying the resulting notification matches the template output.

**Acceptance Scenarios**:

1. **Given** a blueprint action defines a summary template referencing payload fields, **When** a notification is generated for that action, **Then** the summary displays the actual values from the transaction payload (e.g., "ORD-4421 — Batch 7B").
2. **Given** a blueprint action defines a deadline field reference, **When** a notification is generated, **Then** the urgency is calculated from the deadline proximity (urgent if within 4 hours, warning if within 24 hours, normal otherwise).
3. **Given** a blueprint action defines a grouping key, **When** a user has multiple pending actions with the same grouping value, **Then** they appear as a single grouped entry showing the count.
4. **Given** a blueprint action has no notification configuration, **When** a notification is generated, **Then** sensible defaults are used (blueprint title + action title as summary, no urgency, no grouping).

---

### User Story 4 - Notification Preferences (Priority: P2)

Users control how they receive notifications — choosing between real-time alerts, periodic digests, or quiet mode. Preferences are per-user and can be adjusted at any time.

**Why this priority**: The delivery infrastructure exists but preferences are hardcoded. This enables users to manage notification volume according to their work patterns.

**Independent Test**: Can be tested by changing a user's preference from real-time to digest, then verifying subsequent notifications are batched rather than delivered immediately.

**Acceptance Scenarios**:

1. **Given** a user sets their preference to "digest" with hourly frequency, **When** 5 actions arrive within the hour, **Then** they receive a single digest notification summarising all 5 actions.
2. **Given** a user sets their preference to "real-time", **When** an action arrives, **Then** they receive an immediate notification.
3. **Given** a user sets their preference to "quiet" (do not disturb), **When** actions arrive, **Then** they accumulate silently in the inbox without alerts.
4. **Given** a user changes their preference, **When** the next notification triggers, **Then** it follows the new preference immediately.

---

### User Story 5 - Notification History & Catch-Up (Priority: P3)

Notification history is persisted so users can review past notifications and catch up after being away. The system replays missed notifications on reconnect.

**Why this priority**: Nice-to-have for reliability. The core inbox and real-time alerts work without persistence, but users who've been offline for extended periods benefit from durable history.

**Independent Test**: Can be tested by generating notifications while a user is disconnected, then reconnecting and verifying all missed notifications appear.

**Acceptance Scenarios**:

1. **Given** a user was offline for 2 hours during which 8 actions arrived, **When** they reconnect, **Then** they see all 8 notifications in the inbox, ordered chronologically.
2. **Given** a user wants to review past notifications, **When** they access notification history, **Then** they can scroll through the last 30 days of notifications.
3. **Given** the platform restarts, **When** a user reconnects, **Then** their notification history is intact (not lost with volatile storage restart).

---

### Edge Cases

- What happens when a notification references a blueprint that has been deleted or archived? Display last-known blueprint name with a "blueprint unavailable" indicator.
- How does the system handle a user who belongs to multiple organisations receiving actions across all of them? Unified inbox with org context shown per notification.
- What happens when a blueprint action's summary template references a payload field that doesn't exist in the transaction? Fall back to the action title; log a warning for blueprint designers.
- How does the inbox behave when a pending action is completed by another delegate of the same wallet? Remove from inbox and show a brief "completed by [delegate name]" note.
- What happens when thousands of notifications arrive in a burst (e.g., recovery sync)? Rate-limit UI rendering, batch updates, show "catching up..." indicator.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST deliver pending action notifications to the correct user within 5 seconds of transaction confirmation
- **FR-002**: Notifications MUST display the blueprint name, action title, sender display name, and a payload-derived summary
- **FR-003**: System MUST provide a pending action inbox showing all pending actions grouped by blueprint
- **FR-004**: Inbox MUST sort actions by urgency (deadline-approaching first, then chronological)
- **FR-005**: System MUST support blueprint-defined notification templates with summary extraction, urgency calculation, and grouping rules
- **FR-006**: Notification templates MUST gracefully handle missing payload fields by falling back to default text
- **FR-007**: System MUST allow users to navigate directly from a notification to the relevant action page
- **FR-008**: System MUST remove or mark completed actions when the user or a delegate completes them
- **FR-009**: System MUST support three notification delivery modes: real-time, digest, and quiet
- **FR-010**: System MUST deliver missed notifications when a user reconnects after being offline
- **FR-011**: System MUST persist notification history for at least 30 days
- **FR-012**: System MUST show urgency indicators (urgent, warning, normal) based on deadline proximity
- **FR-013**: System MUST update the inbox in real-time when new actions arrive (no manual refresh required)
- **FR-014**: System MUST handle burst notifications (>100 in quick succession) without UI degradation
- **FR-015**: Digest notifications MUST group actions by blueprint and include counts and summaries

### Key Entities

- **PendingActionNotification**: Represents a pending action presented to a user — contains blueprint context, action details, payload summary, urgency level, sender info, timestamp, read/unread state, and navigation link
- **NotificationTemplate**: Blueprint-level configuration defining how an action's notifications appear — summary template with payload field references, urgency rule (deadline field + thresholds), and grouping key
- **NotificationPreference**: Per-user delivery preference — mode (real-time/digest/quiet), digest frequency (hourly/daily), and quiet hours schedule
- **NotificationHistory**: Persisted record of delivered notifications — enables catch-up after reconnect, audit trail, and history browsing with 30-day retention

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Users see pending action notifications within 5 seconds of the triggering transaction being confirmed
- **SC-002**: 95% of users can identify the required action from the notification content alone, without navigating to the action page first
- **SC-003**: Average time from notification receipt to action completion reduces by 50% compared to manual dashboard polling
- **SC-004**: Pending action inbox loads within 2 seconds regardless of the number of pending actions (up to 500)
- **SC-005**: Notification delivery succeeds for 99.9% of events during normal operation
- **SC-006**: Users who reconnect after being offline receive 100% of missed notifications within 10 seconds
- **SC-007**: Blueprint designers can configure notification templates without developer assistance

## Assumptions

- Users are authenticated and have active WebSocket connections when receiving real-time notifications
- Blueprint designers will adopt notification template configuration gradually; sensible defaults must work without any template configuration
- The existing bloom filter + gRPC notification pipeline is reliable; this feature builds on top of it rather than replacing it
- Digest frequency options are limited to: immediate (real-time), hourly, and daily
- Quiet mode still shows a badge count on the inbox icon but suppresses toast/sound alerts
- Multi-org users see a unified inbox across all their organisations, with org context shown per notification
