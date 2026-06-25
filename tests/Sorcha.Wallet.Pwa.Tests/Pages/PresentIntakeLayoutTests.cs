// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Sorcha.UI.Core.Models.Presentation;
using Sorcha.UI.Testing;
using Sorcha.Wallet.Pwa.Models.Device;
using Sorcha.Wallet.Pwa.Services;
using Sorcha.Wallet.Pwa.Services.Device;
using Sorcha.Wallet.Pwa.Services.Presentation;
using Xunit;
using PresentPage = Sorcha.Wallet.Pwa.Pages.Present;

namespace Sorcha.Wallet.Pwa.Tests.Pages;

/// <summary>
/// Feature 159 — bUnit tests for the three <see cref="IntakeMode"/> layout branches on the
/// Present page: CameraFirst (US1), PasteWithScan (US2), PasteOnly (US3).
/// Injects a fake <see cref="IDeviceProfileProbe"/> to control which layout is rendered
/// without invoking real JS interop or browser camera APIs.
/// </summary>
public sealed class PresentIntakeLayoutTests : ComponentTestFixture
{
    private readonly Mock<IPresentationEngine> _engine = new();
    private readonly Mock<ICredentialCache> _credentials = new();
    private readonly Mock<IDeviceKeyService> _deviceKey = new();
    private readonly Mock<IDelegationStore> _delegation = new();
    private readonly Mock<IStatusListService> _statusList = new();
    private readonly Mock<IPresentationLog> _log = new();

    public PresentIntakeLayoutTests()
    {
        Services.AddSingleton(_engine.Object);
        Services.AddSingleton(_credentials.Object);
        Services.AddSingleton(_deviceKey.Object);
        Services.AddSingleton(_delegation.Object);
        Services.AddSingleton(_statusList.Object);
        Services.AddSingleton(_log.Object);
        Services.AddSingleton<Microsoft.Extensions.Logging.ILogger<PresentPage>>(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<PresentPage>.Instance);
        Services.AddScoped<System.Net.Http.HttpClient>(_ =>
            new System.Net.Http.HttpClient());
    }

    private Mock<IDeviceProfileProbe> SetupProbe(DeviceFormFactor factor, CameraAvailability camera)
    {
        var probe = new Mock<IDeviceProfileProbe>();
        var profile = new DeviceProfile(factor, camera);
        probe.Setup(p => p.GetProfileAsync(It.IsAny<CancellationToken>()))
             .ReturnsAsync(profile);
        Services.AddScoped<IDeviceProfileProbe>(_ => probe.Object);
        return probe;
    }

    // ── US1: CameraFirst (Handheld + Usable) ─────────────────────────────────────

    [Fact]
    public void CameraFirst_HandheldWithUsableCamera_RendersViewfinderAndPasteControl()
    {
        SetupProbe(DeviceFormFactor.Handheld, CameraAvailability.Usable);
        JSInterop.Setup<bool>("SorchaQrScanner.isSupported").SetResult(true);

        var cut = Render<PresentPage>();

        // Viewfinder video element should be present
        cut.FindAll("[data-testid=present-viewfinder]").Should().ContainSingle(
            "CameraFirst renders the viewfinder automatically");

        // "Paste a link instead" escape control must exist
        cut.FindAll("[data-testid=present-paste-instead]").Should().ContainSingle(
            "CameraFirst always shows paste escape control");
    }

    [Fact]
    public async Task CameraFirst_TapPasteInstead_StopsCameraAndShowsPasteField()
    {
        SetupProbe(DeviceFormFactor.Handheld, CameraAvailability.Usable);
        JSInterop.Setup<bool>("SorchaQrScanner.isSupported").SetResult(true);
        // Return empty string so auto-start doesn't advance past AwaitingDeepLink.
        // Loose mode handles SorchaQrScanner.stop automatically as a no-op.
        JSInterop.Setup<string>("SorchaQrScanner.start", VideoElementId()).SetResult(string.Empty);

        var cut = Render<PresentPage>();

        // Tap "Paste a link instead" — find within InvokeAsync to avoid stale handler IDs
        // after auto-start re-renders the component.
        await cut.InvokeAsync(() => cut.Find("[data-testid=present-paste-instead]").Click());

        // Paste field should now be visible
        cut.WaitForAssertion(() =>
            cut.FindAll("[data-testid=present-paste-field]").Should().ContainSingle(
                "tapping paste-instead shows the paste field"));

        // Viewfinder should be gone
        cut.FindAll("[data-testid=present-viewfinder]").Should().BeEmpty(
            "viewfinder is stopped when paste is active");
    }

    [Fact]
    public async Task CameraFirst_CameraStartFailure_FallsBackToPasteWithMessage()
    {
        SetupProbe(DeviceFormFactor.Handheld, CameraAvailability.Usable);
        JSInterop.Setup<bool>("SorchaQrScanner.isSupported").SetResult(true);
        JSInterop.Setup<string>("SorchaQrScanner.start", VideoElementId())
            .SetException(new Microsoft.JSInterop.JSException("NotAllowedError: permission denied"));

        var cut = Render<PresentPage>();
        await cut.InvokeAsync(() => Task.CompletedTask);  // give async init time

        // After failure, paste field must be present
        cut.FindAll("[data-testid=present-paste-field]").Should().ContainSingle(
            "camera start failure falls back to paste field");

        // Inline message (intakeMessage) must be shown
        cut.FindAll("[data-testid=present-intake-message]").Should().ContainSingle(
            "camera-start failure shows a plain-language inline message");
    }

    // ── US2: PasteWithScan (Desktop + Usable) ────────────────────────────────────

