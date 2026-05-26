// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using Microsoft.Extensions.Logging;
using Sorcha.McpServer.Infrastructure;
using Sorcha.McpServer.Services;
using Sorcha.McpServer.Tools.Admin;
using Sorcha.ServiceClients.Tenant;

namespace Sorcha.McpServer.Tests.Tools.Admin;

/// <summary>
/// Spec 139 US4: TenantListTool reads via the typed <see cref="ITenantServiceClient"/>
/// (route pinned, caller token forwarded), so these tests mock the client rather than HTTP.
/// </summary>
public class TenantListToolTests
{
    private readonly Mock<IMcpAuthorizationService> _authServiceMock = new();
    private readonly Mock<IServiceAvailabilityTracker> _availabilityTrackerMock = new();
    private readonly Mock<ITenantServiceClient> _tenantClientMock = new();

    private TenantListTool CreateTool() => new(
        _authServiceMock.Object,
        _availabilityTrackerMock.Object,
        _tenantClientMock.Object,
        Mock.Of<ILogger<TenantListTool>>());

    private void Allow()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_tenant_list")).Returns(true);
        _availabilityTrackerMock.Setup(a => a.IsServiceAvailable("Tenant")).Returns(true);
    }

    [Fact]
    public async Task ListTenantsAsync_Unauthorized_ReturnsUnauthorizedResult()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_tenant_list")).Returns(false);

        var result = await CreateTool().ListTenantsAsync();

        result.Status.Should().Be("Unauthorized");
    }

    [Fact]
    public async Task ListTenantsAsync_ServiceUnavailable_ReturnsUnavailableResult()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_tenant_list")).Returns(true);
        _availabilityTrackerMock.Setup(a => a.IsServiceAvailable("Tenant")).Returns(false);

        var result = await CreateTool().ListTenantsAsync();

        result.Status.Should().Be("Unavailable");
    }

    [Fact]
    public async Task ListTenantsAsync_InvalidStatus_ReturnsError()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_tenant_list")).Returns(true);

        var result = await CreateTool().ListTenantsAsync(status: "Bogus");

        result.Status.Should().Be("Error");
    }

    [Fact]
    public async Task ListTenantsAsync_Success_ReturnsTenants()
    {
        Allow();

        var response = JsonSerializer.Serialize(new
        {
            Items = new[]
            {
                new { OrganizationId = "tenant-1", Name = "Tenant One", Status = "Active", UserCount = 10, BlueprintCount = 5, CreatedAt = DateTimeOffset.UtcNow.AddDays(-30), LastActivityAt = DateTimeOffset.UtcNow.AddHours(-1) },
                new { OrganizationId = "tenant-2", Name = "Tenant Two", Status = "Suspended", UserCount = 5, BlueprintCount = 2, CreatedAt = DateTimeOffset.UtcNow.AddDays(-60), LastActivityAt = DateTimeOffset.UtcNow.AddDays(-5) }
            },
            TotalCount = 2, Page = 1, PageSize = 20, TotalPages = 1
        });
        _tenantClientMock
            .Setup(c => c.ListOrganizationsAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        var result = await CreateTool().ListTenantsAsync();

        result.Status.Should().Be("Success");
        result.Tenants.Should().HaveCount(2);
        result.Tenants[0].TenantId.Should().Be("tenant-1");
        result.Tenants[0].Name.Should().Be("Tenant One");
        result.Tenants[0].UserCount.Should().Be(10);
        result.TotalCount.Should().Be(2);
        _availabilityTrackerMock.Verify(a => a.RecordSuccess("Tenant"), Times.Once);
    }

    [Fact]
    public async Task ListTenantsAsync_WithFilters_IncludesQueryParameters()
    {
        Allow();
        _tenantClientMock
            .Setup(c => c.ListOrganizationsAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(JsonSerializer.Serialize(new { Items = Array.Empty<object>(), TotalCount = 0, Page = 1, PageSize = 20, TotalPages = 0 }));

        await CreateTool().ListTenantsAsync(status: "Active", search: "acme");

        _tenantClientMock.Verify(
            c => c.ListOrganizationsAsync(It.Is<string>(q => q.Contains("status=Active") && q.Contains("search=acme")), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ListTenantsAsync_PageSizeExceeds100_ClampedTo100()
    {
        Allow();
        _tenantClientMock
            .Setup(c => c.ListOrganizationsAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(JsonSerializer.Serialize(new { Items = Array.Empty<object>(), TotalCount = 0, Page = 1, PageSize = 100, TotalPages = 0 }));

        await CreateTool().ListTenantsAsync(pageSize: 500);

        _tenantClientMock.Verify(
            c => c.ListOrganizationsAsync(It.Is<string>(q => q.Contains("pageSize=100")), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ListTenantsAsync_Null_ReturnsErrorResult()
    {
        Allow();
        _tenantClientMock
            .Setup(c => c.ListOrganizationsAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var result = await CreateTool().ListTenantsAsync();

        result.Status.Should().Be("Error");
    }

    [Fact]
    public async Task ListTenantsAsync_Timeout_ReturnsTimeoutResult()
    {
        Allow();
        _tenantClientMock
            .Setup(c => c.ListOrganizationsAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TaskCanceledException());

        var result = await CreateTool().ListTenantsAsync();

        result.Status.Should().Be("Timeout");
    }
}
