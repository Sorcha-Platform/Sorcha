// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Sorcha.ServiceClients.Auth;

namespace Sorcha.ServiceDefaults.Tests;

/// <summary>
/// Tests for <see cref="AuthorizationPolicyExtensions"/> which register the standard
/// set of shared authorization policies across all Sorcha services.
/// </summary>
public class AuthorizationPolicyExtensionsTests
{
    /// <summary>
    /// The six shared policy names that must be registered by <see cref="AuthorizationPolicyExtensions.AddSorchaAuthorizationPolicies{TBuilder}"/>.
    /// </summary>
    private static readonly string[] ExpectedPolicyNames =
    [
        "RequireAuthenticated",
        "RequireService",
        "RequireOrganizationMember",
        "RequireDelegatedAuthority",
        "RequireAdministrator",
        "CanWriteDockets"
    ];

    #region Helper Methods

    /// <summary>
    /// Builds a service provider with Sorcha authorization policies registered.
    /// </summary>
    private static ServiceProvider BuildServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSorchaAuthorizationPolicies();
        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Retrieves the resolved <see cref="AuthorizationOptions"/> from the service provider.
    /// </summary>
    private static AuthorizationOptions GetAuthorizationOptions(ServiceProvider provider)
    {
        return provider.GetRequiredService<IOptions<AuthorizationOptions>>().Value;
    }

    /// <summary>
    /// Evaluates an authorization policy against a <see cref="ClaimsPrincipal"/> using the full
    /// <see cref="IAuthorizationService"/> pipeline, ensuring requirements are checked properly.
    /// </summary>
    private static async Task<AuthorizationResult> EvaluatePolicyAsync(
        ServiceProvider provider, string policyName, ClaimsPrincipal user)
    {
        var authorizationService = provider.GetRequiredService<IAuthorizationService>();
        return await authorizationService.AuthorizeAsync(user, policyName);
    }

    /// <summary>
    /// Creates an authenticated <see cref="ClaimsPrincipal"/> with the specified claims.
    /// </summary>
    private static ClaimsPrincipal CreateAuthenticatedUser(params Claim[] claims)
    {
        var identity = new ClaimsIdentity(claims, authenticationType: "TestScheme");
        return new ClaimsPrincipal(identity);
    }

    /// <summary>
    /// Creates an unauthenticated <see cref="ClaimsPrincipal"/> (no identity or unauthenticated identity).
    /// </summary>
    private static ClaimsPrincipal CreateUnauthenticatedUser()
    {
        return new ClaimsPrincipal(new ClaimsIdentity());
    }

    #endregion

    #region IHostApplicationBuilder Overload

    [Fact]
    public void AddSorchaAuthorizationPolicies_WithHostBuilder_ReturnsBuilderForChaining()
    {
        // Arrange
        var builder = WebApplication.CreateBuilder();

        // Act
        var result = builder.AddSorchaAuthorizationPolicies();

        // Assert
        result.Should().BeSameAs(builder,
            "the method should return the builder for fluent chaining");
    }

    #endregion

    #region IServiceCollection Overload

    [Fact]
    public void AddSorchaAuthorizationPolicies_WithServiceCollection_ReturnsServiceCollectionForChaining()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var result = services.AddSorchaAuthorizationPolicies();

