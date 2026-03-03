# PR Review Fixes: Crypto Correctness, UI Bugs, Code Quality

**Date:** 2026-02-26
**Scope:** Address outstanding issues from PR code reviews #127–#132

---

## 1. Crypto Correctness

### C5 — Hybrid Verification Mode (CryptoModule.cs:886)

**Problem:** `HybridVerifyAsync` uses OR logic — either classical or PQC signature passing is sufficient. An attacker who compromises one key type alone can forge a valid hybrid signature.

**Fix:** Add configurable `HybridVerificationMode` enum:

```csharp
public enum HybridVerificationMode
{
    /// <summary>Both classical AND PQC must verify.</summary>
    Strict,
    /// <summary>Either classical OR PQC verifying is sufficient (migration mode).</summary>
    Permissive
}
```

- Add `mode` parameter to `ICryptoModule.HybridVerifyAsync` defaulting to `Strict`
- Line 886: `mode == Strict ? (classicalValid && pqcValid) : (classicalValid || pqcValid)`
- Update `ICryptoModule` interface to include the parameter
- Add tests for both modes

### C6 — SSRF Protection in did:web Resolver (WebDidResolver.cs)

**Problem:** `BuildUrl()` constructs URLs from DID strings with no IP validation. `did:web:169.254.169.254` would hit cloud metadata endpoints.

**Fix:** After URI construction, resolve the hostname and reject private/reserved IPs:

- Add static `IsPrivateOrReservedAddress(IPAddress)` helper checking: loopback (127.0.0.0/8), link-local (169.254.0.0/16), private (10/8, 172.16/12, 192.168/16), IPv6 equivalents
- Add `IConfiguration` injection with `AllowPrivateAddresses` setting (default `false`)
- In Development/Docker, set `AllowPrivateAddresses: true` to allow local services
- Log warning when a private address is blocked

### M5 — Hard-coded Algorithm Names in Hybrid Sign (WalletEndpoints.cs:455-457)

**Problem:** `ClassicalAlgorithm = "ED25519"` and `PqcAlgorithm = "ML-DSA-65"` hardcoded regardless of wallet's actual algorithm.

**Fix:** Look up both wallet entities before signing:

```csharp
var classicalWallet = await walletManager.GetWalletAsync(address, cancellationToken);
var pqcWallet = await walletManager.GetWalletAsync(request.PqcWalletAddress, cancellationToken);
// ...
ClassicalAlgorithm = classicalWallet.Algorithm,
PqcAlgorithm = pqcWallet.Algorithm,
```

### M6 / M14 — Spec File Inconsistency

**Problem:** Older spec files default to ML-DSA-44; code and 040 spec use ML-DSA-65.

**Fix:** Update:
- `.specify/specs/features/cryptography/spec.md` — default ML-DSA-44 → ML-DSA-65
- `.specify/specs/features/transaction-handler/spec.md` — same
- `specs/040-quantum-safe-crypto/contracts/crypto-policy-api.md` — remove `sharedSecret` from encapsulate response

---

## 2. UI Bugs

### C10 — BlueprintId Mapped from BlueprintName (MyActions.razor:247)

**Problem:** `BlueprintId = action.BlueprintName` sends display name instead of the published blueprint TxId. Action submissions will fail.

**Fix:**
1. **Backend** (`Program.cs:1720-1728`): Include `blueprintId = instance.BlueprintId` and `registerId = instance.RegisterId` in the `next-actions` response object
2. **Model** (`PendingActionViewModel`): Add `BlueprintId` and `RegisterId` properties
3. **Razor** (`MyActions.razor:247,251`): Use `action.BlueprintId` and `action.RegisterId`

Note: `BlueprintId` is the TxId of the published blueprint Control transaction on the register. This is the version-specific identifier — v2 publishes get a new TxId, with PrevTxId linking to v1.

### M11 — RegisterAddress Hardcoded to Empty String (MyActions.razor:251)

**Problem:** `RegisterAddress = string.Empty` where register address is required.

**Fix:** Covered by C10 — the `RegisterId` flows from the Instance model through the `next-actions` endpoint.

---

## 3. Code Quality

### M1 — Inconsistent TokenClaimConstants Usage

**Problem:** 21+ hardcoded `"token_type"` / `"org_id"` strings across 5 `AuthenticationExtensions.cs` files and `TokenService.cs`. `TokenClaimConstants` class exists but is not consistently used.

