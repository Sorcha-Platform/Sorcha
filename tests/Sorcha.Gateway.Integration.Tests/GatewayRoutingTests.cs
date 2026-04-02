// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using FluentAssertions;

namespace Sorcha.Gateway.Integration.Tests;

/// <summary>
/// Integration tests for gateway routing and YARP proxy functionality
/// </summary>
public class GatewayRoutingTests : GatewayIntegrationTestBase
{
    [Fact]
    public async Task BlueprintRoutes_AreProxiedCorrectly()
    {
        SkipIfInfrastructureUnavailable();

        // Act - Get blueprints through gateway
        var response = await GatewayClient!.GetAsync("/api/blueprint/blueprints");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task BlueprintStatus_MapsToHealthEndpoint()
    {
        SkipIfInfrastructureUnavailable();

        // Act
        var response = await GatewayClient!.GetAsync("/api/blueprint/status");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("status");
        content.Should().Contain("service");
    }

    [Fact]
    public async Task PeerRoutes_AreProxiedCorrectly()
    {
        SkipIfInfrastructureUnavailable();

        // Act - Get peers through gateway
        var response = await GatewayClient!.GetAsync("/api/peer/peers");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task PeerStatus_MapsToHealthEndpoint()
    {
        SkipIfInfrastructureUnavailable();

        // Act
        var response = await GatewayClient!.GetAsync("/api/peer/status");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("status");
        content.Should().Contain("service");
    }

    [Fact]
    public async Task NonExistentRoute_Returns404()
    {
        SkipIfInfrastructureUnavailable();

        // Act
        var response = await GatewayClient!.GetAsync("/api/nonexistent/route");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task CorsHeaders_ArePresent()
    {
        SkipIfInfrastructureUnavailable();

        // Arrange
        var request = new HttpRequestMessage(HttpMethod.Options, "/api/health");
        request.Headers.Add("Origin", "http://localhost:5000");
        request.Headers.Add("Access-Control-Request-Method", "GET");

        // Act
        var response = await GatewayClient!.SendAsync(request);

        // Assert
        response.Headers.Should().ContainKey("Access-Control-Allow-Origin")
            .WhoseValue.Should().Contain("*");
    }
}
