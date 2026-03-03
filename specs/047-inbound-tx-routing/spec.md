# Feature Specification: Inbound Transaction Routing & User Notification

**Feature Branch**: `047-inbound-tx-routing`
**Created**: 2026-03-02
**Status**: Draft
**Input**: User description: "Inbound Transaction Routing & User Notification — route incoming peer transactions to local users and notify them of pending actions"

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Local Address Registration (Priority: P1)

When a user creates or derives a new wallet address on the local node, the system automatically registers that address as "local" so that future inbound transactions destined for it can be identified and routed. The address registration is entirely internal — no external peer learns which addresses belong to this node, preserving user privacy.

**Why this priority**: Without address registration, the system cannot identify which inbound transactions are relevant to local users. This is the foundation for all notification and routing capabilities.

**Independent Test**: Can be fully tested by creating a wallet, verifying the address appears in the local address index, restarting the node, and confirming the index is rebuilt correctly. Delivers the core capability of "know which addresses are mine."

**Acceptance Scenarios**:

1. **Given** a user creates a new wallet, **When** the wallet address is generated, **Then** the system registers that address in the local address index within 2 seconds.
2. **Given** a user derives a new address from an existing HD wallet, **When** the derived address is created, **Then** it is automatically added to the local address index.
3. **Given** the node restarts, **When** the system initialises, **Then** the local address index is rebuilt from all known wallet addresses before normal operation resumes.
4. **Given** an administrator triggers a manual rebuild, **When** the rebuild command executes, **Then** the local address index is fully reconstructed and a confirmation is returned.
5. **Given** a wallet address is registered locally, **When** an external peer queries or observes network traffic, **Then** no information about the address being local is disclosed.

---

### User Story 2 - Inbound Action Notification (Priority: P1)

When the node receives a transaction from the peer network that is destined for a local wallet address and represents a blueprint action, the system identifies the owning user and notifies them that they have a pending action. The notification includes the blueprint name, action step, and a link to respond. Users who prefer real-time alerts see the notification immediately; users who prefer digest mode receive a summary on their configured schedule.

**Why this priority**: This is the core user-facing value — users learn about pending actions without polling or manual checking. Tied with P1 because it requires address registration but delivers the primary end-user benefit.

**Independent Test**: Can be tested by submitting a transaction targeting a local wallet address and verifying the user receives an in-app notification with the correct blueprint and action context. Delivers "I know when someone sends me something."

**Acceptance Scenarios**:

1. **Given** a transaction arrives with a recipient address matching a local wallet, **When** the transaction is stored, **Then** the system identifies the wallet owner and delivers a notification within 5 seconds (real-time mode).
2. **Given** a transaction carries blueprint metadata (blueprint ID, instance ID, action ID), **When** the notification is delivered, **Then** it includes the blueprint name, action description, and a navigation path to the action.
3. **Given** a user has real-time notifications enabled, **When** an inbound action transaction arrives, **Then** the notification appears in the user's browser without page refresh.
4. **Given** a user has digest notifications enabled (hourly or daily), **When** inbound action transactions arrive during the digest period, **Then** they are batched and delivered as a single summary at the next scheduled digest time.
5. **Given** a transaction arrives for a local address but is NOT an action type (e.g., control or docket transaction), **When** the system processes it, **Then** no user notification is generated.
6. **Given** a transaction has multiple recipient addresses and only one is local, **When** the system processes it, **Then** only the local recipient's owner is notified.

---

### User Story 3 - Notification Preferences (Priority: P2)

Users can configure how they receive action notifications through their settings page. Options include the delivery method (in-app only, in-app plus email, in-app plus push notification) and frequency (real-time per-transaction, hourly digest, daily digest). Preferences are stored in the user's profile and take effect immediately.

**Why this priority**: Real-time in-app notification (the default) works without preferences. This story adds configurability for users who want email/push or digest batching.

**Independent Test**: Can be tested by changing notification preferences in settings and verifying subsequent notifications respect the new configuration. Delivers "I control how I'm notified."

**Acceptance Scenarios**:

1. **Given** a user opens the notification settings, **When** the page loads, **Then** the current notification method and frequency are displayed.
2. **Given** a user changes their notification frequency from real-time to daily digest, **When** they save, **Then** subsequent action notifications are batched and delivered once daily.
3. **Given** a user enables email notifications, **When** an inbound action arrives, **Then** an email summary is sent in addition to the in-app notification.
4. **Given** a user changes preferences, **When** they save and return to the settings page later, **Then** the saved preferences persist correctly.
5. **Given** a new user account is created, **When** no preferences have been set, **Then** the default is real-time in-app notifications only.

---

### User Story 4 - Register Recovery & Sync (Priority: P2)

