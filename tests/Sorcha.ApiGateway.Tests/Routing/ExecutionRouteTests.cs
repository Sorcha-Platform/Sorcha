// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;

using FluentAssertions;

using Xunit;

namespace Sorcha.ApiGateway.Tests.Routing;

/// <summary>
/// Guards the gateway route for the Blueprint Service's execution helpers (#1633):
/// <c>POST /api/execution/validate</c>, <c>/calculate</c>, <c>/route</c> and <c>/disclose</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Without this route the MCP server's whole rehearsal toolset is dead on every node.</b> The
/// <c>mcp-server-http</c> container reaches the Blueprint Service THROUGH the gateway (every
/// <c>ServiceClients__*__Address</c> is <c>http://api-gateway:8080</c>), so
/// <c>sorcha_blueprint_validate</c>, <c>sorcha_action_validate</c>, <c>sorcha_blueprint_simulate</c>
/// and <c>sorcha_disclosure_analysis</c> all received a bodiless 404 from the <c>ui-static</c>
/// catch-all, while the same paths straight at <c>blueprint-service:8080</c> answered 401 (mapped,
/// wanting a token). A cold-start agent found it on n1 (2026-09-15) when it could not rehearse a
/// blueprint before publishing.
/// </para>
/// <para>
/// <c>check-mcp-routes</c> could not see it: it verifies that the owning SERVICE maps the route, which
/// it does. Nothing verified that the gateway the MCP server is configured to use routes it. Same class
/// as #1309 (<c>/api/presentations</c>) and F188 (<c>/api/provenance</c>).
/// </para>
/// </remarks>
public class ExecutionRouteTests
{
    private static JsonElement Routes()
    {
        // The gateway's appsettings.json is copied to the test output alongside the assembly.
        var path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        File.Exists(path).Should().BeTrue(
            "the gateway configuration must be present in the test output for this guard to mean anything");

        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        return doc.RootElement.GetProperty("ReverseProxy").GetProperty("Routes").Clone();
    }

    [Fact]
    public void ExecutionApi_IsRoutedToTheBlueprintCluster()
    {
        var routes = Routes();

        routes.TryGetProperty("blueprint-execution", out var route).Should().BeTrue(
            "/api/execution/** must have its own gateway route. Without one it falls through to the " +
            "ui-static catch-all, and every MCP rehearsal tool (validate, simulate, disclosure analysis) " +
            "gets a bodiless 404 — #1633.");

        route.GetProperty("ClusterId").GetString().Should().Be("blueprint-cluster",
            "the execution helpers are mapped by the Blueprint Service");

        route.GetProperty("Match").GetProperty("Path").GetString()
            .Should().Be("/api/execution/{**catch-all}",
                "validate, calculate, route and disclose all live under this prefix");
    }

    /// <summary>
    /// The helpers require an authenticated caller at the service (they answer 401 without a token),
    /// and every sibling Blueprint route carries the same edge policy. An anonymous edge route would
    /// be the only unauthenticated door into a Blueprint Service surface that evaluates
    /// caller-supplied data against a definition.
    /// </summary>
    [Fact]
    public void ExecutionApi_RequiresAnAuthenticatedCallerAtTheEdge()
    {
        var route = Routes().GetProperty("blueprint-execution");

        route.TryGetProperty("AuthorizationPolicy", out var policy).Should().BeTrue();
        policy.GetString().Should().Be("RequireAuthenticated");
    }
}
