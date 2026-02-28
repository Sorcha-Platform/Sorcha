# Phase C — Services Code Quality Review Findings

**Date:** 2026-02-28
**Scope:** All 7 services in `src/Services/` (~74K LoC, ~380 files)
**Methodology:** Parallel review agents (3 groups) applying the standard 10-point checklist

---

## Summary

| Category | Count |
|----------|-------|
| Critical | 7 |
| Important | 9 |
| Minor | 5 |
| **Total** | **21** |

---

## Critical Findings

### C3a-1: Blocking async `.GetAwaiter().GetResult()` in TransactionQueueManager constructor
- **File:** `Peer.Service/Distribution/TransactionQueueManager.cs:44`
- **Fix:** Move initialization to `IHostedService.StartAsync` or lazy-init pattern

### C3a-2: Blocking async `.GetAwaiter().GetResult()` in StreamingCommunicationClient.Dispose()
- **File:** `Peer.Service/Communication/StreamingCommunicationClient.cs:174`
- **Fix:** Implement `IAsyncDisposable`, best-effort sync Dispose

### C3a-3: `Console.WriteLine` in Register.Service Program.cs (logs connection string!)
- **File:** `Register.Service/Program.cs:90,101`
- **Fix:** Replace with `ILogger<Program>`, redact connection string

### C3a-4: `LoggerFactory.Create(AddConsole)` bypasses DI pipeline (3 locations)
- **File:** `Peer.Service/Communication/CommunicationProtocolManager.cs:141,260,269`
- **Fix:** Inject `ILoggerFactory` via constructor

### C3a-5: Thread-unsafe `static Dictionary` in ChatHub (SignalR concurrent connections)
- **File:** `Blueprint.Service/Hubs/ChatHub.cs:43`
- **Fix:** Replace with `ConcurrentDictionary<string, CancellationTokenSource>`

### C3a-6: Bare `catch` swallows all exceptions in HealthAggregationService
- **File:** `ApiGateway/Services/HealthAggregationService.cs:185`
- **Fix:** Log exception at Warning level

### C3a-7: Swallowed exception in fire-and-forget Task.Run
- **File:** `Blueprint.Service/Endpoints/SchemaLibraryEndpoints.cs:231`
- **Fix:** Log exception explicitly in catch block

---

## Important Findings

### C3b-1: SemaphoreSlim without IDisposable (4 classes)
- `Blueprint.Service/Services/StatusListManager.cs:56`
- `Peer.Service/Discovery/PeerDiscoveryService.cs:23`
- `Peer.Service/Discovery/PeerExchangeService.cs:23`
- `Validator.Service/Services/PendingDocketStore.cs:17`
- **Fix:** Implement `IDisposable` on each class

### C3b-2: Thread-unsafe `HashSet<string>` in GossipProtocolEngine
- **File:** `Peer.Service/Distribution/GossipProtocolEngine.cs:277`
- **Fix:** Replace with `ConcurrentDictionary<string, byte>` as set

### C3b-3: Fire-and-forget Task.Run per-request in RegisterCreationOrchestrator
- **File:** `Register.Service/Services/RegisterCreationOrchestrator.cs:206`
- **Fix:** Move cleanup to background service or inline call

### C3b-4: Unobserved task in ParticipantIndexService
- **File:** `Register.Service/Services/ParticipantIndexService.cs:105`
- **Fix:** Assign to `_ =` to explicitly discard

### C3b-5: TOCTOU race in InMemoryInstanceStore.UpdateAsync
- **File:** `Blueprint.Service/Storage/InMemoryInstanceStore.cs:48-62`
- **Fix:** Use ConcurrentDictionary atomic operations or lock

### C3b-6: Hardcoded placeholder URL in StatusListManager (duplicated)
- **Files:** `Blueprint.Service/Services/StatusListManager.cs:69`, `StatusListEndpoints.cs:58`
- **Fix:** Make configuration required, remove fallback

### C3b-7: Missing CancellationToken propagation in ActionResolverService
- **File:** `Blueprint.Service/Services/Implementation/ActionResolverService.cs:66`
- **Fix:** Pass `cancellationToken` to `_blueprintStore.GetAsync`

### C3b-8: `.Result` on completed tasks in WalletEndpoints (exception wrapping risk)
- **File:** `Wallet.Service/Endpoints/WalletEndpoints.cs:466-467`
- **Fix:** Use `await` instead of `.Result`

### C3b-9: Hardcoded 24h TTL in TokenRevocationService (duplicated)
- **File:** `Tenant.Service/Services/TokenRevocationService.cs:131,160`
- **Fix:** Add configurable property to `TokenRevocationConfiguration`

---

## Minor Findings

### C3c-1: `#region` blocks — 59 total
- Tenant.Service: 27 across 7 files
- Validator.Service: 32 across 17 files
- **Fix:** Remove all `#region`/`#endregion` pairs

### C3c-2: Hardcoded magic values in Peer.Service
- `TransactionDistributionService.cs:142` — `maxBatch = 10`
- `PeerDiscoveryService.cs:220` — 5s ping timeout
- **Fix:** Source from configuration

### C3c-3: Bare `catch` in StunClient.GetLocalIPAddress()
- **File:** `Peer.Service/Network/StunClient.cs:319`
- **Fix:** Log at Debug level

### C3c-4: Synchronous Redis `tran.Execute()` in async context
- **File:** `Register.Service/Services/PendingRegistrationStore.cs:83`
- **Fix:** Use `ExecuteAsync()`

### C3c-5: Dockerfile COPY layer caching (informational)
- Blueprint.Service Dockerfile copies only 3/11 dependency csproj files for restore
- **Deferred:** `COPY src/` on the next line captures everything anyway
