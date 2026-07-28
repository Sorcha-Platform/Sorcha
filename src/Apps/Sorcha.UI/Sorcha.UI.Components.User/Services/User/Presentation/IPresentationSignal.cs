// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.UI.Core.Services.User.Presentation;

/// <summary>
/// Feature 127 — surfaces the presentation-outcome signal a council page
/// subscribes to after submitting a credential-gated starting action. Wraps
/// the BlueprintHub <c>PresentationOutcomeReady</c> event with a polling
/// fallback against F111's existing <c>GET /api/presentations/{id}/status</c>
/// endpoint so the council page can advance regardless of websocket health.
/// </summary>
/// <remarks>
/// Cadence (mirrors F126's <c>IEnrolPairingSignal</c>):
/// <list type="bullet">
///   <item><description>2 s hub-connect window before polling engages</description></item>
///   <item><description>3 s polling cadence on F111's <c>/status</c> endpoint</description></item>
///   <item><description>60 s ceiling before the manual-recovery affordance fires</description></item>
/// </list>
/// </remarks>
public interface IPresentationSignal
{
    /// <summary>
    /// Fires when the F111 lifecycle reaches a terminal outcome state — success,
    /// decline, or abandoned — regardless of which transport carried the signal.
    /// The outcome kind is carried on <see cref="PresentationSignalOutcome"/>;
    /// the council page branches on the kind to decide whether to fetch
    /// disclosed claims (success) or render an error state (decline / abandoned).
    /// </summary>
    event Func<PresentationSignalOutcome, Task>? OnOutcomeReady;

    /// <summary>
    /// Fires when the polling fallback engages (after 2 s with no successful
    /// SignalR connection). Useful for diagnostics and connection-state UI.
    /// </summary>
    event Action? OnFallbackEngaged;

    /// <summary>
    /// Fires when neither transport has surfaced a signal within 60 s — the
    /// council page should render the manual-recovery affordance.
    /// </summary>
    event Action? OnManualRecoveryRequired;

    /// <summary>
    /// Fires when the lifecycle reports it holds no such request — repeated 404s from the status
    /// endpoint. Permanent: no amount of further waiting can succeed, so the consumer should stop
    /// and say so rather than let the request run out its clock and report an expiry.
    /// </summary>
    event Action? OnRequestUnreachable;

    /// <summary>
    /// Starts listening for outcome signals on the given presentation request.
    /// </summary>
    Task StartAsync(Guid presentationRequestId, CancellationToken ct);

    /// <summary>Stops listening and disposes transport resources.</summary>
    Task StopAsync();
}

/// <summary>
/// Surfaced to handlers of <see cref="IPresentationSignal.OnOutcomeReady"/>.
/// Opaque IDs and the kind only; disclosed claims (on success) are fetched
/// separately via the F127 claims-fetch endpoint with the
/// <c>ClaimsFetchToken</c> returned at initiation.
/// </summary>
/// <param name="PresentationRequestId">The presentation that completed.</param>
/// <param name="Kind">Terminal kind: <c>"success"</c> / <c>"decline"</c> / <c>"abandoned"</c> / <c>"abandoned-with-late-outcome"</c>.</param>
public sealed record PresentationSignalOutcome(Guid PresentationRequestId, string Kind);
