# Code Quality Fixes — Common Libraries

**Branch:** `feature/phase-a-quality-updates`
**Created:** 2026-02-27
**Status:** In Progress
**Scope:** All 15 src/Common/ libraries

---

## Package Updates Applied (A14 — DONE)

Safe minor/patch updates applied to `Directory.Packages.props`:

| Category | From | To | Packages |
|----------|------|----|----------|
| .NET Aspire | 13.1.0 | 13.1.2 | All 8 Aspire.* packages |
| Microsoft.* | 10.0.2 | 10.0.3 | ~40 packages (AspNetCore, EFCore, Extensions, Data) |
| Resilience | 10.2.0 | 10.3.0 | Http.Resilience, ServiceDiscovery |
| gRPC | - | - | Google.Protobuf 3.33.4→3.34.0, Grpc.Tools 2.76.0→2.78.0 |
| Testing | 18.0.1 | 18.3.0 | Microsoft.NET.Test.Sdk |
| Scalar | 2.12.24 | 2.12.50 | Scalar.AspNetCore |
| Redis | 2.10.1 | 2.11.8 | StackExchange.Redis |
| JWT | 8.15.0 | 8.16.0 | System.IdentityModel.Tokens.Jwt |
| SourceLink | 10.0.102 | 10.0.103 | Microsoft.SourceLink.GitHub |
| bunit | 2.5.3 | 2.6.2 | bunit |
| CLI | 2.0.2 | 2.0.3 | System.CommandLine |
| QR | 1.6.0 | 1.7.0 | QRCoder |
| Security | 10.0.2 | 10.0.3 | System.Security.Cryptography.ProtectedData, System.Threading.RateLimiting |

### NOT Updated (Breaking Major Versions — Separate Effort)

| Package | Current | Latest | Reason |
|---------|---------|--------|--------|
| JsonLogic | 5.5.0 | 6.0.0 | Breaking — Blueprint engine dependency |
| JsonSchema.Net | 8.0.5 | 9.1.1 | Breaking — Evaluate API changes, used by many projects |
| JsonSchema.Net.Generation | 6.0.0 | 7.1.0 | Breaking — tied to JsonSchema.Net |
| MudBlazor | 8.15.0 | 9.0.0 | Breaking — UI component changes |
| Refit | 9.0.2 | 10.0.1 | Breaking — HTTP client generation |
| SimpleBase | 4.0.2 | 5.6.0 | Breaking — encoding API changes |
| coverlet.collector | 6.0.4 | 8.0.0 | Breaking — test coverage tool |
| ModelContextProtocol | 0.7.0-preview.1 | 1.0.0 | Breaking — MCP server API |
| Anthropic.SDK | 4.0.0 | 5.10.0 | Breaking — AI integration |
| JsonE.Net | 2.5.1 | 3.0.0 | Breaking — Demo project dependency |

---

## Critical Bugs (5)

### FIX-C1: Redis `RemoveByPatternAsync` corrupts wildcard patterns
**Library:** Sorcha.Storage.Redis
**File:** `src/Common/Sorcha.Storage.Redis/RedisCacheStore.cs` ~line 278-279
**Bug:** Strips ALL wildcards from user pattern, then re-appends `*` at end only. Pattern `"user:*:sessions"` becomes `"sorcha:user::sessions*"` — completely wrong.
**Fix:** Use `$"{_keyPrefix}{pattern}"` as the full pattern, don't strip wildcards.

### FIX-C2: `ComputeSHA256RIPEMD160` is a broken placeholder
**Library:** Sorcha.Cryptography
**File:** `src/Common/Sorcha.Cryptography/Core/HashProvider.cs` lines 166-178
**Bug:** Named SHA256-RIPEMD160 but does double-SHA256 truncated to 20 bytes. Returns incorrect hashes.
**Fix:** Either implement using BouncyCastle's RIPEMD-160, or throw `NotImplementedException` with a clear message. Remove the stale "placeholder" comment.

