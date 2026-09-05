# MCP P0 — Restore the Advertised Surface Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make every advertised MCP tool actually invokable over the HTTP transport, and add the in-process gates that stop it silently breaking again.

**Architecture:** Three defects, three gates. (1) Eleven tools inject a dead `IMcpSessionService` that the HTTP branch never registers — delete the dependency and add a DI-activation test. (2) `ServiceAuthClient` throws from its constructor, so a caller-token-forwarding host cannot resolve typed clients — make it fail at use and add a resolution test. (3) Ten tools call routes that were never mapped — repoint them and add a source-scanning CI gate in the style of the repo's existing `check-*.ps1` gates.

**Tech Stack:** .NET 10, xUnit v3 (MTP mode), ModelContextProtocol C# SDK, PowerShell CI gates.

**Spec:** `docs/superpowers/specs/2026-09-05-mcp-agent-experience-design.md`

## Global Constraints

- License header on every new file: `// SPDX-License-Identifier: MIT` / `// Copyright (c) 2026 Sorcha Contributors`
- File-scoped namespaces; `_camelCase` private fields; `Async` suffix on async methods.
- `dotnet test` runs in **MTP mode**. One project: `dotnet test --project x.csproj`. Filters: `--filter-class "*Name*"` (no `--`). `--collect` does not work.
- **Do NOT add `ServiceAuth__*` secrets to the `mcp-server-http` compose service.** The MCP server forwards the caller's bearer by design; service-principal credentials would grant it ambient authority the design refuses. See the spec.
- Never hard-code `<Version>` in a `.csproj`.
- Stage explicit paths when committing — never `git add -A`.

---

### Task 1: HTTP-mode DI activation gate, and delete the dead session dependency

The eleven tools declare `IMcpSessionService`, assign it, and never use it. The HTTP branch
never registers it, so activation fails. Delete the dependency; prove it with a test that
builds the HTTP-mode container and activates every tool type.

**Files:**
- Modify: `src/Apps/Sorcha.McpServer/Program.cs` (extract HTTP service registration into a testable method)
- Modify (delete field, ctor param and assignment only): `src/Apps/Sorcha.McpServer/Tools/Admin/HealthCheckTool.cs`, `Admin/PeerStatusTool.cs`, `Admin/ValidatorStatusTool.cs`, `Designer/BlueprintValidateTool.cs`, `Designer/DisclosureAnalysisTool.cs`, `Designer/JsonLogicTestTool.cs`, `Designer/SchemaGenerateTool.cs`, `Designer/SchemaValidateTool.cs`, `Participant/ActionValidateTool.cs`, `Participant/RegisterQueryTool.cs`, `Participant/WalletSignTool.cs`
- Test: `tests/Sorcha.McpServer.Tests/Infrastructure/HttpModeActivationTests.cs`

**Interfaces:**
- Consumes: nothing (first task).
- Produces: `public static void McpServerHttpRegistration.ConfigureServices(IServiceCollection services, IConfiguration configuration)` — the exact registrations the HTTP transport uses, callable from tests.

- [ ] **Step 1: Extract the HTTP registrations into a callable method**

Create `src/Apps/Sorcha.McpServer/Infrastructure/McpServerHttpRegistration.cs`:

```csharp
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Sorcha.McpServer.Infrastructure;

/// <summary>
/// The service registrations the HTTP transport uses, in one callable place so a test can
/// build the same container the server builds. Extracted because the HTTP branch silently
/// omitted <c>IMcpSessionService</c> while eleven tools still demanded it, and nothing could
/// observe that from outside <c>Program.cs</c>'s top-level statements.
/// </summary>
public static class McpServerHttpRegistration
{
    public static void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IJwtValidationHandler, JwtValidationHandler>();
        services.AddHttpContextAccessor();
        services.AddSingleton<ICallerContext, HttpCallerContext>();

        // Must mirror production exactly, including the typed service clients. A test container
        // that omitted them would activate tools against a shape the server never builds, and the
        // activation gate would pass while the deployed surface stayed dead — the precise failure
        // this task exists to end.
        RegisterServiceClients(services, configuration);
    }
}
```

