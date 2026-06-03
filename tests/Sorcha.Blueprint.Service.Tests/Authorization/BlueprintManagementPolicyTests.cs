// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Claims;

using FluentAssertions;

using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;

using Sorcha.Blueprint.Service.Extensions;
using Sorcha.ServiceClients.Auth;

using Xunit;

namespace Sorcha.Blueprint.Service.Tests.Authorization;

/// <summary>
/// Policy-evaluation tests for the Blueprint Service <c>CanManageBlueprints</c> policy (Feature 147 / review H2).
/// Mirrors the shared <c>AuthorizationPolicyExtensionsTests</c> harness: build a provider via
/// <see cref="AuthenticationExtensions.AddBlueprintAuthorization"/> and evaluate through the real
/// <see cref="IAuthorizationService"/> pipeline. Default installation is "sorcha", so tier audiences are
/// <c>sorcha:{consumer|platform|service}</c>. The key regression: a consumer token carrying an
/// <c>org_id</c> (Feature 136) must be refused.
/// </summary>
public class BlueprintManagementPolicyTests
{
    private const string PolicyName = "CanManageBlueprints";

    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddBlueprintAuthorization();
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
    public async Task ConsumerWithOrgId_Fails()
    {
        using var provider = BuildProvider();
        var user = AuthenticatedUser(
            new Claim(TokenClaimConstants.OrgId, "org-1"),
            new Claim("aud", "sorcha:consumer"));

        (await SucceedsAsync(provider, user)).Should().BeFalse(
            "a consumer/citizen token carrying org_id must not reach blueprint authoring (review H2)");
    }

    [Fact]
    public async Task PlatformWithOrgId_Succeeds()
    {
        using var provider = BuildProvider();
        var user = AuthenticatedUser(
            new Claim(TokenClaimConstants.OrgId, "org-1"),
            new Claim("aud", "sorcha:platform"));

        (await SucceedsAsync(provider, user)).Should().BeTrue(
            "a platform-tier org member may author blueprints");
    }

    [Fact]
    public async Task ServiceToken_Succeeds()
    {
        using var provider = BuildProvider();
        var user = AuthenticatedUser(
            new Claim(TokenClaimConstants.TokenType, TokenClaimConstants.TokenTypeService),
            new Claim("aud", "sorcha:service"));

        (await SucceedsAsync(provider, user)).Should().BeTrue(
            "a service-tier caller may author blueprints (service-to-service)");
    }

    [Fact]
    public async Task PlatformWithoutOrgId_Fails()
    {
        using var provider = BuildProvider();
        var user = AuthenticatedUser(new Claim("aud", "sorcha:platform"));

        (await SucceedsAsync(provider, user)).Should().BeFalse(
            "a platform-tier token without an org_id is not an org member for authoring purposes");
    }

    [Fact]
    public async Task OrgIdWithoutTierAudience_Fails()
    {
        using var provider = BuildProvider();
        var user = AuthenticatedUser(new Claim(TokenClaimConstants.OrgId, "org-1"));

        (await SucceedsAsync(provider, user)).Should().BeFalse(
            "an org_id without the :platform audience must not satisfy the platform branch");
    }

    [Fact]
    public async Task ServiceTokenWithoutServiceAudience_Fails()
    {
        using var provider = BuildProvider();
        var user = AuthenticatedUser(
            new Claim(TokenClaimConstants.TokenType, TokenClaimConstants.TokenTypeService));

        (await SucceedsAsync(provider, user)).Should().BeFalse(
            "token_type alone is insufficient; the :service audience is required");
    }
}
