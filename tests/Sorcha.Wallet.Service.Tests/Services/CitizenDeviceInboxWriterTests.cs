// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net.Http;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sorcha.ServiceClients.Inbox;
using Sorcha.Wallet.Service.Services.Implementation;

namespace Sorcha.Wallet.Service.Tests.Services;

/// <summary>
/// Unit tests for <see cref="CitizenDeviceInboxWriter"/> — Phase 2c of the
/// Snackbar retirement plan. Covers the citizen-device revoke path; the
/// rest of Phase 2c (wiring TenantSecurityInboxWriter's existing methods to
/// their 2FA/password/backup-code endpoints) is rolled into task #14 because
/// it lives entirely in Tenant Service and overlaps the admin-paths audit.
/// </summary>
public class CitizenDeviceInboxWriterTests
{
    private readonly Mock<IPlatformInboxClient> _inbox = new();
    private readonly CitizenDeviceInboxWriter _sut;
    private readonly Guid _platformUserId = Guid.NewGuid();
    private readonly Guid _deviceId = Guid.NewGuid();

    public CitizenDeviceInboxWriterTests()
    {
        _sut = new CitizenDeviceInboxWriter(_inbox.Object, NullLogger<CitizenDeviceInboxWriter>.Instance);
    }

    [Fact]
    public async Task WriteDeviceRevokedAsync_HappyPath_PostsExpectedPayload()
    {
        InboxWritePayload? captured = null;
        _inbox.Setup(i => i.WriteAsync(It.IsAny<InboxWritePayload>(), It.IsAny<CancellationToken>()))
            .Callback<InboxWritePayload, CancellationToken>((p, _) => captured = p)
            .ReturnsAsync(new InboxWriteOutcome(Guid.NewGuid(), Idempotent: false));

        await _sut.WriteDeviceRevokedAsync(_platformUserId, _deviceId, "Alice's iPhone");

        captured.Should().NotBeNull();
        captured!.PlatformUserId.Should().Be(_platformUserId);
        captured.Category.Should().Be("Security");
        captured.Severity.Should().Be("Warning");
        captured.Title.Should().Be("Device revoked: Alice's iPhone");
        captured.DetailHref.Should().Be("/api/v1/me/devices");
        captured.IconKey.Should().Be("security.device-revoked");
        captured.Summary.Should().Contain("If this wasn't you");
    }

    [Fact]
    public async Task WriteDeviceRevokedAsync_NoLabel_FallsBackToGenericTitle()
    {
        InboxWritePayload? captured = null;
        _inbox.Setup(i => i.WriteAsync(It.IsAny<InboxWritePayload>(), It.IsAny<CancellationToken>()))
            .Callback<InboxWritePayload, CancellationToken>((p, _) => captured = p)
            .ReturnsAsync(new InboxWriteOutcome(Guid.NewGuid(), Idempotent: false));

        await _sut.WriteDeviceRevokedAsync(_platformUserId, _deviceId, deviceLabel: null);

        captured!.Title.Should().Be("Device revoked: your device");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task WriteDeviceRevokedAsync_BlankLabel_FallsBackToGenericTitle(string? label)
    {
        InboxWritePayload? captured = null;
        _inbox.Setup(i => i.WriteAsync(It.IsAny<InboxWritePayload>(), It.IsAny<CancellationToken>()))
            .Callback<InboxWritePayload, CancellationToken>((p, _) => captured = p)
            .ReturnsAsync(new InboxWriteOutcome(Guid.NewGuid(), Idempotent: false));

        await _sut.WriteDeviceRevokedAsync(_platformUserId, _deviceId, label);

        captured!.Title.Should().Be("Device revoked: your device");
    }

    [Fact]
    public async Task WriteDeviceRevokedAsync_EmptyPlatformUserId_ShortCircuits()
    {
        await _sut.WriteDeviceRevokedAsync(Guid.Empty, _deviceId, "Phone");

        _inbox.Verify(
            i => i.WriteAsync(It.IsAny<InboxWritePayload>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task WriteDeviceRevokedAsync_EmptyDeviceId_ShortCircuits()
    {
        await _sut.WriteDeviceRevokedAsync(_platformUserId, Guid.Empty, "Phone");

        _inbox.Verify(
            i => i.WriteAsync(It.IsAny<InboxWritePayload>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task WriteDeviceRevokedAsync_DeterministicSourceId_CollapsesDuplicates()
    {
        var sourceIds = new List<Guid>();
        _inbox.Setup(i => i.WriteAsync(It.IsAny<InboxWritePayload>(), It.IsAny<CancellationToken>()))
            .Callback<InboxWritePayload, CancellationToken>((p, _) => sourceIds.Add(p.SourceEventId))
            .ReturnsAsync(new InboxWriteOutcome(Guid.NewGuid(), Idempotent: true));

        // Simulate the documented concurrent web + PWA race — same (user, device)
        // pair, two writes. They MUST collapse to the same SourceEventId.
        await _sut.WriteDeviceRevokedAsync(_platformUserId, _deviceId, "Phone");
        await _sut.WriteDeviceRevokedAsync(_platformUserId, _deviceId, "Phone");

        sourceIds.Should().HaveCount(2);
        sourceIds[0].Should().Be(sourceIds[1]);
    }

    [Fact]
    public async Task WriteDeviceRevokedAsync_DifferentDevices_HaveDifferentSourceIds()
    {
        var sourceIds = new List<Guid>();
        _inbox.Setup(i => i.WriteAsync(It.IsAny<InboxWritePayload>(), It.IsAny<CancellationToken>()))
            .Callback<InboxWritePayload, CancellationToken>((p, _) => sourceIds.Add(p.SourceEventId))
            .ReturnsAsync(new InboxWriteOutcome(Guid.NewGuid(), Idempotent: false));

        await _sut.WriteDeviceRevokedAsync(_platformUserId, Guid.NewGuid(), "Phone A");
        await _sut.WriteDeviceRevokedAsync(_platformUserId, Guid.NewGuid(), "Phone B");

        sourceIds.Distinct().Should().HaveCount(2);
    }

    [Fact]
    public async Task WriteDeviceRevokedAsync_InboxThrows_DoesNotPropagate()
    {
        _inbox.Setup(i => i.WriteAsync(It.IsAny<InboxWritePayload>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Tenant unavailable"));

        var act = () => _sut.WriteDeviceRevokedAsync(_platformUserId, _deviceId, "Phone");

        await act.Should().NotThrowAsync(
            "device revocation must never fail because of an inbox-write outage");
    }

    [Fact]
    public async Task WriteDeviceRevokedAsync_DetailHrefStartsWithApi_PerInternalInboxValidation()
    {
        InboxWritePayload? captured = null;
        _inbox.Setup(i => i.WriteAsync(It.IsAny<InboxWritePayload>(), It.IsAny<CancellationToken>()))
            .Callback<InboxWritePayload, CancellationToken>((p, _) => captured = p)
            .ReturnsAsync(new InboxWriteOutcome(Guid.NewGuid(), Idempotent: false));

        await _sut.WriteDeviceRevokedAsync(_platformUserId, _deviceId, "Phone");

        captured!.DetailHref.Should().StartWith("/api/");
    }
}
