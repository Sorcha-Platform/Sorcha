// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Sorcha.Tenant.Service.Data;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Services;
using Sorcha.Tenant.Service.Tests.Helpers;

namespace Sorcha.Tenant.Service.Tests.Services;

/// <summary>
/// Tests for <see cref="PlatformUserDeviceService"/> (Feature 114). Validates
/// idempotent enrolment behaviour expected by the Wallet Service's enrolment
/// retry path.
/// </summary>
public sealed class PlatformUserDeviceServiceTests : IDisposable
{
    private readonly TenantDbContext _db;
    private readonly PlatformUserDeviceService _sut;
    private readonly Guid _platformUserId = Guid.NewGuid();

    private const string Thumbprint = "abcdefghijklmnopqrstuvwxyz0123456789ABCDEFG"; // 43 chars
    private const string AltThumbprint = "ZYXWVUTSRQPONMLKJIHGFEDCBA9876543210zyxwvut"; // 43 chars

    public PlatformUserDeviceServiceTests()
    {
        _db = InMemoryDbContextFactory.Create();
        _sut = new PlatformUserDeviceService(_db, NullLogger<PlatformUserDeviceService>.Instance);
    }

    public void Dispose() => _db.Dispose();

    private Task<PlatformUserDevice> RegisterAsync(
        string thumbprint = Thumbprint,
        string label = "iPhone",
        string jti = "jti-1",
        int statusListIndex = 7,
        DateTimeOffset? expires = null)
        => _sut.RegisterAsync(
            _platformUserId,
            label,
            thumbprint,
            "{\"kty\":\"EC\",\"crv\":\"P-256\",\"x\":\"...\",\"y\":\"...\"}",
            "iOS 19 / Safari 19",
            "Mozilla/5.0 ...",
            expires ?? DateTimeOffset.UtcNow.AddDays(365),
            jti,
            statusListIndex);

    [Fact]
    public async Task RegisterAsync_FirstCall_PersistsDeviceAsActive()
    {
        var device = await RegisterAsync();

        device.Id.Should().NotBeEmpty();
        device.Status.Should().Be(PlatformUserDeviceStatus.Active);
        device.PlatformUserId.Should().Be(_platformUserId);
        device.DevicePublicJwkThumbprint.Should().Be(Thumbprint);
        device.StatusListIndex.Should().Be(7);

        (await _db.PlatformUserDevices.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task RegisterAsync_DuplicateThumbprint_ReturnsExistingRowWithRefreshedDelegation()
    {
        var first = await RegisterAsync(jti: "jti-original", statusListIndex: 7);
        var firstId = first.Id;
        var firstEnrolledAt = first.EnrolledAt;

        var newExpiry = DateTimeOffset.UtcNow.AddDays(400);
        var second = await RegisterAsync(
            label: "iPhone (renamed)",
            jti: "jti-renewed",
            statusListIndex: 7,
            expires: newExpiry);

        second.Id.Should().Be(firstId, because: "idempotent — same row, deviceId stable for the wallet");
        second.EnrolledAt.Should().Be(firstEnrolledAt, because: "EnrolledAt is preserved across renewals");
        second.Label.Should().Be("iPhone (renamed)");
        second.DelegationCredentialJti.Should().Be("jti-renewed");
        second.DelegationExpiresAt.Should().BeCloseTo(newExpiry, TimeSpan.FromMilliseconds(1));

        (await _db.PlatformUserDevices.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task RegisterAsync_DifferentThumbprintSameUser_CreatesSecondRow()
    {
        await RegisterAsync(thumbprint: Thumbprint, jti: "j-1", statusListIndex: 7);
        await RegisterAsync(thumbprint: AltThumbprint, jti: "j-2", statusListIndex: 8);

        (await _db.PlatformUserDevices.CountAsync()).Should().Be(2);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task RegisterAsync_RejectsEmptyLabel(string label)
    {
        var act = () => RegisterAsync(label: label);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task RegisterAsync_RejectsLabelOver120Chars()
    {
        var act = () => RegisterAsync(label: new string('x', 121));
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task RegisterAsync_RejectsThumbprintOfWrongLength()
    {
        var act = () => RegisterAsync(thumbprint: "tooshort");
        await act.Should().ThrowAsync<ArgumentException>();
    }
}
