// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sorcha.Tenant.Service.Data;
using Sorcha.Tenant.Service.Endpoints;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Tests.Infrastructure;
using Xunit;

namespace Sorcha.Tenant.Service.Tests.Endpoints;

/// <summary>
/// Feature 118 T058 — covers <c>POST /api/internal/inbox</c>:
/// the RequireService policy gate, the idempotent write contract on
/// <c>(PlatformUserId, SourceEventId)</c>, and the validation rejections.
/// </summary>
public class InternalInboxEndpointsTests
    : IClassFixture<TenantServiceWebApplicationFactory>, IAsyncLifetime
{
    private readonly TenantServiceWebApplicationFactory _factory;
    private HttpClient _serviceClient = null!;

    public InternalInboxEndpointsTests(TenantServiceWebApplicationFactory factory)
    {
        _factory = factory;
    }

    public async ValueTask InitializeAsync()
    {
        await _factory.SeedTestDataAsync();
        _serviceClient = CreateServicePrincipalClient();
        await ClearInboxAsync();
    }

    public ValueTask DisposeAsync()
    {
        _serviceClient?.Dispose();
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task Write_WithoutServiceToken_ReturnsForbidden()
    {
        // Member-token client: authenticated, but no token_type=service claim.
        var memberClient = _factory.CreateMemberClient();
        var response = await memberClient.PostAsJsonAsync(
            "/api/internal/inbox", BuildRequest(TestDataSeeder.MemberPlatformUserId));

        response.StatusCode.Should().BeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Write_FirstCall_Returns201Created()
    {
        var request = BuildRequest(TestDataSeeder.MemberPlatformUserId);

        var response = await _serviceClient.PostAsJsonAsync("/api/internal/inbox", request);
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("idempotent").GetBoolean().Should().BeFalse();
        body.GetProperty("entry").GetProperty("title").GetString().Should().Be(request.Title);
    }

    [Fact]
    public async Task Write_DuplicateOnPlatformUserAndSourceEventId_Returns200Idempotent()
    {
        var request = BuildRequest(TestDataSeeder.MemberPlatformUserId);

        var first = await _serviceClient.PostAsJsonAsync("/api/internal/inbox", request);
        first.StatusCode.Should().Be(HttpStatusCode.Created);

        // Same SourceEventId — should fold to a no-op write.
        var second = await _serviceClient.PostAsJsonAsync("/api/internal/inbox", request);
        second.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await second.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("idempotent").GetBoolean().Should().BeTrue();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var rows = await db.InboxEntries.CountAsync(e =>
            e.PlatformUserId == request.PlatformUserId &&
            e.SourceEventId == request.SourceEventId);
        rows.Should().Be(1);
    }

    [Fact]
    public async Task Write_EmptyPlatformUserId_Returns400()
    {
        var request = BuildRequest(Guid.Empty);

        var response = await _serviceClient.PostAsJsonAsync("/api/internal/inbox", request);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Write_BlankCorrelationKey_Returns400()
    {
        var request = BuildRequest(TestDataSeeder.MemberPlatformUserId) with { CorrelationKey = "" };

        var response = await _serviceClient.PostAsJsonAsync("/api/internal/inbox", request);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Write_DetailHrefNotStartingWithApi_Returns400()
    {
        var request = BuildRequest(TestDataSeeder.MemberPlatformUserId) with
        {
            DetailHref = "https://evil.example/api/me/inbox/123"
        };

        var response = await _serviceClient.PostAsJsonAsync("/api/internal/inbox", request);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Write_TitleOver200Chars_Returns400()
    {
        var request = BuildRequest(TestDataSeeder.MemberPlatformUserId) with
        {
            Title = new string('x', 201)
        };

        var response = await _serviceClient.PostAsJsonAsync("/api/internal/inbox", request);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private static InboxWriteRequestDto BuildRequest(Guid platformUserId) => new(
        PlatformUserId: platformUserId,
        Category: InboxCategory.Action,
        Severity: InboxSeverity.Info,
        CorrelationKey: $"test:{Guid.NewGuid():N}",
        DetailHref: "/api/test/detail",
        SourceEventId: Guid.NewGuid(),
        OccurredAt: DateTimeOffset.UtcNow,
        Title: "Test inbox entry");

    private HttpClient CreateServicePrincipalClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-User-Id", Guid.NewGuid().ToString());
        client.DefaultRequestHeaders.Add("X-Test-Token-Type", "service");
        return client;
    }

    private async Task ClearInboxAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var existing = await db.InboxEntries.ToListAsync();
        if (existing.Count > 0)
        {
            db.InboxEntries.RemoveRange(existing);
            await db.SaveChangesAsync();
        }
    }
}
