// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Bunit;
using FluentAssertions;
using Sorcha.UI.Core.Components.Wallet;
using Sorcha.UI.Testing;
using Xunit;

namespace Sorcha.UI.Core.Tests.Components.Wallet;

/// <summary>
/// bUnit tests for <see cref="CredentialWalletCard"/>. The eyebrow clamp (the
/// "pan" bug fix) is CSS, so the no-horizontal-overflow guarantee is verified
/// by the Playwright Home@360px test; here we guard the markup contract the CSS
/// relies on — the long issuer flows into the dedicated eyebrow node, and the
/// type chip is omitted entirely (not rendered empty) when there is no label.
/// </summary>
public sealed class CredentialWalletCardTests : ComponentTestFixture
{
    private const string LongIssuer = "did:sorcha:org:WS11QRNVabcdefghijklmnopqrstuvwxyz0123456789HGNRK";

    [Fact]
    public void LongIssuer_RendersInClampedEyebrowNode()
    {
        var cut = Render<CredentialWalletCard>(ps => ps
            .Add(p => p.Issuer, LongIssuer)
            .Add(p => p.Name, "Assured Identity"));

        var eyebrow = cut.Find(".credential-wallet-card__eyebrow");
        eyebrow.TextContent.Trim().Should().Be(LongIssuer);
    }

    [Fact]
    public void Heading_WrapsEyebrowAndName_LeftAlignContract()
    {
        // The centre-crunch fix left-aligns the card via scoped CSS that targets
        // .credential-wallet-card__heading and its __eyebrow / __name children
        // (the <button> default is text-align:center). bUnit (AngleSharp) has no
        // layout/CSS engine, so the *computed* text-align can't be asserted here —
        // that lives in the Playwright Home@360px coverage once the #700 card
        // fixture lands. What we CAN pin is the markup the scoped selectors bind
        // to: a single heading wrapper containing both the eyebrow and the name,
        // so a refactor can't rename the nodes out from under the left-align CSS.
        var cut = Render<CredentialWalletCard>(ps => ps
            .Add(p => p.Issuer, "sorcha.dev")
            .Add(p => p.Name, "Assured Identity"));

        var heading = cut.Find(".credential-wallet-card__heading");
        heading.QuerySelector(".credential-wallet-card__eyebrow")
            .Should().NotBeNull("the eyebrow must live inside the heading the left-align CSS targets");
        heading.QuerySelector(".credential-wallet-card__name")
            .Should().NotBeNull("the name must live inside the heading the left-align CSS targets");

        // The chip is a sibling of the heading (the space-between top row), not a
        // child — so the heading text stays flush-left rather than centring.
        var top = cut.Find(".credential-wallet-card__top");
        top.QuerySelector(":scope > .credential-wallet-card__heading")
            .Should().NotBeNull("the heading must be a direct child of the space-between top row");
    }

    [Fact]
    public void EmptyMeta_OmitsMetaLine()
    {
        // The card name carries the type and the eyebrow the issuer, so a real
        // credential passes no meta — it must not render an empty meta node
        // (and certainly never the raw VCT).
        var cut = Render<CredentialWalletCard>(ps => ps
            .Add(p => p.Issuer, "sorcha.dev")
            .Add(p => p.Name, "Assured Identity")
            .Add(p => p.Meta, ""));

        cut.FindAll(".credential-wallet-card__meta").Should().BeEmpty();
    }

    [Fact]
    public void NullTypeLabel_OmitsChip()
    {
        var cut = Render<CredentialWalletCard>(ps => ps
            .Add(p => p.Issuer, "sorcha.dev")
            .Add(p => p.Name, "Assured Identity")
            .Add(p => p.TypeLabel, null));

        cut.FindAll(".credential-wallet-card__type-chip").Should().BeEmpty();
    }

    [Fact]
    public void TypeLabel_RendersChip()
    {
        var cut = Render<CredentialWalletCard>(ps => ps
            .Add(p => p.Issuer, "sorcha.dev")
            .Add(p => p.Name, "Assured Identity")
            .Add(p => p.TypeLabel, "ID"));

        cut.Find(".credential-wallet-card__type-chip").TextContent.Trim().Should().Be("ID");
    }

    [Fact]
    public void StatusNone_RendersNoPill()
    {
        var cut = Render<CredentialWalletCard>(ps => ps
            .Add(p => p.Issuer, "sorcha.dev").Add(p => p.Name, "Assured Identity"));

        cut.FindAll(".credential-wallet-card__status").Should().BeEmpty();
    }

    [Fact]
    public void ValidStatus_RendersValidPill()
    {
        var cut = Render<CredentialWalletCard>(ps => ps
            .Add(p => p.Issuer, "sorcha.dev").Add(p => p.Name, "Assured Identity")
            .Add(p => p.Status, CredentialStatus.Valid));

        var pill = cut.Find(".credential-wallet-card__status");
        pill.ClassList.Should().Contain("credential-wallet-card__status--valid");
        pill.TextContent.Trim().Should().Be("Valid");
    }

    [Fact]
    public void ExpiringStatus_UsesStatusTextOverride()
    {
        var cut = Render<CredentialWalletCard>(ps => ps
            .Add(p => p.Issuer, "sorcha.dev").Add(p => p.Name, "Assured Identity")
            .Add(p => p.Status, CredentialStatus.ExpiringSoon)
            .Add(p => p.StatusText, "Expires in 3 days"));

        cut.Find(".credential-wallet-card__status").TextContent.Trim().Should().Be("Expires in 3 days");
    }

    [Fact]
    public void RevokedStatus_DimsCard_AndShowsRevokedPill()
    {
        var cut = Render<CredentialWalletCard>(ps => ps
            .Add(p => p.Issuer, "sorcha.dev").Add(p => p.Name, "Assured Identity")
            .Add(p => p.Status, CredentialStatus.Revoked));

        cut.Find(".credential-wallet-card").ClassList.Should().Contain("credential-wallet-card--revoked");
        cut.Find(".credential-wallet-card__status").ClassList
            .Should().Contain("credential-wallet-card__status--revoked");
    }
}
