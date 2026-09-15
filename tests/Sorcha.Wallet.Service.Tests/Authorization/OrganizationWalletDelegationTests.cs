// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Claims;

using Sorcha.Wallet.Contracts.Constants;
using Sorcha.Wallet.Core.Domain;
using Sorcha.Wallet.Core.Domain.Entities;
using Sorcha.Wallet.Service.Authorization;

namespace Sorcha.Wallet.Service.Tests.Authorization;

/// <summary>
/// #1643: an organisation's signing wallet is owned by the organisation, so no person could sign
/// with it and <c>sorcha_register_create</c> could never succeed. A person may now sign with it only
/// under an active, scoped delegation AND while they are still an Administrator of that organisation.
/// Every refusal below is a way that authority could otherwise leak.
/// </summary>
public class OrganizationWalletDelegationTests
{
    private const string OrgId = "00000000-0000-0000-0000-00000000000a";
    private const string OtherOrgId = "00000000-0000-0000-0000-00000000000b";
    private const string AdminUser = "admin-platform-user";
    private const string OrgWallet = "ws1orgwallet";

    private static ClaimsPrincipal Caller(string userId, string? orgId, params string[] roles)
    {
        var claims = new List<Claim> { new("platform_user_id", userId) };
        if (orgId is not null)
        {
            claims.Add(new Claim("org_id", orgId));
        }

        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
    }

    private static ClaimsPrincipal OrgAdmin() => Caller(AdminUser, OrgId, "Administrator");

    private static WalletAccess Grant(
        string subject = AdminUser,
        AccessRight right = AccessRight.ReadWrite,
        List<string>? contexts = null,
        DateTime? expiresAt = null,
        DateTime? revokedAt = null) => new()
        {
            ParentWalletAddress = OrgWallet,
            Subject = subject,
            AccessRight = right,
            GrantedBy = AdminUser,
            AllowedDerivationContexts = contexts ?? [SorchaDerivationPaths.RegisterAttestation],
            ExpiresAt = expiresAt,
            RevokedAt = revokedAt
        };

    private static DelegatedSigningDecision Sign(
        ClaimsPrincipal? caller = null,
        string walletOwner = OrgId,
        WalletAccess? grant = null,
        string? path = SorchaDerivationPaths.RegisterAttestation) =>
        OrganizationWalletDelegation.EvaluateSigning(
            caller ?? OrgAdmin(), AdminUser, walletOwner, [grant ?? Grant()], path);

    // ---- allowed ----

    [Fact]
    public void EvaluateSigning_OrgAdministratorWithScopedGrant_AtAScopedContext_IsAllowed()
    {
        var grant = Grant();

        var decision = Sign(grant: grant);

        decision.Allowed.Should().BeTrue(decision.Reason);
        decision.Grant.Should().BeSameAs(grant);
    }

    [Fact]
    public void EvaluateSigning_TheRawBip44PathOfAScopedContext_IsAllowed()
    {
        // A caller may name the same key by its BIP44 path; scope is about the KEY, not the spelling.
        Sign(path: SorchaDerivationPaths.RegisterAttestationPath).Allowed.Should().BeTrue();
    }

    // ---- refused: who the caller is ----

