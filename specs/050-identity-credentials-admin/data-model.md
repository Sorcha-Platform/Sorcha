# Data Model: Identity & Credentials Admin

No new database entities are required. All data flows through existing backend APIs. This document defines the **view models** and **request/response models** used by UI services and CLI commands.

## UI View Models

### CredentialLifecycleRequest

Used by the credential lifecycle dialog to submit suspend/reinstate/revoke/refresh operations.

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| CredentialId | string | Yes | ID of the credential to operate on |
| IssuerWallet | string | Yes | Wallet address of the issuer performing the action |
| Action | enum | Yes | Suspend, Reinstate, Revoke, Refresh |
| Reason | string | No | Human-readable reason (suspend/reinstate/revoke) |
| NewExpiryDuration | string | No | ISO 8601 duration for refresh (e.g., "P365D") |

### CredentialLifecycleResult

Returned after a successful lifecycle operation.

| Field | Type | Description |
|-------|------|-------------|
| CredentialId | string | ID of the affected credential |
| NewStatus | string | Updated status (Suspended, Active, Revoked, Consumed) |
| PerformedBy | string | Wallet address that performed the action |
| PerformedAt | DateTimeOffset | When the action was performed |
| Reason | string? | Reason if provided |
| StatusListUpdated | bool | Whether the bitstring status list was updated |
| NewCredentialId | string? | ID of new credential (refresh only) |

### StatusListViewModel

Displayed in the status list viewer page.

| Field | Type | Description |
|-------|------|-------------|
| Id | string | Status list identifier |
| Purpose | string | "revocation" or "suspension" |
| IssuerDid | string | DID of the issuing authority |
| ValidFrom | DateTimeOffset | When the list was last updated |
| EncodedList | string | Base64-encoded compressed bitstring |
| ContextUrls | string[] | JSON-LD context URLs |

### CreatePresentationRequestViewModel

Used by the verifier admin page to create a presentation request.

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| CredentialType | string | Yes | Type of credential requested |
| AcceptedIssuers | string[] | No | List of accepted issuer DIDs |
| RequiredClaims | ClaimConstraint[] | No | Claims the holder must disclose |
| CallbackUrl | string | Yes | HTTPS URL for result callback |
| TargetWalletAddress | string | No | Specific wallet to target |
| TtlSeconds | int | No | Time-to-live (default 300) |
| VerifierIdentity | string | No | Display name of the verifier |

### PresentationRequestResultViewModel

Displayed when viewing a completed presentation request.

| Field | Type | Description |
|-------|------|-------------|
| RequestId | string | Presentation request ID |
| Status | string | Pending, Completed, Denied, Expired |
| QrCodeUrl | string | OID4VP authorize URL for QR code |
| RequestUrl | string | Direct URL to the request |
| ExpiresAt | DateTimeOffset | When the request expires |
| VerificationResult | object? | Verification outcome (when completed) |

## State Transitions

### Credential States

```
Active ──suspend──> Suspended
Active ──revoke───> Revoked
Suspended ──reinstate──> Active
Suspended ──revoke─────> Revoked
Expired ──refresh──> Consumed (+ new Active credential)
Revoked ──(terminal, no transitions)──
```

### Participant States

```
Active ──suspend──> Suspended
Active ──deactivate──> Inactive
Suspended ──reactivate──> Active
Suspended ──deactivate──> Inactive
Inactive ──(terminal, no transitions)──
```

### Presentation Request States

```
Pending ──submit──> Completed (with verification result)
Pending ──deny───> Denied
Pending ──timeout──> Expired
```
