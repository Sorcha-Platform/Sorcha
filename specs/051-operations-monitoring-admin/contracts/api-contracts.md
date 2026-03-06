# API Contracts: Operations & Monitoring Admin (Feature 051)

All endpoints below already exist in their respective backend services. This document records the contracts that UI services and CLI commands will consume.

## Dashboard & Alerts (API Gateway)

### GET /api/dashboard
Returns aggregated system statistics.
```json
{
  "totalBlueprints": 12,
  "totalBlueprintInstances": 45,
  "activeBlueprintInstances": 8,
  "totalWallets": 23,
  "totalRegisters": 5,
  "totalTransactions": 1847,
  "totalTenants": 3,
  "connectedPeers": 4,
  "timestamp": "2026-03-06T12:00:00Z"
}
```

### GET /api/alerts
Returns active system alerts with severity counts.
```json
{
  "alerts": [
    {
      "id": "alert-001",
      "severity": "Warning",
      "source": "validator",
      "message": "Validation success rate below threshold",
      "metricName": "SuccessRate",
      "currentValue": 92.5,
      "threshold": 95.0,
      "timestamp": "2026-03-06T11:55:00Z"
    }
  ],
  "infoCount": 0,
  "warningCount": 1,
  "errorCount": 0,
  "criticalCount": 0,
  "totalCount": 1,
  "timestamp": "2026-03-06T12:00:00Z"
}
```

## Wallet Access Delegation (Wallet Service)

### POST /api/v1/wallets/{walletAddress}/access
Grant access to a wallet.
```json
// Request
{
  "subject": "user-123",
  "accessRight": "ReadWrite",
  "reason": "Team member needs signing access",
  "expiresAt": "2026-06-06T00:00:00Z"
}

// Response 200
{
  "id": "grant-001",
  "subject": "user-123",
  "accessRight": "ReadWrite",
  "grantedBy": "sorcha1abc123",
  "reason": "Team member needs signing access",
  "grantedAt": "2026-03-06T12:00:00Z",
  "expiresAt": "2026-06-06T00:00:00Z",
  "isActive": true
}
```

### GET /api/v1/wallets/{walletAddress}/access
List active access grants.
```json
// Response 200
[
  {
    "id": "grant-001",
    "subject": "user-123",
    "accessRight": "ReadWrite",
    "grantedBy": "sorcha1abc123",
    "reason": "Team member needs signing access",
    "grantedAt": "2026-03-06T12:00:00Z",
    "expiresAt": "2026-06-06T00:00:00Z",
    "isActive": true
  }
]
```

### DELETE /api/v1/wallets/{walletAddress}/access/{subject}
Revoke access. Returns 204 No Content.

### GET /api/v1/wallets/{walletAddress}/access/{subject}/check?requiredRight=ReadWrite
Check if subject has access.
```json
// Response 200
{
  "walletAddress": "sorcha1abc123",
  "subject": "user-123",
  "requiredRight": "ReadWrite",
  "hasAccess": true
}
```

## Schema Providers (Blueprint Service)

### GET /api/v1/schemas/providers
List all schema providers with health status.
```json
// Response 200
[
  {
    "providerName": "sorcha-official",
    "isEnabled": true,
    "baseUri": "https://schemas.sorcha.io",
    "providerType": "remote",
    "rateLimitPerSecond": 10,
    "refreshIntervalHours": 24,
    "lastSuccessfulFetch": "2026-03-06T08:00:00Z",
    "lastError": null,
    "lastErrorAt": null,
    "schemaCount": 42,
    "healthStatus": "Healthy",
    "backoffUntil": null,
    "consecutiveFailures": 0
  }
]
```

### POST /api/v1/schemas/providers/{providerName}/refresh
Trigger manual refresh. Returns 200 with updated provider status.

## Events Admin (Blueprint Service)

### GET /api/events/admin?severity={severity}&page={page}&pageSize={size}&since={iso8601}
List system events for all users in organization (admin only).
```json
// Response 200
{
  "events": [
    {
      "id": "evt-001",
      "type": "blueprint.published",
      "severity": "Info",
      "message": "Blueprint 'Onboarding' published",
      "source": "blueprint-service",
      "timestamp": "2026-03-06T11:30:00Z",
      "userId": "user-456",
      "metadata": {}
    }
  ],
  "totalCount": 150,
  "page": 1,
  "pageSize": 20
}
```

### DELETE /api/events/{id}
Delete a single event. Returns 204 No Content.

## Push Subscriptions (Tenant Service)

### POST /api/push-subscriptions
Register push subscription.
```json
// Request
{
  "endpoint": "https://fcm.googleapis.com/fcm/send/...",
  "p256dh": "BNc...",
  "auth": "abc..."
}

// Response 200
{ "subscribed": true }
```

### DELETE /api/push-subscriptions?endpoint={encodedEndpoint}
Remove push subscription. Returns 204 No Content.

### GET /api/push-subscriptions/status
Check subscription status.
```json
// Response 200
{ "hasActiveSubscription": true }
```

## Encryption Operations (Blueprint Service)

### GET /api/operations/{operationId}
Get encryption operation status.
```json
// Response 200
{
  "operationId": "op-001",
  "status": "Processing",
  "stage": "encrypting-per-recipient",
  "percentComplete": 45,
  "recipientCount": 5,
  "processedRecipients": 2,
  "submittedAt": "2026-03-06T12:00:00Z",
  "walletAddress": "sorcha1abc123"
}
```

## Credential Lifecycle Error Responses (Blueprint Service)

These existing responses need to be surfaced in the UI with specific messages:

| Status Code | Meaning | UI Message |
|-------------|---------|------------|
| 200 | Success | Operation completed successfully |
| 403 | Forbidden | "Permission denied: you are not the issuer of this credential" |
| 404 | Not Found | "Credential not found" |
| 409 | Conflict | "Credential is already {currentStatus}" |
| 500 | Server Error | "An unexpected error occurred. Please try again." |
