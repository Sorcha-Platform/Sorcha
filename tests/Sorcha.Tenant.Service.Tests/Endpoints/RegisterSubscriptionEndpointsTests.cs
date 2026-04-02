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

public class RegisterSubscriptionEndpointsTests : IClassFixture<TenantServiceWebApplicationFactory>, IAsyncLifetime
{
    private readonly TenantServiceWebApplicationFactory _factory;
    private readonly HttpClient _adminClient;
    private readonly HttpClient _memberClient;

    // Valid 32-char hex register IDs for testing
    private const string ValidRegisterId = "aabbccdd11223344aabbccdd11223344";
    private const string ValidRegisterId2 = "11223344aabbccdd11223344aabbccdd";
    private const string OwnerRegisterId = "eeeeffffeeeeffffeeeeffffeeeeeeee";

    public RegisterSubscriptionEndpointsTests(TenantServiceWebApplicationFactory factory)
    {
        _factory = factory;
        _adminClient = _factory.CreateAdminClient();
        _memberClient = _factory.CreateMemberClient();
    }

    public async ValueTask InitializeAsync() => await _factory.SeedTestDataAsync();
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private string OrgUrl => $"/api/organizations/{TestDataSeeder.TestOrganizationId}/register-subscriptions";

    // ── List ────────────────────────────────────────────────────

    [Fact]
    public async Task ListSubscriptions_ReturnsEmptyList()
    {
        var response = await _memberClient.GetAsync(OrgUrl);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<RegisterSubscriptionListResponse>();
        result.Should().NotBeNull();
        result!.Items.Should().BeEmpty();
        result.TotalCount.Should().Be(0);
        result.Page.Should().Be(1);
        result.PageSize.Should().Be(25);
    }

    // ── Subscribe ───────────────────────────────────────────────

    [Fact]
    public async Task Subscribe_ValidRegister_Returns201()
    {
        var request = new SubscribeRequest { RegisterId = ValidRegisterId };

        var response = await _adminClient.PostAsJsonAsync(OrgUrl, request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var result = await response.Content.ReadFromJsonAsync<RegisterSubscriptionResponse>();
        result.Should().NotBeNull();
        result!.RegisterId.Should().Be(ValidRegisterId);
        result.OrganizationId.Should().Be(TestDataSeeder.TestOrganizationId);
        result.SubscriptionType.Should().Be("Public");
        result.Status.Should().Be("Active");
        result.SubscribedByUserId.Should().Be(TestDataSeeder.AdminUserId);
    }

    [Fact]
    public async Task Subscribe_DuplicateRegister_Returns409()
    {
        var request = new SubscribeRequest { RegisterId = ValidRegisterId2 };

        // First subscription should succeed
        var first = await _adminClient.PostAsJsonAsync(OrgUrl, request);
        first.StatusCode.Should().Be(HttpStatusCode.Created);

        // Second subscription to same register should return 409
        var second = await _adminClient.PostAsJsonAsync(OrgUrl, request);
        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Subscribe_AsMember_Returns403()
    {
        var request = new SubscribeRequest { RegisterId = "ffaabbcc11223344ffaabbcc11223344" };

        var response = await _memberClient.PostAsJsonAsync(OrgUrl, request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── Get ─────────────────────────────────────────────────────

    [Fact]
    public async Task GetSubscription_Exists_Returns200()
    {
        var registerId = "deadbeefdeadbeefdeadbeefdeadbeef";
        var request = new SubscribeRequest { RegisterId = registerId };
        await _adminClient.PostAsJsonAsync(OrgUrl, request);

        var response = await _memberClient.GetAsync($"{OrgUrl}/{registerId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<RegisterSubscriptionResponse>();
        result.Should().NotBeNull();
        result!.RegisterId.Should().Be(registerId);
    }

    [Fact]
    public async Task GetSubscription_NotFound_Returns404()
    {
        var response = await _memberClient.GetAsync($"{OrgUrl}/00000000000000000000000000000000");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── Unsubscribe ─────────────────────────────────────────────

    [Fact]
    public async Task Unsubscribe_PublicType_Returns204()
    {
        var registerId = "cafebabecafebabecafebabecafebabe";
        var request = new SubscribeRequest { RegisterId = registerId };
        await _adminClient.PostAsJsonAsync(OrgUrl, request);

        var response = await _adminClient.DeleteAsync($"{OrgUrl}/{registerId}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Unsubscribe_OwnerType_Returns400()
    {
        // Seed an Owner subscription directly in the database
        await SeedOwnerSubscription(OwnerRegisterId);

        var response = await _adminClient.DeleteAsync($"{OrgUrl}/{OwnerRegisterId}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Unsubscribe_AsMember_Returns403()
    {
        var registerId = "abcdef01abcdef01abcdef01abcdef01";
        var request = new SubscribeRequest { RegisterId = registerId };
        await _adminClient.PostAsJsonAsync(OrgUrl, request);

        var response = await _memberClient.DeleteAsync($"{OrgUrl}/{registerId}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── /api/me/subscribed-registers ────────────────────────────

    [Fact]
    public async Task GetMySubscribedRegisters_Authenticated_ReturnsSubscriptions()
    {
        var response = await _memberClient.GetAsync("/api/me/subscribed-registers");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<List<RegisterSubscriptionResponse>>();
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task GetMySubscribedRegisters_Unauthenticated_Returns401()
    {
        var unauthClient = _factory.CreateUnauthenticatedClient();

        var response = await unauthClient.GetAsync("/api/me/subscribed-registers");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── Helpers ─────────────────────────────────────────────────

    private async Task SeedOwnerSubscription(string registerId)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TenantDbContext>();

        context.OrganizationRegisterSubscriptions.Add(new OrganizationRegisterSubscription
        {
            OrganizationId = TestDataSeeder.TestOrganizationId,
            RegisterId = registerId,
            RegisterName = "Owner Register",
            SubscriptionType = SubscriptionType.Owner,
            Status = SubscriptionStatus.Active,
            SubscribedAt = DateTimeOffset.UtcNow,
            SubscribedByUserId = TestDataSeeder.AdminUserId
        });

        await context.SaveChangesAsync();
    }
}
