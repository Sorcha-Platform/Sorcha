# Codebase Audit — 2026-04-01

Comprehensive audit of dead code, duplicates, constitution compliance, validation, security, and efficiency.

---

## CRITICAL

- [ ] **SEC-001**: CORS `AllowAnyOrigin()` at service level in `src/Common/Sorcha.ServiceDefaults/CorsExtensions.cs:25-27` — any service accessed directly bypasses gateway CORS. Restrict to specific origins or remove service-level CORS entirely.
- [ ] **SEC-002**: Peer subscribe endpoint `POST /api/registers/{registerId}/subscribe` is `.AllowAnonymous()` in `src/Services/Sorcha.Peer.Service/Program.cs:660` — tracked as #165, must be resolved.
- [ ] **CODE-001**: 50+ direct `new HttpClient(handler)` instantiations in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Extensions/ServiceCollectionExtensions.cs` — causes socket exhaustion and poor connection pooling. Refactor to `AddHttpClient<T>()` typed clients.

## HIGH

- [ ] **CODE-002**: Sync-over-async `.Result` in `src/Services/Sorcha.ApiGateway/Services/AlertAggregationService.cs:41-42` — `validatorTask.Result` and `peerTask.Result` risk deadlocks. Use `await Task.WhenAll()` then access results.
- [ ] **CODE-003**: Sync-over-async `.Result` in `src/Common/Sorcha.Cryptography/Core/CryptoModule.cs:899-900` — `classicalTask.Result` and `pqcTask.Result` after `Task.WhenAll()`. Replace with awaited values.
- [ ] **CODE-004**: Sync-over-async `.Result` in `src/Services/Sorcha.Validator.Service/Services/ConsensusEngine.cs:150-151` — `.Select(t => t.Result)` pattern fragile even with IsCompleted guard.
- [ ] **VAL-001**: Request DTOs lack explicit validation attributes (`[Required]`, `[StringLength]`, etc.) — global middleware catches attacks but not business logic validation. Files: `Sorcha.Tenant.Service/Models/Dtos/OrganizationDtos.cs`, `Sorcha.Blueprint.Service/Models/Requests/ActionSubmissionRequest.cs`.

## MEDIUM

- [ ] **DEAD-001**: 15+ unused model classes in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Models/` — `ActivityEventDto.cs`, `OrganizationConfigurationViewModel.cs`, `CredentialLifecycleRequest.cs`, `CredentialMatchResult.cs`, `CredentialNotification.cs`, `IssuanceSummaryViewModel.cs`, `CreatePresentationRequestViewModel.cs`, `StatusListViewModel.cs`, `ODataQueryModel.cs`, `FileAttachmentInfo.cs`, `FormSubmission.cs`, `ProofAttachment.cs`, `WalletAccessGrantViewModel.cs`. Delete or document purpose.
- [ ] **DEAD-002**: Unused CLI models in `src/Apps/Sorcha.Cli/Models/` — `ActionExecuteCliRequest`, `BootstrapRequest`, `CredentialSummary` never instantiated outside their definition files.
- [ ] **DEAD-003**: Unused interfaces — `src/Apps/Sorcha.Cli/Services/IAdminServiceClient.cs`, `src/Services/Sorcha.Tenant.Service/Services/IDashboardService.cs`. Remove if deprecated.
- [ ] **DUP-001**: 40+ inline `new JsonSerializerOptions()` instances across CLI commands and services. Create shared `SorchaJsonOptions` in `Sorcha.ServiceDefaults` and reference everywhere.
- [ ] **DUP-002**: Duplicate encryption implementations across 123 files in `Sorcha.Cryptography/`, `Sorcha.TransactionHandler/Encryption/`, `Sorcha.Wallet.Core/Encryption/`, CLI infrastructure. Audit for consolidation opportunities.
- [ ] **SEC-003**: Fire-and-forget Redis operations in `src/Services/Sorcha.Peer.Service/Replication/RegisterAdvertisementService.cs:368-374` — failed writes silently logged but never retried. Add retry logic or make awaitable.
- [ ] **SEC-004**: Public org lookup `GET /api/organizations/by-subdomain/{subdomain}` is `.AllowAnonymous()` in `src/Services/Sorcha.Tenant.Service/Endpoints/OrganizationEndpoints.cs:91` — enables subdomain enumeration. Consider rate limiting.
- [ ] **CODE-005**: `async void` event handler in `src/Services/Sorcha.Validator.Service/Services/RotatingLeaderElectionService.cs:465` — `OnValidatorListChanged`. Verify intentional event handler pattern; if not, change to `Task`-returning.
- [ ] **CODE-006**: `ManualResetEventSlim.Wait()` inside `Task.Run()` in `src/Services/Sorcha.Peer.Service/Replication/RegisterSyncBackgroundService.cs:108` — blocks thread pool thread for up to 5 minutes. Replace with `SemaphoreSlim` or `Channel<Unit>`.
- [ ] **CODE-007**: Placeholder test files `tests/Sorcha.UI.Core.Tests/UnitTest1.cs` and `tests/Sorcha.UI.Integration.Tests/UnitTest1.cs` — rename or delete.

