// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Net.Http.Json;

using FluentAssertions;

using Sorcha.Tenant.Service.Tests.Infrastructure;

namespace Sorcha.Tenant.Service.Tests.Endpoints;

/// <summary>
/// B2+ (catch-up security review 2026-07-29, generalised) — every route group under
/// <c>/api/organizations/{organizationId}/...</c> must bind the caller to the organisation in the
/// route.
///
/// <para>The groups were gated on <c>RequireAdministrator</c> + <c>RequirePlatformAudience</c>,
/// which is a ROLE and TIER check only: <c>RequireAdministrator</c> is literally
/// <c>RequireRole("SystemAdmin", "Administrator")</c> and never looks at <c>org_id</c>. No handler
/// compared the caller's organisation to the route either. So an Administrator of org A could
/// operate on org B — read its audit log, change its custom domain and domain restrictions, read
/// its dashboard, and manage its invitations (a resend ROTATES the invitation token and emails the
/// invitee, so it is a write with an outbound side effect).</para>
///
/// <para>That the codebase knows how to do this is not in doubt: <c>RequireSystemAdmin</c> in the
/// same file asserts <c>org_id == 00000000-0000-0000-0000-000000000001</c>. Its absence on the
/// org-scoped groups is a gap, not a decision.</para>
///
/// <para>Cross-org access by a genuine platform SystemAdmin IS legitimate and must keep working —
/// verified live on n1, where the seeded <c>admin@sorcha.local</c> (roles include
/// <c>SystemAdmin</c>, org <c>…0001</c>) reads other organisations deliberately. These tests pin
/// both halves: a plain Administrator is confined to their own org; a SystemAdmin is not.</para>
/// </summary>
public class OrgScopedCallerBindingTests : IClassFixture<TenantServiceWebApplicationFactory>
{
    private readonly TenantServiceWebApplicationFactory _factory;

    /// <summary>An organisation the caller below is NOT a member of.</summary>
    private static readonly Guid ForeignOrgId = TestDataSeeder.TestOrganizationId;

    /// <summary>Some other organisation — the caller's own.</summary>
    private static readonly Guid CallerOwnOrgId = new("dddddddd-dddd-dddd-dddd-dddddddddddd");

    public OrgScopedCallerBindingTests(TenantServiceWebApplicationFactory factory)
    {
        _factory = factory;
    }

    /// <summary>
    /// A plain org Administrator — role <c>Administrator</c> only, deliberately NOT
    /// <c>SystemAdmin</c> — whose <c>org_id</c> is <paramref name="orgId"/>.
    /// </summary>
    private HttpClient CreatePlainOrgAdminClient(Guid orgId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Role", "Administrator");
        client.DefaultRequestHeaders.Add("X-Test-User-Id", Guid.NewGuid().ToString());
        client.DefaultRequestHeaders.Add("X-Test-Organization-Id", orgId.ToString());
        return client;
    }

    /// <summary>A platform SystemAdmin in the system-admin org — legitimately cross-org.</summary>
    private HttpClient CreateSystemAdminClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Role", "SystemAdmin");
        client.DefaultRequestHeaders.Add("X-Test-User-Id", TestDataSeeder.AdminUserId.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Organization-Id", "00000000-0000-0000-0000-000000000001");
        return client;
    }

    public static TheoryData<string, string> ForeignOrgRoutes() => new()
    {
        { "GET", "invitations" },
        { "GET", "audit" },
        { "GET", "domain-restrictions" },
        { "GET", "dashboard" },
    };

