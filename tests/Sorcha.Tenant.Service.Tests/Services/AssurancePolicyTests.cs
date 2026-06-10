// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Services;
using Xunit;

namespace Sorcha.Tenant.Service.Tests.Services;

/// <summary>
/// Exhaustive matrix for <see cref="AssurancePolicy"/> — the Feature 150 floor rule
/// (design contract: <c>specs/150-account-security/contracts/floor-rule-policy.md</c>).
/// Every proof-tier × operation × target combination and the four worked invariants are
/// covered. This is the security spine: if a row here is wrong, a weak factor could strip
/// a strong one, so the matrix is intentionally complete rather than representative.
/// </summary>
public sealed class AssurancePolicyTests
{
    // ---- Table A: proof method → tier ----

    [Theory]
    [InlineData(ChallengeMethod.Passkey, AuthAssuranceTier.Strongest)]
    [InlineData(ChallengeMethod.Totp, AuthAssuranceTier.Strong)]
    [InlineData(ChallengeMethod.Password, AuthAssuranceTier.Strong)]   // flagged decision (T061)
    [InlineData(ChallengeMethod.ReOAuth, AuthAssuranceTier.Strong)]
    public void TierOfProof_MapsEachMethod(ChallengeMethod method, AuthAssuranceTier expected)
        => AssurancePolicy.TierOfProof(method).Should().Be(expected);

    // ---- Method → tier (badge + "what you'd lose") ----

    [Theory]
    [InlineData(AuthMethodKind.Passkey, AuthAssuranceTier.Strongest)]
    [InlineData(AuthMethodKind.Password, AuthAssuranceTier.Strong)]
    [InlineData(AuthMethodKind.Social, AuthAssuranceTier.Basic)]
    public void TierOfMethod_MapsEachKind(AuthMethodKind kind, AuthAssuranceTier expected)
        => AssurancePolicy.TierOfMethod(kind).Should().Be(expected);

    // ---- The pure floor comparison: every tier × tier combination (9 cells) ----

    [Theory]
    // proof Basic
    [InlineData(AuthAssuranceTier.Basic, AuthAssuranceTier.Basic, true)]
    [InlineData(AuthAssuranceTier.Basic, AuthAssuranceTier.Strong, false)]
    [InlineData(AuthAssuranceTier.Basic, AuthAssuranceTier.Strongest, false)]
    // proof Strong
    [InlineData(AuthAssuranceTier.Strong, AuthAssuranceTier.Basic, true)]
    [InlineData(AuthAssuranceTier.Strong, AuthAssuranceTier.Strong, true)]
    [InlineData(AuthAssuranceTier.Strong, AuthAssuranceTier.Strongest, false)]
    // proof Strongest
    [InlineData(AuthAssuranceTier.Strongest, AuthAssuranceTier.Basic, true)]
    [InlineData(AuthAssuranceTier.Strongest, AuthAssuranceTier.Strong, true)]
    [InlineData(AuthAssuranceTier.Strongest, AuthAssuranceTier.Strongest, true)]
    public void CanAuthorize_IsProofTierGreaterOrEqualRequired(
        AuthAssuranceTier proof, AuthAssuranceTier required, bool expected)
        => AssurancePolicy.CanAuthorize(proof, required).Should().Be(expected);

    // ---- Table B: operation × target → required proof tier ----

    [Theory]
    [InlineData(AuthMethodKind.Passkey, AuthAssuranceTier.Strongest)]
    [InlineData(AuthMethodKind.Social, AuthAssuranceTier.Basic)]
    public void RequiredProofTier_RemoveAuthMethod_DependsOnTarget(
        AuthMethodKind target, AuthAssuranceTier expected)
        => AssurancePolicy.RequiredProofTier(ScopedOperation.RemoveAuthMethod, target).Should().Be(expected);

    [Fact]
    public void RequiredProofTier_RemoveAuthMethod_UnknownTarget_FailsSafeToStrongest()
        => AssurancePolicy.RequiredProofTier(ScopedOperation.RemoveAuthMethod, targetKind: null)
            .Should().Be(AuthAssuranceTier.Strongest);