## LOW

- [ ] **DOC-001**: Missing XML documentation on 10+ public constructors in Blueprint Service implementations — `AnthropicProviderService.cs:26`, `ChatOrchestrationService.cs:172`, `ActionExecutionService.cs:65`, `ActionResolverService.cs:31`, `BlueprintRecoveryService.cs:22`, `EncryptionBackgroundService.cs:41`, `EventsHubNotificationBridge.cs:40`, `InMemoryEncryptionOperationStore.cs:21`, `NotificationService.cs:20`.
- [ ] **DOC-002**: Missing XML documentation on MCP Server services — `McpAuthorizationService.cs`, `RateLimitService.cs`. Minimal doc coverage.
- [ ] **DOC-003**: Missing XML documentation on PeerRouter gRPC services — `RouterDiscoveryService.cs` methods undocumented.
- [ ] **DOC-004**: Missing XML documentation on CLI services — `ConfigurationService.cs` (4 docs vs 11 public members), `DemoAuthService.cs` (3 docs vs 10 public members).
- [ ] **CODE-008**: Only 4 uses of `.ConfigureAwait(false)` in entire codebase. Library/infrastructure code should use it on all await points to prevent context deadlocks.
- [ ] **CODE-009**: Commented-out code in CLI commands (96 lines in `RegisterCommands.cs`, 65 in `PeerCommands.cs`, 57 in `QueryCommands.cs`, 49 in `OrganizationCommands.cs`). Review and clean up if truly dead.
- [ ] **DUP-003**: CLI has 9 separate Refit service client interfaces — consider base abstraction or factory pattern for shared auth/error handling.
- [ ] **DUP-004**: Tenant Service has both gRPC and REST paths for similar operations (e.g., org lookup via proto `GetSystemOrganizationConfig` and REST `/api/organizations/by-subdomain`). Consolidate to single protocol per use case.
- [ ] **DUP-005**: API Gateway maintains legacy route remapping (`/api/blueprint/{**}` → `/api/{**}`) alongside current routes. Remove legacy routes if no longer needed.
- [ ] **CODE-010**: `CancellationToken.None` used in background cleanup code — `RegisterSyncBackgroundService.cs:570`, `WalletEndpoints.cs:390`. Acceptable for fire-and-forget but document reasoning.
- [ ] **TEST-001**: Pre-existing test failures — `QueryApiTests.GetTransactionsByWallet_WithoutRegisterId_ShouldReturn400BadRequest` (Register Service), `ParticipantTests.Constructor_ShouldInitializeWithDefaults`, `ValidatorRegistryApprovalTests.RejectValidatorAsync`. Fix or remove.
- [ ] **TEST-002**: 68 pre-existing compilation errors in `Sorcha.Register.Core.Tests` — `TenantId` property removed from models but tests not updated.
- [ ] **TEST-003**: 30 flaky E2E tests — timing-dependent Playwright tests (WalletDetail_HasTabs, Flow1/Flow2 walkthrough, ChatDesigner_ConnectsToSignalRHub). Add retry logic or increase timeouts.

---

## Summary

| Severity | Count |
|----------|-------|
| Critical | 3 |
| High | 4 |
| Medium | 11 |
| Low | 15 |
| **Total** | **33** |

### Positive Findings
- License headers: 100% compliant across all sampled files
- Error handling: 1,934 catch blocks, none empty, all with proper logging
- Security headers: Properly implemented (X-Frame-Options, CSP, HSTS, etc.)
- Private field naming: No `_camelCase` violations found
- Async method naming: All methods properly suffixed with `Async`
- Interface naming: All properly prefixed with `I`
- JWT validation: Proper lifetime, issuer, audience checks with token revocation
- Service-to-service auth: Properly implemented via ServiceClientAuthHelper
- No hardcoded secrets in source code
- No stack traces exposed in HTTP error responses