Move the existing `RegisterServiceClients` helper out of `Program.cs` into this class (or make
it internal and call it) so there is one definition, not two.

Then replace the equivalent inline block in `Program.cs`'s HTTP branch with a call to it,
leaving the transport/tool registration (`AddMcpServer(...).WithHttpTransport(...).WithToolsFromAssembly()`) where it is.

- [ ] **Step 2: Write the failing test**

Create `tests/Sorcha.McpServer.Tests/Infrastructure/HttpModeActivationTests.cs`:

```csharp
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;
using Sorcha.McpServer.Infrastructure;

namespace Sorcha.McpServer.Tests.Infrastructure;

/// <summary>
/// Every advertised tool must be constructible from the container the HTTP transport builds.
/// Eleven tools once injected a stdio-only <c>IMcpSessionService</c> the HTTP branch never
/// registered, so every one of them failed to activate and the whole public surface returned
/// "An error occurred invoking 'X'." for six days. `initialize` and `tools/list` both still
/// succeeded, so no smoke check noticed.
/// </summary>
public class HttpModeActivationTests
{
    private static ServiceProvider BuildHttpModeProvider()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["JwtSettings:InstallationName"] = "test",
                ["ServiceClients:BlueprintService:Address"] = "http://localhost",
                ["ServiceClients:RegisterService:Address"] = "http://localhost",
                ["ServiceClients:WalletService:Address"] = "http://localhost",
                ["ServiceClients:TenantService:Address"] = "http://localhost",
                ["ServiceClients:ValidatorService:Address"] = "http://localhost",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        McpServerHttpRegistration.ConfigureServices(services, configuration);
        return services.BuildServiceProvider();
    }

    public static TheoryData<Type> ToolTypes()
    {
        var data = new TheoryData<Type>();
        foreach (var type in typeof(McpServerHttpRegistration).Assembly.GetTypes()
                     .Where(t => t.GetCustomAttribute<McpServerToolTypeAttribute>() is not null))
        {
            data.Add(type);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(ToolTypes))]
    public void EveryAdvertisedTool_CanBeActivated_FromTheHttpModeContainer(Type toolType)
    {
        using var provider = BuildHttpModeProvider();

        var act = () => ActivatorUtilities.CreateInstance(provider, toolType);

        act.Should().NotThrow(
            $"{toolType.Name} is advertised over the HTTP transport and must be constructible there");
    }

    [Fact]
    public void ToolTypes_AreDiscovered_SoTheTheoryIsNotVacuous()
    {
        ToolTypes().Should().HaveCountGreaterThan(20,
            "a discovery bug that found no tools would make every activation assertion pass trivially");
    }
}
```

- [ ] **Step 3: Run it and confirm it fails for the right reason**

Run: `dotnet test tests/Sorcha.McpServer.Tests/Sorcha.McpServer.Tests.csproj --filter-class "*HttpModeActivationTests*"`

Expected: FAIL. **Two distinct failure messages are expected here, and that is correct** —
because the container now mirrors production, both live defects surface at once:

1. the eleven tools with `Unable to resolve service for type 'Sorcha.McpServer.Services.IMcpSessionService'`
2. others with `ServiceAuth:ClientId not configured`, thrown by `ServiceAuthClient`'s constructor

This task clears (1). **Task 2 clears (2)**, so this test is expected to stay red between the
two tasks — do not weaken it to get green early, and do not add ServiceAuth configuration to
the test to make (2) disappear. If any *third* failure message appears, stop and read it.

- [ ] **Step 4: Delete the dead dependency from all eleven tools**

In each of the eleven files, remove exactly three things and nothing else — the field, the
constructor parameter, and the assignment. For example in `HealthCheckTool.cs`:

```csharp
// DELETE the field:
private readonly IMcpSessionService _sessionService;

// DELETE the constructor parameter:
IMcpSessionService sessionService,

// DELETE the assignment:
_sessionService = sessionService;
```

