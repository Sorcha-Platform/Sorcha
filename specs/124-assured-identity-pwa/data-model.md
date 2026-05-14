# Phase 1 Data Model — AssuredIdentity on the PWA

**Feature**: 124-assured-identity-pwa
**Date**: 2026-05-14

Two records, one per side of the system boundary. Neither is an EF entity; neither requires a database migration.

## PendingApplicationNotice (server-side)

Server-side state representing that a citizen has an application in flight whose credential will eventually arrive at the wallet. Lives in distributed cache; never persisted to the relational store.

**Storage**: `IDistributedCache` (Redis in production, in-memory in tests) under key `sorcha:wallet:pending-app:{platformUserId:N}`. Absolute TTL 24 hours.

**Wire shape**:

```csharp
public sealed record PendingApplicationNotice(
    Guid PlatformUserId,
    string Label,           // e.g. "Assured Identity"
    DateTimeOffset SetAt    // UTC timestamp of set
);
```

**Fields**:

| Field | Type | Validation | Notes |
|-------|------|------------|-------|
| `PlatformUserId` | `Guid` | derived from caller's JWT, never client-supplied on read | identifies the citizen whose wallet shows the waiting state |
| `Label` | `string` | non-empty, ≤ 80 chars, plain text (no HTML, no claim values) | rendered inline in the wallet's waiting message |
| `SetAt` | `DateTimeOffset` | server-side; UTC | informational; not used for TTL (Redis TTL is authoritative) |

**Lifecycle**:

- **Created** by `SetPendingApplication` endpoint when the walkthrough script (or, in future, a council application submission flow) calls in. Idempotent — set replaces any prior notice for the same citizen.
- **Read** by `GetPendingApplication` endpoint, returning `null` if absent.
- **Cleared** by `ClearPendingApplication` endpoint or by TTL expiry (whichever comes first).

**Invariants**:

- One notice per citizen at a time. Setting a new label replaces the old.
- Notice MUST NOT contain credential content. Compile-time enforced by accepting only `string Label`.

## WalletFlags (PWA-side, per-device)

Client-side per-device state recording wallet-level flags. Initially carries only the welcome-takeover dismissal record; designed to accept more flags as later specs land (it is the natural home for "tour seen", "tutorial completed", "rate-limit-banner-dismissed", etc.).

**Storage**: IndexedDB store `device`, key `flags`. Same store as the existing `DeviceMetaRecord` (key `enrolment`) — a deliberate co-tenancy: both are "things this device knows about itself."

**Wire shape**:

```csharp
public sealed record WalletFlagsRecord(
    DateTimeOffset? WelcomedAt
);
```

**Fields**:

| Field | Type | Validation | Notes |
|-------|------|------------|-------|
| `WelcomedAt` | `DateTimeOffset?` | nullable; set once and never again on this device | the moment the citizen dismissed the first-credential welcome takeover |

**Lifecycle**:

- **Created lazily** the first time the wallet attempts to read or write flags. Default value: `WelcomedAt = null`.
- **Mutated** exactly once on first takeover dismissal: `WelcomedAt = now`. The store impl writes back the whole record.
- **Cleared** only if the wallet's IndexedDB is wiped (clear-site-data, uninstall+reinstall). Acceptable per the spec's edge case.

**Invariants**:

- `WelcomedAt` only ever transitions `null → non-null`. There is no "un-welcome" path.
- The flag is per-device — never synced to the server, never read by another device. Hence the local IndexedDB storage.

## Entity-relationship view

```text
┌─────────────────────────────────┐                       ┌──────────────────────────────┐
│ Server (Wallet Service)         │                       │ Device (Citizen Wallet PWA)  │
│                                 │                       │                              │
│  PendingApplicationNotice       │   set/clear/read      │  WalletFlagsRecord           │
│  (Redis-backed)                 │◄──────HTTP───────────►│  (IndexedDB-backed)          │
│  - PlatformUserId               │                       │  - WelcomedAt                │
│  - Label                        │                       │                              │
│  - SetAt                        │                       │                              │
└─────────────────────────────────┘                       └──────────────────────────────┘
        ▲                                                          ▲
        │ written by walkthrough script (Spec 1)                    │ written exactly once on takeover dismiss
        │ or council application submission (Spec 2 follow-up)      │ read on every wallet open + every sync completion
```

No foreign keys, no cross-entity invariants beyond "both can exist independently — they meet only at the wallet's render-time decision about whether to show the waiting state and/or the welcome takeover."

## State machines

### PendingApplicationNotice

```text
   (absent)
      │
      │  POST /api/v1/wallet/pending-applications  { label }
      ▼
   (present)  ────── 24h TTL expiry ──────►  (absent)
      │
      │  DELETE /api/v1/wallet/pending-applications
      ▼
   (absent)
```

Set is idempotent — `(present) ── set ──► (present, new label, fresh TTL)`.

### WalletFlags.WelcomedAt

```text
   (null)
      │
      │  takeover dismissed (Open button tapped)
      ▼
   (non-null, frozen)
```

One-way transition. No state machine afterwards.
