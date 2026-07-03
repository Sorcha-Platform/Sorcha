# Sorcha Platform — Performance Analysis (bottlenecks & potential wins)

**Date:** 2026-07-04 · **Type:** analysis only (no code changed) · **Method:** five parallel deep-dives
(relational data access, Mongo/Redis + cache, hot request paths, crypto/serialization/allocation, network
fan-out + background services + startup), each producing ranked findings with `file:line` evidence, then
synthesized + de-duplicated here.

> How to read this: **§1 Executive summary** → the shape of the problem. **§2 Top bottlenecks** are the
> cross-cutting, highest-value items (several were found independently by more than one dive — noted). **§3
> Quick wins** is the low-effort do-first list. **§4 Structural** is the higher-effort work. **§5** keeps the
> full per-dimension detail. **§6** is the background-loop interval map. **§7** records what's already good.
> Impact = effect on latency/throughput/cost at scale. Effort = rough implementation cost. Nothing here is a
> committed change — it's a prioritized menu.

---

## 1. Executive summary

The platform's code is generally healthy on the classic footguns: **no sync-over-async on the EF or crypto
paths** (the old audit CODE-003 is resolved), typed HTTP clients all use `IHttpClientFactory`, service tokens
are cached, SignalR sends are all group-scoped (no `Clients.All`), and health checks don't cascade. So the
wins are not "fix broken code" — they're **scale and steady-state efficiency**.

Three themes dominate:

1. **The Register read layer doesn't use the database as a database.** Nearly every register read funnels
   through `GetTransactionsAsync`, which does `Find(Empty).ToList().AsQueryable()` and then filters/sorts/pages
   **in memory** — so every read is O(ledger size), transfers full payload blobs, and bypasses every Mongo
   index (OData `$top=100` included). *Found independently by the Mongo dive (F1/F2) and the hot-paths dive
   (R1/R2/R4).* This is the single biggest scalability wall. Three hot queries also have **no supporting
   index** (`RecipientsWallets`, `MetaData.TrackingData.credentialId`, `MetaData.InstanceId`) → full COLLSCANs.
   The relational side has the mirror problem: the **instance inbox/badge paths** scan every instance row and
   filter the `ParticipantWallets` jsonb in C#.

2. **A fleet of background loops busy-polls regardless of load.** The validator polls Redis every **100 ms per
   register** with no backoff/jitter; a per-subscriber gRPC stream polls every **2 s**; blueprint recovery
   re-discovers, re-fetches and **re-hashes every published blueprint every 60 s**. All scale with register
   count, none jitter, and every replica fires in lockstep. This is most of the idle-state CPU/Redis/HTTP.

3. **gRPC connection churn and sequential fan-out on the peer/submit hot paths.** Gossip, consensus votes, and
   the peer client each build+dispose a gRPC channel *per message/vote* instead of using the existing pool;
   submission fan-out and multi-step state reconstruction `await` peers/decrypts **serially** when they could
   run concurrently. *gRPC-per-vote found by both the hot-paths dive (V2) and the fan-out dive (F2).*

Plus a long tail of **cheap hygiene wins** (missing `AsNoTracking`, per-call `JsonSerializerOptions`, an
always-on `LastAccessedAt` UPDATE on every wallet read, SignalR reconnect jitter, Docker probing `/health`
instead of `/alive`) that individually are small but collectively remove a lot of constant-factor overhead.

**If you do nothing else:** push Register queries into Mongo with the three missing indexes (§2 T1/T2),
add backoff+jitter to the validator poll and widen blueprint recovery (§2 T3), and pool the gRPC channels
(§2 T4). Those four address the dominant scale, idle-cost, and latency issues respectively.

---

## 2. Top bottlenecks (ranked, cross-cutting)

