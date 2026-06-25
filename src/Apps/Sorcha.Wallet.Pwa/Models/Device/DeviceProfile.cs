// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Wallet.Pwa.Models.Device;

/// <summary>The holder's device form factor as classified for intake-layout selection.</summary>
public enum DeviceFormFactor
{
    /// <summary>Device the holder typically holds and points at a QR code (phone / small tablet).</summary>
    Handheld,

    /// <summary>Laptop or desktop-class device.</summary>
    Desktop,
}

/// <summary>Whether the wallet can drive a working camera capture capability on this device.</summary>
public enum CameraAvailability
{
    /// <summary>Camera API is present and a video input device exists — the viewfinder can be driven.</summary>
    Usable,

    /// <summary>
    /// Camera API absent, no camera hardware found, or otherwise not drivable for layout purposes.
    /// Runtime permission denial is handled separately at viewfinder-start time.
    /// </summary>
    Unavailable,
}

/// <summary>The intake layout the Present page will render, derived from the device profile.</summary>
public enum IntakeMode
{
    /// <summary>Live viewfinder auto-started on load; "Paste a link instead" control is available.</summary>
    CameraFirst,

    /// <summary>Paste field is the default; "Scan with camera" control starts the viewfinder on demand.</summary>
    PasteWithScan,

    /// <summary>Paste field only — no scan control anywhere on the intake surface.</summary>
    PasteOnly,
}

/// <summary>
/// The holder's device as classified for intake-layout selection on the Present page.
/// Constructed once per page load by <see cref="Sorcha.Wallet.Pwa.Services.Device.IDeviceProfileProbe"/>;
/// never persisted.
/// </summary>
/// <param name="FormFactor">Handheld or Desktop form factor.</param>
/// <param name="CameraAvailability">Whether a camera is available for capture.</param>
public record DeviceProfile(DeviceFormFactor FormFactor, CameraAvailability CameraAvailability)
{
    /// <summary>
    /// The intake layout to render on the Present page, derived from the total
    /// <see cref="FormFactor"/> × <see cref="CameraAvailability"/> mapping (FR-010 / SC-002).
    /// Every combination maps to exactly one <see cref="IntakeMode"/>.
    /// </summary>
    public IntakeMode Mode => (FormFactor, CameraAvailability) switch
    {
        (DeviceFormFactor.Handheld, CameraAvailability.Usable)     => IntakeMode.CameraFirst,
        (DeviceFormFactor.Desktop,  CameraAvailability.Usable)     => IntakeMode.PasteWithScan,
        _                                                           => IntakeMode.PasteOnly,
    };
}
