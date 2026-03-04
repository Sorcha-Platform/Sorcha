// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Sorcha.ServiceClients.Auth;
using Sorcha.Validator.Service.Extensions;

namespace Sorcha.Validator.Service.Tests;

/// <summary>
/// Tests for Validator Service authorization policies configured in AuthenticationExtensions.
/// Verifies that policies correctly grant/deny access based on token claims.
/// </summary>
public class AuthenticationTests
{
    private readonly IAuthorizationService _authorizationService;

    public AuthenticationTests()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthorization();
        var provider = services.BuildServiceProvider();
        _authorizationService = provider.GetRequiredService<IAuthorizationService>();
    }

    private static ClaimsPrincipal CreatePrincipal(params Claim[] claims)
    {
        var identity = new ClaimsIdentity(claims, "TestScheme");
        return new ClaimsPrincipal(identity);
    }

    private static ClaimsPrincipal CreateAnonymousPrincipal()
    {
        return new ClaimsPrincipal(new ClaimsIdentity());
    }

    // --- RequireAuthenticated ---

    [Fact]
    public async Task RequireAuthenticated_WithValidToken_Succeeds()
    {
        var principal = CreatePrincipal(new Claim(ClaimTypes.Name, "test-user"));
        var result = await _authorizationService.AuthorizeAsync(principal, "RequireAuthenticated");
        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task RequireAuthenticated_WithoutToken_Fails()
    {
        var principal = CreateAnonymousPrincipal();
        var result = await _authorizationService.AuthorizeAsync(principal, "RequireAuthenticated");
        result.Succeeded.Should().BeFalse();
    }

    // --- RequireService ---

    [Fact]
    public async Task RequireService_WithServiceToken_Succeeds()
    {
        var principal = CreatePrincipal(
            new Claim(TokenClaimConstants.TokenType, TokenClaimConstants.TokenTypeService));
        var result = await _authorizationService.AuthorizeAsync(principal, "RequireService");
        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task RequireService_WithUserToken_Fails()
    {
        var principal = CreatePrincipal(
            new Claim(TokenClaimConstants.TokenType, TokenClaimConstants.TokenTypeUser));
        var result = await _authorizationService.AuthorizeAsync(principal, "RequireService");
        result.Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task RequireService_WithoutTokenType_Fails()
    {
        var principal = CreatePrincipal(new Claim(ClaimTypes.Name, "test-user"));
        var result = await _authorizationService.AuthorizeAsync(principal, "RequireService");
        result.Succeeded.Should().BeFalse();
    }

    // --- CanValidateChains ---

    [Fact]
    public async Task CanValidateChains_WithServiceToken_Succeeds()
    {
        var principal = CreatePrincipal(
            new Claim(TokenClaimConstants.TokenType, TokenClaimConstants.TokenTypeService));
        var result = await _authorizationService.AuthorizeAsync(principal, "CanValidateChains");
        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task CanValidateChains_WithAdminRole_Succeeds()
    {
        var principal = CreatePrincipal(
            new Claim(ClaimTypes.Role, "Administrator"));
        var result = await _authorizationService.AuthorizeAsync(principal, "CanValidateChains");
        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task CanValidateChains_WithRegularUserToken_Fails()
    {
        var principal = CreatePrincipal(
            new Claim(TokenClaimConstants.TokenType, TokenClaimConstants.TokenTypeUser),
            new Claim(ClaimTypes.Role, "Member"));
        var result = await _authorizationService.AuthorizeAsync(principal, "CanValidateChains");
        result.Succeeded.Should().BeFalse();
    }

    // --- CanWriteDockets ---

    [Fact]
    public async Task CanWriteDockets_WithServiceToken_Succeeds()
    {
        var principal = CreatePrincipal(
            new Claim(TokenClaimConstants.TokenType, TokenClaimConstants.TokenTypeService));
        var result = await _authorizationService.AuthorizeAsync(principal, "CanWriteDockets");
        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task CanWriteDockets_WithUserToken_Fails()
    {
        var principal = CreatePrincipal(
            new Claim(TokenClaimConstants.TokenType, TokenClaimConstants.TokenTypeUser));
        var result = await _authorizationService.AuthorizeAsync(principal, "CanWriteDockets");
        result.Succeeded.Should().BeFalse();
    }

    // --- RequireOrganizationMember ---

    [Fact]
    public async Task RequireOrganizationMember_WithOrgId_Succeeds()
    {
        var principal = CreatePrincipal(
            new Claim(TokenClaimConstants.OrgId, Guid.NewGuid().ToString()));
        var result = await _authorizationService.AuthorizeAsync(principal, "RequireOrganizationMember");
        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task RequireOrganizationMember_WithoutOrgId_Fails()
    {
        var principal = CreatePrincipal(new Claim(ClaimTypes.Name, "test-user"));
        var result = await _authorizationService.AuthorizeAsync(principal, "RequireOrganizationMember");
        result.Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task RequireOrganizationMember_WithEmptyOrgId_Fails()
    {
        var principal = CreatePrincipal(
            new Claim(TokenClaimConstants.OrgId, ""));
        var result = await _authorizationService.AuthorizeAsync(principal, "RequireOrganizationMember");
        result.Succeeded.Should().BeFalse();
    }

    // --- RequireAdministrator ---

    [Fact]
    public async Task RequireAdministrator_WithAdminRole_Succeeds()
    {
        var principal = CreatePrincipal(
            new Claim(ClaimTypes.Role, "Administrator"));
        var result = await _authorizationService.AuthorizeAsync(principal, "RequireAdministrator");
        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task RequireAdministrator_WithMemberRole_Fails()
    {
        var principal = CreatePrincipal(
            new Claim(ClaimTypes.Role, "Member"));
        var result = await _authorizationService.AuthorizeAsync(principal, "RequireAdministrator");
        result.Succeeded.Should().BeFalse();
    }
}