Do not remove `IMcpAuthorizationService` or `IMcpErrorHandler`. Remove the now-unused
`using Sorcha.McpServer.Services;` only if nothing else in the file needs it.

- [ ] **Step 5: Run the test and confirm it passes**

Run: `dotnet test tests/Sorcha.McpServer.Tests/Sorcha.McpServer.Tests.csproj --filter-class "*HttpModeActivationTests*"`
Expected: PASS, every tool type activated, plus the non-vacuity check.

- [ ] **Step 6: Run the whole MCP suite for regressions**

Run: `dotnet test tests/Sorcha.McpServer.Tests/Sorcha.McpServer.Tests.csproj`
Expected: no new failures versus master. Record the before/after counts in the commit message.

- [ ] **Step 7: Commit**

```bash
git add src/Apps/Sorcha.McpServer/Infrastructure/McpServerHttpRegistration.cs \
        src/Apps/Sorcha.McpServer/Program.cs \
        src/Apps/Sorcha.McpServer/Tools \
        tests/Sorcha.McpServer.Tests/Infrastructure/HttpModeActivationTests.cs
git commit -m "fix: [MCP-P0] every tool must activate in the HTTP-mode container

Eleven tools injected IMcpSessionService, which the HTTP branch never registers.
In all eleven the dependency was declared, assigned and never used, so the fix is
deletion rather than registering a stdio-shaped singleton in a stateless server.

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
```

---

### Task 2: Make `ServiceAuthClient` fail at use, not at construction

`AddServiceClients` registers `IServiceAuthClient` unconditionally and the constructor
throws when `ServiceAuth:ClientId` is absent. That makes an optional dependency mandatory
for every host, and it is why the remaining tools fail. The MCP server must **not** be given
service-principal credentials — it forwards the caller's bearer.

**Files:**
- Modify: `src/Common/Sorcha.ServiceClients.Http/Auth/ServiceAuthClient.cs:55-120`
- Test: `tests/Sorcha.McpServer.Tests/Infrastructure/CallerTokenOnlyResolutionTests.cs`

**Interfaces:**
- Consumes: `McpServerHttpRegistration.ConfigureServices` from Task 1.
- Produces: `ServiceAuthClient` that constructs without configuration and throws `InvalidOperationException` only when a token is actually requested.

- [ ] **Step 1: Write the failing test**

```csharp
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sorcha.ServiceClients.Extensions;
using Sorcha.ServiceClients.Http.Auth;

namespace Sorcha.McpServer.Tests.Infrastructure;

/// <summary>
/// A host that authorises by forwarding the caller's bearer must be able to resolve the typed
/// service clients without holding service-principal credentials of its own. Giving the MCP
/// server a ServiceAuth client id and secret would grant it ambient authority the design
/// deliberately refuses ("not by anonymous service-to-service trust").
/// </summary>
public class CallerTokenOnlyResolutionTests
{
    [Fact]
    public void ServiceAuthClient_Resolves_WithoutServiceAuthConfiguration()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["ServiceClients:TenantService:Address"] = "http://localhost" }).Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddServiceClients(configuration);

        using var provider = services.BuildServiceProvider();

        var act = () => provider.GetRequiredService<IServiceAuthClient>();

        act.Should().NotThrow(
            "a caller-token-forwarding host never acquires a service token, so construction must not demand credentials");
    }

    [Fact]
    public async Task ServiceAuthClient_Throws_OnlyWhenATokenIsActuallyRequested()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["ServiceClients:TenantService:Address"] = "http://localhost" }).Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddServiceClients(configuration);
        using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IServiceAuthClient>();

        var act = async () => await client.GetTokenAsync(CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*ServiceAuth:ClientId*",
                "the failure must still be loud and specific for hosts that genuinely need a service token");
    }
}
```

- [ ] **Step 2: Run it and confirm the first test fails**

