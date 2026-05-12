# Contract — `IRegisterSubscriptionService` Audit Verdict

## Verdict

**USER-only. No split required. Folder move only.**

## Evidence

Phase 0 inspection of the interface surface:

```csharp
Task<List<RegisterSubscriptionDto>> GetMySubscribedRegistersAsync(CancellationToken ct = default);
Task<RegisterSubscriptionDto?> SubscribeAsync(Guid orgId, string registerId, string? registerName = null, string? description = null, CancellationToken ct = default);
Task<RegisterSubscriptionDto?> CreateOwnerSubscriptionAsync(Guid orgId, string registerId, string? registerName, CancellationToken ct = default);
Task<bool> UnsubscribeAsync(Guid orgId, string registerId, CancellationToken ct = default);
Task<List<AvailableRegisterDto>> GetAvailableRegistersAsync(CancellationToken ct = default);
```

All five methods are subscription-management operations from the perspective of the signed-in user (acting on behalf of one of their organisations). "Subscribe" / "Unsubscribe" are user-initiated actions even when the user is acting in an org-admin role. The interface is not bi-modal — there is no admin-only "manage all subscriptions across all orgs" surface here.

Note: `CreateOwnerSubscriptionAsync` is a user-initiated operation that happens to require org-owner permission server-side. The permission check is enforced at the API gateway, not at the client interface; from the client's perspective, the operation is user-acting.

## Action

| Step | Detail |
|---|---|
| 1 | Move `IRegisterSubscriptionService.cs` from `Services/IRegisterSubscriptionService.cs` to `Services/User/IRegisterSubscriptionService.cs` |
| 2 | Move `RegisterSubscriptionService.cs` (concrete) alongside it |
| 3 | DI registration unchanged |
| 4 | No consumer updates required |

## Verification

1. **Given** a user signed in as an org admin, **When** they call `CreateOwnerSubscriptionAsync` after the refactor, **Then** the operation behaves identically — same backend call, same response, same error handling.
2. **Given** the refactored codebase, **When** a developer browses `Services/User/`, **Then** they see `IRegisterSubscriptionService` and `IRegisterReadService` co-located, signalling that both are user-facing register-related interfaces.
