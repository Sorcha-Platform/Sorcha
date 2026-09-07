// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Claims;

using FluentAssertions;

using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;

using Sorcha.Blueprint.Service.Extensions;

using Xunit;

namespace Sorcha.Blueprint.Service.Tests.Authorization;

/// <summary>
/// Policy-evaluation tests for <c>CanPublishBlueprints</c>, the policy guarding
/// <c>POST /api/blueprints/{id}/publish</c>. Built through
/// <see cref="AuthenticationExtensions.AddBlueprintAuthorization"/> and evaluated by the real
/// <see cref="IAuthorizationService"/> pipeline, so what is asserted is what the middleware does.
/// <para>
/// The regression these pin is <b>SystemAdmin</b>. The policy read
/// <c>IsInRole("Administrator")</c> alone, while the shared
/// <c>AuthorizationPolicies.RequireAdministrator</c> is
/// <c>RequireRole("SystemAdmin", "Administrator")</c> and the Register Service's
/// <c>CanManageRegisters</c> accepts either — so a SystemAdmin could create the register but never
/// publish to it. The failure did not stop at a 403: a SystemAdmin's refusal here is
/// DETERMINISTIC, and the MCP server's service-availability tracker trips after three consecutive
/// failures, so three attempts silently disabled every Blueprint MCP tool behind a false
/// "service is currently unavailable".
/// </para>
/// </summary>
public class CanPublishBlueprintsPolicyTests
{
    private const string PolicyName = "CanPublishBlueprints";

    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddBlueprintAuthorization();
        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Builds a principal whose role claims use <see cref="ClaimTypes.Role"/> — the same claim type
    /// the Tenant Service's <c>TokenService</c> emits (<c>new Claim(ClaimTypes.Role, role.ToString())</c>),
    /// so <c>IsInRole</c> resolves here exactly as it does against a real token.
    /// </summary>
    private static ClaimsPrincipal AuthenticatedUser(params Claim[] claims) =>
        new(new ClaimsIdentity(claims, authenticationType: "TestScheme", nameType: ClaimTypes.Name, roleType: ClaimTypes.Role));

    private static async Task<bool> SucceedsAsync(ServiceProvider provider, ClaimsPrincipal user)
    {
        var authz = provider.GetRequiredService<IAuthorizationService>();
        return (await authz.AuthorizeAsync(user, PolicyName)).Succeeded;
    }

    [Fact]
    public async Task Administrator_Succeeds()
    {
        using var provider = BuildProvider();
        var user = AuthenticatedUser(new Claim(ClaimTypes.Role, "Administrator"));

        (await SucceedsAsync(provider, user)).Should().BeTrue(
            "an organisation Administrator is the ordinary publisher");
    }

    [Fact]
    public async Task SystemAdmin_Succeeds()
    {
        using var provider = BuildProvider();
        var user = AuthenticatedUser(new Claim(ClaimTypes.Role, "SystemAdmin"));

        (await SucceedsAsync(provider, user)).Should().BeTrue(
            "SystemAdmin already clears the shared RequireAdministrator policy and the Register "
            + "Service's CanManageRegisters, so excluding it here made the register creatable but "
            + "not publishable to");
    }

    [Fact]
    public async Task CanPublishBlueprintClaim_Succeeds_WithoutAnyRole()
    {
        // The claim branch is the policy's other half. The Tenant Service does not currently emit
        // it, but the branch is live and a deployment that starts emitting it must keep working.
        using var provider = BuildProvider();
        var user = AuthenticatedUser(new Claim("can_publish_blueprint", "true"));

        (await SucceedsAsync(provider, user)).Should().BeTrue();
    }

    [Theory]
    [InlineData("Designer")]
    [InlineData("Auditor")]
    [InlineData("Consumer")]
    public async Task NonAdminRole_Fails(string role)
    {
        // The widening must not become "any authenticated platform user". A Designer in
        // particular is the role the MCP tool surface would otherwise have been entitled on.
        using var provider = BuildProvider();
        var user = AuthenticatedUser(new Claim(ClaimTypes.Role, role));

        (await SucceedsAsync(provider, user)).Should().BeFalse(
            $"'{role}' confers no publish authority");
    }

    [Fact]
    public async Task CanPublishBlueprintClaimSetToFalse_Fails()
    {
        using var provider = BuildProvider();
        var user = AuthenticatedUser(new Claim("can_publish_blueprint", "false"));

        (await SucceedsAsync(provider, user)).Should().BeFalse(
            "the claim is matched on its value, not merely its presence");
    }

    [Fact]
    public async Task AuthenticatedWithNoRolesOrClaim_Fails()
    {
        using var provider = BuildProvider();
        var user = AuthenticatedUser(new Claim(ClaimTypes.Name, "someone"));

        (await SucceedsAsync(provider, user)).Should().BeFalse();
    }
}
