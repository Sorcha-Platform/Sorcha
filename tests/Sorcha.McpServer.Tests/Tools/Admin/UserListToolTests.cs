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
/// Spec 139 US4 / MCP P0 Task 5: UserListTool reads via the typed <see cref="ITenantServiceClient"/>
/// (route pinned, caller token forwarded), so these tests mock the client rather than HTTP.
/// </summary>
public class UserListToolTests
{
    private const string OrgId = "11111111-1111-1111-1111-111111111111";

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

        var result = await CreateTool().ListUsersAsync(OrgId);

        result.Status.Should().Be("Unauthorized");
    }

    [Fact]
    public async Task ListUsersAsync_ServiceUnavailable_ReturnsUnavailableResult()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_user_list")).Returns(true);
        _availabilityTrackerMock.Setup(a => a.IsServiceAvailable("Tenant")).Returns(false);

        var result = await CreateTool().ListUsersAsync(OrgId);

        result.Status.Should().Be("Unavailable");
    }

    [Fact]
    public async Task ListUsersAsync_MissingOrganizationId_ReturnsError()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_user_list")).Returns(true);

        var result = await CreateTool().ListUsersAsync("");

        result.Status.Should().Be("Error");
    }

    [Fact]
    public async Task ListUsersAsync_NonGuidOrganizationId_ReturnsError()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_user_list")).Returns(true);

        var result = await CreateTool().ListUsersAsync("not-a-guid");

        result.Status.Should().Be("Error");
        result.Message.Should().Contain("GUID");
    }

    [Fact]
    public async Task ListUsersAsync_Success_ReturnsUsers()
    {
        Allow();

        var response = JsonSerializer.Serialize(new
        {
            Users = new[]
            {
                new
                {
                    Id = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
                    OrganizationId = OrgId,
                    Email = "user1@test.com",
                    DisplayName = "User One",
                    Roles = new[] { "Administrator" },
                    Status = "Active",
                    CreatedAt = DateTimeOffset.UtcNow.AddDays(-30),
                    LastLoginAt = DateTimeOffset.UtcNow.AddHours(-1),
                    EmailVerified = true,
                    ProvisionedVia = "Local",
                    ProfileCompleted = true
                },
                new
                {
                    Id = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
                    OrganizationId = OrgId,
                    Email = "user2@test.com",
                    DisplayName = "User Two",
                    Roles = new[] { "Designer" },
                    Status = "Active",
                    CreatedAt = DateTimeOffset.UtcNow.AddDays(-60),
                    LastLoginAt = DateTimeOffset.UtcNow.AddDays(-1),
                    EmailVerified = false,
                    ProvisionedVia = "Invitation",
                    ProfileCompleted = false
                }
            },
            TotalCount = 2,
            PendingInvitations = Array.Empty<object>(),
            PendingInvitationCount = 0
        });
        _tenantClientMock
            .Setup(c => c.ListUsersAsync(OrgId, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        var result = await CreateTool().ListUsersAsync(OrgId);

        result.Status.Should().Be("Success");
        result.Users.Should().HaveCount(2);
        result.Users[0].UserId.Should().Be("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        result.Users[0].Email.Should().Be("user1@test.com");
        result.Users[0].Roles.Should().Contain("Administrator");
        result.TotalCount.Should().Be(2);
        _availabilityTrackerMock.Verify(a => a.RecordSuccess("Tenant"), Times.Once);
    }

    [Fact]
    public async Task ListUsersAsync_WithFilters_IncludesQueryParameters()
    {
        Allow();
        _tenantClientMock
            .Setup(c => c.ListUsersAsync(OrgId, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(JsonSerializer.Serialize(new { Users = Array.Empty<object>(), TotalCount = 0, PendingInvitations = Array.Empty<object>(), PendingInvitationCount = 0 }));

        await CreateTool().ListUsersAsync(OrgId, includeInactive: true, emailVerified: true, provisionedVia: "Local", includePending: true);

        _tenantClientMock.Verify(
            c => c.ListUsersAsync(
                OrgId,
                It.Is<string>(q =>
                    q.Contains("includeInactive=True") &&
                    q.Contains("emailVerified=True") &&
                    q.Contains("provisionedVia=Local") &&
                    q.Contains("includePending=true")),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ListUsersAsync_Null_ReturnsErrorResult()
    {
        Allow();
        _tenantClientMock
            .Setup(c => c.ListUsersAsync(OrgId, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var result = await CreateTool().ListUsersAsync(OrgId);

        result.Status.Should().Be("Error");
    }

    [Fact]
    public async Task ListUsersAsync_Timeout_ReturnsTimeoutResult()
    {
        Allow();
        _tenantClientMock
            .Setup(c => c.ListUsersAsync(OrgId, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TaskCanceledException());

        var result = await CreateTool().ListUsersAsync(OrgId);

        result.Status.Should().Be("Timeout");
    }
}
