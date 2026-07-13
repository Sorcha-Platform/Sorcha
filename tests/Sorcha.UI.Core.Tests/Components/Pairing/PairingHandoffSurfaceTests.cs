// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Sorcha.UI.Core.Components.Pairing;
using Sorcha.UI.Core.Services;
using Sorcha.UI.Core.Services.User.Pairing;
using Xunit;

namespace Sorcha.UI.Core.Tests.Components.Pairing;

/// <summary>
/// Regression for the live n1 bug: a signed-in citizen landing on <c>/setup/add-device</c> saw
/// "Couldn't generate a pairing code." every time. The surface was minting through the host's
/// <b>ambient</b> HttpClient, which in the web client is deliberately anonymous — so the mint went
/// out with no bearer token and the server answered 401.
/// <para>
/// The surface now mints through <see cref="IPairingClient"/>, a typed client the host registers
/// with its own auth handler. These tests pin the two outcomes that matter: a successful mint renders
/// the QR (never the error), and only a genuinely failed mint renders the error.
/// </para>
/// </summary>
public sealed class PairingHandoffSurfaceTests : BunitContext
{
    private readonly Mock<IPairingClient> _pairing = new();
    private readonly Mock<IPwaInstallabilityProbe> _install = new();
    private readonly Mock<IQrPresentationService> _qr = new();

    public PairingHandoffSurfaceTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton(_pairing.Object);
        Services.AddSingleton(_install.Object);
        Services.AddSingleton(_qr.Object);

        _qr.Setup(q => q.GenerateSvgFromUri(It.IsAny<string>(), It.IsAny<int>()))
            .Returns("<svg data-testid=\"stub-qr\"/>");

        // Desktop: the QR variant, which is the surface the web citizen actually lands on.
        _install.Setup(p => p.ProbeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(PwaInstallabilityVerdict.CannotInstall);
    }

    [Fact]
    public void SuccessfulMint_RendersTheQr_AndNotTheError()
    {
        _pairing.Setup(c => c.MintAsync("standalone", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EnrolSessionMint(
                "jwt", "https://n1.sorcha.dev/wallet/enrol?t=jwt",
                DateTimeOffset.UtcNow.AddMinutes(10), "standalone"));

        var cut = Render<PairingHandoffSurface>();

        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().NotContain("Couldn't generate a pairing code",
                "a signed-in citizen whose mint succeeded must never see the failure message");
            cut.Markup.Should().Contain("data-testid=\"stub-qr\"");
        });
    }

    [Fact]
    public void FailedMint_RendersTheErrorAndARetry()
    {
        // The client returns null on any non-success (401, 500, network) — it never throws.
        _pairing.Setup(c => c.MintAsync("standalone", It.IsAny<CancellationToken>()))
            .ReturnsAsync((EnrolSessionMint?)null);

        var cut = Render<PairingHandoffSurface>();

        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("Couldn't generate a pairing code");
            cut.Markup.Should().Contain("data-testid=\"pairing-handoff-retry\"");
        });
    }

    [Fact]
    public void InstallVariant_MintsTheShortCode_AndRendersIt()
    {
        _install.Setup(p => p.ProbeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(PwaInstallabilityVerdict.CanInstallProgrammatically);
        _pairing.Setup(c => c.MintAsync("standalone", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EnrolSessionMint(
                "jwt", "https://n1.sorcha.dev/wallet/enrol?t=jwt",
                DateTimeOffset.UtcNow.AddMinutes(10), "standalone"));
        _pairing.Setup(c => c.MintShortCodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PairingShortCode("243806", DateTimeOffset.UtcNow.AddMinutes(5)));

        var cut = Render<PairingHandoffSurface>();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("243806"));
    }
}
