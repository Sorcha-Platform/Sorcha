// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sorcha.Tenant.Service.Data;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Models.Dtos;
using Sorcha.Tenant.Service.Models.Requests;
using Sorcha.Tenant.Service.Tests.Infrastructure;

namespace Sorcha.Tenant.Service.Tests.Endpoints;

/// <summary>
/// Integration tests for the Feature 116 US2 passkey lifecycle endpoints
/// (PUT /credentials/{id} rename, modified DELETE /credentials/{id} soft-revoke).
/// Drives the unauthorised / missing-challenge / Disabled-bypass paths through
/// the real HTTP pipeline; the full challenge consume + transition matrix is
/// covered by <see cref="Services.PasskeyRevocationTests"/> against SQLite,
/// since the WebApplicationFactory's InMemory EF provider does not support
/// the <c>ExecuteUpdateAsync</c> used by the atomic-consume primitive.
/// </summary>
public class PasskeyEndpointTests : IClassFixture<TenantServiceWebApplicationFactory>
{
    private readonly TenantServiceWebApplicationFactory _factory;

    public PasskeyEndpointTests(TenantServiceWebApplicationFactory factory)
    {
        _factory = factory;
    }

    // -------- Rename endpoint ----------

    [Fact]
    public async Task Rename_Anonymous_Returns401()
    {
        await _factory.SeedTestDataAsync();
        var passkeyId = await SeedPasskeyAsync(TestDataSeeder.AdminPlatformUserId, CredentialStatus.Active);
        using var client = _factory.CreateUnauthenticatedClient();

        var response = await client.PutAsJsonAsync(
            $"/api/passkey/credentials/{passkeyId}",
            new PasskeyRenameRequest("New Name"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Rename_EmptyName_Returns400()
    {
        await _factory.SeedTestDataAsync();
        var passkeyId = await SeedPasskeyAsync(TestDataSeeder.AdminPlatformUserId, CredentialStatus.Active);
        using var client = _factory.CreateAdminClient();

        var response = await client.PutAsJsonAsync(
            $"/api/passkey/credentials/{passkeyId}",
            new PasskeyRenameRequest("   "));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Rename_OverlongName_Returns400()
    {
        await _factory.SeedTestDataAsync();
        var passkeyId = await SeedPasskeyAsync(TestDataSeeder.AdminPlatformUserId, CredentialStatus.Active);
        using var client = _factory.CreateAdminClient();

        var response = await client.PutAsJsonAsync(
            $"/api/passkey/credentials/{passkeyId}",
            new PasskeyRenameRequest(new string('x', 101)));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Rename_ActivePasskey_Returns204AndPersists()
    {
        await _factory.SeedTestDataAsync();
        var passkeyId = await SeedPasskeyAsync(TestDataSeeder.AdminPlatformUserId, CredentialStatus.Active, "Old Name");
        using var client = _factory.CreateAdminClient();

        var response = await client.PutAsJsonAsync(
            $"/api/passkey/credentials/{passkeyId}",
            new PasskeyRenameRequest("My YubiKey"));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var stored = await db.PasskeyCredentials.AsNoTracking().FirstAsync(c => c.Id == passkeyId);
        stored.DisplayName.Should().Be("My YubiKey");
    }

    [Fact]
    public async Task Rename_DisabledPasskey_Returns409()
    {
        await _factory.SeedTestDataAsync();
        var passkeyId = await SeedPasskeyAsync(
            TestDataSeeder.AdminPlatformUserId,
            CredentialStatus.Disabled,
            "Cloned",
            disabledReason: "signature-counter-regression");
        using var client = _factory.CreateAdminClient();

        var response = await client.PutAsJsonAsync(
            $"/api/passkey/credentials/{passkeyId}",
            new PasskeyRenameRequest("Renamed"));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Rename_NotFound_Returns404()
    {
        await _factory.SeedTestDataAsync();
        using var client = _factory.CreateAdminClient();

        var response = await client.PutAsJsonAsync(
            $"/api/passkey/credentials/{Guid.NewGuid()}",
            new PasskeyRenameRequest("Anything"));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // -------- Delete (soft-revoke) endpoint ----------

    [Fact]
    public async Task Delete_Anonymous_Returns401()
    {
        await _factory.SeedTestDataAsync();
        var passkeyId = await SeedPasskeyAsync(TestDataSeeder.AdminPlatformUserId, CredentialStatus.Active);
        using var client = _factory.CreateUnauthenticatedClient();

        var response = await client.DeleteAsync($"/api/passkey/credentials/{passkeyId}");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Delete_ActivePasskey_NoChallengeHeader_Returns401()
    {
        await _factory.SeedTestDataAsync();
        var passkeyId = await SeedPasskeyAsync(TestDataSeeder.AdminPlatformUserId, CredentialStatus.Active);
        using var client = _factory.CreateAdminClient();

        var response = await client.DeleteAsync($"/api/passkey/credentials/{passkeyId}");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Delete_NotFound_Returns404()
    {
        await _factory.SeedTestDataAsync();
        using var client = _factory.CreateAdminClient();

        var response = await client.DeleteAsync($"/api/passkey/credentials/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Delete_AlreadyRevoked_Returns404()
    {
        await _factory.SeedTestDataAsync();
        var passkeyId = await SeedPasskeyAsync(
            TestDataSeeder.AdminPlatformUserId,
            CredentialStatus.Revoked,
            "Old",
            disabledReason: "user-removed");
        using var client = _factory.CreateAdminClient();

        var response = await client.DeleteAsync($"/api/passkey/credentials/{passkeyId}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // -------- List filtering ----------

    [Fact]
    public async Task ListCredentials_ExcludesRevoked()
    {
        await _factory.SeedTestDataAsync();
        var activeId = await SeedPasskeyAsync(TestDataSeeder.AdminPlatformUserId, CredentialStatus.Active, "Active");
        var disabledId = await SeedPasskeyAsync(TestDataSeeder.AdminPlatformUserId, CredentialStatus.Disabled, "Disabled",
            disabledReason: "signature-counter-regression");
        var revokedId = await SeedPasskeyAsync(TestDataSeeder.AdminPlatformUserId, CredentialStatus.Revoked, "Revoked",
            disabledReason: "user-removed");
        using var client = _factory.CreateAdminClient();

        var response = await client.GetAsync("/api/passkey/credentials");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var payload = await response.Content.ReadFromJsonAsync<PasskeyCredentialListResponse>();
        payload.Should().NotBeNull();
        var ids = payload!.Credentials.Select(c => c.Id).ToList();
        ids.Should().Contain(activeId);
        ids.Should().Contain(disabledId);
        ids.Should().NotContain(revokedId);
    }

    // -------- Helpers ----------

    private async Task<Guid> SeedPasskeyAsync(
        Guid platformUserId,
        CredentialStatus status,
        string displayName = "Test Key",
        string? disabledReason = null)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var passkey = new PasskeyCredential
        {
            Id = Guid.NewGuid(),
            PlatformUserId = platformUserId,
            CredentialId = Guid.NewGuid().ToByteArray(),
            PublicKeyCose = new byte[] { 1, 2, 3 },
            DisplayName = displayName,
            AttestationType = "none",
            Status = status,
            DisabledAt = status != CredentialStatus.Active ? DateTimeOffset.UtcNow : null,
            DisabledReason = disabledReason,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.PasskeyCredentials.Add(passkey);
        await db.SaveChangesAsync();
        return passkey.Id;
    }

}
