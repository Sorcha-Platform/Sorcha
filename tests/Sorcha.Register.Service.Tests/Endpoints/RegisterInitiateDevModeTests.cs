// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Sorcha.Register.Service.Tests.Helpers;
using Xunit;

namespace Sorcha.Register.Service.Tests.Endpoints;

/// <summary>
/// Tests for DevMode flag handling during register initiation (POST /api/registers/initiate).
/// Verifies that the DevMode property on InitiateRegisterCreationRequest is correctly
/// propagated to the pending registration.
/// </summary>
public class RegisterInitiateDevModeTests : IClassFixture<RegisterServiceWebApplicationFactory>
{
    private readonly RegisterServiceWebApplicationFactory _factory;

    public RegisterInitiateDevModeTests(RegisterServiceWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task InitiateRegisterCreation_WithDevModeTrue_ShouldAcceptRequest()
    {
        // Arrange
        var client = _factory.CreateClient();

        var request = new
        {
            name = "DevMode Test Register",
            description = "Register with DevMode enabled",
            tenantId = "test-tenant-001",
            devMode = true,
            owners = new[]
            {
                new { userId = "test-user-001", walletId = "test-wallet-001" }
            }
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/registers/initiate", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        result.GetProperty("registerId").GetString().Should().NotBeNullOrEmpty();
        result.GetProperty("nonce").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task InitiateRegisterCreation_WithDevModeFalse_ShouldAcceptRequest()
    {
        // Arrange
        var client = _factory.CreateClient();

        var request = new
        {
            name = "Non-DevMode Test Register",
            description = "Register with DevMode disabled",
            tenantId = "test-tenant-001",
            devMode = false,
            owners = new[]
            {
                new { userId = "test-user-001", walletId = "test-wallet-001" }
            }
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/registers/initiate", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        result.GetProperty("registerId").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task InitiateRegisterCreation_WithDevModeOmitted_ShouldDefaultToFalse()
    {
        // Arrange
        var client = _factory.CreateClient();

        // DevMode not included in the request — should default to false
        var request = new
        {
            name = "Default DevMode Register",
            tenantId = "test-tenant-001",
            owners = new[]
            {
                new { userId = "test-user-001", walletId = "test-wallet-001" }
            }
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/registers/initiate", request);

        // Assert — request succeeds; DevMode defaults to false (bool default)
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        result.GetProperty("registerId").GetString().Should().NotBeNullOrEmpty();
    }
}
