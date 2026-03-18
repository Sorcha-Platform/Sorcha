# Data Model: Transaction Explorer UX Overhaul

**Branch**: `064-transaction-explorer` | **Date**: 2026-03-18

## Modified Models

### PayloadViewModel (extended)

**Location**: `src/Apps/Sorcha.UI/Sorcha.UI.Core/Models/Registers/PayloadViewModel.cs`

| Field | Type | Status | Description |
|-------|------|--------|-------------|
| Index | int | existing | Payload index within transaction |
| Hash | string | existing | SHA-256 hash |
| PayloadSize | ulong | existing | Size in bytes |
| WalletAccess | IReadOnlyList\<string\> | existing | Authorized wallet addresses |
| PayloadFlags | string? | existing | Encryption metadata flags |
| HasIV | bool | existing | Whether IV present (encryption indicator) |
| ChallengeCount | int | existing | Per-wallet encryption challenges |
| Data | string? | existing | Raw Base64-encoded payload data (trimmed) |
| **DecodedContent** | string? | **new** | UTF-8 decoded text (computed lazily) |
| **IsJson** | bool | **new** | Whether decoded content is valid JSON |
| **PrettyJson** | string? | **new** | Pretty-printed JSON (computed lazily, null if not JSON) |
| **ContentType** | string? | **new** | Detected or declared MIME type |
| **IsAccessible** | bool | **new** | Whether current user can decrypt (wallet in WalletAccess) |
| **IsEncrypted** | bool | **new** | Whether payload is encrypted (HasIV == true) |
| **PayloadSizeFormatted** | string | existing | Human-readable size |

**State transitions**: None (read-only view model)

**Validation rules**:
- `Data` MUST have leading/trailing whitespace, LF, CR trimmed at construction time
- `DecodedContent` computed on first access (lazy), null if Data is null/empty or decoding fails
- `IsAccessible` = !IsEncrypted || WalletAccess.Contains(currentUserWallet)

### TransactionViewModel (extended)

**Location**: `src/Apps/Sorcha.UI/Sorcha.UI.Core/Models/Registers/TransactionViewModel.cs`

No new fields. Existing `PrevTxId` field already present (used for DAG edges).

## New Models

### TransactionGraphNode

**Location**: `src/Apps/Sorcha.UI/Sorcha.UI.Core/Models/Registers/TransactionGraphNode.cs`

| Field | Type | Description |
|-------|------|-------------|
| TxId | string | Transaction ID (64-char hex) |
| PrevTxId | string | Previous transaction ID (empty for genesis) |
| SenderWallet | string | Sender wallet address |
| TimeStamp | DateTime | Transaction timestamp |
| DocketNumber | ulong? | Docket number if confirmed |
| BlueprintId | string? | Blueprint ID from metadata |
| InstanceId | string? | Instance ID from metadata |
| TransactionType | string | Type derived from metadata |
| X | double | Computed layout X coordinate |
| Y | double | Computed layout Y coordinate |
| IsGenesis | bool | Computed: PrevTxId is empty or all-zeros |
| IsHighlighted | bool | Whether node is in highlighted chain |
| Rank | int | Computed: depth from genesis (0-based) |
| OrderInRank | int | Computed: position within rank for layout |

**Relationships**:
- `PrevTxId` → `TxId` of parent node (directed edge)
- Multiple nodes may share the same `PrevTxId` (fork)
- Multiple nodes may share the same `InstanceId` (same workflow instance, colour group)

### TransactionGraphEdge

**Location**: `src/Apps/Sorcha.UI/Sorcha.UI.Core/Models/Registers/TransactionGraphNode.cs` (nested or same file)

| Field | Type | Description |
|-------|------|-------------|
| SourceTxId | string | Parent transaction (PrevTxId owner) |
| TargetTxId | string | Child transaction |
| IsHighlighted | bool | Whether edge is in highlighted chain |

### NavigationContext

**Location**: `src/Apps/Sorcha.UI/Sorcha.UI.Core/Models/Registers/NavigationContext.cs`

| Field | Type | Description |
|-------|------|-------------|
| Level | NavigationLevel | Current depth: Register, Docket, Transaction |
| DocketId | string? | Selected docket ID (when Level >= Docket) |
| DocketVersion | ulong? | Docket version for breadcrumb display |
| TransactionId | string? | Selected transaction ID (when Level == Transaction) |
| TransactionIdTruncated | string | Computed: first 8 chars of TransactionId |

**State transitions**:
- Register → Docket (user clicks docket in chain)
- Docket → Transaction (user clicks transaction in docket list)
- Transaction → Docket (breadcrumb click on docket segment)
- Docket → Register (breadcrumb click on register segment, or close panel)

```
NavigationLevel enum: Register, Docket, Transaction
```

### TransactionGraphResponse (API response DTO)

**Location**: `src/Services/Sorcha.Register.Service/` (inline record or separate file)

| Field | Type | Description |
|-------|------|-------------|
| RegisterId | string | Register ID |
| Nodes | TransactionGraphNodeDto[] | Lightweight transaction projections |
| TotalCount | int | Total transactions in register |

### TransactionGraphNodeDto (API projection)

| Field | Type | Description |
|-------|------|-------------|
| TxId | string | Transaction ID |
| PrevTxId | string | Previous transaction ID |
| SenderWallet | string | Sender wallet address |
| TimeStamp | DateTime | Timestamp |
| DocketNumber | ulong? | Docket number |
| BlueprintId | string? | Blueprint ID |
| InstanceId | string? | Instance ID |
| TransactionType | int? | Transaction type enum value |

## Entity Relationship Summary

```
TransactionGraphNode ---PrevTxId---> TransactionGraphNode (DAG edge)
TransactionGraphNode ---InstanceId--> [colour group] (visual grouping)
NavigationContext ---DocketId---> DocketViewModel (breadcrumb reference)
NavigationContext ---TransactionId---> TransactionViewModel (detail panel content)
PayloadViewModel ---WalletAccess---> [current user wallet] (access check)
```