| # | Bottleneck | Impact | Effort | Corroboration |
|---|---|---|---|---|
| **T1** | Register reads load the whole tx collection + filter in memory (incl. OData) | **High** | Med | Mongo F1/F2, HotPath R1/R2/R4 |
| **T2** | Missing Mongo indexes → COLLSCAN on inbox / credential / instance queries | **High** | **Low** | Mongo F3, HotPath R3 |
| **T3** | Idle busy-polling loops (validator 100 ms; recovery 60 s re-hash; 2 s sub poll) | **High** | Med | FanOut F1/F4/F12, HotPath V5 |
| **T4** | gRPC channel built+disposed per gossip / per vote / per client resolve | **High** | **Low** | FanOut F2/F9, HotPath V2 |
| **T5** | EF instance inbox/badge paths full-scan + in-memory jsonb filter | **High** | Med | EF F1/F2/F3 |
| **T6** | Sequential awaits that could parallelize (decrypt loop, submit fan-out, DID) | **High** | Low-Med | HotPath B1/B3, FanOut F3/F18 |
| **T7** | Validator re-fetches register-global chain state per transaction (~150/batch) | **High** | Med | HotPath V1 |
| **T8** | Fast crypto primitives wrapped in `Task.Run` on the validator verify loop | Med-High | Med | Crypto F1 |
| **T9** | Merkle tree hashes hex strings, not raw bytes (per-seal + per-verify allocs) | Med-High | Med | Crypto F2 |
| **T10** | `new JsonSerializerOptions()` per call on sealing/hash paths (~40 sites) | Med | **Low** | Crypto F4 |
| **T11** | Load-all-then-loop maintenance (Peer TTL sweep, Tenant prune/expire/mark-read) | Med | **Low** | EF F4/F7 |
| **T12** | Cheap read hygiene: `AsNoTracking` gap + always-on `LastAccessedAt` UPDATE | Med | **Low** | EF F6/F8 |

### Detail on the top items

**T1 — Register read layer materializes the whole ledger.**
`MongoRegisterRepository.GetTransactionsAsync` does `Find(Empty).ToListAsync().AsQueryable()`
(`MongoRegisterRepository.cs:534`), and ~15 call sites then apply `.Where`/`.OrderByDescending(TimeStamp)`/
`.Skip`/`.Take`/`.FirstOrDefault` in LINQ-to-Objects (`Program.cs:1143,1192,2087,2204,2492,3230,3286,3454,3471`;
`QueryManager.cs:62,165`; `CryptoPolicyService.cs:84,108`; `SystemRegisterService.cs:403,444`). The OData
`Transactions` set is backed by that same in-memory `IQueryable` (`Program.cs:71-84`), so `$filter`/`$top=100`
still scans + deserializes the entire collection first. Global-stats endpoints even load every tx of every
register **just to count** (`Program.cs:3452,3467`). **Win:** push predicate+sort+skip/limit+projection into the
driver (`Find(filter).Sort().Skip().Limit().Project(header)`); back OData with `IMongoQueryable`; use
`CountDocumentsAsync` for totals. Turns O(ledger) reads into index-backed page-sized reads.

**T2 — Three missing indexes (one-line-each).** `RecipientsWallets` multikey (wallet-inbox routing,
`MongoRegisterRepository.cs:617`), compound `MetaData.TrackingData.type + credentialId` (credential
verify/revoke, `:763-772`), and `MetaData.InstanceId` (the validator's instance query can't use the
`(BlueprintId, InstanceId)` compound — wrong prefix — `TransactionManager.cs:219`). Add to
`CreateTransactionIndexesAsync` (`:321-355`). Converts three scans into seeks.

**T3 — Idle busy-polling.** Validator polls Redis every 100 ms per register with no backoff/jitter, all
replicas in lockstep (`ValidationEngineService.cs:50-55,121`; interval `ValidationEngineConfiguration.cs:24`);
`UseBlockingPop`/`BlockingTimeout` are configured but unused (`TransactionPoolPollerConfiguration.cs:34-39`).
`BlueprintRecoveryService` re-discovers + re-fetches + re-hashes the entire published-blueprint corpus of every
register every 60 s even in steady state (`BlueprintRecoveryService.cs:70-119,302-345`) despite an event-driven
path already existing (`:192`). A per-subscriber gRPC stream polls the cache every 2 s regardless of activity
(`RegisterSyncGrpcService.cs:378-404`). **Win:** event/signal-driven wake (Redis `BRPOP`/pub-sub, a per-register
`Channel`) + exponential backoff + ±20% jitter; widen recovery to 10-15 min and skip unchanged-height
registers. Cuts idle Redis/CPU/HTTP by 1-2 orders of magnitude and scales with load not register count.

