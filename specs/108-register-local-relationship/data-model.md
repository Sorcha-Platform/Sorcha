# Phase 1 Data Model — Register State Aggregation & Local Relationship

**Feature**: `108-register-local-relationship`
**Date**: 2026-04-21

All new types live under `Sorcha.Register.Models` (wire contracts) or `Sorcha.Register.Core` (derivation internals). No new database tables. Existing `Register` entity gets one field change and one converter.

---

## 1. `RegisterSyncState` (enum)

**Namespace**: `Sorcha.Register.Models.Enums`
**Replaces**: `Register.SyncState` string.

```csharp
public enum RegisterSyncState
{
    /// <summary>
    /// No recent peer evidence and no local authoritative claim — cannot determine health.
    /// Entered at startup before first advert, or when all adverts exceed the staleness window.
    /// </summary>
    Indeterminate = 0,

    /// <summary>
    /// Local docket height is strictly less than network high-water-mark.
    /// Actively catching up via pull replication.
    /// </summary>
    Syncing = 1,

    /// <summary>
    /// Local docket height matches the high-water-mark with sufficient advert confidence.
    /// </summary>
    CaughtUp = 2,

    /// <summary>
    /// Pull pipeline has failed repeatedly; sync cannot proceed until operator intervention.
    /// </summary>
    Error = 3
}
```

**Transition rules** (pure function, captured in `RegisterSyncStateResolver`):

| From | Condition | To |
|---|---|---|
| Any | `ObservationsCount(within staleness window) == 0` | `Indeterminate` |
| `Indeterminate` | first advert arrives with `networkHeight > localHeight` | `Syncing` |
| `Indeterminate` | first advert arrives with `networkHeight == localHeight` | `CaughtUp` |
| `Syncing` | `localHeight == networkHeight` AND (≥2 distinct peers agree OR single-peer-mode within window) | `CaughtUp` |
| `CaughtUp` | new advert with `networkHeight > localHeight` | `Syncing` |
| Any | consecutive-failure counter ≥ threshold | `Error` |
| `Error` | successful pull clears counter | `Syncing` |

**Invariants**:
- `Indeterminate` is always recoverable on next advert.
- `Error` is sticky until pull succeeds.
- Transition is a pure function of `(currentState, localHeight, observations, failureCount, now)`.

---

## 2. `RegisterLocalRelationship` (record)

**Namespace**: `Sorcha.Register.Models.LocalRelationship`

```csharp
public sealed record RegisterLocalRelationship(
    string RegisterId,
    RegisterRoleSet Roles,
    int ControlRecordVersion,       // docket number of the control tx this was derived from
    DateTimeOffset DerivedAt)
{
    public bool IsOwner       => Roles.HasFlag(RegisterRoleSet.Owner);
    public bool IsAdmin       => Roles.HasFlag(RegisterRoleSet.Admin);
    public bool IsAuditor     => Roles.HasFlag(RegisterRoleSet.Auditor);
    public bool IsDesigner    => Roles.HasFlag(RegisterRoleSet.Designer);
    public bool IsValidator   => Roles.HasFlag(RegisterRoleSet.Validator);
    public bool IsSubscriber  => !IsOwner && !IsAdmin && !IsValidator;     // no sealing or governance authority
}

[Flags]
public enum RegisterRoleSet
{
    None      = 0,
    Owner     = 1 << 0,
    Admin     = 1 << 1,
    Auditor   = 1 << 2,
    Designer  = 1 << 3,
    Validator = 1 << 4
    // Subscriber is derived (not flagged): IsSubscriber == true when no attestation role matches.
}
```

**Derivation inputs** (in `RegisterLocalRelationshipService`):
- Latest `RegisterControlRecord` for the register (from Mongo).
- `LocalIdentity`: a DI-provided record `{ WalletAddresses: string[], ValidatorPublicKey: byte[]? }`.

