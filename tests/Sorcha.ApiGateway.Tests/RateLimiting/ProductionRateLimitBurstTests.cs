// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Net.Http.Headers;

using FluentAssertions;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

using Sorcha.ServiceDefaults;

using Xunit;

namespace Sorcha.ApiGateway.Tests.RateLimiting;

/// <summary>
/// Public Gates verification: proves that the SHIPPED Production rate-limit configuration
/// (<c>src/Services/Sorcha.ApiGateway/appsettings.Production.json</c>) both keeps its intended
/// tightened values (regression guard) AND actually activates real HTTP 429 throttling when
/// driven through the genuine <c>AddRateLimiting</c> middleware extension from
/// Sorcha.ServiceDefaults (<see cref="RateLimitPolicies"/>, <see cref="RateLimitSettings"/>).
/// </summary>
/// <remarks>
/// <para>
/// n1 runs <c>ASPNETCORE_ENVIRONMENT=Development</c>, where <see cref="RateLimitSettings"/>'
/// coded defaults are deliberately relaxed (100k/min) so local development is never throttled.
/// The Production appsettings tighten every policy dramatically. Before this test existed, "does
/// production rate limiting actually throttle" was verified by a one-off manual burst against a
/// running node — this test replaces that with a permanent, reproducible, in-process guard that
/// runs on every build without Docker or a live deployment.
/// </para>
/// <para>
/// The burst targets <see cref="RateLimitPolicies.Authentication"/> (sliding window,
/// 60 requests/min in Production) rather than <see cref="RateLimitPolicies.Api"/> (600/min) —
/// it trips in a handful of sequential requests instead of hundreds, keeping the test fast
/// without weakening what it proves: the same <c>AddRateLimiting</c> extension wires every
/// policy from the same <see cref="RateLimitSettings"/> binding.
/// </para>
/// </remarks>
public class ProductionRateLimitBurstTests(ITestOutputHelper output)
{
    private const string ProductionConfigFileName = "ApiGateway.appsettings.Production.json";
    private const string BurstClientIp = "203.0.113.7"; // TEST-NET-3 (RFC 5737) — never a routable client

    private static string ProductionConfigPath =>
        Path.Combine(AppContext.BaseDirectory, ProductionConfigFileName);

    /// <summary>
    /// Test A — regression guard. Pins the exact values shipped in
    /// <c>appsettings.Production.json</c> so a future edit that loosens them (e.g. reverting to
    /// the 100k/min dev-scale coded default) fails CI instead of silently reaching production.
    /// </summary>
    [Fact]
    public void ProductionRateLimitSettings_ShippedValues_ArePinned()
    {
        File.Exists(ProductionConfigPath).Should().BeTrue(
            $"the linked copy of appsettings.Production.json must be present at {ProductionConfigPath} " +
            "for this guard to mean anything — see the <Content Include> in the csproj");

        var configuration = new ConfigurationBuilder()
            .AddJsonFile(ProductionConfigPath, optional: false)
            .Build();

        var settings = new RateLimitSettings();
        configuration.GetSection(RateLimitSettings.SectionName).Bind(settings);

        settings.ApiPermitLimit.Should().Be(600);
        settings.ApiQueueLimit.Should().Be(0);
        settings.AuthenticationPermitLimit.Should().Be(60);
        settings.AuthenticationQueueLimit.Should().Be(0);
        settings.StrictTokenLimit.Should().Be(60);
        settings.StrictTokensPerPeriod.Should().Be(10);
        settings.StrictReplenishmentPeriodSeconds.Should().Be(1);
        settings.StrictQueueLimit.Should().Be(0);

        // A regression back toward the dev-scale coded default (100,000/min) must fail here —
        // this is what makes the guard discriminating rather than a tautology.
        settings.ApiPermitLimit.Should().BeLessThan(10_000,
            "production must be far tighter than RateLimitSettings' 100k/min development default");
    }

