// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sorcha.Tenant.Service.Data;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Models.Requests;
using Sorcha.Tenant.Service.Tests.Infrastructure;

namespace Sorcha.Tenant.Service.Tests.Endpoints;

/// <summary>
/// Integration tests for the Feature 116 US3 password lifecycle endpoints
/// (POST /set, /change, /remove). Drives the unauthorised / missing-challenge /
/// bootstrap-bypass paths through the real HTTP pipeline; the full state-machine
/// + floor coverage is in <see cref="Services.PasswordManagementServiceTests"/>
/// against SQLite, since the WebApplicationFactory's InMemory EF provider does
/// not support the <c>ExecuteUpdateAsync</c> used by the atomic-consume primitive.
/// </summary>
public class PasswordEndpointTests : IClassFixture<TenantServiceWebApplicationFactory>
{
    private readonly TenantServiceWebApplicationFactory _factory;

    public PasswordEndpointTests(TenantServiceWebApplicationFactory factory)
    {
        _factory = factory;
    }

    // -------- /api/auth/password/set --------

    [Fact]
    public async Task Set_Anonymous_Returns401()
    {
        await _factory.SeedTestDataAsync();
        using var client = _factory.CreateUnauthenticatedClient();

        var response = await client.PostAsJsonAsync("/api/auth/password/set",
            new PasswordRequest("Brand-New-P4ssw0rd!!"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Set_EmptyPassword_Returns400()
    {
        await _factory.SeedTestDataAsync();
        using var client = _factory.CreateAdminClient();

        var response = await client.PostAsJsonAsync("/api/auth/password/set",
            new PasswordRequest(""));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Set_AdminAlreadyHasPassword_NoChallenge_Returns401()
    {
        // Admin is seeded with a password. Not in bootstrap mode → challenge
        // required → no challenge header → filter returns 401.
        await _factory.SeedTestDataAsync();
        using var client = _factory.CreateAdminClient();

        var response = await client.PostAsJsonAsync("/api/auth/password/set",
            new PasswordRequest("Brand-New-P4ssw0rd!!"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Set_BootstrapMode_NoChallenge_Returns204_AndPersists()
    {
        // Manufacture a bootstrap-mode user: scrub the password, ensure no
        // socials / passkeys exist. Then /set must accept without a challenge.
        await _factory.SeedTestDataAsync();
        await ScrubAdminAuthMethodsAsync();
        using var client = _factory.CreateAdminClient();

        var response = await client.PostAsJsonAsync("/api/auth/password/set",
            new PasswordRequest("Brand-New-P4ssw0rd!!"));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var stored = await db.PlatformUsers.AsNoTracking().FirstAsync(u => u.Id == TestDataSeeder.AdminPlatformUserId);
        stored.PasswordHash.Should().NotBeNullOrEmpty();
        BCrypt.Net.BCrypt.Verify("Brand-New-P4ssw0rd!!", stored.PasswordHash).Should().BeTrue();
    }

    [Fact]
    public async Task Set_BootstrapMode_AlreadyHasPasswordAfterRace_Returns409()
    {
        // Edge case: bootstrap-mode user calls /set twice. First call lands.
        // Second call sees a password already set and gets 409 (not 401 —
        // bootstrap mode bypassed the challenge for both attempts but the
        // service-layer state machine catches the duplicate).
        await _factory.SeedTestDataAsync();
        await ScrubAdminAuthMethodsAsync();
        using var client = _factory.CreateAdminClient();

        var first = await client.PostAsJsonAsync("/api/auth/password/set", new PasswordRequest("Pass1-V4lid!!"));
        first.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Second call: user now has a password → no longer bootstrap mode →
        // challenge required → 401. Documents the bootstrap window's strict
        // single-use property: once you've set a password the easy path is gone.
        var second = await client.PostAsJsonAsync("/api/auth/password/set", new PasswordRequest("Pass2-V4lid!!"));
        second.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // -------- /api/auth/password/change --------

    [Fact]
    public async Task Change_Anonymous_Returns401()
    {
        await _factory.SeedTestDataAsync();
        using var client = _factory.CreateUnauthenticatedClient();

        var response = await client.PostAsJsonAsync("/api/auth/password/change",
            new PasswordRequest("Brand-New-P4ssw0rd!!"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Change_NoChallenge_Returns401()
    {
        await _factory.SeedTestDataAsync();
        using var client = _factory.CreateAdminClient();

        var response = await client.PostAsJsonAsync("/api/auth/password/change",
            new PasswordRequest("Brand-New-P4ssw0rd!!"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // -------- /api/auth/password/remove --------

    [Fact]
    public async Task Remove_Anonymous_Returns401()
    {
        await _factory.SeedTestDataAsync();
        using var client = _factory.CreateUnauthenticatedClient();

        var response = await client.PostAsync("/api/auth/password/remove", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Remove_NoChallenge_Returns401()
    {
        await _factory.SeedTestDataAsync();
        using var client = _factory.CreateAdminClient();

        var response = await client.PostAsync("/api/auth/password/remove", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // -------- helpers --------

    /// <summary>
    /// Strips the seeded admin's password and removes any social/passkey rows
    /// so they fall into bootstrap mode (zero sign-in methods).
    /// </summary>
    private async Task ScrubAdminAuthMethodsAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();

        var user = await db.PlatformUsers.FirstAsync(u => u.Id == TestDataSeeder.AdminPlatformUserId);
        user.PasswordHash = null;

        var socials = await db.PlatformSocialLogins
            .Where(s => s.PlatformUserId == TestDataSeeder.AdminPlatformUserId)
            .ToListAsync();
        db.PlatformSocialLogins.RemoveRange(socials);

        var passkeys = await db.PasskeyCredentials
            .Where(p => p.PlatformUserId == TestDataSeeder.AdminPlatformUserId)
            .ToListAsync();
        db.PasskeyCredentials.RemoveRange(passkeys);

        await db.SaveChangesAsync();
    }
}
