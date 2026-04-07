# Feature Specification: SignalR Minimal Disclosure & Notification Fix

**Feature Branch**: `089-signalr-minimal-disclosure`
**Created**: 2026-04-07
**Status**: Draft
**Input**: User description: "SignalR minimal disclosure notification fix - thin signal payloads, remove instance group broadcasting, close service token authz loophole, fix notification delivery bug"
**Design Spec**: `docs/superpowers/specs/2026-04-07-signalr-minimal-disclosure-design.md`

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Agent Receives Real-Time Action Signals (Priority: P1)

An autonomous agent actor connected via SignalR receives a thin signal when a new action becomes available for its wallet, triggering an immediate data pull instead of waiting for the next 30-second polling cycle. This fixes the current bug where agents never receive real-time notifications and rely entirely on polling.

**Why this priority**: This is the core delivery bug. Without this fix, all real-time notification features are non-functional for agents. Sub-second response times are critical for walkthrough demonstrations and production workflows.

**Independent Test**: Deploy two agents in Docker. Agent A executes an action whose routing targets Agent B. Agent B receives a thin signal within 2 seconds and pulls updated instance state, without relying on the 30s polling cycle.

**Acceptance Scenarios**:

1. **Given** Agent B is connected to ActionsHub and subscribed to its wallet, **When** Agent A completes an action that routes to Agent B, **Then** Agent B receives a signal containing only `signalType`, `instanceId`, `correlationId`, and `timestamp` within 2 seconds.
2. **Given** Agent B receives an `action-available` signal, **When** it processes the signal, **Then** it immediately polls the instance endpoint using the `instanceId` from the signal to retrieve full action details.
3. **Given** a participant has no linked wallet address, **When** an action is routed to them, **Then** the system logs a warning and no signal is sent (the participant's client falls back to polling).

---

### User Story 2 - Notification Payloads Disclose Minimal Information (Priority: P1)

All SignalR notification payloads are reduced to thin triggers containing only the signal type, instance identifier, and optional correlation ID. No workflow metadata (action titles, participant names, blueprint IDs, transaction hashes) is transmitted over SignalR. Clients pull details through authenticated endpoints.

**Why this priority**: Equal to P1 because over-disclosure is a security issue. Current payloads leak business logic, participant identity, and workflow structure to anyone observing the WebSocket connection.

**Independent Test**: Capture SignalR traffic during a workflow execution. Verify that no notification payload contains action titles, participant identifiers, wallet addresses, blueprint names, or transaction hashes.

**Acceptance Scenarios**:

1. **Given** an action is completed and the next participant is notified, **When** the SignalR message is sent, **Then** the payload contains only `signalType: "action-available"`, `instanceId`, `correlationId`, and `timestamp`.
2. **Given** an action is rejected, **When** the rejection signal is sent, **Then** the payload contains only `signalType: "action-rejected"`, `instanceId`, `correlationId`, and `timestamp` — no rejection reason, no target participant ID.
3. **Given** an encryption operation is in progress, **When** progress signals are sent, **Then** the payload contains only `operationId`, `percentComplete`, `status`, and `timestamp` — no recipient names, no disclosed field summaries.
4. **Given** the EventsHub detects an inbound action, **When** the signal is sent to the user group, **Then** the payload contains only `signalType: "inbound-action"`, `instanceId`, `correlationId`, and `timestamp` — no blueprint name, sender name, navigation path, summary, urgency, or deadline.

---

### User Story 3 - Service Token Authorization Enforcement (Priority: P1)

Service tokens connecting to the ActionsHub must include an `org_id` claim. Tokens without this claim are rejected, closing a backward-compatibility loophole that allowed unscoped wallet subscriptions.

**Why this priority**: Security hardening. An unscoped service token could subscribe to any wallet and passively monitor all workflow activity.

**Independent Test**: Attempt to connect a service token without `org_id` claim to ActionsHub and subscribe to a wallet. Verify the subscription is rejected.

**Acceptance Scenarios**:

1. **Given** a service token without an `org_id` claim, **When** it attempts to subscribe to a wallet address, **Then** the subscription is rejected with an authorization error.
2. **Given** a service token with a valid `org_id` claim, **When** it subscribes to a wallet address, **Then** the subscription succeeds as before.

---

### User Story 4 - Instance Group Broadcasting Removed (Priority: P2)

The `instance:{id}` SignalR group is no longer used for broadcasting. All action-related signals are delivered exclusively through `wallet:{address}` groups, which have ownership validation.

**Why this priority**: Eliminates a signals intelligence risk. The instance group had no access control — anyone with a valid JWT and a known instance ID could observe workflow activity patterns.

**Independent Test**: Subscribe to an `instance:{id}` group and verify no signals are received during workflow execution. Verify that the same signals arrive on the `wallet:{address}` group instead.

**Acceptance Scenarios**:

1. **Given** the notification service sends an `action-available` signal, **When** the signal is dispatched, **Then** it is sent only to the `wallet:{address}` group, not to any `instance:{id}` group.
2. **Given** a workflow completes, **When** the completion signal is dispatched, **Then** it is sent to the `wallet:{address}` groups of all participants who were involved, not to the `instance:{id}` group.

---

### User Story 5 - Connection Resilience with Polling Fallback (Priority: P2)

When the SignalR connection drops, the client uses retry policies to reconnect. During disconnection, the existing polling cycle continues to ensure no signals are missed. On reconnection, an immediate poll catches anything missed during the gap.

**Why this priority**: Reliability. Real-time signals are the fast path, but polling is the safety net. Both must always be active.

**Independent Test**: Disconnect an agent's SignalR connection mid-workflow. Verify the agent continues to discover pending actions via polling. Reconnect and verify an immediate poll fires.

**Acceptance Scenarios**:

1. **Given** the agent's SignalR connection drops, **When** the connection is lost, **Then** the client uses retry policies with exponential backoff to reconnect.
2. **Given** the agent is disconnected, **When** a new action becomes available, **Then** the agent discovers it via the next polling cycle (within 30 seconds).
3. **Given** the agent reconnects after a disconnection, **When** the connection is re-established, **Then** the agent immediately polls for any missed actions before resuming signal-driven mode.

---

### User Story 6 - UI Receives Thin Signals and Refreshes (Priority: P3)

The Blazor UI receives thin signals and refreshes its views by pulling data from authenticated endpoints. Notification toasts show generic messages with links rather than displaying workflow metadata from the signal payload.

**Why this priority**: Lower priority because the UI currently has limited real-time notification integration. The agent path (P1) is the critical fix.

**Independent Test**: Trigger a workflow action in the UI. Verify the notification toast shows a generic message and that clicking it loads full details from the API.

**Acceptance Scenarios**:

1. **Given** the UI receives an `action-available` signal, **When** the signal is processed, **Then** a notification toast shows a generic message (e.g., "New action available") with a navigation link.
2. **Given** the UI receives an encryption progress signal, **When** the signal is processed, **Then** the progress bar updates using the inline `percentComplete` and `status` values.

---

### Edge Cases

- What happens when a participant has multiple linked wallets? Signal is sent to all linked wallet groups.
- What happens when the pull-back endpoint is unavailable after receiving a signal? Client retries the pull with its standard HTTP retry policy; the 30s polling cycle provides eventual consistency.
- What happens when an encryption operation is evicted from the in-memory store before the UI pulls detail? The pull-back returns a 404; the UI shows the last known progress state (complete/failed from the signal) without per-recipient detail.
- What happens during a rolling deployment where server sends new thin signals but an old client expects rich payloads? Old clients fail to deserialize, log a warning, and fall back to polling. This is transient during deployment.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: All action-related SignalR signals (available, rejected, completed) MUST contain only signal type, instance identifier, optional correlation identifier, and timestamp.
- **FR-002**: Encryption progress signals MUST contain only operation identifier, percent complete, status, and timestamp.
- **FR-003**: EventsHub inbound action signals MUST contain only signal type, instance identifier, optional correlation identifier, and timestamp.
- **FR-004**: All action-related signals MUST be delivered exclusively through wallet-scoped groups with verified ownership.
- **FR-005**: Instance-scoped group broadcasting MUST be removed from the notification service.
- **FR-006**: Service tokens without an `org_id` claim MUST be rejected when subscribing to wallet groups.
- **FR-007**: Clients MUST pull detailed notification data through authenticated endpoints after receiving a signal.
- **FR-008**: The agent client MUST trigger an immediate instance poll upon receiving any action signal.
- **FR-009**: The agent client MUST maintain its polling cycle as a fallback during SignalR disconnections.
- **FR-010**: On SignalR reconnection, the client MUST perform an immediate poll to catch missed signals.
- **FR-011**: When a signal cannot be delivered (no linked wallet), the system MUST log a structured warning including signal type, instance identifier, and participant identifier.
- **FR-012**: All existing pull-back endpoints (instance detail, activity feed, encryption operations) MUST continue to enforce their existing authentication and authorization checks.

### Key Entities

- **SignalNotification**: Thin trigger payload for action-related events (signal type, instance ID, correlation ID, timestamp)
- **EncryptionSignal**: Thin trigger payload for encryption progress (operation ID, percent complete, status, timestamp)
- **ActionNotification** (deprecated): Current rich payload to be replaced by SignalNotification

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Agents receive action signals within 2 seconds of action completion (down from 30-second polling dependency).
- **SC-002**: SignalR notification payloads contain zero workflow metadata fields (action titles, participant names, blueprint names, transaction hashes).
- **SC-003**: Unauthorized wallet subscriptions (service tokens without org scope) are rejected 100% of the time.
- **SC-004**: No signals are delivered through instance-scoped groups.
- **SC-005**: During SignalR disconnection, agents discover pending actions within one polling cycle (30 seconds).
- **SC-006**: On reconnection, agents poll immediately and resume signal-driven operation with no manual intervention.
- **SC-007**: All workflow detail remains accessible through authenticated pull-back endpoints with no degradation in functionality.

## Assumptions

- All existing service tokens issued by Sorcha include the `org_id` claim. If any legacy tokens lack this claim, they will break after this change — to be verified during implementation.
- The in-memory encryption operation store retains operations long enough for UI pull-back after the final signal. TTL to be verified during implementation.
- The existing pull-back endpoints (instance detail, activity feed, encryption operations) have adequate authentication and authorization already in place.
- The 30-second polling interval is sufficient as a fallback safety net and does not need adjustment.
- The `wallet:{address}` group subscription validation (ownership check via Participant Service) is correct and complete.

## Dependencies

- Participant Service must be available for wallet ownership validation (existing dependency, fail-closed behavior already implemented).
- Tenant Service activity feed endpoint must be available for EventsHub pull-back (existing dependency).
- Encryption operations endpoint must be available for encryption detail pull-back (existing dependency).

## Out of Scope

- Encrypted signal envelopes (transport already secured by TLS + authentication)
- New pull-back endpoints (all required endpoints already exist)
- Changes to EventsHubNotificationBridge enrichment/persistence logic (still enriches and persists, just stops sending rich payloads over SignalR)
- HTTPS enforcement (SEC-001 — separate P0 item)
- RegisterHub or organization-level event changes
- Changes to the 30-second polling interval
