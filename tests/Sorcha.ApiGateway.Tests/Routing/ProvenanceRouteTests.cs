// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;

using FluentAssertions;

using Xunit;

namespace Sorcha.ApiGateway.Tests.Routing;

/// <summary>
/// Guards the gateway route for Feature 188's provenance surfaces.
/// </summary>
/// <remarks>
/// <para>
/// <b>A new <c>/api/</c> path prefix without a route is invisible, not broken.</b> The gateway ends
/// with a <c>ui-static</c> route matching <c>/{**catch-all}</c>, so an unrouted API path does not
/// 404 at the edge — it is served the SPA's static fallback. Every request appears to succeed and
/// returns HTML, which a client then fails to deserialise somewhere far away from the cause.
/// </para>
/// <para>
/// This is not hypothetical. The whole of Feature 111's presentation surface — request object,
/// direct_post callback, status poll, disclosed claims — was unreachable through the gateway for
/// exactly this reason until #1309, and the symptom was a bodiless 404 that looked like an
/// application bug.
/// </para>
/// <para>
/// The check reads the shipped <c>appsettings.json</c> rather than a copy, so it fails if the route
/// is removed, renamed, or pointed at the wrong cluster.
/// </para>
/// </remarks>
public class ProvenanceRouteTests
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
    public void ProvenanceApi_IsRoutedToTheRegisterCluster()
    {
        var routes = Routes();

        routes.TryGetProperty("provenance-api", out var route).Should().BeTrue(
            "/api/provenance/** must have its own gateway route. Without one it falls through to " +
            "the ui-static catch-all and the entire surface is silently unreachable — the F111 " +
            "failure (#1309), where the symptom looks like an application bug, not a routing gap.");

        route.GetProperty("ClusterId").GetString().Should().Be("register-cluster",
            "the provenance endpoints are owned by the Register Service, which holds the evidence");

        route.GetProperty("Match").GetProperty("Path").GetString()
            .Should().Be("/api/provenance/{**catch-all}",
                "both the spine and the per-docket trail live under this prefix");
    }

    /// <summary>
    /// The route deliberately carries no edge authorization policy: the endpoints compose
    /// <c>RequireAdministrator</c> with <c>RequirePlatformAudience</c> themselves. An edge policy
    /// would duplicate that at best and contradict it at worst.
    /// </summary>
    [Fact]
    public void ProvenanceApi_LeavesAuthorizationToTheEndpoints()
    {
        var route = Routes().GetProperty("provenance-api");

        route.TryGetProperty("AuthorizationPolicy", out _).Should().BeFalse(
            "authorization for these endpoints is the tier gate composed on the role gate, declared " +
            "at the endpoints (CLAUDE.md pattern #13), not duplicated at the edge");
    }

    /// <summary>
    /// <b>Every</b> key under <c>Routes</c> must be a route YARP can load.
    /// </summary>
    /// <remarks>
    /// <para>
    /// YARP treats every key under <c>Routes</c> as a route, with no notion of a comment. A
    /// documentation key added there — the obvious way to leave a note next to a route — makes the
    /// whole proxy configuration invalid, and the gateway does not degrade: it throws at startup
    /// (<c>Route 'x' requires Hosts or Path specified</c>) and crash-loops, taking the entire edge
    /// down. Every service behind it becomes unreachable.
    /// </para>
    /// <para>
    /// This happened during the Feature 188 deploy and cost an outage. The route-specific tests above
    /// all still passed, because they only checked the route they cared about — which is exactly why
    /// this whole-file check exists. Notes belong in a route's <c>Metadata</c>, which YARP carries
    /// without interpreting.
    /// </para>
    /// </remarks>
    [Fact]
    public void EveryRoute_HasAMatchWithPathOrHosts_SoTheProxyConfigLoads()
    {
        var invalid = new List<string>();

        foreach (var route in Routes().EnumerateObject())
        {
            if (route.Value.ValueKind != JsonValueKind.Object)
            {
                invalid.Add($"{route.Name} (not an object — a comment or stray value?)");
                continue;
            }

            if (!route.Value.TryGetProperty("Match", out var match))
            {
                invalid.Add($"{route.Name} (no Match)");
                continue;
            }

            var hasPath = match.TryGetProperty("Path", out var path)
                          && !string.IsNullOrWhiteSpace(path.GetString());
            var hasHosts = match.TryGetProperty("Hosts", out var hosts)
                           && hosts.ValueKind == JsonValueKind.Array
                           && hosts.GetArrayLength() > 0;

            if (!hasPath && !hasHosts)
            {
                invalid.Add($"{route.Name} (Match has neither Path nor Hosts)");
            }
        }

        invalid.Should().BeEmpty(
            "YARP rejects the ENTIRE proxy configuration if any route lacks Hosts or Path, and the " +
            "gateway then crash-loops at startup and takes the whole edge down with it. Offending " +
            "keys: {0}. If you wanted to leave a note, put it in that route's Metadata.",
            string.Join(", ", invalid));
    }
}
