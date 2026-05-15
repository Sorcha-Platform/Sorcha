// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.UI.Core.Services.User.Enrolment;

/// <summary>
/// Surfaces the pairing-completion signal (Feature 126 cross-device coordination).
/// Wraps the SignalR hub event with a polling fallback so the council page
/// can advance to the form whether the websocket is healthy or not.
/// </summary>
public interface IEnrolPairingSignal
{
    /// <summary>
    /// Fires when a wallet device has been registered for the bound platform
    /// user, regardless of which transport carried the signal.
    /// </summary>
    event Func<Guid, Task>? OnDeviceEnrolled;

    /// <summary>
    /// Fires when the polling fallback engages (after 2 s with no successful
    /// SignalR connection) — useful for diagnostics and connection-state UI.
    /// </summary>
    event Action? OnFallbackEngaged;

    /// <summary>
    /// Fires when neither path has surfaced a signal within 60 s — the council
    /// page should render the manual-recovery affordance (FR-016 / SC-005).
    /// </summary>
    event Action? OnManualRecoveryRequired;

    /// <summary>
    /// Starts listening for pairing-completion signals for
    /// <paramref name="platformUserId"/>.
    /// </summary>
    Task StartAsync(Guid platformUserId, CancellationToken ct);

    /// <summary>Stops listening and disposes any transport resources.</summary>
    Task StopAsync();
}
