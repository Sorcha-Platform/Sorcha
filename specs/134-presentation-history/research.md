# Phase 0 Research: Cross-Device Citizen Presentation History

All open questions were resolved during the brainstorm; the full analysis lives in the source design (`docs/superpowers/specs/2026-05-20-f114-us5-offline-presentation-reconciliation-design.md`). This file records the load-bearing decisions in the standard format. **No `NEEDS CLARIFICATION` remain.**

## D1 — Where reported presentations live

- **Decision**: A durable per-citizen store in the **Wallet Service** (PostgreSQL via `WalletDbContext`). No Blueprint Service involvement, no `IPresentationConsumer`, no register/ledger write.
- **Rationale**: Two hard invariants of the shipped F111/F127 lifecycle make the original "offline consumer writes the register" model impossible: (1) `IPresentationConsumer` implementations MUST NOT write the register; (2) the lifecycle is server-initiated (`InitiateAsync` writes `presentation-initiated` and stashes Redis pending state keyed by a server-minted `presentationRequestId`; the callback reconstructs context from that state). A free-standing offline presentation has none of it. Deeper: a presentation that gates a Sorcha action is inherently *online* (the wallet scans a server-minted QR), so a free-standing offline presentation has **no originating register at all**.
- **Alternatives considered**: (a) Blueprint `OfflinePresentationConsumer` writing the register — rejected (violates both invariants). (b) A new lifecycle-service method writing a free-standing register tx — rejected (no instance/register to anchor to; breaks the three-event model and F119 chain rules). (c) Citizen inbox/notification entry only — rejected (notification semantics, not an audit list). (d) Keep client-only / no forwarding — rejected (defeats cross-device, wastes PR2).

## D2 — Storage backend & durability

- **Decision**: PostgreSQL EF entity `CitizenPresentationRecord` on `WalletDbContext`, registered via `IStorageRegistrationLog` (`RegisterPersistent` with Postgres, `RegisterInMemory` fallback). **Not** added to the F113 fail-fast audited interface list.
- **Rationale**: Cross-device history must survive device loss, re-pairing, and local-data clears — that requires durable server-side storage, not Redis-with-TTL (which is for ephemeral state like F124's pending-application notice). It is *convenience* data, not correctness-critical infrastructure, so it should warn (not gate startup) when on an in-memory backend — hence off the fail-fast list.
- **Alternatives considered**: Redis with TTL — rejected (history must not expire). Reuse an existing citizen table — rejected (distinct lifecycle and identity; clean entity is clearer).

## D3 — Idempotency of forwarding

- **Decision**: The store's `UpsertAsync` keyed on `(PlatformUserId, EntryId)` is the **authoritative** idempotency guard. PR2's Redis SET-NX dedupe (24h) is retained as a hot-retry short-circuit, unchanged.
- **Rationale**: The wallet-generated entry id is globally unique and already the dedupe unit in PR2. A durable upsert makes re-reports safe even after the 24h SET-NX claim expires.
- **Alternatives considered**: Drop SET-NX and rely on upsert only — viable, but keeping SET-NX avoids redundant DB writes on hot retries at no cost and preserves PR2's shipped behaviour.

## D4 — Delete semantics

- **Decision**: **Server-authoritative**. `DELETE /api/v1/wallet/presentations/{id}` removes the citizen's server row; the PWA also removes the local copy. The entry stays gone across all devices.
- **Rationale**: Under a cross-device merge, a local-only delete would reappear on the next sync. Server-authoritative delete is the only coherent model. FR-031's old "platform-side records are unaffected" wording referred to *register/legal evidence* — which these presentations don't produce — so it is reframed (FR-009): "removed from your history on all your devices; does not affect the verifier's own records."
- **Alternatives considered**: Local-only delete + per-device tombstone — rejected (confusing across devices, tombstone bookkeeping). Drop per-row delete once server-backed — rejected (removes a control PR1 shipped; users expect it).

## D5 — Activity merge rule (avoiding reappear-after-delete and duplicates)

- **Decision**: `display = (server history) ∪ {local entries where !SyncedToServer}`. Local entries already marked synced are display-suppressed in favour of the server list.
- **Rationale**: Gives instant local feedback for a just-made presentation, then a single server-sourced representation once synced. A server-side delete therefore removes the entry from every device's display, and a lingering local synced copy never resurrects it. Naïve "local ∪ server dedup-by-id" would reappear on the deleting device's siblings.
- **Alternatives considered**: Server-authoritative display only (no local pre-sync row) — rejected (a just-made presentation wouldn't show until the next sync round-trip; SC-006 fails).

## D6 — Delivery guarantee

- **Decision**: Convenience-grade (at-most-once-ish). PR2 marks the local entry synced on the 202 before the off-request-path forward runs; a store-write failure is logged and not retried by the wallet. `UpsertAsync` idempotency heals re-reports while the SET-NX claim is live.
- **Rationale**: For cross-device convenience history, a rare dropped entry (still present on the originating device locally) is acceptable. Exactly-once/outbox machinery is disproportionate.
- **Alternatives considered**: Outbox-grade delivery — explicitly deferred (out of scope per spec).

## D7 — Wire shape for the read endpoint

- **Decision**: Reuse the `Sorcha.CitizenWallet.Abstractions` `PresentationLogEntry` as the read-response element (wrapped in a `PresentationHistoryResponse`). The vestigial `RegisterId`/`ActionTxId` fields are simply null.
- **Rationale**: One wire contract for both report (PR2) and read (PR3) keeps the PWA mapping trivial and avoids a parallel DTO.
- **Alternatives considered**: A bespoke read-model DTO — rejected (needless duplication for v1).
