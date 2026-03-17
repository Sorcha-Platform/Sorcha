# Data Model: Pending Action Notifications

**Feature**: 062-pending-action-notifications | **Date**: 2026-03-17

## New Entities

### NotificationConfig (on Action model)

Optional configuration added to each blueprint action defining how notifications appear.

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| SummaryTemplate | string | No | Template with `{{payload.field}}` references for extracting business context |
| UrgencyRule | string | No | One of: `none`, `deadline`, `always-urgent` |
| DeadlineField | string | No | Payload field path for deadline (e.g., `payload.inspectionDeadline`) |
| GroupBy | string | No | Payload field path for grouping related notifications |

**Defaults when absent**: Summary = "{BlueprintTitle} — {ActionTitle}", Urgency = none, GroupBy = none

### PendingActionSummary (query response)

Lightweight projection returned by the pending actions query endpoint.

| Field | Type | Description |
|-------|------|-------------|
| InstanceId | string | Blueprint instance ID |
| ActionId | int | Action ID within blueprint |
| ActionTitle | string | Human-readable action title |
| BlueprintId | string | Blueprint ID |
| BlueprintTitle | string | Blueprint display name |
| SenderAddress | string | Wallet address of previous action submitter |
| SenderDisplayName | string | Resolved participant display name |
| Summary | string | Rendered notification summary (from template or default) |
| Urgency | enum | Normal / Warning / Urgent |
| Deadline | DateTimeOffset? | Deadline timestamp (if configured) |
| RegisterId | string | Register containing the instance |
| TransactionId | string | Triggering transaction ID |
| NavigationPath | string | Deep link path to action page |
| ReceivedAt | DateTimeOffset | When the notification was delivered |

## Modified Entities

### Action (Blueprint.Models)

Add one new optional property:

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| Notification | NotificationConfig? | No | Per-action notification configuration |

### InboundActionNotification (EventsHubNotificationBridge)

Add fields for template-derived content:

| Field | Type | Description |
|-------|------|-------------|
| Summary | string | Rendered summary from NotificationConfig template (or default) |
| Urgency | string | Calculated urgency level: `normal`, `warning`, `urgent` |
| Deadline | DateTimeOffset? | Parsed deadline value (if configured) |
| GroupKey | string? | Resolved grouping key value |

## Existing Entities (No Changes)

### ActivityEvent (Tenant Service)

Used as-is for notification persistence. New notifications will create ActivityEvent records with:
- `EventType = "PendingAction"`
- `Severity = Info` (normal), `Warning` (deadline approaching), `Error` (overdue)
- `Title = "{ActionTitle}"`
- `Message = "{Summary}"`
- `EntityId = "{InstanceId}"`
- `EntityType = "BlueprintInstance"`

### UserPreferences (Tenant Service)

Used as-is for notification delivery preferences. Relevant fields:
- `NotificationsEnabled` (bool)
- `NotificationMethod` (InApp / InAppPlusEmail / InAppPlusPush)
- `NotificationFrequency` (RealTime / HourlyDigest / DailyDigest)

## State Transitions

### Notification Lifecycle

```
Transaction Confirmed
  → InboundActionEvent created (Wallet Service)
  → Delivery routing (real-time / digest / quiet)
  → Enrichment (Blueprint Service bridge)
  → ActivityEvent persisted (Tenant Service)
  → SignalR push to UI (EventsHub)
  → UI renders in inbox + toast

User acts on notification:
  → Clicks CTA → navigates to action page
  → Completes action → notification marked complete
  → Inbox removes/moves to completed

Delegate completes action:
  → Instance state changes → notification marked "completed by {delegate}"
```
