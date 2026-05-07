# Feature 114 — US4 Implementation Plan

**User Story 4 (P3): Receive a newly-issued credential automatically.**

When a blueprint action issues a credential whose recipient is a citizen's holder
wallet, the citizen's PWA learns about it within seconds — without manual sync —
and the new credential appears on the Home screen. Pull-on-open remains
authoritative; the SignalR push is an optimisation.

**Spec reference:** `specs/114-citizen-wallet-pwa/spec.md` § US4.
**Tasks superseded:** This plan replaces tasks T123–T131 in `tasks.md` (the old
"build a new push pipeline" framing). Updated tasks below.

---

## 1. Architecture decision

US4 reuses **Feature 106's `SorchaLocalWallet` register-native delivery** end to
end. No new credential storage, no new issuance path, no new audited storage
interface. The work is a small *projection* over an existing pipeline:

```
Blueprint action with credentialIssuanceConfig.targetAudience = SorchaLocalWallet
        │
        ▼ ActionExecutionService mints SD-JWT VC + writes register tx
        │
        ▼ register replication
        │
        ▼ InboundCredentialDetector (Wallet Service BackgroundService)
        │
        ▼ CredentialStore.AddAsync(status: PendingAcceptance)   ← already shipped
        ─────────────────── new in US4 ──────────────────
        ▼ projector: is recipient wallet a citizen holder?
        ▼ yes → append CitizenCredentialEvent + emit WalletHub.CredentialAvailable
```

**What this delivers automatically:**

