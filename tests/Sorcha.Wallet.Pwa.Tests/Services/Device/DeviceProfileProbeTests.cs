// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Threading.Tasks;
using Bunit;
using FluentAssertions;
using Sorcha.UI.Testing;
using Sorcha.Wallet.Pwa.Models.Device;
using Sorcha.Wallet.Pwa.Services.Device;
using Xunit;

namespace Sorcha.Wallet.Pwa.Tests.Services.Device;

/// <summary>
/// Feature 159 — covers <see cref="DeviceProfileProbe"/> classification logic:
/// all four <see cref="DeviceFormFactor"/> × <see cref="CameraAvailability"/> →
/// <see cref="IntakeMode"/> mappings (FR-010 / SC-002), plus the JS-error-fallback
/// contract guarantee (paste-safe profile on any interop failure).
///
/// Probe tests drive the JS bridge via bUnit's JSInterop; mapping derivation is
/// also verified directly on <see cref="DeviceProfile.Mode"/> for completeness.
/// </summary>
public sealed class DeviceProfileProbeTests : ComponentTestFixture
{
    // ── DeviceProfile.Mode total mapping (pure C#, FR-010 / SC-002) ──────────────

    [Fact]
    public void Mode_Handheld_Usable_IsCameraFirst()
    {
        var profile = new DeviceProfile(DeviceFormFactor.Handheld, CameraAvailability.Usable);
        profile.Mode.Should().Be(IntakeMode.CameraFirst);
    }

    [Fact]
    public void Mode_Handheld_Unavailable_IsPasteOnly()
    {
        var profile = new DeviceProfile(DeviceFormFactor.Handheld, CameraAvailability.Unavailable);
        profile.Mode.Should().Be(IntakeMode.PasteOnly);
    }

    [Fact]
    public void Mode_Desktop_Usable_IsPasteWithScan()
    {
        var profile = new DeviceProfile(DeviceFormFactor.Desktop, CameraAvailability.Usable);
        profile.Mode.Should().Be(IntakeMode.PasteWithScan);
    }

    [Fact]
    public void Mode_Desktop_Unavailable_IsPasteOnly()
    {
        var profile = new DeviceProfile(DeviceFormFactor.Desktop, CameraAvailability.Unavailable);
        profile.Mode.Should().Be(IntakeMode.PasteOnly);
    }

    // ── DeviceProfileProbe — JS interop mapping (via JSInterop stub) ─────────────

    [Fact]
    public async Task GetProfileAsync_HandheldUsableCamera_ReturnsCameraFirst()
    {
        JSInterop.Setup<DeviceSignals>("SorchaDeviceProfile.detect")
            .SetResult(new DeviceSignals("handheld", CameraApi: true, HasVideoInput: null));
        JSInterop.Setup<int?>("SorchaDeviceProfile.countVideoInputsAsync")
            .SetResult(1);

        var probe = new DeviceProfileProbe(JSInterop.JSRuntime);
        var profile = await probe.GetProfileAsync();

        profile.FormFactor.Should().Be(DeviceFormFactor.Handheld);
        profile.CameraAvailability.Should().Be(CameraAvailability.Usable);
        profile.Mode.Should().Be(IntakeMode.CameraFirst);
    }

    [Fact]
    public async Task GetProfileAsync_HandheldNoCameraApi_ReturnsPasteOnly()
    {
        JSInterop.Setup<DeviceSignals>("SorchaDeviceProfile.detect")
            .SetResult(new DeviceSignals("handheld", CameraApi: false, HasVideoInput: null));

        var probe = new DeviceProfileProbe(JSInterop.JSRuntime);
        var profile = await probe.GetProfileAsync();

        profile.FormFactor.Should().Be(DeviceFormFactor.Handheld);
        profile.CameraAvailability.Should().Be(CameraAvailability.Unavailable);
        profile.Mode.Should().Be(IntakeMode.PasteOnly);
    }

    [Fact]
    public async Task GetProfileAsync_DesktopUsableCamera_ReturnsPasteWithScan()
    {
        JSInterop.Setup<DeviceSignals>("SorchaDeviceProfile.detect")
            .SetResult(new DeviceSignals("desktop", CameraApi: true, HasVideoInput: null));
        JSInterop.Setup<int?>("SorchaDeviceProfile.countVideoInputsAsync")
            .SetResult(2);

        var probe = new DeviceProfileProbe(JSInterop.JSRuntime);
        var profile = await probe.GetProfileAsync();

        profile.FormFactor.Should().Be(DeviceFormFactor.Desktop);
        profile.CameraAvailability.Should().Be(CameraAvailability.Usable);
        profile.Mode.Should().Be(IntakeMode.PasteWithScan);
    }

    [Fact]
    public async Task GetProfileAsync_DesktopZeroVideoInputs_ReturnsPasteOnly()
    {
        JSInterop.Setup<DeviceSignals>("SorchaDeviceProfile.detect")
            .SetResult(new DeviceSignals("desktop", CameraApi: true, HasVideoInput: null));
        JSInterop.Setup<int?>("SorchaDeviceProfile.countVideoInputsAsync")
            .SetResult(0);

        var probe = new DeviceProfileProbe(JSInterop.JSRuntime);
        var profile = await probe.GetProfileAsync();

        profile.CameraAvailability.Should().Be(CameraAvailability.Unavailable);
        profile.Mode.Should().Be(IntakeMode.PasteOnly);
    }

    [Fact]
    public async Task GetProfileAsync_JsDetectThrows_ReturnsPasteSafeProfile()
    {
        // Any interop failure must degrade to paste-safe (FR-007, contracts/device-profile.md).
        JSInterop.Mode = JSRuntimeMode.Strict;
        // No setup → InvokeAsync will throw because no handler is registered.

        var probe = new DeviceProfileProbe(JSInterop.JSRuntime);
        var profile = await probe.GetProfileAsync();

        profile.CameraAvailability.Should().Be(CameraAvailability.Unavailable);
        profile.Mode.Should().Be(IntakeMode.PasteOnly);
    }
}
