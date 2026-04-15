# Contract: Instance mirror reconstructor (Blueprint Service)

**Feature**: 106-register-native-credentials
**Surface**: `InstanceMirrorReconstructor` background service
**Layer**: Blueprint Service — new background worker subscribing to `docket:confirmed` Redis events
**Binds**: FR-010, FR-011, FR-012, FR-018, FR-019

## Responsibility

On a Sorcha node where a holder's wallet lives but the issuer's blueprint service does not, the local Blueprint Service has no native knowledge of the instances the holder is a participant in. The reconstructor fills that gap by observing peer-replicated register events, identifying transactions whose `participantWallets` include a locally-owned wallet, and building read-only `Instance` rows in the local Blueprint Service DB from the observed transaction content. Reconstructed rows are flagged `IsReadOnlyMirror = true` so the normal execution pipeline cannot mutate them — only the reconstructor pathway writes to mirror rows.

The result: `GetPendingActionsByWalletAsync` on the holder's node (which, after Fix A in PR #288, resolves wallets via live Wallet Service lookup) surfaces Action 3 in the holder's MyActions pending list without any direct RPC between the holder's node and the issuer's node. Everything flows through the register.

## Trigger

Subscribes to the Redis pub/sub channel `docket:confirmed`, the same channel `TransactionLifecycleEventBridge` already consumes for Feature 104 transaction lifecycle ticks. Messages carry the confirmed transaction's id, register id, and docket number. The reconstructor fetches the full transaction via `IRegisterServiceClient.GetTransactionAsync` and inspects it.

## Interface

```csharp
namespace Sorcha.Blueprint.Service.Services.Implementation;

public sealed class InstanceMirrorReconstructor : BackgroundService
{
    private readonly ISubscriber _redisSubscriber;
    private readonly IRegisterServiceClient _registerClient;
    private readonly IWalletServiceClient _walletClient;
    private readonly IInstanceStore _instanceStore;
    private readonly IBlueprintCache _blueprintCache;
    private readonly ILogger<InstanceMirrorReconstructor> _logger;
    private readonly InstanceMirrorMetrics _metrics;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await _redisSubscriber.SubscribeAsync("docket:confirmed", async (_, message) =>
        {
            try
            {
                var evt = JsonSerializer.Deserialize<DocketConfirmedEvent>(message!);
                if (evt is null) return;

                // Fetch the transaction — only validator-confirmed transactions trust-reach the reconstructor
                var tx = await _registerClient.GetTransactionAsync(evt.RegisterId, evt.TransactionId, stoppingToken);
                if (tx is null || tx.ValidatorConfirmations < 1) return;

                // Check if any participant wallet is locally owned
                var locallyOwnedWallets = await GetLocallyOwnedParticipantWalletsAsync(tx, stoppingToken);
                if (locallyOwnedWallets.Count == 0) return;

                // Reconstruct or advance the mirror for this instance
                await ReconstructOrAdvanceAsync(tx, locallyOwnedWallets, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Mirror reconstruction failed for docket-confirmed event: {Message}", message);
                _metrics.RecordError();
            }
        });

        await Task.Delay(Timeout.Infinite, stoppingToken);
    }
}
```

## Reconstruction rules

1. **Trust only validator-confirmed transactions.** The reconstructor MUST verify `tx.ValidatorConfirmations >= 1` before trusting any content. Arbitrary peer gossip is not trusted — the reconstructor only acts on transactions that are sealed in a docket a local validator has confirmed. This matches the trust model `NotificationDeliveryService` already uses.

2. **Create mirror rows at the first observed transaction.** If no local `Instance` row exists for the transaction's `instanceId`, create one with:
   - `Id = tx.InstanceId`
   - `BlueprintId = tx.BlueprintId`
   - `RegisterId = tx.RegisterId`
   - `State = InstanceState.Active`
   - `ParticipantWallets = tx.MetaData.ParticipantWallets` (copied from the first transaction that establishes the binding)
   - `CurrentActionIds = [tx.NextActionId]` (the action the holder can take next)
   - `CreatedAt = tx.TimeStamp`
   - `UpdatedAt = tx.TimeStamp`
   - `Version = 1`
   - `IsReadOnlyMirror = true` ← the load-bearing flag

3. **Advance mirror rows at subsequent observed transactions.** If the row already exists:
   - Update `CurrentActionIds` based on the transaction's `nextActionId` (advance only — never roll back)
   - Update `PendingActionPayloads` from the transaction's seeded payload if present
   - Update `UpdatedAt`, increment `Version`
   - MUST go through the new `IInstanceStore.UpdateMirrorAsync` method, which is the only path that bypasses the `IsReadOnlyMirror` write guard.

4. **Never write a mirror as anything other than `Active` state.** Terminal state transitions for instances are the responsibility of the instance's home node — a holder's mirror observing a rejection transaction surfaces the state change for UI purposes but doesn't own the authoritative instance closure.

5. **Idempotency.** The reconstructor MUST handle replays gracefully. The `docket:confirmed` Redis channel may re-deliver messages on subscriber reconnect. Replaying a reconstruction for a transaction that's already been applied MUST be a no-op, not a duplicate write.

## Read-only mirror write guard

`IInstanceStore.UpdateAsync` gains a precondition check:

