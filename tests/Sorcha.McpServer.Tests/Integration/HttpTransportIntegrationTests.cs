// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace Sorcha.McpServer.Tests.Integration;

/// <summary>
/// Live proof (spec 139 US3) that the Streamable HTTP transport is a protected resource: an
/// unauthenticated MCP request is rejected before any tool dispatches, and a request carrying a
/// valid platform bearer is accepted by the auth layer (the MCP handler processes the JSON-RPC).
/// <para>
/// Reached through the gateway <c>/mcp</c> route. Docker-gated: skips when the gateway or the
/// HTTP-mode mcp-server is not running (matching the repo's other infra-dependent suites).
/// </para>
/// </summary>
[Trait("Category", "McpIntegration")]
public class HttpTransportIntegrationTests : McpIntegrationTestBase
{
    private const string McpEndpoint = "/mcp";

    // A minimal MCP initialize JSON-RPC request — enough to drive the transport past the auth gate.
    private const string InitializeBody = """
        {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"sorcha-it","version":"1.0.0"}}}
        """;

    private static HttpRequestMessage BuildInitialize(string? token)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, McpEndpoint)
        {
            Content = new StringContent(InitializeBody, Encoding.UTF8, "application/json")
        };
        // Streamable HTTP requires the client to accept both JSON and the SSE stream.
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("text/event-stream");
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return request;
    }

    private static async Task EnsureMcpEndpointOrSkipAsync()
    {
        using var client = new HttpClient { BaseAddress = new Uri(GatewayUrl), Timeout = TimeSpan.FromSeconds(5) };
        try
        {
            using var probe = await client.SendAsync(BuildInitialize(token: null));
            // 404 means the gateway /mcp route or the HTTP mcp-server isn't deployed yet.
            if (probe.StatusCode == HttpStatusCode.NotFound)
            {
                Assert.Skip($"{GatewayUrl}{McpEndpoint} returned 404; HTTP-mode mcp-server not deployed. Skipping.");
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            Assert.Skip($"{GatewayUrl}{McpEndpoint} not reachable ({ex.GetType().Name}); skipping live HTTP transport test.");
        }
    }

    [Fact]
    public async Task McpHttp_WithoutBearer_IsRejectedBeforeDispatch()
    {
        await EnsureGatewayOrSkipAsync();
        await EnsureMcpEndpointOrSkipAsync();

        using var client = new HttpClient { BaseAddress = new Uri(GatewayUrl), Timeout = TimeSpan.FromSeconds(10) };
        using var response = await client.SendAsync(BuildInitialize(token: null));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "the MCP HTTP endpoint is protected by RequireAuthorization — an absent bearer must be refused before any tool dispatches");
    }

    [Fact]
    public async Task McpHttp_WithValidPlatformBearer_PassesAuthGate()
    {
        await EnsureGatewayOrSkipAsync();
        await EnsureMcpEndpointOrSkipAsync();
        var token = await GetPlatformAdminTokenAsync();

        using var client = new HttpClient { BaseAddress = new Uri(GatewayUrl), Timeout = TimeSpan.FromSeconds(15) };
        using var response = await client.SendAsync(BuildInitialize(token));

        // The auth layer accepted the bearer (no 401/403); the MCP handler then processes the
        // JSON-RPC initialize and returns a success status (200 / 202 / SSE), never an auth refusal.
        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized,
            "a valid platform bearer must pass the auth gate");
        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden,
            "a valid platform bearer is an entitled MCP caller");
        ((int)response.StatusCode).Should().BeLessThan(500,
            "the MCP handler should process the initialize request, not fault");
    }
}