When a node comes online after being offline (or is newly joining the network), it automatically detects that it is behind the current state of each subscribed register. The system enters recovery mode, requests missing dockets from peers, and processes them in order. During recovery, any transactions matching local addresses generate catch-up notifications so users are alerted to actions they may have missed while the node was down. A health check endpoint reports sync status so operators can monitor progress.

**Why this priority**: The system works without recovery if the node is always online. Recovery is essential for production reliability but not for the core notification pipeline.

**Independent Test**: Can be tested by stopping the node, submitting transactions to the network, restarting the node, and verifying it catches up and delivers missed notifications. Delivers "I don't miss anything when my node goes offline."

**Acceptance Scenarios**:

1. **Given** the node starts and the local register's latest docket is behind the network head, **When** startup completes, **Then** the system automatically enters recovery mode and begins requesting missing dockets.
2. **Given** recovery mode is active, **When** a missing docket is received, **Then** it is stored with full chain integrity verification (previous hash linkage).
3. **Given** recovery is processing historical dockets, **When** a transaction in a recovered docket matches a local address, **Then** the user is notified (respecting their notification preferences).
4. **Given** recovery mode is active, **When** an operator queries the health endpoint, **Then** the response includes: sync status (recovering/synced), current docket number, target docket number, and estimated progress percentage.
5. **Given** recovery completes (local docket matches network head), **When** the system transitions to normal mode, **Then** the health endpoint reports "synced" and normal transaction processing resumes.
6. **Given** a peer is unreachable during recovery, **When** the system cannot retrieve a docket, **Then** it retries with alternative peers before reporting a sync warning.

---

### User Story 5 - Digest Notification Batching (Priority: P3)

Users who prefer digest notifications receive a consolidated summary of all pending actions accumulated during the digest window. The digest groups actions by blueprint, shows counts and urgency, and is delivered at the configured interval (hourly or daily). In-app digests appear as a single consolidated notification; email digests are formatted as a readable summary.

**Why this priority**: Real-time notifications cover most use cases. Digest is a refinement for high-volume users who find per-transaction alerts noisy.

**Independent Test**: Can be tested by configuring hourly digest, sending multiple action transactions over 30 minutes, and verifying a single consolidated notification arrives at the next hour mark. Delivers "I get a clean summary instead of notification spam."

**Acceptance Scenarios**:

1. **Given** a user has hourly digest enabled, **When** 5 action transactions arrive in a 1-hour window, **Then** a single digest notification is delivered at the next hour boundary containing all 5 actions.
2. **Given** a digest is delivered, **When** the user views it, **Then** actions are grouped by blueprint with counts and the most recent action timestamp.
3. **Given** no action transactions arrive during a digest window, **When** the digest interval elapses, **Then** no notification is sent (no empty digests).
4. **Given** a user switches from digest to real-time, **When** any pending digest items exist, **Then** they are delivered immediately as individual notifications and the digest queue is cleared.

---

### Edge Cases

- What happens when a wallet is deleted or deactivated? The address must be removed from the local index to stop generating notifications.
- What happens when a transaction arrives during index rebuild? Transactions received during rebuild must be queued and processed after the rebuild completes, or the rebuild must be atomic (swap in the new index).
- What happens when the same address exists on multiple nodes? Each node independently identifies it as local — both receive notifications. This is by design (key sharing scenario).
- What happens when a user has multiple wallets with addresses that all receive actions from the same blueprint instance? Each action targets a specific address; the user receives one notification per unique action, not per address.
- What happens during network partitions? Notifications are best-effort. When the partition heals, recovery mode catches up missed dockets and delivers delayed notifications.
- What happens when the bloom filter produces a false positive? The system performs a definitive address lookup, finds no match, and discards — no user impact.
- What happens when thousands of transactions arrive simultaneously (bulk sync)? The notification system must handle burst processing without overwhelming the user. Digest mode absorbs bursts naturally; real-time mode should rate-limit to prevent notification flooding (e.g., max 10 per minute, then auto-switch to digest for the remainder).

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST register new wallet addresses in the local address index automatically when wallets are created or addresses are derived.
- **FR-002**: System MUST rebuild the local address index from all known wallet addresses on node startup.
- **FR-003**: System MUST support an administrative command to force-rebuild the local address index on demand.
- **FR-004**: System MUST never expose the local address index or its contents to external peers.
- **FR-005**: System MUST check recipient addresses of every stored transaction against the local address index.
- **FR-006**: System MUST, on a local address match for an action-type transaction, identify the wallet owner (user) and generate a notification.
- **FR-007**: Notifications MUST include: blueprint name (resolved from blueprint ID), blueprint instance identifier, action description (resolved from action ID), sender display name (resolved from sender address via published participant lookup; falls back to raw address if participant not found), and a navigation path to the action.
- **FR-008**: System MUST deliver real-time notifications to connected users within 5 seconds of transaction storage.
- **FR-009**: System MUST support digest notification batching at hourly and daily intervals.
- **FR-010**: System MUST store notification preferences (method and frequency) as part of the user's profile.
- **FR-011**: Notification preferences MUST default to real-time in-app delivery for new users.
- **FR-012**: System MUST automatically detect when the local register is behind the network head on startup.
- **FR-013**: System MUST enter recovery mode and request missing dockets sequentially from peers until caught up.
- **FR-014**: System MUST process local-address matches during recovery and deliver notifications for missed actions.
- **FR-015**: System MUST expose a health check endpoint reporting sync status (recovering, synced), current docket, target docket, and progress percentage.
- **FR-016**: System MUST remove addresses from the local index when wallets are deleted or deactivated.
- **FR-017**: System MUST rate-limit real-time notifications during burst scenarios (e.g., recovery or bulk sync) to prevent user notification flooding.
- **FR-018**: System MUST only notify for action-type transactions; control, docket, and participant transaction types do not generate user notifications.
- **FR-019**: Digest notifications MUST group actions by blueprint and include counts and the most recent timestamp per group.
- **FR-020**: System MUST NOT send empty digest notifications when no actions occurred during the digest window.

