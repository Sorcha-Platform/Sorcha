// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Sorcha.Validator.Service.IntegrationTests;

/// <summary>
/// Integration tests for admin endpoints.
/// Admin endpoints require the RequireAdministrator policy.
/// </summary>
[Collection("ValidatorService")]
public class AdminEndpointTests
{
    private readonly ValidatorServiceWebApplicationFactory _factory;

    public AdminEndpointTests(ValidatorServiceWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.ClearTestData();
    }

    [Fact]
    public async Task StartValidator_WithValidRequest_ReturnsOk()
    {
        // Arrange
        using var client = _factory.CreateAdminClient();
        var request = new { RegisterId = Guid.NewGuid().ToString("N") };

        // Act
        var response = await client.PostAsJsonAsync("/api/admin/validators/start", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadFromJsonAsync<JsonDocument>();
        content.Should().NotBeNull();
        content!.RootElement.GetProperty("status").GetString().Should().Be("Started");
    }

    [Fact]
    public async Task StartValidator_WithEmptyRegisterId_ReturnsBadRequest()
    {
        // Arrange
        using var client = _factory.CreateAdminClient();
        var request = new { RegisterId = "" };

        // Act
        var response = await client.PostAsJsonAsync("/api/admin/validators/start", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task StopValidator_WithNotStartedValidator_ReturnsError()
    {
        // Arrange
        using var client = _factory.CreateAdminClient();
        var registerId = Guid.NewGuid().ToString("N");
        var request = new { RegisterId = registerId, PersistMemPool = true };

        // Act - don't start validator first, so stop should fail
        var response = await client.PostAsJsonAsync("/api/admin/validators/stop", request);

        // Assert - Returns 500 because validator wasn't started
        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task StopValidator_WithEmptyRegisterId_ReturnsBadRequest()
    {
        // Arrange
        using var client = _factory.CreateAdminClient();
        var request = new { RegisterId = "", PersistMemPool = false };

        // Act
        var response = await client.PostAsJsonAsync("/api/admin/validators/stop", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetValidatorStatus_WithUnknownRegisterId_ReturnsNotFound()
    {
        // Arrange
        using var client = _factory.CreateAdminClient();
        var registerId = Guid.NewGuid().ToString("N");

        // Act - validator not started, so status should return 404
        var response = await client.GetAsync($"/api/admin/validators/{registerId}/status");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ProcessValidationPipeline_WithNoTransactions_ReturnsOkWithMessage()
    {
        // Arrange
        using var client = _factory.CreateAdminClient();
        var registerId = Guid.NewGuid().ToString("N");

        // Act - no validator started, no transactions
        var response = await client.PostAsync($"/api/admin/validators/{registerId}/process", null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadFromJsonAsync<JsonDocument>();
        content.Should().NotBeNull();
        // Either returns a docket result or a message about no pending transactions
        var hasMessage = content!.RootElement.TryGetProperty("message", out _);
        var hasDocketNumber = content.RootElement.TryGetProperty("docketNumber", out _);
        (hasMessage || hasDocketNumber).Should().BeTrue();
    }

    [Fact]
    public async Task StartValidator_ReturnsExpectedResponseStructure()
    {
        // Arrange
        using var client = _factory.CreateAdminClient();
        var registerId = Guid.NewGuid().ToString("N");

        // Act - Start validator
        var startResponse = await client.PostAsJsonAsync("/api/admin/validators/start", new { RegisterId = registerId });

        // Assert
        startResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var startContent = await startResponse.Content.ReadFromJsonAsync<JsonDocument>();
        startContent.Should().NotBeNull();
        startContent!.RootElement.GetProperty("status").GetString().Should().Be("Started");
        startContent.RootElement.GetProperty("registerId").GetString().Should().Be(registerId);
        startContent.RootElement.TryGetProperty("message", out _).Should().BeTrue();
    }
}