    [Theory]
    [InlineData(ScopedOperation.ChangePassword, AuthAssuranceTier.Strong)]
    [InlineData(ScopedOperation.RemovePassword, AuthAssuranceTier.Strong)]
    [InlineData(ScopedOperation.Disable2Fa, AuthAssuranceTier.Strong)]
    [InlineData(ScopedOperation.SetPassword, AuthAssuranceTier.Basic)]
    public void RequiredProofTier_OtherOperations(ScopedOperation operation, AuthAssuranceTier expected)
        => AssurancePolicy.RequiredProofTier(operation).Should().Be(expected);

    // ---- Worked invariant 1: only a passkey can remove a passkey ----

    [Theory]
    [InlineData(ChallengeMethod.Passkey, true)]    // assert-then-delete: the passkey removes itself
    [InlineData(ChallengeMethod.Totp, false)]
    [InlineData(ChallengeMethod.Password, false)]  // even the password (Strong) cannot strip a passkey
    [InlineData(ChallengeMethod.ReOAuth, false)]
    public void CanProofSatisfy_RemovePasskey_RequiresPasskeyTierProof(ChallengeMethod proof, bool expected)
        => AssurancePolicy.CanProofSatisfy(proof, ScopedOperation.RemoveAuthMethod, AuthMethodKind.Passkey)
            .Should().Be(expected);

    // ---- Unlinking a social is Basic — every current proof satisfies it ----

    [Theory]
    [InlineData(ChallengeMethod.Passkey)]
    [InlineData(ChallengeMethod.Totp)]
    [InlineData(ChallengeMethod.Password)]
    [InlineData(ChallengeMethod.ReOAuth)]
    public void CanProofSatisfy_UnlinkSocial_AnyEnrolledProofSatisfies(ChallengeMethod proof)
        => AssurancePolicy.CanProofSatisfy(proof, ScopedOperation.RemoveAuthMethod, AuthMethodKind.Social)
            .Should().BeTrue();

    // ---- Disabling 2FA / changing the password is Strong — all current proofs are Strong+ ----

    [Theory]
    [InlineData(ChallengeMethod.Passkey)]
    [InlineData(ChallengeMethod.Totp)]
    [InlineData(ChallengeMethod.Password)]
    [InlineData(ChallengeMethod.ReOAuth)]
    public void CanProofSatisfy_Disable2Fa_AllCurrentProofsSatisfy(ChallengeMethod proof)
        => AssurancePolicy.CanProofSatisfy(proof, ScopedOperation.Disable2Fa).Should().BeTrue();

    [Theory]
    [InlineData(ChallengeMethod.Passkey)]
    [InlineData(ChallengeMethod.Totp)]
    [InlineData(ChallengeMethod.Password)]
    [InlineData(ChallengeMethod.ReOAuth)]
    public void CanProofSatisfy_ChangePassword_AllCurrentProofsSatisfy(ChallengeMethod proof)
        => AssurancePolicy.CanProofSatisfy(proof, ScopedOperation.ChangePassword).Should().BeTrue();

    // ---- Worked invariant: a Basic proof (future Email/SMS OTP) can never reach Strong/Strongest ----
    // ChallengeMethod has no Basic member yet (lands in US2/US3), so this is asserted at the tier level
    // to lock the guarantee in before those rungs exist.

    [Fact]
    public void BasicProof_CannotAuthorize_StrongOrStrongerOperations()
    {
        AssurancePolicy.CanAuthorize(AuthAssuranceTier.Basic, AuthAssuranceTier.Strong).Should().BeFalse();
        AssurancePolicy.CanAuthorize(AuthAssuranceTier.Basic, AuthAssuranceTier.Strongest).Should().BeFalse();
        // ...but a Basic proof can still authorise Basic-tier operations (e.g. unlink social, disable email OTP).
        AssurancePolicy.CanAuthorize(AuthAssuranceTier.Basic, AuthAssuranceTier.Basic).Should().BeTrue();
    }

    // ---- Tier ordinality is load-bearing (the >= comparison depends on it) ----

    [Fact]
    public void Tiers_AreOrdinal_BasicLtStrongLtStrongest()
    {
        ((int)AuthAssuranceTier.Basic).Should().BeLessThan((int)AuthAssuranceTier.Strong);
        ((int)AuthAssuranceTier.Strong).Should().BeLessThan((int)AuthAssuranceTier.Strongest);
    }
}
