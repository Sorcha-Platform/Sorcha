# F114 US5 — Offline Presentation Reconciliation (PR3)

**Date:** 2026-05-20
**Status:** Design approved. Brainstorm complete — three decisions captured in §3.
**Spec affected:** F114 / `114-citizen-wallet-pwa` (User Story 5)
**Pre-existing features involved:** F111 / `111-presentation-lifecycle` (shipped), F127 / `127-credential-gated-service` (shipped — reconciled the consumer contract)
**Supersedes:** `specs/114-citizen-wallet-pwa/contracts/presentation-lifecycle-offline-extension.md` (the 2026-04-26 offline-extension contract) and tasks **T132–T135**.

---

## §1 — Why this doc exists

US5 PR2 (server forwarding for the citizen presentation log, PR #803) shipped the transport: the wallet reports its locally-recorded presentations to `POST /api/v1/wallet/presentations/log`, the Wallet Service dedupes per entry id (Redis SET-NX, 24h) and forwards each new entry through an `IPresentationLogForwarder` seam. PR2's forwarder is a **logging no-op stub** — the real destination was deliberately deferred to PR3.

PR3 was originally specified (T132–T135) as a Blueprint Service `OfflinePresentationConsumer : IPresentationConsumer` that writes `PresentationInitiated` + `PresentationOutcome` to "the originating register, preserving offline timestamps." That contract predates Feature 127, which reconciled the presentation-lifecycle subsystem. Against the shipped code, the original contract is **structurally impossible**. This doc records the reconciliation and the approved replacement.

## §2 — The structural finding

Two hard facts about the shipped F111/F127 lifecycle make the original "offline consumer writes the register" model impossible:

1. **Consumers MUST NOT write the register.** This is an explicit invariant on `IPresentationConsumer` (see `src/Common/Sorcha.PresentationLifecycle.Abstractions/IPresentationConsumer.cs`, contract invariants): *"Consumers MUST NOT write to the register directly; they return an outcome, the lifecycle service writes it."* A consumer that writes `PresentationInitiated`/`PresentationOutcome` violates the contract F127 locked.

2. **F111 is server-initiated and online.** `IPresentationLifecycleService.InitiateAsync` writes the `presentation-initiated` transaction **and** stashes pending state in Redis keyed by a server-minted `presentationRequestId`. The verifier callback (`HandleOutcomeAsync`) reconstructs a `PresentationInitiationContext` *from that pending state* before dispatching to `VerifyAsync`. An offline-reported presentation has **none** of this — no instance, no action, no register, no `presentationRequestId`, no pending state. `VerifyAsync` would have nothing to run against.

There is also a deeper product fact:

3. **A presentation that gates a Sorcha action is inherently online.** The wallet must scan a server-minted OID4VP QR (produced by `InitiateAsync` → `BuildInitiationAsync`). If the citizen is offline, no `InitiateAsync` ran, so there is no QR and no gate. Therefore a *free-standing offline* presentation — to the F125 reference verifier desk or an in-person scan — has **no originating register at all**. The optional `RegisterId` / `ActionTxId` fields on the PR2 wire `PresentationLogEntry` are vestigial from the stale design and are null in every real offline flow.

**Conclusion:** an offline-reported presentation is **citizen-owned convenience data**, not an F111 register lifecycle event. PR3 has no business in the Blueprint Service.

## §3 — Decisions (brainstorm)

1. **Purpose** — server forwarding exists to give the citizen **cross-device activity history**: a freshly-paired device shows past presentations. (Not a legal/compliance ledger, not verifier-org visibility.)
2. **Scope** — the **full cross-device loop** in one PR: Wallet Service durable store + real forwarder + read endpoint, *and* the PWA Activity page reading server history and merging it with the local log.
3. **Delete** — **server-authoritative**. Per-row delete removes the citizen's server row (and the local copy); the entry stays gone across all devices. FR-031's "platform-side records are unaffected" wording referred to register/legal evidence — which offline presentations don't produce — so it is reframed (see §6).

## §4 — Architecture & components

All work is in the **Wallet Service** and the **PWA**. No Blueprint Service, no `IPresentationConsumer`, no register write.

| Component | Location | Role |
|---|---|---|
| `CitizenPresentationRecord` (+ EF migration) | `src/Core/Sorcha.Wallet.Portable/Domain/Entities/` → `WalletDbContext` | Durable per-citizen row. Primary key `(PlatformUserId, EntryId)`. Stores only the wire `PresentationLogEntry` fields: entry id, credential id, verifier label, disclosed claim **names**, presented-at, outcome. **No credential values, ever** — the type carries claim names only, mirroring the privacy contract of the local log. |
| `ICitizenPresentationStore` + `EfCoreCitizenPresentationStore` (+ in-memory) | Wallet Service `Services/Interfaces` + `Services/Implementation` | `UpsertAsync(platformUserId, entry, ct)` — idempotent on `(platformUserId, entryId)`, the **authoritative** idempotency guard; `ListAsync(platformUserId, ct)` — newest-first; `DeleteAsync(platformUserId, entryId, ct)` — idempotent, scoped to the caller. Registered via `IStorageRegistrationLog` (`RegisterPersistent` with Postgres, `RegisterInMemory` fallback). **Not** on the F113 fail-fast audited list — convenience data, not correctness-critical, so it warns but does not gate startup. |
| `CitizenPresentationStoreForwarder : IPresentationLogForwarder` | Wallet Service `Services/Implementation` | Replaces PR2's `LoggingPresentationLogForwarder` (pure DI swap in `Program.cs`). `ForwardAsync` calls `store.UpsertAsync`. PR2's `CitizenPresentationLogReporter` + Redis SET-NX dedupe are **unchanged** — SET-NX now short-circuits hot retries; the store upsert is the durable idempotency. |
| `GET /api/v1/wallet/presentations` | `CitizenWalletEndpoints` | Citizen JWT, `RateLimitPolicies.Strict`. Returns the caller's presentation history newest-first as `PresentationHistoryResponse` (reuses the abstractions `PresentationLogEntry` wire shape). |
| `DELETE /api/v1/wallet/presentations/{id}` | `CitizenWalletEndpoints` | Citizen JWT, Strict. `store.DeleteAsync(platformUserId, id)`. 204 idempotent; cross-user is 404-indistinguishable (no existence leak). |
| `ICitizenWalletClient.ListPresentationsAsync` / `DeletePresentationAsync` | `Sorcha.ServiceClients.Http/CitizenWallet/` | PWA → Wallet client methods over the two endpoints. |
| PWA Activity page wiring | `Sorcha.Wallet.Pwa` | Fetch server history, apply the merge rule (§5), wire per-row delete to server + local, reframe the FR-031 messaging. |

## §5 — The merge rule

A naïve "local ∪ server, dedup by id" display breaks under server-authoritative delete: device B deletes an entry server-side, but device A still holds a *local synced copy*, so it reappears on A's next Activity render. The precise rule that fixes this:

```
Activity display = (server history)  ∪  { local entries where !SyncedToServer }
```

- A just-made presentation (`SyncedToServer == false`) is shown from the **local log** immediately — instant feedback before the next sync.
- Once the sync drain reports it and the server confirms (PR2 flips `SyncedToServer = true`), the entry is represented by the **server list**; the local synced copy is **display-suppressed**.
- A server-side delete removes it from the server list → it disappears from every device's display. The lingering suppressed local copy never resurrects it.

So post-sync, the PWA-local `presentationLog` store's only display role is "not-yet-synced entries"; everything else is the outbound queue. This keeps a single display source (the server) for all synced history and avoids divergence across devices.

## §6 — Data flow

- **Forward** — device A presents offline → local `PresentationLogEntry` (`SyncedToServer = false`) → next successful `SyncService.SyncAsync` drains it (PR2) → `POST /presentations/log` → `CitizenPresentationLogReporter` SET-NX dedupe → `CitizenPresentationStoreForwarder.ForwardAsync` → `store.UpsertAsync`. PR2 flips the local entry to `SyncedToServer = true` on the 202.
- **Cross-device read** — device B (freshly paired) opens Activity → `GET /presentations` → sees device A's presentation.
- **Delete** — any device → `DELETE /presentations/{id}` → server row removed + local removed → vanishes everywhere via §5. Reframed FR-031 messaging:
  > *"Removes this from your history on all your devices. It does not affect the verifier's own records."*

## §7 — Error handling & edges

- **Forward failure after PR2's 202.** PR2 marks `SyncedToServer = true` on the 202, before the forwarder's store write runs (the forward is dispatched off the request path). A store-write failure is logged. Because `UpsertAsync` is idempotent on `(platformUserId, entryId)`, a re-report heals it — but the PWA won't re-report once `SyncedToServer = true`, and the SET-NX claim expires after 24h. **This is a known at-most-once-ish delivery seam.** For cross-device *convenience* history the cost of a rare dropped entry is acceptable; stronger (outbox) delivery is explicitly deferred. The doc flags it so PR4+ can revisit if the product needs exactly-once.
- **Delete of an unsynced-only entry** — no server row exists; local delete only.
- **Cross-user list/delete** — scoped by JWT `platform_user_id`; delete of another user's id is a 404-indistinguishable no-op.
- **Empty history** — `GET /presentations` returns an empty list, never 404.

## §8 — Testing

- **Store** (`ICitizenPresentationStore`) — upsert idempotency on `(platformUserId, entryId)`, list newest-first ordering, scoped delete (own row deletes, cross-user no-op). Uses the `TestCitizenWalletDbContext` pattern (`tests/Sorcha.Wallet.Service.Tests/Services/`) so the InMemory provider works.
- **Forwarder** — `ForwardAsync` calls `UpsertAsync` with the mapped entry.
- **Endpoints** — reflection-based static-handler invocation (the established `CitizenWalletEnrolEndpointTests` pattern): list returns the caller's rows; delete → 204 and scoped; missing `platform_user_id` claim → 401.
- **Client** — `ListPresentationsAsync` / `DeletePresentationAsync` round-trip (mocked `HttpClient`).
- **PWA** — Activity merge rule (server ∪ local-unsynced; synced locals suppressed); per-row delete hits server and local; reframed messaging.

## §9 — What changes vs the stale design

| Stale (T132–T135 / offline-extension contract) | Reconciled (this doc) |
|---|---|
| `OfflinePresentationConsumer : IPresentationConsumer` in Blueprint Service | **DROP.** Consumers can't write the register and there's no pending state to verify against. |
| Consumer writes `PresentationInitiated` + `PresentationOutcome` on the originating register | **DROP.** Free-standing offline presentations have no originating register. No register write at all. |
| `PresentationConfig.AcceptOfflinePresentationsWithinSeconds` + `-late` tagging | **DROP.** No lifecycle events means no late-arrival tagging. |
| Forward target = Blueprint Service via service-to-service auth | **REPLACE** with the Wallet Service's own durable store (`ICitizenPresentationStore`). |
| `presentation-lifecycle-offline-extension.md` contract | **SUPERSEDED** by this doc. |

What carries over from PR2 unchanged: the `POST /presentations/log` endpoint, `CitizenPresentationLogReporter`, the Redis SET-NX dedupe, and the `IPresentationLogForwarder` seam (its implementation swaps from logging-stub to store-writer).

## §10 — Files referenced

- `src/Common/Sorcha.PresentationLifecycle.Abstractions/IPresentationConsumer.cs` (the "consumers MUST NOT write the register" invariant)
- `src/Services/Sorcha.Blueprint.Service/Services/Interfaces/IPresentationLifecycleService.cs` (server-initiated `InitiateAsync` + pending-state-keyed `HandleOutcomeAsync`)
- `src/Services/Sorcha.Blueprint.Service/Services/Implementation/SorchaWalletPresentationConsumer.cs` (F127 consumer precedent)
- `src/Services/Sorcha.Wallet.Service/Services/Interfaces/ICitizenPresentationLogReporter.cs` + `Implementation/CitizenPresentationLogReporter.cs` (PR2 reporter + dedupe)
- `src/Services/Sorcha.Wallet.Service/Services/Interfaces/IPresentationLogForwarder.cs` + `Implementation/LoggingPresentationLogForwarder.cs` (PR2 seam — swapped in PR3)
- `src/Services/Sorcha.Wallet.Service/Endpoints/CitizenWalletEndpoints.cs` (where the two new endpoints land)
- `src/Apps/Sorcha.Wallet.Pwa/Services/IPresentationLog.cs` + `ISyncService.cs` (PWA local log + drain)
- `src/Common/Sorcha.CitizenWallet.Abstractions/Models/PresentationLogEntry.cs` (wire shape reused by the read endpoint)
- `docs/superpowers/specs/2026-05-15-f127-f111-reconciliation.md` (the reconciliation precedent / template)
- `specs/114-citizen-wallet-pwa/contracts/presentation-lifecycle-offline-extension.md` (superseded)
- `specs/114-citizen-wallet-pwa/tasks.md` (T132–T135 obsoleted; T142–T143 reframed)
