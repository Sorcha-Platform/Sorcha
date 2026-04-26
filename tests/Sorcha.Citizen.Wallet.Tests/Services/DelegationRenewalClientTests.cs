// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sorcha.CitizenWallet.Abstractions.Models;
using Sorcha.Citizen.Wallet.Services;
using Sorcha.ServiceClients.CitizenWallet;
using Xunit;

namespace Sorcha.Citizen.Wallet.Tests.Services;

/// <summary>
/// Tests for the PWA-side delegation renewal trigger
/// (<see cref="DelegationRenewalClient"/>, Feature 114, T106 PWA part).
/// Covers all five outcomes: NotEnrolled, NotDue, Renewed, DeviceNotFound, Failed.
/// </summary>
public sealed class DelegationRenewalClientTests
{
    private readonly Mock<ICitizenWalletClient> _client = new();
    private readonly InMemoryDeviceMetaStore _meta = new();
    private readonly InMemoryDelegationStore _delegations = new();
    private readonly TestClock _clock = new(DateTimeOffset.Parse("2026-04-26T12:00:00Z"));
    private readonly DelegationRenewalClient _sut;

    public DelegationRenewalClientTests()
    {
        _sut = new DelegationRenewalClient(_meta, _delegations, _client.Object, _clock,
            NullLogger<DelegationRenewalClient>.Instance);
    }

    [Fact]
    public async Task RenewIfDueAsync_NotEnrolled_ReturnsNotEnrolled()
    {
        var outcome = await _sut.RenewIfDueAsync();

        outcome.Should().Be(DelegationRenewalOutcome.NotEnrolled);
        _client.Verify(c => c.RenewDelegationAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RenewIfDueAsync_DelegationFarFromExpiry_ReturnsNotDueWithoutCallingServer()
    {
        await _meta.SetAsync(NewMeta(DateTimeOffset.Parse("2027-04-26T12:00:00Z"))); // 365 days out

        var outcome = await _sut.RenewIfDueAsync();

        outcome.Should().Be(DelegationRenewalOutcome.NotDue);
        _client.Verify(c => c.RenewDelegationAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RenewIfDueAsync_DelegationWithinRenewalWindow_RenewsAndPersists()
    {
        var meta = NewMeta(DateTimeOffset.Parse("2026-05-15T12:00:00Z")); // 19 days out → due
        await _meta.SetAsync(meta);

        var newExpiry = DateTimeOffset.Parse("2027-04-26T12:00:00Z");
        _client.Setup(c => c.RenewDelegationAsync(meta.DeviceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DelegationRenewalResponse
            {
                DelegationCredential = "eyJ.fresh.delegation",
                ExpiresAt = newExpiry,
            });

        var outcome = await _sut.RenewIfDueAsync();

        outcome.Should().Be(DelegationRenewalOutcome.Renewed);
        (await _delegations.GetCurrentAsync()).Should().Be("eyJ.fresh.delegation");
        var refreshed = await _meta.GetAsync();
        refreshed!.DelegationExpiresAt.Should().Be(newExpiry);
        refreshed.DeviceId.Should().Be(meta.DeviceId, "device id must be preserved across the meta refresh");
    }

    [Fact]
    public async Task RenewIfDueAsync_Server404_ReturnsDeviceNotFound()
    {
        var meta = NewMeta(DateTimeOffset.Parse("2026-05-15T12:00:00Z"));
        await _meta.SetAsync(meta);
        _client.Setup(c => c.RenewDelegationAsync(meta.DeviceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((DelegationRenewalResponse?)null);

        var outcome = await _sut.RenewIfDueAsync();

        outcome.Should().Be(DelegationRenewalOutcome.DeviceNotFound);
        // Existing delegation must NOT be cleared — the user might still be able to
        // present until they re-enrol.
        (await _meta.GetAsync()).Should().NotBeNull();
    }

    [Fact]
    public async Task RenewIfDueAsync_HttpFailure_ReturnsFailed()
    {
        var meta = NewMeta(DateTimeOffset.Parse("2026-05-15T12:00:00Z"));
        await _meta.SetAsync(meta);
        _client.Setup(c => c.RenewDelegationAsync(meta.DeviceId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("network down"));

        var outcome = await _sut.RenewIfDueAsync();

        outcome.Should().Be(DelegationRenewalOutcome.Failed);
    }

    private static DeviceMetaRecord NewMeta(DateTimeOffset expiry) => new(
        DeviceId: Guid.Parse("0d000000-0000-0000-0000-000000000001"),
        Label: "My iPhone",
        EnrolledAt: DateTimeOffset.Parse("2025-04-26T12:00:00Z"),
        DelegationExpiresAt: expiry);

    private sealed class TestClock : TimeProvider
    {
        private readonly DateTimeOffset _now;
        public TestClock(DateTimeOffset now) => _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
    }
}
