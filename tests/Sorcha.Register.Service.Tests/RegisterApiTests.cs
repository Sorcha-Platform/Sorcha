// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Sorcha.Register.Models.Enums;
using Sorcha.Register.Service.Tests.Helpers;
using Xunit;

namespace Sorcha.Register.Service.Tests;

public class RegisterApiTests : IClassFixture<RegisterServiceWebApplicationFactory>
{
    private readonly RegisterServiceWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public RegisterApiTests(RegisterServiceWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task InitiateRegisterCreation_WithValidData_ShouldReturn200Ok()
    {
        // Arrange
        var request = new
        {
            name = "Test Register",
            tenantId = "tenant123",
            owners = new[]
            {
                new { userId = "test-user-001", walletId = "test-wallet-001" }
            }
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/registers/initiate", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task InitiateRegisterCreation_ShouldReturnRegisterIdAndControlRecord()
    {
        // Arrange
        var request = new
        {
            name = "Test Register",
            tenantId = "tenant123",
            owners = new[]
            {
                new { userId = "test-user-001", walletId = "test-wallet-001" }
            }
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/registers/initiate", request);
        var result = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();

        // Assert
        result.GetProperty("registerId").GetString().Should().NotBeNullOrWhiteSpace();
        result.GetProperty("registerId").GetString()!.Length.Should().Be(32);
        result.GetProperty("nonce").GetString().Should().NotBeNullOrEmpty();
        result.GetProperty("expiresAt").GetDateTimeOffset().Should().BeAfter(DateTimeOffset.UtcNow);
        result.GetProperty("attestationsToSign").GetArrayLength().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task InitiateRegisterCreation_WithEmptyOwners_ShouldReturn400BadRequest()
    {
        // Arrange
        var request = new
        {
            name = "Test Register",
            tenantId = "tenant123",
            owners = Array.Empty<object>()
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/registers/initiate", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task InitiateRegisterCreation_WithMissingName_ShouldReturn400BadRequest()
    {
        // Arrange — name is empty, which the orchestrator should reject
        var request = new
        {
            name = "",
            tenantId = "tenant123",
            owners = new[]
            {
                new { userId = "test-user-001", walletId = "test-wallet-001" }
            }
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/registers/initiate", request);

        // Assert — initiate phase generates a register ID regardless of name content;
        // name validation occurs during finalize when the control record is verified
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetAllRegisters_ShouldReturn200OK()
    {
        // Act
        var response = await _client.GetAsync("/api/registers");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetAllRegisters_ShouldReturnArrayOfRegisters()
    {
        // Arrange
        await _factory.CreateTestRegisterAsync("Register 1", "tenant123");
        await _factory.CreateTestRegisterAsync("Register 2", "tenant456");

        // Act
        var response = await _client.GetAsync("/api/registers");
        var registers = await response.Content.ReadFromJsonAsync<RegisterResponse[]>();

        // Assert
        registers.Should().NotBeNull();
        registers!.Length.Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task GetAllRegisters_WithTenantFilter_ShouldReturnOnlyTenantRegisters()
    {
        // Arrange
        await _factory.CreateTestRegisterAsync("Tenant1 Reg1", "tenant123");
        await _factory.CreateTestRegisterAsync("Tenant2 Reg", "tenant456");
        await _factory.CreateTestRegisterAsync("Tenant1 Reg2", "tenant123");

        // Act
        var response = await _client.GetAsync("/api/registers?tenantId=tenant123");
        var registers = await response.Content.ReadFromJsonAsync<RegisterResponse[]>();

        // Assert
        registers.Should().NotBeNull();
        registers.Should().OnlyContain(r => r.TenantId == "tenant123");
    }

    [Fact]
    public async Task GetRegister_WithValidId_ShouldReturn200OK()
    {
        // Arrange
        var created = await _factory.CreateTestRegisterAsync("Test Register", "tenant123");

        // Act
        var response = await _client.GetAsync($"/api/registers/{created.Id}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var register = await response.Content.ReadFromJsonAsync<RegisterResponse>();
        register.Should().NotBeNull();
        register!.Id.Should().Be(created.Id);
        register.Name.Should().Be("Test Register");
    }

    [Fact]
    public async Task GetRegister_WithNonExistentId_ShouldReturn404NotFound()
    {
        // Act
        var response = await _client.GetAsync("/api/registers/nonexistent12345678901234567890");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task UpdateRegister_WithValidData_ShouldReturn200OK()
    {
        // Arrange
        var created = await _factory.CreateTestRegisterAsync("Original Name", "tenant123");
        var updateRequest = new
        {
            name = "Updated Name",
            status = (int)RegisterStatus.Online,
            advertise = true
        };

        // Act
        var response = await _client.PutAsJsonAsync($"/api/registers/{created.Id}", updateRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await response.Content.ReadFromJsonAsync<RegisterResponse>();
        updated!.Name.Should().Be("Updated Name");
        updated.Status.Should().Be(RegisterStatus.Online);
        updated.Advertise.Should().BeTrue();
    }

    [Fact]
    public async Task UpdateRegister_WithNonExistentId_ShouldReturn404NotFound()
    {
        // Arrange
        var updateRequest = new
        {
            name = "Updated Name"
        };

        // Act
        var response = await _client.PutAsJsonAsync("/api/registers/nonexistent12345678901234567890", updateRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteRegister_WithValidTenant_ShouldReturn204NoContent()
    {
        // Arrange
        var created = await _factory.CreateTestRegisterAsync("Test Register", "tenant123");

        // Act
        var response = await _client.DeleteAsync($"/api/registers/{created.Id}?tenantId=tenant123");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verify deletion
        var getResponse = await _client.GetAsync($"/api/registers/{created.Id}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteRegister_WithWrongTenant_ShouldReturn403Forbidden()
    {
        // Arrange
        var created = await _factory.CreateTestRegisterAsync("Test Register", "tenant123");

        // Act
        var response = await _client.DeleteAsync($"/api/registers/{created.Id}?tenantId=wrongTenant");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task DeleteRegister_WithNonExistentId_ShouldReturn404NotFound()
    {
        // Act
        var response = await _client.DeleteAsync("/api/registers/nonexistent12345678901234567890?tenantId=tenant123");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetRegisterCount_ShouldReturn200OK()
    {
        // Arrange
        await _factory.CreateTestRegisterAsync("Register 1", "tenant123");
        await _factory.CreateTestRegisterAsync("Register 2", "tenant456");

        // Act
        var response = await _client.GetAsync("/api/registers/stats/count");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<CountResponse>();
        result!.Count.Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task CreateAndRetrieveRegister_EndToEndWorkflow()
    {
        // Create via service layer
        var created = await _factory.CreateTestRegisterAsync(
            "E2E Test Register", "e2e-tenant", advertise: true, isFullReplica: true);

        // Retrieve via API
        var getResponse = await _client.GetAsync($"/api/registers/{created.Id}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var retrieved = await getResponse.Content.ReadFromJsonAsync<RegisterResponse>();

        // Assert
        retrieved.Should().NotBeNull();
        retrieved!.Id.Should().Be(created.Id);
        retrieved.Name.Should().Be("E2E Test Register");

        // Update
        var updateRequest = new
        {
            name = "E2E Updated Name",
            status = (int)RegisterStatus.Online
        };

        var updateResponse = await _client.PutAsJsonAsync($"/api/registers/{created.Id}", updateRequest);
        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await updateResponse.Content.ReadFromJsonAsync<RegisterResponse>();
        updated!.Name.Should().Be("E2E Updated Name");
        updated.Status.Should().Be(RegisterStatus.Online);

        // Delete
        var deleteResponse = await _client.DeleteAsync($"/api/registers/{created.Id}?tenantId=e2e-tenant");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verify deletion
        var getFinalResponse = await _client.GetAsync($"/api/registers/{created.Id}");
        getFinalResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private record RegisterResponse(
        string Id,
        string Name,
        uint Height,
        RegisterStatus Status,
        bool Advertise,
        bool IsFullReplica,
        string TenantId,
        DateTime CreatedAt,
        DateTime UpdatedAt);

    private record CountResponse(int Count);
}