### FIX-C3: `DeleteWalletAsync` hardcodes wrong `OldStatus`
**Library:** Sorcha.Wallet.Core
**File:** `src/Common/Sorcha.Wallet.Core/Services/Implementation/WalletManager.cs` line 377
**Bug:** `OldStatus = WalletStatus.Active` should be `OldStatus = wallet.Status`. Event reports wrong transition for non-Active wallets.
**Fix:** Change `WalletStatus.Active` to `wallet.Status`.

### FIX-C4: `MongoSchemaRepository` constructor `.GetAwaiter().GetResult()` deadlock
**Library:** Sorcha.Blueprint.Schemas
**File:** `src/Common/Sorcha.Blueprint.Schemas/Repositories/MongoSchemaRepository.cs` lines 38-40
**Bug:** Blocking async call in constructor causes deadlocks in ASP.NET contexts.
**Fix:** Move index creation to an `IHostedService.StartAsync` or a lazy initialization pattern.

### FIX-C5: `PeerServiceClient` uses `new HttpClient()` — socket exhaustion
**Library:** Sorcha.ServiceClients
**File:** `src/Common/Sorcha.ServiceClients/Peer/PeerServiceClient.cs` line 62
**Bug:** Bypasses `IHttpClientFactory`, causing socket exhaustion and stale DNS.
**Fix:** Register with `services.AddHttpClient<PeerServiceClient>()` in `ServiceCollectionExtensions`, accept `HttpClient` via constructor injection.

---

## Important Fixes (20)

### FIX-I1: TOCTOU race in `InMemoryWormStore.AppendAsync`
**File:** `src/Common/Sorcha.Storage.InMemory/InMemoryWormStore.cs` lines 50-61
**Fix:** Remove redundant `ContainsKey` check before `TryAdd`. `TryAdd` is already atomic.

### FIX-I2: MongoDB `InsertAsync` pre-check round-trip + TOCTOU
**File:** `src/Common/Sorcha.Storage.MongoDB/MongoDocumentStore.cs` lines 108-118
**Fix:** Remove `GetAsync` pre-check, catch `MongoWriteException` with `DuplicateKey` error code instead.

### FIX-I3: `VerifiedCache` fetches WORM store on every cache hit
**File:** `src/Common/Sorcha.Storage.Abstractions/Caching/VerifiedCache.cs` lines 87-106
**Fix:** Make verification probabilistic or background-based, not per-read.

### FIX-I4: `StorageProviderFactory.GetHealthAsync` returns fake healthy status
**File:** `src/Common/Sorcha.Storage.Abstractions/StorageProviderFactory.cs` lines 94-102
**Fix:** Return `TierHealthStatus.Unknown` instead of fake `Healthy(responseTimeMs: 0)`.

### FIX-I5: `EFCoreRepository.ExistsAsync` loads full entity
**File:** `src/Common/Sorcha.Storage.EFCore/EFCoreRepository.cs` lines 122-126
**Fix:** Use `AnyAsync` instead of `FindAsync` for existence checks.

### FIX-I6: EFCore mutating methods — undocumented unit-of-work contract
**File:** `src/Common/Sorcha.Storage.EFCore/EFCoreRepository.cs`
**Fix:** Add XML doc `<remarks>` on the class explaining that `AddAsync/UpdateAsync/DeleteAsync` stage changes in the change tracker; `SaveChangesAsync()` is required to persist.

### FIX-I7: O(n) latency tracking duplicated in Redis + VerifiedCache
**Files:** `src/Common/Sorcha.Storage.Redis/RedisCacheStore.cs` lines 395-403, `src/Common/Sorcha.Storage.Abstractions/Caching/VerifiedCache.cs` lines 370-378
**Fix:** Replace `List<double>.RemoveAt(0)` with `Queue<double>.Dequeue()`. Extract shared `LatencyTracker` utility.

### FIX-I8: Bare `catch {}` in all 4 `WalletUtilities` crypto methods
**File:** `src/Common/Sorcha.Cryptography/Utilities/WalletUtilities.cs` lines 62-65, 109-112, 163-166, 192-194
**Fix:** Catch specific exceptions (`FormatException`, `CryptographicException`), log, return null. Don't swallow `OutOfMemoryException`.

