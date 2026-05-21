// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using FluentAssertions;
using Microsoft.IdentityModel.Tokens;
using Sorcha.ServiceDefaults.Auth;
using Xunit;

namespace Sorcha.ServiceDefaults.Tests.Auth;

/// <summary>
/// Tests that a token minted under one installation's identity (issuer + audience namespace)
/// is rejected by another installation (spec 136, US3, FR-004/SC-003). The signing key is held
/// constant so these tests isolate the issuer/audience axis from the per-installation key axis —
/// even with a shared key, installation A's token must not validate under installation B.
/// </summary>
public sealed class CrossInstallationRejectionTests
{
    private const string SharedKey = "cross-installation-test-signing-key-min-32-chars-long-enough";

    private static readonly SymmetricSecurityKey Key =
        new(Encoding.UTF8.GetBytes(SharedKey));

    private static string MintFor(string installation, Tier tier)
    {
        var audiences = new SorchaAudiences(installation);
        var issuer = SorchaIssuer.Resolve(explicitIssuer: null, installation, allowDevLocalFallback: false);

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audiences.For(tier),
            claims: [new Claim(JwtRegisteredClaimNames.Sub, "user-1")],
            notBefore: DateTime.UtcNow.AddMinutes(-1),
            expires: DateTime.UtcNow.AddMinutes(60),
            signingCredentials: new SigningCredentials(Key, SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static TokenValidationParameters ParamsFor(string installation) => new()
    {
        ValidateIssuer = true,
        ValidIssuer = SorchaIssuer.Resolve(null, installation, allowDevLocalFallback: false),
        ValidateAudience = true,
        ValidAudiences = new SorchaAudiences(installation).All,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = Key,
        ValidateLifetime = true,
        ClockSkew = TimeSpan.FromMinutes(1),
        ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
    };

    [Fact]
    public void TokenFromInstallationA_RejectedUnderInstallationB()
    {
        var tokenA = MintFor("acme", Tier.Platform);
        var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };

        var act = () => handler.ValidateToken(tokenA, ParamsFor("n1"), out _);

        // Both the issuer (urn:sorcha:acme vs urn:sorcha:n1) and the audience namespace differ,
        // so validation fails closed. The handler checks audience before issuer, so assert the
        // base SecurityTokenException to stay robust to validation order.
        act.Should().Throw<SecurityTokenException>();
    }

    [Fact]
    public void TokenFromInstallationA_AudienceRejectedUnderInstallationB()
    {
        // Isolate the audience axis: skip issuer validation so the audience-namespace check is what fails.
        var tokenA = MintFor("acme", Tier.Consumer);
        var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };

        var ps = ParamsFor("n1");
        ps.ValidateIssuer = false;

        var act = () => handler.ValidateToken(tokenA, ps, out _);

        act.Should().Throw<SecurityTokenInvalidAudienceException>(
            "acme:consumer is not in installation n1's accepted audience set");
    }

    [Fact]
    public void TokenFromInstallation_ValidatesUnderItsOwnIdentity()
    {
        var token = MintFor("n1", Tier.Platform);
        var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };

        var principal = handler.ValidateToken(token, ParamsFor("n1"), out var validated);

        principal.FindFirst(JwtRegisteredClaimNames.Sub)!.Value.Should().Be("user-1");
        ((JwtSecurityToken)validated).Audiences.Should().Contain("n1:platform");
    }
}
