# API Contract: Published Blueprints by Register

## GET /api/registers/{registerId}/blueprints/published (New)

Returns all blueprint-publish control transactions for a register. Used by Blueprint Service during startup recovery to rebuild the published blueprint index.

### Request

```
GET /api/registers/{registerId}/blueprints/published
Authorization: Bearer {jwt}
```

### Response (200 OK)

```json
{
  "registerId": "4f43bee55c334a4f8b5e2f2f3623b4e4",
  "blueprints": [
    {
      "blueprintId": "construction-permit-20260326185524",
      "transactionId": "a1b2c3d4...",
      "publishedBy": "system",
      "publishedAt": "2026-03-26T18:55:24Z",
      "blueprintJson": "{ full serialized blueprint }"
    }
  ],
  "registerHeight": 11,
  "queriedAt": "2026-03-26T19:30:00Z"
}
```

### Response Fields

| Field | Type | Description |
|-------|------|-------------|
| registerId | string | Register queried |
| blueprints | array | Published blueprint entries |
| blueprints[].blueprintId | string | Blueprint identifier |
| blueprints[].transactionId | string | Ledger transaction ID |
| blueprints[].publishedBy | string | Who published (wallet or "system") |
| blueprints[].publishedAt | string | ISO 8601 timestamp |
| blueprints[].blueprintJson | string | Full serialized blueprint JSON |
| registerHeight | int | Current register transaction count |
| queriedAt | string | When this query was executed |

### Error Responses

- **404 Not Found**: Register does not exist
- **401 Unauthorized**: Missing or invalid JWT
- **503 Service Unavailable**: Register storage unavailable

---

## GET /api/health (Modified — Blueprint Service)

### Current Response (200 OK)

```json
{
  "status": "healthy",
  "service": "blueprint-service",
  "timestamp": "2026-03-26T...",
  "version": "1.0.0",
  "uptime": "00:05:00",
  "metrics": {
    "totalBlueprints": 5,
    "publishedVersions": 12,
    "statusListAvailable": true
  }
}
```

### New Response During Recovery (503 Service Unavailable)

```json
{
  "status": "recovering",
  "service": "blueprint-service",
  "timestamp": "2026-03-26T...",
  "version": "1.0.0",
  "uptime": "00:00:05",
  "recovery": {
    "startedAt": "2026-03-26T...",
    "registersTotal": 3,
    "registersRecovered": 1,
    "registersOffline": 1,
    "registersPending": 1
  }
}
```

### New Response After Recovery (200 OK — additional fields)

```json
{
  "status": "healthy",
  "service": "blueprint-service",
  "timestamp": "2026-03-26T...",
  "version": "1.0.0",
  "uptime": "00:05:00",
  "metrics": {
    "totalBlueprints": 5,
    "publishedVersions": 12,
    "statusListAvailable": true
  },
  "registers": {
    "total": 3,
    "online": 2,
    "offline": 1,
    "lastRefresh": "2026-03-26T..."
  }
}
```
