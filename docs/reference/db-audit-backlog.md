# Database Audit — Deferred Backlog

Companion to the index quick-wins squashed into the InitialCreate migrations
(PR #402). Items here were identified during the per-service DB audit but
deferred because they require **code changes** (new background services,
view definitions, refactors) rather than a migration tweak.

Captured on 2026-04-25 against `master @ 0b00b2f1`. Updated 2026-04-25
after the storage-coherence track (PRs #405–#408) shipped.

Code-locatable items carry a `// TODO(db-audit):` marker at the relevant
site so a `grep "TODO(db-audit)"` from any storage / DB sweep surfaces
them in priority order.

---

## Tenant Service — `sorcha_tenant`

### Pruning gaps — no cleanup background service

These tables grow unbounded under normal use. Today only `AuditLogEntries` (`AuditCleanupService`) is
swept. (`ActivityEvents` and `EventCleanupService` were removed in F170 — activity events now flow through the Inbox spine.) A unified `DatabaseHousekeepingService` modeled on the existing
`AuditCleanupService` could handle all of them in one hourly tick.

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

## Redis (added 2026-04-25 from Redis/Mongo audit)

The two outage-shaped items (unbounded `maxmemory` and `noeviction` policy)
shipped in PR #404 alongside the `MongoSchemaIndexRepository` bootstrap
fix. Items below are quality-of-life improvements deferred until they
have a concrete trigger.

- **Db isolation** — every service uses db 0 today. Splitting into
  db 0 (cache), db 1 (event streams), db 2 (sessions) makes
  `INFO keyspace` and `MEMORY USAGE` per-namespace useful, and lets you
  `FLUSHDB 0` without nuking streams. Touches every service connection
  string.
- **Cache hit-ratio observation** — measured 27.5% on a fresh stack
  (heavy cold-start noise). Re-measure after a real walkthrough; if it
  stays low, suspect TTL-too-short or churning keys.
- **AOF for the event streams** — current `appendonly: no` means a Redis
  crash loses any stream events written between RDB snapshots
  (~10-min intervals). If event-driven sync is load-bearing for register
  consistency, switch to `appendonly yes` with `appendfsync everysec`,
  scoped only to the keys that need it (or accept the loss as the price
  of cache-only Redis and drive durability from the Postgres ledger).

---

## MongoDB (added 2026-04-25)

PR #404 fixed the schemaIndex bootstrap bug (indexes were never being
created on the write path). Remaining items:

- **`SearchAsync` regex → `$text`** — `MongoSchemaIndexRepository.SearchAsync`
  filters with `BsonRegularExpression` (regex) rather than the
  `$text` operator that the now-created `idx_text_search` index serves.
  At 1,390 docs the difference is sub-millisecond, but as the schema
  catalogue grows this becomes a collection scan. Behaviour change
  (text-index uses word stems, not substring match) so it's a product
  decision, not just a perf one.
- **Per-register collection audit** — `MongoRegisterRepository` defines
  comprehensive indexes on `transactions`, `dockets`, `receipts`
  (`TxId` unique, `SenderWallet`, `TimeStamp DESC`, `DocketNumber`,
  `MetaData.TransactionType`, revocation index, etc.) but per-register
  databases are lazy-created. Verify these all materialise on first use
  once a real register is exercised end-to-end.
- **`maxIncomingConnections: 500`** — current default is ~420k. A buggy
  client looping `MongoClient.Create()` could exhaust file descriptors
  before hitting any visible cap. Tighten to a sane bound for the
  expected service count.

---

## Storage-coherence follow-ups (added 2026-04-25 after PRs #405–#408)

These five items extend the storage-coherence track. The first four are
code-locatable and carry inline `// TODO(db-audit):` markers. The fifth is
diffuse and lives only here.

1. **Sweep Register + Validator onto the SorchaConnections cascade** —
   marker at `src/Core/Sorcha.Register.Storage.MongoDB/MongoRegisterStorageServiceExtensions.cs`.
   Both services bind MongoDB via the typed `MongoRegisterStorageConfiguration`
   Options class fed from the bespoke `RegisterStorage:MongoDB` config section,
   not from `ConnectionStrings:Sorcha:Mongo`. Migration needs either a typed-
   Options-aware extension to the cascade resolver, or refactoring the
   IMongoClient registration to read directly from the cascade and letting
   the Options class carry only database/collection names.

2. **Standardise on `AddDbContextFactory<>` for Tenant + Wallet** — markers at
   `Sorcha.Tenant.Service/Extensions/ServiceCollectionExtensions.cs` (around
   `AddDbContext<TenantDbContext>`) and `Sorcha.Wallet.Service/Extensions/
   WalletServiceExtensions.cs` (around `AddDbContext<WalletDbContext>`).
   Today these use scoped lifetime; Blueprint and Peer use the factory
   pattern. Background services in Tenant + Wallet currently work around
   the scoped lifetime via `IServiceScopeFactory` ceremony — the factory
   removes that. Touches every consumer of those DbContexts.

3. **Drop the Aspire-named compose aliases** (`ConnectionStrings__redis`,
   `ConnectionStrings__mongodb`) — marker at the `x-sorcha-connections`
   anchor in `docker-compose.yml`. Today they're the back-compat layer for
   `builder.AddRedisClient("redis")` / `GetConnectionString("mongodb")`
   call sites that haven't been migrated to cascade-aware registrations
   yet. Once every Program.cs reads via the cascade resolver, the aliases
   become dead config and can go.

4. **Adopt `ICacheStore` opportunistically** — no single marker site since
   the goal is consistent Redis access across services. Today four patterns
   coexist (raw `IConnectionMultiplexer`, Aspire `AddRedis*`, `IDistributedCache`,
   bespoke `Redis*Store` classes). Each time a service's Redis usage gets
   touched for unrelated reasons, replace the bespoke pattern with
   `ICacheStore` injection. Convergence comes from incremental adoption,
   not a single sweep.

5. **All Tenant pruning sweepers** (5 unindexed growth-prone tables:
   `WalletLinkChallenges`, `InvitationNonces`, `OrgInvitations`,
   `RegisterInvitationRecords`, `ParticipantAuditEntries`). Already
   captured in detail in the **Tenant Service → Pruning gaps** section
   above; remains the highest-effort and highest-value remaining item.

### Note on `MongoRegisterRepository.EnsureIndexesCreatedAsync`

The original audit flagged this as needing the bootstrap pattern fix
that PR #404 applied to `MongoSchemaIndexRepository`. On closer inspection
it already uses the correct double-checked-locking pattern with
`SemaphoreSlim`. The only difference from `ValidatorRegistry` is that
`MongoRegisterRepository` lets exceptions propagate from `CreateIndexesAsync`
(retries on the next call) while `ValidatorRegistry` swallows + logs.
That's a design choice, not a bug — no change needed.

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
