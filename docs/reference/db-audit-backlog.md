# Database Audit — Deferred Backlog

Companion to the index quick-wins squashed into the InitialCreate migrations
(PR #402). Items here were identified during the per-service DB audit but
deferred because they require **code changes** (new background services,
view definitions, refactors) rather than a migration tweak.

Captured on 2026-04-25 against `master @ 0b00b2f1`.

---

## Tenant Service — `sorcha_tenant`

### Pruning gaps — no cleanup background service

These tables grow unbounded under normal use. Today only `ActivityEvents`
(`EventCleanupService`) and `AuditLogEntries` (`AuditCleanupService`) are
swept. A unified `DatabaseHousekeepingService` modeled on the existing
`EventCleanupService` could handle all of them in one hourly tick.

| Table | Trigger field | Suggested rule |
|---|---|---|
| `WalletLinkChallenges` | `ExpiresAt` | Delete `WHERE ExpiresAt < now() - 1 day` |
| `InvitationNonces` | `ConsumedAt` | Delete `WHERE ConsumedAt IS NOT NULL AND ConsumedAt < now() - 30 days` |
| `OrgInvitations` | `Status`, `ExpiresAt` | Delete `WHERE Status IN ('Expired','Revoked') AND ExpiresAt < now() - 90 days`; keep `Accepted` for audit |
| `RegisterInvitationRecords` | `Status`, `ExpiresAt` | Same shape as `OrgInvitations` |
| `ParticipantAuditEntries` | `Timestamp` | Either reuse `Organizations.AuditRetentionMonths` (join via `ParticipantId → Organization`) or add its own retention setting |
| `PlatformUsers.VerificationToken` / `PasswordResetTokenHash` | `*ExpiresAt` | Daily job nulls fields where `*ExpiresAt < now()` |

Supporting indexes for these sweepers (add when the service is added,
not before — unused indexes cost write throughput):
- `WalletLinkChallenges (ExpiresAt) WHERE Status='Pending'`
- `RegisterInvitationRecords (ExpiresAt) WHERE Status='Pending'`
- `OrgInvitations (ExpiresAt) WHERE Status='Pending'`
- `InvitationNonces (ConsumedAt)`

### View candidates

Hold off until indexes (in PR #402) are proven insufficient under real load.
Views add a maintenance surface without changing fundamental cost.

- **`v_my_organizations`** — `PlatformUserOrgMemberships ⋈ Organizations ⋈
  ParticipantIdentities` filtered to active rows. Used by the org switcher
  and "my profile" page.
- **`v_active_register_subscriptions`** — `OrganizationRegisterSubscriptions
  ⋈ Organizations` filtered `Status='Active'`. Three call sites today.
- **Materialised** `mv_org_activity_summary` — counts of users / pending
  invitations / subscriptions per org for the dashboard. Refresh nightly.
  Only worth it if dashboard load times become noticeable.

### Choke-point candidates

EXPLAIN-ANALYZE these once production has representative data:

1. **Login → org switcher** (`AuthEndpoints.cs:922`) — joins `UserIdentities
   ⋈ Organizations` filtered `Status=Active`. A covering index
   `(PlatformUserId) INCLUDE (OrganizationId, Role)` on
   `PlatformUserOrgMemberships` could make it index-only.
2. **`DashboardService.cs:34,39,61`** — count-active-users + count-pending-
   invitations on every dashboard load. Materialised view candidate above.
3. **`OrgWalletReconciliationService.cs:93`** — periodic sweep
   `Status=Active AND WalletAddress IS NULL`. A partial unique index
   `(Id) WHERE Status='Active' AND WalletAddress IS NULL` makes this
   O(matches) instead of O(orgs). Likely fine until ~10k orgs.

### Security inconsistency (cross-cutting)

`PlatformUsers.PasswordResetTokenHash` is hashed at rest;
`PlatformUsers.VerificationToken` is plaintext. Same threat model
(URL-bearer one-time token), different treatment. Migrate `VerificationToken`
to a `VerificationTokenHash` column and re-issue all in-flight tokens.

---

## Wallet Service — `sorcha_wallet`

The wallet schema is the best-indexed in the platform (36 non-PK indexes,
including several smart partials). Items below are speculative until query
patterns confirm them.

- **`SigningSessions (State, ExpiresAt)`** — for finding expired in-flight
  sessions. Add when a sweeper is added.
- **`Credentials (ExpiresAt) WHERE Status='Active'`** — for credential
  validity / cleanup. Add when revocation/expiry pruning is implemented.
- **`WalletAccess (ExpiresAt) WHERE RevokedAt IS NULL`** — for delegation
  expiry checks. Add when delegation auto-expiry is wired up.

Each of these is a quick win if/when the corresponding cleanup or expiry
behaviour is implemented; adding them now would just be unused write tax.

---

## Blueprint Service — `sorcha_blueprint`

- **Idempotency-key cleanup** — `Actions.IdempotencyExpiry` lets the row's
  idempotency key expire (`EfCoreActionStore.cs:260`), but expired rows
  aren't pruned and the field isn't indexed. Two-step: add a background
  sweeper that nulls `IdempotencyKey` where `IdempotencyExpiry < now()`,
  then add `(IdempotencyExpiry) WHERE IdempotencyKey IS NOT NULL` to
  support it.
- **Migration location is non-canonical** — Blueprint migrations live in
  `src/Services/Sorcha.Blueprint.Service/Data/Migrations/` rather than the
  EF default `Migrations/`. Future regens via `dotnet ef migrations add`
  will drop new files in the canonical location and you'll have to move
  them. Either standardise on `Migrations/` (move existing files + update
  namespace) or set `MigrationsAssembly` / output dir explicitly.

---

## Peer Service — `sorcha_peer`

- **`PurgeQueuedTransactionsAsync` loads the entire table to filter TTL
  in C#** (`PeerDataCleanupService.cs:139`). Push the TTL predicate into
  SQL: `WHERE EnqueuedAt + (TTL || ' seconds')::interval < now()`. The
  composite `(Status, EnqueuedAt)` added in PR #402 covers the
  Completed/Failed branch but the TTL branch still scans the whole table.
- **`SyncCheckpoints (NextSyncDue)`** — the sync scheduler probably
  queries "what's due now"; needs confirmation against actual scheduler
  code, then add the index.
- **`Peers.LastSeen DESC` for "recent peers" listing** — current index
  is ASC. If the listing is "most recent first" this becomes a backward
  scan, fine for small N but worth a DESC index at scale.

---

## MongoDB — Register Service

`sorcha_register_registry.registers` has indexes on `_id`, `Status`,
`Name`, `Purpose`. No audit done on per-register databases
(`sorcha_register_<id>`) yet — they're created lazily and currently
empty. Audit them once a real register is exercised end-to-end:

- Confirm `transactions` collection has `(RegisterId, BlockHeight)` or
  similar for chronological scans.
- Confirm `dockets` collection has `(RegisterId, SealedAt DESC)` for
  the docket viewer.
- TTL indexes for any ephemeral/replication-state collections.

---

## Tenant — known DB-init oddity (out of scope, but worth flagging)

- `Sorcha.Wallet.Service` logs `Error: libgssapi_krb5.so.2: cannot open
  shared object file` on startup. The wallet Dockerfile copies kerberos
  libs into the chiseled image; blueprint and peer do not, and they
  log the same error. The error is swallowed (Npgsql falls back to
  password auth) but it's noise, and any future change to PG auth could
  turn it into a hard failure for blueprint/peer.

---

## Process notes

- All quick-win indexes were squashed into the InitialCreate migration
  for each affected DbContext rather than added as a follow-up migration.
  This means a fresh deployment gets the indexes from the first run, no
  step-up migration needed. Existing dev volumes need to be dropped
  (`docker compose down -v`) for the squashed migration to apply.
- Items in this backlog should each become a focused ticket / PR rather
  than a single "do all the deferred DB work" effort, so they can be
  prioritised independently against real production signal.