        // Assert
        result.Should().BeSameAs(services,
            "the method should return the IServiceCollection for fluent chaining");
    }

    #endregion

    #region Policy Registration

    [Theory]
    [InlineData("RequireAuthenticated")]
    [InlineData("RequireService")]
    [InlineData("RequireOrganizationMember")]
    [InlineData("RequireDelegatedAuthority")]
    [InlineData("RequireAdministrator")]
    [InlineData("CanWriteDockets")]
    public void AddSorchaAuthorizationPolicies_RegistersPolicy_ByName(string policyName)
    {
        // Arrange & Act
        using var provider = BuildServiceProvider();
        var options = GetAuthorizationOptions(provider);

        // Assert
        var policy = options.GetPolicy(policyName);
        policy.Should().NotBeNull(
            $"the shared policy '{policyName}' should be registered in AuthorizationOptions");
    }

    [Fact]
    public void AddSorchaAuthorizationPolicies_RegistersAllSixPolicies()
    {
        // Arrange & Act
        using var provider = BuildServiceProvider();
        var options = GetAuthorizationOptions(provider);

        // Assert
        foreach (var name in ExpectedPolicyNames)
        {
            options.GetPolicy(name).Should().NotBeNull(
                $"expected policy '{name}' to be registered");
        }
    }

    #endregion

    #region RequireAuthenticated Policy

    [Fact]
    public async Task RequireAuthenticated_AuthenticatedUser_Succeeds()
    {
        // Arrange
        using var provider = BuildServiceProvider();
        var user = CreateAuthenticatedUser(new Claim("sub", "user-1"));

        // Act
        var result = await EvaluatePolicyAsync(provider, "RequireAuthenticated", user);

        // Assert
        result.Succeeded.Should().BeTrue(
            "an authenticated user should satisfy the RequireAuthenticated policy");
    }

    [Fact]
    public async Task RequireAuthenticated_UnauthenticatedUser_Fails()
    {
        // Arrange
        using var provider = BuildServiceProvider();
        var user = CreateUnauthenticatedUser();

        // Act
        var result = await EvaluatePolicyAsync(provider, "RequireAuthenticated", user);

        // Assert
        result.Succeeded.Should().BeFalse(
            "an unauthenticated user should not satisfy the RequireAuthenticated policy");
    }

    #endregion

    #region RequireService Policy

    [Fact]
    public async Task RequireService_WithServiceTokenTypeAndServiceAudience_Succeeds()
    {
        // Arrange — spec 136: RequireService now also requires this installation's :service audience.
        using var provider = BuildServiceProvider();
        var user = CreateAuthenticatedUser(
            new Claim(TokenClaimConstants.TokenType, TokenClaimConstants.TokenTypeService),
            new Claim("aud", "sorcha:service"));

        // Act
        var result = await EvaluatePolicyAsync(provider, "RequireService", user);

        // Assert
        result.Succeeded.Should().BeTrue(
            "a service token carrying the :service audience should satisfy RequireService");
    }

    [Fact]
    public async Task RequireService_WithServiceTokenTypeButNoServiceAudience_Fails()
    {
        // Spec 136: token_type alone is no longer sufficient — the :service audience is required.
        using var provider = BuildServiceProvider();
        var user = CreateAuthenticatedUser(
            new Claim(TokenClaimConstants.TokenType, TokenClaimConstants.TokenTypeService));

        var result = await EvaluatePolicyAsync(provider, "RequireService", user);

        result.Succeeded.Should().BeFalse(
            "without the :service audience a service-typed token must be refused at the audience layer");
    }

    [Fact]
    public async Task RequireService_WithServiceAudienceFromAnotherInstallation_Fails()
    {
        // Cross-installation: the default installation here is "sorcha"; a foreign :service aud is refused.
        using var provider = BuildServiceProvider();
        var user = CreateAuthenticatedUser(
            new Claim(TokenClaimConstants.TokenType, TokenClaimConstants.TokenTypeService),
            new Claim("aud", "acme:service"));

        var result = await EvaluatePolicyAsync(provider, "RequireService", user);

        result.Succeeded.Should().BeFalse("a service token from a different installation must be refused");
    }

    [Fact]
    public async Task RequireService_WithUserTokenType_Fails()
    {
        // Arrange
        using var provider = BuildServiceProvider();
        var user = CreateAuthenticatedUser(
            new Claim(TokenClaimConstants.TokenType, TokenClaimConstants.TokenTypeUser));

        // Act
        var result = await EvaluatePolicyAsync(provider, "RequireService", user);

        // Assert
        result.Succeeded.Should().BeFalse(
            "a token with token_type=user should not satisfy the RequireService policy");
    }

    [Fact]
    public async Task RequireService_WithNoTokenTypeClaim_Fails()
    {
        // Arrange
        using var provider = BuildServiceProvider();
        var user = CreateAuthenticatedUser(new Claim("sub", "user-1"));

        // Act
        var result = await EvaluatePolicyAsync(provider, "RequireService", user);

        // Assert
        result.Succeeded.Should().BeFalse(
            "a token without a token_type claim should not satisfy the RequireService policy");
    }

    #endregion

    #region RequireOrganizationMember Policy

    [Fact]
    public async Task RequireOrganizationMember_WithNonEmptyOrgId_Succeeds()
    {
        // Arrange
        using var provider = BuildServiceProvider();
        var user = CreateAuthenticatedUser(
            new Claim(TokenClaimConstants.OrgId, "org-123"));

        // Act
        var result = await EvaluatePolicyAsync(provider, "RequireOrganizationMember", user);

        // Assert
        result.Succeeded.Should().BeTrue(
            "a token with a non-empty org_id claim should satisfy RequireOrganizationMember");
    }

    [Fact]
    public async Task RequireOrganizationMember_WithEmptyOrgId_Fails()
    {
        // Arrange
        using var provider = BuildServiceProvider();
        var user = CreateAuthenticatedUser(
            new Claim(TokenClaimConstants.OrgId, ""));

        // Act
        var result = await EvaluatePolicyAsync(provider, "RequireOrganizationMember", user);

        // Assert
        result.Succeeded.Should().BeFalse(
            "a token with an empty org_id claim should not satisfy RequireOrganizationMember");
    }

    [Fact]
    public async Task RequireOrganizationMember_WithNoOrgIdClaim_Fails()
    {
        // Arrange
        using var provider = BuildServiceProvider();
        var user = CreateAuthenticatedUser(new Claim("sub", "user-1"));

        // Act
        var result = await EvaluatePolicyAsync(provider, "RequireOrganizationMember", user);

        // Assert
        result.Succeeded.Should().BeFalse(
            "a token without an org_id claim should not satisfy RequireOrganizationMember");
    }

    [Fact]
    public async Task RequireOrganizationMember_UnauthenticatedUser_Fails()
    {
        // Arrange
        using var provider = BuildServiceProvider();
        var user = CreateUnauthenticatedUser();

        // Act
        var result = await EvaluatePolicyAsync(provider, "RequireOrganizationMember", user);

        // Assert
        result.Succeeded.Should().BeFalse(
            "an unauthenticated user should not satisfy RequireOrganizationMember");
    }

    #endregion

    #region RequireDelegatedAuthority Policy

    [Fact]
    public async Task RequireDelegatedAuthority_WithServiceTokenAndDelegatedUserId_Succeeds()
    {
        // Arrange
        using var provider = BuildServiceProvider();
        var user = CreateAuthenticatedUser(
            new Claim(TokenClaimConstants.TokenType, TokenClaimConstants.TokenTypeService),
            new Claim(TokenClaimConstants.DelegatedUserId, "delegated-user-42"));

        // Act
        var result = await EvaluatePolicyAsync(provider, "RequireDelegatedAuthority", user);

        // Assert
        result.Succeeded.Should().BeTrue(
            "a service token with a non-empty delegated_user_id should satisfy RequireDelegatedAuthority");
    }

    [Fact]
    public async Task RequireDelegatedAuthority_WithServiceTokenButNoDelegatedUserId_Fails()
    {
        // Arrange
        using var provider = BuildServiceProvider();
        var user = CreateAuthenticatedUser(
            new Claim(TokenClaimConstants.TokenType, TokenClaimConstants.TokenTypeService));

        // Act
        var result = await EvaluatePolicyAsync(provider, "RequireDelegatedAuthority", user);

        // Assert
        result.Succeeded.Should().BeFalse(
            "a service token without a delegated_user_id claim should not satisfy RequireDelegatedAuthority");
    }

    [Fact]
    public async Task RequireDelegatedAuthority_WithDelegatedUserIdButUserToken_Fails()
    {
        // Arrange
        using var provider = BuildServiceProvider();
        var user = CreateAuthenticatedUser(
            new Claim(TokenClaimConstants.TokenType, TokenClaimConstants.TokenTypeUser),
            new Claim(TokenClaimConstants.DelegatedUserId, "delegated-user-42"));

        // Act
        var result = await EvaluatePolicyAsync(provider, "RequireDelegatedAuthority", user);

        // Assert
        result.Succeeded.Should().BeFalse(
            "a user token (not service) should not satisfy RequireDelegatedAuthority even with delegated_user_id");
    }

    [Fact]
    public async Task RequireDelegatedAuthority_WithServiceTokenAndEmptyDelegatedUserId_Fails()
    {
        // Arrange
        using var provider = BuildServiceProvider();
        var user = CreateAuthenticatedUser(
            new Claim(TokenClaimConstants.TokenType, TokenClaimConstants.TokenTypeService),
            new Claim(TokenClaimConstants.DelegatedUserId, ""));

        // Act
        var result = await EvaluatePolicyAsync(provider, "RequireDelegatedAuthority", user);

        // Assert
        result.Succeeded.Should().BeFalse(
            "a service token with an empty delegated_user_id should not satisfy RequireDelegatedAuthority");
    }

    [Fact]
    public async Task RequireDelegatedAuthority_WithNoTokenTypeClaim_Fails()
    {
        // Arrange
        using var provider = BuildServiceProvider();
        var user = CreateAuthenticatedUser(
            new Claim(TokenClaimConstants.DelegatedUserId, "delegated-user-42"));

        // Act
        var result = await EvaluatePolicyAsync(provider, "RequireDelegatedAuthority", user);

        // Assert
        result.Succeeded.Should().BeFalse(
            "a token without token_type should not satisfy RequireDelegatedAuthority");
    }

    #endregion

    #region RequireAdministrator Policy

    [Fact]
    public async Task RequireAdministrator_WithAdministratorRole_Succeeds()
    {
        // Arrange
        using var provider = BuildServiceProvider();
        var user = CreateAuthenticatedUser(
            new Claim(ClaimTypes.Role, "Administrator"));

        // Act
        var result = await EvaluatePolicyAsync(provider, "RequireAdministrator", user);

        // Assert
        result.Succeeded.Should().BeTrue(
            "a user with the Administrator role should satisfy RequireAdministrator");
    }

    [Fact]
    public async Task RequireAdministrator_WithDifferentRole_Fails()
    {
        // Arrange
        using var provider = BuildServiceProvider();
        var user = CreateAuthenticatedUser(
            new Claim(ClaimTypes.Role, "User"));

        // Act
        var result = await EvaluatePolicyAsync(provider, "RequireAdministrator", user);

        // Assert
        result.Succeeded.Should().BeFalse(
            "a user without the Administrator role should not satisfy RequireAdministrator");
    }

    [Fact]
    public async Task RequireAdministrator_WithNoRoleClaim_Fails()
    {
        // Arrange
        using var provider = BuildServiceProvider();
        var user = CreateAuthenticatedUser(new Claim("sub", "user-1"));

        // Act
        var result = await EvaluatePolicyAsync(provider, "RequireAdministrator", user);

        // Assert
        result.Succeeded.Should().BeFalse(
            "a user without any role claim should not satisfy RequireAdministrator");
    }

    #endregion

    #region CanWriteDockets Policy

    [Fact]
    public async Task CanWriteDockets_WithServiceTokenAndServiceAudience_Succeeds()
    {
        // Arrange — spec 136: CanWriteDockets mirrors the extended RequireService (:service audience).
        using var provider = BuildServiceProvider();
        var user = CreateAuthenticatedUser(
            new Claim(TokenClaimConstants.TokenType, TokenClaimConstants.TokenTypeService),
            new Claim("aud", "sorcha:service"));

        // Act
        var result = await EvaluatePolicyAsync(provider, "CanWriteDockets", user);

        // Assert
        result.Succeeded.Should().BeTrue(
            "a service token carrying the :service audience should satisfy CanWriteDockets");
    }

    [Fact]
    public async Task CanWriteDockets_WithServiceTokenButNoServiceAudience_Fails()
    {
        using var provider = BuildServiceProvider();
        var user = CreateAuthenticatedUser(
            new Claim(TokenClaimConstants.TokenType, TokenClaimConstants.TokenTypeService));

        var result = await EvaluatePolicyAsync(provider, "CanWriteDockets", user);

        result.Succeeded.Should().BeFalse("the :service audience is required (spec 136)");
    }

    [Fact]
    public async Task CanWriteDockets_WithUserTokenType_Fails()
    {
        // Arrange
        using var provider = BuildServiceProvider();
        var user = CreateAuthenticatedUser(
            new Claim(TokenClaimConstants.TokenType, TokenClaimConstants.TokenTypeUser));

        // Act
        var result = await EvaluatePolicyAsync(provider, "CanWriteDockets", user);

        // Assert
        result.Succeeded.Should().BeFalse(
            "a user token should not satisfy CanWriteDockets");
    }

    [Fact]
    public async Task CanWriteDockets_WithNoTokenTypeClaim_Fails()
    {
        // Arrange
        using var provider = BuildServiceProvider();
        var user = CreateAuthenticatedUser(new Claim("sub", "user-1"));

        // Act
        var result = await EvaluatePolicyAsync(provider, "CanWriteDockets", user);

        // Assert
        result.Succeeded.Should().BeFalse(
            "a token without token_type should not satisfy CanWriteDockets");
    }

    #endregion

    #region Tier-audience policies (spec 136)

    [Fact]
    public async Task RequireConsumerAudience_WithConsumerAudience_Succeeds()
    {
        using var provider = BuildServiceProvider();
        var user = CreateAuthenticatedUser(new Claim("aud", "sorcha:consumer"));

        var result = await EvaluatePolicyAsync(provider, "RequireConsumerAudience", user);

        result.Succeeded.Should().BeTrue("a consumer-tier token satisfies RequireConsumerAudience");
    }

    [Fact]
    public async Task RequireConsumerAudience_WithPlatformAudience_Fails()
    {
        using var provider = BuildServiceProvider();
        var user = CreateAuthenticatedUser(new Claim("aud", "sorcha:platform"));

        var result = await EvaluatePolicyAsync(provider, "RequireConsumerAudience", user);

        result.Succeeded.Should().BeFalse("a platform-tier token must be refused at a consumer surface");
    }

    [Fact]
    public async Task RequirePlatformAudience_WithPlatformAudience_Succeeds()
    {
        using var provider = BuildServiceProvider();
        var user = CreateAuthenticatedUser(new Claim("aud", "sorcha:platform"));

        var result = await EvaluatePolicyAsync(provider, "RequirePlatformAudience", user);

        result.Succeeded.Should().BeTrue("a platform-tier token satisfies RequirePlatformAudience");
    }

    [Fact]
    public async Task RequirePlatformAudience_WithConsumerAudience_Fails()
    {
        using var provider = BuildServiceProvider();
        var user = CreateAuthenticatedUser(new Claim("aud", "sorcha:consumer"));

        var result = await EvaluatePolicyAsync(provider, "RequirePlatformAudience", user);

        result.Succeeded.Should().BeFalse("a consumer-tier token must be refused at a platform surface");
    }

    [Fact]
    public async Task TierAudiencePolicies_UnauthenticatedUser_Fails()
    {
        using var provider = BuildServiceProvider();
        var user = CreateUnauthenticatedUser();

        (await EvaluatePolicyAsync(provider, "RequireConsumerAudience", user)).Succeeded.Should().BeFalse();
        (await EvaluatePolicyAsync(provider, "RequirePlatformAudience", user)).Succeeded.Should().BeFalse();
    }

    #endregion
}
