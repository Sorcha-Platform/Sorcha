// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.UI.Core.Services.User.Pairing;

/// <summary>Which handoff path the F128 surface should lead with.</summary>
public enum PairingHandoffRoute
{
    /// <summary>Install the wallet PWA on the device the citizen is holding.</summary>
    InstallOnThisPhone = 0,

    /// <summary>
    /// Open the already-reachable wallet on this device. The same-device path for a mobile browser
    /// that offers no install affordance — FR-008's case, and the one that was missing.
    /// </summary>
    OpenOnThisDevice = 1,

    /// <summary>Desktop: mint a QR and let the citizen scan it with their phone.</summary>
    ScanWithYourPhone = 2,
}

/// <summary>
/// Chooses the handoff path from the two facts that actually matter: can this browser install the
/// PWA, and is this a phone.
/// </summary>
/// <remarks>
/// <para>
/// Issue #1270: the surface only ever had ONE axis. <see cref="IPwaInstallabilityProbe"/> answers
/// "can this browser install the PWA?" and the surface treated "no" as "this is a desktop".
/// <b>Mobile-but-not-installable is neither.</b> An Android citizen who already installed the wallet
/// (so <c>beforeinstallprompt</c> never fires again), anyone in an in-app browser, and every
/// non-Safari iOS browser all land there — and were shown a QR code to scan with the phone they were
/// already holding, with the same-device route buried below two screenfuls of it.
/// </para>
/// <para>
/// A positive install verdict is NOT overridden by the mobile axis: Chromium on a desktop genuinely
/// does fire <c>beforeinstallprompt</c>, and installing there is a real outcome.
/// </para>
/// </remarks>
public static class PairingHandoffRouting
{
    /// <summary>Picks the route. Unknown verdicts on mobile stay on a same-device path by design.</summary>
    public static PairingHandoffRoute Choose(PwaInstallabilityVerdict verdict, bool isMobile) =>
        verdict switch
        {
            PwaInstallabilityVerdict.CanInstallProgrammatically or
            PwaInstallabilityVerdict.CanInstallManually => PairingHandoffRoute.InstallOnThisPhone,

            _ when isMobile => PairingHandoffRoute.OpenOnThisDevice,
            _ => PairingHandoffRoute.ScanWithYourPhone,
        };
}
