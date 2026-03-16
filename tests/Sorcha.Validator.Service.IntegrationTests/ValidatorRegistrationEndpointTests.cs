// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Sorcha.Validator.Service.IntegrationTests;

/// <summary>
/// Integration tests for validator registration endpoints.
/// Endpoints are mapped at /api/validators/*
/// </summary>
[Collection("ValidatorService")]
public class ValidatorRegistrationEndpointTests
{
    private readonly ValidatorServiceWebApplicationFactory _factory;

    public ValidatorRegistrationEndpointTests(ValidatorServiceWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.ClearTestData();
    }

    [Fact]
    public async Task GetValidators_WithValidRegisterId_ReturnsValidators()
    {
        // Arrange
        using var client = _factory.CreateValidatorClient();
        var registerId = Guid.NewGuid().ToString("N");

        // Act
        var response = await client.GetAsync($"/api/validators/{registerId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadFromJsonAsync<JsonDocument>();
        content.Should().NotBeNull();
        content!.RootElement.GetProperty("registerId").GetString().Should().Be(registerId);
        content.RootElement.TryGetProperty("count", out _).Should().BeTrue();
        content.RootElement.TryGetProperty("validators", out _).Should().BeTrue();
    }

    [Fact]
    public async Task GetValidators_EndpointExists()
    {
        // Arrange
        using var client = _factory.CreateValidatorClient();

        // Act
        var response = await client.GetAsync("/api/validators/test-register");

        // Assert - Endpoint exists (not 404)
        response.StatusCode.Should().NotBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetValidator_WithUnknownValidator_ReturnsNotFound()
    {
        // Arrange
        using var client = _factory.CreateValidatorClient();
        var registerId = Guid.NewGuid().ToString("N");
        var validatorId = Guid.NewGuid().ToString("N");

        // Act
        var response = await client.GetAsync($"/api/validators/{registerId}/{validatorId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetValidatorCount_WithValidRegisterId_ReturnsCount()
    {
        // Arrange
        using var client = _factory.CreateValidatorClient();
        var registerId = Guid.NewGuid().ToString("N");

        // Act
        var response = await client.GetAsync($"/api/validators/{registerId}/count");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadFromJsonAsync<JsonDocument>();
        content.Should().NotBeNull();
        content!.RootElement.GetProperty("registerId").GetString().Should().Be(registerId);
        content.RootElement.TryGetProperty("activeCount", out _).Should().BeTrue();
        content.RootElement.TryGetProperty("hasQuorum", out _).Should().BeTrue();
    }

    [Fact]
    public async Task GetValidatorCount_EndpointExists()
    {
        // Arrange
        using var client = _factory.CreateValidatorClient();

        // Act
        var response = await client.GetAsync("/api/validators/test-register/count");

        // Assert - Endpoint exists (not 404)
        response.StatusCode.Should().NotBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task RefreshValidators_WithValidRegisterId_ReturnsSuccess()
    {
        // Arrange
        using var client = _factory.CreateValidatorClient();
        var registerId = Guid.NewGuid().ToString("N");

        // Act
        var response = await client.PostAsync($"/api/validators/{registerId}/refresh", null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadFromJsonAsync<JsonDocument>();
        content.Should().NotBeNull();
        content!.RootElement.GetProperty("registerId").GetString().Should().Be(registerId);
        content.RootElement.GetProperty("refreshed").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task RefreshValidators_EndpointExists()
    {
        // Arrange
        using var client = _factory.CreateValidatorClient();

        // Act
        var response = await client.PostAsync("/api/validators/test-register/refresh", null);

        // Assert - Endpoint exists (not 404)
        response.StatusCode.Should().NotBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task RegisterValidator_EndpointExists()
    {
        // Arrange
        using var client = _factory.CreateValidatorClient();
        var request = new
        {
            RegisterId = Guid.NewGuid().ToString("N"),
            ValidatorId = Guid.NewGuid().ToString("N"),
            PublicKey = Convert.ToBase64String(new byte[32]),
            GrpcEndpoint = "http://localhost:7004"
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/validators/register", request);

        // Assert - Endpoint exists (not 404)
        response.StatusCode.Should().NotBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task RegisterValidator_WithValidRequest_ReturnsCreatedOrBadRequest()
    {
        // Arrange
        using var client = _factory.CreateValidatorClient();
        var registerId = Guid.NewGuid().ToString("N");

        var request = new
        {
            RegisterId = registerId,
            ValidatorId = Guid.NewGuid().ToString("N"),
            PublicKey = Convert.ToBase64String(new byte[32]),
            GrpcEndpoint = "http://localhost:7004",
            Metadata = new Dictionary<string, string>
            {
                ["version"] = "1.0.0"
            }
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/validators/register", request);

        // Assert - Either Created (201) if public registration or BadRequest (400) if consent mode
        // The default config might return consent mode, so expect either
        response.StatusCode.Should().BeOneOf(HttpStatusCode.Created, HttpStatusCode.BadRequest);
    }
}
