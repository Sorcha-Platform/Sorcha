# SEC-011: S2S Auth Gap Closure — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Secure 5 open internal endpoints with `RequireService` policy and ensure all calling service clients attach JWT auth headers.

**Architecture:** Apply the existing `RequireService` authorization policy (checks `token_type=service` claim) to the 5 unprotected internal endpoints. Add `SetAuthHeaderAsync()` calls to the 4 service client methods that currently skip auth. No new infrastructure — all components exist.

**Tech Stack:** .NET 10 Minimal APIs, JWT Bearer auth, xUnit + FluentAssertions integration tests

---

### File Map

| Action | File | Responsibility |
|--------|------|---------------|
| Modify | `src/Services/Sorcha.Register.Service/Program.cs` | Add RequireService to 3 internal endpoints |
| Modify | `src/Services/Sorcha.Peer.Service/Program.cs` | Add RequireService to 2 subscription endpoints |
| Modify | `src/Common/Sorcha.ServiceClients/Register/RegisterServiceClient.cs` | Add SetAuthHeaderAsync to 2 methods |
| Modify | `src/Common/Sorcha.ServiceClients/Peer/PeerServiceClient.cs` | Add IServiceAuthClient + SetAuthHeaderAsync to 2 methods |
| Modify | `src/Common/Sorcha.ServiceClients/Extensions/ServiceCollectionExtensions.cs` | Wire IServiceAuthClient into PeerServiceClient DI |
| Modify | `tests/Sorcha.Register.Service.IntegrationTests/RegisterEndpointsTests.cs` | Add auth tests for 3 internal endpoints |
| Modify | `.specify/MASTER-TASKS.md` | Mark SEC-011 complete, add SEC-011b |

---

### Task 1: Secure Register Service Internal Endpoints

**Files:**
- Modify: `src/Services/Sorcha.Register.Service/Program.cs:249-260, 401-407, 455-460`

- [ ] **Step 1: Change GET /api/internal/registers auth**

Replace lines 257-258:
```csharp
// Before:
.AllowAnonymous()
.ExcludeFromDescription();

// After:
.RequireAuthorization("RequireService")
.ExcludeFromDescription();
```
Also update the `.WithDescription()` on line 255 — remove "Unauthenticated endpoint" wording:
```csharp
.WithDescription("Internal endpoint for Blueprint Service startup recovery. Returns minimal register info. Requires service token.")
```

- [ ] **Step 2: Change POST /api/internal/register-subscriptions auth**

Replace lines 405-407:
```csharp
// Before:
.AllowAnonymous()
.ExcludeFromDescription();

// After:
.RequireAuthorization("RequireService")
.ExcludeFromDescription();
```

- [ ] **Step 3: Change POST /api/internal/register-sync-status auth**

Replace lines 459-460:
```csharp
// Before:
.AllowAnonymous() // TODO(SEC-011): add S2S auth; currently open within Docker network
.ExcludeFromDescription();

// After:
.RequireAuthorization("RequireService")
.ExcludeFromDescription();
```

- [ ] **Step 4: Build to verify no compilation errors**

Run: `dotnet build src/Services/Sorcha.Register.Service/`
Expected: Build succeeded.

- [ ] **Step 5: Commit**

```bash
git add src/Services/Sorcha.Register.Service/Program.cs
git commit -m "feat(SEC-011): secure Register Service internal endpoints with RequireService"
```

---

### Task 2: Secure Peer Service Subscription Endpoints

**Files:**
- Modify: `src/Services/Sorcha.Peer.Service/Program.cs:656-659, 690-693`

- [ ] **Step 1: Add RequireService to POST subscribe**

Replace line 659:
```csharp
// Before:
    .WithTags("Registers"); // TODO(SEC-011): add S2S auth; currently open within Docker network

// After:
    .WithTags("Registers")
    .RequireAuthorization("RequireService");
```

- [ ] **Step 2: Add RequireService to DELETE subscribe**

Replace line 693:
```csharp
// Before:
    .WithTags("Registers"); // TODO(SEC-011): add S2S auth; currently open within Docker network

// After:
    .WithTags("Registers")
    .RequireAuthorization("RequireService");
```

- [ ] **Step 3: Build to verify**

