# Data Model: SignalR Minimal Disclosure

## New Entities

### SignalNotification

Thin trigger payload for all action-related SignalR signals. Replaces `ActionNotification`, `ActionAvailableNotification`, `ActionRejectedNotification`, and `WorkflowCompletedNotification`.

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| SignalType | string | Yes | One of: `action-available`, `action-rejected`, `workflow-completed`, `inbound-action` |
| InstanceId | string | Yes | Blueprint instance identifier |
| CorrelationId | Guid? | No | Optional identifier for pull-back correlation |
| Timestamp | DateTimeOffset | Yes | UTC timestamp of signal creation |

### EncryptionSignal

Thin trigger payload for encryption progress signals. Replaces `EncryptionProgressNotification`, `EncryptionCompleteNotification`, `EncryptionFailedNotification`, and `RecipientEncryptionNotification`.

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| OperationId | string | Yes | Encryption operation identifier |
| PercentComplete | int | Yes | Progress percentage (0-100) |
| Status | string | Yes | One of: `encrypting`, `complete`, `failed` |
| Timestamp | DateTimeOffset | Yes | UTC timestamp of signal creation |

## Deprecated Entities

### ActionNotification (ActionsHub.cs:223-233)
**Replaced by:** SignalNotification
**Fields removed:** TransactionHash, WalletAddress, RegisterAddress, BlueprintId, ActionId, Message

### ActionAvailableNotification (UI.Core ActionNotification.cs:56-82)
**Replaced by:** SignalNotification
**Fields removed:** ActionId (int), ActionTitle, ParticipantId

### ActionRejectedNotification (UI.Core ActionNotification.cs:87-118)
**Replaced by:** SignalNotification
**Fields removed:** RejectedActionId, TargetActionId, TargetParticipantId, Reason

### WorkflowCompletedNotification (UI.Core ActionNotification.cs:123-134)
**Replaced by:** SignalNotification (no fields removed beyond what SignalNotification carries)

### EncryptionProgressNotification (EncryptionNotifications.cs:9-40)
**Replaced by:** EncryptionSignal
**Fields removed:** Step, StepName, TotalSteps

### EncryptionCompleteNotification (EncryptionNotifications.cs:45-61)
**Replaced by:** EncryptionSignal (Status="complete")
**Fields removed:** TransactionHash

### EncryptionFailedNotification (EncryptionNotifications.cs:66-92)
**Replaced by:** EncryptionSignal (Status="failed")
**Fields removed:** Error, FailedRecipient, Step

### RecipientEncryptionNotification (EncryptionNotifications.cs:98-126)
**Replaced by:** EncryptionSignal
**Fields removed:** RecipientName, RecipientIndex, TotalRecipients, DisclosedFieldsSummary, PipelineStep, ErrorMessage

### InboundActionNotification (EventsHubNotificationBridge.cs:305-348)
**Replaced by:** SignalNotification (SignalType="inbound-action")
**Fields removed:** BlueprintName, ActionDescription, SenderDisplayName, NavigationPath, TransactionId, RegisterId, WalletAddress, IsRecoveryEvent, Summary, Urgency, Deadline, GroupKey

### UI Mirror Types (EncryptionHubModels.cs, PendingActionNotificationDto.cs)
**Replaced by:** SignalNotification and EncryptionSignal equivalents in UI.Core

## Unchanged Entities

### InboundActionEvent (ServiceClients.Http)
Unchanged — this is the Redis pub/sub message format between Wallet Service and Blueprint Service. The EventsHubNotificationBridge still receives and enriches this; it just sends a thin signal over SignalR instead of the enriched payload.

### ActivityEventDto (UI.Core)
Unchanged — this is the pull-back model fetched from the Tenant Service activity feed. Remains the source of truth for rich notification data in the UI.

### CredentialNotification (UI.Core)
Out of scope — credential notifications are a separate concern not affected by this feature.

## State Transitions

No state machine changes. SignalR group membership is transient (connection-scoped) and unchanged except for removing `instance:{id}` groups.

## Group Model Changes

| Group Pattern | Before | After |
|---------------|--------|-------|
| `wallet:{address}` | Used for some signals | Used for ALL action signals |
| `instance:{id}` | Used for action/completion signals | **Removed** |
| `user:{userId}` | Used for EventsHub | Unchanged (thin signal instead of rich payload) |
| `org:{orgId}` | Used for org events | Unchanged (not affected) |
| `register:{registerId}` | Used for register events | Unchanged (not affected) |
