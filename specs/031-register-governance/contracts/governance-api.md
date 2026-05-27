# API Contracts: Register Governance

**Branch**: `031-register-governance` | **Date**: 2026-02-11

## Register Service — Governance Endpoints

### GET /api/registers/{registerId}/roster

Reconstruct the current admin roster from the Control chain.

**Response 200**:
```json
{
  "registerId": "a1b2c3d4e5f6...",
  "members": [
    {
      "did": "did:sorcha:w:1A1zP1eP5QGefi2DMPTfTL5SLmv7DivfNa",
      "role": "Owner",
      "publicKey": "base64...",
      "grantedAt": "2026-02-11T10:00:00Z"
    },
    {
      "did": "did:sorcha:w:3J98t1WpEZ73CNmQviecrnyiWrnqRhWNLy",
      "role": "Admin",
      "publicKey": "base64...",
      "grantedAt": "2026-02-12T15:30:00Z"
    }
  ],
  "controlTransactionCount": 3,
  "lastControlTxId": "abc123def456...",
  "quorum": {
    "votingMembers": 2,
    "threshold": 2
  }
}
```

**Response 404**: Register not found.

---

### GET /api/registers/{registerId}/governance/history

List all governance operations (Control transactions) on the register.

**Query Parameters**:
- `page` (int, default 1)
- `pageSize` (int, default 20)

**Response 200**:
```json
{
  "items": [
    {
      "txId": "abc123...",
      "operationType": "Add",
      "proposerDid": "did:sorcha:w:...",
      "targetDid": "did:sorcha:w:...",
      "targetRole": "Admin",
      "status": "Recorded",
      "proposedAt": "2026-02-12T15:00:00Z",
      "recordedAt": "2026-02-12T15:30:00Z",
      "approvalCount": 2
    }
  ],
  "total": 5,
  "page": 1,
  "pageSize": 20
}
```

---

## Blueprint Service — Governance Workflow Endpoints

Governance operations are executed through the standard blueprint execution flow. The genesis blueprint (`register-governance-v1`) is a normal blueprint with specific actions.

### POST /api/instances (existing — used for governance)

Start a new governance proposal by creating a blueprint instance.

**Request**:
```json
{
  "blueprintId": "register-governance-v1",
  "registerId": "a1b2c3d4e5f6...",
  "participantWallets": {
    "proposer": "1A1zP1eP5QGefi2DMPTfTL5SLmv7DivfNa"
  }
}
```

**Response 201**:
```json
{
  "instanceId": "550e8400-e29b-41d4-a716-446655440000",
  "blueprintId": "register-governance-v1",
  "registerId": "a1b2c3d4e5f6...",
  "state": "Active",
  "currentActionIds": [1]
}
```

---

### POST /api/instances/{instanceId}/actions/{actionId}/submit (existing — used for governance)

Submit a governance action (propose, approve, accept, etc.).

**Action 1 — Propose Change**:
```json
{
  "senderWallet": "1A1zP1eP5QGefi2DMPTfTL5SLmv7DivfNa",
  "payloadData": {
    "operationType": "Add",
    "targetDid": "did:sorcha:w:3J98t1WpEZ73CNmQviecrnyiWrnqRhWNLy",
    "targetRole": "Admin",
    "justification": "Adding new team member"
  }
}
```

**Action 2 — Collect Quorum (each admin submits one)**:
```json
{
  "senderWallet": "1BvBMSEYstWetqTFn5Au4m4GFg7xJaNVN2",
  "payloadData": {
    "vote": "approve",
    "comment": "Approved — verified identity offline"
  }
}
```

Or rejection:
```json
{
  "senderWallet": "1BvBMSEYstWetqTFn5Au4m4GFg7xJaNVN2",
  "payloadData": {
    "vote": "reject",
    "reason": "Unknown identity — not approved"
  }
}
```

**Action 3 — Accept Role (target admin)**:
```json
{
  "senderWallet": "3J98t1WpEZ73CNmQviecrnyiWrnqRhWNLy",
  "payloadData": {
    "accepted": true
  }
}
```

Or decline:
```json
{
  "senderWallet": "3J98t1WpEZ73CNmQviecrnyiWrnqRhWNLy",
  "payloadData": {
    "accepted": false,
    "reason": "Declining role at this time"
  }
}
```