Run: `dotnet test tests/Sorcha.McpServer.Tests/Sorcha.McpServer.Tests.csproj --filter-class "*CallerTokenOnlyResolutionTests*"`
Expected: `ServiceAuthClient_Resolves_WithoutServiceAuthConfiguration` FAILS with `ServiceAuth:ClientId not configured`. The second test may pass already — that is fine and is the point of the pair.

- [ ] **Step 3: Move the configuration reads out of the constructor**

In `ServiceAuthClient.cs`, replace the eager reads with stored configuration and a lazy
resolve used by the token-acquisition path:

```csharp
private readonly IConfiguration _configuration;
private string? _clientIdOrNull;

// in the constructor — store, do not validate:
_configuration = configuration;
_clientIdOrNull = configuration["ServiceAuth:ClientId"];

/// <summary>
/// Resolves the client id at the point a service token is actually needed. Deliberately not
/// in the constructor: hosts that authorise by forwarding the caller's bearer (the MCP server)
/// resolve this client through AddServiceClients but never acquire a service token, and a
/// throwing constructor made that impossible.
/// </summary>
private string RequireClientId() =>
    _clientIdOrNull
    ?? throw new InvalidOperationException("ServiceAuth:ClientId not configured");
```

Call `RequireClientId()` at the top of the token-acquisition method, and apply the same
treatment to the `ServiceAuth:ClientSecret` read so certificate/secret selection also happens
lazily. Do not change the exception type or message text — other code and operators match on it.

- [ ] **Step 4: Run both tests and confirm they pass**

Run: `dotnet test tests/Sorcha.McpServer.Tests/Sorcha.McpServer.Tests.csproj --filter-class "*CallerTokenOnlyResolutionTests*"`
Expected: PASS.

- [ ] **Step 5: Run every suite that touches service clients**

Run: `dotnet test tests/Sorcha.ServiceClients.Tests/Sorcha.ServiceClients.Tests.csproj` then `dotnet test tests/Sorcha.McpServer.Tests/Sorcha.McpServer.Tests.csproj`
Expected: no new failures. A host that genuinely needs a service token must still fail loudly — that is what the second test pins.

- [ ] **Step 6: Commit**

```bash
git add src/Common/Sorcha.ServiceClients.Http/Auth/ServiceAuthClient.cs \
        tests/Sorcha.McpServer.Tests/Infrastructure/CallerTokenOnlyResolutionTests.cs
git commit -m "fix: [MCP-P0] ServiceAuthClient fails at use, not at construction

A throwing constructor made an optional dependency mandatory for every host.
The MCP server forwards the caller's bearer and must not hold service-principal
credentials; it could not resolve any typed client. Failure stays loud for hosts
that do need a service token.

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
```

---

### Task 3: Route contract gate — prove the ten broken tools are broken

Mirrors the established `scripts/check-*.ps1` CI gates and the `Sorcha.Cli.ContractTests`
precedent (CLAUDE.md pattern 18): a cross-boundary contract nothing verified.

**Files:**
- Create: `scripts/check-mcp-routes.ps1`
- Create: `.mcp-routes-allowlist`
- Modify: `.github/workflows/` — add a `mcp-routes-gate` job alongside the existing gates

**Interfaces:**
- Consumes: nothing.
- Produces: a CI gate failing when an MCP tool's request path has no matching route family in `src/Services`.

- [ ] **Step 1: Write the gate script**

`scripts/check-mcp-routes.ps1` must: collect every string literal beginning `api/` or `/api/`
from `src/Apps/Sorcha.McpServer/Tools/**/*.cs` and from the typed-client methods those tools
call in `src/Common/Sorcha.ServiceClients.Http/**`; reduce each to its **route family** (the
literal segments before the first `{` or query string, e.g. `api/workflows`, `api/inbox`,
`api/registers/*/transactions`); collect every `MapGroup`/`MapGet`/`MapPost`/`MapPut`/
`MapDelete`/`MapPatch` literal from `src/Services/**/*.cs` and reduce the same way; fail with
a non-zero exit code for any tool-side family absent from the service side, listing
`file:line` and the offending path. Entries in `.mcp-routes-allowlist` (one path family per
line, `#` comments) are exempt and the file may only shrink.

