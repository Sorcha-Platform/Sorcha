# Data Model: Envelope Encryption Integration

**Feature**: 052-encryption-integration
**Date**: 2026-03-06

## Entities

### 1. ActionSubmissionResultViewModel (Modified)

**Purpose**: UI view model returned after action submission. Extended with async operation fields.

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| TransactionId | string | yes | Transaction hash (empty when IsAsync=true) |
| InstanceId | string | yes | Workflow instance ID |
| IsComplete | bool | yes | Whether the workflow completed |
| NextActions | List\<NextActionInfo\> | no | Subsequent actions in workflow |
| Warnings | List\<string\> | no | Non-blocking validation warnings |
| **OperationId** | **string?** | **no** | **Operation ID for async encryption tracking (new)** |
| **IsAsync** | **bool** | **no** | **True when encryption is processed asynchronously (new)** |

**State transitions**: Not applicable — this is a response DTO, not a stateful entity.

### 2. EncryptionOperation (Existing — No Changes)

**Purpose**: Tracks the lifecycle of an async encryption job in the Blueprint Service.

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| OperationId | string | yes | Unique identifier (GUID without hyphens) |
| Status | EncryptionOperationStatus | yes | Current lifecycle state |
| BlueprintId | string | yes | Source blueprint |
| ActionId | string | yes | Source action within blueprint |
| InstanceId | string | yes | Workflow instance |
| SubmittingWalletAddress | string | yes | Wallet that initiated |
| TotalRecipients | int | yes | Number of encryption recipients |
| TotalGroups | int | yes | Number of disclosure groups |
| CurrentStep | int | yes | Current pipeline step (1-4) |
| TotalSteps | int | yes | Total pipeline steps (4) |
| StepName | string | yes | Human-readable step name |
| PercentComplete | int | yes | Progress 0-100 |
| TransactionHash | string? | no | Result hash on success |
| Error | string? | no | Error message on failure |
| FailedRecipient | string? | no | Wallet of failed recipient |
| CreatedAt | DateTimeOffset | yes | Creation timestamp |
| CompletedAt | DateTimeOffset? | no | Completion timestamp |

**State transitions**:

```
Pending → ResolvingKeys → Encrypting → BuildingTransaction → Submitting → Complete
                                                                         ↘ Failed
           ↗ Failed        ↗ Failed         ↗ Failed           ↗ Failed
```

Any in-progress state can transition to Failed. Only Submitting transitions to Complete.

### 3. EncryptionOperationViewModel (Existing — Extended)

**Purpose**: UI-side view model for operation display.

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| OperationId | string | yes | Operation identifier |
| Stage | string | yes | Current stage name |
| PercentComplete | int | yes | Progress 0-100 |
| RecipientCount | int | yes | Total recipients |
| ProcessedRecipients | int | yes | Completed recipients |
| ErrorMessage | string? | no | Error details |
| **TransactionHash** | **string?** | **no** | **Result transaction hash on success (new)** |
| **BlueprintId** | **string?** | **no** | **Source blueprint for context display (new)** |
| **ActionTitle** | **string?** | **no** | **Action name for context display (new)** |
| **CreatedAt** | **DateTimeOffset** | **yes** | **Operation start time (new)** |
| **CompletedAt** | **DateTimeOffset?** | **no** | **Operation end time (new)** |
| IsComplete | bool (computed) | yes | Stage is Complete or Failed |
| IsSuccess | bool (computed) | yes | Stage is Complete |

### 4. OperationHistoryItem (New)

**Purpose**: Represents a completed operation in the history list, derived from ActivityEvent records.

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| OperationId | string | yes | Operation identifier |
| Status | string | yes | Final status (completed, failed) |
| BlueprintId | string | yes | Source blueprint |
| ActionTitle | string | yes | Action display name |
| InstanceId | string | yes | Workflow instance |
| WalletAddress | string | yes | Submitting wallet |
| RecipientCount | int | yes | Total recipients |
| TransactionHash | string? | no | Result hash (if successful) |
| ErrorMessage | string? | no | Error details (if failed) |
| CreatedAt | DateTimeOffset | yes | When the operation started |
| CompletedAt | DateTimeOffset | yes | When the operation finished |

### 5. OperationHistoryPage (New)

**Purpose**: Paginated response for operations history listing.

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| Items | List\<OperationHistoryItem\> | yes | Operations in current page |
| Page | int | yes | Current page number (1-based) |
| PageSize | int | yes | Items per page |
| TotalCount | int | yes | Total operations across all pages |
| HasMore | bool | yes | Whether more pages exist |

### 6. ActionExecuteRequest (Existing — No Changes)

**Purpose**: Request model for submitting an action from the UI.

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| BlueprintId | string | yes | Blueprint identifier |
| ActionId | string | yes | Action ID within blueprint |
| InstanceId | string | yes | Workflow instance |
| SenderWallet | string | yes | Submitting wallet address |
| RegisterAddress | string | yes | Target register |
| PayloadData | Dictionary\<string, object\> | yes | Form data |

## Relationships

```
ActionExecuteRequest ──submits──▶ Blueprint Service
                                        │
                              ┌─────────┴──────────┐
                              │                    │
                         (sync path)          (async path)
                              │                    │
                    ActionSubmissionResult    EncryptionOperation
                    (TransactionId filled)    (OperationId returned)
                                                   │
                                          EncryptionOperationViewModel
                                          (UI polling/SignalR updates)
                                                   │
                                          OperationHistoryItem
                                          (from ActivityEvent records)
```

## Validation Rules

- OperationId: Non-empty string, unique per operation
- PercentComplete: Integer 0-100, monotonically increasing per operation
- WalletAddress: Must be a valid wallet address the caller owns
- Page: Positive integer >= 1
- PageSize: Integer 1-50, default 20
