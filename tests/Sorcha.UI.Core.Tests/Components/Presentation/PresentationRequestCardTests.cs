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
/// The single presentation card. Its one job that no previous surface did is picking a transport
/// from <see cref="PresentationSource"/> — the whole defect was a SorchaWallet gate being polled
/// against HAIP, which no test could catch because nothing branched at all.
/// </summary>
public sealed class PresentationRequestCardTests : BunitContext
{
    private sealed class FakeTransport(PresentationSource source, GateOutcome outcome)
        : IPresentationGateTransport
    {
        public PresentationSource Source => source;
        public bool WasUsed { get; private set; }
        public string? SeenToken { get; private set; }
        public bool ClaimsRequested { get; private set; }

        /// <summary>Set to return null claims, standing in for a failed claims fetch.</summary>
        public bool ClaimsUnavailable { get; init; }

        public Task<GateOutcome> WaitForOutcomeAsync(
            Guid requestId, IProgress<GateOutcome>? progress = null, CancellationToken ct = default)
        {
            WasUsed = true;
            return Task.FromResult(outcome);
        }

        public Task<IReadOnlyDictionary<string, object?>?> FetchClaimsAsync(
            Guid requestId, string? claimsFetchToken, CancellationToken ct = default)
        {
            ClaimsRequested = true;
            SeenToken = claimsFetchToken;
            return Task.FromResult<IReadOnlyDictionary<string, object?>?>(
                ClaimsUnavailable
                    ? null
                    : new Dictionary<string, object?> { ["givenName"] = "Stuart" });
        }
    }

    private readonly Mock<IQrPresentationService> _qr = new();

    public PresentationRequestCardTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        _qr.Setup(q => q.GenerateSvgFromUri(It.IsAny<string>(), It.IsAny<int>()))
            .Returns("<svg data-testid=\"stub-qr\"/>");
        Services.AddSingleton(_qr.Object);
    }

    private void Register(params IPresentationGateTransport[] transports)
    {
        foreach (var t in transports)
        {
            Services.AddSingleton(t);
        }
    }

    private IRenderedComponent<PresentationRequestCard> RenderCard(
        PresentationSource source,
        string? token = null,
        Action<ComponentParameterCollectionBuilder<PresentationRequestCard>>? extra = null)
        => Render<PresentationRequestCard>(p =>
        {
            p.Add(c => c.RequestId, Guid.NewGuid())
             .Add(c => c.PresentationRequestUri, "openid4vp://authorize?x=1")
             .Add(c => c.Source, source)
             .Add(c => c.ClaimsFetchToken, token);
            extra?.Invoke(p);
        });

    [Fact]
    public void SelectsTheTransportMatchingTheSource()
    {
        var haip = new FakeTransport(PresentationSource.HaipExternalWallet, GateOutcome.Success);
        var wallet = new FakeTransport(PresentationSource.SorchaWallet, GateOutcome.Success);
        Register(haip, wallet);

        RenderCard(PresentationSource.SorchaWallet);

        wallet.WasUsed.Should().BeTrue("a SorchaWallet gate must not be polled against HAIP");
        haip.WasUsed.Should().BeFalse();
    }

    [Fact]
    public void SelectsTheHaipTransportForAHaipGate()
    {
        var haip = new FakeTransport(PresentationSource.HaipExternalWallet, GateOutcome.Success);
        var wallet = new FakeTransport(PresentationSource.SorchaWallet, GateOutcome.Success);
        Register(haip, wallet);

        RenderCard(PresentationSource.HaipExternalWallet);

        haip.WasUsed.Should().BeTrue();
        wallet.WasUsed.Should().BeFalse();
    }

    [Fact]
    public void SaysSoWhenNoTransportIsRegisteredForTheSource()
    {
        // Rendering nothing would look exactly like an unscanned QR code, forever — which is the
        // failure mode this whole change exists to remove.
        Register(new FakeTransport(PresentationSource.HaipExternalWallet, GateOutcome.Success));

        var cut = RenderCard(PresentationSource.SorchaWallet);

        cut.Markup.Should().Contain("couldn't start");
    }

    [Fact]
    public void UnreachableSaysSoRatherThanClaimingExpiry()
    {
        Register(new FakeTransport(PresentationSource.SorchaWallet, GateOutcome.Unreachable));

        var cut = RenderCard(PresentationSource.SorchaWallet);

        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("Nothing was sent from your wallet");
            cut.Markup.Should().NotContain("expired");
        });
    }

    [Fact]
    public void SuccessSurfacesClaimsAndRendersChildContent()
    {
        Register(new FakeTransport(PresentationSource.SorchaWallet, GateOutcome.Success));
        IReadOnlyDictionary<string, object?>? received = null;

        var cut = RenderCard(PresentationSource.SorchaWallet, "tok-abc", p => p
            .Add(c => c.OnClaims, (IReadOnlyDictionary<string, object?>? claims) => received = claims)
            .AddChildContent("<p data-testid=\"form\">the form</p>"));

        cut.WaitForAssertion(() =>
        {
            received.Should().NotBeNull();
            received!.Should().ContainKey("givenName");
            cut.Find("[data-testid=form]").TextContent.Should().Be("the form");
        });
    }

    [Fact]
    public void ChildContentIsWithheldUntilTheGateClears()
    {
        Register(new FakeTransport(PresentationSource.SorchaWallet, GateOutcome.Declined));

        var cut = RenderCard(PresentationSource.SorchaWallet, "tok-abc", p => p
            .AddChildContent("<p data-testid=\"form\">the form</p>"));

        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("denied");
            cut.FindAll("[data-testid=form]").Should().BeEmpty();
        });
    }

    [Fact]
    public void TheClaimsTokenIsHandedToTheTransportUnchanged()
    {
        var transport = new FakeTransport(PresentationSource.SorchaWallet, GateOutcome.Success);
        Register(transport);

        var cut = RenderCard(PresentationSource.SorchaWallet, "tok-abc");

        cut.WaitForAssertion(() => transport.SeenToken.Should().Be("tok-abc"));
    }

    [Fact]
    public void AFailedClaimsFetchDoesNotReportTheGateAsFailed()
    {
        // The presentation DID succeed. Reporting the whole gate as failed would repeat the
        // "no matching credential" lie of #1324.
        Register(new FakeTransport(PresentationSource.SorchaWallet, GateOutcome.Success)
        {
            ClaimsUnavailable = true
        });

        var cut = RenderCard(PresentationSource.SorchaWallet, "tok-abc");

        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("verified");
            cut.Markup.Should().NotContain("denied");
        });
    }

    [Fact]
    public void ClaimsAreNotFetchedWhenTheGateDidNotSucceed()
    {
        var transport = new FakeTransport(PresentationSource.SorchaWallet, GateOutcome.Expired);
        Register(transport);

        var cut = RenderCard(PresentationSource.SorchaWallet, "tok-abc");

        cut.WaitForAssertion(() => transport.ClaimsRequested.Should().BeFalse());
    }

    [Fact]
    public void TheOutcomeIsReportedToTheHost()
    {
        Register(new FakeTransport(PresentationSource.SorchaWallet, GateOutcome.Abandoned));
        GateOutcome? reported = null;

        var cut = RenderCard(PresentationSource.SorchaWallet, null, p => p
            .Add(c => c.OnOutcome, (GateOutcome o) => reported = o));

        cut.WaitForAssertion(() => reported.Should().Be(GateOutcome.Abandoned));
    }
}