Family matching, not exact matching, is deliberate: it catches the "no such route family"
class — which is all ten current failures — without false-positiving on route parameters.

- [ ] **Step 2: Run it and confirm it fails, naming the known ten**

Run: `pwsh scripts/check-mcp-routes.ps1`
Expected: non-zero exit listing at least `api/inbox`, `api/workflows`, `api/actions/{id}`,
`api/registers/{id}/data`, `api/users`, `api/tokens/revoke`, `api/blueprints/{id}/diff`.
If it reports zero, the extraction is broken — verify by temporarily adding a bogus
`api/definitely-not-a-route` literal to a tool and confirming the gate catches it, then remove it.

- [ ] **Step 3: Seed the allowlist with exactly the current failures**

Write each reported family into `.mcp-routes-allowlist` with a one-line reason, so the gate
is green on master before the fixes land and can only shrink from here.

- [ ] **Step 4: Confirm the gate is green with the allowlist**

Run: `pwsh scripts/check-mcp-routes.ps1`
Expected: exit 0.

- [ ] **Step 5: Wire it into CI**

Add a `mcp-routes-gate` job running `pwsh scripts/check-mcp-routes.ps1`, matching the shape
of the existing `derivation-contexts-gate` and `error-code-contract-gate` jobs.

- [ ] **Step 6: Commit**

```bash
git add scripts/check-mcp-routes.ps1 .mcp-routes-allowlist .github/workflows
git commit -m "test: [MCP-P0] gate MCP tool request paths against mapped service routes

Ten tools call routes that were never mapped, including the entire participant
discovery loop. Nothing verified the join. Allowlist seeded with the current
failures so it is green on master and may only shrink.

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
```

---

### Task 4: Repoint the Blueprint-service tools and shrink the allowlist

**Files:**
- Modify: `src/Common/Sorcha.ServiceClients.Http/Blueprint/BlueprintServiceClient.cs:397` (`api/workflows` → instances list), `:404` (`api/workflows/{id}` → `api/instances/{id}`), `:408` (`api/actions/{id}` → instance-scoped action), `:412` (`api/inbox` → `api/actions/pending`)
- Modify: `src/Apps/Sorcha.McpServer/Tools/Participant/ActionValidateTool.cs:140`
- Modify: `.mcp-routes-allowlist` (remove the entries fixed here)
- Test: `tests/Sorcha.McpServer.Tests/Infrastructure/` (gate covers it)

**Interfaces:**
- Consumes: the gate from Task 3.
- Produces: `GetInboxAsync`, `GetWorkflowStatusAsync`, `GetActionDetailsAsync`, `GetWorkflowInstancesAsync` targeting routes that exist.

- [ ] **Step 1: Repoint each client method to a verified route**

Confirmed present in `Sorcha.Blueprint.Service`:

| Method | Was | Becomes |
|---|---|---|
| `GetInboxAsync` | `api/inbox` | `api/actions/pending` (`ActionEndpoints.cs:28`) |
| `GetWorkflowStatusAsync` | `api/workflows/{id}` | `api/instances/{id}` (`InstanceReadEndpoints.cs:50`) |
| `GetWorkflowInstancesAsync` | `api/workflows` | `api/instances/` (`InstanceReadEndpoints.cs:38`) |
| `GetActionDetailsAsync` | `api/actions/{id}` | `api/instances/{instanceId}/actions/{actionId}` (`InstanceActionEndpoints.cs:36`) |

`GetActionDetailsAsync` needs an `instanceId` it does not currently take. Change its signature
to `GetActionDetailsAsync(string instanceId, string actionId, CancellationToken ct)` and update
`ActionDetailsTool` to accept both, updating its `[Description]` text and input schema to match.

- [ ] **Step 2: Point `sorcha_action_validate` at the endpoint that exists**