**Derivation rules**:
- For each `RegisterAttestation` in the control record whose `Subject` DID resolves to one of `LocalIdentity.WalletAddresses`, set the corresponding `RegisterRoleSet` flag (Owner / Admin / Auditor / Designer).
- If `LocalIdentity.ValidatorPublicKey` is non-null and matches any entry in `RegisterControlRecord.Validators.Validators.PublicKey`, set `Validator` flag.
- If none match, `Roles == None` → consumer treats as Subscriber via `IsSubscriber`.
- `ControlRecordVersion` = docket number of the latest docket containing a control tx (genesis = 0, governance ops bump it).

**Caching**: keyed by `registerId`, invalidated when a control tx is sealed (detected by `DocketWriteHandler`).

**Legacy fallback** (FR-020): if `RegisterControlRecord.Validators == null` (pre-086), treat the genesis `ProposerValidatorId` → proposer key as the sole validator entry. If that key matches `LocalIdentity.ValidatorPublicKey`, set `Validator` flag.

---

## 3. `PeerHeightObservation` (DTO)

**Namespace**: `Sorcha.Register.Models.Observations`

```csharp
public sealed record PeerHeightObservation(
    string RegisterId,
    string SourcePeerId,
    long NetworkHeight,
    DateTimeOffset ObservedAt);
```

**Lifecycle**:
- Produced by `Sorcha.Peer.Service.Replication.RegisterAdvertisementService` on every advert ingest.
- Posted via `POST /api/internal/registers/{registerId}/peer-height-observation` to Register.Service.
- Stored in `IObservationStore` as a per-peer upsert. No persistence.

**Validation**:
- `NetworkHeight >= 0`.
- `SourcePeerId` non-empty, matches a known peer or is rejected.
- `ObservedAt` within `± 5 minutes` of server clock (clock-skew bound — rejects poisoning via forged timestamps).

---

## 4. `ValidatorSealingObservation` (DTO)

**Namespace**: `Sorcha.Register.Models.Observations`

```csharp
public sealed record ValidatorSealingObservation(
    string RegisterId,
    long LastSealedHeight,
    int MempoolDepth,
    DateTimeOffset ObservedAt);
```

**Lifecycle**:
- Produced by `Sorcha.Validator.Service.Services.ValidationEngineService` on docket seal, and (throttled) on mempool-depth change.
- Posted via `POST /api/internal/registers/{registerId}/validator-observation` to Register.Service.
- Stored in `IObservationStore` as a single overwriting slot per register. No persistence.

**Validation**:
- Only accepted for registers where the caller's validator key is on the roster (Register.Service derives the caller's relationship and rejects if `!IsValidator`).
- `LastSealedHeight >= 0`, `MempoolDepth >= 0`.

---

## 5. `IObservationStore` (internal interface)

**Namespace**: `Sorcha.Register.Core.Observations`

```csharp
public interface IObservationStore
{
    void RecordPeerHeight(PeerHeightObservation observation);
    void RecordValidatorSealing(ValidatorSealingObservation observation);

    IReadOnlyList<PeerHeightObservation> GetRecentPeerHeights(string registerId, TimeSpan stalenessWindow);
    ValidatorSealingObservation? GetLatestValidatorSealing(string registerId);
}
```

**Implementation notes**:
- Per-register `ConcurrentDictionary<string, PeerHeightObservation>` (key = `SourcePeerId`).
- Per-register single-slot `ConcurrentDictionary<string, ValidatorSealingObservation>`.
- Periodic pruner (`IHostedService`) removes registers with no observations older than 30 minutes — prevents memory growth if a register is deleted.
- Cap per-register distinct peer count at 16; new entries evict the oldest.

---

## 6. `Register` entity change

**Namespace**: `Sorcha.Register.Models`

**Before**:
```csharp
public string? SyncState { get; set; }
```

**After**:
```csharp
[BsonRepresentation(BsonType.String)]   // persisted as the enum name for forward-compat
public RegisterSyncState? SyncState { get; set; }
```

