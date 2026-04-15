# Contract: `CredentialStatus` enum + state machine

**Feature**: 106-register-native-credentials
**Surface**: `Sorcha.Wallet.Portable.Domain.Entities.CredentialStatus` enum + `CredentialRepository.PatchStatusAsync` transition enforcement
**Layer**: Wallet Service domain model + repository
**Binds**: FR-006, FR-013, FR-014, FR-015, FR-024

## Enum definition

**Before Feature 106**:

```csharp
public enum CredentialStatus
{
    Active = 0,
    Expired = 1,
    Revoked = 2,
    Suspended = 3
}
```

**After Feature 106**:

```csharp
public enum CredentialStatus
{
    Active = 0,
    Expired = 1,
    Revoked = 2,
    Suspended = 3,
    PendingAcceptance = 4,  // NEW
    Declined = 5            // NEW
}
```

Numeric values are preserved for existing members so JSON serialisation and database storage remain backward compatible. New values append at the end.

**JSON serialisation**: via `JsonStringEnumConverter` on the containing DTO — clients see human-readable strings (`"PendingAcceptance"`, `"Declined"`) rather than integers. Unknown-value tolerance: older clients deserialising a response with the new values will throw unless they've been updated; but the feature ships together as one deploy so no mixed-version windows in practice.

## State machine

```text
                       ┌────────────────────────┐
                       │ [ inbound detection  ] │
                       │  by InboundCredential  │
                       │       Detector         │
                       └───────────┬────────────┘
                                   │
                                   ▼
                         ┌─────────────────────┐
                         │  PendingAcceptance  │
                         └─────────┬───────────┘
                                   │
              ┌────────────────────┼────────────────────┐
              │                    │                    │
              │ holder             │ holder             │ embedded notValidAfter
              │ clicks Accept      │ clicks Decline     │ passes
              ▼                    ▼                    ▼
         ┌──────────┐        ┌──────────┐         ┌──────────┐
         │  Active  │        │ Declined │         │  Expired │
         └────┬─────┘        └────┬─────┘         └──────────┘
              │                   │
              │ embedded           │ explicit
              │ notValidAfter      │ DELETE
              │ passes             │
              ▼                   ▼
         ┌──────────┐        (row removed)
         │  Expired │
         └──────────┘

    ┌──────────┐        ┌──────────┐
    │  Active  │───────►│  Revoked │
    └──────────┘        └──────────┘
    (issuer status list update — existing behaviour)

    ┌──────────┐        ┌──────────┐
    │  Active  │◄──────►│ Suspended│
    └──────────┘        └──────────┘
    (existing behaviour — bidirectional for temporary holds)
```

## Transition table

| From | To | Triggered by | Authoritative service | Notes |
|---|---|---|---|---|
| `(null)` | `PendingAcceptance` | Inbound credential detector on recipient-encrypted disclosure | Wallet Service | New row; dedup by credential id |
| `PendingAcceptance` | `Active` | Holder clicks Accept; PATCH `/credentials/{id}` | Wallet Service (local) + Blueprint Service (register tx) | Parallel client orchestration |
| `PendingAcceptance` | `Declined` | Holder clicks Decline; PATCH `/credentials/{id}` | Wallet Service (local) + Blueprint Service (reject tx) | Parallel client orchestration |
| `PendingAcceptance` | `Expired` | Embedded `notValidAfter` passed at access time | Wallet Service (passive check) | Computed on read; lazily reflected in DB |
| `Active` | `Expired` | Embedded `notValidAfter` passed | Wallet Service (passive check) | Existing behaviour, unchanged |
| `Active` | `Revoked` | Status list update from issuer | Wallet Service (periodic check) | Existing behaviour, unchanged |
| `Active` | `Suspended` | Issuer status list / admin action | Wallet Service | Existing behaviour, unchanged |
| `Suspended` | `Active` | Issuer status list / admin action | Wallet Service | Existing behaviour, unchanged |
| `Declined` | `(row removed)` | Holder explicit DELETE `/credentials/{id}` | Wallet Service | Audit row becomes unrecoverable after delete |
| Any other | Any other | (rejected) | 409 Conflict at PATCH endpoint | Enforces invariants |

