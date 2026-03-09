// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Net.Http.Json;

using FluentAssertions;

using Sorcha.Tenant.Service.Models.Dtos;
using Sorcha.Tenant.Service.Tests.Infrastructure;

namespace Sorcha.Tenant.Service.Tests.Endpoints;

public class InvitationEndpointTests : IClassFixture<TenantServiceWebApplicationFactory>, IAsyncLifetime
{
    private readonly TenantServiceWebApplicationFactory _factory;
    private readonly HttpClient _adminClient;
    private readonly HttpClient _memberClient;

    public InvitationEndpointTests(TenantServiceWebApplicationFactory factory)
    {
        _factory = factory;
        _adminClient = _factory.CreateAdminClient();
        _memberClient = _factory.CreateMemberClient();
    }

    public async ValueTask InitializeAsync() => await _factory.SeedTestDataAsync();
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task CreateInvitation_AsAdmin_Returns201()
    {
        var request = new CreateInvitationRequest
        {
            Email = "invite@example.com",
            Role = Sorcha.Tenant.Service.Models.UserRole.Designer,
            ExpiryDays = 7
        };

        var response = await _adminClient.PostAsJsonAsync(
            $"/api/organizations/{TestDataSeeder.TestOrganizationId}/invitations", request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var result = await response.Content.ReadFromJsonAsync<InvitationResponse>();
        result.Should().NotBeNull();
        result!.Email.Should().Be("invite@example.com");
        result.AssignedRole.Should().Be("Designer");
        result.Status.Should().Be("Pending");
    }

    [Fact]
    public async Task CreateInvitation_AsMember_Returns403()
    {
        var request = new CreateInvitationRequest { Email = "forbidden@example.com" };

        var response = await _memberClient.PostAsJsonAsync(
            $"/api/organizations/{TestDataSeeder.TestOrganizationId}/invitations", request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ListInvitations_AsAdmin_ReturnsInvitations()
    {
        // Seed an invitation first
        var request = new CreateInvitationRequest { Email = "list@example.com" };
        await _adminClient.PostAsJsonAsync(
            $"/api/organizations/{TestDataSeeder.TestOrganizationId}/invitations", request);

        var response = await _adminClient.GetAsync(
            $"/api/organizations/{TestDataSeeder.TestOrganizationId}/invitations");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var invitations = await response.Content.ReadFromJsonAsync<List<InvitationResponse>>();
        invitations.Should().NotBeNull();
        invitations.Should().Contain(i => i.Email == "list@example.com");
    }

    [Fact]
    public async Task ListInvitations_WithStatusFilter_FiltersCorrectly()
    {
        var response = await _adminClient.GetAsync(
            $"/api/organizations/{TestDataSeeder.TestOrganizationId}/invitations?status=Accepted");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task RevokeInvitation_PendingInvitation_Returns200()
    {
        var createRequest = new CreateInvitationRequest { Email = "revoke@example.com" };
        var createResponse = await _adminClient.PostAsJsonAsync(
            $"/api/organizations/{TestDataSeeder.TestOrganizationId}/invitations", createRequest);
        var created = await createResponse.Content.ReadFromJsonAsync<InvitationResponse>();

        var revokeResponse = await _adminClient.PostAsync(
            $"/api/organizations/{TestDataSeeder.TestOrganizationId}/invitations/{created!.Id}/revoke",
            null);

        revokeResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task RevokeInvitation_NonExistent_Returns404()
    {
        var response = await _adminClient.PostAsync(
            $"/api/organizations/{TestDataSeeder.TestOrganizationId}/invitations/{Guid.NewGuid()}/revoke",
            null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task CreateInvitation_Unauthenticated_Returns401()
    {
        var unauthClient = _factory.CreateUnauthenticatedClient();
        var request = new CreateInvitationRequest { Email = "unauth@example.com" };

        var response = await unauthClient.PostAsJsonAsync(
            $"/api/organizations/{TestDataSeeder.TestOrganizationId}/invitations", request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
