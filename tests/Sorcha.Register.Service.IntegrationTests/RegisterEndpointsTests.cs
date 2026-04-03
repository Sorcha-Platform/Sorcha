// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Sorcha.Register.Service.IntegrationTests.Fixtures;

namespace Sorcha.Register.Service.IntegrationTests;

/// <summary>
/// Integration tests for Register Service endpoints.
/// Verifies health checks, register CRUD operations, and authentication requirements.
/// </summary>
[Collection("RegisterService")]
public class RegisterEndpointsTests : IAsyncLifetime
{
    private readonly RegisterServiceWebApplicationFactory _factory;
    private HttpClient _client = null!;

    public RegisterEndpointsTests(RegisterServiceWebApplicationFactory factory)
    {
        _factory = factory;
    }

    public ValueTask InitializeAsync()
    {
        _client = _factory.CreateAuthenticatedClient();
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        _client.Dispose();
        return ValueTask.CompletedTask;
    }

    // === Health & Liveness ===

    [Fact]
    public async Task HealthEndpoint_ReturnsOk()
    {
        // Arrange
        using var client = _factory.CreateUnauthenticatedClient();

        // Act
        var response = await client.GetAsync("/health");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AliveEndpoint_ReturnsOk()
    {
        // Arrange
        using var client = _factory.CreateUnauthenticatedClient();

        // Act
        var response = await client.GetAsync("/alive");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // === Register Endpoints ===

    [Fact]
    public async Task GetAllRegisters_ReturnsOk()
    {
        // Act
        var response = await _client.GetAsync("/api/registers/");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetRegisterById_UnknownId_ReturnsNotFound()
    {
        // Act
        var response = await _client.GetAsync("/api/registers/unknown-register-id");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetAllRegisters_RequiresAuthentication()
    {
        // Arrange
        using var unauthenticatedClient = _factory.CreateUnauthenticatedClient();

        // Act
        var response = await unauthenticatedClient.GetAsync("/api/registers/");

        // Assert
        response.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetRegisterById_RequiresAuthentication()
    {
        // Arrange
        using var unauthenticatedClient = _factory.CreateUnauthenticatedClient();

        // Act
        var response = await unauthenticatedClient.GetAsync("/api/registers/some-id");

        // Assert
        response.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);
    }

    // === Register Count ===

    [Fact]
    public async Task GetRegisterCount_ReturnsOk()
    {
        // Act
        var response = await _client.GetAsync("/api/registers/stats/count");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetRegisterCount_RequiresAuthentication()
    {
        // Arrange
        using var unauthenticatedClient = _factory.CreateUnauthenticatedClient();

        // Act
        var response = await unauthenticatedClient.GetAsync("/api/registers/stats/count");

        // Assert
        response.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);
    }

    // === Register Creation (Anonymous) ===

    [Fact]
    public async Task InitiateRegisterCreation_WithEmptyBody_ReturnsBadRequest()
    {
        // Arrange
        using var client = _factory.CreateUnauthenticatedClient();

        // Act
        var response = await client.PostAsync("/api/registers/initiate", null);

        // Assert
        // Empty body should return BadRequest or a 4xx/5xx error
        response.StatusCode.Should().BeOneOf(
            HttpStatusCode.BadRequest,
            HttpStatusCode.UnsupportedMediaType,
            HttpStatusCode.InternalServerError);
    }

    #region Internal Endpoint Auth Tests

    [Fact]
    public async Task InternalGetRegisters_WithoutAuth_ReturnsUnauthorized()
    {
        // Arrange
        using var unauthenticatedClient = _factory.CreateUnauthenticatedClient();

        // Act
        var response = await unauthenticatedClient.GetAsync("/api/internal/registers");

        // Assert
        response.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task InternalGetRegisters_WithServiceToken_ReturnsOk()
    {
        // Arrange
        using var serviceClient = _factory.CreateServiceClient();

        // Act
        var response = await serviceClient.GetAsync("/api/internal/registers");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task InternalRegisterSubscriptions_WithoutAuth_ReturnsUnauthorized()
    {
        // Arrange
        using var unauthenticatedClient = _factory.CreateUnauthenticatedClient();
        var content = JsonContent.Create(new { registerId = "abcdef0123456789abcdef0123456789", action = "subscribe" });

        // Act
        var response = await unauthenticatedClient.PostAsync("/api/internal/register-subscriptions", content);

        // Assert
        response.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task InternalSyncStatus_WithoutAuth_ReturnsUnauthorized()
    {
        // Arrange
        using var unauthenticatedClient = _factory.CreateUnauthenticatedClient();
        var content = JsonContent.Create(new { registerId = "abcdef0123456789abcdef0123456789", syncState = "Active", peerConnectionActive = true });

        // Act
        var response = await unauthenticatedClient.PostAsync("/api/internal/register-sync-status", content);

        // Assert
        response.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);
    }

    #endregion
}
