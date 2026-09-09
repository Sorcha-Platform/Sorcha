// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace Sorcha.Gateway.Integration.Tests;

/// <summary>
/// Integration tests for health aggregation functionality
/// </summary>
public class HealthAggregationTests : GatewayIntegrationTestBase
{
    [Fact]
    public async Task GetAggregatedHealth_ReturnsHealthStatus()
    {
        SkipIfInfrastructureUnavailable();

        // Act
        var response = await GatewayClient!.GetAsync("/api/health");

        // Assert
        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.ServiceUnavailable);

        var content = await response.Content.ReadAsStringAsync();
        var healthResponse = JsonDocument.Parse(content);

        healthResponse.RootElement.GetProperty("status").GetString().Should().NotBeNullOrEmpty();
        healthResponse.RootElement.GetProperty("timestamp").GetDateTimeOffset().Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));
        healthResponse.RootElement.TryGetProperty("services", out var services).Should().BeTrue();

        services.EnumerateObject().Should().NotBeEmpty("at least one service should be registered");
    }

    [Fact]
    public async Task GetAggregatedHealth_IncludesAllServices()
    {
        SkipIfInfrastructureUnavailable();

        // Act
        var response = await GatewayClient!.GetAsync("/api/health");
        var content = await response.Content.ReadAsStringAsync();
        var healthResponse = JsonDocument.Parse(content);

        // Assert
        var services = healthResponse.RootElement.GetProperty("services");

        // Verify blueprint service is included
        services.TryGetProperty("blueprint", out var blueprintHealth).Should().BeTrue();
        blueprintHealth.GetProperty("status").GetString().Should().NotBeNullOrEmpty();

        // Verify peer service is included
        services.TryGetProperty("peer", out var peerHealth).Should().BeTrue();
        peerHealth.GetProperty("status").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task GetSystemStats_ReturnsStatistics()
    {
        SkipIfInfrastructureUnavailable();

        // /api/gateway/stats, not /api/stats (#1616). The gateway used to answer the bare path
        // itself, which meant no backend could be reached on it — every client pointed at the
        // gateway got this aggregation whatever it asked for. The bare path now proxies to the
        // Register Service.
        var response = await GatewayClient!.GetAsync("/api/gateway/stats");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var stats = JsonDocument.Parse(content);

        stats.RootElement.GetProperty("totalServices").GetInt32().Should().BeGreaterThan(0);
        stats.RootElement.GetProperty("healthyServices").GetInt32().Should().BeGreaterThanOrEqualTo(0);
        stats.RootElement.GetProperty("unhealthyServices").GetInt32().Should().BeGreaterThanOrEqualTo(0);
        stats.RootElement.GetProperty("timestamp").GetDateTimeOffset().Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task GetStats_ProxiesToTheRegisterService_NotTheGatewayAggregation()
    {
        SkipIfInfrastructureUnavailable();

        // The point of #1616: asking the gateway for /api/stats must reach the REGISTER Service's
        // platform-wide counts. It previously returned the gateway's own service-health body, and
        // RegisterServiceClient bound {registerCount, transactionCount} out of it — both absent, so
        // both silently 0. sorcha_register_stats reported "operational with 0 registers" against a
        // node holding 21.
        var response = await GatewayClient!.GetAsync("/api/stats");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var stats = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        stats.RootElement.TryGetProperty("registerCount", out _).Should().BeTrue(
            "the bare path must carry the Register Service's shape, or every client that binds it gets zeros");
        stats.RootElement.TryGetProperty("totalServices", out _).Should().BeFalse(
            "the gateway aggregation moved to /api/gateway/stats");
    }

    [Fact]
    public async Task GetLandingPage_ReturnsHtml()
    {
        SkipIfInfrastructureUnavailable();

        // Act
        var response = await GatewayClient!.GetAsync("/");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("text/html");

        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("Sorcha API Gateway");
        html.Should().Contain("Download Client");
        html.Should().Contain("API Documentation");
    }
}