    /// <summary>
    /// Test B — real burst through the real middleware. Builds an in-process TestServer host
    /// wired with the genuine <c>AddRateLimiting</c> extension, bound to the shipped Production
    /// config, and fires a sequential burst at a policy-gated endpoint. Asserts real HTTP 429s
    /// are returned once the Production <c>AuthenticationPermitLimit</c> (60/min) is exceeded.
    /// </summary>
    [Fact]
    public async Task Burst_AgainstProductionAuthenticationPolicy_TripsRealFourTwentyNines()
    {
        await using var app = await BuildProductionHostAsync();
        using var client = app.GetTestClient();
        client.BaseAddress = new Uri("http://localhost/");

        // Every request must land in the SAME rate-limit partition (GetClientIdentifier
        // partitions by client IP via ClientIpHelper.GetClientIp, which honours
        // X-Forwarded-For first). A fixed header here is what makes the burst deterministic —
        // without it, TestServer's default RemoteIpAddress is stable too, but pinning it
        // explicitly documents the mechanism and survives TestServer defaults changing.
        client.DefaultRequestHeaders.Add("X-Forwarded-For", BurstClientIp);

        const int totalRequests = 70;
        var statusCodes = new List<HttpStatusCode>(totalRequests);
        HttpResponseHeaders? firstRejectedHeaders = null;

        for (var i = 0; i < totalRequests; i++)
        {
            using var response = await client.GetAsync("/burst");
            statusCodes.Add(response.StatusCode);

            if (i == 0)
            {
                response.StatusCode.Should().Be(HttpStatusCode.OK,
                    "the very first request against a freshly-started host must succeed");
            }

            if (response.StatusCode == HttpStatusCode.TooManyRequests && firstRejectedHeaders is null)
            {
                // Capture headers from the actual 429 response object (not a re-issued request)
                // so the Retry-After assertion below proves THIS burst's rejection carried it.
                firstRejectedHeaders = response.Headers;
            }
        }

        var successCount = statusCodes.Count(s => s == HttpStatusCode.OK);
        var throttledCount = statusCodes.Count(s => s == HttpStatusCode.TooManyRequests);
        var firstThrottledAt = statusCodes.FindIndex(s => s == HttpStatusCode.TooManyRequests);

        output.WriteLine(
            $"Burst result: {successCount} succeeded (200), {throttledCount} throttled (429), " +
            $"first 429 at request #{firstThrottledAt + 1} of {totalRequests}.");

        (successCount + throttledCount).Should().Be(totalRequests,
            "every response must be either 200 or 429 — anything else means the burst hit something other than the limiter");

        throttledCount.Should().BeGreaterThan(0,
            "the burst must trip the shipped Production AuthenticationPermitLimit (60/min) — a " +
            "silent zero here means the guard is vacuous, not that rate limiting is healthy");

        // Sliding-window timing tolerance: SegmentsPerWindow=6 means the effective limit can
        // drift a little either side of the nominal 60 depending on wall-clock segment boundaries
        // crossed during the burst — assert a band, not the exact number, to avoid flakiness.
        successCount.Should().BeInRange(50, 65,
            "success count should track the Production AuthenticationPermitLimit (60/min) within sliding-window tolerance");

        statusCodes.Should().Contain(HttpStatusCode.TooManyRequests);
        var firstThrottledIndex = statusCodes.FindIndex(s => s == HttpStatusCode.TooManyRequests);
        firstThrottledIndex.Should().BeGreaterThan(0, "at least one request must succeed before throttling begins");

        // Prove it's the rate limiter's OnRejected handler (Sorcha.ServiceDefaults.Extensions),
        // not some unrelated 429 from elsewhere in the pipeline.
        firstRejectedHeaders.Should().NotBeNull();
        firstRejectedHeaders!.Contains("Retry-After").Should().BeTrue(
            "the OnRejected handler in AddRateLimiting always sets Retry-After on a 429");
    }

    private static async Task<WebApplication> BuildProductionHostAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();

        // Bind RateLimitSettings from the SHIPPED Production appsettings — this is the file
        // that ships to n1/production, not a hand-authored in-memory dictionary mirroring it.
        builder.Configuration.AddJsonFile(ProductionConfigPath, optional: false);

        // The real extension under test — no shortcuts, no re-implementation.
        builder.AddRateLimiting();

        var app = builder.Build();
        app.UseRateLimiter();
        app.MapGet("/burst", () => Results.Ok("ok"))
            .RequireRateLimiting(RateLimitPolicies.Authentication);

        await app.StartAsync();
        return app;
    }
}
