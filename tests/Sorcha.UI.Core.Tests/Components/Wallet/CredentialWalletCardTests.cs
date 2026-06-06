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
