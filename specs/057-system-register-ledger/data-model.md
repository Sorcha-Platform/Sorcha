# Data Model: System Register as Real Ledger

**Date**: 2026-03-15
**Feature**: 057-system-register-ledger

## Overview

No new entities are introduced. The system register uses the existing `Register`, `TransactionModel`, and `Docket` entities. The removed entities are `SystemRegisterEntry` and `ISystemRegisterRepository`.

## Entities (Existing — Used As-Is)

### Register

The system register is a standard `Register` entity with a deterministic ID.

| Field | Value for System Register |
|-------|--------------------------|
| Id | `aebf26362e079087571ac0932d4db973` (deterministic) |
| Name | "Sorcha System Register" |
| TenantId | "system" |
| Status | `Online` (after genesis) |
| Advertise | `true` (default-on replication) |
| IsFullReplica | `true` |
| Height | Increments with each docket |

### TransactionModel (Blueprint Transactions)

Blueprint publications are stored as standard transactions. The payload contains the blueprint JSON.

| Field | Value for Blueprint Transaction |
|-------|-------------------------------|
| TxId | SHA-256 of `"blueprint-{blueprintId}-{timestamp}"` |
| RegisterId | System register ID |
| SenderWallet | System wallet address |
| PrevTxId | Previous version's TxId (for versioning) or empty (first version) |
| Payloads[0].Data | Base64URL-encoded blueprint JSON |
| Payloads[0].Hash | SHA-256 of canonical blueprint JSON |
| Payloads[0].ContentType | `"application/json"` |
| MetaData.TransactionType | `Action` |
| MetaData.BlueprintId | `"system-blueprint-publish"` |
| Metadata["Type"] | `"BlueprintPublish"` |
| Metadata["BlueprintId"] | Logical blueprint ID (e.g., "register-creation-v1") |
| Metadata["BlueprintVersion"] | Version string (e.g., "1.0.0") |

### TransactionModel (Genesis Transaction)

Standard genesis transaction with control record payload. System wallet as sole Owner.

| Field | Value for System Register Genesis |
|-------|----------------------------------|
| TxId | SHA-256 of `"genesis-aebf26362e079087571ac0932d4db973"` |
| RegisterId | System register ID |
| SenderWallet | `"system"` |
| MetaData.TransactionType | `Control` |
| Attestations[0].Role | `Owner` |
| Attestations[0].Subject | System wallet DID |

## Entities Removed

### SystemRegisterEntry (DELETED)

Previously stored in `sorcha_system_register_blueprints` MongoDB collection. All fields are now covered by `TransactionModel`:

| Old Field | Replaced By |
|-----------|-------------|
| BlueprintId | `Metadata["BlueprintId"]` on transaction |
| Document (BsonDocument) | `Payloads[0].Data` (blueprint JSON) |
| PublishedAt | `TimeStamp` on transaction |
| PublishedBy | `SenderWallet` on transaction |
| Version (auto-increment) | Transaction chain length (count predecessors) |
| IsActive | Always true (immutable ledger — no soft delete) |
| Checksum | `Payloads[0].Hash` (SHA-256) |

## State Transitions

### System Register Bootstrap State Machine

```
[Not Exists] ──(first startup)──> [Creating]
    │                                  │
    │                          InitiateAsync +
    │                          FinalizeAsync
    │                                  │
    │                                  ▼
    │                           [Genesis Submitted]
    │                                  │
    │                          (wait for docket seal)
    │                                  │
    │                                  ▼
    │                           [Online, No Blueprints]
    │                                  │
    │                          (submit seed blueprints)
    │                                  │
    │                                  ▼
    │                           [Online, Initialized]
    │                                  │
[Exists] ──(restart)──> [Skip Bootstrap]
```

## Relationships

```
System Register (1) ──has──> (N) Transactions
    │                              │
    │                         ┌────┴────┐
    │                         │         │
    │                    Genesis    Blueprint
    │                    (Control)  (Action)
    │                                  │
    │                           Version Chain
    │                         (PrevTxId links)
    │
    └──sealed in──> (N) Dockets
```
