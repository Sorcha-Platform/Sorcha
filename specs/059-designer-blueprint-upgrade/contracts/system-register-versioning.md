# API Contract: System Register Versioning

## Publish Blueprint (Modified)

`POST /api/system-register/publish`

Request (extended):
```json
{
  "blueprintId": "bp-001",
  "blueprint": { /* full blueprint JSON including instructions */ },
  "metadata": {
    "changeType": "structural",
    "structuralHash": "a1b2c3d4...64hex",
    "previousVersionMajor": 1,
    "previousVersionMinor": 2
  }
}
```

Response (extended):
```json
{
  "transactionId": "tx-...",
  "blueprintId": "bp-001",
  "version": {
    "major": 2,
    "minor": 0,
    "changeType": "structural",
    "structuralHash": "a1b2c3d4...64hex"
  },
  "publishedAt": "2026-03-16T14:30:00Z",
  "publishedBy": "wallet-address-..."
}
```

Version calculation:
- If `changeType` is "structural": `major = previousMajor + 1`, `minor = 0`
- If `changeType` is "documentation": `major = previousMajor`, `minor = previousMinor + 1`
- First publish: `major = 1`, `minor = 0`

## Query Blueprint Versions

`GET /api/system-register/blueprints/{blueprintId}/versions`

Response:
```json
{
  "blueprintId": "bp-001",
  "latestVersion": { "major": 2, "minor": 1 },
  "versions": [
    {
      "major": 1, "minor": 0,
      "changeType": "structural",
      "publishedAt": "2026-01-15T10:00:00Z",
      "publishedBy": "wallet-...",
      "transactionId": "tx-..."
    },
    {
      "major": 1, "minor": 1,
      "changeType": "documentation",
      "publishedAt": "2026-02-01T14:00:00Z",
      "publishedBy": "wallet-...",
      "transactionId": "tx-..."
    },
    {
      "major": 2, "minor": 0,
      "changeType": "structural",
      "publishedAt": "2026-03-10T09:00:00Z",
      "publishedBy": "wallet-...",
      "transactionId": "tx-..."
    },
    {
      "major": 2, "minor": 1,
      "changeType": "documentation",
      "publishedAt": "2026-03-16T14:30:00Z",
      "publishedBy": "wallet-...",
      "transactionId": "tx-..."
    }
  ]
}
```

## Structural Diff Endpoint

`POST /api/system-register/blueprints/{blueprintId}/classify-change`

Request:
```json
{
  "newBlueprint": { /* full blueprint JSON */ }
}
```

Response:
```json
{
  "changeType": "structural",
  "currentVersion": { "major": 1, "minor": 2 },
  "proposedVersion": { "major": 2, "minor": 0 },
  "structuralHashCurrent": "abc123...",
  "structuralHashNew": "def456...",
  "structuralFieldsChanged": true
}
```

If no previous version exists (first publish):
```json
{
  "changeType": "structural",
  "currentVersion": null,
  "proposedVersion": { "major": 1, "minor": 0 },
  "structuralHashNew": "def456...",
  "structuralFieldsChanged": true
}
```