- Multi-node delivery (Feature 106's design point — peer replication carries the
  credential to the holder's node).
- Lifecycle states: `PendingAcceptance` → `Active` on accept, `Declined` on
  decline, `Expired` lazy. The PWA reuses these.
- Status list, Token Status List 2024 freshness, `cnf` holder-binding, all of
  Feature 106's hardening.

**What's new (and small):** a holder-address lookup, a citizen event log, a
projector, a hub emit, a PWA hub client, an E2E.

---

## 2. Server-side changes

### 2.1 `IHolderAddressLookup` (Wallet Service)

**Problem:** `InboundCredentialDetector` knows the recipient `WalletAddress`; it
does not know whether that address belongs to a citizen-PWA holder, and the
hub emit needs a `PlatformUserId`.

**Solution:** A new resolver alongside `IHolderKeyService`.

```csharp
public interface IHolderAddressLookup
{
    Task<Guid?> ResolvePlatformUserIdAsync(string walletAddress, CancellationToken ct = default);
}
```

**Implementation:** Index off the existing slot-108 derivation. At device
enrolment time (`HolderKeyService.GetOrCreateAsync`), persist the mapping
`(WalletAddress → PlatformUserId)` in a small EF table `CitizenHolderIndex`
(or a Redis hash with persistent fallback — preference: EF, since it's small,
write-once-per-citizen, and rides the existing `WalletDbContext`).

Cache reads in Redis with 24 h TTL — same TTL as `HolderKeyService`'s JWK
cache.

### 2.2 `CitizenCredentialEventLog` table

**Schema (new EF entity, new migration):**

| Column | Type | Notes |
|--------|------|-------|
| `Id` | `Guid` PK | |
| `PlatformUserId` | `Guid` indexed | Sharding key for the event stream |
| `Seq` | `long` | Monotonic per `PlatformUserId` (computed via `MAX(Seq)+1` in transaction, or sequence) |
| `Kind` | `int` | `Added`/`Revoked`/`Replaced` from `CitizenCredentialEventKind` |
| `CredentialId` | `string` | FK-by-convention to `CredentialEntity.Id` |
| `CreatedAt` | `DateTimeOffset` | |

**Index:** `(PlatformUserId, Seq)` — every read query is `WHERE PlatformUserId =
? AND Seq > ? ORDER BY Seq`.

**Storage audit registration:** Add `ICitizenCredentialEventStream` to
`AuditedStorageInterfaces` so a forgotten Postgres connection string causes
fail-fast in Production rather than silent miss.

### 2.3 Replace `EmptyCitizenCredentialEventStream`

Drop the placeholder. New
`EfCoreCitizenCredentialEventStream` reads from `CitizenCredentialEventLog`
joined to `CredentialEntity` (for the payload `CitizenSyncService` already
expects). Map `CredentialStatus.Revoked`/`Declined` rows to `Revoked` events;
map `Active` and `PendingAcceptance` rows to `Added`.

### 2.4 The projector — single composition point

**Where:** Append to `InboundCredentialDetector` at the existing
`CredentialStore.AddAsync` call site (the line that writes
`PendingAcceptance`). Wrap in a new helper `ICitizenInboxProjector` so the
detector stays single-responsibility.

```csharp
// in InboundCredentialDetector, immediately after CredentialStore.AddAsync(...)
await _citizenProjector.OnCredentialAddedAsync(credentialEntity, ct);
```

`CitizenInboxProjector.OnCredentialAddedAsync`:

1. `platformUserId = await _holderLookup.ResolvePlatformUserIdAsync(entity.WalletAddress)`
2. If null → return (org-credential recipient, not a citizen).
3. Insert `CitizenCredentialEventLog { PlatformUserId, Seq = next, Kind = Added,
   CredentialId = entity.Id }`.
4. `await _walletHub.Clients.Group(WalletHub.GroupNameFor(platformUserId.Value))
   .CredentialAvailable(entity.Id);`

**Same projector** handles `CredentialStore.PatchStatusAsync` for the
`Revoked`/`Declined` transitions, emitting `Kind = Revoked`. (Status changes
are wallet-domain, not citizen-domain, but appear in the citizen sync as
"this credential is now invalid.")

### 2.5 Hub emit — already wired

`IWalletHubClient.CredentialAvailable(string)` is declared with full XML doc
(`Hubs/IWalletHubClient.cs:137`). The thin-signal contract test in
`tests/Sorcha.Integration.Tests/Hubs/ThinSignalContractTests.cs` enforces its
shape. No changes to the hub itself.

---

## 3. PWA-side changes

### 3.1 New `CitizenWalletHubConnection`

**Location:** `src/Apps/Sorcha.Citizen.Wallet/Services/CitizenWalletHubConnection.cs`.

**Design:** Modelled on `Sorcha.UI.Core/Services/WalletHubConnection.cs` but
scoped to the citizen-wallet event surface only (`DeviceRevoked`,
`CredentialAvailable`). Drops the org-credential, transaction-lifecycle, and
encryption-pipeline events that the main UI listens to but the PWA does not
care about. Auth via the existing `IAuthService` (citizen JWT, audience
`sorcha:citizen-wallet`). URL: `{gateway}/hubs/wallet`.

Reconnect-with-jitter, auto-reconnect, REST poll fallback after 90s — copy
the patterns from `WalletHubConnection`.

### 3.2 Wire `OnCredentialAvailable` to sync

In `Sorcha.Citizen.Wallet.Pages.Index.razor` (Home), subscribe on `OnInitialized`:

```csharp
_hub.OnCredentialAvailable += async credentialId =>
{
    _logger.LogInformation("Push: credential {Id} available — syncing", credentialId);
    await _syncService.SyncAsync();
    await InvokeAsync(StateHasChanged);
};
```

The push is *trigger only* — the credential payload comes from the next
`/sync` call, not from the hub message (thin-signal contract).

### 3.3 Service worker background sync

`wwwroot/service-worker.published.js` registers a `sync` event handler with
tag `citizen-credential-sync`. The hub connection (when in the background)
calls `registration.sync.register('citizen-credential-sync')` on
`CredentialAvailable`. Chromium-only; non-Chromium browsers no-op gracefully
and rely on the next foreground open.

### 3.4 DI registration

In `Sorcha.Citizen.Wallet.Extensions.ServiceCollectionExtensions
.AddCitizenWalletServices`, register `CitizenWalletHubConnection` as scoped,
and start it from `Program.cs` after the auth service is ready.

---

## 4. Worked example blueprint

A council issues an Assured Identity credential to a citizen-PWA holder. Same
shape as the existing Driving Licence walkthrough but with
`targetAudience: "SorchaLocalWallet"` and the citizen participant late-bound.

```jsonc
{
  "title": "Assured Identity (PWA delivery)",
  "participants": [
    { "id": "applicant", "walletAddress": null },
    { "id": "verifier",  "walletAddress": "ws1qta..." }
  ],
  "actions": [
    { "id": 1, "isStartingAction": true, "sender": "applicant",
      "schemaRef": "AssuredIdentityApplication/v1" },
    { "id": 2, "sender": "verifier",
      "schemaRef": "VerifierDecision/v1" },
    { "id": 3, "sender": "verifier",
      "credentialIssuanceConfig": {
        "credentialType": "AssuredIdentityCredential/v1",
        "targetAudience": "SorchaLocalWallet",
        "recipientParticipantId": "applicant",
        "claimMappings": [
          { "claimName": "givenName",   "sourceField": "/1/payload/givenName" },
          { "claimName": "familyName",  "sourceField": "/1/payload/familyName" },
          { "claimName": "dateOfBirth", "sourceField": "/1/payload/dateOfBirth" }
        ],
        "disclosable": ["givenName", "familyName", "dateOfBirth"],
        "expiryDuration": "P5Y"
      } }
  ]
}
```

This blueprint becomes a Playwright E2E fixture. The walkthrough's existing
`AssuredIdentity` actor framework already drives the verifier side; only the
applicant-side actor (the citizen PWA) is new.

---

## 5. Test plan

| Layer | Test | File (new) |
|-------|------|-----------|
| Unit | `CitizenInboxProjector.OnCredentialAddedAsync` — citizen → emits + logs; org → no-op | `tests/Sorcha.Wallet.Service.Tests/CitizenWallet/CitizenInboxProjectorTests.cs` |
| Unit | `EfCoreCitizenCredentialEventStream` — read after Seq, ordering, joined credential payload | `tests/Sorcha.Wallet.Service.Tests/CitizenWallet/EfCoreCitizenCredentialEventStreamTests.cs` |
| Unit | `IHolderAddressLookup` — hit/miss, cache behaviour, idempotent enrolment write | `tests/Sorcha.Wallet.Service.Tests/CitizenWallet/HolderAddressLookupTests.cs` |
| Integration | `WebApplicationFactory` — InboundCredentialDetector → projector → in-memory hub captures `CredentialAvailable` | `tests/Sorcha.Wallet.Service.Tests/CitizenWallet/CitizenInboxProjectionIntegrationTests.cs` |
| E2E | Playwright — verifier issues blueprint above; citizen PWA receives `CredentialAvailable`; new credential card appears on Home | `tests/Sorcha.UI.E2E.Tests/Docker/CitizenWallet/CitizenWalletPushTests.cs` |

**E2E infra extension required:** A `CitizenWalletPage` page object and a
`AuthenticatedCitizenWalletTestBase` that logs in with citizen audience and
navigates to `/wallet/`. The existing `tests/Sorcha.UI.E2E.Tests` infra
targets the main UI at `/`; this is a small additive extension, not a
rewrite. Pattern: copy `AuthenticatedDockerTestBase`, swap auth scope and
URL.

Use `TestCitizenWalletDbContext` from existing 114 tests for the unit/integration
layer.

---

## 6. Out of scope

- **Adaptive `targetAudience` selection** based on recipient wallet type —
  recorded in `.specify/tasks/deferred-tasks.md` § "Blueprint Engine —
  Adaptive Credential Audience (Backlog)".
- **HAIP-path push** — not in scope. HAIP is wallet-initiated; the citizen
  always pulls.
- **Production DID-backed `IIssuerKeyResolver`** — independent track,
  unrelated to US4. Stays deferred.
- **US5 activity log** — deferred separately (T132–T143).

---

## 7. Risks and open questions

| # | Risk | Mitigation |
|---|------|-----------|
| 1 | `(PlatformUserId, Seq)` race — two concurrent issuance writes for the same citizen could collide on Seq | Compute Seq inside a `SERIALIZABLE` transaction or use a Postgres sequence partitioned by `PlatformUserId`. Postgres sequence per-citizen is overkill — go with `MAX(Seq)+1` inside a transaction with row-level locking on a sentinel row. Will be tested for concurrency in the integration test. |
| 2 | Existing org-credential receivers have a wallet that *also* happens to be a citizen holder address (test/dev only — should not happen in prod) | `IHolderAddressLookup` is the source of truth; if the address is in the citizen index, the recipient is treated as a citizen. The org-credential UI will continue to show the credential too (same `CredentialStore` row, two presentations). This is acceptable v1 behaviour. |
| 3 | Service-worker background sync requires HTTPS + Chromium | Foreground open will always reconcile via the standard `SyncService` pull. Background sync is genuinely an optimisation. |
| 4 | `CitizenCredentialEventLog` grows unbounded | Add a follow-up retention task (90-day TTL?) — not US4 scope, file as MSG-equivalent in deferred-tasks. |

---

## 8. Updated task list (replaces tasks.md US4 section)

| ID | Story | Description |
|----|-------|-------------|
| T123 | US4 | Add `CitizenHolderIndex` EF entity + migration; populate from `HolderKeyService.GetOrCreateAsync` |
| T124 | US4 | `IHolderAddressLookup` + `EfCoreHolderAddressLookup` (Redis-cached) + unit tests |
| T125 | US4 | Add `CitizenCredentialEventLog` EF entity + migration |
| T126 | US4 | `EfCoreCitizenCredentialEventStream` replacement + unit tests; register on the audited list |
| T127 | US4 | `ICitizenInboxProjector` + impl; hook into `InboundCredentialDetector` after `CredentialStore.AddAsync` and `PatchStatusAsync` |
| T128 | US4 | DI rewire in `Sorcha.Wallet.Service.Program.cs` — replace `EmptyCitizenCredentialEventStream` registration |
| T129 | US4 | `WebApplicationFactory` integration test driving the full path with an in-memory hub |
| T130 | US4 | New `CitizenWalletHubConnection` in PWA; subscribe in `Index.razor`; wire to `SyncService.SyncAsync` |
| T131 | US4 | Service-worker `sync` handler for `citizen-credential-sync` tag |
| T132 | US4 | E2E test fixture + new `AuthenticatedCitizenWalletTestBase` + `CitizenWalletPushTests` Playwright suite |
| T133 | US4 | Update `sorcha-architecture` skill, `verifiable-credentials` skill, and `.claude/skills/blueprint-builder/SKILL.md` with the citizen-PWA delivery worked example |

**Estimated effort:** ~3 days of implementation + 1 day testing/polish.

---

## 9. Files created/modified

**New:**

- `src/Core/Sorcha.Wallet.Portable/Domain/Entities/CitizenHolderIndex.cs`
- `src/Core/Sorcha.Wallet.Portable/Domain/Entities/CitizenCredentialEventLog.cs`
- `src/Core/Sorcha.Wallet.Core/Data/Migrations/{date}_AddCitizenInboxProjection.cs`
- `src/Services/Sorcha.Wallet.Service/Services/Interfaces/IHolderAddressLookup.cs`
- `src/Services/Sorcha.Wallet.Service/Services/Implementation/EfCoreHolderAddressLookup.cs`
- `src/Services/Sorcha.Wallet.Service/Services/Interfaces/ICitizenInboxProjector.cs`
- `src/Services/Sorcha.Wallet.Service/Services/Implementation/CitizenInboxProjector.cs`
- `src/Services/Sorcha.Wallet.Service/Services/Implementation/EfCoreCitizenCredentialEventStream.cs`
- `src/Apps/Sorcha.Citizen.Wallet/Services/CitizenWalletHubConnection.cs`
- 5× test files per § 5

**Modified:**

- `src/Services/Sorcha.Wallet.Service/Services/Implementation/HolderKeyService.cs` (write to `CitizenHolderIndex` on first derivation)
- `src/Services/Sorcha.Wallet.Service/Services/Implementation/InboundCredentialDetector.cs` (call projector)
- `src/Services/Sorcha.Wallet.Service/Credentials/CredentialStore.cs` (call projector on `PatchStatusAsync` for status changes — or wrap)
- `src/Services/Sorcha.Wallet.Service/Program.cs` (replace `EmptyCitizenCredentialEventStream` registration)
- `src/Common/Sorcha.ServiceDefaults/Storage/AuditedStorageInterfaces.cs` (add `ICitizenCredentialEventStream`)
- `src/Apps/Sorcha.Citizen.Wallet/Pages/Index.razor` (subscribe to `OnCredentialAvailable`)
- `src/Apps/Sorcha.Citizen.Wallet/Extensions/ServiceCollectionExtensions.cs` (DI for hub connection)
- `src/Apps/Sorcha.Citizen.Wallet/wwwroot/service-worker.published.js` (sync handler)
- `specs/114-citizen-wallet-pwa/tasks.md` (mark T123–T133 plan applied)
