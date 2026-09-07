# MCP P1 — Completable Surface Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give an external agent everything it needs to go from nothing to a running, auditable Sorcha workflow — create a register, publish a blueprint onto it, start an instance — with a real human approving the consequential steps.

**Architecture:** Three new lifecycle tools close the loop that `sorcha_action_submit` already assumes. Human sign-off lands on the MCP client via elicitation, behind an `IHumanApproval` seam (the SDK's `ElicitAsync` is non-virtual and cannot be mocked, so tools must not call it directly). Resources and prompts add the nouns and recipes to a surface that is currently tools-only. A response-shape CI gate closes the hole that let `sorcha_tenant_list` report 0 of 27 tenants while the route gate stayed green.

**Tech Stack:** .NET 10, C# 14, xUnit v3 (MTP mode), FluentAssertions 8.x, Moq 4.20.x, ModelContextProtocol C# SDK **2.2.0**, PowerShell CI gates.

**Spec:** `docs/superpowers/specs/2026-09-06-mcp-p1-completable-surface-design.md`

## Global Constraints

- License header on every new file:
  `// SPDX-License-Identifier: MIT` / `// Copyright (c) 2026 Sorcha Contributors`
- File-scoped namespaces; `_camelCase` private fields; `Async` suffix on async methods.
- Import order: System, Microsoft, third-party, Sorcha.
- `dotnet test` runs in **MTP mode**. One project: `dotnet test --project x.csproj`. Filters: `--filter-class "*Name*"` (no `--`). `--collect` does not work.
- **Never give the MCP server `ServiceAuth__*` credentials.** It forwards the caller's bearer. A credential failure inside a tool is a bug in `ServiceClientAuthHelper`, not a missing secret.
- **Never add a line to `.mcp-routes-allowlist`** to silence the route gate. Repoint the tool or map the route.
- Derivation contexts come from `Sorcha.Wallet.Contracts.Constants.SorchaDerivationPaths` — never a string literal (CLAUDE.md pattern 15; enforced by `scripts/check-derivation-contexts.ps1`).
- Service addresses resolve through `SorchaServiceAddresses` — never a raw config key (pattern 17).
- Never hard-code `<Version>` in a `.csproj` (pattern 14).
- Stage explicit paths when committing — never `git add -A`.
- Every public member needs `/// <summary>`; every endpoint-facing tool needs `[Description]`.

## SDK facts established by reflection against 2.2.0 (do not re-derive)

- **`IMcpServer` does not exist.** The type is `ModelContextProtocol.Server.McpServer`, an **abstract class**.
- `McpServer.ClientCapabilities` is **virtual** (mockable). `McpServer.ElicitAsync` is **NOT virtual** (not mockable) — this is why Task 1 exists.
- `RequestContext<T>.Server` returns the `McpServer`. `RequestContext<T>` is sealed.
- `ElicitResult` has `Action` (string), `IsAccepted` (bool), `Content`.
- `ClientCapabilities.Elicitation` is an `ElicitationCapability?` — null means the client did not declare it.
- `ElicitRequestParams { Message, RequestedSchema }`; `ElicitRequestParams.RequestSchema { Type, Properties, Required }`; `ElicitRequestParams.BooleanSchema { Type, Default, Title, Description }`.
- Annotations are attribute properties: `[McpServerTool(Name=…, Destructive=…, ReadOnly=…, Idempotent=…)]`.
- Resources: `[McpServerResourceType]` on the class, `[McpServerResource(UriTemplate=…, Name=…, MimeType=…)]` on methods.
- Prompts: `[McpServerPromptType]` on the class, `[McpServerPrompt(Name=…)]` on methods.

## File Structure

| File | Responsibility |
|---|---|
| `src/Apps/Sorcha.McpServer/Services/IHumanApproval.cs` | The approval seam: interface, request/result types, three-state outcome. |
| `src/Apps/Sorcha.McpServer/Services/ElicitationHumanApproval.cs` | The one production implementation; the only place `ElicitAsync` is called. |
| `src/Apps/Sorcha.McpServer/Tools/Designer/RegisterCreateTool.cs` | `sorcha_register_create` — composite ceremony. |
| `src/Apps/Sorcha.McpServer/Tools/Designer/BlueprintPublishTool.cs` | `sorcha_blueprint_publish` — publish with rehearsal-gate handling. |
| `src/Apps/Sorcha.McpServer/Tools/Designer/InstanceCreateTool.cs` | `sorcha_instance_create` — starts a workflow. |
| `src/Apps/Sorcha.McpServer/Resources/SorchaResources.cs` | Static resources: blueprint schema, examples, glossary. |
| `src/Apps/Sorcha.McpServer/Resources/LiveStateResources.cs` | Caller-scoped live resources: registers, instances. |
| `src/Apps/Sorcha.McpServer/Prompts/SorchaPrompts.cs` | The three guided prompts. |
| `scripts/check-mcp-response-shapes.ps1` | Response-shape CI gate. |
| `.mcp-response-shapes-allowlist` | Shrink-only ratchet for the gate. |

---

### Task 1: The `IHumanApproval` seam

`McpServer.ElicitAsync` is non-virtual, so a tool that calls it directly cannot be unit-tested. Every tool needing a human moment depends on this interface instead. The single implementation is tested on its own against a stub `McpServer`.

The three-state rule is the whole point: a client may **declare** `elicitation` and still answer automatically. The spike measured Claude Code 2.1.261 in `-p` mode returning `{"action":"cancel"}`. Only `accept` is approval.

**Files:**
- Create: `src/Apps/Sorcha.McpServer/Services/IHumanApproval.cs`
- Create: `src/Apps/Sorcha.McpServer/Services/ElicitationHumanApproval.cs`
- Modify: `src/Apps/Sorcha.McpServer/Infrastructure/McpServerHttpRegistration.cs` (register the service)
- Test: `tests/Sorcha.McpServer.Tests/Services/ElicitationHumanApprovalTests.cs`

**Interfaces:**
- Consumes: nothing (first task).
- Produces:
  - `enum ApprovalOutcome { Approved, Refused, NotSupported }`
  - `sealed record HumanApprovalRequest(string Message, string ConfirmTitle)`
  - `sealed record ApprovalResult(ApprovalOutcome Outcome, string Detail)`
  - `interface IHumanApproval { Task<ApprovalResult> RequestAsync(McpServer server, HumanApprovalRequest request, CancellationToken cancellationToken = default); }`

- [ ] **Step 1: Write the seam**

Create `src/Apps/Sorcha.McpServer/Services/IHumanApproval.cs`:

```csharp
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using ModelContextProtocol.Server;

namespace Sorcha.McpServer.Services;

/// <summary>How a request for human approval resolved.</summary>
public enum ApprovalOutcome
{
    /// <summary>A person explicitly accepted. The ONLY outcome that permits the operation.</summary>
    Approved,

    /// <summary>A person declined, or the client dismissed the request without a choice.</summary>
    Refused,

    /// <summary>The client did not declare the elicitation capability, so no person can be asked.</summary>
    NotSupported
}

/// <summary>What the person is being asked to approve.</summary>
/// <param name="Message">The full question, naming the concrete consequence.</param>
/// <param name="ConfirmTitle">The label on the confirmation field.</param>
public sealed record HumanApprovalRequest(string Message, string ConfirmTitle);

/// <summary>The resolved approval, with operator-facing detail for the refusal paths.</summary>
/// <param name="Outcome">The outcome.</param>
/// <param name="Detail">Human-readable explanation, surfaced to the agent verbatim.</param>
public sealed record ApprovalResult(ApprovalOutcome Outcome, string Detail);

/// <summary>
/// Obtains a real person's approval before an operationally-consequential act.
/// <para>
/// This exists as a seam for two reasons. First, <c>McpServer.ElicitAsync</c> is non-virtual in
/// SDK 2.2.0 and therefore cannot be mocked, so a tool calling it directly is untestable.
/// Second, the platform genuinely cannot tell an agent from the human whose bearer token it
/// forwards — so the sign-off must happen at the client, where the person actually is, and this
/// is the single place that contract is expressed.
/// </para>
/// </summary>
public interface IHumanApproval
{
    /// <summary>Asks the caller's client to put <paramref name="request"/> to a person.</summary>
    /// <param name="server">The live MCP server for this invocation (from <c>RequestContext.Server</c>).</param>
    /// <param name="request">What the person is being asked.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The approval outcome. Treat anything but <see cref="ApprovalOutcome.Approved"/> as a refusal.</returns>
    Task<ApprovalResult> RequestAsync(
        McpServer server,
        HumanApprovalRequest request,
        CancellationToken cancellationToken = default);
}
```

- [ ] **Step 2: Write the failing test**

Create `tests/Sorcha.McpServer.Tests/Services/ElicitationHumanApprovalTests.cs`:

```csharp
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using ModelContextProtocol.Protocol;
using Sorcha.McpServer.Services;

namespace Sorcha.McpServer.Tests.Services;

/// <summary>
/// The three client states. A client may DECLARE elicitation and still answer automatically:
/// the spike measured Claude Code 2.1.261 in non-interactive mode returning
/// <c>{"action":"cancel"}</c>. A capability-only check would let a headless agent straight
/// through, so only <c>accept</c> may approve.
/// </summary>
public class ElicitationHumanApprovalTests
{
    private static readonly HumanApprovalRequest Request =
        new("Create register 'Acme Supply'?", "Confirm");

    [Fact]
    public async Task RequestAsync_ClientDidNotDeclareElicitation_ReturnsNotSupported()
    {
        var server = new FakeMcpServer(capabilities: new ClientCapabilities());
        var sut = new ElicitationHumanApproval();

        var result = await sut.RequestAsync(server, Request);

        result.Outcome.Should().Be(ApprovalOutcome.NotSupported);
        server.ElicitCallCount.Should().Be(0, "no person can be asked, so nothing should be sent");
    }

    [Fact]
    public async Task RequestAsync_UserAccepts_ReturnsApproved()
    {
        var server = new FakeMcpServer(
            capabilities: new ClientCapabilities { Elicitation = new ElicitationCapability() },
            response: new ElicitResult { Action = "accept" });
        var sut = new ElicitationHumanApproval();

        var result = await sut.RequestAsync(server, Request);

        result.Outcome.Should().Be(ApprovalOutcome.Approved);
    }

    [Theory]
    [InlineData("decline")]
    [InlineData("cancel")]
    public async Task RequestAsync_UserDeclinesOrClientCancels_ReturnsRefused(string action)
    {
        var server = new FakeMcpServer(
            capabilities: new ClientCapabilities { Elicitation = new ElicitationCapability() },
            response: new ElicitResult { Action = action });
        var sut = new ElicitationHumanApproval();

        var result = await sut.RequestAsync(server, Request);

        result.Outcome.Should().Be(ApprovalOutcome.Refused);
        result.Detail.Should().Contain(action);
    }

    [Fact]
    public async Task RequestAsync_SendsTheMessageAndABooleanConfirmSchema()
    {
        var server = new FakeMcpServer(
            capabilities: new ClientCapabilities { Elicitation = new ElicitationCapability() },
            response: new ElicitResult { Action = "accept" });
        var sut = new ElicitationHumanApproval();

        await sut.RequestAsync(server, Request);

        server.LastParams.Should().NotBeNull();
        server.LastParams!.Message.Should().Be("Create register 'Acme Supply'?");
        server.LastParams.RequestedSchema.Properties.Should().ContainKey("confirm");
        server.LastParams.RequestedSchema.Required.Should().Contain("confirm");
    }
}
```

Create the fake in the same file (below the test class). `McpServer` is abstract with a virtual `ClientCapabilities`, but `ElicitAsync` is **not** virtual — so the fake overrides the one send primitive `ElicitAsync` routes through:

```csharp
/// <summary>
/// Minimal <see cref="McpServer"/> stand-in. <c>ElicitAsync</c> is non-virtual, so it cannot be
/// overridden; it is intercepted at <c>SendRequestAsync</c>, which is the primitive it calls.
/// </summary>
internal sealed class FakeMcpServer : McpServer
{
    private readonly ElicitResult? _response;

    public FakeMcpServer(ClientCapabilities capabilities, ElicitResult? response = null)
    {
        ClientCapabilities = capabilities;
        _response = response;
    }

    public override ClientCapabilities? ClientCapabilities { get; }

    public int ElicitCallCount { get; private set; }

    public ElicitRequestParams? LastParams { get; private set; }

    public override async Task<JsonRpcResponse> SendRequestAsync(
        JsonRpcRequest request,
        CancellationToken cancellationToken = default)
    {
        ElicitCallCount++;
        LastParams = JsonSerializer.Deserialize<ElicitRequestParams>(
            request.Params, McpJsonUtilities.DefaultOptions);
        await Task.Yield();
        return new JsonRpcResponse
        {
            Id = request.Id,
            Result = JsonSerializer.SerializeToNode(
                _response ?? new ElicitResult { Action = "cancel" },
                McpJsonUtilities.DefaultOptions)
        };
    }
}
```

> **Implementer note.** `McpServer`'s abstract surface may require more overrides than
> `ClientCapabilities` and `SendRequestAsync` (the compiler will name each one). Implement the
> remainder as `throw new NotSupportedException()` — the tests must only exercise the elicitation
> path. If intercepting at `SendRequestAsync` proves impractical, put a thin
> `IElicitationTransport` (one method: `Task<ElicitResult> SendAsync(McpServer, ElicitRequestParams, CancellationToken)`)
> between `ElicitationHumanApproval` and the SDK and mock that instead. Do **not** solve it by
> making the tools call `ElicitAsync` directly — that reintroduces the untestable coupling this
> task exists to prevent.

- [ ] **Step 3: Run the test and confirm it fails**

Run: `dotnet test --project tests/Sorcha.McpServer.Tests/Sorcha.McpServer.Tests.csproj --filter-class "*ElicitationHumanApprovalTests*"`
Expected: FAIL — `ElicitationHumanApproval` does not exist.

- [ ] **Step 4: Implement**

Create `src/Apps/Sorcha.McpServer/Services/ElicitationHumanApproval.cs`:

```csharp
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Sorcha.McpServer.Services;

/// <summary>
/// Obtains approval through the MCP elicitation primitive. The only place in the codebase that
/// calls <see cref="McpServer.ElicitAsync(ElicitRequestParams, CancellationToken)"/>.
/// </summary>
public sealed class ElicitationHumanApproval : IHumanApproval
{
    /// <inheritdoc />
    public async Task<ApprovalResult> RequestAsync(
        McpServer server,
        HumanApprovalRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(server);
        ArgumentNullException.ThrowIfNull(request);

        // Fail closed. A client that cannot ask a person does not get to perform the act —
        // in every environment, with no bypass flag.
        if (server.ClientCapabilities?.Elicitation is null)
        {
            return new ApprovalResult(
                ApprovalOutcome.NotSupported,
                "This operation needs a person to confirm it, and your MCP client did not " +
                "declare the 'elicitation' capability at initialize. Connect with a client " +
                "that supports elicitation, or perform this step in the Sorcha UI.");
        }

        var parameters = new ElicitRequestParams
        {
            Message = request.Message,
            RequestedSchema = new ElicitRequestParams.RequestSchema
            {
                Properties =
                {
                    ["confirm"] = new ElicitRequestParams.BooleanSchema
                    {
                        Title = request.ConfirmTitle,
                        Description = "Confirm to proceed. Anything else cancels."
                    }
                },
                Required = { "confirm" }
            }
        };

        var result = await server.ElicitAsync(parameters, cancellationToken).ConfigureAwait(false);

        // IsAccepted is true ONLY for action == "accept". A non-interactive client answers
        // "cancel" automatically, which must not read as approval.
        if (result.IsAccepted)
        {
            return new ApprovalResult(ApprovalOutcome.Approved, "Approved by the user.");
        }

        return new ApprovalResult(
            ApprovalOutcome.Refused,
            $"The user did not approve this operation (action: {result.Action}). Nothing was changed.");
    }
}
```

- [ ] **Step 5: Register it**

In `src/Apps/Sorcha.McpServer/Infrastructure/McpServerHttpRegistration.cs`, inside `ConfigureServices`, alongside the existing singletons:

```csharp
services.AddSingleton<IHumanApproval, ElicitationHumanApproval>();
```

Add `using Sorcha.McpServer.Services;` if not already present.

- [ ] **Step 6: Run the tests and confirm they pass**

Run: `dotnet build && dotnet test --project tests/Sorcha.McpServer.Tests/Sorcha.McpServer.Tests.csproj --filter-class "*ElicitationHumanApprovalTests*"`
Expected: PASS (4 tests, one of them a 2-case theory).

Also run the P0 activation gate, which must still pass now that a new dependency exists:

Run: `dotnet test --project tests/Sorcha.McpServer.Tests/Sorcha.McpServer.Tests.csproj --filter-class "*HttpModeActivationTests*"`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add src/Apps/Sorcha.McpServer/Services/IHumanApproval.cs \
        src/Apps/Sorcha.McpServer/Services/ElicitationHumanApproval.cs \
        src/Apps/Sorcha.McpServer/Infrastructure/McpServerHttpRegistration.cs \
        tests/Sorcha.McpServer.Tests/Services/ElicitationHumanApprovalTests.cs
git commit -m "feat: [MCP-P1] add the IHumanApproval elicitation seam

Only action=='accept' approves. A client may declare elicitation and
still answer automatically — Claude Code 2.1.261 in non-interactive
mode returns {\"action\":\"cancel\"} — so a capability-only check would
let a headless agent straight through.

The seam exists because McpServer.ElicitAsync is non-virtual in SDK
2.2.0 and cannot be mocked; tools depending on it directly would be
untestable."
```

---

### Task 2: Fix `sorcha_tenant_list`, and make argument errors legible

Two defects found by executing tools against the CI image on 2026-09-06. Neither is caught by any test or gate.

**Files:**
- Modify: `src/Apps/Sorcha.McpServer/Tools/Admin/TenantListTool.cs`
- Modify: `src/Apps/Sorcha.McpServer/Program.cs` (tool-invocation error mapping)
- Test: `tests/Sorcha.McpServer.Tests/Tools/TenantListToolTests.cs`

**Interfaces:**
- Consumes: nothing from Task 1.
- Produces: nothing later tasks depend on.

- [ ] **Step 1: Write the failing test**

Create `tests/Sorcha.McpServer.Tests/Tools/TenantListToolTests.cs`:

```csharp
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using FluentAssertions;

namespace Sorcha.McpServer.Tests.Tools;

/// <summary>
/// The Tenant Service returns <c>OrganizationListResponse { organizations, totalCount }</c>.
/// The tool used to deserialize <c>{ items, page, pageSize, totalPages }</c>, so it reported
/// "Retrieved 0 tenant(s)" against a live node holding 27 — while its own description tells an
/// agent to call it "to check whether an organisation is already provisioned before creating a
/// new one". The route is mapped, so the route gate never saw it.
/// </summary>
public class TenantListToolTests
{
    private const string ServerBody = """
        {
          "organizations": [
            { "organizationId": "00000000-0000-0000-0000-000000000001",
              "name": "Sorcha Local", "status": "Active" },
            { "organizationId": "00000000-0000-0000-0000-000000000002",
              "name": "Sorcha Public", "status": "Active" }
          ],
          "totalCount": 27
        }
        """;

    [Fact]
    public void Parse_ServerBody_ReadsOrganizationsNotItems()
    {
        var parsed = TenantListTool.ParseTenantList(ServerBody);

        parsed.Should().NotBeNull();
        parsed!.Organizations.Should().HaveCount(2);
        parsed.Organizations[0].Name.Should().Be("Sorcha Local");
        parsed.TotalCount.Should().Be(27);
    }

    [Fact]
    public void BuildQuery_UsesPageNumber_NotPage()
    {
        // The endpoint binds `pageNumber`. Sending `page` left it at its default, so a request
        // for page 3 silently returned page 1.
        var query = TenantListTool.BuildQueryString(page: 3, pageSize: 10, status: null, search: null);

        query.Should().Contain("pageNumber=3");
        query.Should().NotContain("page=3");
        query.Should().Contain("pageSize=10");
    }
}
```

- [ ] **Step 2: Run it and confirm it fails**

Run: `dotnet test --project tests/Sorcha.McpServer.Tests/Sorcha.McpServer.Tests.csproj --filter-class "*TenantListToolTests*"`
Expected: FAIL — `ParseTenantList` / `BuildQueryString` do not exist.

- [ ] **Step 3: Fix the DTO and extract the two testable helpers**

In `TenantListTool.cs`, replace the private `TenantListResponse` class with one matching the server, and make it plus the two helpers `internal` so the test project can reach them:

```csharp
    /// <summary>
    /// Mirrors the Tenant Service's <c>OrganizationListResponse</c> from
    /// <c>GET /api/organizations/</c>. Property names here ARE the wire contract — the previous
    /// shape (<c>Items</c>/<c>Page</c>/<c>PageSize</c>/<c>TotalPages</c>) matched nothing the
    /// server sends, so every field silently defaulted.
    /// </summary>
    internal sealed class TenantListResponse
    {
        public List<TenantDto> Organizations { get; set; } = [];

        public int TotalCount { get; set; }
    }

    /// <summary>Deserializes the Tenant Service list body. Returns null on unparseable input.</summary>
    internal static TenantListResponse? ParseTenantList(string body) =>
        JsonSerializer.Deserialize<TenantListResponse>(
            body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

    /// <summary>
    /// Builds the list query string. The endpoint binds <c>pageNumber</c>, not <c>page</c>.
    /// </summary>
    internal static string BuildQueryString(int page, int pageSize, string? status, string? search)
    {
        var parts = new List<string> { $"pageNumber={page}", $"pageSize={pageSize}" };
        if (!string.IsNullOrWhiteSpace(status))
        {
            parts.Add($"status={Uri.EscapeDataString(status)}");
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            parts.Add($"search={Uri.EscapeDataString(search)}");
        }

        return string.Join("&", parts);
    }
```

Then update the call site that built the query (currently the `$"page={page}"` / `$"pageSize={pageSize}"` list around line 109) to call `BuildQueryString`, and update the result mapping to read `result.Organizations` instead of `result.Items`. Because the server sends no paging envelope, report the values the tool requested rather than echoing zeros:

```csharp
                Tenants = result.Organizations.Select(t => new TenantInfo
                {
                    TenantId = t.OrganizationId ?? "",
                    Name = t.Name ?? "",
                    Status = t.Status ?? "Active",
                    UserCount = t.UserCount,
                    BlueprintCount = t.BlueprintCount,
                    CreatedAt = t.CreatedAt,
                    LastActivityAt = t.LastActivityAt
                }).ToList(),
                TotalCount = result.TotalCount,
                Page = page,
                PageSize = pageSize,
                TotalPages = pageSize > 0 ? (int)Math.Ceiling(result.TotalCount / (double)pageSize) : 0
```

Update the `Message` to use `result.Organizations.Count`.

Add `InternalsVisibleTo` if the test project cannot see internals. Check first — if `Sorcha.McpServer.csproj` has no `InternalsVisibleTo`, add:

```xml
  <ItemGroup>
    <InternalsVisibleTo Include="Sorcha.McpServer.Tests" />
  </ItemGroup>
```

- [ ] **Step 4: Run the tests and confirm they pass**

Run: `dotnet build && dotnet test --project tests/Sorcha.McpServer.Tests/Sorcha.McpServer.Tests.csproj --filter-class "*TenantListToolTests*"`
Expected: PASS (2 tests).

- [ ] **Step 5: Make argument-binding failures legible**

Calling `sorcha_user_list` without its required `organizationId` returns
`"An error occurred invoking 'sorcha_user_list'."` — byte-identical to the string the six-day
P0 outage produced. That opacity is why the outage went unnoticed.

In `src/Apps/Sorcha.McpServer/Program.cs`, inside `ConfigureServerOptions`, add a filter that
converts an argument-binding `ArgumentException` into actionable text:

```csharp
    // A missing required argument previously produced the SAME opaque string as a wholly dead
    // surface ("An error occurred invoking 'X'."), with the real reason visible only in server
    // logs. An agent cannot distinguish "you forgot a field" from "the platform is broken", and
    // that ambiguity is exactly what masked the P0 outage for six days.
    options.Capabilities ??= new ServerCapabilities();
    options.Capabilities.Tools ??= new ToolsCapability();
    var innerHandler = options.Capabilities.Tools.CallToolHandler;
    options.Capabilities.Tools.CallToolHandler = async (context, cancellationToken) =>
    {
        try
        {
            return await innerHandler!(context, cancellationToken);
        }
        catch (ArgumentException ex) when (ex.Message.Contains("missing a value for the required parameter"))
        {
            return new CallToolResult
            {
                IsError = true,
                Content = [new TextContentBlock
                {
                    Text = $"Missing a required argument for '{context.Params?.Name}'. {ex.Message} " +
                           "Call tools/list to see this tool's required parameters."
                }]
            };
        }
    };
```

> **Implementer note.** The exact hook name may differ in SDK 2.2.0 — if
> `Capabilities.Tools.CallToolHandler` is not assignable after `WithToolsFromAssembly()`, achieve
> the same with the SDK's tool-filter/middleware mechanism instead. The requirement is behavioural:
> **the response text must name the missing parameter.** Prove it with the test in Step 6 and do
> not settle for logging it server-side.

- [ ] **Step 6: Test the error mapping**

Append to `tests/Sorcha.McpServer.Tests/Infrastructure/HttpModeInvocationTests.cs`:

```csharp
    [Fact]
    public async Task CallTool_MissingRequiredArgument_ReturnsTextNamingTheParameter()
    {
        // Regression guard: this must NOT be the same opaque string a dead surface produces.
        var result = await InvokeToolAsync("sorcha_user_list", new Dictionary<string, object?>());

        var text = string.Join(" ", result.Content.OfType<TextContentBlock>().Select(c => c.Text));
        text.Should().Contain("organizationId");
        text.Should().NotBe("An error occurred invoking 'sorcha_user_list'.");
    }
```

> Reuse whatever helper `HttpModeInvocationTests` already has for driving a tool call; if there is
> no `InvokeToolAsync`, add one following the existing pattern in that file.

- [ ] **Step 7: Run and commit**

Run: `dotnet test --project tests/Sorcha.McpServer.Tests/Sorcha.McpServer.Tests.csproj`
Expected: PASS.

```bash
git add src/Apps/Sorcha.McpServer/Tools/Admin/TenantListTool.cs \
        src/Apps/Sorcha.McpServer/Program.cs \
        src/Apps/Sorcha.McpServer/Sorcha.McpServer.csproj \
        tests/Sorcha.McpServer.Tests/Tools/TenantListToolTests.cs \
        tests/Sorcha.McpServer.Tests/Infrastructure/HttpModeInvocationTests.cs
git commit -m "fix: [MCP-P1] sorcha_tenant_list read the wrong response shape

It deserialized { items, page, pageSize, totalPages }; the Tenant
Service returns { organizations, totalCount }. Live on n1 it reported
'Retrieved 0 tenant(s)' with totalCount 27 — and its description tells
agents to call it to check whether an org already exists, so it
answered no for every org that does. Also sent page= where the endpoint
binds pageNumber=, so page 3 silently returned page 1.

Additionally: a missing required argument no longer returns the same
opaque string as a dead surface — the response now names the parameter."
```

---

### Task 3: The response-shape CI gate

The route gate proves a tool's URL exists. Nothing proves the tool reads what the endpoint sends — which is how Task 2's defect survived, and how nine of the ten tools repointed in P0 were *also* wrong about response shape after their URLs were fixed. This is the MCP analogue of `Sorcha.Cli.ContractTests` (CLAUDE.md pattern 18).

**Files:**
- Create: `scripts/check-mcp-response-shapes.ps1`
- Create: `.mcp-response-shapes-allowlist`
- Modify: `.github/workflows/` — the workflow that carries `mcp-routes-gate`

**Interfaces:**
- Consumes: Task 2's corrected `TenantListResponse` (it must not appear in the seeded allowlist).
- Produces: a CI gate later tasks must keep green.

- [ ] **Step 1: Read the existing gate to match its shape**

Read `scripts/check-mcp-routes.ps1` in full. Reuse its conventions: derive the guarded set from source rather than hardcoding, fail on **stale** allowlist entries as well as new violations, and enforce a non-vacuity floor so a broken parser cannot pass by finding nothing.

- [ ] **Step 2: Write the gate**

Create `scripts/check-mcp-response-shapes.ps1`. It must:

1. Find every private/internal response DTO declared inside a file under
   `src/Apps/Sorcha.McpServer/Tools/` (the classes tools deserialize service bodies into).
2. For each, collect its property names.
3. Find the server type the owning tool's route produces, by locating the route string in the tool
   and matching it against `.Produces<T>()` / typed-results declarations in
   `src/Services/Sorcha.*.Service/`.
4. Report any DTO property with no corresponding property on the server type (case-insensitive,
   honouring `[JsonPropertyName]` on both sides).
5. Exit non-zero on any violation not in `.mcp-response-shapes-allowlist`, **and** on any
   allowlist entry that no longer corresponds to a real violation (stale entries rot).
6. Exit non-zero if fewer than 20 DTOs were discovered — a non-vacuity floor. Print the count.

Mirror the parameter block, output formatting, and exit-code conventions of `check-mcp-routes.ps1`.

- [ ] **Step 3: Seed the allowlist honestly**

Run: `pwsh scripts/check-mcp-response-shapes.ps1`

Write every reported violation into `.mcp-response-shapes-allowlist`, one per line, each with a
trailing comment naming *why* it is there. Expect a non-trivial seed: reflection cannot see a
server type that is a `private record` or an anonymous `Results.Ok(new { … })`, so some entries
will be "server shape not statically reachable" rather than real defects — the same limitation
`Sorcha.Cli.ContractTests` documents in its `NotAWireContract` list.

Add this header to the file:

```
# Response-shape gate ratchet. MAY ONLY SHRINK.
# An entry here is a tool DTO property with no matching property on the server type its route
# produces. Two kinds live here:
#   (a) a genuine mismatch not yet fixed — fix it and delete the line;
#   (b) a server shape the analyser cannot see statically (private record, anonymous
#       Results.Ok(new { ... })) — the comment must say so.
# NEVER add a line to make a build pass. Fix the DTO instead.
```

**`TenantListTool.TenantListResponse` must NOT appear here** — Task 2 fixed it.

- [ ] **Step 4: Prove the gate is not vacuous**

Temporarily add a bogus property to a tool DTO:

```csharp
        public string BogusShapeProbe { get; set; } = string.Empty;
```

Run: `pwsh scripts/check-mcp-response-shapes.ps1`
Expected: **FAIL**, naming `BogusShapeProbe`.

Then remove the property and re-run.
Expected: PASS.

> A gate that has never been seen to fail is not known to work. Record both outcomes in the commit
> message. **Revert the probe before committing** — `docker build` snapshots the working tree, not
> HEAD, so a stray probe can ship into an image.

- [ ] **Step 5: Wire it into CI**

Find the workflow job that runs `scripts/check-mcp-routes.ps1` (job name `mcp-routes-gate`) and add
a sibling job `mcp-response-shapes-gate` with the same trigger, runner, and shell, invoking
`pwsh scripts/check-mcp-response-shapes.ps1`.

- [ ] **Step 6: Commit**

```bash
git add scripts/check-mcp-response-shapes.ps1 .mcp-response-shapes-allowlist .github/workflows/
git commit -m "test: [MCP-P1] gate MCP tool response shapes

The route gate proves a URL exists; nothing proved the tool reads what
the endpoint sends. That is how sorcha_tenant_list reported 0 of 27
tenants with a green route gate, and how nine of ten tools repointed in
P0 were also wrong about response shape after their URLs were fixed.

Ratchet allowlist, shrink-only, fails on stale entries, with a
non-vacuity floor. Proved to fail on an injected bogus property and
pass once removed."
```

---

### Task 4: `sorcha_instance_create`

The simplest lifecycle tool, done first to establish the typed-client pattern the other two follow. It produces the `instanceId` that `sorcha_action_submit` already requires and nothing currently supplies.

**Files:**
- Modify: `src/Common/Sorcha.ServiceClients.Http/Blueprint/IBlueprintServiceClient.cs`
- Modify: the `IBlueprintServiceClient` implementation in the same folder
- Create: `src/Apps/Sorcha.McpServer/Tools/Designer/InstanceCreateTool.cs`
- Modify: `src/Apps/Sorcha.McpServer/Services/ToolEntitlement.cs`
- Test: `tests/Sorcha.McpServer.Tests/Tools/InstanceCreateToolTests.cs`

**Interfaces:**
- Consumes: `IMcpAuthorizationService`, `IServiceAvailabilityTracker` (existing patterns — copy `BlueprintCreateTool`).
- Produces: `Task<string?> IBlueprintServiceClient.CreateInstanceAsync(string blueprintId, string registerId, string? tenantId, CancellationToken)` — Task 6 does **not** use it, but keep the name stable.

- [ ] **Step 1: Add the typed client method**

In `IBlueprintServiceClient.cs`, after `GetWorkflowStatusAsync`:

```csharp
    /// <summary>
    /// Creates a workflow instance from a published blueprint. Calls <c>POST /api/instances/</c>.
    /// </summary>
    /// <param name="blueprintId">The blueprint to instantiate.</param>
    /// <param name="registerId">The register the instance's transactions are written to.</param>
    /// <param name="tenantId">Optional tenant id for isolation (server defaults to "default").</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The created-instance JSON body, or null on non-success.</returns>
    Task<string?> CreateInstanceAsync(
        string blueprintId,
        string registerId,
        string? tenantId = null,
        CancellationToken cancellationToken = default);
```

Implement it in the concrete client, following the exact shape of the neighbouring
`CreateBlueprintAsync` (same auth-header helper, same non-success-returns-null convention).
The request body binds `CreateInstanceRequest { BlueprintId, RegisterId, TenantId?, Metadata? }`,
so serialize:

```csharp
        var payload = JsonSerializer.Serialize(new
        {
            blueprintId,
            registerId,
            tenantId
        });
```

- [ ] **Step 2: Write the failing test**

Create `tests/Sorcha.McpServer.Tests/Tools/InstanceCreateToolTests.cs`:

```csharp
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sorcha.McpServer.Services;
using Sorcha.McpServer.Tools.Designer;
using Sorcha.ServiceClients.Blueprint;

namespace Sorcha.McpServer.Tests.Tools;

public class InstanceCreateToolTests
{
    private readonly Mock<IBlueprintServiceClient> _client = new();
    private readonly Mock<IMcpAuthorizationService> _auth = new();
    private readonly Mock<IServiceAvailabilityTracker> _availability = new();

    private InstanceCreateTool CreateSut()
    {
        _auth.Setup(a => a.CanInvokeTool("sorcha_instance_create")).Returns(true);
        _availability.Setup(a => a.IsServiceAvailable("Blueprint")).Returns(true);
        return new InstanceCreateTool(
            _auth.Object, _availability.Object, _client.Object,
            NullLogger<InstanceCreateTool>.Instance);
    }

    [Fact]
    public async Task CreateInstanceAsync_Success_ReturnsInstanceIdAndReference()
    {
        _client.Setup(c => c.CreateInstanceAsync("bp-1", "reg-1", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync("""
                { "instanceId": "inst-42", "blueprintId": "bp-1", "registerId": "reg-1",
                  "state": "Active", "instanceReference": "CP-RIV-14-A7K3" }
                """);

        var result = await CreateSut().CreateInstanceAsync("bp-1", "reg-1");

        result.Status.Should().Be("Success");
        result.InstanceId.Should().Be("inst-42");
        result.InstanceReference.Should().Be("CP-RIV-14-A7K3");
    }

    [Fact]
    public async Task CreateInstanceAsync_BlueprintNotReplicatedYet_SurfacesRetryableState()
    {
        // The service answers 409 blueprint_not_available on a replica whose blueprints have not
        // finished replicating. That is retryable, not a permanent failure, and the agent must be
        // told which it is or it will give up on a register that is about to work.
        _client.Setup(c => c.CreateInstanceAsync(It.IsAny<string>(), It.IsAny<string>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var result = await CreateSut().CreateInstanceAsync("bp-1", "reg-1");

        result.Status.Should().Be("Error");
        result.InstanceId.Should().BeNull();
    }

    [Fact]
    public async Task CreateInstanceAsync_NotEntitled_RefusesWithoutCallingTheService()
    {
        _auth.Setup(a => a.CanInvokeTool("sorcha_instance_create")).Returns(false);
        var sut = new InstanceCreateTool(
            _auth.Object, _availability.Object, _client.Object,
            NullLogger<InstanceCreateTool>.Instance);

        var result = await sut.CreateInstanceAsync("bp-1", "reg-1");

        result.Status.Should().Be("Unauthorized");
        _client.Verify(c => c.CreateInstanceAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
```

- [ ] **Step 3: Run it and confirm it fails**

Run: `dotnet test --project tests/Sorcha.McpServer.Tests/Sorcha.McpServer.Tests.csproj --filter-class "*InstanceCreateToolTests*"`
Expected: FAIL — `InstanceCreateTool` does not exist.

- [ ] **Step 4: Implement the tool**

Create `src/Apps/Sorcha.McpServer/Tools/Designer/InstanceCreateTool.cs`, following
`BlueprintCreateTool.cs` exactly for structure (authorization check, availability check,
stopwatch, the four catch blocks, a `sealed record` result type with
`Status`/`Message`/`CheckedAt`/`ResponseTimeMs`).

The tool method:

```csharp
    /// <summary>Starts a workflow instance from a published blueprint.</summary>
    /// <param name="blueprintId">The published blueprint to instantiate.</param>
    /// <param name="registerId">The register the instance's transactions are written to.</param>
    /// <param name="tenantId">Optional tenant id for isolation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The created instance's id and human-readable reference.</returns>
    [McpServerTool(Name = "sorcha_instance_create", Destructive = false, ReadOnly = false, Idempotent = false)]
    [Description("Starts a running workflow instance from a blueprint that has already been published to a register, and returns the instanceId plus a human-readable instanceReference. This is the step that turns a published definition into live work: call it after sorcha_blueprint_publish, and pass the returned instanceId to sorcha_action_submit to perform the workflow's first action. Both blueprintId and registerId are required, and the blueprint must already be published to that register — publishing and instantiating are separate steps.")]
    public async Task<InstanceCreateResult> CreateInstanceAsync(
        [Description("The published blueprint's ID")] string blueprintId,
        [Description("The register the instance writes its transactions to")] string registerId,
        [Description("Optional tenant ID for isolation; omit for the default tenant")] string? tenantId = null,
        CancellationToken cancellationToken = default)
```

The result record:

```csharp
/// <summary>Result of starting a workflow instance.</summary>
public sealed record InstanceCreateResult
{
    /// <summary>Operation status: Success, Error, Unavailable, Timeout, or Unauthorized.</summary>
    public required string Status { get; init; }

    /// <summary>Human-readable message about the operation result.</summary>
    public required string Message { get; init; }

    /// <summary>When the operation was performed.</summary>
    public required DateTimeOffset CheckedAt { get; init; }

    /// <summary>Response time in milliseconds.</summary>
    public int ResponseTimeMs { get; init; }

    /// <summary>The created instance's ID. Pass this to sorcha_action_submit.</summary>
    public string? InstanceId { get; init; }

    /// <summary>The instance's human-readable reference (e.g. "CP-RIV-14-A7K3"), when configured.</summary>
    public string? InstanceReference { get; init; }

    /// <summary>The instance's initial state.</summary>
    public string? State { get; init; }
}
```

- [ ] **Step 5: Add the entitlement**

In `ToolEntitlement.cs`, in the designer block:

```csharp
        new("sorcha_instance_create", PlatformOnly, DesignerRole),
```

- [ ] **Step 6: Run the tests, the activation gate, and both source gates**

```bash
dotnet build
dotnet test --project tests/Sorcha.McpServer.Tests/Sorcha.McpServer.Tests.csproj
pwsh scripts/check-mcp-routes.ps1
pwsh scripts/check-mcp-response-shapes.ps1
```
Expected: all PASS. `check-mcp-routes.ps1` must stay green **with an empty allowlist** — if the new
route is not recognised, extend the resolver, never the allowlist.

- [ ] **Step 7: Commit**

```bash
git add src/Common/Sorcha.ServiceClients.Http/Blueprint/ \
        src/Apps/Sorcha.McpServer/Tools/Designer/InstanceCreateTool.cs \
        src/Apps/Sorcha.McpServer/Services/ToolEntitlement.cs \
        tests/Sorcha.McpServer.Tests/Tools/InstanceCreateToolTests.cs
git commit -m "feat: [MCP-P1] add sorcha_instance_create

Closes the loop: sorcha_action_submit has always required an instanceId
that nothing on the surface produced. Adds the missing typed client
method (POST /api/instances/) alongside the existing ones."
```

---

### Task 5: `sorcha_register_create`

The composite ceremony, and the first tool to require human approval. Two values must never be chosen by the agent: the derivation path (a wrong one silently produces an ungovernable register) and the owning wallet.

**Files:**
- Create: `src/Apps/Sorcha.McpServer/Tools/Designer/RegisterCreateTool.cs`
- Modify: `src/Apps/Sorcha.McpServer/Services/ToolEntitlement.cs`
- Test: `tests/Sorcha.McpServer.Tests/Tools/RegisterCreateToolTests.cs`

**Interfaces:**
- Consumes: `IHumanApproval` / `ApprovalOutcome` / `HumanApprovalRequest` (Task 1);
  `ICallerContext.OrganizationId`; `IWalletServiceClient.SignTransactionAsync`;
  `IOrgInfoClient` (or whichever existing client resolves an org's `walletAddress` — locate it
  under `src/Common/Sorcha.ServiceClients.Http/OrgInfo/`).
- Produces: nothing later tasks depend on.

- [ ] **Step 1: Locate the org-wallet lookup**

Run: `grep -rn "walletAddress" src/Common/Sorcha.ServiceClients.Http/OrgInfo/ src/Common/Sorcha.ServiceClients.Http/Tenant/ --include=*.cs | head`

Use the existing client that returns an organisation's wallet address. **Do not add a new HTTP
call if one exists.** Record which you chose in the tool's XML doc.

- [ ] **Step 2: Write the failing tests**

Create `tests/Sorcha.McpServer.Tests/Tools/RegisterCreateToolTests.cs`. These four are the
load-bearing ones — each pins a value or a refusal whose failure mode is silent:

```csharp
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ModelContextProtocol.Server;
using Sorcha.McpServer.Services;
using Sorcha.McpServer.Tools.Designer;
using Sorcha.Wallet.Contracts.Constants;

namespace Sorcha.McpServer.Tests.Tools;

public class RegisterCreateToolTests
{
    [Fact]
    public async Task CreateRegisterAsync_ClientCannotElicit_RefusesBeforeAnyBackendCall()
    {
        var h = new Harness();
        h.Approval.Setup(a => a.RequestAsync(It.IsAny<McpServer>(), It.IsAny<HumanApprovalRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApprovalResult(ApprovalOutcome.NotSupported, "no elicitation"));

        var result = await h.Sut().CreateRegisterAsync(h.Server, "Acme Supply", "A register");

        result.Status.Should().Be("ApprovalRequired");
        h.Register.Verify(r => r.InitiateRegisterCreationAsync(
            It.IsAny<object>(), It.IsAny<CancellationToken>()), Times.Never);
        h.Wallet.Verify(w => w.SignTransactionAsync(
            It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Theory]
    [InlineData(ApprovalOutcome.Refused)]
    [InlineData(ApprovalOutcome.NotSupported)]
    public async Task CreateRegisterAsync_NotApproved_NeverInitiates(ApprovalOutcome outcome)
    {
        var h = new Harness();
        h.Approval.Setup(a => a.RequestAsync(It.IsAny<McpServer>(), It.IsAny<HumanApprovalRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApprovalResult(outcome, "nope"));

        await h.Sut().CreateRegisterAsync(h.Server, "Acme Supply", "A register");

        h.Register.Verify(r => r.InitiateRegisterCreationAsync(
            It.IsAny<object>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateRegisterAsync_Approved_SignsWithTheRegisterAttestationContext()
    {
        // A wrong derivation path does not throw — it derives a DIFFERENT, perfectly valid key,
        // and the register is silently ungovernable from creation. Nothing else catches this.
        var h = new Harness().WithApproval().WithInitiate(walletId: "ws11qorg", dataToSignHex: "aabb");

        await h.Sut().CreateRegisterAsync(h.Server, "Acme Supply", "A register");

        h.Wallet.Verify(w => w.SignTransactionAsync(
            "ws11qorg",
            It.IsAny<byte[]>(),
            SorchaDerivationPaths.RegisterAttestation,
            true,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateRegisterAsync_Approved_SignsWithTheCallersOrgWalletNotAnAgentSuppliedOne()
    {
        var h = new Harness().WithApproval().WithInitiate(walletId: "ws11qorg", dataToSignHex: "aabb");
        h.Caller.SetupGet(c => c.OrganizationId).Returns("00000000-0000-0000-0000-000000000042");

        await h.Sut().CreateRegisterAsync(h.Server, "Acme Supply", "A register");

        h.OrgInfo.Verify(o => o.GetOrganizationAsync(
            "00000000-0000-0000-0000-000000000042", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateRegisterAsync_Approved_TagsProvenanceInMetadata()
    {
        var h = new Harness().WithApproval().WithInitiate(walletId: "ws11qorg", dataToSignHex: "aabb");

        await h.Sut().CreateRegisterAsync(h.Server, "Acme Supply", "A register");

        h.CapturedInitiateMetadata.Should().ContainKey("createdVia")
            .WhoseValue.Should().Be("mcp");
    }
}
```

> **Implementer note.** Build the `Harness` to match whichever register client method you use for
> initiate/finalize. If `IRegisterServiceClient` has no initiate/finalize method, add them
> following the shape of the other spec-139 methods (raw-body-in, raw-body-or-null-out) rather
> than issuing raw `HttpClient` calls from the tool — the typed client is what forwards the
> caller's bearer and pins the route for the route gate.

- [ ] **Step 3: Run and confirm failure**

Run: `dotnet test --project tests/Sorcha.McpServer.Tests/Sorcha.McpServer.Tests.csproj --filter-class "*RegisterCreateToolTests*"`
Expected: FAIL — `RegisterCreateTool` does not exist.

- [ ] **Step 4: Implement**

Create `src/Apps/Sorcha.McpServer/Tools/Designer/RegisterCreateTool.cs`. Signature — note the
`McpServer` parameter, which the SDK injects and which the tool passes to `IHumanApproval`:

```csharp
    /// <summary>Creates a register, after a person confirms it.</summary>
    /// <param name="server">The live MCP server, injected by the SDK; used to reach the caller's client.</param>
    /// <param name="name">Register name (1-38 characters).</param>
    /// <param name="description">What the register is for (max 500 characters).</param>
    /// <param name="devMode">When true, payloads are stored as plaintext with read-time disclosure filtering.</param>
    /// <param name="advertise">When true, the register is advertised to the peer network.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The created register's id and genesis transaction id.</returns>
    [McpServerTool(Name = "sorcha_register_create", Destructive = true, ReadOnly = false, Idempotent = false)]
    [Description("Creates a new Sorcha register — the ledger that a workflow's transactions are written to — and returns its registerId and genesis transaction id. This is the first step in setting up any workflow: create a register, publish a blueprint to it with sorcha_blueprint_publish, then start work with sorcha_instance_create. Creating a register is irreversible and establishes governance, so it REQUIRES a person to confirm it interactively; if your MCP client does not support elicitation the call is refused. Set devMode only for development registers — it stores payloads as plaintext rather than encrypting them.")]
    public async Task<RegisterCreateResult> CreateRegisterAsync(
        McpServer server,
        [Description("Register name, 1-38 characters")] string name,
        [Description("What this register is for, max 500 characters")] string description,
        [Description("Store payloads as plaintext instead of encrypting them. Development only.")] bool devMode = false,
        [Description("Advertise this register to the peer network")] bool advertise = false,
        CancellationToken cancellationToken = default)
```

Body order, which is load-bearing:

```csharp
        // 1. Entitlement (cheap, local).
        if (!_authService.CanInvokeTool("sorcha_register_create")) { return Unauthorized(); }

        // 2. Validate name/description lengths against InitiateRegisterCreationRequest's
        //    DataAnnotations (name 1-38, description max 500) so the agent gets a precise
        //    message rather than a 400 it must decode.

        // 3. Resolve the owning wallet from the CALLER'S org_id — never from an argument.
        //    An agent naming a wallet cannot forge a signature, but it can produce a working
        //    register whose governance roster records the wrong key.
        var orgId = _callerContext.OrganizationId;
        if (string.IsNullOrWhiteSpace(orgId)) { return Error("No organisation in your token…"); }
        // …resolve walletAddress; if null, tell the agent the org has no wallet yet and that an
        // org admin must create and link one (#1525) — this is a human handoff by design.

        // 4. Ask a person. BEFORE /initiate, so their thinking time does not run against the
        //    5-minute pending-registration TTL.
        var approval = await _humanApproval.RequestAsync(server, new HumanApprovalRequest(
            $"Create a new Sorcha register '{name}'?\n\n" +
            $"Purpose: {description}\n" +
            $"Owning organisation wallet: {walletAddress}\n" +
            (devMode ? "Storage: DEVELOPMENT MODE — payloads stored as PLAINTEXT.\n" : "Storage: encrypted payloads.\n") +
            (advertise ? "Visibility: advertised to the peer network.\n" : "Visibility: private.\n") +
            "\nThis is irreversible and establishes the register's governance.",
            "Create the register"), cancellationToken);

        if (approval.Outcome != ApprovalOutcome.Approved)
        {
            return new RegisterCreateResult
            {
                Status = approval.Outcome == ApprovalOutcome.NotSupported ? "ApprovalRequired" : "Refused",
                Message = approval.Detail,
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        // 5. initiate -> sign each attestation -> finalize.
```

The signing step, with both fixed values:

```csharp
            foreach (var attestation in initiateResponse.AttestationsToSign)
            {
                // SorchaDerivationPaths.RegisterAttestation is slot 100 — the organisation's
                // GOVERNANCE key. The register's roster records whatever key signs here, and the
                // validator authorises later governance transactions by matching it. A wrong
                // value does not throw; it derives a different valid key and the register is
                // silently ungovernable. Never a literal (CLAUDE.md pattern 15).
                var signature = await _walletClient.SignTransactionAsync(
                    attestation.WalletId,
                    Convert.FromHexString(attestation.DataToSign),
                    SorchaDerivationPaths.RegisterAttestation,
                    isPreHashed: true,
                    cancellationToken);
                …
            }
```

Provenance on the initiate request:

```csharp
                Metadata = new Dictionary<string, string>
                {
                    // Audit fact about the creation event. Claims nothing about participants.
                    // NOTHING in the platform may ever branch on this (CLAUDE.md pattern 23) —
                    // it is a self-supplied label, so reading it to grant anything would be the
                    // exact defect that pattern exists to prevent.
                    ["createdVia"] = "mcp",
                    ["mcpToolVersion"] = McpServerVersion.Current
                },
```

- [ ] **Step 5: Add the entitlement**

```csharp
        new("sorcha_register_create", PlatformOnly, DesignerRole),
```

- [ ] **Step 6: Assert nothing reads the provenance key**

Append to `tests/Sorcha.McpServer.Tests/Tools/RegisterCreateToolTests.cs`:

```csharp
    [Fact]
    public void CreatedViaProvenance_IsWrittenButNeverRead()
    {
        // Pattern 23: a self-supplied label may be recorded as an audit fact, but the moment
        // anything branches on it, it becomes an authority claim the agent controls.
        var sourceRoot = FindRepositoryRoot();
        var readSites = Directory
            .EnumerateFiles(Path.Combine(sourceRoot, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                     && !f.EndsWith("RegisterCreateTool.cs", StringComparison.Ordinal))
            .Where(f => File.ReadAllText(f).Contains("createdVia", StringComparison.Ordinal))
            .ToList();

        readSites.Should().BeEmpty(
            "createdVia is an audit fact; branching on it would make it an authority claim the agent supplies about itself");
    }
```

Add a `FindRepositoryRoot()` helper that walks up from `AppContext.BaseDirectory` to the directory
containing `Sorcha.sln` (or reuse an equivalent helper if the test project already has one).

- [ ] **Step 7: Run everything and commit**

```bash
dotnet build
dotnet test --project tests/Sorcha.McpServer.Tests/Sorcha.McpServer.Tests.csproj
pwsh scripts/check-mcp-routes.ps1
pwsh scripts/check-mcp-response-shapes.ps1
pwsh scripts/check-derivation-contexts.ps1
```
Expected: all PASS.

```bash
git add src/Apps/Sorcha.McpServer/Tools/Designer/RegisterCreateTool.cs \
        src/Apps/Sorcha.McpServer/Services/ToolEntitlement.cs \
        src/Common/Sorcha.ServiceClients.Http/Register/ \
        tests/Sorcha.McpServer.Tests/Tools/RegisterCreateToolTests.cs
git commit -m "feat: [MCP-P1] add sorcha_register_create with human sign-off

Runs initiate -> sign -> finalize as one call. Elicits BEFORE initiate
so the person's thinking time does not run against the 5-minute pending
registration TTL. Only action=='accept' proceeds; a client that cannot
elicit is refused before any backend call.

Two values the agent never chooses: the derivation path (slot 100, the
org governance key — a wrong value silently yields an ungovernable
register) and the owning wallet (resolved from the caller's org_id).

Records createdVia=mcp as an audit fact, with a test asserting no
production code reads it."
```

---

### Task 6: `sorcha_blueprint_publish`

`PublishGate` blocks **any** publish without a matching rehearsal pass, so an agent's freshly-authored blueprint always hits `409 REHEARSAL_REQUIRED`. The tool never overrides silently; it asks a person, naming what they are waiving.

**Files:**
- Create: `src/Apps/Sorcha.McpServer/Tools/Designer/BlueprintPublishTool.cs`
- Modify: `src/Apps/Sorcha.McpServer/Services/ToolEntitlement.cs`
- Test: `tests/Sorcha.McpServer.Tests/Tools/BlueprintPublishToolTests.cs`

**Interfaces:**
- Consumes: `IHumanApproval` (Task 1);
  `IBlueprintServiceClient.PublishBlueprintAsync(string blueprintId, PublishBlueprintRequest request, CancellationToken)`
  returning `PublishBlueprintOutcome?` (already exists, currently uncalled);
  `PublishBlueprintRequest { RegisterId, Override }`; `PublishOverride`.
- Produces: nothing later tasks depend on.

- [ ] **Step 1: Read the outcome type**

Run: `grep -n "PublishBlueprintOutcome" -A 25 src/Common/Sorcha.ServiceClients.Http/Blueprint/Models/*.cs | head -40`

Note how a `REHEARSAL_REQUIRED` block is represented versus a success, and how `PublishOverride`
is shaped. Use those exact members below.

- [ ] **Step 2: Write the failing tests**

Create `tests/Sorcha.McpServer.Tests/Tools/BlueprintPublishToolTests.cs`:

```csharp
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ModelContextProtocol.Server;
using Sorcha.McpServer.Services;
using Sorcha.McpServer.Tools.Designer;
using Sorcha.ServiceClients.Blueprint;
using Sorcha.ServiceClients.Blueprint.Models;

namespace Sorcha.McpServer.Tests.Tools;

public class BlueprintPublishToolTests
{
    [Fact]
    public async Task PublishAsync_FirstAttemptCarriesNoOverride()
    {
        // The agent must never pre-emptively waive the gate.
        var h = new Harness().WithPublishSuccess();

        await h.Sut().PublishBlueprintAsync(h.Server, "bp-1", "reg-1");

        h.Client.Verify(c => c.PublishBlueprintAsync(
            "bp-1",
            It.Is<PublishBlueprintRequest>(r => r.RegisterId == "reg-1" && r.Override == null),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PublishAsync_RehearsalRequiredAndUserApproves_RetriesWithOverride()
    {
        var h = new Harness().WithRehearsalRequiredThenSuccess().WithApproval(ApprovalOutcome.Approved);

        var result = await h.Sut().PublishBlueprintAsync(h.Server, "bp-1", "reg-1");

        result.Status.Should().Be("Success");
        result.PublishedWithoutRehearsal.Should().BeTrue();
        h.Client.Verify(c => c.PublishBlueprintAsync(
            "bp-1",
            It.Is<PublishBlueprintRequest>(r => r.Override != null),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(ApprovalOutcome.Refused)]
    [InlineData(ApprovalOutcome.NotSupported)]
    public async Task PublishAsync_RehearsalRequiredAndNotApproved_NeverOverrides(ApprovalOutcome outcome)
    {
        var h = new Harness().WithRehearsalRequiredThenSuccess().WithApproval(outcome);

        var result = await h.Sut().PublishBlueprintAsync(h.Server, "bp-1", "reg-1");

        result.Status.Should().BeOneOf("RehearsalRequired", "ApprovalRequired");
        h.Client.Verify(c => c.PublishBlueprintAsync(
            It.IsAny<string>(),
            It.Is<PublishBlueprintRequest>(r => r.Override != null),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task PublishAsync_RehearsalRequired_TellsThePersonWhatTheyAreWaiving()
    {
        var h = new Harness().WithRehearsalRequiredThenSuccess().WithApproval(ApprovalOutcome.Approved);

        await h.Sut().PublishBlueprintAsync(h.Server, "bp-1", "reg-1");

        h.Approval.Verify(a => a.RequestAsync(
            It.IsAny<McpServer>(),
            It.Is<HumanApprovalRequest>(r =>
                r.Message.Contains("not been rehearsed") && r.Message.Contains("bp-1")),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
```

- [ ] **Step 3: Run and confirm failure**

Run: `dotnet test --project tests/Sorcha.McpServer.Tests/Sorcha.McpServer.Tests.csproj --filter-class "*BlueprintPublishToolTests*"`
Expected: FAIL — `BlueprintPublishTool` does not exist.

- [ ] **Step 4: Implement**

Create `src/Apps/Sorcha.McpServer/Tools/Designer/BlueprintPublishTool.cs`:

```csharp
    /// <summary>Publishes a draft blueprint to a register (Go live).</summary>
    /// <param name="server">The live MCP server, injected by the SDK.</param>
    /// <param name="blueprintId">The draft blueprint to publish.</param>
    /// <param name="registerId">The register to publish it to.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The published version, or the rehearsal block if a person declined to waive it.</returns>
    [McpServerTool(Name = "sorcha_blueprint_publish", Destructive = true, ReadOnly = false, Idempotent = false)]
    [Description("Publishes a draft blueprint to a register so workflow instances can be started from it, returning the immutable published version number. Call this after sorcha_blueprint_create and before sorcha_instance_create. A blueprint that has not been rehearsed is blocked by a safety gate; when that happens this tool asks a person whether to publish anyway, and refuses if your MCP client cannot present that question. Publishing is recorded permanently on the register's ledger.")]
    public async Task<BlueprintPublishResult> PublishBlueprintAsync(
        McpServer server,
        [Description("The draft blueprint's ID")] string blueprintId,
        [Description("The register to publish to")] string registerId,
        CancellationToken cancellationToken = default)
```

The flow:

```csharp
        // 1. Attempt with NO override. The agent never pre-emptively waives the gate.
        var outcome = await _blueprintClient.PublishBlueprintAsync(
            blueprintId,
            new PublishBlueprintRequest { RegisterId = registerId },
            cancellationToken);

        if (outcome is null) { return Error("…"); }
        if (outcome is a success) { return Success(version, publishedWithoutRehearsal: false); }

        // 2. Blocked on the rehearsal soft gate. Ask a person, naming precisely what is waived.
        var approval = await _humanApproval.RequestAsync(server, new HumanApprovalRequest(
            $"Publish blueprint '{blueprintId}' to register '{registerId}' WITHOUT rehearsing it?\n\n" +
            "This blueprint version has not been rehearsed, so its routing, disclosure and " +
            "credential-issuance rules have never been executed. Publishing is recorded " +
            "permanently on the register's ledger.\n\n" +
            "The safer option is to cancel and rehearse it first in the Sorcha designer.",
            "Publish without rehearsing"), cancellationToken);

        if (approval.Outcome != ApprovalOutcome.Approved)
        {
            return new BlueprintPublishResult
            {
                Status = approval.Outcome == ApprovalOutcome.NotSupported ? "ApprovalRequired" : "RehearsalRequired",
                Message = approval.Outcome == ApprovalOutcome.NotSupported
                    ? approval.Detail
                    : "This blueprint has not been rehearsed and publishing anyway was not approved. " +
                      "Rehearse it in the Sorcha designer, then publish again.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        // 3. Retry with the audited override. ProceedWithOverride attributes it to the caller.
        var overridden = await _blueprintClient.PublishBlueprintAsync(
            blueprintId,
            new PublishBlueprintRequest
            {
                RegisterId = registerId,
                Override = new PublishOverride
                {
                    Confirm = true,
                    Reason = "Published via MCP with explicit user confirmation that the blueprint was not rehearsed."
                }
            },
            cancellationToken);
```

The result record carries `Status`, `Message`, `CheckedAt`, `ResponseTimeMs`, plus:

```csharp
    /// <summary>The immutable published version number, when the publish succeeded.</summary>
    public int? Version { get; init; }

    /// <summary>
    /// True when this version went live without a rehearsal pass, on an explicit human override.
    /// </summary>
    public bool PublishedWithoutRehearsal { get; init; }
```

- [ ] **Step 5: Add the entitlement**

```csharp
        new("sorcha_blueprint_publish", PlatformOnly, DesignerRole),
```

- [ ] **Step 6: Run everything and commit**

```bash
dotnet build
dotnet test --project tests/Sorcha.McpServer.Tests/Sorcha.McpServer.Tests.csproj
pwsh scripts/check-mcp-routes.ps1
pwsh scripts/check-mcp-response-shapes.ps1
```
Expected: all PASS.

```bash
git add src/Apps/Sorcha.McpServer/Tools/Designer/BlueprintPublishTool.cs \
        src/Apps/Sorcha.McpServer/Services/ToolEntitlement.cs \
        tests/Sorcha.McpServer.Tests/Tools/BlueprintPublishToolTests.cs
git commit -m "feat: [MCP-P1] add sorcha_blueprint_publish

Uses the existing (previously uncalled) PublishBlueprintAsync typed
method. Attempts with no override; on 409 REHEARSAL_REQUIRED it asks a
person, naming what is being waived, and only then retries with the
audited override. An agent can never waive the gate on its own."
```

---

### Task 7: Resources

Zero resources exist today; `WithToolsFromAssembly()` is the only registration. Tools are verbs, resources are the nouns. The blueprint schema is the one intervention the A/B measured as closing the authoring gap.

**Files:**
- Create: `src/Apps/Sorcha.McpServer/Resources/SorchaResources.cs`
- Create: `src/Apps/Sorcha.McpServer/Resources/LiveStateResources.cs`
- Modify: `src/Apps/Sorcha.McpServer/Sorcha.McpServer.csproj` (embed schema + examples)
- Modify: `src/Apps/Sorcha.McpServer/Program.cs` (`.WithResourcesFromAssembly()`)
- Test: `tests/Sorcha.McpServer.Tests/Resources/SorchaResourcesTests.cs`

**Interfaces:**
- Consumes: `ICallerContext` (for the caller-scoped live resources), `IBlueprintServiceClient`.
- Produces: nothing later tasks depend on.

- [ ] **Step 1: Embed the schema and examples**

In `Sorcha.McpServer.csproj`:

```xml
  <ItemGroup>
    <EmbeddedResource Include="..\..\Common\blueprint.schema.json" LogicalName="Sorcha.McpServer.Resources.blueprint.schema.json" />
    <EmbeddedResource Include="..\..\..\walkthroughs\AssuredIdentity\blueprints\assured-identity.json" LogicalName="Sorcha.McpServer.Resources.examples.assured-identity.json" />
    <EmbeddedResource Include="..\..\..\walkthroughs\EncryptionAtRest\blueprints\encryption-at-rest.json" LogicalName="Sorcha.McpServer.Resources.examples.encryption-at-rest.json" />
    <EmbeddedResource Include="..\..\..\walkthroughs\PingPongN1\blueprints\ping-pong.json" LogicalName="Sorcha.McpServer.Resources.examples.ping-pong.json" />
  </ItemGroup>
```

These are files the walkthrough suite actually executes, so a stale example shows up as a failing
walkthrough rather than a silent lie.

- [ ] **Step 2: Write the failing test**

Create `tests/Sorcha.McpServer.Tests/Resources/SorchaResourcesTests.cs`:

```csharp
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using FluentAssertions;
using Sorcha.McpServer.Resources;

namespace Sorcha.McpServer.Tests.Resources;

public class SorchaResourcesTests
{
    [Fact]
    public void BlueprintSchema_IsEmbeddedAndIsValidJsonSchema()
    {
        var schema = SorchaResources.GetBlueprintSchema();

        schema.Should().NotBeNullOrWhiteSpace();
        var doc = JsonDocument.Parse(schema);
        doc.RootElement.TryGetProperty("$schema", out _).Should().BeTrue();
    }

    [Theory]
    [InlineData("assured-identity")]
    [InlineData("encryption-at-rest")]
    [InlineData("ping-pong")]
    public void Example_IsEmbeddedAndParses(string name)
    {
        var example = SorchaResources.GetExample(name);

        example.Should().NotBeNullOrWhiteSpace();
        var doc = JsonDocument.Parse(example!);
        doc.RootElement.TryGetProperty("title", out _).Should().BeTrue();
        doc.RootElement.TryGetProperty("participants", out _).Should().BeTrue();
    }

    [Fact]
    public void GetExample_UnknownName_ReturnsNull()
    {
        SorchaResources.GetExample("no-such-example").Should().BeNull();
    }

    [Fact]
    public void Glossary_DistinguishesPublicationIdFromExecDefHash()
    {
        // These answer different questions and live in different value spaces (pattern 22).
        // Comparing one against the other yields a plausible-but-wrong answer rather than an
        // error, which is exactly how isPinnedToLatest shipped hard-wired to false.
        var glossary = SorchaResources.GetGlossary();

        glossary.Should().Contain("publicationTxId");
        glossary.Should().Contain("execDefHash");
    }
}
```

- [ ] **Step 3: Run and confirm failure**

Run: `dotnet test --project tests/Sorcha.McpServer.Tests/Sorcha.McpServer.Tests.csproj --filter-class "*SorchaResourcesTests*"`
Expected: FAIL — `SorchaResources` does not exist.

- [ ] **Step 4: Implement the static resources**

Create `src/Apps/Sorcha.McpServer/Resources/SorchaResources.cs` with `[McpServerResourceType]` on
the class and these members:

```csharp
    [McpServerResource(UriTemplate = "sorcha://schema/blueprint", Name = "Blueprint JSON Schema", MimeType = "application/schema+json")]
    [Description("The complete JSON Schema for a Sorcha blueprint: participants, actions, data schemas, disclosure groups, routing rules, and instanceReference. Read this before writing any blueprint JSON — it is the difference between real JSON and a plausible-looking guess.")]
    public static string BlueprintSchema() => GetBlueprintSchema();

    [McpServerResource(UriTemplate = "sorcha://examples/{name}", Name = "Example blueprint", MimeType = "application/json")]
    [Description("A complete, working blueprint that the Sorcha walkthrough suite actually executes. Available names: assured-identity (credential issuance with selective disclosure), encryption-at-rest (encrypted payloads and disclosure groups), ping-pong (the minimal two-party exchange).")]
    public static string? Example(string name) => GetExample(name);

    [McpServerResource(UriTemplate = "sorcha://glossary", Name = "Sorcha glossary", MimeType = "text/markdown")]
    [Description("What Sorcha's core terms mean: register, blueprint, action, participant, disclosure group, docket, publication id versus executable-definition hash.")]
    public static string Glossary() => GetGlossary();
```

plus the three `internal static` accessors the tests call, reading from
`Assembly.GetExecutingAssembly().GetManifestResourceStream(...)` by the `LogicalName` values above.
`GetExample` maps a short name to its logical name and returns null for anything unrecognised.

Write the glossary inline as a `const string` in Markdown. It must define, at minimum: register,
blueprint, action, participant, disclosure group, docket, wallet address, `publicationTxId`
(identifies a published definition) and `execDefHash` (the behavioural signature that decides
whether a rehearsal pass survives a republish) — stating plainly that these are different values
answering different questions and must never be compared against each other.

- [ ] **Step 5: Implement the live-state resources**

Create `src/Apps/Sorcha.McpServer/Resources/LiveStateResources.cs`, an instance
`[McpServerResourceType]` class taking `IBlueprintServiceClient` and `ICallerContext`:

```csharp
    [McpServerResource(UriTemplate = "sorcha://instances", Name = "Your workflow instances", MimeType = "application/json")]
    [Description("The workflow instances visible to you right now, as JSON. Read this instead of spending a tool call when you only need to see what is running.")]
    public async Task<string> InstancesAsync(CancellationToken cancellationToken)
```

which returns the raw body from `GetWorkflowInstancesAsync`, or `{"instances":[],"note":"…"}` when
the caller is unauthenticated or the service is unavailable. Add the equivalent
`sorcha://registers` reading the register list.

- [ ] **Step 6: Register resources**

In `Program.cs`, on the builder chain that already calls `.WithToolsFromAssembly()`, add
`.WithResourcesFromAssembly()`.

- [ ] **Step 7: Run and commit**

```bash
dotnet build
dotnet test --project tests/Sorcha.McpServer.Tests/Sorcha.McpServer.Tests.csproj
```
Expected: PASS.

```bash
git add src/Apps/Sorcha.McpServer/Resources/ \
        src/Apps/Sorcha.McpServer/Sorcha.McpServer.csproj \
        src/Apps/Sorcha.McpServer/Program.cs \
        tests/Sorcha.McpServer.Tests/Resources/
git commit -m "feat: [MCP-P1] serve MCP resources

The server was tools-only: zero resources, zero prompts. Serves the
blueprint JSON Schema (the one intervention the cold-start A/B measured
as closing the authoring gap), three worked examples the walkthrough
suite executes, a glossary, and caller-scoped live state."
```

---

### Task 8: Prompts and `ServerInstructions`

Two teaching surfaces. `ServerInstructions` currently spends eight lines restating role names, one of which (`sorcha:participant`) gates zero tools — the code can no longer produce the denial message that names it.

**Files:**
- Create: `src/Apps/Sorcha.McpServer/Prompts/SorchaPrompts.cs`
- Modify: `src/Apps/Sorcha.McpServer/Program.cs` (`ConfigureServerOptions`, `.WithPromptsFromAssembly()`)
- Test: `tests/Sorcha.McpServer.Tests/Prompts/SorchaPromptsTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: nothing.

- [ ] **Step 1: Write the failing test**

Create `tests/Sorcha.McpServer.Tests/Prompts/SorchaPromptsTests.cs`:

```csharp
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.McpServer.Prompts;

namespace Sorcha.McpServer.Tests.Prompts;

public class SorchaPromptsTests
{
    [Fact]
    public void TwoPartyExchange_NamesTheLifecycleInOrder()
    {
        var text = SorchaPrompts.TwoPartyExchange("Acme", "Beta", "invoice totals");

        text.Should().Contain("sorcha_register_create");
        text.Should().Contain("sorcha_blueprint_publish");
        text.Should().Contain("sorcha_instance_create");
        text.IndexOf("sorcha_register_create", StringComparison.Ordinal)
            .Should().BeLessThan(text.IndexOf("sorcha_instance_create", StringComparison.Ordinal));
    }

    [Fact]
    public void TwoPartyExchange_PointsAtTheSchemaResource()
    {
        SorchaPrompts.TwoPartyExchange("Acme", "Beta", "invoice totals")
            .Should().Contain("sorcha://schema/blueprint");
    }

    [Fact]
    public void EveryPrompt_WarnsThatSomeStepsNeedAHuman()
    {
        foreach (var text in new[]
                 {
                     SorchaPrompts.TwoPartyExchange("A", "B", "x"),
                     SorchaPrompts.IssueCredential("A", "B", "x"),
                     SorchaPrompts.ProveToRegulator("reg-1", "tx-1")
                 })
        {
            text.Should().Contain("confirm", Exactly.Once().Because("agents must expect the human gate"));
        }
    }
}
```

> If `Exactly.Once()` reads awkwardly for a substring assertion, use
> `text.ToLowerInvariant().Should().Contain("confirm")`.

- [ ] **Step 2: Run and confirm failure**

Run: `dotnet test --project tests/Sorcha.McpServer.Tests/Sorcha.McpServer.Tests.csproj --filter-class "*SorchaPromptsTests*"`
Expected: FAIL — `SorchaPrompts` does not exist.

- [ ] **Step 3: Implement the prompts**

Create `src/Apps/Sorcha.McpServer/Prompts/SorchaPrompts.cs` with `[McpServerPromptType]` and three
`[McpServerPrompt]` static methods returning strings. Each walks the lifecycle in order, names the
resources to read first, and says which steps need a person.

```csharp
    [McpServerPrompt(Name = "sorcha_two_party_exchange")]
    [Description("Set up a two-party data exchange with selective disclosure between two organisations.")]
    public static string TwoPartyExchange(
        [Description("The organisation that starts the exchange")] string firstParty,
        [Description("The organisation that responds")] string secondParty,
        [Description("What is being exchanged, e.g. 'invoice totals'")] string subject)
```

The body is a step-by-step brief naming `sorcha://schema/blueprint`, then
`sorcha_blueprint_create` → `sorcha_register_create` → `sorcha_blueprint_publish` →
`sorcha_instance_create` → `sorcha_action_submit`, and stating that register creation and
publishing an unrehearsed blueprint each require a person to confirm interactively.

Add `sorcha_issue_credential` and `sorcha_prove_to_regulator` in the same shape.

- [ ] **Step 4: Rewrite `ServerInstructions`**

In `Program.cs`, replace the `options.ServerInstructions` block:

```csharp
    options.ServerInstructions = """
        Sorcha MCP Server — a decentralised register platform for multi-party data flow with
        cryptographically enforced selective disclosure.

        THE LIFECYCLE, IN ORDER. Most tasks follow it end to end:
          1. sorcha_blueprint_create   — define the workflow (participants, actions, disclosure)
          2. sorcha_register_create    — create the ledger it runs on          [needs a human]
          3. sorcha_blueprint_publish  — publish the definition to that register [may need a human]
          4. sorcha_instance_create    — start a running instance
          5. sorcha_action_submit      — perform an action on that instance

        READ THESE FIRST — they are resources, not tool calls, so they cost you nothing:
          sorcha://schema/blueprint   the complete blueprint JSON Schema. Read it before writing
                                      any blueprint; it is the difference between real JSON and a
                                      plausible guess.
          sorcha://examples/{name}    working blueprints: assured-identity, encryption-at-rest,
                                      ping-pong.
          sorcha://glossary           what register, docket, disclosure group and the rest mean.
          sorcha://registers          the registers you can see right now.
          sorcha://instances          the workflow instances you can see right now.

        GUIDED RECIPES are available as prompts: sorcha_two_party_exchange,
        sorcha_issue_credential, sorcha_prove_to_regulator.

        SOME STEPS NEED A PERSON. Creating a register, and publishing a blueprint that has not
        been rehearsed, ask the human operating your client to confirm. This is deliberate: those
        acts are irreversible and establish governance. If your client does not support the MCP
        elicitation capability, those tools refuse rather than proceed unsupervised.

        WHAT YOU CAN SEE depends on your token's trust tier and roles; tools you are not entitled
        to use are not listed. If a tool reports an error, read the message — a missing required
        argument is reported as such and names the argument.
        """;
```

Note this removes the reference to `sorcha:participant`, which gates zero tools.

- [ ] **Step 5: Register prompts**

Add `.WithPromptsFromAssembly()` to the builder chain in `Program.cs`.

- [ ] **Step 6: Run and commit**

```bash
dotnet build
dotnet test --project tests/Sorcha.McpServer.Tests/Sorcha.McpServer.Tests.csproj
```
Expected: PASS.

```bash
git add src/Apps/Sorcha.McpServer/Prompts/ \
        src/Apps/Sorcha.McpServer/Program.cs \
        tests/Sorcha.McpServer.Tests/Prompts/
git commit -m "feat: [MCP-P1] add MCP prompts and rewrite ServerInstructions

Three guided recipes, and ServerInstructions becomes a map of the
lifecycle and the resources rather than eight lines restating role
names — one of which (sorcha:participant) gates zero tools."
```

---

### Task 9: Documentation sync

Mandatory per CLAUDE.md. PRs without documentation updates are not approved.

**Files:**
- Modify: `src/Apps/Sorcha.McpServer/README.md`
- Modify: `docs/reference/API-DOCUMENTATION.md`
- Modify: `.specify/MASTER-TASKS.md`
- Modify: `.claude/skills/sorcha-architecture/SKILL.md`
- Modify: `.claude/skills/n1-deploy/SKILL.md`

**Interfaces:**
- Consumes: everything above.
- Produces: nothing.

- [ ] **Step 1: MCP server README**

Add sections for the three lifecycle tools, the resources, the prompts, and the human-approval
model. State the three client states explicitly and that only `accept` approves.

- [ ] **Step 2: API documentation**

Add the three tools to the MCP tool tables in `docs/reference/API-DOCUMENTATION.md`, with their
backing endpoints.

- [ ] **Step 3: MASTER-TASKS**

Mark the P1 items ✅ and add the deferred follow-ups as 📋: rehearsal tools, a longer-lived
pending registration, P2 (schema enrichment, catalogue unification, OAuth protected-resource
metadata), P3 (tool-surface trimming).

- [ ] **Step 4: sorcha-architecture skill**

Add a short "MCP lifecycle tools + human approval" subsection recording: `IHumanApproval` is the
only place `ElicitAsync` is called; only `accept` approves; a client that cannot elicit is refused;
`createdVia` is an audit fact nothing may branch on.

- [ ] **Step 5: n1-deploy skill**

Extend "Verifying the MCP tool surface end-to-end" to note that the lifecycle tools cannot be
verified by a non-interactive probe — they correctly refuse with `cancel` — so a headless smoke
check must assert the *refusal*, not treat it as a failure.

- [ ] **Step 6: Commit**

```bash
git add src/Apps/Sorcha.McpServer/README.md \
        docs/reference/API-DOCUMENTATION.md \
        .specify/MASTER-TASKS.md \
        .claude/skills/sorcha-architecture/SKILL.md \
        .claude/skills/n1-deploy/SKILL.md
git commit -m "docs: [MCP-P1] document the lifecycle tools, resources, prompts and approval model"
```

---

### Task 10: Deploy and validate

The plan's claims are not proven until they run on a node. P0's lesson: `initialize` and
`tools/list` both succeed against a wholly dead surface, and `sorcha_health_check` passes against
it too.

- [ ] **Step 1: Merge and wait for the image**

```bash
gh pr create --fill
gh pr merge --squash
RUN_ID=$(gh run list --limit 10 --json databaseId,name --jq '.[] | select(.name | contains("Docker Publish")) | .databaseId' | head -1)
gh run watch $RUN_ID --exit-status
```

- [ ] **Step 2: Deploy to n1**

```bash
C="docker compose -f docker-compose.yml -f docker-compose.n1.yml -f docker-compose.ports.yml"
ssh sorcha@51.105.7.135 "cd /opt/sorcha && $C pull mcp-server-http && $C up -d --force-recreate --no-deps mcp-server-http"
ssh sorcha@51.105.7.135 'docker ps --filter name=sorcha-mcp-server-http --format "{{.Status}}|{{.Image}}"'
```
Expected: image `sorchadev/mcp-server:latest`, uptime reset. Compose progress lines are swallowed
over non-interactive SSH, so the `ps` line is the proof.

- [ ] **Step 3: Verify the surface still works from more than one tier**

Follow "Verifying the MCP tool surface end-to-end" in the `n1-deploy` skill. Confirm
`resources/list` and `prompts/list` now return non-empty, and that `sorcha_tenant_list` reports a
non-zero tenant count.

- [ ] **Step 4: Verify the lifecycle tools refuse headlessly**

```bash
call '{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"sorcha_register_create","arguments":{"name":"Probe","description":"Headless refusal probe"}}}'
```
Expected: a refusal naming the elicitation requirement, and **no register created**. Confirm with
`sorcha://registers` or the register list. A success here is a **failure of the design**.

- [ ] **Step 5: Re-run the cold-start experiment**

A fresh agent with no Sorcha knowledge, given only `llms.txt` and the tool catalogue, asked to set
up a two-party data exchange with selective disclosure and a regulator-facing record. Baseline is
**3/10**, hard stop at "no way to create a register or start an instance".

The run must be **interactive** so a person can answer the confirmations — a headless run is
correctly refused. Record: whether it reaches a running instance, how many confirmations it needed,
where it hesitated, and whether it read the resources unprompted.

- [ ] **Step 6: Record the outcome**

Append a "Measured outcome" section to
`docs/superpowers/specs/2026-09-06-mcp-p1-completable-surface-design.md` with the new score and
what still blocks it. Commit.

---

## Self-Review

**Spec coverage:**

| Spec section | Task |
|---|---|
| Human sign-off model (three states, accept-only) | 1 |
| `sorcha_register_create` (composite, fixed path, fixed wallet, provenance) | 5 |
| `sorcha_blueprint_publish` (no silent override) | 6 |
| `sorcha_instance_create` (+ typed client method) | 4 |
| Annotations, lifecycle tools only | 4, 5, 6 (on the attributes) |
| Resources (schema, examples, glossary, live state) | 7 |
| Prompts | 8 |
| `ServerInstructions` | 8 |
| `sorcha_tenant_list` + argument-error legibility | 2 |
| Response-shape gate | 3 |
| Testing (all bullets) | 1-8 |
| Validation | 10 |
| Documentation sync | 9 |

**Type consistency:** `IHumanApproval.RequestAsync(McpServer, HumanApprovalRequest, CancellationToken)`
is used identically in Tasks 5 and 6. `ApprovalOutcome.{Approved,Refused,NotSupported}` is used
consistently. `IBlueprintServiceClient.CreateInstanceAsync` is defined in Task 4 and referenced
nowhere later. `SorchaDerivationPaths.RegisterAttestation` matches the constant verified to exist
at `SorchaDerivationPaths.cs:81`.

**Known soft spots, flagged rather than hidden:**
- Task 1 Step 2's fake depends on `McpServer`'s abstract surface, which reflection did not fully
  enumerate. The fallback (`IElicitationTransport`) is specified inline.
- Task 2 Step 5's error-mapping hook name may differ in SDK 2.2.0; the *behavioural* requirement
  and its test are what must hold.
- Task 5 Step 1 requires locating the existing org-wallet lookup rather than assuming a client.
