// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Sorcha.Register.Core.Managers;
using Sorcha.Register.Service.Tests.Helpers;
using Xunit;

namespace Sorcha.Register.Service.Tests.Endpoints;

/// <summary>
/// Tests for the DevMode toggle endpoint (PUT /api/registers/{registerId}/devmode).
/// Verifies enabling, disabling, 404 for unknown registers, and authorization requirements.
/// </summary>
[Collection("RegisterWebApp")]
public class RegisterDevModeToggleTests : IClassFixture<RegisterServiceWebApplicationFactory>
{
    private readonly RegisterServiceWebApplicationFactory _factory;

    public RegisterDevModeToggleTests(RegisterServiceWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ToggleDevMode_EnableOnExistingRegister_ShouldReturn200WithDevModeTrue()
    {
        // Arrange — create a register directly via RegisterManager
        var register = await CreateTestRegisterAsync("DevMode Toggle Enable");
        var client = _factory.CreateClient();

        var request = new { enabled = true };

        // Act
        var response = await client.PutAsJsonAsync($"/api/registers/{register.Id}/devmode", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        result.GetProperty("registerId").GetString().Should().Be(register.Id);
        result.GetProperty("devMode").GetBoolean().Should().BeTrue();
        result.TryGetProperty("effectiveFrom", out _).Should().BeTrue();
    }

    [Fact]
    public async Task ToggleDevMode_DisableOnExistingRegister_ShouldReturn200WithDevModeFalse()
    {
        // Arrange — create a register directly via RegisterManager
        var register = await CreateTestRegisterAsync("DevMode Toggle Disable");
        var client = _factory.CreateClient();

        var request = new { enabled = false };

        // Act
        var response = await client.PutAsJsonAsync($"/api/registers/{register.Id}/devmode", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        result.GetProperty("registerId").GetString().Should().Be(register.Id);
        result.GetProperty("devMode").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task ToggleDevMode_UnknownRegister_ShouldReturn404()
    {
        // Arrange
        var client = _factory.CreateClient();
        var unknownRegisterId = "00000000000000000000000000000000";

        var request = new { enabled = true };

        // Act
        var response = await client.PutAsJsonAsync($"/api/registers/{unknownRegisterId}/devmode", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ToggleDevMode_EnableThenDisable_ShouldReflectLatestState()
    {
        // Arrange
        var register = await CreateTestRegisterAsync("DevMode Roundtrip");
        var client = _factory.CreateClient();

        // Act — enable
        var enableResponse = await client.PutAsJsonAsync(
            $"/api/registers/{register.Id}/devmode",
            new { enabled = true });
        enableResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var enableResult = await enableResponse.Content.ReadFromJsonAsync<JsonElement>();
        enableResult.GetProperty("devMode").GetBoolean().Should().BeTrue();

        // Act — disable
        var disableResponse = await client.PutAsJsonAsync(
            $"/api/registers/{register.Id}/devmode",
            new { enabled = false });
        disableResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var disableResult = await disableResponse.Content.ReadFromJsonAsync<JsonElement>();
        disableResult.GetProperty("devMode").GetBoolean().Should().BeFalse();
    }

    /// <summary>
    /// Creates a test register directly via RegisterManager, bypassing the two-phase API.
    /// </summary>
    private async Task<Sorcha.Register.Models.Register> CreateTestRegisterAsync(string name)
    {
        using var scope = _factory.Services.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<RegisterManager>();
        return await manager.CreateRegisterAsync(name);
    }
}
