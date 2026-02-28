// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;

using FluentAssertions;

using Sorcha.Tenant.Models;

using Xunit;

namespace Sorcha.Tenant.Models.Tests;

public class TokenClaimsTests
{
    [Fact]
    public void Constructor_WithAllProperties_SetsValues()
    {
        var deploymentId = Guid.NewGuid();
        var orgId = Guid.NewGuid();
        var blockchainId = Guid.NewGuid();

        var claims = new TokenClaims
        {
            Subject = "user-123",
            Issuer = "https://tenant.sorcha.io",
            Audience = ["sorcha-api"],
            ExpiresAt = 1700000000,
            IssuedAt = 1699990000,
            TokenId = "jti-abc",
            Email = "user@example.com",
            Name = "Test User",
            DeploymentId = deploymentId,
            DeploymentName = "Test Deployment",
            Federated = true,
            OrganizationId = orgId,
            OrganizationSubdomain = "acme",
            TokenType = TokenClaims.TokenTypes.User,
            Roles = [TokenClaims.RoleNames.Administrator],
            PermittedBlockchains = [blockchainId],
            CanCreateBlockchain = true,
            CanPublishBlueprint = true
        };

        claims.Subject.Should().Be("user-123");
        claims.Issuer.Should().Be("https://tenant.sorcha.io");
        claims.Audience.Should().ContainSingle().Which.Should().Be("sorcha-api");
        claims.ExpiresAt.Should().Be(1700000000);
        claims.IssuedAt.Should().Be(1699990000);
        claims.TokenId.Should().Be("jti-abc");
        claims.Email.Should().Be("user@example.com");
        claims.Name.Should().Be("Test User");
        claims.DeploymentId.Should().Be(deploymentId);
        claims.DeploymentName.Should().Be("Test Deployment");
        claims.Federated.Should().BeTrue();
        claims.OrganizationId.Should().Be(orgId);
        claims.OrganizationSubdomain.Should().Be("acme");
        claims.TokenType.Should().Be("user");
        claims.Roles.Should().ContainSingle().Which.Should().Be("Administrator");
        claims.PermittedBlockchains.Should().ContainSingle().Which.Should().Be(blockchainId);
        claims.CanCreateBlockchain.Should().BeTrue();
        claims.CanPublishBlueprint.Should().BeTrue();
    }

    [Fact]
    public void Constructor_WithRequiredOnly_HasCorrectDefaults()
    {
        var claims = new TokenClaims
        {
            Subject = "svc-1",
            Issuer = "https://tenant.sorcha.io",
            TokenId = "jti-1",
            DeploymentId = Guid.NewGuid(),
            TokenType = TokenClaims.TokenTypes.Service
        };

        claims.Audience.Should().BeEmpty();
        claims.ExpiresAt.Should().Be(0);
        claims.IssuedAt.Should().Be(0);
        claims.Email.Should().BeNull();
        claims.Name.Should().BeNull();
        claims.DeploymentName.Should().BeNull();
        claims.Federated.Should().BeFalse();
        claims.OrganizationId.Should().BeNull();
        claims.OrganizationSubdomain.Should().BeNull();
        claims.Roles.Should().BeEmpty();
        claims.PermittedBlockchains.Should().BeEmpty();
        claims.CanCreateBlockchain.Should().BeFalse();
        claims.CanPublishBlueprint.Should().BeFalse();
    }

    [Fact]
    public void ClaimNames_StandardClaims_HaveCorrectValues()
    {
        TokenClaims.ClaimNames.Subject.Should().Be("sub");
        TokenClaims.ClaimNames.Issuer.Should().Be("iss");
        TokenClaims.ClaimNames.Audience.Should().Be("aud");
        TokenClaims.ClaimNames.ExpiresAt.Should().Be("exp");
        TokenClaims.ClaimNames.IssuedAt.Should().Be("iat");
        TokenClaims.ClaimNames.TokenId.Should().Be("jti");
        TokenClaims.ClaimNames.Email.Should().Be("email");
        TokenClaims.ClaimNames.Name.Should().Be("name");
    }

