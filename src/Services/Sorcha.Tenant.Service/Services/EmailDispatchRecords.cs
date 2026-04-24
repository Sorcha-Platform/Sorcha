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
/// Context passed by <c>WelcomeEmailDispatcher</c> when dispatching a welcome email.
/// <see cref="InvitingOrganization"/> is required when <see cref="Variant"/> is
/// <see cref="WelcomeVariant.Invited"/>; null for <see cref="WelcomeVariant.Public"/>.
/// </summary>
public sealed record WelcomeDispatchContext(
    PlatformUser User,
    WelcomeVariant Variant,
    Organization? InvitingOrganization);

/// <summary>Which welcome-email template variant to render.</summary>
public enum WelcomeVariant
{
    /// <summary>Public self-signup — recovery-phrase advance-warning content.</summary>
    Public,

    /// <summary>User joined via organisation invitation — org-branded, org-scoped content.</summary>
    Invited,
}
