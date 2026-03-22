# Data Model: Validator Consensus Security

**Feature**: 066-validator-consensus-security
**Date**: 2026-03-22

## Entities

### ValidatorInfo (EXISTING — extend)

**Location**: `src/Services/Sorcha.Validator.Service/Services/Interfaces/IValidatorRegistry.cs`

Current fields (no changes):
- `ValidatorId` (string, required) — Wallet address, unique identifier
- `PublicKey` (string, required) — Public key for signature verification
- `GrpcEndpoint` (string, required) — Peer communication endpoint
- `Status` (ValidatorStatus, required) — Current lifecycle state
- `RegisteredAt` (DateTimeOffset, required) — Registration timestamp
- `OrderIndex` (int?) — Position for rotating leader election
- `RegistrationTxId` (string?) — On-chain registration transaction
- `Metadata` (Dictionary?) — Optional key-value pairs

**New fields to add**:
- `ApprovedAt` (DateTimeOffset?) — When approved by admin
- `ApprovedBy` (string?) — Admin who approved (wallet address)
- `SuspendedAt` (DateTimeOffset?) — When suspended
- `SuspendedBy` (string?) — Admin who suspended
- `RevokedAt` (DateTimeOffset?) — When revoked (terminal)
- `RevokedBy` (string?) — Admin who revoked
- `LastStateChangeAt` (DateTimeOffset) — Timestamp of most recent state transition
- `Algorithm` (string?) — Signing algorithm for public key (ED25519, NISTP256, RSA4096)

### ValidatorStatus (EXISTING — extend)

Current values: Pending, Active, Suspended, Removed

**Change**: Rename `Removed` → `Revoked` to match spec terminology (terminal, non-reversible)

State transitions:
```
Pending → Active (approve)
Pending → Revoked (reject/revoke)
Active → Suspended (suspend)
Active → Revoked (revoke)
Suspended → Active (reactivate)
Suspended → Revoked (revoke)
```

### ValidatorAuditEntry (NEW)

Records all state transitions for audit trail.

- `Id` (string) — Unique entry ID
- `RegisterId` (string) — Register scope
- `ValidatorId` (string) — Validator affected
- `PreviousStatus` (ValidatorStatus) — State before transition
- `NewStatus` (ValidatorStatus) — State after transition
- `PerformedBy` (string) — Admin wallet address
- `Reason` (string?) — Optional reason/notes
- `Timestamp` (DateTimeOffset) — When transition occurred

### ConsensusVote (EXISTING — extend)

**Location**: `src/Services/Sorcha.Validator.Service/Models/ConsensusVote.cs`

**New fields to add**:
- `Signature` (byte[]) — Cryptographic signature of canonical vote content
- `SignerPublicKey` (byte[]) — Public key used for signing (for verification)
- `Algorithm` (string) — Signing algorithm

**Vote signing contract** (canonical format):
```
SignedContent = SHA256("{DocketId}:{DocketHash}:{Approved}:{ValidatorId}")
```

### WalletSequence (NEW)

Tracks per-wallet, per-register sequence numbers for replay protection.

- `RegisterId` (string) — Register scope
- `WalletAddress` (string) — Sender wallet
- `LastSequenceNumber` (ulong) — Last accepted sequence number
- `LastUpdatedAt` (DateTimeOffset) — When last updated

**Compound key**: `(RegisterId, WalletAddress)` — unique

### Transaction (EXISTING — extend)

**Location**: `src/Services/Sorcha.Validator.Service/Models/Transaction.cs`

**New field to add**:
- `SequenceNumber` (ulong) — Per-sender monotonic sequence number

## MongoDB Collections

### `validators` (NEW)

```json
{
  "_id": "{registerId}:{validatorId}",
  "registerId": "string",
  "validatorId": "string",
  "publicKey": "string",
  "algorithm": "string",
  "grpcEndpoint": "string",
  "status": "string",
  "registeredAt": "ISODate",
  "approvedAt": "ISODate | null",
  "approvedBy": "string | null",
  "suspendedAt": "ISODate | null",
  "suspendedBy": "string | null",
  "revokedAt": "ISODate | null",
  "revokedBy": "string | null",
  "lastStateChangeAt": "ISODate",
  "orderIndex": "int",
  "metadata": "object"
}
```

Indexes:
- `{ registerId: 1, status: 1 }` — per-register status queries
- `{ registerId: 1, validatorId: 1 }` — unique, point lookups

### `validator_audit` (NEW)

```json
{
  "_id": "ObjectId",
  "registerId": "string",
  "validatorId": "string",
  "previousStatus": "string",
  "newStatus": "string",
  "performedBy": "string",
  "reason": "string | null",
  "timestamp": "ISODate"
}
```

Indexes:
- `{ registerId: 1, validatorId: 1, timestamp: -1 }` — audit trail queries
- `{ timestamp: -1 }` — recent activity

### `wallet_sequences` (NEW)

```json
{
  "_id": "{registerId}:{walletAddress}",
  "registerId": "string",
  "walletAddress": "string",
  "lastSequenceNumber": "NumberLong",
  "lastUpdatedAt": "ISODate"
}
```

Indexes:
- `{ registerId: 1, walletAddress: 1 }` — unique, point lookups
