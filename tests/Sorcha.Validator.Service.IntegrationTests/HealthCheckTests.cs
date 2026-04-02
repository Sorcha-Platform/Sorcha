// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Validator.Service.IntegrationTests;

/// <summary>
/// Integration tests for Validator Service health check endpoints.
/// </summary>
[Collection("ValidatorService")]
public class HealthCheckTests
{
    private readonly ValidatorServiceWebApplicationFactory _factory;

    public HealthCheckTests(ValidatorServiceWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task HealthEndpoint_ReturnsHealthy()
    {
        _factory.SkipIfUnavailable();

        // Arrange
        using var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/health");

        // Assert
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
    }

    [Fact]
    public async Task AliveEndpoint_ReturnsOk()
    {
        _factory.SkipIfUnavailable();

        // Arrange
        using var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/alive");

        // Assert
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
    }
}
