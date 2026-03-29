# Data Model: FLE Completion & Crypto Progress UX

## New Models

### RecipientProgress (TransactionHandler layer)

Tracks per-recipient encryption outcome within the pipeline result.

| Field | Type | Description |
|-------|------|-------------|
| WalletAddress | string | Recipient's wallet address |
| DisplayName | string? | Participant display name (null = use truncated wallet) |
| DisclosedFields | string[] | JSON Pointer paths disclosed to this recipient |
| GroupId | string | Disclosure group this recipient belongs to |
| Status | RecipientProgressStatus | Waiting, Encrypting, Secured, Failed |
| ErrorMessage | string? | Only populated when Status = Failed |

### RecipientProgressStatus (enum)

| Value | Description |
|-------|-------------|
| Waiting | Not yet processed |
| Encrypting | Key wrapping in progress |
| Secured | Key successfully wrapped |
| Failed | Key wrapping failed |

### RecipientEncryptionNotification (Blueprint.Service layer)

SignalR event payload for per-recipient progress.

| Field | Type | Description |
|-------|------|-------------|
| OperationId | string | Correlation ID for the encryption operation |
| RecipientName | string | Display name or truncated wallet address |
| RecipientIndex | int | 1-based index (e.g., 2 of 5) |
| TotalRecipients | int | Total recipients in this operation |
| DisclosedFieldsSummary | string[] | JSON Pointer paths for this recipient |
| Status | string | "waiting", "encrypting", "secured", "failed" |
| ErrorMessage | string? | Only when status = "failed" |
| Timestamp | DateTimeOffset | When this event was emitted |

### EncryptionOperationState (UI layer)

Client-side model tracking an active encryption operation for the popover.

| Field | Type | Description |
|-------|------|-------------|
| OperationId | string | Operation tracking ID |
| Status | OperationStatus | Pending, InProgress, Complete, Failed |
| PercentComplete | int | 0-100 overall progress |
| Recipients | RecipientDisplayState[] | Per-recipient state for UI rendering |
| TransactionHash | string? | Set on completion |
| ErrorMessage | string? | Set on failure |
| FailedRecipient | string? | Name of recipient that caused failure |
| PanelState | PopoverState | Expanded, Minimised, Dismissed |
| CreatedAt | DateTimeOffset | When operation started |

### RecipientDisplayState (UI layer)

Per-recipient state for rendering in the popover.

| Field | Type | Description |
|-------|------|-------------|
| Name | string | Display name or truncated wallet |
| FieldsSummary | string | "all fields" or "decision, site details" |
| Status | string | "waiting", "encrypting", "secured", "failed" |

### PopoverState (enum, UI layer)

| Value | Description |
|-------|-------------|
| Expanded | Full floating panel with recipient list |
| Minimised | Compact pill with summary |
| Dismissed | No visual — toast on completion |

## Modified Models

### EncryptionResult (existing, TransactionHandler)

Add field:

| Field | Type | Description |
|-------|------|-------------|
| RecipientProgress | RecipientProgress[] | Per-recipient completion metadata (new) |

### RecipientInfo (existing, TransactionHandler)

Add field:

| Field | Type | Description |
|-------|------|-------------|
| DisplayName | string? | Participant display name for UI events (new) |

### EncryptionOperation (existing, Blueprint.Service)

Add field:

| Field | Type | Description |
|-------|------|-------------|
| Recipients | RecipientOperationStatus[] | Per-recipient status for polling endpoint (new) |

### RecipientOperationStatus (new, Blueprint.Service)

| Field | Type | Description |
|-------|------|-------------|
| Name | string | Display name or truncated wallet |
| DisclosedFields | string[] | JSON Pointer paths |
| Status | string | "waiting", "encrypting", "secured", "failed" |

## Entity Relationships

```
EncryptionOperation (1) ──── (N) RecipientOperationStatus
       │
       │ tracked by
       ▼
EncryptionOperationTracker (1) ──── (N) EncryptionOperationState
       │                                       │
       │ renders                                │ contains
       ▼                                        ▼
CryptoProgressPopover                   RecipientDisplayState[]
```

## State Transitions

### Operation Lifecycle

```
Pending → InProgress → Complete
                    └→ Failed
```

### Recipient Lifecycle

```
Waiting → Encrypting → Secured
                    └→ Failed
```

### Popover Lifecycle

```
[not visible] → Expanded → Minimised → Expanded (toggle)
                        └→ Dismissed → [toast on complete/fail]
```
