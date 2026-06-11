// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Tenant.Service.Models;

namespace Sorcha.Tenant.Service.Services;

/// <summary>Input to <see cref="ITransactionalEmailService.SendVerificationAsync"/>.</summary>
public sealed record VerifyEmailDispatch(
    string ToEmail,
    string DisplayName,
    string VerifyUrl,
    int ExpiresInHours);

/// <summary>
/// Input to <see cref="ITransactionalEmailService.SendInvitationAsync"/>. Carries the
/// inviting organisation so the branding resolver can apply the org's logo/colour/name.
/// </summary>
public sealed record InviteEmailDispatch(
    string ToEmail,
    string InviterName,
    Organization InvitingOrganization,
    string RoleDisplayName,
    string AcceptUrl,
    int ExpiresInDays);

/// <summary>Input to <see cref="ITransactionalEmailService.SendPasswordResetAsync"/>.</summary>
public sealed record ResetPasswordDispatch(
    string ToEmail,
    string DisplayName,
    string ResetUrl,
    int ExpiresInMinutes);

/// <summary>
/// Input to <see cref="ITransactionalEmailService.SendPairingResumptionAsync"/>.
/// Feature 128 US2 — the "Email me a link" affordance from the desktop
/// handoff page (sub-PR B3). The link reopens /setup/add-device on whatever
/// device the citizen taps the email on.
/// </summary>
public sealed record PairingResumptionDispatch(
    string ToEmail,
    string DisplayName,
    string ResumptionUrl,
    int ExpiresInHours);

/// <summary>Input to <see cref="ITransactionalEmailService.SendTwoFactorCodeAsync"/> (Feature 150). Always Sorcha-branded.</summary>
public sealed record TwoFactorCodeDispatch(
    string ToEmail,
    string DisplayName,
    string Code,
    int ExpiresInMinutes);

/// <summary>
/// Input to <see cref="ITransactionalEmailService.SendSecurityChangeAsync"/> (Feature 150) — the
/// always-notify alert sent on every account-security change. Always Sorcha-branded (never per-org):
/// a security alert must carry the platform's identity, not an org's.
/// </summary>
public sealed record SecurityChangeDispatch(
    string ToEmail,
    string DisplayName,
    string Title,
    string Summary,
    string ManageUrl);

/// <summary>
/// Context passed by <c>WelcomeEmailDispatcher</c> when dispatching a welcome email.
/// <see cref="InvitingOrganization"/> and <see cref="InvitedRole"/> are required when
/// <see cref="Variant"/> is <see cref="WelcomeVariant.Invited"/>; both are null for
/// <see cref="WelcomeVariant.Public"/>.
/// </summary>
public sealed record WelcomeDispatchContext(
    PlatformUser User,
    WelcomeVariant Variant,
    Organization? InvitingOrganization,
    string? InvitedRole);

/// <summary>Which welcome-email template variant to render.</summary>
public enum WelcomeVariant
{
    /// <summary>Public self-signup — recovery-phrase advance-warning content.</summary>
    Public,

    /// <summary>User joined via organisation invitation — org-branded, org-scoped content.</summary>
    Invited,
}