**T4 — gRPC channel churn.** `TransactionDistributionService.SendGossip` builds `GrpcChannel.ForAddress(...)`
in a `using` per message (`:264-265`) while a pool sits injected right there (`PeerConnectionPool.cs:160-169`);
`ConsensusEngine.CollectVoteFromValidatorAsync` does the same per validator per docket (`:358`);
`PeerServiceClient` builds a channel in its ctor and is registered **transient** (`PeerServiceClient.cs:60-63`,
`ServiceCollectionExtensions.cs:61-71`). Each is a full TCP+TLS+HTTP/2 handshake then teardown on a hot path.
**Win:** reuse the pool / register singleton / `AddGrpcClient`. Low effort, removes handshake latency from
every gossip hop and consensus vote.

**T5 — EF instance scans.** `GetByParticipantWalletAsync`, `GetPendingActionsByWalletAsync`, and
`GetPendingActionCountByWalletAsync` each load all (Active) instances, deserialize each `ParticipantWallets`
jsonb in C#, filter with `ContainsWalletAddress`, then `Skip/Take` after materialization
(`EfCoreInstanceStore.cs:222-241,255-284,360-363,377-409,628-646`). The badge-count path repeats the pending
scan just to return an int, and is rendered on the same screen (double load). **Win:** normalize participant
wallets to a queryable `InstanceParticipant(InstanceId, Wallet)` join table (or a GIN `@>` containment index on
the jsonb) and push filter+sort+page into SQL; share one filtered query for list + count.

**T6 — Serial awaits that should be concurrent.** State reconstruction decrypts K prior-action payloads with K
**sequential** Wallet round-trips (`StateReconstructionService.cs:115,140,372,507`) though they're order-
independent → `Task.WhenAll` (bounded) collapses K×RTT to ~1×RTT (biggest multi-step submit-latency win). Submit
also does two serial Wallet signs + a serial sequence-number fetch on the tail (`ActionExecutionService.cs:1053,
1064,1079`) — the seq fetch is independent of signing and can overlap. Peer submission fan-out awaits each
carrier serially (`TransactionDistributionService.cs:148-186`) while gossip/heartbeat already use `Task.WhenAll`.
DID `alsoKnownAs` links resolve sequentially and bypass the cache (`DidResolverRegistry.cs:137-172`).

**T7 — Per-tx chain re-fetch.** `ValidateChainAsync` reads register height + latest/predecessor docket
**inside every transaction's** validation (`ValidationEngine.cs:1169-1178`, called per-tx `:206`, batch `:320`),
so a 50-tx batch issues ~150 identical Register round-trips every 100 ms poll. **Win:** hoist the docket-level
check to once per batch and pass it into per-tx validation (~50× fewer calls at default batch size).

