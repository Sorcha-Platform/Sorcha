// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sorcha.Tenant.Service.Data;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Tests.Infrastructure;
using Xunit;

namespace Sorcha.Tenant.Service.Tests.Endpoints;

/// <summary>
/// Feature 118 T057 — integration coverage for the public
/// <c>/api/me/inbox/*</c> endpoints. Covers the six endpoints, authorization
/// scoping (caller never sees another user's entries — 404 indistinguishable),
/// and idempotency on the read / dismiss / mark-all paths.
/// </summary>
public class MeInboxEndpointsTests
    : IClassFixture<TenantServiceWebApplicationFactory>, IAsyncLifetime
{
    private readonly TenantServiceWebApplicationFactory _factory;
    private HttpClient _memberClient = null!;

    public MeInboxEndpointsTests(TenantServiceWebApplicationFactory factory)
    {
        _factory = factory;
    }

    public async ValueTask InitializeAsync()
    {
        await _factory.SeedTestDataAsync();
        _memberClient = _factory.CreateMemberClient();
        // Inbox endpoints scope by the JWT platform_user_id claim — point the
        // test handler at MemberPlatformUserId (distinct from the legacy
        // MemberUserId set by CreateMemberClient).
        _memberClient.DefaultRequestHeaders.Add(
            "X-Test-Platform-User-Id", TestDataSeeder.MemberPlatformUserId.ToString());
        await ClearInboxAsync();
    }

    public ValueTask DisposeAsync()
    {
        _memberClient?.Dispose();
        return ValueTask.CompletedTask;
    }

    // -----------------------------------------------------------------------
    // GET /api/me/inbox
    // -----------------------------------------------------------------------

    [Fact]
    public async Task List_WithoutAuth_ReturnsUnauthorized()
    {
        var unauth = _factory.CreateUnauthenticatedClient();
        var response = await unauth.GetAsync("/api/me/inbox");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task List_NoEntries_ReturnsEmptyPage()
    {
        var response = await _memberClient.GetAsync("/api/me/inbox");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("entries").GetArrayLength().Should().Be(0);
        body.GetProperty("totalCount").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task List_ReturnsOnlyCallersEntries()
    {
        await SeedEntryAsync(TestDataSeeder.MemberPlatformUserId, "mine-1");
        await SeedEntryAsync(TestDataSeeder.MemberPlatformUserId, "mine-2");
        // Other user's entry — must NOT leak through.
        await SeedEntryAsync(TestDataSeeder.AdminPlatformUserId, "theirs");

        var response = await _memberClient.GetAsync("/api/me/inbox");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var entries = body.GetProperty("entries").EnumerateArray()
            .Select(e => e.GetProperty("title").GetString()).ToList();
        entries.Should().BeEquivalentTo(new[] { "mine-1", "mine-2" });
    }

    // -----------------------------------------------------------------------
    // GET /api/me/inbox/unread-count
    // -----------------------------------------------------------------------

    [Fact]
    public async Task UnreadCount_OnlyCountsCallersUnreadEntries()
    {
        await SeedEntryAsync(TestDataSeeder.MemberPlatformUserId, "mine-unread");
        await SeedEntryAsync(TestDataSeeder.MemberPlatformUserId, "mine-read", readAt: DateTimeOffset.UtcNow);
        await SeedEntryAsync(TestDataSeeder.AdminPlatformUserId, "theirs-unread");

        var response = await _memberClient.GetAsync("/api/me/inbox/unread-count");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("unread").GetInt32().Should().Be(1);
    }

    // -----------------------------------------------------------------------
    // GET /api/me/inbox/{id}
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GetById_OwnedEntry_ReturnsOk()
    {
        var entry = await SeedEntryAsync(TestDataSeeder.MemberPlatformUserId, "mine");

        var response = await _memberClient.GetAsync($"/api/me/inbox/{entry.Id}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetById_OtherUsersEntry_Returns404()
    {
        var entry = await SeedEntryAsync(TestDataSeeder.AdminPlatformUserId, "theirs");

        var response = await _memberClient.GetAsync($"/api/me/inbox/{entry.Id}");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetById_UnknownId_Returns404Indistinguishably()
    {
        var response = await _memberClient.GetAsync($"/api/me/inbox/{Guid.NewGuid()}");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // -----------------------------------------------------------------------
    // POST /api/me/inbox/{id}/read
    // -----------------------------------------------------------------------

    [Fact]
    public async Task MarkRead_OwnedUnreadEntry_Succeeds()
    {
        var entry = await SeedEntryAsync(TestDataSeeder.MemberPlatformUserId, "mine");

        var response = await _memberClient.PostAsync($"/api/me/inbox/{entry.Id}/read", null);
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var stored = await db.InboxEntries.FindAsync(entry.Id);
        stored!.ReadAt.Should().NotBeNull();
    }

    [Fact]
    public async Task MarkRead_AlreadyRead_IsIdempotent()
    {
        var entry = await SeedEntryAsync(TestDataSeeder.MemberPlatformUserId, "mine");

        var first = await _memberClient.PostAsync($"/api/me/inbox/{entry.Id}/read", null);
        first.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var second = await _memberClient.PostAsync($"/api/me/inbox/{entry.Id}/read", null);
        second.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task MarkRead_OtherUsersEntry_Returns404_NoMutation()
    {
        var foreign = await SeedEntryAsync(TestDataSeeder.AdminPlatformUserId, "theirs");

        var response = await _memberClient.PostAsync($"/api/me/inbox/{foreign.Id}/read", null);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var stored = await db.InboxEntries.FindAsync(foreign.Id);
        stored!.ReadAt.Should().BeNull();
    }

    // -----------------------------------------------------------------------
    // POST /api/me/inbox/{id}/dismiss
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Dismiss_OwnedEntry_FlipsDismissedAt()
    {
        var entry = await SeedEntryAsync(TestDataSeeder.MemberPlatformUserId, "mine");

        var response = await _memberClient.PostAsync($"/api/me/inbox/{entry.Id}/dismiss", null);
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var stored = await db.InboxEntries.FindAsync(entry.Id);
        stored!.DismissedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Dismiss_OtherUsersEntry_Returns404()
    {
        var foreign = await SeedEntryAsync(TestDataSeeder.AdminPlatformUserId, "theirs");

        var response = await _memberClient.PostAsync($"/api/me/inbox/{foreign.Id}/dismiss", null);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // -----------------------------------------------------------------------
    // POST /api/me/inbox/mark-all-read
    // -----------------------------------------------------------------------

    [Fact]
    public async Task MarkAllRead_AffectsOnlyCallersEntries()
    {
        await SeedEntryAsync(TestDataSeeder.MemberPlatformUserId, "mine-1");
        await SeedEntryAsync(TestDataSeeder.MemberPlatformUserId, "mine-2");
        var foreign = await SeedEntryAsync(TestDataSeeder.AdminPlatformUserId, "theirs");

        var response = await _memberClient.PostAsync("/api/me/inbox/mark-all-read", null);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("marked").GetInt32().Should().Be(2);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var foreignStored = await db.InboxEntries.FindAsync(foreign.Id);
        foreignStored!.ReadAt.Should().BeNull("MarkAllRead must not bleed across user boundaries");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private async Task<InboxEntry> SeedEntryAsync(
        Guid platformUserId, string title, DateTimeOffset? readAt = null)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();

        var entry = new InboxEntry
        {
            Id = Guid.NewGuid(),
            PlatformUserId = platformUserId,
            Category = InboxCategory.Action,
            Severity = InboxSeverity.Info,
            CorrelationKey = $"test:{Guid.NewGuid():N}",
            DetailHref = "/api/test/detail",
            SourceEventId = Guid.NewGuid(),
            OccurredAt = DateTimeOffset.UtcNow,
            Title = title,
            ChannelHints = ChannelHints.Inbox,
            ReadAt = readAt,
        };

        db.InboxEntries.Add(entry);
        await db.SaveChangesAsync();
        return entry;
    }

    private async Task ClearInboxAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        // ExecuteDeleteAsync isn't supported by the in-memory provider used in tests.
        var existing = await db.InboxEntries.ToListAsync();
        if (existing.Count > 0)
        {
            db.InboxEntries.RemoveRange(existing);
            await db.SaveChangesAsync();
        }
    }
}
