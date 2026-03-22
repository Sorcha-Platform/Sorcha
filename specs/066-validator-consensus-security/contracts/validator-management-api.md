# API Contract: Validator Management

**Service**: Validator Service (via API Gateway `/api/validators`)

## Existing Endpoints (no changes needed)

| Method | Path | Purpose |
|--------|------|---------|
| POST | `/register` | Register validator (creates Pending in consent mode) |
| GET | `/{registerId}` | List active validators |
| GET | `/{registerId}/pending` | List pending validators |
| GET | `/{registerId}/{validatorId}` | Get validator details |
| GET | `/{registerId}/count` | Get active validator count |
| POST | `/{registerId}/{validatorId}/approve` | Approve pending validator |
| POST | `/{registerId}/{validatorId}/reject` | Reject pending validator |
| POST | `/{registerId}/refresh` | Force refresh from chain |

## New Endpoints

### POST `/{registerId}/{validatorId}/suspend`

Suspend an active validator. Requires SystemAdmin authorization.

**Request**:
```json
{
  "suspendedBy": "string (wallet address)",
  "reason": "string"
}
```

**Response** (200):
```json
{
  "validatorId": "string",
  "registerId": "string",
  "status": "suspended",
  "suspendedAt": "2026-03-22T00:00:00Z",
  "suspendedBy": "string"
}
```

**Errors**:
- 400: Validator not active, or is last active validator
- 404: Validator not found

### POST `/{registerId}/{validatorId}/reactivate`

Reactivate a suspended validator. Requires SystemAdmin authorization.

**Request**:
```json
{
  "reactivatedBy": "string (wallet address)",
  "notes": "string?"
}
```

**Response** (200):
```json
{
  "validatorId": "string",
  "registerId": "string",
  "status": "active",
  "reactivatedAt": "2026-03-22T00:00:00Z"
}
```

**Errors**:
- 400: Validator not in Suspended state
- 404: Validator not found

### POST `/{registerId}/{validatorId}/revoke`

Permanently revoke a validator. Terminal state. Requires SystemAdmin authorization.

**Request**:
```json
{
  "revokedBy": "string (wallet address)",
  "reason": "string"
}
```

**Response** (200):
```json
{
  "validatorId": "string",
  "registerId": "string",
  "status": "revoked",
  "revokedAt": "2026-03-22T00:00:00Z",
  "revokedBy": "string"
}
```

**Errors**:
- 400: Validator already revoked, or is last active validator
- 404: Validator not found

### GET `/{registerId}/audit`

Get audit trail for all validators on a register. Requires SystemAdmin authorization.

**Query params**: `?validatorId=&limit=50&offset=0`

**Response** (200):
```json
{
  "registerId": "string",
  "entries": [
    {
      "validatorId": "string",
      "previousStatus": "active",
      "newStatus": "suspended",
      "performedBy": "string",
      "reason": "string?",
      "timestamp": "2026-03-22T00:00:00Z"
    }
  ],
  "total": 42
}
```

---

# API Contract: Transaction Replay Protection

### GET `/{registerId}/sequence/{walletAddress}`

Get the current sequence number for a wallet on a register.

**Response** (200):
```json
{
  "registerId": "string",
  "walletAddress": "string",
  "lastSequenceNumber": 42,
  "nextSequenceNumber": 43
}
```

**Response** (404 if no transactions yet):
```json
{
  "registerId": "string",
  "walletAddress": "string",
  "lastSequenceNumber": 0,
  "nextSequenceNumber": 1
}
```
