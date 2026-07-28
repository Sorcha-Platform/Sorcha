// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using MudBlazor;
using MudBlazor.Services;
using Sorcha.UI.Core.Components.Presentation;
using Sorcha.UI.Core.Services.User.Presentation;
using Xunit;

namespace Sorcha.UI.Core.Tests.Components.Presentation;

/// <summary>
/// The "Use this device" consent surface (#1330) — required claims locked on, optional claims
/// toggleable and default ON (a default-off portrait turns the AIAS cyber happy path into a hard
/// reject), and error copy that must never repeat the "no matching credential" lie of #1324.
/// </summary>
public sealed class UseThisDevicePanelTests : BunitContext
{
    private readonly Mock<ISorchaWalletLocalPresenter> _presenter = new();

    public UseThisDevicePanelTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        Services.AddSingleton(_presenter.Object);
    }

    private static LocalPresentationCandidate Candidate() => new()
    {
        CredentialId = "urn:uuid:c1", WalletAddress = "ws1q", Vct = "https://sorcha.dev/vc/assured-identity/v1",
        RequiredClaims = ["givenName", "familyName"], OptionalClaims = ["portrait"],
        Nonce = "n", ClientId = "did:sorcha:org:x", ResponseUri = "/cb", QueryId = "credential",
        RequestState = "rid-1", JoseAlgorithm = "EdDSA", KidThumbprint = "t",
    };

    private static LocalPresentationCandidate Candidate(
        string credentialId, string requestState, IReadOnlyList<string> optionalClaims) => new()
    {
        CredentialId = credentialId, WalletAddress = "ws1q", Vct = "https://sorcha.dev/vc/assured-identity/v1",
        RequiredClaims = ["givenName", "familyName"], OptionalClaims = optionalClaims,
        Nonce = "n", ClientId = "did:sorcha:org:x", ResponseUri = "/cb", QueryId = "credential",
        RequestState = requestState, JoseAlgorithm = "EdDSA", KidThumbprint = "t",
    };

    private IRenderedComponent<UseThisDevicePanel> RenderPanel(
        Action<ComponentParameterCollectionBuilder<UseThisDevicePanel>>? extra = null)
        => Render<UseThisDevicePanel>(p =>
        {
            p.Add(x => x.Candidate, Candidate())
             .Add(x => x.CredentialDisplayName, "Assured Identity");
            extra?.Invoke(p);
        });

    [Fact]
    public void Render_ListsRequiredClaimsLockedAndOptionalToggledOn()
    {
        var cut = RenderPanel();
        cut.Markup.Should().Contain("givenName").And.Contain("familyName").And.Contain("portrait");

        // Optional claims default ON — a default-off portrait converts the cyber happy path
        // into a hard reject (the agent refuses portrait-less presentations). Required claims
        // are rendered as a locked row (not a MudCheckBox), so the only toggleable control here
        // is the optional claim.
        var toggles = cut.FindComponents<MudCheckBox<bool>>();
        toggles.Should().HaveCount(1); // only the optional claim is toggleable
        toggles[0].Instance.Value.Should().BeTrue();
    }

    [Fact]
    public void ShareAndContinue_PassesRequiredPlusCheckedOptionalClaims()
    {
        IReadOnlyCollection<string>? sent = null;
        _presenter.Setup(p => p.PresentAsync(It.IsAny<LocalPresentationCandidate>(),
                It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .Callback<LocalPresentationCandidate, IReadOnlyCollection<string>, CancellationToken>(
                (_, claims, _) => sent = claims)
            .ReturnsAsync(LocalPresentResult.Submitted());

        var cut = RenderPanel();
        cut.Find("[data-testid=use-this-device-share]").Click();

        sent.Should().NotBeNull();
        sent.Should().BeEquivalentTo(["givenName", "familyName", "portrait"]);
    }

    [Fact]
    public void ShareAndContinue_Submitted_InvokesOnSubmitted()
    {
        _presenter.Setup(p => p.PresentAsync(It.IsAny<LocalPresentationCandidate>(),
                It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(LocalPresentResult.Submitted());

        var submitted = false;
        var cut = RenderPanel(p => p.Add(x => x.OnSubmitted, () => submitted = true));
        cut.Find("[data-testid=use-this-device-share]").Click();

        submitted.Should().BeTrue();
    }

    [Fact]
    public void ShareAndContinue_Failed_ShowsInlineErrorAndDoesNotInvokeOnSubmitted()
    {
        _presenter.Setup(p => p.PresentAsync(It.IsAny<LocalPresentationCandidate>(),
                It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(LocalPresentResult.Failed("boom"));

        var submitted = false;
        var cut = RenderPanel(p => p.Add(x => x.OnSubmitted, () => submitted = true));
        cut.Find("[data-testid=use-this-device-share]").Click();

        cut.Markup.Should().Contain("couldn't share"); // inline error, QR remains the fallback
        cut.Markup.Should().NotContain("no matching credential");
        submitted.Should().BeFalse();
    }

    [Fact]
    public void OnParametersSet_CandidateSwap_ResetsOptionalConsentToDefaultOn()
    {
        var candidateA = Candidate("urn:uuid:c1", "rid-1", ["portrait", "nickname"]);
        var candidateB = Candidate("urn:uuid:c2", "rid-2", ["portrait"]);

        IReadOnlyCollection<string>? sent = null;
        _presenter.Setup(p => p.PresentAsync(It.IsAny<LocalPresentationCandidate>(),
                It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .Callback<LocalPresentationCandidate, IReadOnlyCollection<string>, CancellationToken>(
                (_, claims, _) => sent = claims)
            .ReturnsAsync(LocalPresentResult.Submitted());

        var cut = Render<UseThisDevicePanel>(p => p
            .Add(x => x.Candidate, candidateA)
            .Add(x => x.CredentialDisplayName, "Assured Identity"));

        // Uncheck portrait on candidate A — it should NOT stay off once we move to candidate B.
        var portraitToggle = cut.FindComponents<MudCheckBox<bool>>()
            .First(c => c.Instance.Label == "portrait");
        cut.InvokeAsync(() => portraitToggle.Instance.ValueChanged.InvokeAsync(false));

        // Swap to a different candidate — new CredentialId/RequestState, same claim name recurring.
        cut.Render(p => p.Add(x => x.Candidate, candidateB));
        cut.Find("[data-testid=use-this-device-share]").Click();

        sent.Should().NotBeNull();
        sent.Should().Contain("portrait"); // default ON restored despite being unchecked on candidate A
        sent.Should().NotContain("nickname"); // never existed on candidate B — must not leak across the swap
    }
}
