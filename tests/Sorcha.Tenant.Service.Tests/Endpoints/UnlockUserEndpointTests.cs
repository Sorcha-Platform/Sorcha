// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Reflection;
using System.Security.Claims;
using FluentAssertions;
using Moq;
using Sorcha.Tenant.Service.Data;
using Sorcha.Tenant.Service.Data.Repositories;
using Sorcha.Tenant.Service.Endpoints;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Services;
using Sorcha.Tenant.Service.Tests.Helpers;
using Xunit;

namespace Sorcha.Tenant.Service.Tests.Endpoints;

/// <summary>
/// The admin "unlock account" endpoint. Brute-force lockout state lives on
/// <see cref="PlatformUser"/> (the cross-org identity anchor), not on the org-scoped
/// <see cref="UserIdentity"/> — <c>PlatformUserService.ValidatePasswordAsync</c> enforces tiered
/// thresholds and sets <c>FailedLoginCount</c> / <c>LockedUntil</c> / <c>LockedPermanently</c>.
///
/// The endpoint used to reactivate a Suspended status and nothing else, carrying a TODO saying
/// lockout "will be handled by PlatformUser in a future task". That task landed; the TODO did not
/// get revisited. So an admin unlocking a locked-out account cleared nothing, while the audit entry
/// recorded <c>AccountUnlockedByAdmin</c> as a success — the log asserted an unlock that had not
/// happened, which is worse than the endpoint plainly not existing.
/// </summary>
public class UnlockUserEndpointTests : IDisposable
{
    private readonly TenantDbContext _dbContext;
    private readonly Guid _orgId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public UnlockUserEndpointTests()
    {
        _dbContext = InMemoryDbContextFactory.Create();
    }

    public void Dispose() => _dbContext.Dispose();

    [Fact]
    public async Task UnlockUser_ClearsBruteForceLockoutStateOnPlatformUser()
    {
        var platformUser = LockedOutPlatformUser();
        var identity = IdentityFor(platformUser.Id);
        var platformUserService = ServiceReturning(platformUser);

        var result = await InvokeAsync(identity, platformUserService.Object);

        result.Should().NotBeNull();
        platformUser.FailedLoginCount.Should().Be(0);
        platformUser.LockedUntil.Should().BeNull();
        platformUser.LockedPermanently.Should().BeFalse();
        platformUserService.Verify(
            s => s.UpdateAsync(platformUser, It.IsAny<CancellationToken>()),
            Times.Once,
            "clearing the fields in memory is not an unlock unless it is persisted");
    }

    [Fact]
    public async Task UnlockUser_ClearsPermanentLockToo()
    {
        var platformUser = LockedOutPlatformUser();
        platformUser.LockedPermanently = true;
        platformUser.LockedUntil = null;
        var platformUserService = ServiceReturning(platformUser);

        await InvokeAsync(IdentityFor(platformUser.Id), platformUserService.Object);

        platformUser.LockedPermanently.Should().BeFalse(
            "a permanent lock is precisely the state an admin unlock exists to clear");
    }

    [Fact]
    public async Task UnlockUser_AlsoReactivatesASuspendedIdentity()
    {
        // The pre-existing behaviour must survive: unlock still un-suspends.
        var platformUser = LockedOutPlatformUser();
        var identity = IdentityFor(platformUser.Id);
        identity.Status = IdentityStatus.Suspended;

        await InvokeAsync(identity, ServiceReturning(platformUser).Object);

        identity.Status.Should().Be(IdentityStatus.Active);
    }

    [Fact]
    public async Task UnlockUser_MissingPlatformUser_StillSucceedsAndReactivates()
    {
        // A UserIdentity whose PlatformUser is absent is already an inconsistency; the status
        // reactivation is still worth doing, so this tolerates rather than faults.
        var identity = IdentityFor(Guid.NewGuid());
        identity.Status = IdentityStatus.Suspended;
        var platformUserService = new Mock<IPlatformUserService>();
        platformUserService
            .Setup(s => s.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PlatformUser?)null);

        var result = await InvokeAsync(identity, platformUserService.Object);

        result.Should().NotBeNull();
        identity.Status.Should().Be(IdentityStatus.Active);
        platformUserService.Verify(
            s => s.UpdateAsync(It.IsAny<PlatformUser>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UnlockUser_WritesTheAuditEntry()
    {
        var platformUser = LockedOutPlatformUser();

        await InvokeAsync(IdentityFor(platformUser.Id), ServiceReturning(platformUser).Object);

        _dbContext.AuditLogEntries.Should().ContainSingle(e =>
            e.EventType == AuditEventType.AccountUnlockedByAdmin && e.Success);
    }

    private static PlatformUser LockedOutPlatformUser() => new()
    {
        Id = Guid.NewGuid(),
        Email = "locked@example.com",
        DisplayName = "Locked User",
        FailedLoginCount = 12,
        LockedUntil = DateTimeOffset.UtcNow.AddHours(4),
        LockedPermanently = false,
    };

    private UserIdentity IdentityFor(Guid platformUserId) => new()
    {
        Id = Guid.NewGuid(),
        OrganizationId = _orgId,
        PlatformUserId = platformUserId,
        Email = "locked@example.com",
        DisplayName = "Locked User",
        Status = IdentityStatus.Active,
        Roles = [UserRole.Consumer],
        CreatedAt = DateTimeOffset.UtcNow,
    };

    private static Mock<IPlatformUserService> ServiceReturning(PlatformUser platformUser)
    {
        var mock = new Mock<IPlatformUserService>();
        mock.Setup(s => s.GetByIdAsync(platformUser.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(platformUser);
        mock.Setup(s => s.UpdateAsync(It.IsAny<PlatformUser>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return mock;
    }

    private async Task<object?> InvokeAsync(UserIdentity identity, IPlatformUserService platformUserService)
    {
        var identityRepository = new Mock<IIdentityRepository>();
        identityRepository
            .Setup(r => r.GetUserByIdAsync(identity.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(identity);
        identityRepository
            .Setup(r => r.UpdateUserAsync(It.IsAny<UserIdentity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserIdentity u, CancellationToken _) => u);

        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("sub", Guid.NewGuid().ToString())], "test"));

        var method = typeof(OrganizationEndpoints).GetMethod(
            "UnlockUser", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull("UnlockUser should be reachable for reflection-based endpoint testing");

        var task = (Task)method!.Invoke(null, [
            _orgId, identity.Id, identityRepository.Object, platformUserService,
            _dbContext, principal, CancellationToken.None,
        ])!;

        await task;
        return task.GetType().GetProperty("Result")?.GetValue(task);
    }
}
