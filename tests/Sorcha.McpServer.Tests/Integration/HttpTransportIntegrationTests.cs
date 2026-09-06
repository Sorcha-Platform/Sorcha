// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

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

    // A tools/call for sorcha_health_check — the cheapest admin tool to invoke end-to-end. This is
    // the request that proves a tool actually DISPATCHES, not just that the transport authenticates.
    // Before the P0 fixes, this exact call returned HTTP 200 with
    // {"result":{"content":[{"type":"text","text":"An error occurred invoking 'sorcha_health_check'."}],"isError":true}}
    // for six days without CI noticing, because nothing asserted on the JSON-RPC payload.
    private const string HealthCheckToolCallBody = """
        {"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"sorcha_health_check","arguments":{}}}
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

    private static HttpRequestMessage BuildHealthCheckToolCall(string? token)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, McpEndpoint)
        {
            Content = new StringContent(HealthCheckToolCallBody, Encoding.UTF8, "application/json")
        };
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("text/event-stream");
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return request;
    }

    /// <summary>
    /// Skip guard specific to the tool-dispatch test. Deliberately does NOT delegate to
    /// <see cref="EnsureGatewayOrSkipAsync"/>/<see cref="EnsureMcpEndpointOrSkipAsync"/> so that a
    /// skipped run names exactly what was skipped ("tools/call verification for
    /// sorcha_health_check") rather than the generic "skipping live MCP integration test" message
    /// those helpers share across every test in this class — a CI run where every MCP test skipped
    /// must not read like a passing tool-dispatch check.
    /// </summary>
    private static async Task EnsureHealthCheckToolCallReadyOrSkipAsync()
    {
        using var healthClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        try
        {
            using var health = await healthClient.GetAsync($"{GatewayUrl}/health");
            if (!health.IsSuccessStatusCode)
            {
                Assert.Skip(
                    $"SKIPPED tools/call verification for sorcha_health_check: gateway at {GatewayUrl} " +
                    $"returned {(int)health.StatusCode} from /health. This test did NOT prove the tool " +
                    "surface is alive.");
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            Assert.Skip(
                $"SKIPPED tools/call verification for sorcha_health_check: gateway at {GatewayUrl} not " +
                $"reachable ({ex.GetType().Name}). This test did NOT prove the tool surface is alive.");
        }

        using var mcpClient = new HttpClient { BaseAddress = new Uri(GatewayUrl), Timeout = TimeSpan.FromSeconds(5) };
        try
        {
            using var probe = await mcpClient.SendAsync(BuildInitialize(token: null));
            if (probe.StatusCode == HttpStatusCode.NotFound)
            {
                Assert.Skip(
                    $"SKIPPED tools/call verification for sorcha_health_check: {GatewayUrl}{McpEndpoint} " +
                    "returned 404 (HTTP-mode mcp-server not deployed). This test did NOT prove the tool " +
                    "surface is alive.");
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            Assert.Skip(
                $"SKIPPED tools/call verification for sorcha_health_check: {GatewayUrl}{McpEndpoint} not " +
                $"reachable ({ex.GetType().Name}). This test did NOT prove the tool surface is alive.");
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

    /// <summary>
    /// Live proof that a tool actually DISPATCHES, not merely that the transport authenticates.
    /// This is the exact gap that let a completely dead tool surface ship for six days unnoticed:
    /// every prior test in this class stopped at <c>initialize</c>, and a dead surface still
    /// returns HTTP 200 for <c>tools/call</c> — the failure is encoded in the JSON-RPC payload
    /// (<c>result.isError == true</c>), not the status code. Asserting on status alone here would
    /// have passed against the dead surface, which is precisely why it must not.
    /// </summary>
    [Fact]
    public async Task McpHttp_ToolsCall_SorchaHealthCheck_DispatchesAndReturnsNonErrorResult()
    {
        await EnsureHealthCheckToolCallReadyOrSkipAsync();
        var token = await GetPlatformAdminTokenAsync();

        using var client = new HttpClient { BaseAddress = new Uri(GatewayUrl), Timeout = TimeSpan.FromSeconds(20) };
        using var response = await client.SendAsync(BuildHealthCheckToolCall(token));

        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized,
            "a valid platform bearer must pass the auth gate before tool dispatch is even attempted");
        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden,
            "a valid platform bearer holding the admin role is entitled to call sorcha_health_check");

        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        root.TryGetProperty("error", out var rpcError).Should().BeFalse(
            $"a JSON-RPC-level error means tools/call itself failed before the tool could run: {rpcError}");

        var result = root.GetProperty("result");
        var isError = result.TryGetProperty("isError", out var isErrorElement)
            && isErrorElement.ValueKind == JsonValueKind.True;

        isError.Should().BeFalse(
            "tools/call for sorcha_health_check must not report isError: a dead tool surface answers " +
            "HTTP 200 with isError:true and content \"An error occurred invoking 'sorcha_health_check'.\" " +
            $"— exactly the six-day-undetected failure this test exists to catch. Full response body: {body}");
    }
}