`ActionValidateTool.cs:140` targets `/api/actions/{actionInstanceId}/validate`, which is not
mapped. The real endpoint is `POST /api/execution/validate` (`Blueprint.Service/Program.cs:1962`),
which takes `{ blueprintId, actionId, data }`. Change the tool's inputs to
`blueprintId`, `actionId`, `dataJson` and call that route.

Add to the tool's `[Description]`: that it validates against the blueprint's **latest**
definition, not an instance's pinned one — that divergence is tracked as #1606 and must not be
silently implied away.

- [ ] **Step 3: Run the gate and confirm those families are gone**

Run: `pwsh scripts/check-mcp-routes.ps1` after deleting the corresponding allowlist lines.
Expected: exit 0 with a smaller allowlist.

- [ ] **Step 4: Run the suites**

Run: `dotnet test tests/Sorcha.McpServer.Tests/Sorcha.McpServer.Tests.csproj` and `dotnet test tests/Sorcha.ServiceClients.Tests/Sorcha.ServiceClients.Tests.csproj`
Expected: no new failures.

- [ ] **Step 5: Commit**

```bash
git add src/Common/Sorcha.ServiceClients.Http/Blueprint/BlueprintServiceClient.cs \
        src/Apps/Sorcha.McpServer/Tools/Participant \
        .mcp-routes-allowlist
git commit -m "fix: [MCP-P0] repoint the participant discovery loop at routes that exist

inbox_list, workflow_status, workflow_instances, action_details and action_validate
all targeted unmapped routes, so an agent could submit but never discover or inspect.

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
```

---

### Task 5: Repoint the Register and Tenant tools, and withdraw `blueprint_diff`

**Files:**
- Modify: `src/Apps/Sorcha.McpServer/Tools/Participant/RegisterQueryTool.cs:135`
- Modify: `src/Common/Sorcha.ServiceClients.Http/Tenant/TenantServiceClient.cs:65,72,76`
- Modify: `src/Apps/Sorcha.McpServer/Tools/Admin/UserListTool.cs`, `Admin/UserManageTool.cs` (add the org id these routes require)
- Modify: `src/Apps/Sorcha.McpServer/Tools/Designer/BlueprintDiffTool.cs` (withdraw from the surface)
- Modify: `.mcp-routes-allowlist`

**Interfaces:**
- Consumes: the gate from Task 3.
- Produces: `ListUsersAsync(string organizationId, ...)`, `ManageUserAsync(string organizationId, string userId, string action, ...)`, `RevokeTokenAsync` targeting `/api/auth/token/revoke-user`.

- [ ] **Step 1: Repoint each to a verified route**

| Tool / method | Was | Becomes |
|---|---|---|
| `sorcha_register_query` | `api/registers/{id}/data` | `api/registers/{id}/transactions` (`Register.Service/Program.cs:1181`) |
| `ListUsersAsync` | `api/users` | `api/organizations/{organizationId}/users` (`OrganizationEndpoints.cs:189`) |
| `ManageUserAsync` | `api/users/{id}/actions` | `api/organizations/{orgId}/users/{userId}/suspend`\|`reactivate`\|`unlock`\|`role` (`OrganizationEndpoints.cs:235-300`) |
| `RevokeTokenAsync` | `api/tokens/revoke` | `api/auth/token/revoke-user` and `/revoke-organization` (`AuthEndpoints.cs:155,166`) |

`ListUsersAsync` and `ManageUserAsync` need an organisation id in the **path**; their tools
currently take none. Add a required `organizationId` parameter to both tool signatures and
their `[Description]` text.

- [ ] **Step 2: Withdraw `sorcha_blueprint_diff` from the advertised surface**

No `/diff` endpoint exists anywhere in `Sorcha.Blueprint.Service`, so there is nothing to
repoint at. Follow the precedent set by `WalletSignTool`: leave the class and its
`[McpServerTool]` attribute in place but remove `[McpServerToolType]` from the class so the
assembly scan does not register it, with a comment stating why and linking the follow-up issue.