**T8-T10 — Crypto/serialization constant factors.** `CryptoModule` wraps single-digit-µs ED25519/P-256
sign+verify in `Task.Run` (threadpool hop + Task/closure alloc per signature) on the validator verify loop
(`CryptoModule.cs:467-496,578-625`; consumed `ValidationEngine.cs:733,1012`) — make the primitives sync/
`ValueTask`, reserve `Task.Run` for RSA-4096/PQC keygen. `MerkleTree.CombineAndHash` concatenates two 64-char
hex strings, UTF8-encodes, hashes, then hex+lowercases — ~4-5 allocs per node, ~2N nodes per tree, on every
seal *and* verify (`MerkleTree.cs:271-281,48,212`); carry `byte[32]` internally, hex only at the boundary.
`new JsonSerializerOptions()` is allocated per call in `DocketHasher` (once per tx during sealing, `:52,100`),
`DisclosureGroupBuilder.cs:87`, `Transaction.cs:292,395`, and ~40 more sites — hoist to `static readonly` /
reuse `SorchaJson.Options` / `RegisterSerializationOptions.Canonical` (the correct pattern already exists in the
codebase; these sites just don't use it).

---

## 3. Quick wins (low effort, do-first)

These are Low-effort with real payoff — a good first sweep:

- **T2** — add the three missing Mongo indexes (`RecipientsWallets`, `TrackingData.type+credentialId`,
  `MetaData.InstanceId`). *One line each; removes COLLSCANs from inbox/credential/instance queries.*
- **T4** — reuse the gRPC pool in gossip + make `PeerServiceClient` singleton. *Kills per-message handshakes.*
- **T10** — hoist per-call `JsonSerializerOptions` to `static readonly` on the sealing/hash sites.
- **T12** — add `.AsNoTracking()` across Tenant read/list repos (`IdentityRepository`, `OrganizationRepository`,
  `ParticipantRepository`, `InvitationRepository`, `CustomDomainRepository`); stop the always-on
  `LastAccessedAt` UPDATE on every wallet `GetByAddressAsync` (`EfCoreWalletRepository.cs:119-127`) — throttle or
  fire-and-forget. *10-30% less CPU/alloc on read-heavy endpoints; halves wallet-read round-trips.*
- **T11** — convert load-all-then-loop maintenance to set-based SQL: Peer TTL sweep with an indexed `ExpiresAt`
  + `ExecuteDeleteAsync` (`PeerDataCleanupService.cs:139-142`); Tenant prune/expire/mark-read via
  `ExecuteDelete/UpdateAsync` (`AuthChallengeRepository.cs:64-68`, `ParticipantRepository.cs:246-255`,
  `EfCoreInboxStore.cs:179-190`).
- **FanOut F6** — add ±20% jitter to SignalR client reconnect (six `WithAutomaticReconnect({0,2,5,10,30}s)`
  arrays; the CLI's `SorchaHubConnectionBuilder` already has the right policy). *Prevents reconnect herds.*
- **FanOut F16** — point Docker `healthcheck` at `/alive` not `/health` (`docker-compose.yml:273-277`;
  split exists at `Extensions.cs:176,179`). *Removes ~6 DB + 6 Redis round-trips/min/service + a recurring
  Wallet encrypt+decrypt on every 10 s probe.*
- **FanOut F8** — cache token-introspection results by token hash (`TokenIntrospectionClient.cs:32-57`).
- **Crypto F9/F10** — `data.AsSpan(1)` instead of `.Skip(1).ToArray()` on address decode
  (`WalletUtilities.cs:92,106,186`); register `ICryptoModule` singleton in Register.Service for consistency
  (`Program.cs:178`).
- **HotPath B3 / V6** — start `GetNextSequenceNumberAsync` concurrently with signing; build the consensus
  `VoteRequest` once before fan-out instead of per validator (`ConsensusEngine.cs:381,616`).
- **Crypto F5** — AES-GCM encrypt directly into one `combined[len+16]` buffer (removes a full 4 MB copy per
  F085 chunk, `SymmetricCrypto.cs:137-146`).

---

## 4. Structural (higher effort, plan before doing)

- **T1** — re-platform the Register read layer onto driver-side queries + projections + OData-over-
  `IMongoQueryable`. Touches the repository + ~15 call sites + the OData source; highest scale payoff. Pair with
  caching the queryable/list reads (today `CachedRegisterRepository` only caches single-entity gets —
  `:74,159,224` — so the hot list paths are always cold) and add per-key single-flight to avoid stampedes on
  cold hot registers (`CachedRegisterRepository.cs:88-109`, `RedisCacheStore.cs:242-262`).
- **T5** — normalize instance participant wallets to a join table (or GIN-index the jsonb) and push filter+sort
  +page into SQL across the inbox/pending/badge paths.
- **T3** — move the validator pool poll and the per-subscriber live poll to event/signal-driven wake; widen +
  height-gate blueprint recovery. Also collapse the **three overlapping peer convergence loops** (90 s resync,
  20 s relay poll, 30 s heartbeat) into heartbeat-lag-gated resync (FanOut F20).
- **T7** — hoist validator docket-level chain validation to once-per-batch.
- **T9** — Merkle tree on raw bytes end-to-end.
- **Batch the replica pull** — full-replica sync issues one round-trip per docket (relay path up to 2·N, each
  with a 30 s correlation timeout) while holding the per-register semaphore (`RegisterReplicationService.cs:249,
  278`; `RelayCommunicationService.cs:427,486`); batch tx pulls across a docket batch. And re-finalization
  re-walks the whole chain every 90 s (`:334-339`) — finalize only past `LastSyncedDocketVersion`.
- **F085 large-payload streaming** — `FileChunker` copies the pooled 4 MB buffer into a fresh LOH array per
  chunk (`FileChunker.cs:72-73`), and Blake2b stream hashing fully buffers into memory + `ToArray()`
  (`HashProvider.cs:117-128`). Expose chunks as pooled `ReadOnlyMemory<byte>`; use incremental Blake2b.
- **T8** — de-`Task.Run` the crypto primitives (ValueTask/sync).
- **Per-mint HTTP + seed decrypt** — `IssuanceKeyService.GetActiveSigningMaterialAsync` does an AES-GCM seed
  decrypt + a cross-service canonical-address HTTP call on **every** credential mint (`:238-259,131-133`); cache
  the (near-static) canonical address per org and memoize the derived `ExtKey`.

---

## 5. Full per-dimension findings

Condensed; each item keeps its evidence. Items already folded into §2/§3/§4 are cross-referenced.

### 5a. Relational (EF Core / PostgreSQL)
- **F1-F3 (High)** instance participant-wallet scan + in-memory jsonb filter on inbox/pending/badge → **T5**.
  `EfCoreInstanceStore.cs:222-241,255-284,377-409,628-646`.
- **F4 (High/Low)** Peer TTL cleanup loads whole `QueuedTransactions` table twice (`AddSeconds(TTL)` can't
  translate) → indexed `ExpiresAt` + `ExecuteDeleteAsync`. `PeerDataCleanupService.cs:134-142`. → **T11**.
- **F5 (Med)** template store loads all + `filter.Compile()` in memory for query/count/delete;
  `Category` column unused. `EfCoreTemplateStore.cs:84-106,214-224,252-261`.
- **F6 (Med/Low)** `AsNoTracking` missing across Tenant read repos (Blueprint/Wallet already clean) → **T12**.
- **F7 (Med/Low)** prune/expire/mark-read = load-all + per-row DML, ignoring `IX_AuthChallengeToken_ExpiresAt`.
  `AuthChallengeRepository.cs:64-68`, `ParticipantRepository.cs:246-255`, `EfCoreInboxStore.cs:179-190`. → T11.
- **F8 (Med/Low)** wallet `GetByAddressAsync` always fires a second UPDATE for `LastAccessedAt`
  (`EfCoreWalletRepository.cs:119-127,344-348`). → **T12**.
- **F9 (Med/Low)** `GetUserByEmailAsync` filters `Email` but only a `(OrganizationId, Email)` composite exists →
  seq scan on a login-adjacent path (`IdentityRepository.cs:30-34`; `TenantDbContext.cs:516-518`).
- **F10 (Med)** participant `SearchAsync` uses `ToLower().Contains()` → `LOWER(col) LIKE '%term%'`, unindexable;
  add `pg_trgm` GIN or prefix anchor (`ParticipantRepository.cs:103-118`).
- **F11 (Med/Low)** composite indexes not sort-aligned with hot `ORDER BY CreatedAt/UpdatedAt` → explicit sort
  per page (`EfCoreInstanceStore.cs:174,202,257`; `EfCoreActionStore.cs:92`). Make `(BlueprintId, CreatedAt
  DESC)` etc.
- **F12 (Med)** `TransactionQueueManager` serializes all DB work behind one `SemaphoreSlim` + a `CountAsync`
  per enqueue (`TransactionQueueManager.cs:24,200,223-233,256`).
- **F13-F15 (Med-Low/Low)** unbounded list endpoints (no paging) incl. orgs eager-loading IdP configs;
  unread-filter + orphan-sweep on unindexed null-state columns (partial indexes); read-then-update round-trips +
  client-side `NOT IN`. `EfCoreBlueprintStore.cs:59-81`, `OrganizationRepository.cs:35-48`,
  `EfCoreInboxStore.cs:78-89`, `EfCoreActionStore.cs:301-305`, `PeerDataCleanupService.cs:171-177`.

### 5b. Mongo / Redis / cache
- **F1 (High)** `GetTransactionsAsync` whole-collection load + in-memory LINQ (~15 sites) → **T1**.
- **F2 (High)** OData filters over the in-memory `IQueryable` → **T1**.
- **F3 (High/Low)** missing `RecipientsWallets` + `TrackingData.type+credentialId` indexes → **T2**.
- **F4 (Med)** no projections — full docs (incl. base64 payload blobs) fetched for header-only consumers
  (`MongoRegisterRepository.cs:590-651`; `Program.cs:1209-1219`).
- **F5 (Med/Low)** bloom-filter `MayContainAsync` = k serial `GETBIT` round-trips (`AddAsync` already batches)
  (`RedisBloomFilterAddressIndex.cs:93-99` vs `63-68`).
- **F6 (Med)** `RemoveByPatternAsync`/stats use blocking `KEYS`/`SCAN` over the whole keyspace on delete/
  invalidate (`RedisCacheStore.cs:272-285,369-372`; `CachedRegisterRepository.cs:144-145,419-420`).
- **F7 (Med)** `VerifiedCache.GetManyAsync` is N+1 vs Redis and Mongo (no `MGET`/`$in`) (`VerifiedCache.cs:144-171`).
- **F8 (Med/Low)** `QueryRegistersAsync` takes a `Func` → loads all registers, filters in memory, indexes
  unused (`MongoRegisterRepository.cs:402-410`).
- **F9 (Med)** no cache-stampede/single-flight on miss → **T1** (structural pairing).
- **F10 (Low)** recovery per-docket Redis `HSET` + WORM read-before-write existence checks
  (`RegisterRecoveryService.cs:241-248`, `MongoWormStore.cs:74-103`).

### 5c. Hot request paths
- **B1 (High)** state-reconstruction decrypt loop is K serial Wallet RTTs → **T6**.
- **B2 (Med/Low)** same register fetched twice per submit for the DevMode flag (`StateReconstructionService.cs:100`,
  `ActionExecutionService.cs:811`).
- **B3 (Med/Low)** two serial signs + serial seq-number fetch on the tail → **T6/§3**.
- **B4 (Med)** schema-validation cache key = full serialize + SHA-256 of the schema, and `StripExtensionKeywords`
  deep-clones every call even on a cache hit (`SchemaValidator.cs:58-64`; `JsonSchemaCache`); key on
  blueprint+action+version instead.
- **B5 (Low-Med)** blueprint `BlueprintModel` re-deserialized from Redis string per action; add an in-process L1
  (`ActionResolverService.cs:55-64`).
- **B6 (Low)** `InstanceProjector` folds a docket's txs serially with a Register fetch per tx (gates advancement
  latency, not submit) (`InstanceProjector.cs:121-166`).
- **V1 (High)** per-tx chain re-fetch → **T7**. **V2 (High/Low)** gRPC channel per vote → **T4**.
  **V3 (Med)** validators re-queried + docket re-serialized 3-4× per docket (`ConsensusEngine.cs:83-88`,
  `DocketDistributor.cs:65-136`). **V4 (Med)** pool drain = 4 serial Redis RTTs/tx (`TransactionPoolPoller.cs:
  218-251`). **V5 (Med/Low)** fixed 100 ms poll, no backoff → **T3**. **V6 (Med/Low)** `VoteRequest` rebuilt per
  validator → build once. **V7 (Low)** `MemPoolManager` 5-min keyspace scan + sync-over-async (`:260,294-313`).
- **R1-R4 (High)** Register read/count/stats over the full collection → **T1**. **R3** missing indexes → **T2**.
  **R5-R9 (Med-Low)** sequential sender+recipient awaits (`$or`); `QueryRegistersAsync` `Func`; unbounded docket
  list (anonymous-reachable — federation-bootstrap DoS surface, `Program.cs:1432`); list reads bypass cache;
  tx-graph materializes the whole ledger then cursors in memory despite a `TimeStamp` index.

### 5d. Crypto / serialization / allocation
- **F1 (High)** `Task.Run` over fast crypto → **T8**. **F2 (High)** Merkle hex hashing → **T9**.
- **F3 (High)** per-mint seed decrypt + canonical-address HTTP, no caching → **§4**.
- **F4 (Med/Low)** per-call `JsonSerializerOptions` (~40 sites) → **T10**.
- **F5 (Med/Low)** AES-GCM triple-buffer (4 MB copy/chunk) → **§3**. **F6 (Med/Low)** Blake2b stream hashing
  fully buffers → **§4**. **F7 (Med)** `FileChunker` LOH array per chunk → **§4**.
- **F8 (Low)** SD-JWT verify re-parses payload + `.Count(c=>'.')` LINQ + rehash (`SdJwtService.cs:432,499,609`).
- **F9 (Low)** `WalletUtilities` `.Skip(1).ToArray()` byte slicing → **§3**. **F10 (Low)** Register `ICryptoModule`
  scoped not singleton → **§3**. **F11 (Low)** `Transaction` metadata deserialize→reserialize round-trip +
  `WriteIndented` (`Transaction.cs:287,373`).

### 5e. Network fan-out / background / startup
- **F1 (High)** validator 100 ms poll → **T3**. **F2 (High/Low)** gossip gRPC channel churn → **T4**.
  **F3 (High/Low)** serial submission fan-out → **T6**. **F4 (High/Low)** blueprint recovery 60 s full re-hash
  → **T3** (also gates `/api/health` 503 until first pass). **F5 (High)** `DocketBuildTriggerService` per-register
  HTTP+Mongo every 10 s + unbounded `Task.Run` heartbeat (`:90,111-125,293-301`). **F6 (High/Low)** reconnect
  jitter → **§3**. **F7 (High)** `RegisterHub.SubscribeToRegister` = HTTP per subscribe, re-run per register on
  every reconnect (`RegisterHub.cs:35-49`) — cache org active-register set per connection.
- **F8 (Med-High/Low)** token introspection uncached → **§3**. **F9 (Med)** transient `PeerServiceClient` builds
  a channel per resolve → **T4**. **F10-F11 (Med)** chatty full-replica pull + whole-chain re-finalize → **§4**.
  **F12 (Med)** 2 s per-subscriber gRPC poll → **T3**. **F13 (Med)** register events cross Redis twice; a seal
  fans into 3-4 backplane publishes — coalesce to one `DocketSealed` (thin-signal contract). **F14 (Med)**
  encryption progress crosses Redis twice per tick — throttle/coalesce. **F15 (Med)** inbox writes = COUNT(*) +
  2 publishes each — debounce/`INCR`. **F16 (Med/Low)** `/health` on the 10 s probe → **§3**. **F17 (Med/Low)**
  Peer readiness blocks loading all advertisements (`Program.cs:252-253`) — lazy/background load.
  **F18 (Med)** DID cross-resolution sequential + cache-bypass → **T6**. **F19 (Med/Low)** immediate-sync signal
  parks a thread-pool thread in a blocking wait (`RegisterSyncBackgroundService.cs:118-129`) — async primitive.
  **F20 (Med)** three overlapping convergence loops → **§4**.
- **F21-F27 (Low)** ~10 fixed-interval cleaners with no jitter/leader-gate (only `AbandonmentSweeper` uses a
  leader lock); `RotatingLeaderElection` 1 s `async void` timers that no-op in single-validator mode; several
  serial per-item fan-outs (`NotifyWorkflowCompletedAsync`, subscription paging); unconditional `MigrateAsync()`
  on the readiness path in Blueprint/Peer/Wallet (adopt Tenant's `GetPendingMigrationsAsync` guard); shared
  `DefaultRequestHeaders` mutation in `ServiceClientAuthHelper.cs:34-35`.

---

## 6. Background-loop interval map

Every steady-state loop found (add **jitter + a Redis SET-NX leader gate** to any global DB/Redis sweep so N
replicas don't fire the same work in lockstep — today only `AbandonmentSweeper` does):

| Loop | file:line | Interval | Work/tick |
|---|---|---|---|
| ValidationEngineService | `Validator/ValidationEngineService.cs:55` | **100 ms** | Redis poll per register (all) → **T3** |
| RotatingLeaderElection | `Validator/RotatingLeaderElectionService.cs:334,354` | **1 s** | async-void timers (no-op single-node) |
| PeerService tx-queue | `Peer/PeerService.cs:334` | 5 s | queue drain |
| PresentationSealSubscriber | `Blueprint/PresentationSealSubscriber.cs:90` | 5 s | Redis recovery sweep (no leader gate) |
| RegisterSyncGrpcService live poll | `Peer/RegisterSyncGrpcService.cs:382` | **2 s/subscriber** | cache scan → **T3** |
| DocketBuildTriggerService | `Validator/DocketBuildTriggerService.cs:86` | 10 s | per-register HTTP+Mongo (F5) |
| Peer relay poll | `Peer/RegisterSyncBackgroundService.cs:177` | 20 s | per-register relay wait (serial) |
| PeerService health / heartbeat / keepalive / roster | `Peer/PeerService.cs:248`, `PeerHeartbeatService.cs:61`, `RelayCommunicationService.cs:423`, `Validator/RegisterMonitoringBootstrap.cs:84` | 30 s | peer health / heartbeat / keepalive / roster HTTP |
| AbandonmentSweeper | `Blueprint/AbandonmentSweeper.cs:68` | 30 s | leader-locked expiry scan ✅ |
| BlueprintRecoveryService | `Blueprint/BlueprintRecoveryService.cs:74` | **60 s** | discover+refetch+rehash ALL registers → **T3** |
| OrgWalletReconciliationService | `Tenant/OrgWalletReconciliationService.cs:60` | 60 s | EF query + wallet-create HTTP |
| VerifiedQueueCleanup | `Validator/VerifiedQueueCleanupService.cs:40` | 60 s | in-memory |
| RegisterSync periodic | `Peer/RegisterSyncBackgroundService.cs:79` | 90 s | re-finalize whole chain (F11) |
| MemPool / UnverifiedPool cleanup | `Validator/MemPoolCleanupService.cs:33`, `UnverifiedPoolCleanupService.cs:49` | 5 min | Redis cleanup |
| PeerDataCleanup primary | `Peer/PeerDataCleanupService.cs:46` | 5 min | DB purge (loads full table, F4) |
| AdvertisementResync | `Register/AdvertisementResyncService.cs:43` | 5 min | DB + BulkAdvertise HTTP |
| ObservationStorePruner / OrphanChunkCleanup / InMemoryEncryptionOpStore / NotificationDigestWorker | `Register/ObservationStorePruner.cs:42`, `Blueprint/OrphanChunkCleanupService.cs:66`, `Blueprint/InMemoryEncryptionOperationStore.cs:34`, `Wallet/NotificationDigestWorker.cs:112` | 5 min | prune / DB delete / expiry / per-user HTTP |
| PeerDataCleanup checkpoint / discovery | `Peer/PeerDataCleanupService.cs:47`, `PeerService.cs:206` | 15 min | DB purge / discovery |
| PeerService gossip exchange | `Peer/PeerService.cs:293` | ~7.5 min | gossip |
| CitizenStatusListPublisher | `Wallet/CitizenStatusListPublisherService.cs:55` | 1 h | EF scan + re-sign |
| BadActorCleanup | `Validator/BadActorDetectorExtensions.cs:97` | 1 h | in-memory eviction |
| Audit / AuthChallenge / CustomDomain cleanup | `Tenant/AuditCleanupService.cs:67`, `AuthChallengeTokenCleanupService.cs:66`, `CustomDomainVerificationService.cs:40` | 24 h | DB purge / DNS+DB |
| SchemaIndexRefresh | `Blueprint/SchemaIndexRefreshService.cs:42` | 24 h | rebuild index |

**Startup-only (one pass):** RegisterRecovery, BloomFilterStartupRebuild, DocketCacheWarming, CacheWarming,
seed services, DatabaseInitializer, StorageEnforcement, ValidatorRegistryHydration, SystemWalletInitializer.
**Event-driven (no polling — good):** TransactionLifecycleEventBridge, EncryptionEventBridge,
RegisterEventBridgeService, EventSubscriptionHostedService, InstanceProjector, DidSorchaCacheInvalidation.

---

## 7. Verified clean (already good — don't re-investigate)

- **No sync-over-async** on EF or crypto paths; `HybridSignAsync` uses `Task.WhenAll` (old CODE-003 resolved).
  Remaining `.Result`/`.Wait()` hits are isolated to a couple of Redis/MemPool spots (§5c V7) and Blazor dialogs.
- **HTTP**: all typed clients via `IHttpClientFactory`; no `new HttpClient` per call; **no Polly retry handlers**
  → no retry-storm amplification. Service-token minting is cached.
- **SignalR**: no `Clients.All`; every send is group-scoped + authorized (thin-signal contract).
- **Health checks** don't fan out to other services (no cascading failure); readiness/liveness split exists in
  code (just not wired in compose — §3 F16).
- **Validator** batch validation + consensus votes are already parallelized with a completion-guarded timeout;
  `DocketBuildTriggerService` uses `PeriodicTimer` and skips idle registers; static `JsonSerializerOptions`
  throughout the validator.
- **EF**: no query-inside-loop `SaveChanges` N+1 in the stores; Wallet/Peer indexes are well-matched to hot
  columns; Blueprint read paths use `AsNoTracking`; Tenant migration index set matches `OnModelCreating` (no
  drift). `AuthChallengeRepository.TryConsumeAsync` is the in-repo template for set-based DML.
- **Register reads**: no per-request `JsonSerializerOptions`/`MongoClient`/`HttpClient` allocation.

---

*Prepared by five parallel static-analysis passes over `src/` at master (post the #327 VAL-001 sweep). All
findings are evidence-cited and un-verified against a running profiler — treat impact/effort as informed
estimates to prioritize a profiling + fix pass, not as measured numbers. Recommended next step: instrument the
Register read path and the action-submit pipeline under load (the two highest-traffic paths) to confirm T1/T5/
T6 before the structural work, and land the §3 quick wins opportunistically.*
