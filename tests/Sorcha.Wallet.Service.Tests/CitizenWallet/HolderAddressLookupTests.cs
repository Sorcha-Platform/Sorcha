// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Sorcha.Wallet.Core.Domain.Entities;
using Sorcha.Wallet.Service.Services.Implementation;
using Sorcha.Wallet.Service.Tests.Services;

namespace Sorcha.Wallet.Service.Tests.CitizenWallet;

/// <summary>
/// Feature 114 / US4 — unit coverage for <see cref="EfCoreHolderAddressLookup"/>.
/// </summary>
public class HolderAddressLookupTests
{
    private static (EfCoreHolderAddressLookup lookup, TestCitizenWalletDbContext db) CreateSut(
        [System.Runtime.CompilerServices.CallerMemberName] string testName = "")
    {
        var options = new DbContextOptionsBuilder<TestCitizenWalletDbContext>()
            .UseInMemoryDatabase($"holder-lookup-{testName}-{Guid.NewGuid():N}")
            .Options;
        var db = new TestCitizenWalletDbContext(options);
        var lookup = new EfCoreHolderAddressLookup(db, NullLogger<EfCoreHolderAddressLookup>.Instance);
        return (lookup, db);
    }

    [Fact]
    public async Task ResolvePlatformUserIdAsync_KnownCitizenAddress_ReturnsPlatformUserId()
    {
        var (lookup, db) = CreateSut();
        var pid = Guid.NewGuid();
        db.CitizenHolderIndex.Add(new CitizenHolderIndex
        {
            WalletAddress = "ws1qcitizen",
            PlatformUserId = pid,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        var result = await lookup.ResolvePlatformUserIdAsync("ws1qcitizen");

        result.Should().Be(pid);
    }

    [Fact]
    public async Task ResolvePlatformUserIdAsync_UnknownAddress_ReturnsNull()
    {
        var (lookup, _) = CreateSut();

        var result = await lookup.ResolvePlatformUserIdAsync("ws1qorg-not-citizen");

        result.Should().BeNull();
    }

    [Fact]
    public async Task RegisterAsync_NewAddress_PersistsRow()
    {
        var (lookup, db) = CreateSut();
        var pid = Guid.NewGuid();

        await lookup.RegisterAsync("ws1qcitizen", pid);

        var row = await db.CitizenHolderIndex.SingleAsync();
        row.WalletAddress.Should().Be("ws1qcitizen");
        row.PlatformUserId.Should().Be(pid);
        row.CreatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task RegisterAsync_SamePlatformUser_IsIdempotent()
    {
        var (lookup, db) = CreateSut();
        var pid = Guid.NewGuid();

        await lookup.RegisterAsync("ws1qcitizen", pid);
        await lookup.RegisterAsync("ws1qcitizen", pid);

        var rows = await db.CitizenHolderIndex.ToListAsync();
        rows.Should().HaveCount(1);
        rows[0].PlatformUserId.Should().Be(pid);
    }

    [Fact]
    public async Task RegisterAsync_DifferentPlatformUserSameAddress_LeavesExistingRow()
    {
        // Defensive: an address can never legitimately remap to a new PlatformUser
        // (slot-108 derivation is deterministic per citizen). If somehow called,
        // the existing row wins; the new request is logged but not applied.
        var (lookup, db) = CreateSut();
        var firstPid = Guid.NewGuid();
        var secondPid = Guid.NewGuid();

        await lookup.RegisterAsync("ws1qcitizen", firstPid);
        await lookup.RegisterAsync("ws1qcitizen", secondPid);

        var row = await db.CitizenHolderIndex.SingleAsync();
        row.PlatformUserId.Should().Be(firstPid);
    }
}