    [Theory]
    [MemberData(nameof(ForeignOrgRoutes))]
    public async Task PlainOrgAdmin_ReadingAnotherOrg_IsForbidden(string method, string segment)
    {
        var client = CreatePlainOrgAdminClient(CallerOwnOrgId);

        var response = await client.SendAsync(new HttpRequestMessage(
            new HttpMethod(method), $"/api/organizations/{ForeignOrgId}/{segment}"));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            $"an Administrator of one organisation must not reach another organisation's {segment}");
    }

    [Fact]
    public async Task PlainOrgAdmin_ResendingAnotherOrgsInvitation_IsForbidden()
    {
        // The worst of the set: a resend ROTATES the invitation's token and emails the invitee, so
        // a cross-org caller can invalidate a link already in flight and trigger outbound mail for
        // an organisation they have nothing to do with.
        var client = CreatePlainOrgAdminClient(CallerOwnOrgId);

        var response = await client.PostAsync(
            $"/api/organizations/{ForeignOrgId}/invitations/{Guid.NewGuid()}/resend", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "rotating another organisation's invitation token must be refused at the gate, "
            + "before the invitation is even looked up");
    }

    [Theory]
    [MemberData(nameof(ForeignOrgRoutes))]
    public async Task PlainOrgAdmin_ReadingTheirOwnOrg_IsNotForbidden(string method, string segment)
    {
        // The gate must not confine admins out of their OWN organisation.
        var client = CreatePlainOrgAdminClient(ForeignOrgId);

        var response = await client.SendAsync(new HttpRequestMessage(
            new HttpMethod(method), $"/api/organizations/{ForeignOrgId}/{segment}"));

        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden,
            $"an Administrator must still administer their own organisation's {segment}");
    }

    [Fact]
    public async Task PlainOrgAdmin_ListingAnotherOrgsUsers_IsForbidden()
    {
        var client = CreatePlainOrgAdminClient(CallerOwnOrgId);

        var response = await client.GetAsync($"/api/organizations/{ForeignOrgId}/users");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "another organisation's membership is not the caller's to enumerate");
    }

    [Fact]
    public async Task PlainOrgAdmin_AddingAUserToAnotherOrg_IsForbidden()
    {
        // The most serious of the set: without the org bind, an administrator of org A could add a
        // principal to org B — including themselves, with a role of their choosing.
        var client = CreatePlainOrgAdminClient(CallerOwnOrgId);

        // A WELL-FORMED body, deliberately: minimal-API body binding runs before endpoint filters,
        // so a malformed payload would be rejected with 400 and never reach the ownership gate —
        // which would make this test pass for the wrong reason.
        var response = await client.PostAsJsonAsync(
            $"/api/organizations/{ForeignOrgId}/users",
            new
            {
                email = "attacker@example.test",
                displayName = "Attacker",
                externalIdpSubject = "idp|attacker",
                roles = new[] { "Administrator" }
            });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "granting oneself membership of another organisation must be refused at the gate");
    }

    [Fact]
    public async Task PlainOrgAdmin_ChangingARoleInAnotherOrg_IsForbidden()
    {
        var client = CreatePlainOrgAdminClient(CallerOwnOrgId);

        var response = await client.PutAsJsonAsync(
            $"/api/organizations/{ForeignOrgId}/users/{Guid.NewGuid()}/role",
            new { role = "Administrator" });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "roles in another organisation are not the caller's to change");
    }

    [Fact]
    public async Task PlainOrgAdmin_SuspendingAUserInAnotherOrg_IsForbidden()
    {
        var client = CreatePlainOrgAdminClient(CallerOwnOrgId);

        var response = await client.PostAsync(
            $"/api/organizations/{ForeignOrgId}/users/{Guid.NewGuid()}/suspend", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "denial of service against another organisation's users must be refused");
    }

    // ── Wave 2: the remaining org-scoped groups ─────────────────────────────────────────────
    // Left out of the first pass deliberately, each needing its own judgement. All five below
    // turned out to need the bind; `PlatformOrgEndpoints` turned out NOT to (see below).

    public static TheoryData<string, string> ForeignOrgRoutesWave2() => new()
    {
        // IDP configuration is the most serious of the entire set: it decides HOW USERS
        // AUTHENTICATE into an organisation. An administrator of org A repointing org B's
        // identity provider at an IDP they control is account takeover of org B.
        { "GET", "idp" },
        { "GET", "settings" },
        { "GET", "participants" },
        { "GET", "register-invitations" },
        { "GET", "register-subscriptions" },
    };

    [Theory]
    [MemberData(nameof(ForeignOrgRoutesWave2))]
    public async Task PlainOrgAdmin_ReadingAnotherOrgWave2_IsForbidden(string method, string segment)
    {
        var client = CreatePlainOrgAdminClient(CallerOwnOrgId);

        var response = await client.SendAsync(new HttpRequestMessage(
            new HttpMethod(method), $"/api/organizations/{ForeignOrgId}/{segment}"));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            $"an Administrator of one organisation must not reach another organisation's {segment}");
    }

    [Theory]
    [MemberData(nameof(ForeignOrgRoutesWave2))]
    public async Task PlainOrgAdmin_ReadingTheirOwnOrgWave2_IsNotForbidden(string method, string segment)
    {
        var client = CreatePlainOrgAdminClient(ForeignOrgId);

        var response = await client.SendAsync(new HttpRequestMessage(
            new HttpMethod(method), $"/api/organizations/{ForeignOrgId}/{segment}"));

        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden,
            $"an Administrator must still administer their own organisation's {segment}");
    }

    [Fact]
    public async Task PlainOrgAdmin_RewritingAnotherOrgsIdpConfiguration_IsForbidden()
    {
        // Account takeover, stated plainly: repointing org B's identity provider means every
        // subsequent org B login is authenticated by an IDP the attacker controls.
        var client = CreatePlainOrgAdminClient(CallerOwnOrgId);

        // Every `required` member of IdpConfigurationRequest is present deliberately: minimal-API
        // argument binding runs BEFORE endpoint filters, so a body missing a required member 400s
        // during deserialisation and never reaches the gate — passing for the wrong reason.
        var response = await client.PutAsJsonAsync(
            $"/api/organizations/{ForeignOrgId}/idp",
            new
            {
                providerPreset = "GenericOidc",
                issuerUrl = "https://idp.attacker.test",
                clientId = "attacker",
                clientSecret = "attacker-secret",
                displayName = "Attacker IDP"
            });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "rewriting another organisation's identity provider is account takeover of that org");
    }

    [Fact]
    public async Task AnyAuthenticatedNonAdmin_ListingAnotherOrgsRegisterSubscriptions_IsForbidden()
    {
        // This route was gated on plain `.RequireAuthorization()` — not even a role check — so ANY
        // signed-in principal, including a citizen, could enumerate any organisation's register
        // subscriptions.
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Role", "Consumer");
        client.DefaultRequestHeaders.Add("X-Test-User-Id", Guid.NewGuid().ToString());
        client.DefaultRequestHeaders.Add("X-Test-Organization-Id", CallerOwnOrgId.ToString());

        var response = await client.GetAsync(
            $"/api/organizations/{ForeignOrgId}/register-subscriptions");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "which registers an organisation subscribes to is not public to every signed-in user");
    }

    [Fact]
    public async Task PlatformOrgEndpoints_AreNotGated_TheyAreSystemAdminOrgScopedByPolicy()
    {
        // Deliberate NON-change, pinned so nobody "completes the sweep" by gating it later.
        // /api/platform/organizations is cross-org BY DESIGN (platform topology administration) and
        // is already correctly scoped: RequireSystemAdmin AND RequirePlatformAuditor both assert
        // membership of the system-admin org, not merely a role. Adding a caller-org bind keyed on
        // the ROUTE's {orgId} would refuse the platform admin for every organisation but their own —
        // breaking a real capability while appearing to harden it.
        var client = CreateSystemAdminClient();

        var response = await client.GetAsync(
            $"/api/platform/organizations/{ForeignOrgId}/users");

        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden,
            "platform topology administration is intentionally cross-organisation");
    }

    [Theory]
    [MemberData(nameof(ForeignOrgRoutes))]
    public async Task PlatformSystemAdmin_ReadingAnyOrg_IsNotForbidden(string method, string segment)
    {
        // Platform-wide administration is a real, intended capability — proven live on n1 before
        // this fix was written. Confining SystemAdmin would be a regression, not a hardening.
        var client = CreateSystemAdminClient();

        var response = await client.SendAsync(new HttpRequestMessage(
            new HttpMethod(method), $"/api/organizations/{ForeignOrgId}/{segment}"));

        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden,
            $"a platform SystemAdmin must retain cross-organisation access to {segment}");
    }
}
