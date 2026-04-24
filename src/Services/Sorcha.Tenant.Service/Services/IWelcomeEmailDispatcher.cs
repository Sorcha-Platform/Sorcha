// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Tenant.Service.Models;

namespace Sorcha.Tenant.Service.Services;

/// <summary>
/// Owns the one-shot welcome-email semantics: sends exactly one welcome per user across
/// the lifetime of the account, regardless of how many times the verify-success or
/// first-login trigger fires. Idempotent and non-throwing — a failed send is logged
/// but MUST NOT block the calling authentication flow (FR-020).
/// </summary>
public interface IWelcomeEmailDispatcher
{
    /// <summary>
    /// Sends the appropriate welcome email if the user is eligible (email verified AND
    /// welcome not previously sent). Uses an atomic <c>ExecuteUpdateAsync</c> reservation
    /// with <c>WHERE WelcomeSentAt IS NULL AND EmailVerified = TRUE</c> so concurrent
    /// triggers cannot both send — the losing writer affects zero rows and returns.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Reservation-first ordering.</b> <see cref="PlatformUser.WelcomeSentAt"/> is
    /// persisted <i>before</i> the email send. If the reservation succeeds but the
    /// subsequent send throws (SMTP outage, ACS timeout, etc.), the reservation is
    /// DELIBERATELY kept to prevent a future trigger from double-sending. Operators
    /// reconcile missed sends via the structured error log entry written at the send-
    /// failure path. This is the explicit tradeoff: at most one welcome per user,
    /// possibly zero under infrastructure failure.
    /// </para>
    /// <para>
    /// <b>Non-throwing.</b> All failure paths (context build, reservation, send)
    /// swallow exceptions after logging. Authentication flows must proceed regardless
    /// of whether the welcome email was delivered.
    /// </para>
    /// </remarks>
    Task SendIfPendingAsync(PlatformUser user, CancellationToken ct);
}
