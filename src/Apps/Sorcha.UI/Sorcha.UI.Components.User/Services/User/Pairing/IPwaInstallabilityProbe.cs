// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.UI.Core.Services.User.Pairing;

/// <summary>
/// Three-verdict probe used by the F128 desktop handoff to decide
/// between rendering the QR variant (Story 2) or the install-flavoured
/// variant (Story 3) for the same-phone PWA install path.
/// </summary>
public enum PwaInstallabilityVerdict
{
    /// <summary>
    /// Cannot install — desktop browser, in-app browser, or a mobile
    /// browser that has no install affordance. Render the QR variant.
    /// </summary>
    CannotInstall = 0,

    /// <summary>
    /// Browser captured the <c>beforeinstallprompt</c> event (Android
    /// Chrome / Edge / Samsung Internet). Install can be triggered
    /// programmatically by invoking the deferred prompt.
    /// </summary>
    CanInstallProgrammatically = 1,

    /// <summary>
    /// iOS Safari ≥16.4 — PWA-installable via Add-to-Home-Screen but
    /// there is no programmatic install API. Render manual instructions
    /// ("Tap Share, then Add to Home Screen").
    /// </summary>
    CanInstallManually = 2,
}

/// <summary>
/// Determines whether the current browser can install the wallet PWA.
/// Result is settled once on initialisation and surfaced as a single
/// <see cref="PwaInstallabilityVerdict"/> for the surface to switch on.
/// </summary>
/// <remarks>
/// Per F128 research R2 — listens for the <c>beforeinstallprompt</c>
/// event for ~500ms after load (the empirical window after which
/// Chromium fires it if eligibility checks pass), then falls back to
/// UA-string detection for iOS Safari ≥16.4. The 500ms window is a
/// trade-off: longer delays the page render, shorter risks missing
/// the event.
/// </remarks>
public interface IPwaInstallabilityProbe
{
    /// <summary>
    /// Probes the current browser. Idempotent — repeat calls return
    /// the same verdict without re-running detection.
    /// </summary>
    Task<PwaInstallabilityVerdict> ProbeAsync(CancellationToken ct = default);

    /// <summary>
    /// Triggers the deferred install prompt captured during
    /// <see cref="ProbeAsync"/>. Only valid when the verdict was
    /// <see cref="PwaInstallabilityVerdict.CanInstallProgrammatically"/>.
    /// Returns true if the citizen accepted the install, false on
    /// dismissal or any failure.
    /// </summary>
    Task<bool> TriggerInstallAsync(CancellationToken ct = default);
}
