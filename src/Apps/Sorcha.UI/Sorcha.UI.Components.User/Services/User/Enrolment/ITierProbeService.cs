// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.UI.Core.Services.User.Enrolment;

/// <summary>
/// The three citizen tiers used by Feature 126's enrolment gate. Derived from
/// <c>GET /api/auth/whoami</c> and <c>GET /api/me/devices</c> on every visit;
/// never persisted.
/// </summary>
public enum CitizenTier
{
    /// <summary>Visitor is signed in and has at least one active wallet device — render the form directly.</summary>
    FastPath = 1,

    /// <summary>Visitor is signed in but has no active wallet device — render the wallet-pairing surface.</summary>
    MiniGate = 2,

    /// <summary>Visitor has no Sorcha account — render the preflight signup surface.</summary>
    ColdStart = 3,
}

/// <summary>
/// Probes the platform for the visiting citizen's tier. Used by the
/// <c>EnrolGateComponent</c> on render.
/// </summary>
public interface ITierProbeService
{
    /// <summary>
    /// Probes <c>whoami</c> and <c>/me/devices</c> and returns the citizen tier.
    /// Implementations should apply a short (≤200 ms) timeout on each probe
    /// so the gate component never blocks on a slow round-trip.
    /// </summary>
    Task<CitizenTier> ProbeAsync(CancellationToken ct);
}