### Key Entities

- **LocalAddressIndex**: A probabilistic index of all wallet addresses belonging to the local node. Supports fast membership testing with no false negatives. Rebuilt on startup, updated on address creation/deletion.
- **InboundActionEvent**: Represents a detected action-type transaction destined for a local address. Contains: wallet address, wallet identifier, user identifier, blueprint ID, instance ID, action ID, sender address, timestamp.
- **NotificationPreference**: Per-user configuration for notification delivery. Contains: method (in-app, in-app+email, in-app+push), frequency (real-time, hourly digest, daily digest).
- **DigestQueue**: Accumulated action events awaiting digest delivery. Grouped by user and blueprint. Cleared on digest delivery or when user switches to real-time.
- **RecoveryState**: Per-register sync tracking. Contains: register ID, local latest docket number, network head docket number, status (recovering, synced), start time.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Users receive in-app notification of a pending action within 5 seconds of the transaction being stored (real-time mode).
- **SC-002**: The local address index rebuild completes within 30 seconds for up to 100,000 addresses.
- **SC-003**: False positive rate of local address lookups is below 0.1%, ensuring fewer than 1 in 1,000 transactions trigger unnecessary processing.
- **SC-004**: Recovery mode processes at least 100 dockets per second when catching up from peers.
- **SC-005**: Users who were offline receive all missed action notifications within 2 minutes of their node completing recovery.
- **SC-006**: 100% of action-type transactions destined for local addresses result in a user notification (zero missed notifications).
- **SC-007**: Digest notifications are delivered within 5 minutes of the configured interval boundary (e.g., hourly digest arrives by :05).
- **SC-008**: The health check endpoint accurately reports sync status with less than 10-second staleness.
- **SC-009**: No external peer can determine which addresses are local to a given node through network observation or query.
- **SC-010**: Real-time notification rate limiting caps at 10 notifications per minute per user during burst scenarios, with overflow routed to digest.

## Assumptions

- **Full replicas only**: All nodes store all transactions for v1. Partial replicas (store only local-relevant transactions) are deferred to a future iteration.
- **Validators are full replicas**: Validator nodes require all transactions to build dockets and verify Merkle trees.
- **Broadcast gossip model**: The peer network broadcasts all transactions to all peers. Local filtering is private and never shared.
- **Register creator = full replica**: The node that creates a register always maintains a full copy.
- **Existing transaction metadata**: Transactions already carry BlueprintId, InstanceId, ActionId, and NextActionId in their metadata. No changes to the transaction format are required.
- **Existing notification infrastructure**: The platform already has in-app real-time notifications via EventsHub/SignalR. This feature extends that infrastructure, not replaces it.
- **Email/push delivery**: Email and push notification delivery infrastructure is assumed to exist or be provided by a separate feature. This spec covers the triggering and preference logic, not the email/push transport implementation.
- **Single node per installation**: Each node installation operates independently. Address privacy is per-node.

## Out of Scope

- **Partial replicas**: Storage filtering based on address locality (deferred to future iteration).
- **Full-to-partial replica conversion**: Downgrading a full replica to partial is architecturally complex and deferred.
- **Email/push transport implementation**: The mechanisms for actually sending emails or push notifications. This spec covers when and what to send, not the delivery transport.
- **Cross-node address sharing**: Scenarios where the same HD wallet is restored on multiple nodes. Each node operates independently.
- **Non-action transaction notifications**: Notifications for control, docket, or participant transaction types.
- **Notification UI components**: The UI for displaying notifications already exists. This spec covers the backend pipeline that feeds it.