An advertised tool that cannot work is worse than an absent one, because an agent plans around it.

- [ ] **Step 3: Remove the corresponding allowlist entries and run the gate**

Run: `pwsh scripts/check-mcp-routes.ps1`

Expected: exit 0 with **exactly one** entry left in `.mcp-routes-allowlist`:
`api/blueprints/*/diff`, with a comment recording that the tool is withdrawn and the
endpoint does not exist. It cannot be removed here: the URL literal lives in
`BlueprintServiceClient.GetBlueprintDiffAsync`, not in the tool, and `BlueprintDiffToolTests`
mocks that method — deleting it would break existing tests and is scope growth inside P0.
Open a follow-up issue for removing the dead client method and its mock-only tests, and
reference it in the allowlist comment.

- [ ] **Step 4: Run the suites**

Run: `dotnet test tests/Sorcha.McpServer.Tests/Sorcha.McpServer.Tests.csproj`
Expected: no new failures. The activation theory from Task 1 must still cover one fewer tool type — confirm the non-vacuity assertion still holds.

- [ ] **Step 5: Commit**

```bash
git add src/Apps/Sorcha.McpServer/Tools src/Common/Sorcha.ServiceClients.Http/Tenant/TenantServiceClient.cs .mcp-routes-allowlist
git commit -m "fix: [MCP-P0] repoint register/tenant tools; withdraw blueprint_diff

blueprint_diff targets a /diff endpoint that exists nowhere, so it is unregistered
rather than left advertised and broken. Allowlist is now empty.

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
```

---

### Task 6: Fix the role model so a citizen is a participant

**Files:**
- Create: `src/Apps/Sorcha.McpServer/Services/McpRoleNormalizer.cs`
- Modify: `src/Apps/Sorcha.McpServer/Infrastructure/HttpCallerContext.cs:157-162`, `Services/McpSessionService.cs:196-201` (both delegate to the new normaliser)
- Modify: the tools whose denial message names `sorcha:participant`
- Test: `tests/Sorcha.McpServer.Tests/Services/McpRoleNormalizerTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `public static string McpRoleNormalizer.Normalize(string platformRole)`.

- [ ] **Step 1: Add the test-only project reference the reflective assertion needs**

`UserRole` lives in `src/Services/Sorcha.Tenant.Service/Models/UserIdentity.cs:82` — a service
assembly. `Sorcha.McpServer.Tests` currently references only `Sorcha.McpServer`, so the
reflective test below will not compile without:

```xml
<!-- Test-only: lets the role-coverage assertion reflect over the REAL platform role enum
     instead of a hand-copied list that silently rots. Same layering exception the
     Sorcha.Cli.ContractTests project relies on (CLAUDE.md pattern 18) - legitimate in a test
     project, never in a production assembly. -->
<ProjectReference Include="..\..\src\Services\Sorcha.Tenant.Service\Sorcha.Tenant.Service.csproj" />
```

If that reference proves problematic, pin the five names as `[InlineData]` literals instead —
but then the coverage assertion is no longer drift-proof, so say so in a comment rather than
letting a weaker test look equivalent.

- [ ] **Step 2: Write the failing test**

```csharp
[Theory]
[InlineData("Consumer", "sorcha:participant")]
[InlineData("Auditor", "sorcha:auditor")]
[InlineData("Administrator", "sorcha:admin")]
[InlineData("SystemAdmin", "sorcha:admin")]
[InlineData("Designer", "sorcha:designer")]
public void Normalize_MapsEveryPlatformRole(string platformRole, string expected) =>
    McpRoleNormalizer.Normalize(platformRole).Should().Be(expected);

[Fact]
public void Normalize_CoversEveryValueOfTheRealPlatformRoleEnum() =>
    Enum.GetNames<Sorcha.Tenant.Models.UserRole>()
        .Should().OnlyContain(r => McpRoleNormalizer.Normalize(r).StartsWith("sorcha:"),
            "a platform role that normalises to itself can never satisfy a RequiredRole check");