### FIX-I9: Algorithm-to-enum mapping duplicated in 4 places
**Files:**
- `src/Common/Sorcha.Cryptography/Core/CryptoModule.cs` lines 950-972
- `src/Common/Sorcha.Wallet.Core/Services/Implementation/KeyManagementService.cs` lines 223-235
- `src/Common/Sorcha.Wallet.Core/Services/Implementation/TransactionService.cs` lines 196-208
**Fix:** Create `WalletNetworksParser` utility in `Sorcha.Cryptography`, reference from all locations.

### FIX-I10: `TransactionFactory.GetSerializer` ignores version parameter
**File:** `src/Common/Sorcha.TransactionHandler/Versioning/TransactionFactory.cs` lines 88-91
**Fix:** Either implement version routing or remove the parameter. Add `// TODO:` if V2 is planned.

### FIX-I11: `.GetAwaiter().GetResult()` in TransactionBuilder and serializers
**Files:** `src/Common/Sorcha.TransactionHandler/Core/TransactionBuilder.cs` line 112, `BinaryTransactionSerializer.cs` line 90, `JsonTransactionSerializer.cs` line 43
**Fix:** Make `AddPayload` async (`AddPayloadAsync`), or document the sync-over-async as intentional with justification.

### FIX-I12: `ValidateSignatures` rejects PQC algorithms
**File:** `src/Common/Sorcha.Validator.Core/Validators/TransactionValidator.cs` lines 247-256
**Fix:** Add `ML-DSA-65`, `SLH-DSA-128s`, `SLH-DSA-192s` to the `validAlgorithms` array.

### FIX-I13: `SchemaStore.ListAsync` pagination broken with mixed sources
**File:** `src/Common/Sorcha.Blueprint.Schemas/Services/SchemaStore.cs` lines 69-117
**Fix:** Track system schema cursor separately, or exclude system schemas from cursor-based pagination.

### FIX-I14: MongoDB regex injection (ReDoS) in `MongoSchemaRepository.ListAsync`
**File:** `src/Common/Sorcha.Blueprint.Schemas/Repositories/MongoSchemaRepository.cs` ~line 152
**Fix:** Sanitize search input — escape regex special chars and enforce max length.

### FIX-I15: Dead `_useGrpc` field in `RegisterServiceClient`
**File:** `src/Common/Sorcha.ServiceClients/Register/RegisterServiceClient.cs` lines 22, 44, 52-54
**Fix:** Remove field, configuration read, and log reference. Update log to always say "HTTP".

### FIX-I16: `SetAuthHeaderAsync` duplicated across service clients
**Files:** Multiple service client files
**Fix:** Extract `ServiceClientAuthHelper.SetAuthHeaderAsync(HttpClient, IServiceAuthClient, ILogger, CancellationToken)` as static helper.

### FIX-I17: IP-parsing logic duplicated in ServiceDefaults
**Files:** `src/Common/Sorcha.ServiceDefaults/Extensions.cs` lines 415-431, `InputValidationMiddleware.cs` lines 234-243
**Fix:** Extract `ClientIpHelper.GetClientIp(HttpContext)` utility.

### FIX-I18: Authorization policy names are magic strings
**File:** `src/Common/Sorcha.ServiceDefaults/AuthorizationPolicyExtensions.cs` lines 53-85
**Fix:** Create `public static class AuthorizationPolicies` with `public const string` for each policy name.

### FIX-I19: `Console.WriteLine` in `JwtAuthenticationExtensions`
**File:** `src/Common/Sorcha.ServiceDefaults/JwtAuthenticationExtensions.cs` lines 370, 378, 379
**Fix:** Replace with `ILogger` calls. Add logger parameter to `GetOrCreateDevSigningKey`.

### FIX-I20: ~150 lines AES-GCM code duplicated between encryption providers
**Files:** `src/Common/Sorcha.Wallet.Core/Encryption/Providers/WindowsDpapiEncryptionProvider.cs`, `LinuxSecretServiceEncryptionProvider.cs`
**Fix:** Extract shared `EncryptionProviderBase` abstract class with AES-GCM, TTL-cache, and key-file logic.

---

## Minor Fixes (15)

