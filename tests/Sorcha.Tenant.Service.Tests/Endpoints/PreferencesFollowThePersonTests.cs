// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sorcha.Tenant.Service.Data;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Tests.Infrastructure;
using Xunit;

namespace Sorcha.Tenant.Service.Tests.Endpoints;

/// <summary>
/// #1694 — a person's preferences are theirs, in every organisation they belong to, and the service
/// that delivers their notifications can read them.
/// </summary>
/// <remarks>
/// Preferences were keyed by the JWT <c>sub</c>, which is the per-organisation <c>UserIdentity</c>
/// id: someone in two orgs had two unrelated sets. The Wallet, which knows only a wallet's owner (a
/// <c>PlatformUser</c> id), called the user endpoint unauthenticated and got 401 every time — and
/// would have missed anyway. No one's notification preferences had ever been honoured.
/// </remarks>
public class PreferencesFollowThePersonTests : IClassFixture<TenantServiceWebApplicationFactory>, IAsyncLifetime
{
    private readonly TenantServiceWebApplicationFactory _factory;

    public PreferencesFollowThePersonTests(TenantServiceWebApplicationFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync() => await _factory.SeedTestDataAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private HttpClient PersonIn(Guid platformUserId, Guid userIdentityId, Guid organizationId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Role", "Member");
        client.DefaultRequestHeaders.Add("X-Test-User-Id", userIdentityId.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Organization-Id", organizationId.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Platform-User-Id", platformUserId.ToString());
        return client;
    }

    private HttpClient Service()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-User-Id", Guid.NewGuid().ToString());
        client.DefaultRequestHeaders.Add("X-Test-Token-Type", "service");
        return client;
    }

    private sealed record Prefs(string Theme, bool NotificationsEnabled, string NotificationFrequency);

    private sealed record NotificationPrefs(
        Guid PlatformUserId, bool NotificationsEnabled, string NotificationMethod, string NotificationFrequency);

    [Fact]
    public async Task OnePerson_InTwoOrganisations_HasOneSetOfPreferences()
    {
        var person = Guid.NewGuid();
        using var inOrgA = PersonIn(person, userIdentityId: Guid.NewGuid(), organizationId: Guid.NewGuid());
        using var inOrgB = PersonIn(person, userIdentityId: Guid.NewGuid(), organizationId: Guid.NewGuid());

        (await inOrgA.PutAsJsonAsync("/api/preferences", new { theme = "Dark" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var seenInB = await inOrgB.GetFromJsonAsync<Prefs>("/api/preferences");

        seenInB!.Theme.Should().Be("Dark",
            "the same person switching organisation must keep their preferences");
    }

    [Fact]
    public async Task TwoDifferentPeople_DoNotShare()
    {
        var org = Guid.NewGuid();
        using var alice = PersonIn(Guid.NewGuid(), Guid.NewGuid(), org);
        using var bob = PersonIn(Guid.NewGuid(), Guid.NewGuid(), org);

        await alice.PutAsJsonAsync("/api/preferences", new { theme = "Dark" });

        (await bob.GetFromJsonAsync<Prefs>("/api/preferences"))!.Theme.Should().Be("System");
    }

    [Fact]
    public async Task ATokenWithoutAPerson_IsRefused()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Role", "Member");
        client.DefaultRequestHeaders.Add("X-Test-User-Id", Guid.NewGuid().ToString());

        (await client.GetAsync("/api/preferences")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private async Task SavePreferencesAsync(Guid platformUserId, bool enabled, NotificationFrequency frequency)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var existing = await db.UserPreferences.FirstOrDefaultAsync(p => p.PlatformUserId == platformUserId);
        if (existing is not null) db.UserPreferences.Remove(existing);
        db.UserPreferences.Add(new UserPreferences
        {
            PlatformUserId = platformUserId,
            NotificationsEnabled = enabled,
            NotificationFrequency = frequency,
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task TheDeliveringService_ReadsThePersonsPreferences_ByPlatformUserId()
    {
        await SavePreferencesAsync(TestDataSeeder.AdminPlatformUserId, enabled: false, NotificationFrequency.DailyDigest);
        using var service = Service();

        var prefs = await service.GetFromJsonAsync<NotificationPrefs>(
            $"/api/internal/users/{TestDataSeeder.AdminPlatformUserId}/notification-preferences");

        prefs!.NotificationsEnabled.Should().BeFalse();
        prefs.NotificationFrequency.Should().Be("DailyDigest");
    }

    [Fact]
    public async Task TheDeliveringService_ReachesTheSamePerson_FromAnOrgIdentityId()
    {
        // Wallet.Owner is a UserIdentity id on legacy/org wallets (CLAUDE.md §26).
        await SavePreferencesAsync(TestDataSeeder.AdminPlatformUserId, enabled: false, NotificationFrequency.HourlyDigest);
        using var service = Service();

        var prefs = await service.GetFromJsonAsync<NotificationPrefs>(
            $"/api/internal/users/{TestDataSeeder.AdminUserId}/notification-preferences");

        prefs!.PlatformUserId.Should().Be(TestDataSeeder.AdminPlatformUserId);
        prefs.NotificationFrequency.Should().Be("HourlyDigest");
    }

    [Fact]
    public async Task NoSavedPreferences_Or_AnUnknownId_Is404_SoTheCallersDefaultApplies()
    {
        using var service = Service();

        (await service.GetAsync($"/api/internal/users/{Guid.NewGuid()}/notification-preferences"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await service.GetAsync($"/api/internal/users/{TestDataSeeder.AuditorPlatformUserId}/notification-preferences"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task APersonCannotReadAnotherPersonsPreferences_ThroughTheInternalRoute()
    {
        using var person = PersonIn(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        (await person.GetAsync($"/api/internal/users/{TestDataSeeder.AdminPlatformUserId}/notification-preferences"))
            .StatusCode.Should().BeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.Unauthorized);
    }
}
