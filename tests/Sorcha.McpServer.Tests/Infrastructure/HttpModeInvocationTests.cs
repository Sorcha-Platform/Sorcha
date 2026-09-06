// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sorcha.McpServer.Infrastructure;
using Sorcha.McpServer.Tools.Admin;
using Sorcha.McpServer.Tools.Participant;
using Sorcha.ServiceDefaults.Auth;

namespace Sorcha.McpServer.Tests.Infrastructure;

/// <summary>
/// Activation is not invocation. <see cref="HttpModeActivationTests"/> proves every tool can be
/// CONSTRUCTED from the production-shaped container; it says nothing about whether calling one
/// reaches the network — and for six days it did not.
/// <para>
/// The MCP server deliberately holds no <c>ServiceAuth:*</c> credentials (it forwards the caller's
/// bearer). Every typed client calls <c>ServiceClientAuthHelper.SetAuthHeaderAsync</c> before every
/// request, which called <c>GetTokenAsync</c> unconditionally; with an empty token cache that
/// reached <c>RequireClientId()</c> and threw, and each client's own <c>catch (Exception)</c>
/// swallowed the throw into a null return. The tool then reported a generic failure having never
/// opened a socket — indistinguishable, to an agent, from a backend outage.
/// </para>
/// <para>
/// These tests point the typed clients at an unroutable address and require the tool to report a
/// TRANSPORT failure. A credential failure or a silent empty success fails the assertion, so the
/// test is red against the pre-fix code and cannot pass by construction. No Docker, no live node.
/// </para>
/// </summary>
public class HttpModeInvocationTests
{
    // Port 1 on loopback: nothing listens, so a genuine connection attempt fails fast and
    // deterministically. Reaching this address at all is the proof the request was actually made.
    private const string UnroutableAddress = "http://127.0.0.1:1";

    /// <summary>
    /// A caller the advisory tier/role gate accepts. The gate is not what these tests exercise —
    /// without a caller every tool short-circuits to "Unauthorized" and the probe would be vacuous.
    /// Everything else in the container is exactly what <c>Program.cs</c>'s HTTP branch builds.
    /// </summary>
    private sealed class StubCallerContext(Tier tier, params string[] roles) : ICallerContext
    {
        public string? RawToken => "caller-supplied-bearer";

        public Tier? Tier => tier;

        public IReadOnlyCollection<string> Roles => roles;

        public string? OrganizationId => "11111111-1111-1111-1111-111111111111";

        public string? Subject => "test-subject";

        public bool IsAuthenticated => true;
    }

    private static ServiceProvider BuildHttpModeProvider(ICallerContext caller)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["JwtSettings:InstallationName"] = "test",
                // NO ServiceAuth:* keys — the production shape. Adding them here would hide the
                // very defect this test exists to catch.
                ["ServiceClients:BlueprintService:Address"] = UnroutableAddress,
                ["ServiceClients:RegisterService:Address"] = UnroutableAddress,
                ["ServiceClients:WalletService:Address"] = UnroutableAddress,
                ["ServiceClients:TenantService:Address"] = UnroutableAddress,
                ["ServiceClients:ValidatorService:Address"] = UnroutableAddress,
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        McpServerHttpRegistration.ConfigureServices(services, configuration);

        // Last registration wins: swap only the ambient identity.
        services.AddSingleton(caller);

        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task ConsumerTool_OnATypedClient_ActuallyIssuesTheRequest()
    {
        using var provider = BuildHttpModeProvider(new StubCallerContext(Tier.Consumer));
        var tool = ActivatorUtilities.CreateInstance<InboxListTool>(provider);

        var result = await tool.ListInboxAsync(cancellationToken: TestContext.Current.CancellationToken);

        result.Status.Should().Be("Error",
            "nothing is listening on the unroutable address, so the call must fail — but it must fail AT THE NETWORK");
        result.Message.Should().StartWith(
            "Failed to connect to blueprint service",
            "the tool must report a transport failure. 'Failed to retrieve inbox items.' means the "
            + "IServiceAuthClient throw was swallowed into a null return and NO REQUEST WAS EVER MADE — "
            + "the defect that left ~50 of 64 tools dead behind a green suite");
    }

    [Fact]
    public async Task PlatformTool_OnADifferentTypedClient_ActuallyIssuesTheRequest()
    {
        using var provider = BuildHttpModeProvider(
            new StubCallerContext(Tier.Platform, "sorcha:admin"));
        var tool = ActivatorUtilities.CreateInstance<UserListTool>(provider);

        var result = await tool.ListUsersAsync(
            organizationId: "11111111-1111-1111-1111-111111111111",
            cancellationToken: TestContext.Current.CancellationToken);

        result.Status.Should().Be("Error");
        result.Message.Should().StartWith(
            "Failed to connect to Tenant service",
            "a second typed client (Tenant, not Blueprint) must reach the network too — the "
            + "credential path is shared by every typed client, so one tool passing is not evidence");
    }

    [Fact]
    public async Task ATooOptimisticGate_IsNotWhatMakesThesePass()
    {
        // Counterfactual: with no caller identity the tools deny before reaching any client, so a
        // green result above cannot be an artefact of the tier gate letting everything through.
        using var provider = BuildHttpModeProvider(new StubCallerContext(Tier.Service));
        var tool = ActivatorUtilities.CreateInstance<InboxListTool>(provider);

        var result = await tool.ListInboxAsync(cancellationToken: TestContext.Current.CancellationToken);

        result.Status.Should().Be("Unauthorized");
    }
}
