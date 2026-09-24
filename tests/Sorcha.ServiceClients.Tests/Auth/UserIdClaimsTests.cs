// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Claims;
using Sorcha.ServiceClients.Auth;
using Sorcha.Tenant.Models.Identity;

namespace Sorcha.ServiceClients.Tests.Auth;

/// <summary>
/// Issue #1709 — the one home for reading a person id out of a token as a typed id. The two claims
/// must never be conflated: <c>platform_user_id</c> is a <see cref="PlatformUserId"/>, <c>sub</c> is a
/// <see cref="UserIdentityId"/>, and neither reader falls back to the other.
/// </summary>
public class UserIdClaimsTests
{
    private static readonly Guid PlatformGuid = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid IdentityGuid = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static ClaimsPrincipal Principal(params Claim[] claims) =>
        new(new ClaimsIdentity(claims, "test"));

    [Fact]
    public void GetPlatformUserId_ReadsPlatformUserIdClaim()
    {
        var principal = Principal(
            new Claim(TokenClaimConstants.PlatformUserId, PlatformGuid.ToString()),
            new Claim("sub", IdentityGuid.ToString()));

        principal.GetPlatformUserId().Should().Be(new PlatformUserId(PlatformGuid));
    }

    [Fact]
    public void GetPlatformUserId_OnlySub_ReturnsNull_NeverFallsBackToSub()
    {
        // The #1703 defect: `sub` was read where platform_user_id belonged. The reader must not
        // quietly paper over a missing claim with the other kind of id.
        var principal = Principal(
            new Claim("sub", IdentityGuid.ToString()),
            new Claim(ClaimTypes.NameIdentifier, IdentityGuid.ToString()));

        principal.GetPlatformUserId().Should().BeNull();
    }

    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public void GetPlatformUserId_UnusableValue_ReturnsNull(string raw)
    {
        Principal(new Claim(TokenClaimConstants.PlatformUserId, raw)).GetPlatformUserId().Should().BeNull();
    }

    [Fact]
    public void GetUserIdentityId_PrefersNameIdentifier_ThenSub()
    {
        Principal(new Claim(ClaimTypes.NameIdentifier, IdentityGuid.ToString()))
            .GetUserIdentityId().Should().Be(new UserIdentityId(IdentityGuid));

        Principal(new Claim("sub", IdentityGuid.ToString()))
            .GetUserIdentityId().Should().Be(new UserIdentityId(IdentityGuid));
    }

    [Fact]
    public void GetUserIdentityId_OnlyPlatformUserId_ReturnsNull()
    {
        Principal(new Claim(TokenClaimConstants.PlatformUserId, PlatformGuid.ToString()))
            .GetUserIdentityId().Should().BeNull();
    }

    [Fact]
    public void Readers_NullPrincipal_ReturnNull()
    {
        ClaimsPrincipal? principal = null;
        principal.GetPlatformUserId().Should().BeNull();
        principal.GetUserIdentityId().Should().BeNull();
    }
}
