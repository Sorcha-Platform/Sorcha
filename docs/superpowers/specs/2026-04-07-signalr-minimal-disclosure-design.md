# SignalR Minimal Disclosure & Notification Fix

**Date:** 2026-04-07
**Status:** Approved
**Approach:** A (Minimal Signal + Pull-Back) with instanceId

## Problem Statement

The SignalR notification pipeline has three interlocking issues:

1. **Notification delivery bug** — `ActionAvailable` signals are not reaching agents in practice. Docker logs show zero "Sent ActionAvailable" entries during agent test runs, forcing agents to rely on 30s polling.
2. **Over-disclosure** — Notification payloads carry action titles, participant IDs, blueprint IDs, sender names, disclosed field summaries, and other workflow metadata. This leaks business logic and participant identity even to legitimate recipients who don't need it.
3. **Insufficient subscription authorization** — The `instance:{id}` group has no access control (anyone with a valid JWT could subscribe). Service tokens without `org_id` are warned but allowed to subscribe to any wallet.

These create a signals intelligence risk: a compromised or curious participant could passively monitor workflow activity patterns, participant relationships, and timing across instances they have no business observing.

## Design Principles

- **SignalR as trigger, not data pipe** — Signals tell clients "something changed", not "here's what changed". Smaller payloads, less exposure, better performance.
- **Pull-back for detail** — Clients fetch details through authenticated REST endpoints that enforce proper access control.
- **Wallet-only delivery** — All action signals route through `wallet:{address}` groups, which have ownership validation. No unauthenticated group paths.
- **Fail closed** — If ownership can't be verified, deny the subscription.

## Section 1: Signal Payload Contracts

All SignalR notifications become thin triggers with a universal shape:

```csharp
public record SignalNotification
{
    public required string SignalType { get; init; }
    public required string InstanceId { get; init; }
    public Guid? CorrelationId { get; init; }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}
```

### Action Signal Types

| Signal Type | Current Payload Fields Removed | Kept |
|---|---|---|
| `action-available` | ActionTitle, ActionId, ParticipantId, WalletAddress, BlueprintId, TransactionHash, Message | InstanceId, CorrelationId |
| `action-rejected` | RejectedActionId, TargetActionId, TargetParticipantId, Reason | InstanceId, CorrelationId |
| `workflow-completed` | *(was already minimal)* | InstanceId |

### Encryption Signals

Encryption progress keeps a dedicated shape — percent and status are operational UX, not sensitive data:

```csharp
public record EncryptionSignal
{
    public required string OperationId { get; init; }
    public required int PercentComplete { get; init; }
    public required string Status { get; init; }  // "encrypting", "complete", "failed"
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}
```

Removed from encryption signals: `RecipientName`, `DisclosedFieldsSummary`, `StepName`, `FailedRecipient`, `ErrorMessage`. Available via pull-back if the UI needs detail.

### EventsHub Inbound Action Signal

Currently the richest payload (blueprint name, sender name, navigation path, summary, urgency, deadline). Becomes:

```csharp
// SignalNotification delivered to user:{userId} group
{ signalType: "inbound-action", instanceId: "...", correlationId: "..." }
```

The EventsHubNotificationBridge continues to enrich and persist the full activity event to Tenant Service — the SignalR signal is just the trigger for the UI to refresh from the activity feed.

## Section 2: Group Model & Subscription Authorization Hardening

### Group Decisions

| Group | Verdict | Reason |
|---|---|---|
| `wallet:{address}` | **Keep** | Proper ownership validation, primary delivery channel |
| `instance:{id}` | **Remove** | Dead letter (no subscribers today), no authz, SIGINT risk |
| `user:{userId}` | **Keep** | Scoped to authenticated user, EventsHub delivery |
| `org:{orgId}` | **Keep** | Admin-gated, org-level events |
| `register:{registerId}` | **Keep** | Subscription-validated via RegisterHub |

All action-related signals (`action-available`, `action-rejected`, `workflow-completed`) route exclusively through `wallet:{address}`.

### Service Token Loophole Closure

```csharp
// Current: warn but allow
if (string.IsNullOrWhiteSpace(orgId))
    _logger.LogWarning("Service token without org_id...");

// New: reject
if (string.IsNullOrWhiteSpace(orgId))
    throw new HubException("Unauthorized: service tokens must include org_id claim");
```