    [Fact]
    public void EvaluateSigning_CallerIsNoLongerAnAdministrator_IsRefused()
    {
        // A grant outliving its holder's role would be a standing key with no owner.
        var decision = Sign(caller: Caller(AdminUser, OrgId, "Designer"));

        decision.Allowed.Should().BeFalse();
        decision.Reason.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void EvaluateSigning_AdministratorOfAnotherOrganisation_IsRefused()
    {
        Sign(caller: Caller(AdminUser, OtherOrgId, "Administrator")).Allowed.Should().BeFalse();
    }

    [Fact]
    public void EvaluateSigning_CallerWithNoOrganisation_IsRefused()
    {
        Sign(caller: Caller(AdminUser, orgId: null, "Administrator")).Allowed.Should().BeFalse();
    }

    [Fact]
    public void EvaluateSigning_PersonalWalletNotOwnedByAnOrganisation_IsRefused()
    {
        // Delegated signing is introduced for organisation wallets only.
        Sign(walletOwner: "some-other-platform-user").Allowed.Should().BeFalse();
    }

    // ---- refused: the grant ----

    [Fact]
    public void EvaluateSigning_GrantBelongsToSomeoneElse_IsRefused()
    {
        Sign(grant: Grant(subject: "another-admin")).Allowed.Should().BeFalse();
    }

    [Fact]
    public void EvaluateSigning_ReadOnlyGrant_IsRefused()
    {
        Sign(grant: Grant(right: AccessRight.ReadOnly)).Allowed.Should().BeFalse();
    }

    [Fact]
    public void EvaluateSigning_ExpiredGrant_IsRefused()
    {
        Sign(grant: Grant(expiresAt: DateTime.UtcNow.AddDays(-1))).Allowed.Should().BeFalse();
    }

    [Fact]
    public void EvaluateSigning_RevokedGrant_IsRefused()
    {
        Sign(grant: Grant(revokedAt: DateTime.UtcNow.AddMinutes(-1))).Allowed.Should().BeFalse();
    }

    [Fact]
    public void EvaluateSigning_UnscopedGrant_IsRefused()
    {
        // An unscoped grant on an organisation wallet would reach its governance, issuance and every
        // other key: the #1397 signing oracle with a user token.
        var nullScope = Grant();
        nullScope.AllowedDerivationContexts = null;

        Sign(grant: nullScope).Allowed.Should().BeFalse();
        Sign(grant: Grant(contexts: [])).Allowed.Should().BeFalse();
    }

    // ---- refused: what is being signed ----

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void EvaluateSigning_NoDerivationPath_IsRefused(string? path)
    {
        // No path means the wallet's default key, which no scope can describe. The reason is asserted
        // because the next guard (unknown context) would also refuse a blank path, for the wrong reason.
        var decision = Sign(path: path);

        decision.Allowed.Should().BeFalse();
        decision.Reason.Should().Contain("must name a derivation context");
    }

    [Fact]
    public void EvaluateSigning_ContextOutsideTheGrantScope_IsRefused()
    {
        Sign(path: SorchaDerivationPaths.DocketSigning).Allowed.Should().BeFalse();
    }

    [Fact]
    public void EvaluateSigning_RawPathOutsideTheGrantScope_IsRefused()
    {
        Sign(path: SorchaDerivationPaths.RegisterControlPath).Allowed.Should().BeFalse();
    }

    [Fact]
    public void EvaluateSigning_MistypedContextName_IsRefused()
    {
        // A mistyped context does not throw downstream; it derives a different valid key.
        Sign(path: "sorcha:register-atestation").Allowed.Should().BeFalse();
    }

    // ---- grant validation ----

    [Fact]
    public void ValidateGrant_ByOrgAdministrator_ScopedReadWrite_IsValid()
    {
        OrganizationWalletDelegation.ValidateGrant(
            AccessRight.ReadWrite, [SorchaDerivationPaths.RegisterAttestation], viaOrganisationAdministrator: true)
            .Should().BeNull();
    }

    [Fact]
    public void ValidateGrant_ByOrgAdministrator_OwnerRight_IsRejected()
    {
        OrganizationWalletDelegation.ValidateGrant(
            AccessRight.Owner, [SorchaDerivationPaths.RegisterAttestation], viaOrganisationAdministrator: true)
            .Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void ValidateGrant_ByOrgAdministrator_WithoutScope_IsRejected()
    {
        OrganizationWalletDelegation.ValidateGrant(AccessRight.ReadWrite, null, viaOrganisationAdministrator: true)
            .Should().NotBeNullOrWhiteSpace();
        OrganizationWalletDelegation.ValidateGrant(AccessRight.ReadWrite, [], viaOrganisationAdministrator: true)
            .Should().NotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ValidateGrant_UnknownContext_IsRejected(bool viaOrgAdministrator)
    {
        OrganizationWalletDelegation.ValidateGrant(
            AccessRight.ReadWrite, ["sorcha:register-atestation"], viaOrgAdministrator)
            .Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void ValidateGrant_RawBip44PathInsteadOfAContext_IsRejected()
    {
        // A raw path can name non-system slots; scope must be expressed in named contexts.
        OrganizationWalletDelegation.ValidateGrant(
            AccessRight.ReadWrite, [SorchaDerivationPaths.RegisterAttestationPath], viaOrganisationAdministrator: true)
            .Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void ValidateGrant_ByTheWalletOwner_Unscoped_RemainsValid()
    {
        // Personal-wallet grants by their owner are unchanged by #1643.
        OrganizationWalletDelegation.ValidateGrant(AccessRight.ReadWrite, null, viaOrganisationAdministrator: false)
            .Should().BeNull();
    }

    // ---- the grant seeded at organisation-wallet creation ----

    [Fact]
    public void CreatorGrant_IsScopedToRegisterAttestationOnly()
    {
        OrganizationWalletDelegation.CreatorGrantContexts
            .Should().Equal([SorchaDerivationPaths.RegisterAttestation],
                "the creator's grant exists for register creation; widening it must be a deliberate decision");
    }

    [Fact]
    public void CreatorGrant_Expires()
    {
        OrganizationWalletDelegation.CreatorGrantLifetime.Should().BeGreaterThan(TimeSpan.Zero);
        OrganizationWalletDelegation.CreatorGrantLifetime.Should().BeLessThanOrEqualTo(TimeSpan.FromDays(366));
    }
}