    [Fact]
    public void PasteWithScan_DesktopWithUsableCamera_ShowsPasteFieldAndScanControl()
    {
        SetupProbe(DeviceFormFactor.Desktop, CameraAvailability.Usable);

        var cut = Render<PresentPage>();

        // Paste field should be the default
        cut.FindAll("[data-testid=present-paste-field]").Should().ContainSingle(
            "PasteWithScan renders paste field as default");

        // "Scan with camera" control must be offered
        cut.FindAll("[data-testid=present-scan-with-camera]").Should().ContainSingle(
            "PasteWithScan shows Scan-with-camera control");

        // Camera must NOT be active on load
        cut.FindAll("[data-testid=present-viewfinder]").Should().BeEmpty(
            "PasteWithScan does not auto-start the camera (FR-004)");
    }

    [Fact]
    public async Task PasteWithScan_ActivateScanControl_StartsCamera()
    {
        SetupProbe(DeviceFormFactor.Desktop, CameraAvailability.Usable);
        JSInterop.Setup<bool>("SorchaQrScanner.isSupported").SetResult(true);
        // Simulate a successful scan
        JSInterop.Setup<string>("SorchaQrScanner.start", VideoElementId())
            .SetResult("openid4vp://verifier-payload");
        _engine.Setup(e => e.Parse(It.IsAny<string>()))
               .Throws(new InvalidOperationException("parse error (we just check StartScan was called)"));

        var cut = Render<PresentPage>();
        // Find inside InvokeAsync to avoid stale handler IDs, then WaitForAssertion
        // to let the Task.Yield() chain inside StartDesktopScanAsync complete.
        await cut.InvokeAsync(() => cut.Find("[data-testid=present-scan-with-camera]").Click());

        cut.WaitForAssertion(() => JSInterop.VerifyInvoke("SorchaQrScanner.start", 1),
            TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void PasteWithScan_PasteAndContinue_ReachesParse()
    {
        SetupProbe(DeviceFormFactor.Desktop, CameraAvailability.Usable);
        _engine.Setup(e => e.Parse(It.IsAny<string>()))
               .Returns(new ParsedPresentationRequest
               {
                   ClientId = "verifier1",
                   ResponseUri = "https://example.com/cb",
                   RequiredVct = "TestCred",
                   Nonce = "n1",
                   Purpose = null,
                   ResponseMode = "direct_post",
                   RequiredClaims = Array.Empty<string>(),
                   OptionalClaims = Array.Empty<string>(),
               });
        _credentials.Setup(c => c.ListAsync(It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new List<CachedCredential>());

        var cut = Render<PresentPage>();
        var field = cut.Find("[data-testid=present-paste-field]");
        field.Change("openid4vp://some-link");

        var continueBtn = cut.Find("[data-testid=present-continue]");
        continueBtn.Click();

        _engine.Verify(e => e.Parse(It.IsAny<string>()), Times.Once,
            "paste + Continue reaches ParseAsync and calls IPresentationEngine.Parse");
    }

    // ── US3: PasteOnly (any FormFactor + Unavailable) ────────────────────────────

    [Fact]
    public void PasteOnly_HandheldWithNoCamera_ShowsPasteFieldNoScanControl()
    {
        SetupProbe(DeviceFormFactor.Handheld, CameraAvailability.Unavailable);

        var cut = Render<PresentPage>();

        cut.FindAll("[data-testid=present-paste-field]").Should().ContainSingle(
            "PasteOnly shows paste field");

        // No scan affordance at all — not even hidden (FR-005)
        cut.FindAll("[data-testid=present-scan-with-camera]").Should().BeEmpty(
            "PasteOnly has no Scan-with-camera control (FR-005)");
        cut.FindAll("[data-testid=present-paste-instead]").Should().BeEmpty(
            "PasteOnly has no Paste-instead control (FR-005)");
        cut.FindAll("[data-testid=present-viewfinder]").Should().BeEmpty(
            "PasteOnly has no viewfinder (FR-005)");
    }

    [Fact]
    public void PasteOnly_DesktopWithNoCamera_ShowsPasteFieldNoScanControl()
    {
        SetupProbe(DeviceFormFactor.Desktop, CameraAvailability.Unavailable);

        var cut = Render<PresentPage>();

        cut.FindAll("[data-testid=present-paste-field]").Should().ContainSingle();
        cut.FindAll("[data-testid=present-scan-with-camera]").Should().BeEmpty(
            "PasteOnly (Desktop, Unavailable) has no scan control (FR-005)");
    }

    [Fact]
    public void PasteOnly_PasteAndContinue_ReachesParse()
    {
        SetupProbe(DeviceFormFactor.Handheld, CameraAvailability.Unavailable);
        _engine.Setup(e => e.Parse(It.IsAny<string>()))
               .Returns(new ParsedPresentationRequest
               {
                   ClientId = "verifier1",
                   ResponseUri = "https://example.com/cb",
                   RequiredVct = "TestCred",
                   Nonce = "n1",
                   Purpose = null,
                   ResponseMode = "direct_post",
                   RequiredClaims = Array.Empty<string>(),
                   OptionalClaims = Array.Empty<string>(),
               });
        _credentials.Setup(c => c.ListAsync(It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new List<CachedCredential>());

        var cut = Render<PresentPage>();
        cut.Find("[data-testid=present-paste-field]").Change("openid4vp://some-link");
        cut.Find("[data-testid=present-continue]").Click();

        _engine.Verify(e => e.Parse(It.IsAny<string>()), Times.Once,
            "PasteOnly paste + Continue reaches ParseAsync (FR-011 convergence)");
    }

    private static string VideoElementId() => "sorcha-qr-scan-video";
}
