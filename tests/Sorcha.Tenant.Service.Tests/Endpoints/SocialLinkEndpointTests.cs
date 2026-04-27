// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Sorcha.Tenant.Service.Data;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Models.Dtos;
using Sorcha.Tenant.Service.Tests.Infrastructure;

namespace Sorcha.Tenant.Service.Tests.Endpoints;

/// <summary>
/// Integration tests for the social link/unlink HTTP surface (Feature 116 US1).
/// Drives intent dispatch, the orphan-route removal, and the challenge-gated
/// unlink filter through the real pipeline. The full challenge → mutation
/// happy path is covered by <see cref="Services.SocialLinkServiceTests"/>
/// and the unit-level service tests; the WebApplicationFactory's InMemory
/// EF provider does not support the <c>ExecuteUpdateAsync</c> used by the
/// atomic-consume filter, so end-to-end consume is verified by SQLite-based
/// service tests instead.
/// </summary>
public class SocialLinkEndpointTests : IClassFixture<TenantServiceWebApplicationFactory>
{
    private readonly TenantServiceWebApplicationFactory _factory;

    public SocialLinkEndpointTests(TenantServiceWebApplicationFactory factory)
    {
        _factory = factory;
    }

    // Note: a default-intent (login) anonymous test would require
    // PlatformSettings to be bootstrapped in the test seed. Since the login
    // path here is unchanged behaviour from before this PR, we rely on the
    // existing SocialLoginEndpointsTests for that coverage. The link path
    // — which IS new — is exercised below.

    [Fact]
    public async Task Initiate_IntentLink_AnonymousRejectedWith401()
    {
        await _factory.SeedTestDataAsync();
        using var client = _factory.CreateUnauthenticatedClient();

        var response = await client.PostAsJsonAsync("/api/auth/social/initiate",
            new SocialLoginInitiateRequest { Provider = "google", Intent = "link" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Initiate_InvalidIntent_Returns400()
    {
        await _factory.SeedTestDataAsync();
        using var client = _factory.CreateAdminClient();

        var response = await client.PostAsJsonAsync("/api/auth/social/initiate",
            new SocialLoginInitiateRequest { Provider = "google", Intent = "merge" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task OrphanedSocialLinkRoute_NoLongerMaps_Returns404()
    {
        // T043: the legacy POST /api/auth/social/link initiate-only endpoint
        // was removed. UI now calls /initiate directly with intent=link.
        await _factory.SeedTestDataAsync();
        using var client = _factory.CreateAdminClient();

        var response = await client.PostAsJsonAsync("/api/auth/social/link",
            new SocialLoginInitiateRequest { Provider = "google" });

        // Either 404 (no route) or 405 (path collides with the
        // /api/auth/social/{linkId:guid} DELETE route that was added in this
        // PR — "link" fails the :guid constraint but the path-match yields a
        // method-not-allowed). Both confirm the orphan endpoint is gone.
        response.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed);
    }

    [Fact]
    public async Task Unlink_WithoutChallengeHeader_Rejected()
    {
        await _factory.SeedTestDataAsync();
        var linkId = await SeedLinkAsync(TestDataSeeder.AdminPlatformUserId);
        using var client = _factory.CreateAdminClient();

        var response = await client.DeleteAsync($"/api/auth/social/{linkId}");

        // The challenge filter rejects before reaching the underlying handler;
        // that's the contract — every remove endpoint refuses calls that lack
        // a fresh challenge token. Status is 401 per the filter.
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Unlink_AnonymousCaller_Rejected()
    {
        await _factory.SeedTestDataAsync();
        var linkId = await SeedLinkAsync(TestDataSeeder.AdminPlatformUserId);
        using var client = _factory.CreateUnauthenticatedClient();

        var response = await client.DeleteAsync($"/api/auth/social/{linkId}");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private async Task<Guid> SeedLinkAsync(Guid platformUserId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var link = new PlatformSocialLogin
        {
            Id = Guid.NewGuid(),
            PlatformUserId = platformUserId,
            Provider = "google",
            Subject = $"sub-{Guid.NewGuid():N}",
            Email = "user@gmail.com",
            LinkedAt = DateTimeOffset.UtcNow,
        };
        db.PlatformSocialLogins.Add(link);
        await db.SaveChangesAsync();
        return link.Id;
    }
}