### FIX-M1: Dead field `_jsonOptions` in `VerifiedCache`
**File:** `src/Common/Sorcha.Storage.Abstractions/Caching/VerifiedCache.cs` lines 29, 63-66

### FIX-M2: Dead field `_idSetter` in `InMemoryWormStore`
**File:** `src/Common/Sorcha.Storage.InMemory/InMemoryWormStore.cs` lines 23, 38

### FIX-M3: Duplicate config classes for Register cache (`VerifiedCacheConfiguration` vs `RegisterCacheConfiguration`)
**File:** `src/Common/Sorcha.Storage.Abstractions/Caching/IVerifiedCache.cs` line 208, `StorageConfiguration.cs`

### FIX-M4: Null connection string silently accepted in EFCore
**File:** `src/Common/Sorcha.Storage.EFCore/Extensions/EFCoreServiceExtensions.cs` lines 28-39

### FIX-M5: Misleading `AddInMemoryStorageProviders` only registers cache
**File:** `src/Common/Sorcha.Storage.InMemory/Extensions/InMemoryServiceExtensions.cs` lines 91-96

### FIX-M6: `#region` blocks in Cryptography and TransactionHandler files
**Files:** Multiple Core/ files

### FIX-M7: Missing XML docs on public constructors in Validator.Core
**Files:** `TransactionValidator.cs` line 19, `DocketValidator.cs` line 18

### FIX-M8: `[Obsolete(error:true)]` method `GenerateAddressAsync` has dead implementation body
**File:** `src/Common/Sorcha.Wallet.Core/Services/Implementation/WalletManager.cs` lines 392-421

### FIX-M9: Missing XML docs on 4 core public methods in Extensions.cs
**File:** `src/Common/Sorcha.ServiceDefaults/Extensions.cs` lines 26, 52, 111, 120

### FIX-M10: Multiple unrelated types in single files (JwtAuthenticationExtensions, IValidatorServiceClient, etc.)
**Files:** Multiple — extract to separate files

### FIX-M11: Empty object initializer `new() { }` in OpenApiExtensions
**File:** `src/Common/Sorcha.ServiceDefaults/OpenApiExtensions.cs` lines 38, 44

### FIX-M12: Stale commented-out Aspire scaffold blocks
**File:** `src/Common/Sorcha.ServiceDefaults/Extensions.cs` lines 43-47, 79-80, 101-106

### FIX-M13: `SerilogExtensions.AddSerilogLogging` accepts concrete `WebApplicationBuilder`
**File:** `src/Common/Sorcha.ServiceDefaults/SerilogExtensions.cs` line 22

### FIX-M14: Constructor parameter ordering inconsistent across service clients
**Files:** `ValidatorServiceClient.cs` places HttpClient last; all others place it first

### FIX-M15: `UpdatedAt` not set in `DeleteWalletAsync`
**File:** `src/Common/Sorcha.Wallet.Core/Services/Implementation/WalletManager.cs`

---

## Test Gaps (A13)

### TEST-1: Create `tests/Sorcha.Storage.EFCore.Tests/`
Test EFCore repository operations with InMemory provider. Key tests: `ExistsAsync`, `AddAsync`→`SaveChangesAsync`→`GetByIdAsync`, null connection string guard.

### TEST-2: Create `tests/Sorcha.Storage.MongoDB.Tests/`
Test MongoDB document store operations. Key tests: `InsertAsync` duplicate handling, `GetAsync` with missing documents. Use Testcontainers.MongoDb.

### TEST-3: Create `tests/Sorcha.Storage.Redis.Tests/`
Test Redis cache store operations. Key tests: `RemoveByPatternAsync` with various patterns, circuit breaker behavior, latency tracking. Use Testcontainers.Redis.

### TEST-4: Create `tests/Sorcha.Tenant.Models.Tests/`
Test `PermissionFlags` bitwise operations, `TokenClaims` constant validation. Lightweight but ensures model correctness.

### TEST-5: Create `tests/Sorcha.Wallet.Core.Tests/`
Dedicated unit tests for `WalletManager`, `KeyManagementService`, `TransactionService` without service layer dependencies.
