// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Claims;

using FluentAssertions;

using Sorcha.Blueprint.Service.Endpoints;
using Sorcha.ServiceClients.Auth;

using Xunit;

namespace Sorcha.Blueprint.Service.Tests.Endpoints;

/// <summary>
/// The rehearsal start endpoint captures who is rehearsing into
/// <c>RehearsalSession.StartedByPlatformUserId</c>. Post-#1284 that value is no longer just an
/// attribution label — <see cref="Sorcha.Blueprint.Service.Services.Implementation.RehearsalOrchestrationService"/>
/// mints it as the <c>platform_user_id</c> claim on the synthetic caller principal, and
/// <c>ActionExecutionService</c> feeds that straight into <c>IPlatformUserClaimsClient.ResolveAsync</c>
/// to resolve x-claim-source bindings.
///
/// <para>
/// <c>sub</c> / <c>NameIdentifier</c> is <c>UserIdentity.Id</c>; <c>platform_user_id</c> is
/// <c>PlatformUser.Id</c>. <c>RegistrationService</c> generates the two independently (separate
/// <c>Guid.NewGuid()</c> calls linked only by a FK), so they differ for every real user — they
/// coincide *only* for the seeded default admin, where <c>DatabaseInitializer</c> deliberately reuses
/// <c>WellKnownIds.DefaultAdminUserId</c> for both rows.
/// </para>
///
/// <para>
/// Reading <c>sub</c> here therefore looks correct on a seeded dev box and 404s for everyone else:
/// <c>ResolveAsync</c> finds no <c>PlatformUser</c> with that id and the step dies with "Could not
/// confirm your account details with the platform" — trading #1284's exception for a new one on
/// exactly the claim-source blueprints the fix targets (the AIAS assured-identity template among
/// them). The destination field is named <c>RehearsedByPlatformUserId</c>, so PlatformUser.Id was
/// always the intent.
/// </para>
/// </summary>
public class RehearsalInitiatorIdentityTests
{
    private static ClaimsPrincipal PrincipalWith(params Claim[] claims) =>
        new(new ClaimsIdentity(claims, authenticationType: "test"));

    [Fact]
    public void ResolvePlatformUserId_TokenCarriesBothClaims_PrefersPlatformUserId()
    {
        var platformUserId = Guid.NewGuid();
        var userIdentityId = Guid.NewGuid();

        // Shape of a real Feature 136 human-tier token: distinct sub and platform_user_id.
        var principal = PrincipalWith(
            new Claim(ClaimTypes.NameIdentifier, userIdentityId.ToString()),
            new Claim(TokenClaimConstants.PlatformUserId, platformUserId.ToString()));

        var resolved = RehearsalEndpoints.ResolvePlatformUserId(principal);

        resolved.Should().Be(platformUserId,
            "the synthetic rehearsal caller mints this as platform_user_id, which resolves against " +
            "PlatformUser rows — sub is a UserIdentity id and would 404");
        resolved.Should().NotBe(userIdentityId);
    }

    [Fact]
    public void ResolvePlatformUserId_PlatformUserIdOnJwtSubShape_PrefersPlatformUserId()
    {
        // Raw JWT claim names (no inbound mapping) — "sub" rather than the mapped NameIdentifier URI.
        var platformUserId = Guid.NewGuid();
        var userIdentityId = Guid.NewGuid();

        var principal = PrincipalWith(
            new Claim("sub", userIdentityId.ToString()),
            new Claim(TokenClaimConstants.PlatformUserId, platformUserId.ToString()));

        RehearsalEndpoints.ResolvePlatformUserId(principal).Should().Be(platformUserId);
    }

    [Fact]
    public void ResolvePlatformUserId_NoPlatformUserIdClaim_FallsBackToSub()
    {
        // Tokens minted before Feature 136 carried no platform_user_id. Falling back to sub is the
        // best available guess and is what the seeded admin needs (both ids are the same there).
        var userIdentityId = Guid.NewGuid();

        var principal = PrincipalWith(new Claim(ClaimTypes.NameIdentifier, userIdentityId.ToString()));

        RehearsalEndpoints.ResolvePlatformUserId(principal).Should().Be(userIdentityId);
    }

    [Fact]
    public void ResolvePlatformUserId_MalformedPlatformUserIdClaim_FallsBackToSub()
    {
        var userIdentityId = Guid.NewGuid();

        var principal = PrincipalWith(
            new Claim(TokenClaimConstants.PlatformUserId, "not-a-guid"),
            new Claim(ClaimTypes.NameIdentifier, userIdentityId.ToString()));

        RehearsalEndpoints.ResolvePlatformUserId(principal).Should().Be(userIdentityId);
    }

    [Fact]
    public void ResolvePlatformUserId_NoUsableClaims_ReturnsEmpty()
    {
        RehearsalEndpoints.ResolvePlatformUserId(PrincipalWith()).Should().Be(Guid.Empty);
    }
}
