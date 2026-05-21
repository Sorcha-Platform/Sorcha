// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.IdentityModel.Tokens.Jwt;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Sorcha.ServiceDefaults.Auth;
using Sorcha.Tenant.Service.Data.Repositories;
using Sorcha.Tenant.Service.Extensions;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Services;
using Xunit;

namespace Sorcha.Tenant.Service.Tests.Services;

/// <summary>
/// Per-tier claim-set tests for <see cref="TokenService.GenerateUserTokenAsync"/> (spec 136,
/// FR-013/FR-014): a consumer token carries the holder identifier but omits org context + roles;
/// a platform token carries them. Audience is always tier- and installation-scoped.
/// </summary>
public sealed class TokenServiceTierTests
{
    private const string Installation = "test";

    private static TokenService CreateService()
    {
        var config = new JwtConfiguration
        {
            InstallationName = Installation,
            SigningKey = "tier-tests-signing-key-min-32-characters-long-enough!!",
            Issuer = "urn:sorcha:test",
            AccessTokenLifetimeMinutes = 60,
            RefreshTokenLifetimeHours = 24,
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = false,
        };

        var revocation = new Mock<ITokenRevocationService>();
        revocation
            .Setup(r => r.TrackTokenAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(),
                It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var participants = new Mock<IParticipantRepository>();
        // No participant → no wallet_address claim (keeps the platform assertion focused on org/roles).
        participants
            .Setup(p => p.GetByUserAndOrgAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ParticipantIdentity?)null);

        return new TokenService(
            Options.Create(config),
            revocation.Object,
            Mock.Of<IIdentityRepository>(),
            Mock.Of<IOrganizationRepository>(),
            participants.Object,
            NullLogger<TokenService>.Instance);
    }

    private static (UserIdentity user, Organization org) Subjects() => (
        new UserIdentity
        {
            Id = Guid.NewGuid(),
            OrganizationId = Guid.NewGuid(),
            PlatformUserId = Guid.NewGuid(),
            Email = "citizen@example.com",
            DisplayName = "Citizen Sarah",
            Roles = [UserRole.Administrator, UserRole.Consumer],
            Status = IdentityStatus.Active,
        },
        new Organization { Id = Guid.NewGuid(), Name = "Acme Corp", Subdomain = "acme" });

    private static JwtSecurityToken Decode(string jwt) => new JwtSecurityTokenHandler().ReadJwtToken(jwt);

    [Fact]
    public async Task GenerateUserToken_Consumer_CarriesOrgContext_OmitsRoles()
    {
        var svc = CreateService();
        var (user, org) = Subjects();

        var response = await svc.GenerateUserTokenAsync(user, org, user.PlatformUserId, Tier.Consumer);
        var token = Decode(response.AccessToken);

        token.Audiences.Should().ContainSingle().Which.Should().Be("test:consumer");
        token.Claims.Should().Contain(c => c.Type == "platform_user_id" && c.Value == user.PlatformUserId.ToString());
        token.Claims.Should().Contain(c => c.Type == "token_type" && c.Value == "user");
        token.Claims.Should().Contain(c => c.Type == JwtRegisteredClaimNames.Email && c.Value == user.Email);

        // Spec 136 refinement: a consumer carries org context (their home/public org) so org-scoped
        // consumer operations work — but NOT roles/wallet (the platform-privilege markers). The tier
        // boundary is the audience + the absence of roles, not the absence of org_id.
        token.Claims.Should().Contain(c => c.Type == "org_id" && c.Value == org.Id.ToString());
        token.Claims.Should().Contain(c => c.Type == "org_name" && c.Value == org.Name);
        token.Claims.Should().NotContain(c => c.Type == System.Security.Claims.ClaimTypes.Role || c.Type == "role",
            "roles are platform-only — a consumer token must remain inert against admin surfaces");
        token.Claims.Should().NotContain(c => c.Type == "wallet_address");
    }

    [Fact]
    public async Task GenerateUserToken_Platform_CarriesOrgAndRoles()
    {
        var svc = CreateService();
        var (user, org) = Subjects();

        var response = await svc.GenerateUserTokenAsync(user, org, user.PlatformUserId, Tier.Platform);
        var token = Decode(response.AccessToken);

        token.Audiences.Should().ContainSingle().Which.Should().Be("test:platform");
        token.Claims.Should().Contain(c => c.Type == "org_id" && c.Value == org.Id.ToString());
        token.Claims.Should().Contain(c => c.Type == "org_name" && c.Value == org.Name);
        token.Claims.Should().Contain(c => c.Value == "Administrator");
        token.Claims.Should().Contain(c => c.Type == "platform_user_id" && c.Value == user.PlatformUserId.ToString());
    }

    [Fact]
    public async Task GenerateUserToken_DefaultsToPlatform()
    {
        var svc = CreateService();
        var (user, org) = Subjects();

        var response = await svc.GenerateUserTokenAsync(user, org, user.PlatformUserId);
        var token = Decode(response.AccessToken);

        token.Audiences.Should().ContainSingle().Which.Should().Be("test:platform");
    }

    [Theory]
    [InlineData(Tier.Consumer, "consumer")]
    [InlineData(Tier.Platform, "platform")]
    public async Task GenerateUserToken_RefreshToken_CarriesMatchingTier(Tier tier, string expectedTierClaim)
    {
        var svc = CreateService();
        var (user, org) = Subjects();

        var response = await svc.GenerateUserTokenAsync(user, org, user.PlatformUserId, tier);
        var refresh = Decode(response.RefreshToken);

        refresh.Claims.Should().Contain(c => c.Type == "tier" && c.Value == expectedTierClaim);
        refresh.Claims.Should().Contain(c => c.Type == "token_use" && c.Value == "refresh");
        refresh.Audiences.Should().ContainSingle().Which.Should().Be($"test:{expectedTierClaim}");
    }

    [Theory]
    [InlineData(Tier.Service)]
    [InlineData(Tier.EnrolSession)]
    public async Task GenerateUserToken_MachineTier_Throws(Tier tier)
    {
        var svc = CreateService();
        var (user, org) = Subjects();

        var act = () => svc.GenerateUserTokenAsync(user, org, user.PlatformUserId, tier);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }
}