**Fix:** Replace all hardcoded strings with `TokenClaimConstants.*` references. Add missing constant for `"service"` value if needed. Files:
- `Register.Service/Extensions/AuthenticationExtensions.cs` (5 occurrences)
- `Tenant.Service/Extensions/AuthenticationExtensions.cs` (4)
- `Blueprint.Service/Extensions/AuthenticationExtensions.cs` (4)
- `Peer.Service/Extensions/AuthenticationExtensions.cs` (4)
- `Wallet.Service/Extensions/AuthenticationExtensions.cs` (4)
- `Tenant.Service/Services/TokenService.cs` (7)

### M2 — Security Events at Debug Level (PeerAuthInterceptor.cs:129,134)

**Fix:** Change `LogDebug` → `LogWarning` for token expiry and validation failure.

### M3 — String-based Exception Matching (DelegationEndpoints.cs:108,112,184)

**Fix:** Create typed exceptions:
- `WalletNotFoundException` — thrown from `DelegationService` when wallet/access not found
- `WalletAccessAlreadyExistsException` — thrown when access grant already exists

Catch these explicitly in `DelegationEndpoints` instead of `ex.Message.Contains(...)`.

### M4 — Mutable TokenIntrospectionResult (ITokenIntrospectionClient.cs:31-62)

**Fix:** Change all `{ get; set; }` to `{ get; init; }`.

### M9 — Silent HTTP Error Swallowing (CredentialApiService.cs)

**Fix:** Inject `ILogger<CredentialApiService>` and add `_logger.LogWarning(ex, ...)` in each of the 7 catch blocks. No change to return values — this is observability.

### M13 — Missing Audit Logging on Admin Endpoints (AdminEndpoints.cs)

**Fix:** Add `HttpContext` parameter to Start, Stop, and Process handlers. Extract `sub` and `org_id` claims. Log structured audit entries:

```csharp
var userId = context.User.FindFirst("sub")?.Value ?? "unknown";
logger.LogInformation("Admin action {Action} on register {RegisterId} by {UserId}",
    "StartValidator", request.RegisterId, userId);
```

---

## Files Modified

| File | Changes |
|------|---------|
| `Sorcha.Cryptography/Models/HybridVerificationMode.cs` | **New** — enum |
| `Sorcha.Cryptography/Interfaces/ICryptoModule.cs` | Add mode param to HybridVerifyAsync |
| `Sorcha.Cryptography/Core/CryptoModule.cs` | Implement configurable AND/OR logic |
| `Sorcha.ServiceClients/Did/WebDidResolver.cs` | Add SSRF protection |
| `Sorcha.Wallet.Service/Endpoints/WalletEndpoints.cs` | Fix hardcoded algorithm names |
| `Sorcha.Wallet.Service/Endpoints/DelegationEndpoints.cs` | Use typed exceptions |
| `Sorcha.Wallet.Service/Exceptions/` | **New** — typed domain exceptions |
| `Sorcha.Wallet.Service/Services/DelegationService.cs` | Throw typed exceptions |
| `Sorcha.Blueprint.Service/Program.cs` | Add BlueprintId/RegisterId to next-actions response |
| `Sorcha.UI.Core/Models/Workflows/WorkflowInstanceViewModel.cs` | Add BlueprintId/RegisterId to PendingActionViewModel |
| `Sorcha.UI.Web.Client/Pages/MyActions.razor` | Fix field mappings |
| `Sorcha.ServiceClients/Auth/TokenClaimConstants.cs` | Add ServiceTokenType constant |
| `5x AuthenticationExtensions.cs` | Replace hardcoded strings |
| `Sorcha.Tenant.Service/Services/TokenService.cs` | Replace hardcoded strings |
| `Sorcha.Peer.Service/GrpcServices/PeerAuthInterceptor.cs` | LogDebug → LogWarning |
| `Sorcha.ServiceClients/Auth/ITokenIntrospectionClient.cs` | set → init |
| `Sorcha.UI.Core/Services/Credentials/CredentialApiService.cs` | Add logging |
| `Sorcha.Validator.Service/Endpoints/AdminEndpoints.cs` | Add audit logging |
| `.specify/specs/features/cryptography/spec.md` | Fix default algorithm |
| `.specify/specs/features/transaction-handler/spec.md` | Fix default algorithm |
| `specs/040-quantum-safe-crypto/contracts/crypto-policy-api.md` | Remove sharedSecret |

## Testing Strategy

- **C5:** Unit tests for both Strict and Permissive modes in `Sorcha.Cryptography.Tests`
- **C6:** Unit tests for private IP rejection + development bypass in `Sorcha.ServiceClients.Tests`
- **M5:** Existing wallet signing tests should pass; add test confirming algorithm detection
- **C10/M11:** Existing UI tests; manual verification via walkthrough
- **M3:** Update `DelegationEndpointTests` for typed exception handling
- **M1-M4, M9, M13:** Compile-time verification; existing tests confirm no regressions