**Action 4 — Record Control Transaction (system-triggered)**:
The final action compiles the full roster and writes the Control transaction. This is triggered automatically when the acceptance is recorded.

---

## DID Resolution — Internal Service Contract

> **Historical (Feature 120 Phase 0, 2026-05-09):** This bespoke contract was retired. DID resolution now uses the W3C-shaped `IDidResolverRegistry` in `src/Common/Sorcha.ServiceClients.Http/Did/`, which returns a `DidDocument` rather than the bespoke `DIDResolutionResult`.

### IDIDResolver Interface

```
Resolve(did: string) → DIDResolutionResult
  - did:sorcha:w:{addr} → WalletDIDResult { publicKey, algorithm, walletAddress }
  - did:sorcha:r:{reg}:t:{tx} → RegisterDIDResult { publicKey, algorithm, registerId, txId }
  - invalid → DIDResolutionError { errorCode, message }
```

### IGovernanceRosterService Interface

```
GetCurrentRoster(registerId: string) → AdminRoster
  - Filters Control TXs from register
  - Returns latest roster snapshot

ValidateQuorum(registerId: string, operation: GovernanceOp, approvals: List<Approval>) → QuorumResult
  - Calculates adjusted pool (exclude target for Remove)
  - Checks >50% threshold
  - Returns { isQuorumMet, votesRequired, votesReceived, votingPool }
```

### IRightsEnforcementService Interface

```
ValidateGovernanceRights(registerId: string, submitterDid: string, operation: GovernanceOp) → RightsResult
  - Reconstructs roster
  - Checks submitter is in roster with correct role
  - Returns { isAuthorized, role, error }
```

---

## Genesis Blueprint Contract

### register-governance-v1 (System Blueprint)

```json
{
  "@context": "https://sorcha.dev/blueprints/v1",
  "id": "register-governance-v1",
  "title": "Register Governance",
  "description": "Manages admin roster changes for a register via multi-sig quorum workflow",
  "version": "1.0.0",
  "participants": [
    { "id": "proposer", "name": "Proposer", "organisation": "Register Admin" },
    { "id": "voter", "name": "Voter", "organisation": "Register Admin" },
    { "id": "target", "name": "Target", "organisation": "New/Departing Admin" }
  ],
  "actions": [
    {
      "id": 0,
      "title": "Assert Ownership",
      "sender": "proposer",
      "isStartingAction": true,
      "routes": [
        { "id": "genesis-to-propose", "nextActionIds": [1], "isDefault": true }
      ]
    },
    {
      "id": 1,
      "title": "Propose Change",
      "sender": "proposer",
      "routes": [
        { "id": "transfer-skip-quorum", "nextActionIds": [3], "condition": { "==": [{ "var": "operationType" }, "Transfer"] } },
        { "id": "owner-override", "nextActionIds": [3], "condition": { "==": [{ "var": "ownerOverride" }, true] } },
        { "id": "to-quorum", "nextActionIds": [2], "isDefault": true }
      ]
    },
    {
      "id": 2,
      "title": "Collect Quorum",
      "sender": "voter",
      "routes": [
        { "id": "quorum-met", "nextActionIds": [3], "condition": { ">=": [{ "var": "approvalPercentage" }, 50.01] } },
        { "id": "quorum-blocked", "nextActionIds": [1], "condition": { "==": [{ "var": "quorumBlocked" }, true] } },
        { "id": "collect-more", "nextActionIds": [2], "isDefault": true }
      ]
    },
    {
      "id": 3,
      "title": "Accept Role",
      "sender": "target",
      "routes": [
        { "id": "accepted", "nextActionIds": [4], "condition": { "==": [{ "var": "accepted" }, true] } },
        { "id": "declined", "nextActionIds": [1], "isDefault": true }
      ]
    },
    {
      "id": 4,
      "title": "Record Control Transaction",
      "sender": "proposer",
      "routes": [
        { "id": "loop-back", "nextActionIds": [1], "isDefault": true }
      ]
    }
  ],
  "metadata": {
    "category": "governance",
    "type": "register-governance",
    "isSystem": "true",
    "hasCycles": "true"
  }
}
```
