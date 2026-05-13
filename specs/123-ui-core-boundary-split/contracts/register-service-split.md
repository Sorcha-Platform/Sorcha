# Contract — `IRegisterService` Split

The canonical bi-modal interface; the case that defeated Feature 122 Phase 2.

## Before

`Sorcha.UI.Core/Services/IRegisterService.cs` — one interface, nine methods, two audiences:

```csharp
namespace Sorcha.UI.Core.Services;

public interface IRegisterService
{
    // user-facing — read
    Task<IReadOnlyList<RegisterViewModel>> GetRegistersAsync(CancellationToken ct = default);
    Task<RegisterViewModel?> GetRegisterAsync(string registerId, CancellationToken ct = default);

    // admin / governance
    Task<GovernanceRosterViewModel?> GetGovernanceRosterAsync(string registerId, CancellationToken ct = default);
    Task<InitiateRegisterResponse?> InitiateRegisterAsync(...);
    Task<FinalizeRegisterResponse?> FinalizeRegisterAsync(...);
    Task<RegisterPolicyViewModel?> GetPolicyAsync(string registerId, CancellationToken ct = default);
    Task<PolicyHistoryViewModel> GetPolicyHistoryAsync(string registerId, int page = 1, int pageSize = 20, CancellationToken ct = default);
    Task<PolicyUpdateProposalViewModel?> ProposePolicyUpdateAsync(string registerId, RegisterPolicyFields policy, CancellationToken ct = default);
    Task DisableDevModeAsync(string registerId, CancellationToken ct = default);
}
```

## After

Two interfaces in audience-specific folders, both implemented by `RegisterService`.

```csharp
// Sorcha.UI.Core/Services/User/IRegisterReadService.cs
namespace Sorcha.UI.Core.Services;

/// <summary>
/// User-facing register read operations. Consumed by pages that display
/// the registers a user can see. No governance, no policy, no admin.
/// </summary>
public interface IRegisterReadService
{
    Task<IReadOnlyList<RegisterViewModel>> GetRegistersAsync(CancellationToken ct = default);
    Task<RegisterViewModel?> GetRegisterAsync(string registerId, CancellationToken ct = default);
}
```

```csharp
// Sorcha.UI.Core/Services/Admin/IRegisterGovernanceService.cs
namespace Sorcha.UI.Core.Services;

/// <summary>
/// Admin/governance operations on a register. Consumed by admin pages that
/// initiate registers, edit policy, propose policy updates, view governance
/// rosters, or toggle developer-mode controls.
/// </summary>
public interface IRegisterGovernanceService
{
    Task<GovernanceRosterViewModel?> GetGovernanceRosterAsync(string registerId, CancellationToken ct = default);
    Task<InitiateRegisterResponse?> InitiateRegisterAsync(...);
    Task<FinalizeRegisterResponse?> FinalizeRegisterAsync(...);
    Task<RegisterPolicyViewModel?> GetPolicyAsync(string registerId, CancellationToken ct = default);
    Task<PolicyHistoryViewModel> GetPolicyHistoryAsync(string registerId, int page = 1, int pageSize = 20, CancellationToken ct = default);
    Task<PolicyUpdateProposalViewModel?> ProposePolicyUpdateAsync(string registerId, RegisterPolicyFields policy, CancellationToken ct = default);
    Task DisableDevModeAsync(string registerId, CancellationToken ct = default);
}
```

```csharp
// Sorcha.UI.Core/Services/RegisterService.cs — implementation unchanged in body,
// just declares both interfaces.
public class RegisterService : IRegisterReadService, IRegisterGovernanceService
{
    // existing constructor and method bodies — unchanged
}
```

```csharp
// Sorcha.UI.Core/Extensions/ServiceCollectionExtensions.cs
// Before: services.AddScoped<IRegisterService, RegisterService>();
services.AddScoped<RegisterService>();
services.AddScoped<IRegisterReadService>(sp => sp.GetRequiredService<RegisterService>());
services.AddScoped<IRegisterGovernanceService>(sp => sp.GetRequiredService<RegisterService>());
```

(Two-stage registration ensures both interfaces resolve to the *same* scoped instance, matching pre-refactor behaviour where any `IRegisterService` injection got one instance per scope.)

## Migration path for consumers

1. Grep host-app pages for `@inject IRegisterService` and constructor parameters of type `IRegisterService`.
2. For each consumer, inspect which methods it calls:
   - Calls only read methods → switch injection to `IRegisterReadService`.
   - Calls only governance methods → switch injection to `IRegisterGovernanceService`.
   - Calls methods from both halves → inject both (two `@inject` lines or two constructor parameters).
3. Update test code that mocks `IRegisterService` to mock whichever narrower interface(s) the test exercises.

## Consumer-update expected scale

Pre-refactor grep estimate (based on the methods' nature):

- `IRegisterReadService` users: dashboard pages, register-list pages, register-detail pages, user-facing components. Expected ~6-10 sites.
- `IRegisterGovernanceService` users: admin pages for register policy, governance roster, register initiation. Expected ~3-5 sites.
- Dual-injection: rare. If any page genuinely needs both halves it likely should be split into separate pages, but in-scope migration just adds both injections.

Final counts go into `tasks.md` once Phase 2 runs the actual grep on the working tree.

## Verification

1. **Given** the refactored codebase, **When** a developer greps for `IRegisterService` (without `Read` or `Governance` suffix), **Then** the only matches are the deleted interface file (no longer exists), historical commit messages, and comments — zero active production usages remain.
2. **Given** a user-facing page that previously called `_registerService.GetRegistersAsync(...)`, **When** that page is loaded after the refactor, **Then** the page behaves identically — same data returned, same UI rendered.
3. **Given** an admin page that previously called `_registerService.ProposePolicyUpdateAsync(...)`, **When** that page submits a policy update after the refactor, **Then** the proposal is created identically — same backend call, same response handling.
4. **Given** the test class `RegisterServiceTests`, **When** the test suite runs after the refactor, **Then** all test methods that previously passed still pass — only the mock setup line changes (from `Mock<IRegisterService>()` to `Mock<IRegisterReadService>()` or `Mock<IRegisterGovernanceService>()` per test); test assertions are unchanged.
