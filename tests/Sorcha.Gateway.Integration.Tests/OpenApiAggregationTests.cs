// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Text.Json;
using FluentAssertions;

namespace Sorcha.Gateway.Integration.Tests;

/// <summary>
/// Integration tests for OpenAPI aggregation functionality
/// </summary>
public class OpenApiAggregationTests : GatewayIntegrationTestBase
{
    [Fact]
    public async Task GetAggregatedOpenApi_ReturnsValidSpec()
    {
        SkipIfInfrastructureUnavailable();

        // Act
        var response = await GatewayClient!.GetAsync("/openapi/aggregated.json");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Contain("json");

        var content = await response.Content.ReadAsStringAsync();
        var openApiSpec = JsonDocument.Parse(content);

        // Verify OpenAPI 3.0 structure
        openApiSpec.RootElement.GetProperty("openapi").GetString().Should().StartWith("3.0");
        openApiSpec.RootElement.TryGetProperty("info", out var info).Should().BeTrue();
        info.GetProperty("title").GetString().Should().Contain("Sorcha");
        openApiSpec.RootElement.TryGetProperty("paths", out var paths).Should().BeTrue();
        paths.EnumerateObject().Should().NotBeEmpty("should contain API paths");
    }

    [Fact]
    public async Task AggregatedOpenApi_IncludesBlueprintPaths()
    {
        SkipIfInfrastructureUnavailable();

        // Act
        var response = await GatewayClient!.GetAsync("/openapi/aggregated.json");
        var content = await response.Content.ReadAsStringAsync();
        var openApiSpec = JsonDocument.Parse(content);

        // Assert
        var paths = openApiSpec.RootElement.GetProperty("paths");
        var pathList = paths.EnumerateObject().Select(p => p.Name).ToList();

        pathList.Should().Contain(p => p.StartsWith("/api/blueprint"), "should include blueprint API paths");
    }

    [Fact]
    public async Task AggregatedOpenApi_IncludesPeerPaths()
    {
        SkipIfInfrastructureUnavailable();

        // Act
        var response = await GatewayClient!.GetAsync("/openapi/aggregated.json");
        var content = await response.Content.ReadAsStringAsync();
        var openApiSpec = JsonDocument.Parse(content);

        // Assert
        var paths = openApiSpec.RootElement.GetProperty("paths");
        var pathList = paths.EnumerateObject().Select(p => p.Name).ToList();

        pathList.Should().Contain(p => p.StartsWith("/api/peer"), "should include peer service paths");
    }

    [Fact]
    public async Task AggregatedOpenApi_HasComponents()
    {
        SkipIfInfrastructureUnavailable();

        // Act
        var response = await GatewayClient!.GetAsync("/openapi/aggregated.json");
        var content = await response.Content.ReadAsStringAsync();
        var openApiSpec = JsonDocument.Parse(content);

        // Assert
        openApiSpec.RootElement.TryGetProperty("components", out var components).Should().BeTrue();
        components.TryGetProperty("schemas", out var schemas).Should().BeTrue();
    }

    [Fact]
    public async Task ScalarDocumentation_IsAccessible()
    {
        SkipIfInfrastructureUnavailable();

        // Act
        var response = await GatewayClient!.GetAsync("/scalar/v1");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("text/html");

        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("Sorcha");
    }

    [Fact]
    public async Task GatewayOpenApi_IsAccessible()
    {
        SkipIfInfrastructureUnavailable();

        // Act
        var response = await GatewayClient!.GetAsync("/openapi/v1.json");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var openApiSpec = JsonDocument.Parse(content);

        openApiSpec.RootElement.GetProperty("openapi").GetString().Should().StartWith("3.0");
    }
}
