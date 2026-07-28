// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using MudBlazor.Services;
using Sorcha.Blueprint.Models.Credentials;
using Sorcha.UI.Core.Components.Presentation;
using Sorcha.UI.Core.Services;
using Sorcha.UI.Core.Services.User.Presentation;
using Xunit;

namespace Sorcha.UI.Core.Tests.Components.Presentation;

/// <summary>
/// #1330 — the "Use this device" local route wired into <see cref="PresentationRequestCard"/>.
/// The card resolves <see cref="ISorchaWalletLocalPresenter"/> via <c>IServiceProvider.GetService</c>
/// so a host that never registers it (the council sample portal) degrades silently to QR-only, and a
/// probe that returns null does the same — the existing transport/wait machinery is untouched either
/// way, and the QR stays reachable (collapsed) even when the local panel renders.
/// </summary>
public sealed class PresentationRequestCardLocalRouteTests : BunitContext
{
    /// <summary>Transport that never resolves — keeps the card in Pending for render assertions.</summary>
    private sealed class PendingTransport : IPresentationGateTransport
    {
        public PresentationSource Source => PresentationSource.SorchaWallet;

        public Task<GateOutcome> WaitForOutcomeAsync(
            Guid requestId, IProgress<GateOutcome>? progress = null, CancellationToken ct = default)
            => new TaskCompletionSource<GateOutcome>().Task.WaitAsync(ct);

        public Task<IReadOnlyDictionary<string, object?>?> FetchClaimsAsync(
            Guid requestId, string? claimsFetchToken, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyDictionary<string, object?>?>(null);
    }

    private readonly Mock<IQrPresentationService> _qr = new();

    public PresentationRequestCardLocalRouteTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        _qr.Setup(q => q.GenerateSvgFromUri(It.IsAny<string>(), It.IsAny<int>()))
            .Returns("<svg data-testid=\"stub-qr\"/>");
        Services.AddSingleton(_qr.Object);
        Services.AddSingleton<IPresentationGateTransport>(new PendingTransport());
    }

    private static LocalPresentationCandidate Candidate() => new()
    {
        CredentialId = "urn:uuid:c1", WalletAddress = "ws1q", Vct = "vct",
        RequiredClaims = ["givenName"], OptionalClaims = [],
        Nonce = "n", ClientId = "c", ResponseUri = "/cb", QueryId = "credential",
        RequestState = "rid", JoseAlgorithm = "EdDSA", KidThumbprint = "t",
    };

    private IRenderedComponent<PresentationRequestCard> RenderCard(ISorchaWalletLocalPresenter? presenter)
    {
        if (presenter is not null) Services.AddSingleton(presenter);
        return Render<PresentationRequestCard>(p => p
            .Add(x => x.PresentationRequestUri, "openid4vp://authorize?request_uri=x")
            .Add(x => x.RequestId, Guid.NewGuid())
            .Add(x => x.Source, PresentationSource.SorchaWallet));
    }

    [Fact]
    public void SorchaWalletGate_ProbeReturnsCandidate_RendersLocalPanelWithCollapsedQr()
    {
        var presenter = new Mock<ISorchaWalletLocalPresenter>();
        presenter.Setup(p => p.ProbeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Candidate());

        var cut = RenderCard(presenter.Object);

        cut.WaitForAssertion(() =>
        {
            cut.FindComponents<UseThisDevicePanel>().Should().HaveCount(1);
            cut.Markup.Should().Contain("scan with your phone"); // QR still reachable
        });
    }

    [Fact]
    public void SorchaWalletGate_ProbeReturnsNull_RendersQrOnly()
    {
        var presenter = new Mock<ISorchaWalletLocalPresenter>();
        presenter.Setup(p => p.ProbeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((LocalPresentationCandidate?)null);

        var cut = RenderCard(presenter.Object);

        cut.FindComponents<UseThisDevicePanel>().Should().BeEmpty();
    }

    [Fact]
    public void SorchaWalletGate_NoPresenterRegistered_RendersQrOnly()
    {
        // The council sample portal never registers the presenter — QR-only, no throw.
        var cut = RenderCard(presenter: null);

        cut.FindComponents<UseThisDevicePanel>().Should().BeEmpty();
    }
}