    [Fact]
    public void ClaimNames_DeploymentClaims_HaveCorrectValues()
    {
        TokenClaims.ClaimNames.DeploymentId.Should().Be("deployment_id");
        TokenClaims.ClaimNames.DeploymentName.Should().Be("deployment_name");
        TokenClaims.ClaimNames.Federated.Should().Be("federated");
    }

    [Fact]
    public void ClaimNames_OrganizationClaims_HaveCorrectValues()
    {
        TokenClaims.ClaimNames.OrganizationId.Should().Be("org_id");
        TokenClaims.ClaimNames.OrganizationSubdomain.Should().Be("org_subdomain");
    }

    [Fact]
    public void ClaimNames_IdentityAndRoleClaims_HaveCorrectValues()
    {
        TokenClaims.ClaimNames.TokenType.Should().Be("token_type");
        TokenClaims.ClaimNames.Roles.Should().Be("roles");
    }

    [Fact]
    public void ClaimNames_PermissionClaims_HaveCorrectValues()
    {
        TokenClaims.ClaimNames.PermittedBlockchains.Should().Be("permitted_blockchains");
        TokenClaims.ClaimNames.CanCreateBlockchain.Should().Be("can_create_blockchain");
        TokenClaims.ClaimNames.CanPublishBlueprint.Should().Be("can_publish_blueprint");
    }

    [Fact]
    public void TokenTypes_HaveCorrectValues()
    {
        TokenClaims.TokenTypes.User.Should().Be("user");
        TokenClaims.TokenTypes.Service.Should().Be("service");
        TokenClaims.TokenTypes.Public.Should().Be("public");
    }

    [Fact]
    public void RoleNames_HaveCorrectValues()
    {
        TokenClaims.RoleNames.Administrator.Should().Be("Administrator");
        TokenClaims.RoleNames.Auditor.Should().Be("Auditor");
        TokenClaims.RoleNames.Member.Should().Be("Member");
    }

    [Fact]
    public void JsonRoundTrip_PreservesAllProperties()
    {
        var deploymentId = Guid.NewGuid();
        var orgId = Guid.NewGuid();
        var blockchainId = Guid.NewGuid();

        var original = new TokenClaims
        {
            Subject = "user-456",
            Issuer = "https://auth.example.com",
            Audience = ["api-1", "api-2"],
            ExpiresAt = 1700000000,
            IssuedAt = 1699990000,
            TokenId = "jti-xyz",
            Email = "test@example.com",
            Name = "Test User",
            DeploymentId = deploymentId,
            DeploymentName = "Production",
            Federated = true,
            OrganizationId = orgId,
            OrganizationSubdomain = "test",
            TokenType = TokenClaims.TokenTypes.User,
            Roles = [TokenClaims.RoleNames.Administrator, TokenClaims.RoleNames.Auditor],
            PermittedBlockchains = [blockchainId],
            CanCreateBlockchain = true,
            CanPublishBlueprint = true
        };

        var json = JsonSerializer.Serialize(original);
        var deserialized = JsonSerializer.Deserialize<TokenClaims>(json)!;

        deserialized.Subject.Should().Be(original.Subject);
        deserialized.Issuer.Should().Be(original.Issuer);
        deserialized.Audience.Should().BeEquivalentTo(original.Audience);
        deserialized.ExpiresAt.Should().Be(original.ExpiresAt);
        deserialized.IssuedAt.Should().Be(original.IssuedAt);
        deserialized.TokenId.Should().Be(original.TokenId);
        deserialized.Email.Should().Be(original.Email);
        deserialized.Name.Should().Be(original.Name);
        deserialized.DeploymentId.Should().Be(original.DeploymentId);
        deserialized.DeploymentName.Should().Be(original.DeploymentName);
        deserialized.Federated.Should().Be(original.Federated);
        deserialized.OrganizationId.Should().Be(original.OrganizationId);
        deserialized.OrganizationSubdomain.Should().Be(original.OrganizationSubdomain);
        deserialized.TokenType.Should().Be(original.TokenType);
        deserialized.Roles.Should().BeEquivalentTo(original.Roles);
        deserialized.PermittedBlockchains.Should().BeEquivalentTo(original.PermittedBlockchains);
        deserialized.CanCreateBlockchain.Should().Be(original.CanCreateBlockchain);
        deserialized.CanPublishBlueprint.Should().Be(original.CanPublishBlueprint);
    }
}