### Invariant enforcement points

- **INV-1**: Enforced by the inbound detector's dedup check — a duplicate credential id by `GetByIdAsync` returns null and the detector skips the insert. No transition back into `PendingAcceptance` is possible because the second arrival is recognised as a replay.
- **INV-2**: Enforced by `CredentialRepository.PatchStatusAsync` precondition: `Active` and `Declined` targets are only permitted when the current row is in `PendingAcceptance`. `Expired` is not a client-directed transition — it's computed/observed.
- **INV-3**: Enforced by the same precondition: once in `Declined`, no PATCH can change the status.
- **INV-4**: Same mechanism: `Active` cannot re-enter `PendingAcceptance`, so decline from `Active` is impossible.

## Repository signature

```csharp
namespace Sorcha.Wallet.Service.Repositories;

public interface ICredentialRepository
{
    // existing methods unchanged...

    /// <summary>
    /// Updates the status of a credential, enforcing the state machine invariants
    /// defined in data-model.md. Throws InvalidOperationException on disallowed
    /// transitions. Callers should surface the exception as HTTP 409 Conflict.
    /// </summary>
    /// <returns>The updated credential entity.</returns>
    Task<CredentialEntity> PatchStatusAsync(
        string walletAddress,
        string credentialId,
        CredentialStatus newStatus,
        CancellationToken ct = default);
}
```

## Filter support

`GET /api/v1/wallets/{walletAddress}/credentials` gains a new optional query parameter:

```
?status=Active             (default if omitted — preserves existing caller behaviour)
?status=PendingAcceptance  (populates the MyCredentials PENDING tab)
?status=Declined           (future: declined history view)
?status=Expired            (existing — unchanged)
?status=Revoked            (existing — unchanged)
?status=Suspended          (existing — unchanged)
?status=All                (new convenience — returns everything)
```

The controller parses the query parameter into a `CredentialStatus?` or the `"All"` sentinel and passes it to the repository.

## SignalR notification shape

When a status changes, the Wallet Service emits an event on the existing `events:wallet` SignalR hub:

```csharp
public sealed record CredentialStatusChangedEvent
{
    public required string WalletAddress { get; init; }
    public required string CredentialId { get; init; }
    public required string CredentialType { get; init; }
    public required CredentialStatus PreviousStatus { get; init; }
    public required CredentialStatus NewStatus { get; init; }
    public required DateTimeOffset ChangedAt { get; init; }
}
```

UI consumers use this to refresh both `MyCredentials` and `MyActions` views without polling. For the specific "new `PendingAcceptance` arrival" case, the existing `InboundActionEvent` with the new `CredentialOfferId` field (see `inbound-credential-detection.md`) is the richer notification — `CredentialStatusChangedEvent` is for subsequent transitions after the initial arrival.

## Testing contract

- **Unit tests** for `CredentialRepository.PatchStatusAsync`:
  - Valid transitions (`PendingAcceptance` → `Active`, `PendingAcceptance` → `Declined`) succeed.
  - Invalid transitions (`Active` → `PendingAcceptance`, `Declined` → `Active`, etc.) throw.
  - Idempotent writes (`Active` → `Active`, `Declined` → `Declined`) are no-ops, not errors.
  - Row not found returns null (not a throw).

- **Unit tests** for the enum serialisation: round-trip JSON serialise/deserialise with `JsonStringEnumConverter`.

- **Integration tests** for the filter parameter:
  - `?status=PendingAcceptance` returns only pending rows.
  - `?status=All` returns everything.
  - `?status=` (empty) defaults to `Active`.
  - `?status=InvalidValue` returns 400 Bad Request.

- **Event emission tests**: assert that `CredentialStatusChangedEvent` fires on every successful status transition, with the right `PreviousStatus` and `NewStatus` fields populated.
