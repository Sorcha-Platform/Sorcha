// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net.Http.Json;
using System.Text.Json;

namespace Sorcha.McpServer.Tests.Integration;

/// <summary>
/// Base for live MCP integration tests (spec 139). These exercise the foundation against a
/// running Sorcha stack reached through the API Gateway, so they catch the two defect classes
/// mocked unit tests cannot — a missing forwarded token (401) and endpoint drift (404).
/// <para>
/// Docker-gated: when the gateway is not reachable the test is skipped, matching the repo's
/// other infra-dependent suites. Run with the stack up via <c>docker-compose up -d</c>.
/// </para>
/// </summary>
public abstract class McpIntegrationTestBase
{
    /// <summary>Gateway base URL — override with <c>SORCHA_GATEWAY_URL</c>; defaults to the local stack.</summary>
    protected static string GatewayUrl =>
        Environment.GetEnvironmentVariable("SORCHA_GATEWAY_URL")?.TrimEnd('/')
        ?? "http://localhost:80";

    private static readonly HttpClient ProbeClient = new() { Timeout = TimeSpan.FromSeconds(5) };

    /// <summary>
    /// Skips the calling test (xUnit dynamic skip) when the gateway is not reachable, so the
    /// suite is a no-op on machines without the Docker stack rather than a failure.
    /// </summary>
    protected static async Task EnsureGatewayOrSkipAsync()
    {
        try
        {
            using var response = await ProbeClient.GetAsync($"{GatewayUrl}/health");
            if (!response.IsSuccessStatusCode)
            {
                Assert.Skip($"Gateway at {GatewayUrl} returned {(int)response.StatusCode}; skipping live MCP integration test.");
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            Assert.Skip($"Gateway at {GatewayUrl} not reachable ({ex.GetType().Name}); skipping live MCP integration test.");
        }
    }

    /// <summary>
    /// Logs in via the gateway and returns a real, gateway-accepted platform-admin access token.
    /// Credentials come from <c>SORCHA_ADMIN_EMAIL</c>/<c>SORCHA_ADMIN_PASSWORD</c> or the dev defaults.
    /// </summary>
    protected static async Task<string> GetPlatformAdminTokenAsync()
    {
        var email = Environment.GetEnvironmentVariable("SORCHA_ADMIN_EMAIL") ?? "admin@sorcha.local";
        var password = Environment.GetEnvironmentVariable("SORCHA_ADMIN_PASSWORD") ?? "Dev_Pass_2025!";

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        using var response = await client.PostAsJsonAsync(
            $"{GatewayUrl}/api/auth/login",
            new { email, password });

        if (!response.IsSuccessStatusCode)
        {
            Assert.Skip($"Admin login returned {(int)response.StatusCode}; stack may not be bootstrapped. Skipping.");
        }

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("access_token", out var tokenElement))
        {
            Assert.Skip("Login response carried no access_token (org-selection or 2FA path); skipping.");
        }

        return tokenElement.GetString()!;
    }
}
