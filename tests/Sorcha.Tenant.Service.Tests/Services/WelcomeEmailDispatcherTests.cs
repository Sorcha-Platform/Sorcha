// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sorcha.Tenant.Service.Data;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Services;

namespace Sorcha.Tenant.Service.Tests.Services;

/// <summary>
/// Unit tests for <see cref="WelcomeEmailDispatcher"/>: idempotency, pre-condition
/// guards, public-vs-invited variant selection, and non-throwing error handling.
/// Uses SQLite in-memory because the dispatcher's atomic welcome-reservation relies
/// on <c>ExecuteUpdateAsync</c>, which is NOT supported by the EF Core in-memory
/// provider (throws <c>InvalidOperationException</c> at runtime).
/// </summary>
public class WelcomeEmailDispatcherTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly TenantDbContext _dbContext;
    private readonly Mock<ITransactionalEmailService> _transactional = new();
    private readonly WelcomeEmailDispatcher _dispatcher;
    private readonly ILogger<WelcomeEmailDispatcher> _logger = NullLogger<WelcomeEmailDispatcher>.Instance;

    public WelcomeEmailDispatcherTests()
    {
        // SQLite in-memory, ":memory:" with a shared connection so the schema persists
        // across DbContext operations within one test.
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<TenantDbContext>()
            .UseSqlite(_connection)
            .Options;
        _dbContext = new TenantDbContext(options);
        _dbContext.Database.EnsureCreated();
        _dispatcher = new WelcomeEmailDispatcher(_dbContext, _transactional.Object, _logger);
    }

    private async Task<PlatformUser> SeedUserAsync(bool verified = true)
    {
        var user = new PlatformUser
        {
            Id = Guid.NewGuid(),
            Email = $"user-{Guid.NewGuid():N}@test.com",
            DisplayName = "Test User",
            EmailVerified = verified,
        };
        _dbContext.PlatformUsers.Add(user);
        await _dbContext.SaveChangesAsync();
        return user;
    }

    [Fact]
    public async Task SendIfPendingAsync_VerifiedUserNeverWelcomed_SendsAndSetsWelcomeSentAt()
    {
        var user = await SeedUserAsync(verified: true);

        await _dispatcher.SendIfPendingAsync(user, CancellationToken.None);

        _transactional.Verify(t => t.SendWelcomeAsync(
            It.Is<WelcomeDispatchContext>(c => c.User.Id == user.Id && c.Variant == WelcomeVariant.Public),
            It.IsAny<CancellationToken>()), Times.Once);

        var reloaded = await _dbContext.PlatformUsers.AsNoTracking().FirstAsync(u => u.Id == user.Id);
        reloaded.WelcomeSentAt.Should().NotBeNull();
    }

    [Fact]
    public async Task SendIfPendingAsync_AlreadyWelcomed_IsNoOp()
    {
        var user = await SeedUserAsync(verified: true);
        user.WelcomeSentAt = DateTimeOffset.UtcNow.AddDays(-1);
        await _dbContext.SaveChangesAsync();

        await _dispatcher.SendIfPendingAsync(user, CancellationToken.None);

        _transactional.Verify(t => t.SendWelcomeAsync(
            It.IsAny<WelcomeDispatchContext>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SendIfPendingAsync_UnverifiedUser_IsNoOp()
    {
        var user = await SeedUserAsync(verified: false);

        await _dispatcher.SendIfPendingAsync(user, CancellationToken.None);

        _transactional.Verify(t => t.SendWelcomeAsync(
            It.IsAny<WelcomeDispatchContext>(), It.IsAny<CancellationToken>()), Times.Never);

        var reloaded = await _dbContext.PlatformUsers.AsNoTracking().FirstAsync(u => u.Id == user.Id);
        reloaded.WelcomeSentAt.Should().BeNull();
    }

    [Fact]
    public async Task SendIfPendingAsync_PublicOrgMembershipOnly_ChoosesPublicVariant()
    {
        var user = await SeedUserAsync(verified: true);
        // SQLite enforces FK — seed the public org row before the membership.
        _dbContext.Organizations.Add(new Organization
        {
            Id = WellKnownIds.PublicOrgId,
            Name = "Public",
            Subdomain = "public",
        });
        _dbContext.PlatformUserOrgMemberships.Add(new PlatformUserOrgMembership
        {
            PlatformUserId = user.Id,
            OrganizationId = WellKnownIds.PublicOrgId,
            Role = "Consumer",
            JoinedAt = DateTimeOffset.UtcNow,
        });
        await _dbContext.SaveChangesAsync();

        await _dispatcher.SendIfPendingAsync(user, CancellationToken.None);

        _transactional.Verify(t => t.SendWelcomeAsync(
            It.Is<WelcomeDispatchContext>(c =>
                c.Variant == WelcomeVariant.Public &&
                c.InvitingOrganization == null),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendIfPendingAsync_StandardOrgMembership_ChoosesInvitedVariant()
    {
        var user = await SeedUserAsync(verified: true);
        var org = new Organization { Id = Guid.NewGuid(), Name = "Acme", Subdomain = "acme" };
        _dbContext.Organizations.Add(org);
        _dbContext.PlatformUserOrgMemberships.Add(new PlatformUserOrgMembership
        {
            PlatformUserId = user.Id,
            OrganizationId = org.Id,
            Role = "Designer",
            JoinedAt = DateTimeOffset.UtcNow,
        });
        await _dbContext.SaveChangesAsync();

        await _dispatcher.SendIfPendingAsync(user, CancellationToken.None);

        _transactional.Verify(t => t.SendWelcomeAsync(
            It.Is<WelcomeDispatchContext>(c =>
                c.Variant == WelcomeVariant.Invited &&
                c.InvitingOrganization != null &&
                c.InvitingOrganization.Id == org.Id),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendIfPendingAsync_MultipleStandardOrgs_PicksEarliestJoined()
    {
        var user = await SeedUserAsync(verified: true);
        var older = new Organization { Id = Guid.NewGuid(), Name = "Older Co.", Subdomain = "older" };
        var newer = new Organization { Id = Guid.NewGuid(), Name = "Newer Co.", Subdomain = "newer" };
        _dbContext.Organizations.AddRange(older, newer);
        _dbContext.PlatformUserOrgMemberships.AddRange(
            new PlatformUserOrgMembership
            {
                PlatformUserId = user.Id,
                OrganizationId = newer.Id,
                Role = "Consumer",
                JoinedAt = DateTimeOffset.UtcNow,
            },
            new PlatformUserOrgMembership
            {
                PlatformUserId = user.Id,
                OrganizationId = older.Id,
                Role = "Designer",
                JoinedAt = DateTimeOffset.UtcNow.AddDays(-30),
            });
        await _dbContext.SaveChangesAsync();

        await _dispatcher.SendIfPendingAsync(user, CancellationToken.None);

        _transactional.Verify(t => t.SendWelcomeAsync(
            It.Is<WelcomeDispatchContext>(c =>
                c.InvitingOrganization != null &&
                c.InvitingOrganization.Id == older.Id),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendIfPendingAsync_TransactionalThrowsAfterReservation_SwallowsAndKeepsReservationToPreventDoubleSend()
    {
        // With the atomic ExecuteUpdateAsync reservation (reviewer C-1/C-2/M-6 fix),
        // we set WelcomeSentAt BEFORE calling the transactional service. If the send
        // then fails, the reservation is DELIBERATELY kept — it prevents double-sending
        // on a retry, at the cost of losing one welcome email that operators can
        // reconcile from the structured error log. This is the explicit tradeoff
        // documented in the dispatcher's implementation.
        var user = await SeedUserAsync(verified: true);
        _transactional
            .Setup(t => t.SendWelcomeAsync(It.IsAny<WelcomeDispatchContext>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("SMTP down"));

        Func<Task> act = () => _dispatcher.SendIfPendingAsync(user, CancellationToken.None);

        await act.Should().NotThrowAsync();

        var reloaded = await _dbContext.PlatformUsers.AsNoTracking().FirstAsync(u => u.Id == user.Id);
        reloaded.WelcomeSentAt.Should().NotBeNull(
            "the atomic reservation must survive send failure so a retry can't double-send");
    }


    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
    }
}
