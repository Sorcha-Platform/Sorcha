// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Sorcha.McpServer.Infrastructure;
using Sorcha.ServiceDefaults.Auth;

namespace Sorcha.McpServer.Tests.Integration;

/// <summary>
/// Live proof (spec 139) that the foundational token-forwarding spine works end-to-end
/// against the running gateway: a tool's outbound call carries the caller's bearer (so the
/// platform authorizes it), and an absent token is rejected by the platform — the exact
/// behaviour the pre-139 anonymous-call defect lacked. Docker-gated.
/// </summary>
[Trait("Category", "McpIntegration")]
public class TokenForwardingIntegrationTests : McpIntegrationTestBase
{
    // A representative authenticated, cross-tier gateway endpoint: 200 with a valid token, 401 anonymously.
    private const string AuthedEndpoint = "/api/auth/me";

    private static HttpClient BuildForwardingClient(string? token)
    {
        var caller = new StubCallerContext(token);
        var handler = new CallerTokenForwardingHandler(caller, NullLogger<CallerTokenForwardingHandler>.Instance)
        {
            InnerHandler = new HttpClientHandler()
        };
        return new HttpClient(handler) { BaseAddress = new Uri(GatewayUrl), Timeout = TimeSpan.FromSeconds(10) };
    }

    [Fact]
    public async Task ForwardingHandler_WithValidToken_ReachesAuthenticatedEndpoint()
    {
        await EnsureGatewayOrSkipAsync();
        var token = await GetPlatformAdminTokenAsync();

        using var client = BuildForwardingClient(token);
        using var response = await client.GetAsync(AuthedEndpoint);

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "the forwarding handler must stamp the caller's bearer so the gateway authorizes the call");
    }

    [Fact]
    public async Task ForwardingHandler_WithoutToken_IsRejectedByGateway()
    {
        await EnsureGatewayOrSkipAsync();

        using var client = BuildForwardingClient(token: null);
        using var response = await client.GetAsync(AuthedEndpoint);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "an absent caller token must be rejected by the platform — proving the 200 above is real enforcement, not an open endpoint");
    }

    /// <summary>Minimal <see cref="ICallerContext"/> carrying a fixed raw token for the handler under test.</summary>
    private sealed class StubCallerContext(string? token) : ICallerContext
    {
        public string? RawToken => token;
        public Tier? Tier => ServiceDefaults.Auth.Tier.Platform;
        public IReadOnlyCollection<string> Roles => [];
        public string? OrganizationId => null;
        public string? Subject => "integration-test";
        public bool IsAuthenticated => token is not null;
    }
}
