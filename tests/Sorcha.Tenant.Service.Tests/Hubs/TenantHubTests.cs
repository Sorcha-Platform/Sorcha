// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sorcha.Tenant.Service.Hubs;
using Xunit;

namespace Sorcha.Tenant.Service.Tests.Hubs;

/// <summary>
/// Feature 118 T033 — covers <see cref="TenantHub"/>'s connect path:
/// platform_user_id claim guard, group membership, abort on missing claim.
/// </summary>
public class TenantHubTests
{
    [Fact]
    public async Task OnConnectedAsync_ValidClaim_AddsToUserGroup()
    {
        var platformUserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var (hub, groups, context) = BuildHub(authenticated: true, platformUserId);

        await hub.OnConnectedAsync();

        var expectedGroup = TenantHubGroups.User(platformUserId);
        groups.Verify(
            g => g.AddToGroupAsync("conn-1", expectedGroup, It.IsAny<CancellationToken>()),
            Times.Once);
        context.Verify(c => c.Abort(), Times.Never);
    }

    [Fact]
    public async Task OnConnectedAsync_MissingClaim_AbortsAndDoesNotJoinGroup()
    {
        var (hub, groups, context) = BuildHub(authenticated: true, platformUserId: null);

        await hub.OnConnectedAsync();

        context.Verify(c => c.Abort(), Times.Once);
        groups.Verify(
            g => g.AddToGroupAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task OnConnectedAsync_InvalidGuidClaim_AbortsAndDoesNotJoinGroup()
    {
        var (hub, groups, context) = BuildHub(authenticated: true, claimValue: "not-a-guid");

        await hub.OnConnectedAsync();

        context.Verify(c => c.Abort(), Times.Once);
        groups.Verify(
            g => g.AddToGroupAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public void GroupName_FollowsUserBuilderConvention()
    {
        var platformUserId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

        var name = TenantHubGroups.User(platformUserId);

        name.Should().Be($"user:{platformUserId:N}");
    }

    private static (TenantHub Hub, Mock<IGroupManager> Groups, Mock<HubCallerContext> Context)
        BuildHub(bool authenticated, Guid? platformUserId = null, string? claimValue = null)
    {
        var hub = new TenantHub(NullLogger<TenantHub>.Instance);

        var contextMock = new Mock<HubCallerContext>();
        contextMock.Setup(c => c.ConnectionId).Returns("conn-1");

        var identity = authenticated ? new ClaimsIdentity("Bearer") : new ClaimsIdentity();
        var resolvedClaim = claimValue ?? platformUserId?.ToString();
        if (resolvedClaim is not null)
        {
            identity.AddClaim(new Claim(TenantHub.PlatformUserIdClaim, resolvedClaim));
        }
        contextMock.Setup(c => c.User).Returns(new ClaimsPrincipal(identity));

        var groupsMock = new Mock<IGroupManager>();
        groupsMock
            .Setup(g => g.AddToGroupAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        hub.Context = contextMock.Object;
        hub.Groups = groupsMock.Object;

        return (hub, groupsMock, contextMock);
    }
}
