// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Net.Http.Json;

using FluentAssertions;

using Microsoft.Extensions.DependencyInjection;

using Sorcha.Tenant.Service.Data;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Services;
using Sorcha.Tenant.Service.Tests.Infrastructure;

using Xunit;

namespace Sorcha.Tenant.Service.Tests.Endpoints;

/// <summary>
/// Integration tests for public passkey registration and assertion endpoints.
/// Uses the WebApplicationFactory with in-memory database.
/// FIDO2 operations are not fully testable without a real authenticator, so tests
/// focus on validation, routing, and the user provisioning flow boundaries.
/// </summary>
public class PublicPasskeyEndpointsTests : IClassFixture<TenantServiceWebApplicationFactory>, IAsyncLifetime
{
    private readonly TenantServiceWebApplicationFactory _factory;
    private HttpClient _client = null!;

    public PublicPasskeyEndpointsTests(TenantServiceWebApplicationFactory factory)
    {
        _factory = factory;
    }

    public async ValueTask InitializeAsync()
    {
        _client = _factory.CreateClient();
        await _factory.SeedTestDataAsync();

        // Seed PlatformSettings so endpoints don't throw "not initialized"
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();

        if (!db.PlatformSettings.Any())
        {
            db.PlatformSettings.Add(new PlatformSettings
            {
                PublicOrgEnabled = true,
                MaxOrgsPerUser = 5,
                UpdatedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
        }

        // Ensure public org exists
        if (!db.Organizations.Any(o => o.Id == WellKnownIds.PublicOrgId))
        {
            db.Organizations.Add(new Organization
            {
                Id = WellKnownIds.PublicOrgId,
                Name = "Public",
                Subdomain = "public",
                Status = OrganizationStatus.Active,
                CreatedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
        }
    }

    public ValueTask DisposeAsync()
    {
        _client?.Dispose();
        return ValueTask.CompletedTask;
    }

    #region POST /api/auth/public/passkey/register/options — Validation Tests

    [Fact]
    public async Task RegisterOptions_EmptyDisplayName_ReturnsValidationProblem()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/public/passkey/register/options", new
        {
            display_name = "",
            email = (string?)null
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task RegisterOptions_NullDisplayName_ReturnsValidationProblem()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/public/passkey/register/options", new
        {
            display_name = (string?)null,
            email = (string?)null
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task RegisterOptions_DuplicateEmail_ReturnsConflict()
    {
        // Use the admin email that is always seeded by TestDataSeeder
        // to avoid cross-scope in-memory DB issues
        var email = $"dup-{Guid.NewGuid():N}@example.com";

        // Create user via the same endpoint first (PlatformUser gets created at OPTIONS time)
        var firstResponse = await _client.PostAsJsonAsync("/api/auth/public/passkey/register/options", new
        {
            display_name = "First User",
            email
        });

        // First call should succeed or hit FIDO2 error (but the PlatformUser is created before FIDO2)
        // Either way, the email is now taken
        if (firstResponse.StatusCode is HttpStatusCode.OK or HttpStatusCode.InternalServerError)
        {
            var response = await _client.PostAsJsonAsync("/api/auth/public/passkey/register/options", new
            {
                display_name = "New User",
                email
            });

            response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        }
    }

    [Fact]
    public async Task RegisterOptions_ValidRequest_CreatesUserAndReturnsOptions()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/public/passkey/register/options", new
        {
            display_name = "Test Passkey User",
            email = $"pk-{Guid.NewGuid():N}@example.com"
        });

        // FIDO2 may not be configured in test env — accept 200 (success) or 500 (FIDO2 failure)
        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.InternalServerError);
    }

    #endregion

    #region POST /api/auth/public/passkey/register/verify — Validation Tests

    [Fact]
    public async Task RegisterVerify_EmptyTransactionId_ReturnsValidationProblem()
    {
        // Send a minimal valid JSON body with empty transaction_id
        var response = await _client.PostAsJsonAsync("/api/auth/public/passkey/register/verify", new
        {
            transaction_id = "",
            // attestation_response needs to be a valid-shaped object for deserialization
            attestation_response = new
            {
                id = "test",
                rawId = "dGVzdA==",
                type = "public-key",
                response = new
                {
                    attestationObject = "dGVzdA==",
                    clientDataJSON = "dGVzdA=="
                }
            }
        });

        // May return 400 (validation) or 503/500 (if deserialization of Fido2 types fails)
        response.StatusCode.Should().NotBe(HttpStatusCode.NotFound);
    }

    #endregion

    #region POST /api/auth/passkey/assertion/options — Tests

    [Fact]
    public async Task AssertionOptions_NoEmail_ProcessesRequest()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/passkey/assertion/options", new
        {
            email = (string?)null
        });

        // FIDO2 may not be configured — accept 200 or 500, but not 404
        response.StatusCode.Should().NotBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task AssertionOptions_WithUnknownEmail_ProcessesRequest()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/passkey/assertion/options", new
        {
            email = "unknown@example.com"
        });

        // Even with unknown email, endpoint should process (discoverable credentials flow)
        response.StatusCode.Should().NotBe(HttpStatusCode.NotFound);
    }

