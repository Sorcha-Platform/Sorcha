# API Contract: Revocation Transactions

**Service**: Register Service + Validator Service
**Base Path**: `/api/registers/{registerId}`

## Endpoints

### POST /transactions/revoke

Submit a revocation transaction to revoke a previously sealed transaction.

**Authorization**: `CanSubmitTransactions` policy (user must be original signer or roster Owner/Admin)

**Request Body**:
```json
{
  "originalTxId": "e5f6a7b8...",
  "reason": "erroneous",
  "supersededByTxId": null,
  "metadata": {
    "note": "Incorrect serial number in original submission"
  },
  "signerWalletAddress": "sorcha1abc..."
}
```

**Reason Values**: `superseded`, `erroneous`, `compromised`, `expired`, `withdrawn`, `regulatory`

**Validation Rules**:
- `originalTxId` must reference a sealed transaction on this register
- Target transaction must not already be revoked
- Target transaction must not be a Revocation transaction (no revoking revocations)
- `supersededByTxId` required when `reason = superseded`, forbidden otherwise
- Signer must be original transaction signer OR Owner/Admin in governance roster
- `metadata` max 10 entries, keys max 50 chars, values max 500 chars

**Response 202 Accepted** (submitted to validation pipeline):
```json
{
  "revocationTxId": "rev-a1b2c3...",
  "originalTxId": "e5f6a7b8...",
  "status": "submitted",
  "message": "Revocation transaction submitted for validation and sealing"
}
```

**Response 400 Bad Request**:
```json
{
  "error": "invalid_revocation",
  "message": "Target transaction e5f6a7b8 is already revoked",
  "code": "ALREADY_REVOKED"
}
```

**Error Codes**:
| Code | Description |
|------|-------------|
| TARGET_NOT_FOUND | Original transaction doesn't exist on this register |
| ALREADY_REVOKED | Target transaction is already revoked |
| CANNOT_REVOKE_REVOCATION | Cannot revoke a revocation transaction |
| UNAUTHORIZED_REVOKER | Signer is neither original signer nor roster Owner/Admin |
| SUPERSEDED_TX_REQUIRED | Reason is "superseded" but no supersededByTxId provided |
| SUPERSEDED_TX_FORBIDDEN | Reason is not "superseded" but supersededByTxId was provided |
| INVALID_REASON | Unrecognised revocation reason |

### GET /transactions/{txId}/status

Get the lifecycle status of a transaction (active, revoked, or superseded).

**Authorization**: `CanReadTransactions` policy

**Path Parameters**:
| Parameter | Type | Description |
|-----------|------|-------------|
| registerId | string | Register ID |
| txId | string | Transaction ID |

**Response 200 OK (active)**:
```json
{
  "transactionId": "e5f6a7b8...",
  "status": "active",
  "revocationTxId": null,
  "supersededByTxId": null,
  "revokedAt": null,
  "reason": null
}
```

**Response 200 OK (revoked)**:
```json
{
  "transactionId": "e5f6a7b8...",
  "status": "revoked",
  "revocationTxId": "rev-a1b2c3...",
  "supersededByTxId": null,
  "revokedAt": "2026-03-31T14:00:00Z",
  "reason": "erroneous"
}
```

**Response 200 OK (superseded)**:
```json
{
  "transactionId": "e5f6a7b8...",
  "status": "superseded",
  "revocationTxId": "rev-a1b2c3...",
  "supersededByTxId": "new-d4e5f6...",
  "revokedAt": "2026-03-31T14:00:00Z",
  "reason": "superseded"
}
```

**Response 404 Not Found**: Transaction doesn't exist.

## Validator Validation Rules

When a `TransactionType.Revocation` transaction enters the validation pipeline:

1. **Structure**: Standard transaction structure validation
2. **Payload**: Parse `RevocationPayload` from transaction payload
3. **Target Check**: Query register for `originalTxId` — must exist and be sealed
4. **Double-Revocation Check**: Query for existing revocations of `originalTxId` — must be none
5. **Self-Revocation Check**: `originalTxId` must not be a Revocation transaction
6. **Authority Check**:
   a. Compare revocation `senderWallet` with target transaction's `senderWallet` — if match, authorised
   b. If no match, reconstruct governance roster, check revoker has Owner or Admin role
7. **Reason Validation**: `reason` must be a valid `RevocationReason` enum value
8. **Supersession Validation**: If `reason = Superseded`, `supersededByTxId` must be present
9. **Signature**: Standard cryptographic signature verification
10. **Sequence**: Standard per-wallet sequence number check

**Error Codes** (Validator):
| Code | Description |
|------|-------------|
| VAL_REV_001 | Invalid revocation payload structure |
| VAL_REV_002 | Target transaction not found |
| VAL_REV_003 | Target already revoked |
| VAL_REV_004 | Cannot revoke a revocation transaction |
| VAL_REV_005 | Revoker not authorised (not original signer, not roster Owner/Admin) |
| VAL_REV_006 | Invalid revocation reason |
| VAL_REV_007 | Superseded reason requires supersededByTxId |
