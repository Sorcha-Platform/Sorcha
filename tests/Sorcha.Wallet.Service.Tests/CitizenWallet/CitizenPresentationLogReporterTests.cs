// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sorcha.CitizenWallet.Abstractions.Models;
using Sorcha.Wallet.Service.Services.Implementation;
using Sorcha.Wallet.Service.Services.Interfaces;
using StackExchange.Redis;
using Xunit;

namespace Sorcha.Wallet.Service.Tests.CitizenWallet;

/// <summary>
/// Unit tests for <see cref="CitizenPresentationLogReporter"/> (Feature 114 US5 PR2):
/// per-entry SET-NX dedupe plus forwarding of the newly-claimed entries.
/// </summary>
public sealed class CitizenPresentationLogReporterTests
{
    private static readonly Guid PlatformUserId = Guid.NewGuid();

    private readonly Mock<IConnectionMultiplexer> _redis = new();
    private readonly Mock<IDatabase> _db = new();
    private readonly Mock<IPresentationLogForwarder> _forwarder = new();
    private readonly CitizenPresentationLogReporter _sut;

    public CitizenPresentationLogReporterTests()
    {
        _redis.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object?>()))
            .Returns(_db.Object);

        _sut = new CitizenPresentationLogReporter(
            _redis.Object, _forwarder.Object, NullLogger<CitizenPresentationLogReporter>.Instance);
    }

    private static PresentationLogEntry Entry(Guid? id = null) => new()
    {
        Id = id ?? Guid.NewGuid(),
        CredentialId = Guid.NewGuid(),
        VerifierLabel = "Strathcarron Council",
        DisclosedClaims = ["givenName", "dateOfBirth"],
        PresentedAt = DateTimeOffset.UtcNow,
        Outcome = PresentationLogOutcome.Acknowledged
    };

    private void SetupClaim(bool claimed) =>
        _db.Setup(d => d.StringSetAsync(
                It.IsAny<RedisKey>(), It.IsAny<RedisValue>(),
                It.IsAny<TimeSpan?>(), It.IsAny<When>()))
            .ReturnsAsync(claimed);

    [Fact]
    public async Task ReportAsync_NewEntry_ClaimsAndForwards()
    {
        SetupClaim(true);
        var entry = Entry();

        var accepted = await _sut.ReportAsync(PlatformUserId, [entry]);

        accepted.Should().Be(1);
        _forwarder.Verify(f => f.ForwardAsync(PlatformUserId, entry, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ReportAsync_DuplicateEntry_DoesNotForward()
    {
        SetupClaim(false);
        var entry = Entry();

        var accepted = await _sut.ReportAsync(PlatformUserId, [entry]);

        accepted.Should().Be(0);
        _forwarder.Verify(
            f => f.ForwardAsync(It.IsAny<Guid>(), It.IsAny<PresentationLogEntry>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ReportAsync_MixedBatch_ForwardsOnlyNewEntries()
    {
        var fresh = Entry();
        var dupe = Entry();

        _db.Setup(d => d.StringSetAsync(
                (RedisKey)$"sorcha:wallet:presentation-log-dedupe:{fresh.Id}", It.IsAny<RedisValue>(),
                It.IsAny<TimeSpan?>(), It.IsAny<When>()))
            .ReturnsAsync(true);
        _db.Setup(d => d.StringSetAsync(
                (RedisKey)$"sorcha:wallet:presentation-log-dedupe:{dupe.Id}", It.IsAny<RedisValue>(),
                It.IsAny<TimeSpan?>(), It.IsAny<When>()))
            .ReturnsAsync(false);

        var accepted = await _sut.ReportAsync(PlatformUserId, [fresh, dupe]);

        accepted.Should().Be(1);
        _forwarder.Verify(f => f.ForwardAsync(PlatformUserId, fresh, It.IsAny<CancellationToken>()), Times.Once);
        _forwarder.Verify(f => f.ForwardAsync(PlatformUserId, dupe, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ReportAsync_ClaimsWithEntryIdKeyAndTwentyFourHourNotExistsTtl()
    {
        SetupClaim(true);
        var entry = Entry();

        await _sut.ReportAsync(PlatformUserId, [entry]);

        _db.Verify(d => d.StringSetAsync(
                (RedisKey)$"sorcha:wallet:presentation-log-dedupe:{entry.Id}",
                It.IsAny<RedisValue>(),
                TimeSpan.FromHours(24),
                When.NotExists),
            Times.Once);
    }

    [Fact]
    public async Task ReportAsync_RedisThrows_DegradesOpenAndForwards()
    {
        _db.Setup(d => d.StringSetAsync(
                It.IsAny<RedisKey>(), It.IsAny<RedisValue>(),
                It.IsAny<TimeSpan?>(), It.IsAny<When>()))
            .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, CommandFlags.None, "down"));
        var entry = Entry();

        var accepted = await _sut.ReportAsync(PlatformUserId, [entry]);

        accepted.Should().Be(1);
        _forwarder.Verify(f => f.ForwardAsync(PlatformUserId, entry, It.IsAny<CancellationToken>()), Times.Once);
    }
}
