// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Tenant.Service.Services;

/// <summary>
/// The single entry-point application code uses to send a transactional email.
/// Hides template names, model construction, and branding resolution from callers.
/// </summary>
/// <remarks>
/// Implementations are stateless: they render the appropriate template, delegate to
/// <see cref="IEmailSender"/>, and return. Persistence of lifecycle state (e.g.
/// welcome-sent marker) is the caller's responsibility (see
/// <c>WelcomeEmailDispatcher</c>).
/// </remarks>
public interface ITransactionalEmailService
{
    /// <summary>Sends the "confirm your email" verification message.</summary>
    Task SendVerificationAsync(VerifyEmailDispatch dispatch, CancellationToken ct = default);

    /// <summary>
    /// Sends an organisation invitation. Branding is resolved from
    /// <see cref="InviteEmailDispatch.InvitingOrganization"/>.
    /// </summary>
    Task SendInvitationAsync(InviteEmailDispatch dispatch, CancellationToken ct = default);

    /// <summary>Sends a password-reset link.</summary>
    Task SendPasswordResetAsync(ResetPasswordDispatch dispatch, CancellationToken ct = default);

    /// <summary>
    /// Sends a welcome email in the variant specified by
    /// <see cref="WelcomeDispatchContext.Variant"/>. Does NOT persist the welcome-sent
    /// marker — the calling <c>WelcomeEmailDispatcher</c> owns that responsibility.
    /// </summary>
    Task SendWelcomeAsync(WelcomeDispatchContext context, CancellationToken ct = default);

    /// <summary>
    /// Sends the F128 "Email me a link" pairing-resumption email — gives the
    /// citizen a magic-link they can tap on their phone to reopen the
    /// /setup/add-device handoff in an authenticated session.
    /// </summary>
    Task SendPairingResumptionAsync(PairingResumptionDispatch dispatch, CancellationToken ct = default);

    /// <summary>Sends a two-factor one-time code (Feature 150). Always Sorcha-branded.</summary>
    Task SendTwoFactorCodeAsync(TwoFactorCodeDispatch dispatch, CancellationToken ct = default);

    /// <summary>
    /// Sends a security-change alert (Feature 150 always-notify). Always Sorcha default branding —
    /// a security notification must never carry org branding.
    /// </summary>
    Task SendSecurityChangeAsync(SecurityChangeDispatch dispatch, CancellationToken ct = default);
}
