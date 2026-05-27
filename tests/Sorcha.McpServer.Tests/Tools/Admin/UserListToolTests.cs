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
/// Spec 139 US4: UserListTool reads via the typed <see cref="ITenantServiceClient"/>
/// (route pinned, caller token forwarded), so these tests mock the client rather than HTTP.
/// </summary>
public class UserListToolTests
{
    private readonly Mock<IMcpAuthorizationService> _authServiceMock = new();
    private readonly Mock<IServiceAvailabilityTracker> _availabilityTrackerMock = new();
    private readonly Mock<ITenantServiceClient> _tenantClientMock = new();

    private UserListTool CreateTool() => new(
        _authServiceMock.Object,
        _availabilityTrackerMock.Object,
        _tenantClientMock.Object,
        Mock.Of<ILogger<UserListTool>>());

    private void Allow()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_user_list")).Returns(true);
        _availabilityTrackerMock.Setup(a => a.IsServiceAvailable("Tenant")).Returns(true);
    }

    [Fact]
    public async Task ListUsersAsync_Unauthorized_ReturnsUnauthorizedResult()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_user_list")).Returns(false);

        var result = await CreateTool().ListUsersAsync();

        result.Status.Should().Be("Unauthorized");
    }

    [Fact]
    public async Task ListUsersAsync_ServiceUnavailable_ReturnsUnavailableResult()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_user_list")).Returns(true);
        _availabilityTrackerMock.Setup(a => a.IsServiceAvailable("Tenant")).Returns(false);

        var result = await CreateTool().ListUsersAsync();

        result.Status.Should().Be("Unavailable");
    }

    [Fact]
    public async Task ListUsersAsync_InvalidRole_ReturnsError()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_user_list")).Returns(true);

        var result = await CreateTool().ListUsersAsync(role: "Bogus");

        result.Status.Should().Be("Error");
    }

    [Fact]
    public async Task ListUsersAsync_InvalidStatus_ReturnsError()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_user_list")).Returns(true);

        var result = await CreateTool().ListUsersAsync(status: "Bogus");

        result.Status.Should().Be("Error");
    }

    [Fact]
    public async Task ListUsersAsync_Success_ReturnsUsers()
    {
        Allow();

        var response = JsonSerializer.Serialize(new
        {
            Items = new[]
            {
                new { UserId = "user-1", Email = "user1@test.com", DisplayName = "User One", OrganizationId = "tenant-1", OrganizationName = "Tenant One", Roles = new[] { "Admin" }, Status = "Active", LastLoginAt = DateTimeOffset.UtcNow.AddHours(-1), CreatedAt = DateTimeOffset.UtcNow.AddDays(-30) },
                new { UserId = "user-2", Email = "user2@test.com", DisplayName = "User Two", OrganizationId = "tenant-1", OrganizationName = "Tenant One", Roles = new[] { "Designer" }, Status = "Active", LastLoginAt = DateTimeOffset.UtcNow.AddDays(-1), CreatedAt = DateTimeOffset.UtcNow.AddDays(-60) }
            },
            TotalCount = 2, Page = 1, PageSize = 20, TotalPages = 1
        });
        _tenantClientMock
            .Setup(c => c.ListUsersAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        var result = await CreateTool().ListUsersAsync();

        result.Status.Should().Be("Success");
        result.Users.Should().HaveCount(2);
        result.Users[0].UserId.Should().Be("user-1");
        result.Users[0].Email.Should().Be("user1@test.com");
        result.TotalCount.Should().Be(2);
        _availabilityTrackerMock.Verify(a => a.RecordSuccess("Tenant"), Times.Once);
    }

    [Fact]
    public async Task ListUsersAsync_WithFilters_IncludesQueryParameters()
    {
        Allow();
        _tenantClientMock
            .Setup(c => c.ListUsersAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(JsonSerializer.Serialize(new { Items = Array.Empty<object>(), TotalCount = 0, Page = 1, PageSize = 20, TotalPages = 0 }));

        await CreateTool().ListUsersAsync(tenantId: "t-1", role: "Admin", status: "Active", search: "jane");

        _tenantClientMock.Verify(
            c => c.ListUsersAsync(It.Is<string>(q => q.Contains("organizationId=t-1") && q.Contains("role=Admin") && q.Contains("status=Active") && q.Contains("search=jane")), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ListUsersAsync_PageSizeExceeds100_ClampedTo100()
    {
        Allow();
        _tenantClientMock
            .Setup(c => c.ListUsersAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(JsonSerializer.Serialize(new { Items = Array.Empty<object>(), TotalCount = 0, Page = 1, PageSize = 100, TotalPages = 0 }));

        await CreateTool().ListUsersAsync(pageSize: 500);

        _tenantClientMock.Verify(
            c => c.ListUsersAsync(It.Is<string>(q => q.Contains("pageSize=100")), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ListUsersAsync_Null_ReturnsErrorResult()
    {
        Allow();
        _tenantClientMock
            .Setup(c => c.ListUsersAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var result = await CreateTool().ListUsersAsync();

        result.Status.Should().Be("Error");
    }

    [Fact]
    public async Task ListUsersAsync_Timeout_ReturnsTimeoutResult()
    {
        Allow();
        _tenantClientMock
            .Setup(c => c.ListUsersAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TaskCanceledException());

        var result = await CreateTool().ListUsersAsync();

        result.Status.Should().Be("Timeout");
    }
}
