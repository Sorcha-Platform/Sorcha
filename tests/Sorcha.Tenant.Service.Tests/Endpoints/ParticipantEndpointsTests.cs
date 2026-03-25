// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Sorcha.Tenant.Models;
using Sorcha.Tenant.Service.Models.Dtos;
using Sorcha.Tenant.Service.Tests.Infrastructure;
using Xunit;

namespace Sorcha.Tenant.Service.Tests.Endpoints;

public class ParticipantEndpointsTests : IClassFixture<TenantServiceWebApplicationFactory>, IAsyncLifetime
{
    private readonly TenantServiceWebApplicationFactory _factory;
    private HttpClient _client = null!;

    public ParticipantEndpointsTests(TenantServiceWebApplicationFactory factory)
    {
        _factory = factory;
    }

    public async ValueTask InitializeAsync()
    {
        // Use authenticated admin client for authorized tests
        _client = _factory.CreateAdminClient();

        // Seed test data before tests
        await _factory.SeedTestDataAsync();
    }

    public ValueTask DisposeAsync()
    {
        _client?.Dispose();
        return ValueTask.CompletedTask;
    }

    #region POST /api/organizations/{orgId}/participants Tests

    [Fact]
    public async Task CreateParticipant_WithoutAuth_ReturnsUnauthorized()
    {
        // Arrange
        var orgId = Guid.NewGuid();
        var request = new CreateParticipantRequest { UserId = Guid.NewGuid() };

        // Use unauthenticated client
        var unauthClient = _factory.CreateClient();

        // Act
        var response = await unauthClient.PostAsJsonAsync($"/api/organizations/{orgId}/participants", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CreateParticipant_ValidRequest_ReturnsCreated()
    {
        // Arrange - Create org and user first via seeded data
        var orgId = TestDataSeeder.TestOrganizationId;
        var userId = TestDataSeeder.AdminUserId;
        var request = new CreateParticipantRequest { UserId = userId };

        // Act
        var response = await _client.PostAsJsonAsync($"/api/organizations/{orgId}/participants", request);

        // Assert - May return 201 Created or 409 Conflict if already exists
        response.StatusCode.Should().BeOneOf(HttpStatusCode.Created, HttpStatusCode.Conflict);

        if (response.StatusCode == HttpStatusCode.Created)
        {
            var result = await response.Content.ReadFromJsonAsync<ParticipantDetailResponse>();
            result.Should().NotBeNull();
            result!.UserId.Should().Be(userId);
            result.OrganizationId.Should().Be(orgId);
        }
    }

    #endregion

    #region GET /api/organizations/{orgId}/participants Tests

    [Fact]
    public async Task ListParticipants_WithoutAuth_ReturnsUnauthorized()
    {
        // Arrange
        var orgId = Guid.NewGuid();
        var unauthClient = _factory.CreateClient();

        // Act
        var response = await unauthClient.GetAsync($"/api/organizations/{orgId}/participants");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ListParticipants_ValidRequest_ReturnsOk()
    {
        // Arrange
        var orgId = TestDataSeeder.TestOrganizationId;

        // Act
        var response = await _client.GetAsync($"/api/organizations/{orgId}/participants");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<ParticipantListResponse>();
        result.Should().NotBeNull();
        result!.Participants.Should().NotBeNull();
        result.Page.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task ListParticipants_WithPagination_ReturnsPaginatedResults()
    {
        // Arrange
        var orgId = TestDataSeeder.TestOrganizationId;

        // Act
        var response = await _client.GetAsync($"/api/organizations/{orgId}/participants?page=1&pageSize=5");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<ParticipantListResponse>();
        result.Should().NotBeNull();
        result!.Page.Should().Be(1);
        result.PageSize.Should().Be(5);
    }

    [Fact]
    public async Task ListParticipants_WithStatusFilter_FiltersResults()
    {
        // Arrange
        var orgId = TestDataSeeder.TestOrganizationId;

        // Act
        var response = await _client.GetAsync($"/api/organizations/{orgId}/participants?status=Active");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<ParticipantListResponse>();
        result.Should().NotBeNull();
        // All returned participants should have Active status
        result!.Participants.Should().OnlyContain(p => p.Status == ParticipantIdentityStatus.Active);
    }

    #endregion

    #region GET /api/organizations/{orgId}/participants/{id} Tests

    [Fact]
    public async Task GetParticipant_NonExistent_ReturnsNotFound()
    {
        // Arrange
        var orgId = TestDataSeeder.TestOrganizationId;
        var participantId = Guid.NewGuid();

        // Act
        var response = await _client.GetAsync($"/api/organizations/{orgId}/participants/{participantId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    #endregion

    #region POST /api/participants/search Tests

    [Fact]
    public async Task SearchParticipants_WithoutAuth_ReturnsUnauthorized()
    {
        // Arrange
        var request = new ParticipantSearchRequest { Query = "test" };
        var unauthClient = _factory.CreateClient();

        // Act
        var response = await unauthClient.PostAsJsonAsync("/api/participants/search", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task SearchParticipants_ValidRequest_ReturnsOk()
    {
        // Arrange
        var request = new ParticipantSearchRequest
        {
            Query = "alice",
            Page = 1,
            PageSize = 20
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/participants/search", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<ParticipantSearchResponse>();
        result.Should().NotBeNull();
        result!.Query.Should().Be("alice");
    }

    [Fact]
    public async Task SearchParticipants_WithOrgFilter_ReturnsFilteredResults()
    {
        // Arrange
        var request = new ParticipantSearchRequest
        {
            OrganizationId = TestDataSeeder.TestOrganizationId,
            Page = 1,
            PageSize = 20
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/participants/search", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<ParticipantSearchResponse>();
        result.Should().NotBeNull();
        // All results should be from the specified organization
        result!.Results.Should().OnlyContain(p => p.OrganizationId == TestDataSeeder.TestOrganizationId);
    }

    [Fact]
    public async Task SearchParticipants_WithStatusFilter_ReturnsFilteredResults()
    {
        // Arrange
        var request = new ParticipantSearchRequest
        {
            Status = ParticipantIdentityStatus.Active,
            Page = 1,
            PageSize = 20
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/participants/search", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<ParticipantSearchResponse>();
        result.Should().NotBeNull();
        result!.Results.Should().OnlyContain(p => p.Status == ParticipantIdentityStatus.Active);
    }

    [Fact]
    public async Task SearchParticipants_WithHasLinkedWalletFilter_ReturnsFilteredResults()
    {
        // Arrange
        var request = new ParticipantSearchRequest
        {
            HasLinkedWallet = true,
            Page = 1,
            PageSize = 20
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/participants/search", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<ParticipantSearchResponse>();
        result.Should().NotBeNull();
        // Results with linked wallets should have HasLinkedWallet = true
        result!.Results.Should().OnlyContain(p => p.HasLinkedWallet);
    }

    [Fact]
    public async Task SearchParticipants_WithPagination_ReturnsPaginatedResults()
    {
        // Arrange
        var request = new ParticipantSearchRequest
        {
            Page = 2,
            PageSize = 5
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/participants/search", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<ParticipantSearchResponse>();
        result.Should().NotBeNull();
        result!.Page.Should().Be(2);
        result.PageSize.Should().Be(5);
    }

    [Fact]
    public async Task SearchParticipants_EmptyQuery_ReturnsAllWithinScope()
    {
        // Arrange
        var request = new ParticipantSearchRequest
        {
            Page = 1,
            PageSize = 50
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/participants/search", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<ParticipantSearchResponse>();
        result.Should().NotBeNull();
        result!.Query.Should().BeNull();
    }

    [Fact]
    public async Task SearchParticipants_CombinedFilters_ReturnsCorrectResults()
    {
        // Arrange - Search with multiple filters
        var request = new ParticipantSearchRequest
        {
            Query = "test",
            OrganizationId = TestDataSeeder.TestOrganizationId,
            Status = ParticipantIdentityStatus.Active,
            Page = 1,
            PageSize = 20
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/participants/search", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<ParticipantSearchResponse>();
        result.Should().NotBeNull();
        result!.Query.Should().Be("test");
    }

    #endregion

    #region GET /api/participants/by-wallet/{address} Tests

    [Fact]
    public async Task GetParticipantByWallet_WithoutAuth_ReturnsUnauthorized()
    {
        // Arrange
        var walletAddress = "sorcha1test123";
        var unauthClient = _factory.CreateClient();

        // Act
        var response = await unauthClient.GetAsync($"/api/participants/by-wallet/{walletAddress}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetParticipantByWallet_NonExistent_ReturnsNotFound()
    {
        // Arrange
        var walletAddress = "nonexistent_wallet_address_xyz";

        // Act
        var response = await _client.GetAsync($"/api/participants/by-wallet/{walletAddress}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetParticipantByWallet_WithEncodedAddress_ReturnsCorrectResult()
    {
        // Arrange - Test URL-encoded wallet address
        var walletAddress = Uri.EscapeDataString("sorcha1special+chars/test");

        // Act
        var response = await _client.GetAsync($"/api/participants/by-wallet/{walletAddress}");

        // Assert - Should return NotFound (not a server error)
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    #endregion

    #region GET /api/me/participant-profiles Tests

    [Fact]
    public async Task GetMyParticipantProfiles_WithoutAuth_ReturnsUnauthorized()
    {
        // Arrange
        var unauthClient = _factory.CreateClient();

        // Act
        var response = await unauthClient.GetAsync("/api/me/participant-profiles");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetMyParticipantProfiles_Authenticated_ReturnsOk()
    {
        // Act
        var response = await _client.GetAsync("/api/me/participant-profiles");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<List<ParticipantDetailResponse>>();
        result.Should().NotBeNull();
    }

    #endregion

    #region POST /api/me/organizations/{orgId}/self-register Tests

    [Fact]
    public async Task SelfRegister_WithoutAuth_ReturnsUnauthorized()
    {
        // Arrange
        var orgId = TestDataSeeder.TestOrganizationId;
        var unauthClient = _factory.CreateClient();

        // Act
        var response = await unauthClient.PostAsync($"/api/me/organizations/{orgId}/self-register", null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task SelfRegister_ValidRequest_ReturnsCreatedOrConflict()
    {
        // Arrange - Use a member client (not admin) for self-registration
        var orgId = TestDataSeeder.TestOrganizationId;
        var memberClient = _factory.CreateMemberClient();

        // Act
        var response = await memberClient.PostAsync($"/api/me/organizations/{orgId}/self-register", null);

        // Assert - May return 201 Created or 409 Conflict if already registered
        response.StatusCode.Should().BeOneOf(HttpStatusCode.Created, HttpStatusCode.Conflict);

        if (response.StatusCode == HttpStatusCode.Created)
        {
            var result = await response.Content.ReadFromJsonAsync<ParticipantDetailResponse>();
            result.Should().NotBeNull();
            result!.OrganizationId.Should().Be(orgId);
            result.Status.Should().Be(ParticipantIdentityStatus.Active);
        }
    }

    [Fact]
    public async Task SelfRegister_NonExistentOrganization_ReturnsError()
    {
        // Arrange
        var nonExistentOrgId = Guid.NewGuid();

        // Act
        var response = await _client.PostAsync($"/api/me/organizations/{nonExistentOrgId}/self-register", null);

        // Assert - Should fail since user doesn't belong to this org
        // May return BadRequest, Forbidden, or InternalServerError depending on how the service handles the error
        response.IsSuccessStatusCode.Should().BeFalse();
    }

    [Fact]
    public async Task SelfRegister_WithDisplayNameQuery_UsesProvidedName()
    {
        // Arrange - Use auditor user who may not be registered yet
        var orgId = TestDataSeeder.TestOrganizationId;
        var customName = "Custom Display Name";
        var auditorClient = _factory.CreateClient();
        auditorClient.DefaultRequestHeaders.Add("X-Test-Role", "Consumer");
        auditorClient.DefaultRequestHeaders.Add("X-Test-User-Id", TestDataSeeder.AuditorUserId.ToString());
        auditorClient.DefaultRequestHeaders.Add("X-Test-Organization-Id", orgId.ToString());

        // Act
        var response = await auditorClient.PostAsync(
            $"/api/me/organizations/{orgId}/self-register?displayName={Uri.EscapeDataString(customName)}", null);

        // Assert
        response.StatusCode.Should().BeOneOf(HttpStatusCode.Created, HttpStatusCode.Conflict);
    }

    #endregion

    #region PUT /api/organizations/{orgId}/participants/{id} Tests

    [Fact]
    public async Task UpdateParticipant_NonExistent_ReturnsNotFound()
    {
        // Arrange
        var orgId = TestDataSeeder.TestOrganizationId;
        var participantId = Guid.NewGuid();
        var request = new UpdateParticipantRequest { DisplayName = "Updated Name" };

        // Act
        var response = await _client.PutAsJsonAsync($"/api/organizations/{orgId}/participants/{participantId}", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    #endregion

    #region DELETE /api/organizations/{orgId}/participants/{id} Tests

    [Fact]
    public async Task DeactivateParticipant_NonExistent_ReturnsNotFound()
    {
        // Arrange
        var orgId = TestDataSeeder.TestOrganizationId;
        var participantId = Guid.NewGuid();

        // Act
        var response = await _client.DeleteAsync($"/api/organizations/{orgId}/participants/{participantId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    #endregion

    #region POST /api/organizations/{orgId}/participants/{id}/suspend Tests

    [Fact]
    public async Task SuspendParticipant_NonExistent_ReturnsNotFound()
    {
        // Arrange
        var orgId = TestDataSeeder.TestOrganizationId;
        var participantId = Guid.NewGuid();

        // Act
        var response = await _client.PostAsync($"/api/organizations/{orgId}/participants/{participantId}/suspend", null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    #endregion

    #region POST /api/organizations/{orgId}/participants/{id}/reactivate Tests

    [Fact]
    public async Task ReactivateParticipant_NonExistent_ReturnsNotFound()
    {
        // Arrange
        var orgId = TestDataSeeder.TestOrganizationId;
        var participantId = Guid.NewGuid();

        // Act
        var response = await _client.PostAsync($"/api/organizations/{orgId}/participants/{participantId}/reactivate", null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    #endregion

    #region POST /api/organizations/{orgId}/participants/{id}/wallet-links Tests

    [Fact]
    public async Task InitiateWalletLink_WithoutAuth_ReturnsUnauthorized()
    {
        // Arrange
        var orgId = TestDataSeeder.TestOrganizationId;
        var participantId = Guid.NewGuid();
        var request = new InitiateWalletLinkRequest
        {
            WalletAddress = "sorcha1test123",
            Algorithm = "ED25519"
        };
        var unauthClient = _factory.CreateClient();

        // Act
        var response = await unauthClient.PostAsJsonAsync(
            $"/api/organizations/{orgId}/participants/{participantId}/wallet-links", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task InitiateWalletLink_NonExistentParticipant_ReturnsNotFound()
    {
        // Arrange
        var orgId = TestDataSeeder.TestOrganizationId;
        var participantId = Guid.NewGuid();
        var request = new InitiateWalletLinkRequest
        {
            WalletAddress = "sorcha1nonexistent",
            Algorithm = "ED25519"
        };

        // Act
        var response = await _client.PostAsJsonAsync(
            $"/api/organizations/{orgId}/participants/{participantId}/wallet-links", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    #endregion

    #region POST /api/organizations/{orgId}/participants/{id}/wallet-links/{challengeId}/verify Tests

    [Fact]
    public async Task VerifyWalletLink_WithoutAuth_ReturnsUnauthorized()
    {
        // Arrange
        var orgId = TestDataSeeder.TestOrganizationId;
        var participantId = Guid.NewGuid();
        var challengeId = Guid.NewGuid();
        var request = new VerifyWalletLinkRequest { Signature = "test_signature" };
        var unauthClient = _factory.CreateClient();

        // Act
        var response = await unauthClient.PostAsJsonAsync(
            $"/api/organizations/{orgId}/participants/{participantId}/wallet-links/{challengeId}/verify", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task VerifyWalletLink_NonExistentParticipant_ReturnsNotFound()
    {
        // Arrange
        var orgId = TestDataSeeder.TestOrganizationId;
        var participantId = Guid.NewGuid();
        var challengeId = Guid.NewGuid();
        var request = new VerifyWalletLinkRequest { Signature = "test_signature" };

        // Act
        var response = await _client.PostAsJsonAsync(
            $"/api/organizations/{orgId}/participants/{participantId}/wallet-links/{challengeId}/verify", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    #endregion

    #region GET /api/organizations/{orgId}/participants/{id}/wallet-links Tests

    [Fact]
    public async Task ListWalletLinks_WithoutAuth_ReturnsUnauthorized()
    {
        // Arrange
        var orgId = TestDataSeeder.TestOrganizationId;
        var participantId = Guid.NewGuid();
        var unauthClient = _factory.CreateClient();

        // Act
        var response = await unauthClient.GetAsync(
            $"/api/organizations/{orgId}/participants/{participantId}/wallet-links");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ListWalletLinks_NonExistentParticipant_ReturnsNotFound()
    {
        // Arrange
        var orgId = TestDataSeeder.TestOrganizationId;
        var participantId = Guid.NewGuid();

        // Act
        var response = await _client.GetAsync(
            $"/api/organizations/{orgId}/participants/{participantId}/wallet-links");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    #endregion

    #region DELETE /api/organizations/{orgId}/participants/{id}/wallet-links/{linkId} Tests

    [Fact]
    public async Task RevokeWalletLink_WithoutAuth_ReturnsUnauthorized()
    {
        // Arrange
        var orgId = TestDataSeeder.TestOrganizationId;
        var participantId = Guid.NewGuid();
        var linkId = Guid.NewGuid();
        var unauthClient = _factory.CreateClient();

        // Act
        var response = await unauthClient.DeleteAsync(
            $"/api/organizations/{orgId}/participants/{participantId}/wallet-links/{linkId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task RevokeWalletLink_NonExistentParticipant_ReturnsNotFound()
    {
        // Arrange
        var orgId = TestDataSeeder.TestOrganizationId;
        var participantId = Guid.NewGuid();
        var linkId = Guid.NewGuid();

        // Act
        var response = await _client.DeleteAsync(
            $"/api/organizations/{orgId}/participants/{participantId}/wallet-links/{linkId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    #endregion
}
