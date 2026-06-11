// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Tenant.Service.Models;

namespace Sorcha.Tenant.Service.Services;

/// <summary>
/// The server-authoritative assurance model for account-security operations (Feature 150).
/// Pure, static, and side-effect free — the single source of truth for:
/// <list type="bullet">
///   <item>the assurance <b>tier</b> of every sign-in method and step-up proof, and</item>
///   <item>the <b>floor rule</b>: a step-up proof may authorise a destructive/downgrade
///   operation on a method only when <c>proofTier &gt;= requiredTier</c>.</item>
/// </list>
/// This generalises the last-method floor (<see cref="IAuthMethodService.WouldRemovingLeaveZeroAsync"/>)
/// — the two are complementary: the last-method floor stops you removing your only sign-in method;
/// this stops a weaker factor from removing a stronger one. Both are enforced server-side; the UI
/// only reflects the resulting <c>CanRemove</c> / <c>RequiredProofTier</c> flags.
/// </summary>
/// <remarks>
/// Design contract: <c>specs/150-account-security/contracts/floor-rule-policy.md</c>.
/// <para>
/// <b>T061 resolved (2026-06-11) — the password is <see cref="AuthAssuranceTier.Basic"/> everywhere.</b>
/// A password is a phishable knowledge factor, so it is treated as Basic both as a sign-in method
/// (badge) and as a step-up proof. By the floor's own rule (<i>required tier = the tier of the thing
/// being removed/weakened</i>) the password's own operations — change / remove — are therefore
/// <b>Basic</b>-gated too: you re-enter your password to change it, with no dead-end for password-only
/// users. The guarantee that matters is preserved and strengthened: a Basic proof (the password, and
/// the future Email/SMS OTP) can <b>never</b> disable TOTP (<see cref="ScopedOperation.Disable2Fa"/>,
/// Strong) or remove a passkey (Strongest). Consequence vs the original contract: a Basic factor can
/// now authorise a password change — equivalent to the existing email-reset flow, not a new exposure.
/// </para>
/// </remarks>
public static class AssurancePolicy
{
    /// <summary>
    /// The assurance tier of a step-up <b>proof</b> method.
    /// </summary>
    public static AuthAssuranceTier TierOfProof(ChallengeMethod method) => method switch
    {
        ChallengeMethod.Passkey => AuthAssuranceTier.Strongest, // phishing-resistant
        ChallengeMethod.Totp => AuthAssuranceTier.Strong,
        ChallengeMethod.Password => AuthAssuranceTier.Basic,    // T061 resolved: a password is a phishable knowledge factor
        ChallengeMethod.ReOAuth => AuthAssuranceTier.Strong,    // provider-controlled, proves current control
        ChallengeMethod.EmailOtp => AuthAssuranceTier.Basic,    // US2 — emailed one-time code
        ChallengeMethod.SmsOtp => AuthAssuranceTier.Basic,      // US3 — SMS one-time code
        _ => AuthAssuranceTier.Basic
    };

    /// <summary>
    /// The assurance tier a sign-in <b>method</b> represents — drives the UI badge and, on removal,
    /// "the assurance you would lose" (which is what sets the required proof tier).
    /// </summary>
    public static AuthAssuranceTier TierOfMethod(AuthMethodKind kind) => kind switch
    {
        AuthMethodKind.Passkey => AuthAssuranceTier.Strongest,
        AuthMethodKind.Password => AuthAssuranceTier.Basic,     // T061 resolved: a phishable knowledge factor
        AuthMethodKind.Social => AuthAssuranceTier.Basic,       // a delegated sign-in path of no particular strength
        _ => AuthAssuranceTier.Basic
    };

    /// <summary>
    /// The minimum proof tier required to authorise a destructive/downgrade operation.
    /// <para>
    /// <see cref="ScopedOperation.RemoveAuthMethod"/> is intentionally ambiguous in the enum — it
    /// covers <i>both</i> passkey revocation and social unlink — so the caller MUST supply the
    /// <paramref name="targetKind"/>. An unknown target <b>fails safe to Strongest</b>, never to a
    /// weaker tier, so a missing target can never accidentally lower the bar.
    /// </para>
    /// </summary>
    public static AuthAssuranceTier RequiredProofTier(ScopedOperation operation, AuthMethodKind? targetKind = null) => operation switch
    {
        // Removing a method requires a proof at least as strong as the method being removed.
        ScopedOperation.RemoveAuthMethod => targetKind switch
        {
            AuthMethodKind.Passkey => AuthAssuranceTier.Strongest, // only a passkey can remove a passkey
            AuthMethodKind.Social => AuthAssuranceTier.Basic,      // unlinking a social loses no strong factor
            _ => AuthAssuranceTier.Strongest                        // fail-safe: unknown target → strongest
        },
        // The password is Basic (T061), so by the floor's own rule its own operations are Basic-gated —
        // you re-enter your password to change/remove it, with no dead-end for password-only users.
        ScopedOperation.ChangePassword => AuthAssuranceTier.Basic,
        ScopedOperation.RemovePassword => AuthAssuranceTier.Basic,  // + the last-method floor still applies
        ScopedOperation.Disable2Fa => AuthAssuranceTier.Strong,     // losing TOTP loses Strong protection — a Basic proof can't
        ScopedOperation.SetPassword => AuthAssuranceTier.Basic,     // adding a credential weakens nothing — lowest gated bar
        _ => AuthAssuranceTier.Strongest                            // unknown operation fails safe
    };

    /// <summary>
    /// The floor rule in its purest form: a proof of <paramref name="proofTier"/> authorises an
    /// operation requiring <paramref name="requiredTier"/> iff it is at least as strong.
    /// </summary>
    public static bool CanAuthorize(AuthAssuranceTier proofTier, AuthAssuranceTier requiredTier)
        => proofTier >= requiredTier;

    /// <summary>
    /// Convenience composition: can <paramref name="proof"/> satisfy the step-up for
    /// <paramref name="operation"/> on the (optional) target method?
    /// </summary>
    public static bool CanProofSatisfy(ChallengeMethod proof, ScopedOperation operation, AuthMethodKind? targetKind = null)
        => CanAuthorize(TierOfProof(proof), RequiredProofTier(operation, targetKind));
}