```csharp
public async Task<bool> UpdateAsync(Instance instance, CancellationToken ct = default)
{
    var existing = await LoadEntityAsync(instance.Id, ct);
    if (existing is not null && existing.IsReadOnlyMirror)
    {
        throw new InvalidOperationException(
            $"Cannot call UpdateAsync on read-only mirror instance {instance.Id}. " +
            $"Only InstanceMirrorReconstructor may write to mirror rows via UpdateMirrorAsync. " +
            $"This usually means the normal execution pipeline tried to advance an instance that " +
            $"originates on a different node.");
    }
    // ... existing update path ...
}

public async Task<bool> UpdateMirrorAsync(Instance mirrorInstance, CancellationToken ct = default)
{
    // New method — explicitly bypasses the read-only guard.
    // Internal visibility — only InstanceMirrorReconstructor should call this.
    // ...
}
```

The new `UpdateMirrorAsync` method is marked `internal` with `InternalsVisibleTo` on the reconstructor test project so it cannot be called accidentally from application code.

## Blueprint availability

The reconstructor needs the blueprint definition to resolve action titles, schemas, and next-action routing hints for the mirror row. It uses `IBlueprintCache.GetBlueprintAsync` — the existing cache that handles blueprint lookups with a fall-through to the Blueprint Service's own store. If the blueprint is not yet synced on the holder's node, reconstruction for that instance is **deferred** — the reconstructor logs a warning and moves on. A retry opportunity comes when the blueprint eventually syncs (via the existing blueprint sync path) or when another transaction for the same instance arrives and retriggers reconstruction.

## Metrics (new)

```
instance_mirror_reconstructed_total{outcome="created"}
  — new mirror row created
instance_mirror_reconstructed_total{outcome="advanced"}
  — existing mirror advanced to next action
instance_mirror_reconstructed_total{outcome="skipped",reason="no-local-wallet"}
  — transaction's participants don't include any local wallet
instance_mirror_reconstructed_total{outcome="skipped",reason="blueprint-missing"}
  — can't reconstruct because blueprint isn't synced yet
instance_mirror_reconstructed_total{outcome="skipped",reason="not-confirmed"}
  — transaction not yet validator-confirmed
instance_mirror_reconstructed_total{outcome="error"}
  — unexpected exception

instance_mirror_reconstruction_duration_seconds{outcome=...}
  — histogram of reconstruction latency per outcome
```

## Interaction with the existing execution pipeline

The existing `ActionExecutionService.ExecuteAsync` is the write path for instances the local node authoritatively owns. Nothing about that code path changes — it still operates on non-mirror `Instance` rows via the existing `UpdateAsync` call.

The separation is clean:

- **Locally-owned instances**: created by `CreateAsync` from a local user submission, advanced by `UpdateAsync` from local action executions. `IsReadOnlyMirror = false` throughout.
- **Mirrored instances**: created by `CreateMirrorAsync` from the reconstructor, advanced by `UpdateMirrorAsync` from the reconstructor. `IsReadOnlyMirror = true` throughout.
- **Attempted write mismatch**: throws `InvalidOperationException`. Never happens in correct code; the exception exists to catch bugs early.

## Cross-node correctness proof sketch

The spec's User Story 1 requires that a holder on node B can see pending actions from an instance authored on node A without any direct communication between B and A. The flow:

1. Assessor executes Action 2 on node A → new transaction sealed to register R, docket confirmed on node A's validator.
2. Peer sync replicates the sealed transaction to node B (via existing register peer sync, unchanged).
3. Node B's validator confirms the transaction (standard peer-sync validation).
4. Node B's Redis `docket:confirmed` fires for the transaction.
5. Node B's `InstanceMirrorReconstructor` sees the event, fetches the transaction, confirms `ValidatorConfirmations >= 1`, checks `participantWallets` against locally-owned wallets via `IWalletServiceClient.GetWalletsByOwnerAsync`, finds a match, reconstructs the mirror row with `CurrentActionIds = [3]`, `IsReadOnlyMirror = true`.
6. Node B's holder opens MyActions → `/api/actions/pending` runs → Fix A's wallet resolution fallback returns the holder's wallet → `GetPendingActionsByWalletAsync` queries the local `Instances` table → finds the reconstructed mirror row → returns Action 3 as a pending action.
7. The existing claim-card dispatch (after PR #290) renders the `CredentialClaimCard` with the credential offer data pulled from the `PendingAcceptance` credential the Wallet Service already persisted via the inbound detector.

At no point did node B call node A. The register is the sole cross-node channel.

## Testing contract

- **Unit tests** for `InstanceMirrorReconstructor` (in `Sorcha.UI.Core.Tests` or a new dedicated project — not `Sorcha.Blueprint.Service.Tests` due to pre-existing constructor failures):
  - Happy path: mock a `docket:confirmed` event, mock the transaction, mock the wallet service to return a local wallet, assert `CreateMirrorAsync` is called with the right payload.
  - Skip on no local wallet: mock the wallet service to return empty → assert no write.
  - Skip on unconfirmed tx: mock transaction with `ValidatorConfirmations = 0` → assert no write.
  - Blueprint missing: mock cache to return null → assert warning logged + no write.
  - Advance existing: mock existing mirror row → assert `UpdateMirrorAsync` is called with advanced `CurrentActionIds`.
  - Replay safety: call twice with the same event → assert idempotent (no duplicate write).
- **Integration test** (end-to-end, via two-node docker-compose): submit an action on node A, assert node B's mirror reconstructs within 30 seconds of the docket confirmation.
- **Invariant test**: attempt to call `UpdateAsync` on a mirror row from a non-reconstructor caller → assert `InvalidOperationException`.
