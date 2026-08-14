// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Sorcha.Tenant.Service.Models.Dtos;
using Sorcha.Tenant.Service.Tests.Infrastructure;
using Xunit;

namespace Sorcha.Tenant.Service.Tests.Endpoints;

/// <summary>
/// Issue #1433 (seam bug #14): <c>GET /api/platform/organizations</c> declared <c>page</c>/
/// <c>pageSize</c> as REQUIRED minimal-API query parameters, so a bare request (no query string)
/// 400'd with "Required parameter 'int page' was not provided" instead of defaulting to page 1 —
/// contrary to the documented contract. Reflection-invoked endpoint tests can't see this class of
/// defect because they skip the model binder entirely; these tests go through the real ASP.NET Core
/// binding pipeline via <see cref="TenantServiceWebApplicationFactory"/>.
/// </summary>
public class PlatformOrgEndpointsTests : IClassFixture<TenantServiceWebApplicationFactory>, IAsyncLifetime
{
    private readonly TenantServiceWebApplicationFactory _factory;
    private HttpClient _client = null!;

    public PlatformOrgEndpointsTests(TenantServiceWebApplicationFactory factory)
    {
        _factory = factory;
    }

    public async ValueTask InitializeAsync()
    {
        _client = _factory.CreateSystemAdminClient();
        await _factory.SeedTestDataAsync();
    }

    public ValueTask DisposeAsync()
    {
        _client?.Dispose();
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task ListOrganizations_NoQueryParams_ReturnsOkWithPageOne()
    {
        // Act — bare request, no ?page=/&pageSize= at all.
        var response = await _client.GetAsync("/api/platform/organizations");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<PlatformOrgListResponse>();
        result.Should().NotBeNull();
        result!.Page.Should().Be(1);
        result.PageSize.Should().Be(50);
    }

    [Fact]
    public async Task ListOrganizations_ExplicitPageAndPageSize_HonoursValues()
    {
        // Act
        var response = await _client.GetAsync("/api/platform/organizations?page=2&pageSize=10");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<PlatformOrgListResponse>();
        result.Should().NotBeNull();
        result!.Page.Should().Be(2);
        result.PageSize.Should().Be(10);
    }

    [Fact]
    public async Task GetOrganizationUsers_NoQueryParams_ReturnsOkWithPageOne()
    {
        // Arrange
        var orgId = TestDataSeeder.TestOrganizationId;

        // Act — bare request, no ?page=/&pageSize= at all.
        var response = await _client.GetAsync($"/api/platform/organizations/{orgId}/users");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<OrgUserListResponse>();
        result.Should().NotBeNull();
        result!.Page.Should().Be(1);
        result.PageSize.Should().Be(20);
    }
}