Breaking change for service tokens missing `org_id`. Since Sorcha issues all service tokens, all callers must be verified to include `org_id` (Aspire service defaults should handle this).

### No New Subscription Methods

Hub surface stays the same: `SubscribeToWallet` / `UnsubscribeFromWallet`. Fewer methods, fewer attack surfaces.

## Section 3: Pull-Back Mechanism

Clients receive the thin signal, then pull details through existing authenticated endpoints:

| Signal | Pull-Back Endpoint | Exists |
|---|---|---|
| `action-available` | `GET /api/instances/{instanceId}` | Yes |
| `action-rejected` | `GET /api/instances/{instanceId}` | Yes |
| `workflow-completed` | `GET /api/instances/{instanceId}` | Yes |
| `inbound-action` | `GET /api/activity` (Tenant Service) | Yes |
| `encryption-progress` | `GET /api/operations/{operationId}` | Yes |

No new endpoints required. The pull goes through normal authn/authz which enforces access control on the data, not just on the notification channel.

## Section 4: Notification Delivery Bug Fix

### Root Cause

The `instance:{id}` group broadcast was the primary notification path, but no client ever subscribes to instance groups — making it a dead letter. The wallet-targeted broadcast was conditional on `instance.ParticipantWallets[participantId]` having a value, which may be null if the participant hasn't linked a wallet.

### Fix

- **Remove instance group broadcasting entirely** (per Section 2)
- **Make wallet group the sole delivery channel** — if `ParticipantWallets[participantId]` is null, log a warning. This is a configuration issue: participants must link a wallet before they can receive real-time signals. The 30s polling fallback in their client still works regardless.
- **Add structured logging** around notification sends: signal type, wallet address, instanceId — for delivery tracing in Docker logs
- **No changes to ActionExecutionService sequencing** — `ProcessActionCompletionAsync` runs after `WaitForTransactionConfirmationAsync`, which is correct

## Section 5: Client-Side Changes

### Sorcha.Agent (SignalRInboxListener)

- Receive `SignalNotification` instead of `ActionNotification`
- Extract `instanceId`, trigger immediate instance poll
- Existing 30s polling loop continues as fallback floor
- Remove payload-specific deserialization logic

### Sorcha.ServiceClients.Http (Shared SignalR Client)

- Replace `ActionNotification` record with `SignalNotification`
- Replace `InboundActionNotification` with thin signal contract
- Update `EncryptionProgressNotification` etc. to `EncryptionSignal`

### Sorcha.UI (Blazor)

- Notification toasts show generic "New action available" with link
- Richer detail loads on click or component mount
- Encryption progress bar driven by inline `PercentComplete` + `Status`

### Backward Compatibility

Breaking change to SignalR contract. All clients (Agent, UI, ServiceClients.Http, CLI) updated in one pass. No versioning needed.

## Section 6: Connection Resilience

The resilience model is belt-and-braces:

- **SignalR connection drop** — Polly retry policy for reconnection (exponential backoff, existing pattern in `SorchaHubConnectionBuilder`)
- **While disconnected** — 30s polling continues as the fallback floor, ensuring no signals are missed during the gap
- **On reconnect** — immediate poll to catch anything missed during disconnection, then resume signal-driven mode

SignalR is the fast path (sub-second). Polling is the safety net. Both always active.

## Scope

### In Scope

- Thin signal payloads for all notification types
- Remove `instance:{id}` group broadcasting
- Close service-token-without-org_id loophole
- Fix notification delivery (wallet-only targeting)
- Update all clients (Agent, UI, ServiceClients.Http)
- Structured logging for delivery tracing
- Polly reconnection with immediate poll on reconnect

### Out of Scope

- Encrypted signal envelopes — overkill given TLS + authn
- New pull-back endpoints — all exist
- EventsHubNotificationBridge enrichment logic changes — still enriches and persists, just stops sending rich payloads over SignalR
- HTTPS enforcement (SEC-001) — separate P0
- RegisterHub or org-level event changes
- Changes to the 30s polling interval

### Risk

- Breaking SignalR contract across all clients — mitigated by controlling all clients
- If SignalR drops, fallback to polling (existing, unchanged)
- Encryption operation pull-back assumes in-memory store hasn't evicted — TTL to be verified during implementation
