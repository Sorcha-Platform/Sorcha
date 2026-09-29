// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;

using FluentAssertions;

using Xunit;

namespace Sorcha.ApiGateway.Tests.Routing;

/// <summary>
/// #1759 — a status list is fetched by verifiers who hold no Sorcha token: external wallets, other
/// installations, and Sorcha's own services calling the public URL a credential carries. Each public
/// status-list path therefore needs an ANONYMOUS GET route that wins over the authenticated catch-all
/// beneath it. The IETF path had none: it fell into <c>blueprint-credentials</c>
/// (<c>RequireAuthenticated</c>), so every anonymous fetch got 401 — found live, when the first
/// org-signed list could not be read by the Blueprint Service's own status checker.
/// </summary>
public class StatusListRouteTests
{
    public static TheoryData<string, string, string> PublicStatusListPaths => new()
    {
        // path prefix                                   owning cluster        catch-all it must beat
        { "/api/v1/credentials/status-lists/",      "blueprint-cluster", "blueprint-credentials" },
        { "/api/v1/credentials/ietf-status-lists/", "blueprint-cluster", "blueprint-credentials" },
        { "/api/v1/wallet/status/",                 "wallet-cluster",    "wallet-citizen" },
    };

    [Theory]
    [MemberData(nameof(PublicStatusListPaths))]
    public void PublicStatusList_HasAnAnonymousGetRouteAheadOfTheAuthenticatedCatchAll(
        string prefix, string cluster, string catchAll)
    {
        var routes = Routes();
        var route = routes.EnumerateObject().SingleOrDefault(r =>
            r.Value.GetProperty("Match").GetProperty("Path").GetString() == prefix + "{**catch-all}");

        route.Value.ValueKind.Should().Be(JsonValueKind.Object,
            $"{prefix}** needs its own route, or it falls into '{catchAll}' and anonymous verifiers get 401");
        route.Value.GetProperty("ClusterId").GetString().Should().Be(cluster);
        route.Value.TryGetProperty("AuthorizationPolicy", out _).Should().BeFalse(
            "a status list is fetched by verifiers with no Sorcha token; its authenticity is its signature");
        Order(route.Value).Should().BeLessThan(Order(routes.GetProperty(catchAll)),
            $"the public route must be matched before '{catchAll}', which requires authentication");
    }

    private static int Order(JsonElement route) =>
        route.TryGetProperty("Order", out var o) ? o.GetInt32() : 0;

    private static JsonElement Routes()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        File.Exists(path).Should().BeTrue("the gateway configuration must be present in the test output");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        return doc.RootElement.GetProperty("ReverseProxy").GetProperty("Routes").Clone();
    }
}