**Migration** (D10): Mongo BSON converter handles legacy string values on read:
- `"Subscribing"` / `"Syncing"` → `RegisterSyncState.Syncing`
- `"Synced"` → `RegisterSyncState.CaughtUp`
- `"Error"` → `RegisterSyncState.Error`
- `null` → `RegisterSyncState.Indeterminate`
- Unknown strings → logged warning, treated as `Indeterminate`.

First write of a register document after the feature lands persists the enum-name form, migrating opportunistically. No big-bang migration job.

---

## 7. `RegisterRelationshipChangedEvent` (Redis pub/sub payload)

**Namespace**: `Sorcha.Register.Models.Events` (colocated with existing `TransactionConfirmedEvent`, `DocketConfirmedEvent`).

```csharp
public sealed record RegisterRelationshipChangedEvent(
    string RegisterId,
    int ControlRecordVersion,
    RegisterRoleSet AddedRoles,       // roles present now that weren't before
    RegisterRoleSet RemovedRoles,     // roles present before that aren't now
    DateTimeOffset ChangedAt);
```

**Channel**: `register:relationship-changed`
**Publisher**: Register.Service `RelationshipChangeNotifier` on control-tx seal.
**Subscribers**: Validator.Service `RegisterMonitoringBootstrap`.

Note: `AddedRoles` and `RemovedRoles` are **relative to the local node's identity** — the event is scoped to *this* installation's view. Each node publishes its own events based on its own identity; there's no cross-node broadcast of relationship changes.

---

## 8. `RegisterSyncStateView` (read model)

**Namespace**: `Sorcha.Register.Models.LocalRelationship`

Returned by `GET /api/registers/{id}/sync-state`.

```csharp
public sealed record RegisterSyncStateView(
    string RegisterId,
    RegisterSyncState State,
    long LocalHeight,
    long? NetworkHeightHighWaterMark,
    int DistinctPeerObservers,                // count of peers with observations within staleness window
    DateTimeOffset? LastAdvertAt,
    bool SinglePeerMode,                       // true when only one source peer is observed
    string? LastErrorMessage,                  // populated when State == Error
    ValidatorSealingSnapshot? ValidatorSnapshot); // populated when local IsValidator for this register

public sealed record ValidatorSealingSnapshot(
    long LastSealedHeight,
    int MempoolDepth,
    DateTimeOffset ObservedAt);
```

Operators consuming this can see *why* the system reports a given state (FR-009).

---

## 9. Relationships summary

```text
RegisterControlRecord (existing, read-only)
    │
    ▼
RegisterLocalRelationshipService.Derive(registerId, localIdentity)
    │
    ▼
RegisterLocalRelationship (cached, invalidated on control-tx seal)
    │
    ├── consumed by GET /api/registers/{id}/local-relationship
    ├── consumed by GET /api/internal/my-validated-registers (filtered to IsValidator)
    └── change triggers RegisterRelationshipChangedEvent → Redis → Validator.Service enrolment refresh

PeerHeightObservation ─────┐
ValidatorSealingObservation┤
                           ├─► IObservationStore
                           │
                           ▼
                   RegisterSyncStateResolver.Resolve(registerId, localHeight, observations)
                           │
                           ▼
                    RegisterSyncStateView (returned by GET /api/registers/{id}/sync-state)

ActionExecutionService.ExecuteAsync
    ├─► IValidatorServiceClient.SubmitTransactionAsync  (local mempool, seals iff enrolled)
    └─► IPeerServiceClient.DistributeTransactionAsync   (gossip to SourcePeerIds)
             │
             ▼
    Peer.Service.TransactionDistributionService
             │
             ▼
    Owner peer's Peer.Service.DocketSyncGrpcService.SubmitTransaction (new RPC)
             │
             ▼
    Owner's IValidatorServiceClient.SubmitTransactionAsync (seals — it IS enrolled)
```

---

## 10. Out of this data model

- No new Mongo collection.
- No new Postgres tables (Peer.Service EF model unchanged).
- No change to blueprint, wallet, or tenant models.
- `IRegisterMonitoringRegistry` remains an in-memory HashSet<string> inside Validator.Service; only its *population path* changes (bootstrap + event-driven, no more side-effect enrol).
