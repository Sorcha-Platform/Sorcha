// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Claims;

using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;

using Sorcha.ServiceClients.Auth;
using Sorcha.Wallet.Service.Extensions;

namespace Sorcha.Wallet.Service.Tests.Authorization;

/// <summary>
/// Policy-evaluation tests for the Wallet Service <c>CanRecoverSystemWallet</c> policy (Feature 147 / review H1).
/// Mirrors the shared <c>AuthorizationPolicyExtensionsTests</c> harness: build a provider with
/// <see cref="WalletServiceExtensions"/>-registered policies and evaluate through the real
/// <see cref="IAuthorizationService"/> pipeline. Default installation is "sorcha" (no configuration),
/// so tier audiences are <c>sorcha:{consumer|platform|service}</c>.
/// </summary>
public class WalletAuthorizationPolicyTests
{
    private const string PolicyName = "CanRecoverSystemWallet";

    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddWalletAuthorization();
        return services.BuildServiceProvider();
    }

    private static ClaimsPrincipal AuthenticatedUser(params Claim[] claims) =>
        new(new ClaimsIdentity(claims, authenticationType: "TestScheme"));

    private static ClaimsPrincipal UnauthenticatedUser() => new(new ClaimsIdentity());

    private static async Task<bool> SucceedsAsync(ServiceProvider provider, ClaimsPrincipal user)
    {
        var authz = provider.GetRequiredService<IAuthorizationService>();
        return (await authz.AuthorizeAsync(user, PolicyName)).Succeeded;
    }

    [Fact]
    public async Task ServiceTokenWithServiceAudience_Succeeds()
    {
        using var provider = BuildProvider();
        var user = AuthenticatedUser(
            new Claim(TokenClaimConstants.TokenType, TokenClaimConstants.TokenTypeService),
            new Claim("aud", "sorcha:service"));

        (await SucceedsAsync(provider, user)).Should().BeTrue(
            "a service-tier caller (forward-looking automation) may recover a system wallet");
    }

    [Fact]
    public async Task AdministratorWithPlatformAudience_Succeeds()
    {
        using var provider = BuildProvider();
        var user = AuthenticatedUser(
            new Claim(ClaimTypes.Role, "Administrator"),
            new Claim("aud", "sorcha:platform"));

        (await SucceedsAsync(provider, user)).Should().BeTrue(
            "the genesis-ceremony administrator (platform tier) may recover a system wallet");
    }

    [Fact]
    public async Task SystemAdminWithPlatformAudience_Succeeds()
    {
        using var provider = BuildProvider();
        var user = AuthenticatedUser(
            new Claim(ClaimTypes.Role, "SystemAdmin"),
            new Claim("aud", "sorcha:platform"));

        (await SucceedsAsync(provider, user)).Should().BeTrue(
            "a SystemAdmin platform-tier caller may recover a system wallet");
    }

    [Fact]
    public async Task ConsumerToken_Fails()
    {
        using var provider = BuildProvider();
        var user = AuthenticatedUser(
            new Claim(TokenClaimConstants.OrgId, "org-1"),
            new Claim("aud", "sorcha:consumer"));

        (await SucceedsAsync(provider, user)).Should().BeFalse(
            "a consumer/citizen token must never seat a validator signing wallet");
    }

    [Fact]
    public async Task AdministratorWithConsumerAudience_Fails()
    {
        using var provider = BuildProvider();
        var user = AuthenticatedUser(
            new Claim(ClaimTypes.Role, "Administrator"),
            new Claim("aud", "sorcha:consumer"));

        (await SucceedsAsync(provider, user)).Should().BeFalse(
            "the administrator branch requires the :platform audience, not merely the role");
    }

    [Fact]
    public async Task PlatformAudienceWithoutAdminRole_Fails()
    {
        using var provider = BuildProvider();
        var user = AuthenticatedUser(
            new Claim(TokenClaimConstants.OrgId, "org-1"),
            new Claim("aud", "sorcha:platform"));

        (await SucceedsAsync(provider, user)).Should().BeFalse(
            "a platform-tier non-administrator must not recover a system wallet");
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

    [Fact]
    public async Task UnauthenticatedUser_Fails()
    {
        using var provider = BuildProvider();

        (await SucceedsAsync(provider, UnauthenticatedUser())).Should().BeFalse(
            "an unauthenticated caller must be refused");
    }
}
