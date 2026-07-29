// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Net.Http.Json;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Sorcha.ServiceClients.OrgDidDocument;
using Sorcha.Tenant.Service.Data;
using Sorcha.Tenant.Service.Tests.Infrastructure;

namespace Sorcha.Tenant.Service.Tests.Endpoints;

/// <summary>
/// C1 (catch-up security review 2026-07-29) — <c>POST /orgs/{orgId}/did-document/regenerate</c>
/// publishes an organisation's DID document: the public key material every verifier trusts when
/// checking that org's issued credentials. It shipped with NO authorization attribute at all, and
/// the Tenant Service configures no fallback policy, so it was reachable anonymously on the
/// published Tenant port. An attacker who posted a victim's orgId + wallet address (both public)
/// with their own JWK would have had their key served as the victim's issuer key — i.e. their
/// self-signed credentials would verify as the victim organisation's.
///
/// It is an internal Wallet→Tenant call, so the tier gate is <c>RequireService</c>: a human token
/// of any role must be refused, not just anonymous callers.
/// </summary>
public class OrgDidDocumentEndpointsAuthorizationTests : IClassFixture<TenantServiceWebApplicationFactory>
{
    private readonly TenantServiceWebApplicationFactory _factory;

    public OrgDidDocumentEndpointsAuthorizationTests(TenantServiceWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private static string Route(Guid orgId) => $"/orgs/{orgId}/did-document/regenerate";

    private static OrgDidRegenerateRequest Snapshot(Guid orgId, string walletAddress = "ws1attackercontrolled") =>
        new(orgId, "key-derivation", walletAddress,
            [new OrgDidActiveKey(1, "ED25519", """{"kty":"OKP","crv":"Ed25519","x":"attacker-key"}""", "thumb-1")]);

    [Fact]
    public async Task Regenerate_Anonymously_IsRefused()
    {
        var client = _factory.CreateUnauthenticatedClient();

        var response = await client.PostAsJsonAsync(
            Route(TestDataSeeder.TestOrganizationId),
            Snapshot(TestDataSeeder.TestOrganizationId));

        response.StatusCode.Should().BeOneOf(
            [HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden],
            "publishing an org's issuer keys must never be an anonymous operation");
    }

    [Fact]
    public async Task Regenerate_WithHumanToken_IsRefused()
    {
        // A signed-in member carries org_id and a role but is NOT a service principal.
        // The tier boundary is the point: this is an internal S2S call path only.
        var client = _factory.CreateMemberClient();

        var response = await client.PostAsJsonAsync(
            Route(TestDataSeeder.TestOrganizationId),
            Snapshot(TestDataSeeder.TestOrganizationId));

        response.StatusCode.Should().BeOneOf(
            [HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden],
            "a human token must not be able to republish an organisation's issuer keys");
    }

    [Fact]
    public async Task Regenerate_WithAdminToken_IsRefused()
    {
        var client = _factory.CreateAdminClient();

        var response = await client.PostAsJsonAsync(
            Route(TestDataSeeder.TestOrganizationId),
            Snapshot(TestDataSeeder.TestOrganizationId));

        response.StatusCode.Should().BeOneOf(
            [HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden],
            "even an administrator publishes DID documents only via the Wallet key-event path");
    }

    [Fact]
    public async Task Regenerate_WithServiceToken_IsAllowedThrough()
    {
        var client = CreateServicePrincipalClient();

        var response = await client.PostAsJsonAsync(
            Route(TestDataSeeder.TestOrganizationId),
            Snapshot(TestDataSeeder.TestOrganizationId));

        // The legitimate caller must still get through the tier gate. Whatever the
        // outcome of the request body (see the wallet-address test below), it must not
        // be an authorization failure.
        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Regenerate_WalletAddressNotTheOrgs_IsRejected()
    {
        // Defence in depth behind the tier gate: the canonical DID is built verbatim as
        // did:sorcha:org:{WalletAddress} from the request body, so a caller that can reach this
        // endpoint must still not be able to name an arbitrary wallet address as an org's
        // identity. The org seeded here has a known canonical wallet; a different one must fail.
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var org = await db.Organizations.FirstAsync(o => o.Id == TestDataSeeder.TestOrganizationId);
        org.WalletAddress = "ws1theorgsrealwallet";
        await db.SaveChangesAsync();

        var client = CreateServicePrincipalClient();

        var response = await client.PostAsJsonAsync(
            Route(TestDataSeeder.TestOrganizationId),
            Snapshot(TestDataSeeder.TestOrganizationId, walletAddress: "ws1attackercontrolled"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "a wallet address that is not the organisation's own must never become its DID");
    }

    [Fact]
    public async Task Regenerate_WalletAddressMatchesTheOrgs_IsAccepted()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var org = await db.Organizations.FirstAsync(o => o.Id == TestDataSeeder.TestOrganizationId);
        org.WalletAddress = "ws1theorgsrealwallet";
        await db.SaveChangesAsync();

        var client = CreateServicePrincipalClient();

        var response = await client.PostAsJsonAsync(
            Route(TestDataSeeder.TestOrganizationId),
            Snapshot(TestDataSeeder.TestOrganizationId, walletAddress: "ws1theorgsrealwallet"));

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "the legitimate Wallet-side key-event path must still publish");
    }

    private HttpClient CreateServicePrincipalClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-User-Id", Guid.NewGuid().ToString());
        client.DefaultRequestHeaders.Add("X-Test-Token-Type", "service");
        return client;
    }
}
