// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Sorcha.UI.Core.Components.EnrolGate;
using Sorcha.UI.Core.Services;
using Sorcha.UI.Core.Services.User.Enrolment;
using Xunit;

namespace Sorcha.UI.Core.Tests.Components.EnrolGate;

/// <summary>
/// bunit component tests for Feature 126's EnrolGate surfaces. Verifies
/// tier branching, the FastPath / OnReady contract, and the
/// PreflightSignupSurface + HybridQrAffordance render contracts that
/// downstream consumers + the cold-start E2E rely on.
/// </summary>
public sealed class EnrolGateComponentTests : BunitContext
{
    private readonly Mock<ITierProbeService> _probe = new();
    private readonly Mock<IEnrolPairingSignal> _signal = new();
    private readonly Mock<IQrPresentationService> _qr = new();

    public EnrolGateComponentTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton(_probe.Object);
        Services.AddSingleton(_signal.Object);
        Services.AddSingleton(_qr.Object);
        // HttpClient that immediately returns a synthetic whoami payload —
        // EnrolGateComponent.ResolveBoundPlatformUserIdAsync calls
        // GET /api/auth/whoami when tier != ColdStart.
        Services.AddSingleton(new HttpClient(new StubHandler())
        {
            BaseAddress = new Uri("http://test.local")
        });

        _qr.Setup(q => q.GenerateSvgFromUri(It.IsAny<string>(), It.IsAny<int>()))
            .Returns("<svg data-testid=\"stub-qr\"/>");

        // EnrolPairingSignal is started by WalletPairingSurface — make it a no-op.
        _signal.Setup(s => s.StartAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _signal.Setup(s => s.StopAsync()).Returns(Task.CompletedTask);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"platformUserId\":\"00000000-0000-0000-0000-000000000001\",\"sessionToken\":\"jwt\",\"qrUrl\":\"http://x/?session=jwt\",\"expiresAt\":\"2030-01-01T00:00:00Z\"}",
                    System.Text.Encoding.UTF8, "application/json")
            });
        }
    }

    [Fact]
    public void FastPath_Tier_Renders_ChildContent_And_Fires_OnReady()
    {
        _probe.Setup(p => p.ProbeAsync(It.IsAny<CancellationToken>())).ReturnsAsync(CitizenTier.FastPath);
        Guid? readyForUser = null;
        var readyFired = false;

        var cut = Render<EnrolGateComponent>(parameters => parameters
            .Add(p => p.CouncilName, "Strathcarron Council")
            .Add(p => p.OnReady, EventCallback.Factory.Create<Guid?>(this, (Guid? id) =>
            {
                readyForUser = id;
                readyFired = true;
            }))
            .AddChildContent("<div data-testid=\"form-rendered\">application form</div>"));

        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("data-testid=\"form-rendered\"");
            readyFired.Should().BeTrue();
        });
    }

    [Fact]
    public void ColdStart_Tier_Renders_PreflightSignupSurface()
    {
        _probe.Setup(p => p.ProbeAsync(It.IsAny<CancellationToken>())).ReturnsAsync(CitizenTier.ColdStart);

        var cut = Render<EnrolGateComponent>(parameters => parameters
            .Add(p => p.CouncilName, "Strathcarron Council")
            .Add(p => p.ServiceLabel, "driving licence application"));

        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("enrol-preflight");
            cut.Markup.Should().Contain("Strathcarron Council");
            cut.Markup.Should().Contain("driving licence application");
            // FR-004 — no QR before signup.
            cut.Markup.Should().NotContain("enrol-hybrid-qr");
        });
    }

    [Fact]
    public void MiniGate_Tier_Renders_WalletPairingSurface_Without_Signup_Copy()
    {
        _probe.Setup(p => p.ProbeAsync(It.IsAny<CancellationToken>())).ReturnsAsync(CitizenTier.MiniGate);

        var cut = Render<EnrolGateComponent>(parameters => parameters
            .Add(p => p.CouncilName, "Strathcarron Council"));

        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("enrol-pairing");
            // SC-003 — mini-gate citizen sees no signup surface at any point.
            cut.Markup.Should().NotContain("Sign in or create your account");
        });
    }
}

/// <summary>
/// Focused tests for <see cref="PreflightSignupSurface"/> — Tier 3 preflight
/// surface that links to the F116 signup flow with ?returnUrl=&lt;currentUrl&gt;.
/// </summary>
public sealed class PreflightSignupSurfaceTests : BunitContext
{
    [Fact]
    public void Renders_Signup_Link_With_ReturnUrl_Pointing_At_Current_Page()
    {
        // bunit's FakeNavigationManager defaults to http://localhost/ as the base.
        var cut = Render<PreflightSignupSurface>(parameters => parameters
            .Add(p => p.CouncilName, "Strathcarron Council")
            .Add(p => p.ServiceLabel, "driving licence application"));

        cut.Markup.Should().Contain("data-testid=\"enrol-signup-cta\"");
        cut.Markup.Should().Contain("Strathcarron Council");
        cut.Markup.Should().Contain("/auth/signup?returnUrl=");
        // FR-004 — no QR rendered at this stage.
        cut.Markup.Should().NotContain("enrol-hybrid-qr");
    }

    [Fact]
    public void Defaults_CouncilName_When_Not_Set()
    {
        var cut = Render<PreflightSignupSurface>();
        cut.Markup.Should().Contain("this council");
    }
}

/// <summary>
/// Focused tests for <see cref="HybridQrAffordance"/> — QR + tap-link + copy-link.
/// </summary>
public sealed class HybridQrAffordanceTests : BunitContext
{
    private readonly Mock<IQrPresentationService> _qr = new();

    public HybridQrAffordanceTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton(_qr.Object);
        _qr.Setup(q => q.GenerateSvgFromUri(It.IsAny<string>(), It.IsAny<int>()))
            .Returns("<svg data-testid=\"stub-qr-svg\"/>");
    }

    [Fact]
    public void Renders_Qr_Tap_Link_And_Copy_Button_With_TestIds()
    {
        var cut = Render<HybridQrAffordance>(parameters => parameters
            .Add(p => p.QrUrl, "https://strathcarron.gov/wallet/enrol?session=jwt-123")
            .Add(p => p.ExpiresAt, DateTimeOffset.UtcNow.AddMinutes(10)));

        cut.Markup.Should().Contain("stub-qr-svg", "QR SVG goes through IQrPresentationService");
        cut.Markup.Should().Contain("data-testid=\"enrol-tap-link\"");
        cut.Markup.Should().Contain("data-testid=\"enrol-copy-link\"");
        cut.Markup.Should().Contain("https://strathcarron.gov/wallet/enrol?session=jwt-123");
    }

    [Fact]
    public void Empty_QrUrl_Renders_Nothing()
    {
        var cut = Render<HybridQrAffordance>(parameters => parameters
            .Add(p => p.QrUrl, ""));

        cut.Markup.Should().NotContain("data-testid=\"enrol-tap-link\"");
        cut.Markup.Should().NotContain("data-testid=\"enrol-copy-link\"");
    }
}