Run: `dotnet build src/Services/Sorcha.Peer.Service/`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add src/Services/Sorcha.Peer.Service/Program.cs
git commit -m "feat(SEC-011): secure Peer Service subscribe endpoints with RequireService"
```

---

### Task 3: Add Auth Headers to RegisterServiceClient Internal Methods

**Files:**
- Modify: `src/Common/Sorcha.ServiceClients/Register/RegisterServiceClient.cs:1416-1468`

The `SetAuthHeaderAsync()` instance method already exists at line 55. Two methods skip it with comments saying "Internal endpoint is AllowAnonymous — no auth header". Fix both.

- [ ] **Step 1: Add auth to GetInternalRegistersAsync**

At line 1421-1424, replace:
```csharp
        _logger.LogDebug("Fetching internal register list for recovery");

        // Internal endpoint is AllowAnonymous — no auth header
        var response = await _httpClient.GetAsync(
```
With:
```csharp
        _logger.LogDebug("Fetching internal register list for recovery");

        await SetAuthHeaderAsync(cancellationToken);

        var response = await _httpClient.GetAsync(
```

- [ ] **Step 2: Add auth to NotifySubscriptionAsync**

At line 1461-1464, replace:
```csharp
        _logger.LogDebug(
            "Notifying Register Service of subscription {Action} for register {RegisterId}",
            request.Action, request.RegisterId);

        // Internal endpoint is AllowAnonymous — no auth header
        var response = await _httpClient.PostAsJsonAsync(
```
With:
```csharp
        _logger.LogDebug(
            "Notifying Register Service of subscription {Action} for register {RegisterId}",
            request.Action, request.RegisterId);

        await SetAuthHeaderAsync(cancellationToken);

        var response = await _httpClient.PostAsJsonAsync(
```

- [ ] **Step 3: Build to verify**

Run: `dotnet build src/Common/Sorcha.ServiceClients/`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add src/Common/Sorcha.ServiceClients/Register/RegisterServiceClient.cs
git commit -m "feat(SEC-011): add auth headers to RegisterServiceClient internal methods"
```

---

### Task 4: Add Auth Headers to PeerServiceClient

**Files:**
- Modify: `src/Common/Sorcha.ServiceClients/Peer/PeerServiceClient.cs:16-65, 380-479`
- Modify: `src/Common/Sorcha.ServiceClients/Extensions/ServiceCollectionExtensions.cs`

PeerServiceClient currently has no `IServiceAuthClient` dependency at all. Add it and wire auth into the two HTTP methods.

- [ ] **Step 1: Add IServiceAuthClient to PeerServiceClient constructor**

Add import at top of file:
```csharp
using Sorcha.ServiceClients.Auth;
using Sorcha.ServiceClients.Helpers;
```

Add field after line 26 (`private bool _peerServiceUnavailableLogged;`):
```csharp
    private readonly IServiceAuthClient? _serviceAuth;
```

Update constructor signature at line 28-31 to accept optional auth client:
```csharp
    public PeerServiceClient(
        IConfiguration configuration,
        ILogger<PeerServiceClient> logger,
        HttpClient? httpClient = null,
        IServiceAuthClient? serviceAuth = null)
    {
```

Assign after line 33:
```csharp
        _serviceAuth = serviceAuth;
```

Add private helper method after the constructor closing brace:
```csharp
    private async Task SetAuthHeaderAsync(CancellationToken cancellationToken)
    {
        if (_httpClient is null || _serviceAuth is null) return;
        await ServiceClientAuthHelper.SetAuthHeaderAsync(
            _httpClient, _serviceAuth, _logger, "Peer Service", cancellationToken);
    }
```

- [ ] **Step 2: Add auth to SubscribeToRegisterAsync**

At line 397, before the `PostAsJsonAsync` call, add:
```csharp
        await SetAuthHeaderAsync(cancellationToken);

        var response = await _httpClient.PostAsJsonAsync(
```

- [ ] **Step 3: Add auth to UnsubscribeFromRegisterAsync**

At line 448, before the `DeleteAsync` call, add:
```csharp
        await SetAuthHeaderAsync(cancellationToken);

        var response = await _httpClient.DeleteAsync(
```

- [ ] **Step 4: Wire IServiceAuthClient in DI registration**

In `src/Common/Sorcha.ServiceClients/Extensions/ServiceCollectionExtensions.cs`, find where PeerServiceClient is registered (look for `AddScoped<IPeerServiceClient`). Update to pass `IServiceAuthClient` through. Since PeerServiceClient uses constructor injection with optional parameters, and `IServiceAuthClient` is already registered as a singleton by `AddServiceClients`, no DI changes are needed — the container will resolve it automatically.

Verify by checking the constructor: all parameters are resolvable (IConfiguration, ILogger, HttpClient via factory, IServiceAuthClient as singleton).

- [ ] **Step 5: Build to verify**

Run: `dotnet build src/Common/Sorcha.ServiceClients/`
Expected: Build succeeded.

- [ ] **Step 6: Commit**

```bash
git add src/Common/Sorcha.ServiceClients/Peer/PeerServiceClient.cs
git commit -m "feat(SEC-011): add auth headers to PeerServiceClient subscribe methods"
```

---

### Task 5: Integration Tests for Register Service Internal Endpoints

**Files:**
- Modify: `tests/Sorcha.Register.Service.IntegrationTests/RegisterEndpointsTests.cs`

The test factory already provides `CreateUnauthenticatedClient()` and `CreateServiceClient()` (with `token_type=service`). Add tests verifying internal endpoints reject unauthenticated requests.

- [ ] **Step 1: Add test for GET /api/internal/registers requires service auth**

```csharp
[Fact]
public async Task InternalGetRegisters_WithoutAuth_ReturnsUnauthorized()
{
    // Arrange
    using var client = _factory.CreateUnauthenticatedClient();

    // Act
    var response = await client.GetAsync("/api/internal/registers");

    // Assert
    response.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);
}

[Fact]
public async Task InternalGetRegisters_WithServiceToken_ReturnsOk()
{
    // Arrange
    using var client = _factory.CreateServiceClient();

    // Act
    var response = await client.GetAsync("/api/internal/registers");

    // Assert
    response.StatusCode.Should().Be(HttpStatusCode.OK);
}
```

- [ ] **Step 2: Add test for POST /api/internal/register-subscriptions requires service auth**

```csharp
[Fact]
public async Task InternalRegisterSubscriptions_WithoutAuth_ReturnsUnauthorized()
{
    // Arrange
    using var client = _factory.CreateUnauthenticatedClient();
    var content = JsonContent.Create(new { registerId = "abcdef0123456789abcdef0123456789", action = "subscribe" });

    // Act
    var response = await client.PostAsync("/api/internal/register-subscriptions", content);

    // Assert
    response.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);
}
```

- [ ] **Step 3: Add test for POST /api/internal/register-sync-status requires service auth**

```csharp
[Fact]
public async Task InternalSyncStatus_WithoutAuth_ReturnsUnauthorized()
{
    // Arrange
    using var client = _factory.CreateUnauthenticatedClient();
    var content = JsonContent.Create(new { registerId = "abcdef0123456789abcdef0123456789", syncState = "Active", peerConnectionActive = true });

    // Act
    var response = await client.PostAsync("/api/internal/register-sync-status", content);

    // Assert
    response.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);
}
```

- [ ] **Step 4: Run integration tests**

Run: `dotnet test tests/Sorcha.Register.Service.IntegrationTests/ --filter "Internal" -v q`
Expected: All new tests pass.

- [ ] **Step 5: Commit**

```bash
git add tests/Sorcha.Register.Service.IntegrationTests/RegisterEndpointsTests.cs
git commit -m "test(SEC-011): add auth enforcement tests for internal endpoints"
```

---

### Task 6: Update MASTER-TASKS and Documentation

**Files:**
- Modify: `.specify/MASTER-TASKS.md`

- [ ] **Step 1: Mark SEC-011 complete and add SEC-011b**

In `.specify/MASTER-TASKS.md`, update SEC-011:
```markdown
| SEC-011 | Service-to-service authentication for internal endpoints | P1 | 8h | ✅ | Closed: RequireService policy on all 5 internal endpoints. Service clients attach JWT headers. |
| SEC-011b | Defence-in-depth: per-service identity policies | P2 | 8h | 📋 | Check `service_name` claim per endpoint, scope enforcement, API Gateway internal route blocking, audit logging. Deferred from SEC-011. |
```

- [ ] **Step 2: Run full test suite**

Run: `dotnet test`
Expected: All tests pass (no regressions from auth changes).

- [ ] **Step 3: Final commit**

```bash
git add .specify/MASTER-TASKS.md
git commit -m "docs(SEC-011): mark complete, add SEC-011b for defence-in-depth"
```
