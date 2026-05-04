// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Sorcha.CitizenWallet.Abstractions.Models;
using Sorcha.Tenant.Service.Data;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Tests.Infrastructure;
using Xunit;

namespace Sorcha.Tenant.Service.Tests.Endpoints;

/// <summary>
/// Integration coverage for the Feature 114 recovery-flow device endpoints
/// at <c>/api/v1/me/devices</c>. PR1 scope (T113-T115): list and revoke flip
/// the Tenant-side <see cref="PlatformUserDeviceStatus"/> only — the wallet
/// status-list propagation lands in PR2 (T116-T117).
/// </summary>
public class PlatformUserDeviceEndpointsTests
    : IClassFixture<TenantServiceWebApplicationFactory>, IAsyncLifetime
{
    private readonly TenantServiceWebApplicationFactory _factory;
    private HttpClient _memberClient = null!;

    public PlatformUserDeviceEndpointsTests(TenantServiceWebApplicationFactory factory)
    {
        _factory = factory;
    }

    public async ValueTask InitializeAsync()
    {
        await _factory.SeedTestDataAsync();
        _memberClient = _factory.CreateMemberClient();
    }

    public ValueTask DisposeAsync()
    {
        _memberClient?.Dispose();
        return ValueTask.CompletedTask;
    }

    // -----------------------------------------------------------------------
    // GET /api/v1/me/devices
    // -----------------------------------------------------------------------

    [Fact]
    public async Task List_WithoutAuth_ReturnsUnauthorized()
    {
        var unauth = _factory.CreateUnauthenticatedClient();
        var response = await unauth.GetAsync("/api/v1/me/devices");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task List_NoDevices_ReturnsEmpty()
    {
        var response = await _memberClient.GetAsync("/api/v1/me/devices");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<DeviceListResponse>();
        body.Should().NotBeNull();
        body!.Devices.Should().BeEmpty();
    }

    [Fact]
    public async Task List_ReturnsOnlyCallersDevices_OrderedByEnrolledAtDesc()
    {
        var memberDeviceOlder = await SeedDeviceAsync(
            TestDataSeeder.MemberPlatformUserId,
            label: "Stuart's old phone",
            enrolledAt: DateTimeOffset.UtcNow.AddDays(-30));

        var memberDeviceNewer = await SeedDeviceAsync(
            TestDataSeeder.MemberPlatformUserId,
            label: "Stuart's new phone",
            enrolledAt: DateTimeOffset.UtcNow.AddDays(-1));

        // Other user's device — must NOT leak through.
        await SeedDeviceAsync(
            TestDataSeeder.AdminPlatformUserId,
            label: "Admin phone",
            enrolledAt: DateTimeOffset.UtcNow.AddDays(-5));

        var response = await _memberClient.GetAsync("/api/v1/me/devices");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<DeviceListResponse>();
        body!.Devices.Should().HaveCount(2);
        body.Devices.Select(d => d.DeviceId).Should().ContainInOrder(
            memberDeviceNewer.Id,
            memberDeviceOlder.Id);
        body.Devices.Should().AllSatisfy(d => d.Label.Should().NotBe("Admin phone"));
    }

    [Fact]
    public async Task List_IncludesRevokedDevices()
    {
        var device = await SeedDeviceAsync(TestDataSeeder.MemberPlatformUserId, "Lost phone");
        await MarkRevokedAsync(device.Id);

        var response = await _memberClient.GetAsync("/api/v1/me/devices");
        var body = await response.Content.ReadFromJsonAsync<DeviceListResponse>();

        body!.Devices.Should().ContainSingle(d => d.DeviceId == device.Id)
            .Which.Status.Should().Be(DeviceStatus.Revoked);
    }

    // -----------------------------------------------------------------------
    // DELETE /api/v1/me/devices/{deviceId}
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Revoke_WithoutAuth_ReturnsUnauthorized()
    {
        var unauth = _factory.CreateUnauthenticatedClient();
        var response = await unauth.DeleteAsync($"/api/v1/me/devices/{Guid.NewGuid()}");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Revoke_UnknownDevice_ReturnsNotFound()
    {
        var response = await _memberClient.DeleteAsync($"/api/v1/me/devices/{Guid.NewGuid()}");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Revoke_OtherUsersDevice_ReturnsNotFound_NoLeak()
    {
        var foreign = await SeedDeviceAsync(TestDataSeeder.AdminPlatformUserId, "Admin phone");

        var response = await _memberClient.DeleteAsync($"/api/v1/me/devices/{foreign.Id}");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Foreign device must remain Active — caller should not have been able to mutate it.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var stillActive = await db.PlatformUserDevices.FindAsync(foreign.Id);
        stillActive!.Status.Should().Be(PlatformUserDeviceStatus.Active);
    }

    [Fact]
    public async Task Revoke_OwnedActiveDevice_FlipsStatusAndReturnsNoContent()
    {
        var device = await SeedDeviceAsync(TestDataSeeder.MemberPlatformUserId, "Old phone");

        var response = await _memberClient.DeleteAsync($"/api/v1/me/devices/{device.Id}");
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var revoked = await db.PlatformUserDevices.FindAsync(device.Id);
        revoked!.Status.Should().Be(PlatformUserDeviceStatus.Revoked);
        revoked.RevokedAt.Should().NotBeNull();
        revoked.RevokedByPlatformUserId.Should().Be(TestDataSeeder.MemberPlatformUserId);
    }

    [Fact]
    public async Task Revoke_AlreadyRevokedDevice_IsIdempotent()
    {
        var device = await SeedDeviceAsync(TestDataSeeder.MemberPlatformUserId, "Old phone");

        var first = await _memberClient.DeleteAsync($"/api/v1/me/devices/{device.Id}");
        first.StatusCode.Should().Be(HttpStatusCode.NoContent);

        DateTimeOffset firstRevokedAt;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
            firstRevokedAt = (await db.PlatformUserDevices.FindAsync(device.Id))!.RevokedAt!.Value;
        }

        // Second delete must not 404 and must preserve the original RevokedAt.
        var second = await _memberClient.DeleteAsync($"/api/v1/me/devices/{device.Id}");
        second.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope2 = _factory.Services.CreateScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<TenantDbContext>();
        var stillRevoked = await db2.PlatformUserDevices.FindAsync(device.Id);
        stillRevoked!.Status.Should().Be(PlatformUserDeviceStatus.Revoked);
        stillRevoked.RevokedAt.Should().Be(firstRevokedAt);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private async Task<Sorcha.Tenant.Service.Models.PlatformUserDevice> SeedDeviceAsync(
        Guid platformUserId,
        string label,
        DateTimeOffset? enrolledAt = null)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();

        var enrolled = enrolledAt ?? DateTimeOffset.UtcNow;
        var device = new Sorcha.Tenant.Service.Models.PlatformUserDevice
        {
            Id = Guid.NewGuid(),
            PlatformUserId = platformUserId,
            Label = label,
            DevicePublicJwkThumbprint = Guid.NewGuid().ToString("N").PadRight(43, 'a')[..43],
            DevicePublicJwkJson = "{\"kty\":\"EC\",\"crv\":\"P-256\",\"x\":\"x\",\"y\":\"y\"}",
            Platform = "iOS 19 / Safari 19",
            UserAgent = "Mozilla/5.0",
            Status = PlatformUserDeviceStatus.Active,
            EnrolledAt = enrolled,
            DelegationExpiresAt = enrolled.AddDays(365),
            DelegationCredentialJti = Guid.NewGuid().ToString(),
            StatusListIndex = Random.Shared.Next(0, 100_000)
        };

        db.PlatformUserDevices.Add(device);
        await db.SaveChangesAsync();
        return device;
    }

    private async Task MarkRevokedAsync(Guid deviceId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var device = await db.PlatformUserDevices.FindAsync(deviceId);
        device!.Status = PlatformUserDeviceStatus.Revoked;
        device.RevokedAt = DateTimeOffset.UtcNow;
        device.RevokedByPlatformUserId = device.PlatformUserId;
        await db.SaveChangesAsync();
    }
}
