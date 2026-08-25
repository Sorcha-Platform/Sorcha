// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Sorcha.ServiceClients.Auth;
using Sorcha.Wallet.Service.Endpoints;
using Xunit;

namespace Sorcha.Wallet.Service.Tests.Endpoints;

/// <summary>
/// The boundary deciding who may create a wallet owned by an ORGANISATION (#1525).
/// </summary>
/// <remarks>
/// <para>
/// This gate decides who ends up holding an organisation's BIP39 recovery phrase. It is shown once
/// at creation and never stored, so the person who creates the wallet is the only person who will
/// ever have it — which is precisely why the platform stopped minting these server-side, where the
/// phrase was generated with nobody present to receive it and silently discarded.
/// </para>
/// <para>
/// So the two things asserted here are not ceremony: creating an organisation's wallet must require
/// being <i>in</i> that organisation, and being an <i>administrator</i> of it.
/// </para>
/// </remarks>
public class OrganizationWalletOwnerGateTests
{
    private static readonly Guid Org = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OtherOrg = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void AdministratorOfThatOrg_IsAllowed()
    {
        var refusal = WalletEndpoints.CheckOrganizationWalletOwner(User(Org, "Administrator"), Org);

        refusal.Should().BeNull();
    }

    [Fact]
    public void AdministratorOfADifferentOrg_IsRefused()
    {
        // Org ids are not secret. Without this, an administrator of any organisation could create a
        // wallet owned by another one and hold its recovery phrase.
        var refusal = WalletEndpoints.CheckOrganizationWalletOwner(User(OtherOrg, "Administrator"), Org);

        ShouldBeForbidden(refusal);
    }

    [Fact]
    public void NonAdministratorMemberOfThatOrg_IsRefused()
    {
        var refusal = WalletEndpoints.CheckOrganizationWalletOwner(User(Org, "Member"), Org);

        ShouldBeForbidden(refusal);
    }

    [Fact]
    public void CallerWithNoOrganisationClaim_IsRefused()
    {
        var refusal = WalletEndpoints.CheckOrganizationWalletOwner(
            new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, "Administrator")], "test")),
            Org);

        ShouldBeForbidden(refusal);
    }

    [Fact]
    public void PlatformSystemAdmin_IsRefusedLikeAnyoneElse()
    {
        // The deliberate exception to the platform admin's usual reach. Everywhere else a
        // SystemAdmin can act across organisations; here they must not, because the thing being
        // handed out is the organisation's own secret and the platform has no business holding it.
        var systemAdmin = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(TokenClaimConstants.OrgId, OtherOrg.ToString()),
            new Claim(ClaimTypes.Role, "SystemAdmin"),
            new Claim(ClaimTypes.Role, "Administrator")
        ], "test"));

        var refusal = WalletEndpoints.CheckOrganizationWalletOwner(systemAdmin, Org);

        ShouldBeForbidden(refusal);
    }

    private static ClaimsPrincipal User(Guid orgId, string role) =>
        new(new ClaimsIdentity(
        [
            new Claim(TokenClaimConstants.OrgId, orgId.ToString()),
            new Claim(ClaimTypes.Role, role)
        ], "test"));

    private static void ShouldBeForbidden(IResult? refusal)
    {
        refusal.Should().NotBeNull();
        refusal.Should().BeAssignableTo<IStatusCodeHttpResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }
}
