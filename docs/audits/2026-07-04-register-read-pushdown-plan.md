# Register read pushdown + OData revival — design plan

**Status:** Part A shipped · **Author:** perf initiative (T1 follow-up) · **Date:** 2026-07-04
**Prereq shipped:** the three Mongo tx indexes (#1106) that these pushed-down queries seek against.

> **Update 2026-07-04 — Part A complete (#1112 doc, #1113 A1, #1114 A2, #1115 A3).**
> Every index-able register transaction read is now pushed down to Mongo. The pushed-down methods
> (`GetLatestTransactions/Transaction`, `CountTransactions`, `CountTransactionsBefore`,
> `GetTransactionsByType`, `GetTransactionsBefore`) + `TransactionSort` enum live on
> `IReadOnlyRegisterRepository` (all three impls + cache decorator + `TransactionManager`), with
> contract tests. **Use these — not the materialise-all `GetTransactionsAsync`** — for new register
> read paths. `GetTransactionsAsync` (Find(Empty).ToList().AsQueryable()) is now reserved for the three
> inherent full-scans below.
>
> **Remaining (deliberately not done):**
> - **Inherent full-scans** (§3.3): orphan-transaction detect/purge (`Program.cs`) + register statistics
>   (`QueryManager.GetTransactionStatistics`) genuinely need every transaction. Follow-up = projection /
>   `$group`, not elimination.
> - **Part B (OData revival) — reframed as a small feature, not wiring.** On inspection the OData surface
>   is dead (`AddOData` registered, no `MapControllers`, no gateway route) AND the Explorer
>   (`ODataQueryBuilder.razor` → `ODataQueryService`) was built assuming a **single global `Transactions`
>   collection** — but storage is **per-register databases** (`sorcha_register_{id}`). So B needs a
>   **per-register-routed** OData controller (`/odata/registers/{registerId}/Transactions`, `[EnableQuery]`
>   over `collection.AsQueryable()` = `IMongoQueryable`, allowed-field/`$top` constraints) **and** an
>   Explorer **register-selector UX** (the client must pass `registerId`). Server side is mechanical +
>   testable; the selector UX is real UI work. Treat B as its own focused piece.

## 1. Problem

`IReadOnlyRegisterRepository.GetTransactionsAsync(registerId)`
(`MongoRegisterRepository.cs:554`) does `Find(Empty).ToListAsync().AsQueryable()` — it
materialises the **entire per-register transaction collection** into memory and returns an
in-memory `IQueryable`. 18 call sites then filter/sort/page/count in LINQ-to-Objects. Every read
is O(ledger): fetching one transaction, the latest page, or a count all stream + deserialise the
whole collection first. Registers are append-only, so this only gets worse.

Two independent findings shape the fix:

- **The OData surface is dead wiring.** `AddOData` + the `Transactions`/`Registers` EDM sets are
  registered (`Program.cs:76-84`) but there is **no `MapControllers`, no OData controller, and no
  gateway `/odata` route** — so `/odata/Transactions` 404s today, even though a built-but-inert
  Explorer feature (`ODataQueryBuilder.razor` → `ODataQueryService`) calls it. OData was never
  connected, not dropped.
- **The pushed-down methods largely already exist.** `GetTransactionAsync` (`Find(Eq(TxId))`),
  `QueryTransactionsAsync(predicate)` (`Find(Where(predicate))`), `GetAllTransactionsByRecipient
  AddressAsync` (`Find(AnyEq).SortByDescending`), `GetTransactionsByPrevTxIdAsync`,
  `GetCredentialIssuanceTransactionAsync`, `FindRevocationForTransactionAsync` are all `Find(filter)`
  and already index-backed. The hot paths simply don't use them.

## 2. Two consumers, two mechanisms (the governing principle)

| Consumer | Mechanism | Why |
|---|---|---|
| **A — machine/hot paths** (workflow endpoints, consensus, verify, stats) | purpose-built, index-aligned repository methods | known shapes; no query parser between caller and DB; correctness-critical |
| **B — human/admin Explorer** (ad-hoc ledger querying) | OData, **DB-backed** (`IMongoQueryable`), constrained | flexible slice/sort/page maps 1:1 to a data grid; `$metadata` self-describes |

The rule that keeps it healthy: **the flexible query surface (B) must never back a hot path (A).**

## 3. Part A — push the hot paths down

### 3.1 New repository methods (on `IReadOnlyRegisterRepository` + `MongoRegisterRepository`)

| Method | Mongo | Index |
|---|---|---|
| `GetLatestTransactionsAsync(registerId, skip, take, ct)` → `IReadOnlyList<TransactionModel>` | `Find(Empty).Sort(TimeStamp desc).Skip(skip).Limit(take)` | `TimeStamp desc` |
| `GetLatestTransactionAsync(registerId, ct)` → `TransactionModel?` | `Find(Empty).Sort(TimeStamp desc).Limit(1).FirstOrDefault` | `TimeStamp desc` |
| `CountTransactionsAsync(registerId, ct)` → `long` | `CountDocumentsAsync(Empty)` | — (metadata count) |
| `GetTransactionsByTypeAsync(registerId, TransactionType, TxSort sort, skip, take, ct)` → `IReadOnlyList<TransactionModel>` | `Find(Eq("MetaData.TransactionType", type)).Sort(sort).Skip().Limit()` | `MetaData.TransactionType` |

`TxSort` = `{ TimeStampDesc, DocketNumberDesc }` (the two orderings the call sites use). Existing
`QueryTransactionsAsync(predicate)` covers the remaining compound predicates.

### 3.2 Call-site migration (18 sites)

| Site | Currently | → migrates to |
|---|---|---|
| Program.cs:604 (disable-dev-mode) | latest tx | `GetLatestTransactionAsync` |
| Program.cs:1143 (GET transactions) | count + page | `CountTransactionsAsync` + `GetLatestTransactionsAsync(skip,take)` |
| Program.cs:1192 (graph DAG) | by-TxId + before-cursor | `GetTransactionAsync(txId)` + `GetTransactionsBeforeAsync` (cursor by TimeStamp — see note) |
| Program.cs:2087 (published blueprints) | type∈{BlueprintPublish,Control} ∧ BlueprintId | `QueryTransactionsAsync(predicate)` |
| Program.cs:2204 (governance history) | Control, DocketNumber desc, page | `GetTransactionsByTypeAsync(Control, DocketNumberDesc, page)` |
| Program.cs:2492 (proposals) | Control + TrackingData filter, page | `GetTransactionsByTypeAsync(Control, …)` then in-memory TrackingData filter (small set) |
| Program.cs:2608 (crypto-policy) | latest tx | `GetLatestTransactionAsync` |
| Program.cs:3230/3286 (orphan tx, admin) | all ∖ docketed | **stays full-scan** — see §3.3 |
| Program.cs:3454/3471 (stats count) | `.Count()` | `CountTransactionsAsync` |
| QueryManager:44 (`GetQueryableTransactionsAsync`) | passthrough IQueryable | **B backing** — see §4 |
| QueryManager:62 (`GetTransactionsPaginatedAsync`) | optional filter + count + page | `CountTransactionsAsync` + `GetLatestTransactionsAsync`; predicate path → `QueryTransactionsAsync` |
| QueryManager:292 (`GetTransactionStatistics`) | SelectMany/Distinct/Sum/MinBy | **stays full-scan / aggregate** — see §3.3 |
| CryptoPolicyService:84 (`ExtractGenesisPolicy`) | first Control by TimeStamp | `GetTransactionsByTypeAsync(Control, TimeStampAsc, 0, 1)` |
| CryptoPolicyService:108 (`FindAllPolicyUpdates`) | all Control by TimeStamp | `GetTransactionsByTypeAsync(Control, TimeStampAsc)` |
| SystemRegisterService:403 (blueprint txs) | type∈{BlueprintPublish,Control} ∧ BlueprintId | `QueryTransactionsAsync(predicate)` |
| SystemRegisterService:444 (latest tx id) | latest tx | `GetLatestTransactionAsync` |
| GovernanceRosterService:356 (control txs) | all Control by DocketNumber | `GetTransactionsByTypeAsync(Control, DocketNumberDesc)` |
| TransactionManager:138 (`GetTransactionsAsync` wrapper) | passthrough | keep only as **B backing** |

Note (graph cursor, 1192): add `GetTransactionsBeforeAsync(registerId, DateTime before, take, ct)` =
`Find(Lt(TimeStamp, before)).Sort(TimeStamp desc).Limit(take)` (rides the `TimeStamp desc` index) —
cursor pagination without materialising.

### 3.3 Inherent full-scans (do not pretend to eliminate)

- **Orphan-transaction detect/purge (3230/3286)** compares every tx against docket membership.
- **Register statistics (QueryManager:292)** aggregates over the whole ledger.

These are genuinely O(ledger). The win is **projection + streaming**, not elimination: expose
`StreamTransactionHeadersAsync(registerId)` returning an `IAsyncEnumerable<TxHeader>` with a Mongo
`.Project(...)` to only the fields needed (TxId, DocketNumber, TimeStamp), so we stop deserialising
full payloads and stop building a giant `List` in memory. Stats can move to a Mongo `$group`
aggregation in a later pass. **`log()` these as bounded full-scans** — don't let them masquerade as
cheap. Lower priority than the indexed migrations above.

### 3.4 Keep `GetTransactionsAsync` (materialise-all) only where truly needed

After migration, the only remaining callers of the blanket method are the two wrappers, which exist
to feed **B**. Repurpose them (§4) to return `IMongoQueryable`; the in-memory `.AsQueryable()`
version is deleted once nothing references it.

## 4. Part B — finish OData, DB-backed

### 4.1 Wiring

1. **Controller.** Add `TransactionsController : ODataController` (and a `RegistersController`) with
   `[EnableQuery(MaxTop = 100, AllowedQueryOptions = Filter|OrderBy|Top|Skip|Count|Select)]`. Route
   keyed per register: `/odata/registers/{registerId}/Transactions` (or resolve `registerId` from a
   required `$filter`/route — per-register DBs mean the collection must be chosen per request).
2. **Backing = `IMongoQueryable`.** The action returns
   `GetTransactionsCollection(registerId).AsQueryable()` (driver 3.9 LINQ3 → aggregation pipeline).
   OData composes `$filter/$orderby/$skip/$top` onto it → **server-side**. Never materialise.
3. **`MapControllers()`** in `Program.cs` after routing (currently absent — this is why OData 404s),
   and `AddRouteComponents` already builds the EDM.
4. **Gateway route.** Add a YARP route for `/odata/**` → register-service cluster in
   `ApiGateway/appsettings.json`.

### 4.2 Constraints (non-negotiable — otherwise OData just moves the COLLSCAN server-side)

- `AllowedFilter` / `AllowedOrderBy` **locked to indexed fields only**: `TxId`, `SenderWallet`,
  `RecipientsWallets`, `TimeStamp`, `DocketNumber`, `MetaData.TransactionType`, `MetaData.BlueprintId`,
  `MetaData.InstanceId`. Filtering/sorting on anything else → 400, not a silent scan.
- Hard `MaxTop = 100` (already configured) + default page size.
- Per-register collection resolution is **server-side** (from route), never a client `$filter` the
  user could omit — the register scope is not optional.
- `$expand` disabled (TransactionModel has no navigations anyway).

### 4.3 Client already matches

`ODataQueryService` emits `$filter/$orderby {dir}/$top/$skip/$count=true` and validates field names
against `^[A-Za-z_][A-Za-z0-9_.]*$` — it already round-trips the shape above; it just needs the
endpoint to exist. `ODataQueryBuilder.razor` is the Explorer consumer.

## 5. Rollout

1. **Doc** (this file).
2. **A** — add the repository methods + migrate the 15 index-able sites; leave the 3 full-scans with
   a projection/stream follow-up noted. Build + `dotnet test` the Register suites. PR.
3. **B** — controllers + `MapControllers` + `IMongoQueryable` backing + constraints + gateway route;
   integration test that `/odata/registers/{id}/Transactions?$filter=…&$top=…` returns a server-side
   page with `@odata.count`. PR.

## 6. Risks

- **LINQ3 translation gaps.** A `QueryTransactionsAsync(predicate)` whose expression the driver can't
  translate throws at execution. Mitigation: each migrated predicate is filter-builder-expressible
  (all are `Eq`/`AnyEq`/`In` on indexed fields); prove with a test per new method.
- **Semantic drift.** Ordering ties (e.g. `DocketNumber ?? 0`) must be preserved — Mongo sorts nulls
  low, matching `?? 0` ordering closely but verify the governance-history ordering in a test.
- **OData over-exposure.** Covered by §4.2 allow-lists; add a test asserting a filter on a
  non-allowed field returns 400.
