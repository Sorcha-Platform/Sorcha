// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Claims;

using FluentAssertions;

using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;

using Sorcha.ServiceClients.Auth;
using Sorcha.Tenant.Service.Extensions;

using Xunit;

namespace Sorcha.Tenant.Service.Tests.Authorization;

/// <summary>
/// Policy-evaluation tests for the Tenant Service <c>RequireSystemAdmin</c> policy (Feature 147 / review LOW).
/// <see cref="AuthenticationExtensions.AddTenantAuthorization"/> previously re-registered
/// <c>RequireSystemAdmin</c> as role-only, dropping the org-scope from the shared definition (last-write-wins),
/// so a SystemAdmin in <em>any</em> org cleared platform-administration routes. With the duplicate removed,
/// the shared definition (system-admin org membership AND SystemAdmin role) stands.
/// </summary>
public class TenantSystemAdminPolicyTests
{
    private const string PolicyName = "RequireSystemAdmin";
    private const string SystemAdminOrgId = "00000000-0000-0000-0000-000000000001";

    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTenantAuthorization();
        return services.BuildServiceProvider();
    }

    private static ClaimsPrincipal AuthenticatedUser(params Claim[] claims) =>
        new(new ClaimsIdentity(claims, authenticationType: "TestScheme"));

    private static async Task<bool> SucceedsAsync(ServiceProvider provider, ClaimsPrincipal user)
    {
        var authz = provider.GetRequiredService<IAuthorizationService>();
        return (await authz.AuthorizeAsync(user, PolicyName)).Succeeded;
    }

    [Fact]
    public async Task SystemAdminInSystemAdminOrg_Succeeds()
    {
        using var provider = BuildProvider();
        var user = AuthenticatedUser(
            new Claim(TokenClaimConstants.OrgId, SystemAdminOrgId),
            new Claim(ClaimTypes.Role, "SystemAdmin"));

        (await SucceedsAsync(provider, user)).Should().BeTrue(
            "a SystemAdmin in the system administration org may perform platform administration");
    }

    [Fact]
    public async Task SystemAdminInNonSystemOrg_Fails()
    {
        using var provider = BuildProvider();
        var user = AuthenticatedUser(
            new Claim(TokenClaimConstants.OrgId, "11111111-1111-1111-1111-111111111111"),
            new Claim(ClaimTypes.Role, "SystemAdmin"));

        (await SucceedsAsync(provider, user)).Should().BeFalse(
            "the SystemAdmin role outside the system administration org must NOT clear platform administration (review LOW)");
    }

    [Fact]
    public async Task NonSystemAdminInSystemAdminOrg_Fails()
    {
        using var provider = BuildProvider();
        var user = AuthenticatedUser(
            new Claim(TokenClaimConstants.OrgId, SystemAdminOrgId),
            new Claim(ClaimTypes.Role, "Administrator"));

        (await SucceedsAsync(provider, user)).Should().BeFalse(
            "membership of the system administration org without the SystemAdmin role is insufficient");
    }
}
