// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Services;

namespace Sorcha.Tenant.Service.Tests.Services;

/// <summary>
/// FR-010 proof-policy matrix for <see cref="ScopedOperation.LinkSocial"/> across five account
/// configurations (Feature 168, T021/T022).
///
/// The floor for <see cref="ScopedOperation.LinkSocial"/> is <see cref="AuthAssuranceTier.Strong"/>
/// (T022 decision). Consequence:
/// <list type="bullet">
///   <item>Passkey (Strongest ≥ Strong) — accepted.</item>
///   <item>TOTP (Strong ≥ Strong) — accepted.</item>
///   <item>ReOAuth (Strong ≥ Strong) — accepted.</item>
///   <item>Password (Basic &lt; Strong) — rejected (ProofTierInsufficient) for ALL account configs,
///   including password-only accounts. Password-only accounts get NoMethodAvailable at initiate
///   (400) — they must enrol TOTP or a passkey before linking a social identity.</item>
/// </list>
/// This is the security-conservative choice: it satisfies FR-010 cases 4 and 5 ("bare password
/// insufficient when 2FA enrolled") while erring on the side of requiring a strong factor for
/// a sensitive identity-linking operation. FR-010 case 3 ("password-only → password accepted")
/// is not achievable simultaneously with case 4 under a single static floor; the product decision
/// is to require at least TOTP or a passkey for social linking.
/// </summary>
public sealed class SocialLinkStepUpPolicyTests
{
    // ── Floor is Strong for LinkSocial ──────────────────────────────────────────

    [Fact]
    public void RequiredProofTier_LinkSocial_IsStrong()
        => AssurancePolicy.RequiredProofTier(ScopedOperation.LinkSocial)
            .Should().Be(AuthAssuranceTier.Strong);

    // ── Config 1: passkey enrolled → passkey accepted ──────────────────────────

    [Fact]
    public void Config_PasskeyEnrolled_PasskeyAccepted()
        => AssurancePolicy.CanProofSatisfy(ChallengeMethod.Passkey, ScopedOperation.LinkSocial)
            .Should().BeTrue("Passkey is Strongest ≥ Strong floor");

    // ── Config 2: linked social → re-auth accepted ─────────────────────────────

    [Fact]
    public void Config_LinkedSocial_ReOAuthAccepted()
        => AssurancePolicy.CanProofSatisfy(ChallengeMethod.ReOAuth, ScopedOperation.LinkSocial)
            .Should().BeTrue("ReOAuth is Strong ≥ Strong floor");

    // ── Config 3: password-only, no 2FA → NoMethodAvailable at initiate ────────

    [Fact]
    public void Config_PasswordOnly_PasswordInsufficient()
        => AssurancePolicy.CanProofSatisfy(ChallengeMethod.Password, ScopedOperation.LinkSocial)
            .Should().BeFalse(
                "Password is Basic < Strong floor; password-only accounts get NoMethodAvailable at initiate. "
                + "FR-010 case 3 ('password-only → password accepted') requires at least TOTP or a passkey "
                + "to be enrolled for social linking (security-conservative product decision).");

    // ── Config 4: password + 2FA ────────────────────────────────────────────────

    [Fact]
    public void Config_PasswordWith2Fa_BarePasswordYieldsProofTierInsufficient()
        => AssurancePolicy.CanProofSatisfy(ChallengeMethod.Password, ScopedOperation.LinkSocial)
            .Should().BeFalse("Password (Basic) < Strong floor → ProofTierInsufficient for 2FA-enrolled accounts");

    [Fact]
    public void Config_PasswordWith2Fa_TotpAccepted()
        => AssurancePolicy.CanProofSatisfy(ChallengeMethod.Totp, ScopedOperation.LinkSocial)
            .Should().BeTrue("TOTP is Strong ≥ Strong floor");

    // ── Config 5: password + 2FA + passkey ─────────────────────────────────────

    [Fact]
    public void Config_PasswordWith2FaAndPasskey_PasskeyAccepted()
        => AssurancePolicy.CanProofSatisfy(ChallengeMethod.Passkey, ScopedOperation.LinkSocial)
            .Should().BeTrue("Passkey is Strongest ≥ Strong floor");

    [Fact]
    public void Config_PasswordWith2FaAndPasskey_BarePasswordInsufficient()
        => AssurancePolicy.CanProofSatisfy(ChallengeMethod.Password, ScopedOperation.LinkSocial)
            .Should().BeFalse("Password (Basic) < Strong floor → ProofTierInsufficient");

    // ── Invariant: password is never sufficient for LinkSocial ─────────────────

    [Fact]
    public void Password_IsNeverSufficient_ForLinkSocial()
        => AssurancePolicy.CanProofSatisfy(ChallengeMethod.Password, ScopedOperation.LinkSocial)
            .Should().BeFalse("a phishable knowledge factor cannot authorise identity-linking at Strong tier");

    // ── Invariant: EmailOtp and SmsOtp (Basic) are not sufficient ──────────────

    [Theory]
    [InlineData(ChallengeMethod.EmailOtp)]
    [InlineData(ChallengeMethod.SmsOtp)]
    public void OtpChannels_AreNotSufficient_ForLinkSocial(ChallengeMethod method)
        => AssurancePolicy.CanProofSatisfy(method, ScopedOperation.LinkSocial)
            .Should().BeFalse($"{method} is Basic < Strong floor");
}
