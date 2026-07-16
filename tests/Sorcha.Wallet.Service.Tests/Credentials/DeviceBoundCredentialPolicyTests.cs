// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sorcha.Wallet.Service.Services.Implementation;
using Sorcha.Wallet.Service.Services.Interfaces;

namespace Sorcha.Wallet.Service.Tests.Credentials;

/// <summary>
/// Unit tests for <see cref="DeviceBoundCredentialPolicy"/> — the issuer-side max-3
/// device-bound-copy cap with LRU eviction (Feature 1195, Phase 2, Task 4).
/// </summary>
public class DeviceBoundCredentialPolicyTests
{
    private const string CredentialType = "AssuredIdentityCredential";
    private static readonly Guid UserId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private readonly Mock<IDeviceBoundCredentialLookup> _lookup = new(MockBehavior.Strict);
    private readonly Mock<IDeviceBoundCredentialRevoker> _revoker = new(MockBehavior.Strict);
    private readonly Mock<ICitizenDeviceInboxWriter> _inbox = new(MockBehavior.Strict);

    private DeviceBoundCredentialPolicy CreatePolicy() =>
        new(_lookup.Object, _revoker.Object, _inbox.Object,
            NullLogger<DeviceBoundCredentialPolicy>.Instance);

    private static DeviceBoundCredentialCopy Copy(
        string id, string thumbprint, DateTimeOffset issuedAt) =>
        new(CredentialId: id,
            DeviceKeyThumbprint: thumbprint,
            IssuedAt: issuedAt,
            DeviceId: Guid.NewGuid(),
            DeviceLabel: $"device-{id}");

    private void SetupLiveCopies(params DeviceBoundCredentialCopy[] copies) =>
        _lookup
            .Setup(l => l.GetLiveCopiesAsync(UserId, CredentialType, It.IsAny<CancellationToken>()))
            .ReturnsAsync(copies.ToList());

    [Fact]
    public async Task ReconcileAsync_TwoExistingDistinctPlusNew_ReturnsNewWithinCapNoEviction()
    {
        var now = DateTimeOffset.UtcNow;
        SetupLiveCopies(
            Copy("cred-1", "thumb-A", now.AddDays(-2)),
            Copy("cred-2", "thumb-B", now.AddDays(-1)));

        var result = await CreatePolicy().ReconcileAsync(UserId, CredentialType, "thumb-NEW", default);

        result.Kind.Should().Be(DeviceBindKind.NewWithinCap);
        result.EvictedCredentialId.Should().BeNull();
        _revoker.Verify(
            r => r.RevokeAsync(It.IsAny<Guid>(), It.IsAny<DeviceBoundCredentialCopy>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _inbox.Verify(
            i => i.WriteDeviceRevokedAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ReconcileAsync_ZeroExistingPlusNew_ReturnsNewWithinCap()
    {
        SetupLiveCopies();

        var result = await CreatePolicy().ReconcileAsync(UserId, CredentialType, "thumb-NEW", default);

        result.Kind.Should().Be(DeviceBindKind.NewWithinCap);
        result.EvictedCredentialId.Should().BeNull();
        _revoker.Verify(
            r => r.RevokeAsync(It.IsAny<Guid>(), It.IsAny<DeviceBoundCredentialCopy>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ReconcileAsync_ThreeExistingSameThumbprint_ReturnsReplaceExistingNoEviction()
    {
        var now = DateTimeOffset.UtcNow;
        SetupLiveCopies(
            Copy("cred-1", "thumb-A", now.AddDays(-3)),
            Copy("cred-2", "thumb-B", now.AddDays(-2)),
            Copy("cred-3", "thumb-C", now.AddDays(-1)));

        // Re-bind the SAME device (thumb-B) — idempotent replace, count unchanged.
        var result = await CreatePolicy().ReconcileAsync(UserId, CredentialType, "thumb-B", default);

        result.Kind.Should().Be(DeviceBindKind.ReplaceExisting);
        result.EvictedCredentialId.Should().BeNull();
        _revoker.Verify(
            r => r.RevokeAsync(It.IsAny<Guid>(), It.IsAny<DeviceBoundCredentialCopy>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _inbox.Verify(
            i => i.WriteDeviceRevokedAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ReconcileAsync_ThreeExistingDistinctPlusNew_EvictsOldestRevokesAndNotifies()
    {
        var now = DateTimeOffset.UtcNow;
        var oldest = Copy("cred-oldest", "thumb-A", now.AddDays(-10));
        SetupLiveCopies(
            Copy("cred-2", "thumb-B", now.AddDays(-1)),
            oldest, // deliberately not first in the list — oldest is by IssuedAt, not order
            Copy("cred-3", "thumb-C", now.AddDays(-5)));

        _revoker
            .Setup(r => r.RevokeAsync(UserId, It.Is<DeviceBoundCredentialCopy>(c => c.CredentialId == "cred-oldest"), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _inbox
            .Setup(i => i.WriteDeviceRevokedAsync(UserId, oldest.DeviceId, oldest.DeviceLabel, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var result = await CreatePolicy().ReconcileAsync(UserId, CredentialType, "thumb-NEW", default);

        result.Kind.Should().Be(DeviceBindKind.NewWithEviction);
        result.EvictedCredentialId.Should().Be("cred-oldest");
        _revoker.Verify(
            r => r.RevokeAsync(UserId, It.Is<DeviceBoundCredentialCopy>(c => c.CredentialId == "cred-oldest"), It.IsAny<CancellationToken>()),
            Times.Once);
        _inbox.Verify(
            i => i.WriteDeviceRevokedAsync(UserId, oldest.DeviceId, oldest.DeviceLabel, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ReconcileAsync_RevokeThrows_PropagatesAndLeavesNoPartialState()
    {
        var now = DateTimeOffset.UtcNow;
        var oldest = Copy("cred-oldest", "thumb-A", now.AddDays(-10));
        SetupLiveCopies(
            oldest,
            Copy("cred-2", "thumb-B", now.AddDays(-1)),
            Copy("cred-3", "thumb-C", now.AddDays(-5)));

        _revoker
            .Setup(r => r.RevokeAsync(UserId, It.IsAny<DeviceBoundCredentialCopy>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("status list unavailable"));

        var act = async () => await CreatePolicy().ReconcileAsync(UserId, CredentialType, "thumb-NEW", default);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("status list unavailable");

        // No partial state: the inbox is never written when revoke fails.
        _inbox.Verify(
            i => i.WriteDeviceRevokedAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
