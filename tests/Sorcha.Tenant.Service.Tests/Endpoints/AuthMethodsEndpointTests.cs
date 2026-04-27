// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Sorcha.Tenant.Service.Data;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Models.Requests;
using Sorcha.Tenant.Service.Tests.Infrastructure;

namespace Sorcha.Tenant.Service.Tests.Endpoints;

/// <summary>
/// Integration tests for the GET /api/me/auth-methods aggregate read.
/// Drives the read through the real HTTP pipeline and asserts that the
/// response shape matches the seeded entity counts and that per-row
/// CanRemove flags reflect the last-method floor.
/// </summary>
public class AuthMethodsEndpointTests : IClassFixture<TenantServiceWebApplicationFactory>
{
    private readonly TenantServiceWebApplicationFactory _factory;

    public AuthMethodsEndpointTests(TenantServiceWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Get_AdminWithPasswordOnly_ReturnsPasswordSetAndFloorActive()
    {
        await _factory.SeedTestDataAsync();
        using var client = _factory.CreateAdminClient();

        var response = await client.GetAsync("/api/me/auth-methods");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<AuthMethodsResponse>();
        payload.Should().NotBeNull();
        payload!.Email.Should().Be("admin@test-org.sorcha.io");
        payload.EmailVerified.Should().BeTrue();
        payload.Password.IsSet.Should().BeTrue();
        payload.Password.CanRemove.Should().BeFalse(
            "the password is the admin's only sign-in method");
        payload.Socials.Should().BeEmpty();
        payload.Passkeys.Should().BeEmpty();
    }

    [Fact]
    public async Task Get_PasswordPlusSocialPlusPasskey_AllRowsCanRemove()
    {
        await _factory.SeedTestDataAsync();
        await SeedExtraMethodsAsync(TestDataSeeder.AdminPlatformUserId);
        using var client = _factory.CreateAdminClient();

        var response = await client.GetAsync("/api/me/auth-methods");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<AuthMethodsResponse>();
        payload.Should().NotBeNull();
        payload!.Password.IsSet.Should().BeTrue();
        payload.Password.CanRemove.Should().BeTrue("two other methods remain");
        payload.Socials.Should().HaveCount(1);
        payload.Socials[0].Provider.Should().Be("google");
        payload.Socials[0].CanRemove.Should().BeTrue();
        payload.Passkeys.Should().HaveCount(1);
        payload.Passkeys[0].Status.Should().Be(CredentialStatus.Active);
        payload.Passkeys[0].CanRemove.Should().BeTrue();
        payload.Passkeys[0].CanRename.Should().BeTrue();
    }

    [Fact]
    public async Task Get_RevokedPasskeyExcluded_DisabledIncluded()
    {
        await _factory.SeedTestDataAsync();
        await SeedExtraMethodsAsync(TestDataSeeder.AdminPlatformUserId,
            includeRevoked: true, includeDisabled: true);
        using var client = _factory.CreateAdminClient();

        var response = await client.GetAsync("/api/me/auth-methods");
        var payload = await response.Content.ReadFromJsonAsync<AuthMethodsResponse>();

        payload.Should().NotBeNull();
        payload!.Passkeys.Should().HaveCount(2, "Active + Disabled present; Revoked excluded");
        payload.Passkeys.Should().Contain(p => p.Status == CredentialStatus.Active);
        payload.Passkeys.Should().Contain(p => p.Status == CredentialStatus.Disabled);
        payload.Passkeys.Should().NotContain(p => p.Status == CredentialStatus.Revoked);

        var disabledRow = payload.Passkeys.Single(p => p.Status == CredentialStatus.Disabled);
        disabledRow.CanRename.Should().BeFalse("Disabled passkeys cannot be renamed");
    }

    [Fact]
    public async Task Get_Anonymous_Returns401()
    {
        await _factory.SeedTestDataAsync();
        using var client = _factory.CreateUnauthenticatedClient();

        var response = await client.GetAsync("/api/me/auth-methods");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Get_EmptyDisplayName_FallsBackToUnnamedPasskey()
    {
        await _factory.SeedTestDataAsync();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        db.PasskeyCredentials.Add(new PasskeyCredential
        {
            Id = Guid.NewGuid(),
            PlatformUserId = TestDataSeeder.AdminPlatformUserId,
            CredentialId = new byte[] { 9, 9 },
            PublicKeyCose = new byte[] { 1 },
            DisplayName = " ", // Whitespace -> server fallback
            AttestationType = "none",
            Status = CredentialStatus.Active,
        });
        await db.SaveChangesAsync();

        using var client = _factory.CreateAdminClient();
        var response = await client.GetAsync("/api/me/auth-methods");
        var payload = await response.Content.ReadFromJsonAsync<AuthMethodsResponse>();

        payload!.Passkeys.Single().DisplayName.Should().Be("Unnamed passkey");
    }

    private async Task SeedExtraMethodsAsync(
        Guid platformUserId,
        bool includeRevoked = false,
        bool includeDisabled = false)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();

        db.PlatformSocialLogins.Add(new PlatformSocialLogin
        {
            Id = Guid.NewGuid(),
            PlatformUserId = platformUserId,
            Provider = "google",
            Subject = $"google-{Guid.NewGuid():N}",
            Email = "user@gmail.com",
            DisplayName = "User",
            LinkedAt = DateTimeOffset.UtcNow,
        });

        db.PasskeyCredentials.Add(new PasskeyCredential
        {
            Id = Guid.NewGuid(),
            PlatformUserId = platformUserId,
            CredentialId = new byte[] { 1 },
            PublicKeyCose = new byte[] { 1 },
            DisplayName = "Active YubiKey",
            DeviceType = "YubiKey 5",
            AttestationType = "none",
            Status = CredentialStatus.Active,
        });

        if (includeDisabled)
        {
            db.PasskeyCredentials.Add(new PasskeyCredential
            {
                Id = Guid.NewGuid(),
                PlatformUserId = platformUserId,
                CredentialId = new byte[] { 2 },
                PublicKeyCose = new byte[] { 1 },
                DisplayName = "Cloned-detected key",
                AttestationType = "none",
                Status = CredentialStatus.Disabled,
                DisabledAt = DateTimeOffset.UtcNow,
                DisabledReason = "signature-counter-regression",
            });
        }

        if (includeRevoked)
        {
            db.PasskeyCredentials.Add(new PasskeyCredential
            {
                Id = Guid.NewGuid(),
                PlatformUserId = platformUserId,
                CredentialId = new byte[] { 3 },
                PublicKeyCose = new byte[] { 1 },
                DisplayName = "Old retired key",
                AttestationType = "none",
                Status = CredentialStatus.Revoked,
                DisabledAt = DateTimeOffset.UtcNow.AddDays(-30),
                DisabledReason = "user-removed",
            });
        }

        await db.SaveChangesAsync();
    }
}
