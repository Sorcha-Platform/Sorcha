// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sorcha.Tenant.Service.Data;
using Sorcha.Tenant.Service.Hubs;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Services;
using Xunit;

namespace Sorcha.Tenant.Service.Tests.Hubs;

/// <summary>
/// Feature 126 — verifies <see cref="PlatformUserDeviceService.RegisterAsync"/>
/// emits <c>DeviceEnrolled(platformUserId, deviceId)</c> on the per-user
/// TenantHub group with the thin-signal payload contract (opaque IDs only).
/// </summary>
public sealed class TenantHubDeviceEnrolledTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly TenantDbContext _db;

    private readonly Mock<IHubContext<TenantHub>> _hub = new();
    private readonly Mock<IHubClients> _clients = new();
    private readonly Mock<IClientProxy> _group = new();
    private readonly List<(string Method, object?[] Args)> _emits = new();

    public TenantHubDeviceEnrolledTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<TenantDbContext>().UseSqlite(_connection).Options;
        _db = new TenantDbContext(options);
        _db.Database.EnsureCreated();

        _hub.Setup(h => h.Clients).Returns(_clients.Object);
        _clients.Setup(c => c.Group(It.IsAny<string>())).Returns(_group.Object);
        _group
            .Setup(c => c.SendCoreAsync(It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
            .Callback<string, object?[], CancellationToken>((m, a, _) => _emits.Add((m, a)))
            .Returns(Task.CompletedTask);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private async Task<Guid> SeedPlatformUserAsync()
    {
        var pu = new PlatformUser
        {
            Id = Guid.NewGuid(),
            Email = $"u-{Guid.NewGuid():N}@test.com",
            DisplayName = "Test User",
        };
        _db.PlatformUsers.Add(pu);
        await _db.SaveChangesAsync();
        return pu.Id;
    }

    [Fact]
    public async Task RegisterAsync_NewDevice_Emits_DeviceEnrolled_On_User_Group_With_Thin_Payload()
    {
        var platformUserId = await SeedPlatformUserAsync();
        var service = new PlatformUserDeviceService(_db, NullLogger<PlatformUserDeviceService>.Instance, _hub.Object);

        var device = await service.RegisterAsync(
            platformUserId,
            label: "Sarah's phone",
            devicePublicJwkThumbprint: new string('a', 43),
            devicePublicJwkJson: """{"kty":"OKP"}""",
            platform: "iOS",
            userAgent: "WalletPWA/1",
            delegationExpiresAt: DateTimeOffset.UtcNow.AddDays(30),
            delegationCredentialJti: "jti-123",
            statusListId: 1,
            statusListIndex: 1);

        _clients.Verify(c => c.Group(TenantHubGroups.User(platformUserId)), Times.Once,
            "publish must target the bound platform user's group, never SystemAll or another user");

        _emits.Should().ContainSingle(e => e.Method == "DeviceEnrolled");
        var fire = _emits.Single(e => e.Method == "DeviceEnrolled");
        fire.Args.Should().HaveCount(2, "thin-signal: platformUserId + deviceId, no domain payload");
        fire.Args[0].Should().BeOfType<Guid>().Which.Should().Be(platformUserId);
        fire.Args[1].Should().BeOfType<Guid>().Which.Should().Be(device.Id);
    }

    [Fact]
    public async Task RegisterAsync_Idempotent_Reregistration_Still_Emits()
    {
        // Idempotent re-register (same thumbprint) keeps the original deviceId.
        // The current behaviour re-publishes on every successful Register call;
        // this is intentional and the council page tolerates the repeat
        // (DeviceEnrolled handler is idempotent on the consumer side).
        var platformUserId = await SeedPlatformUserAsync();
        var service = new PlatformUserDeviceService(_db, NullLogger<PlatformUserDeviceService>.Instance, _hub.Object);
        var thumbprint = new string('b', 43);

        var first = await service.RegisterAsync(
            platformUserId, "phone", thumbprint, """{"kty":"OKP"}""", "iOS", "ua",
            DateTimeOffset.UtcNow.AddDays(30), "jti-1", 1, 1);
        _emits.Clear();

        var second = await service.RegisterAsync(
            platformUserId, "phone", thumbprint, """{"kty":"OKP"}""", "iOS", "ua",
            DateTimeOffset.UtcNow.AddDays(30), "jti-2", 1, 2);

        second.Id.Should().Be(first.Id, "idempotent upsert preserves deviceId");
        _emits.Should().ContainSingle(e => e.Method == "DeviceEnrolled");
        _emits.Single().Args[1].Should().Be(first.Id);
    }

    [Fact]
    public async Task RegisterAsync_Hub_Publish_Failure_Does_Not_Fail_Registration()
    {
        var platformUserId = await SeedPlatformUserAsync();
        _group.Setup(c => c.SendCoreAsync(It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("hub fell over"));

        var service = new PlatformUserDeviceService(_db, NullLogger<PlatformUserDeviceService>.Instance, _hub.Object);

        var device = await service.RegisterAsync(
            platformUserId, "phone", new string('c', 43), """{"kty":"OKP"}""", "iOS", "ua",
            DateTimeOffset.UtcNow.AddDays(30), "jti-x", 1, 1);

        device.Should().NotBeNull("hub publish failure must not cascade to the device write");
        var persisted = await _db.PlatformUserDevices.SingleOrDefaultAsync(d => d.Id == device.Id);
        persisted.Should().NotBeNull();
    }

    [Fact]
    public async Task RegisterAsync_With_No_HubContext_Skips_Publish_Cleanly()
    {
        // Unit-test path — hub context not provided. Registration must still
        // succeed without throwing.
        var service = new PlatformUserDeviceService(_db, NullLogger<PlatformUserDeviceService>.Instance);
        var pu = await SeedPlatformUserAsync();

        var device = await service.RegisterAsync(
            pu, "phone", new string('d', 43), """{"kty":"OKP"}""", "iOS", "ua",
            DateTimeOffset.UtcNow.AddDays(30), "jti-y", 1, 1);

        device.Should().NotBeNull();
        _emits.Should().BeEmpty();
    }
}