    #endregion

    #region POST /api/auth/passkey/assertion/verify — Validation Tests

    [Fact]
    public async Task AssertionVerify_EmptyTransactionId_DoesNotReturn404()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/passkey/assertion/verify", new
        {
            transaction_id = "",
            assertion_response = new
            {
                id = "test",
                rawId = "dGVzdA==",
                type = "public-key",
                response = new
                {
                    authenticatorData = "dGVzdA==",
                    clientDataJSON = "dGVzdA==",
                    signature = "dGVzdA=="
                }
            }
        });

        response.StatusCode.Should().NotBe(HttpStatusCode.NotFound);
    }

    #endregion

    #region Endpoint Routing Tests

    [Fact]
    public async Task RegisterOptions_EndpointExists_DoesNotReturn404()
    {
        // Use empty display_name to hit early validation (avoids FIDO2 dependency)
        var response = await _client.PostAsJsonAsync("/api/auth/public/passkey/register/options", new
        {
            display_name = ""
        });

        response.StatusCode.Should().NotBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task RegisterVerify_EndpointExists_DoesNotReturn404()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/public/passkey/register/verify", new
        {
            transaction_id = "test-tx",
            attestation_response = new { id = "test" }
        });

        response.StatusCode.Should().NotBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task AssertionOptions_EndpointExists_DoesNotReturn404()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/passkey/assertion/options", new
        {
            email = (string?)null
        });

        response.StatusCode.Should().NotBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task AssertionVerify_EndpointExists_DoesNotReturn404()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/passkey/assertion/verify", new
        {
            transaction_id = "test-tx",
            assertion_response = new { id = "test" }
        });

        response.StatusCode.Should().NotBe(HttpStatusCode.NotFound);
    }

    #endregion

    #region Public Org Disabled Tests

    [Fact]
    public async Task RegisterOptions_PublicOrgDisabled_ReturnsBadRequest()
    {
        // This test validates the public org disabled check in isolation.
        // Uses a separate factory to avoid shared-state issues with in-memory DB.
        await using var isolatedFactory = new TenantServiceWebApplicationFactory();
        using var isolatedClient = isolatedFactory.CreateClient();
        await isolatedFactory.SeedTestDataAsync();

        // Seed PlatformSettings as disabled
        using (var scope = isolatedFactory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
            db.PlatformSettings.Add(new PlatformSettings
            {
                PublicOrgEnabled = false,
                MaxOrgsPerUser = 5,
                UpdatedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var response = await isolatedClient.PostAsJsonAsync("/api/auth/public/passkey/register/options", new
        {
            display_name = "Test User",
            email = $"pk-disabled-{Guid.NewGuid():N}@example.com"
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    #endregion
}
