# UI Service Contracts (Feature 049)

## IServicePrincipalService (New)

```csharp
namespace Sorcha.UI.Core.Services;

public interface IServicePrincipalService
{
    Task<ServicePrincipalListResult> ListAsync(bool includeInactive = false, CancellationToken ct = default);
    Task<ServicePrincipalViewModel?> GetAsync(Guid id, CancellationToken ct = default);
    Task<ServicePrincipalSecretViewModel> CreateAsync(CreateServicePrincipalRequest request, CancellationToken ct = default);
    Task<ServicePrincipalViewModel?> UpdateScopesAsync(Guid id, string[] scopes, CancellationToken ct = default);
    Task<bool> SuspendAsync(Guid id, CancellationToken ct = default);
    Task<bool> ReactivateAsync(Guid id, CancellationToken ct = default);
    Task<bool> RevokeAsync(Guid id, CancellationToken ct = default);
    Task<ServicePrincipalSecretViewModel> RotateSecretAsync(Guid id, CancellationToken ct = default);
}
```

**Implementation**: `ServicePrincipalService` using `HttpClient` → `GET/POST/PUT/DELETE /api/service-principals/*`

---

## ISystemRegisterService (New)

```csharp
namespace Sorcha.UI.Core.Services;

public interface ISystemRegisterService
{
    Task<SystemRegisterViewModel?> GetStatusAsync(CancellationToken ct = default);
    Task<BlueprintPageResult> GetBlueprintsAsync(int page = 1, int pageSize = 20, CancellationToken ct = default);
    Task<BlueprintDetailViewModel?> GetBlueprintAsync(string blueprintId, CancellationToken ct = default);
    Task<BlueprintDetailViewModel?> GetBlueprintVersionAsync(string blueprintId, long version, CancellationToken ct = default);
}
```

**Implementation**: `SystemRegisterService` using `HttpClient` → `GET /api/system-register/*`

---

## IValidatorAdminService (Extend existing)

```csharp
// Add to existing interface (currently has GetMempoolStatusAsync + GetRegisterMempoolAsync)

// Consent Queue
Task<ConsentQueueViewModel> GetConsentQueueAsync(CancellationToken ct = default);
Task<List<PendingValidatorViewModel>> GetPendingValidatorsAsync(string registerId, CancellationToken ct = default);
Task<bool> ApproveValidatorAsync(string registerId, string validatorId, CancellationToken ct = default);
Task<bool> RejectValidatorAsync(string registerId, string validatorId, string? reason = null, CancellationToken ct = default);
Task<List<ApprovedValidatorInfo>> RefreshApprovedValidatorsAsync(string registerId, CancellationToken ct = default);

// Metrics
Task<AggregatedMetricsViewModel> GetAggregatedMetricsAsync(CancellationToken ct = default);
Task<ValidationSummaryViewModel> GetValidationMetricsAsync(CancellationToken ct = default);
Task<ConsensusSummaryViewModel> GetConsensusMetricsAsync(CancellationToken ct = default);
Task<PoolSummaryViewModel> GetPoolMetricsAsync(CancellationToken ct = default);
Task<CacheSummaryViewModel> GetCacheMetricsAsync(CancellationToken ct = default);

// Threshold
Task<List<ThresholdConfigViewModel>> GetThresholdStatusAsync(CancellationToken ct = default);
Task<ThresholdConfigViewModel> SetupThresholdAsync(ThresholdSetupRequest request, CancellationToken ct = default);

// Config
Task<ValidatorConfigViewModel> GetConfigAsync(CancellationToken ct = default);
```

---

## IRegisterService (Extend existing)

```csharp
// Add to existing interface (currently has GetRegistersAsync, GetRegisterAsync, etc.)

Task<RegisterPolicyViewModel?> GetPolicyAsync(string registerId, CancellationToken ct = default);
Task<PolicyHistoryViewModel> GetPolicyHistoryAsync(string registerId, int page = 1, int pageSize = 20, CancellationToken ct = default);
Task<PolicyUpdateProposalViewModel?> ProposePolicyUpdateAsync(string registerId, RegisterPolicyFields policy, CancellationToken ct = default);
```
