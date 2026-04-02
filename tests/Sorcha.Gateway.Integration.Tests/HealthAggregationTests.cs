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

        // Act
        var response = await GatewayClient!.GetAsync("/api/stats");

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