```

- [ ] **Step 3: Run it and confirm `Consumer` and `Auditor` fail**

Run: `dotnet test tests/Sorcha.McpServer.Tests/Sorcha.McpServer.Tests.csproj --filter-class "*McpRoleNormalizerTests*"`
Expected: FAIL — `Consumer` returns `"Consumer"`, `Auditor` returns `"Auditor"`.

- [ ] **Step 4: Implement the normaliser as the single home for the rule**

One `switch` covering all five platform roles, adding `consumer` → `sorcha:participant` and
`auditor` → `sorcha:auditor`. Delete both duplicated copies and delegate to it.

- [ ] **Step 5: Remove the vestigial denial message**

Tools returning "Access denied. This tool requires the `sorcha:participant` role" name a check
the entitlement table no longer performs (it is tier-gated). Replace with text describing the
actual requirement, so an agent reading the denial is not sent after the wrong thing.

- [ ] **Step 6: Run and confirm green**

Run: `dotnet test tests/Sorcha.McpServer.Tests/Sorcha.McpServer.Tests.csproj`
Expected: PASS, no new failures.

- [ ] **Step 7: Commit**

```bash
git add src/Apps/Sorcha.McpServer tests/Sorcha.McpServer.Tests/Services/McpRoleNormalizerTests.cs
git commit -m "fix: [MCP-P0] Consumer and Auditor now normalise to MCP roles

The normaliser recognised participant|user|member; the platform never emits any of
them - a citizen's role is Consumer. Rule now has one home instead of two copies.

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
```

---

### Task 7: Live verification on n1, then documentation

A green suite is not proof: the whole point of P0 is that the suite was green while the
surface was dead. This task is not complete without a live call.

**Files:**
- Modify: `tests/Sorcha.McpServer.Tests/Integration/HttpTransportIntegrationTests.cs` (add a tool-invocation test)
- Modify: `.specify/MASTER-TASKS.md`, `src/Apps/Sorcha.McpServer/README.md`, `docs/reference/API-DOCUMENTATION.md`

- [ ] **Step 1: Extend the live test to invoke a tool, not just initialize**

Add a Docker-gated test that calls `tools/call` for `sorcha_health_check` and asserts
`isError` is absent or false. Keep the skip behaviour, but the skip must name what was
skipped so a CI run cannot look like a pass.

- [ ] **Step 2: Deploy to n1 and verify by hand**

Follow the `n1-deploy` skill. Then, with a platform bearer:

```bash
curl -s -X POST https://n1.sorcha.dev/mcp \
  -H "Authorization: Bearer $TOK" -H 'Content-Type: application/json' \
  -H 'Accept: application/json, text/event-stream' \
  -d '{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"sorcha_health_check","arguments":{}}}'
```

Expected: a real result, not `"An error occurred invoking 'sorcha_health_check'."`
Then repeat for one designer tool and one participant tool, so all three categories are proven.

- [ ] **Step 3: Check the container logs are clean**

Run: `ssh sorcha@n1.sorcha.dev 'docker logs --tail 100 sorcha-mcp-server-http 2>&1 | grep -iE "error|exception"'`
Expected: no `Unable to resolve service` and no `ServiceAuth:ClientId not configured`.

- [ ] **Step 4: Update documentation**

`.specify/MASTER-TASKS.md` with the outcome and the live evidence; the MCP README with the
caller-token-forwarding rule (and that the server must never hold ServiceAuth credentials);
`docs/reference/API-DOCUMENTATION.md` for the changed tool signatures from Tasks 4-5.

- [ ] **Step 5: Commit and open the PR**

```bash
git add tests/Sorcha.McpServer.Tests/Integration/HttpTransportIntegrationTests.cs \
        .specify/MASTER-TASKS.md src/Apps/Sorcha.McpServer/README.md docs/reference/API-DOCUMENTATION.md
git commit -m "docs: [MCP-P0] live verification on n1 and documentation sync

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
gh pr create --fill
```
