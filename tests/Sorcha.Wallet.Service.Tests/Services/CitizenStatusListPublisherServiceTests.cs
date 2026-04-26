// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Sorcha.Wallet.Core.Data;
using Sorcha.Wallet.Core.Domain.Entities;
using Sorcha.Wallet.Service.Services.Implementation;
using Sorcha.Wallet.Service.Services.Interfaces;
using Xunit;

namespace Sorcha.Wallet.Service.Tests.Services;

/// <summary>
/// Tests for <see cref="CitizenStatusListPublisherService"/> (Feature 114, T080).
/// Drives the worker via the internal <c>RunOnceAsync</c> seam to keep things
/// deterministic — no <see cref="PeriodicTimer"/> wait, no real clock.
/// </summary>
public sealed class CitizenStatusListPublisherServiceTests : IAsyncDisposable
{
    private readonly TestCitizenWalletDbContext _db;
    private readonly Mock<ICitizenStatusListPublisher> _publisher = new();
    private readonly Mock<IOrgStatusSigningWalletResolver> _resolver = new();
    private readonly ServiceProvider _sp;
    private readonly CitizenStatusListPublisherService _worker;

    public CitizenStatusListPublisherServiceTests()
    {
        var options = new DbContextOptionsBuilder<TestCitizenWalletDbContext>()
            .UseInMemoryDatabase($"PublisherWorker-{Guid.NewGuid():N}")
            .Options;
        _db = new TestCitizenWalletDbContext(options);

        var services = new ServiceCollection();
        services.AddSingleton<WalletDbContext>(_db);
        services.AddSingleton(_publisher.Object);
        services.AddSingleton(_resolver.Object);
        _sp = services.BuildServiceProvider();

        _worker = new CitizenStatusListPublisherService(
            _sp.GetRequiredService<IServiceScopeFactory>(),
            Mock.Of<ILogger<CitizenStatusListPublisherService>>());
    }

    public async ValueTask DisposeAsync()
    {
        await _sp.DisposeAsync();
        await _db.DisposeAsync();
    }

    private static CitizenDeviceStatusList MakeList(Guid orgId, int listId, DateTimeOffset expiresAt) => new()
    {
        Id = Guid.NewGuid(),
        OrganizationId = orgId,
        ListId = listId,
        Capacity = 32_768,
        Bitstring = new byte[32_768 / 8],
        ExpiresAt = expiresAt,
        GeneratedAt = expiresAt.AddHours(-24),
        SignedJwt = "stale.jwt"
    };

    [Fact]
    public async Task RunOnceAsync_NoListsDue_DoesNothing()
    {
        var orgId = Guid.NewGuid();
        _db.CitizenDeviceStatusLists.Add(MakeList(orgId, 0, DateTimeOffset.UtcNow.AddHours(12)));
        await _db.SaveChangesAsync();

        await _worker.RunOnceAsync(CancellationToken.None);

        _publisher.Verify(p => p.RegenerateAsync(
            It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _resolver.Verify(r => r.ResolveAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RunOnceAsync_ListWithinLookAhead_Regenerates()
    {
        var orgId = Guid.NewGuid();
        _db.CitizenDeviceStatusLists.Add(MakeList(orgId, 0, DateTimeOffset.UtcNow.AddMinutes(30)));
        await _db.SaveChangesAsync();

        _resolver.Setup(r => r.ResolveAsync(orgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync("ws1qorgsigner1");

        await _worker.RunOnceAsync(CancellationToken.None);

        _publisher.Verify(p => p.RegenerateAsync(orgId, 0, "ws1qorgsigner1", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RunOnceAsync_MultipleListsSameOrg_ReusesResolverResult()
    {
        var orgId = Guid.NewGuid();
        _db.CitizenDeviceStatusLists.Add(MakeList(orgId, 0, DateTimeOffset.UtcNow.AddMinutes(15)));
        _db.CitizenDeviceStatusLists.Add(MakeList(orgId, 1, DateTimeOffset.UtcNow.AddMinutes(20)));
        _db.CitizenDeviceStatusLists.Add(MakeList(orgId, 2, DateTimeOffset.UtcNow.AddMinutes(25)));
        await _db.SaveChangesAsync();

        _resolver.Setup(r => r.ResolveAsync(orgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync("ws1qsigner");

        await _worker.RunOnceAsync(CancellationToken.None);

        // The worker caches the org→signer lookup per tick.
        _resolver.Verify(r => r.ResolveAsync(orgId, It.IsAny<CancellationToken>()), Times.Once);
        _publisher.Verify(p => p.RegenerateAsync(orgId, It.IsAny<int>(), "ws1qsigner", It.IsAny<CancellationToken>()),
            Times.Exactly(3));
    }

    [Fact]
    public async Task RunOnceAsync_OneListThrows_OthersStillRegenerated()
    {
        var orgA = Guid.NewGuid();
        var orgB = Guid.NewGuid();
        _db.CitizenDeviceStatusLists.Add(MakeList(orgA, 0, DateTimeOffset.UtcNow.AddMinutes(10)));
        _db.CitizenDeviceStatusLists.Add(MakeList(orgB, 0, DateTimeOffset.UtcNow.AddMinutes(10)));
        await _db.SaveChangesAsync();

        _resolver.Setup(r => r.ResolveAsync(orgA, It.IsAny<CancellationToken>())).ReturnsAsync("ws1qa");
        _resolver.Setup(r => r.ResolveAsync(orgB, It.IsAny<CancellationToken>())).ReturnsAsync("ws1qb");

        _publisher.Setup(p => p.RegenerateAsync(orgA, 0, "ws1qa", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("kaboom"));

        await _worker.RunOnceAsync(CancellationToken.None);

        // orgB's list still gets regenerated despite orgA's failure.
        _publisher.Verify(p => p.RegenerateAsync(orgB, 0, "ws1qb", It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
