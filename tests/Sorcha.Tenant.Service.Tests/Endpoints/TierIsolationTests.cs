// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using FluentAssertions;
using Sorcha.Tenant.Service.Tests.Infrastructure;
using Xunit;

namespace Sorcha.Tenant.Service.Tests.Endpoints;

/// <summary>
/// Spec 136 / US1 (SC-002): trust-tier isolation is enforced at the audience layer, independently
/// of and on top of role checks. A consumer-tier token is refused at a platform surface even when
/// it carries an admin role; a platform-tier token passes the tier gate. The Tenant test host runs
/// under installation <c>test</c>, so its audiences are <c>test:consumer</c> / <c>test:platform</c>.
/// </summary>
public class TierIsolationTests
    : IClassFixture<TenantServiceWebApplicationFactory>, IAsyncLifetime
{
    private readonly TenantServiceWebApplicationFactory _factory;

    public TierIsolationTests(TenantServiceWebApplicationFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync() => await _factory.SeedTestDataAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    // The admin dashboard group is platform-tier (RequireAdministrator + RequirePlatformAudience).
    private string DashboardUrl => $"/api/organizations/{TestDataSeeder.TestOrganizationId}/dashboard/";

    [Fact]
    public async Task PlatformEndpoint_ConsumerAudienceToken_IsForbidden_EvenWithAdminRole()
    {
        var client = _factory.CreateAdminClient(); // Administrator role + org context
        client.DefaultRequestHeaders.Add("X-Test-Audience", "test:consumer"); // consumer-tier only

        var response = await client.GetAsync(DashboardUrl);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "a consumer-tier token must be refused at a platform surface even with an admin role (SC-002)");
    }

    [Fact]
    public async Task PlatformEndpoint_PlatformAudienceToken_NotForbiddenByTier()
    {
        // CreateAdminClient injects all four tier audiences by default (includes test:platform).
        var client = _factory.CreateAdminClient();

        var response = await client.GetAsync(DashboardUrl);

        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden,
            "a platform-tier admin token passes both the tier gate and the role gate");
    }

    [Fact]
    public async Task PlatformEndpoint_ServiceAudienceToken_IsForbidden()
    {
        var client = _factory.CreateAdminClient();
        client.DefaultRequestHeaders.Add("X-Test-Audience", "test:service"); // service-tier only

        var response = await client.GetAsync(DashboardUrl);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "a service-tier token must be refused at a human platform surface");
    }
}
