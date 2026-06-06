// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Collections.Generic;
using Bunit;
using FluentAssertions;
using Sorcha.UI.Core.Components.Wallets;
using Sorcha.UI.Core.Models.Wallet;
using Sorcha.UI.Testing;
using Sorcha.UI.Testing.Builders;
using Xunit;

namespace Sorcha.UI.Core.Tests.Components.Wallets;

/// <summary>
/// bUnit tests for <see cref="WalletListPanel"/> — the shared admin/user wallet
/// list. Covers the loading / error / empty states and the in-flight delete
/// affordance (spinner swap + disabled siblings) added for the admin delete flow.
/// </summary>
public sealed class WalletListPanelTests : ComponentTestFixture
{
    private static List<WalletDto> TwoWallets() =>
    [
        new WalletDtoBuilder().WithName("Treasury").WithAddress("0xAAA").Build(),
        new WalletDtoBuilder().WithName("Operations").WithAddress("0xBBB").Build(),
    ];

    private IRenderedComponent<WalletListPanel> RenderPanel(
        List<WalletDto>? wallets = null,
        bool isLoading = false,
        string? error = null,
        string? deletingAddress = null) =>
        Render<WalletListPanel>(ps => ps
            .Add(p => p.Wallets, wallets ?? [])
            .Add(p => p.IsLoading, isLoading)
            .Add(p => p.Error, error)
            .Add(p => p.DeletingAddress, deletingAddress)
            .Add(p => p.ShowDefaultIndicator, false)
            .Add(p => p.EmptyTitle, "No Wallets Found"));

    [Fact]
    public void Loading_ShowsSpinnerAndMessage()
    {
        var cut = RenderPanel(isLoading: true);

        cut.Markup.Should().Contain("Loading wallets");
        cut.FindAll(".mud-progress-circular").Should().NotBeEmpty();
    }

    [Fact]
    public void Error_ShowsAlert()
    {
        var cut = RenderPanel(error: "Failed to load wallets: timeout");

        cut.Markup.Should().Contain("Failed to load wallets: timeout");
    }

    [Fact]
    public void Empty_ShowsEmptyState()
    {
        var cut = RenderPanel(wallets: []);

        cut.Markup.Should().Contain("No Wallets Found");
    }

    [Fact]
    public void RendersWalletNames()
    {
        var cut = RenderPanel(wallets: TwoWallets());

        cut.Markup.Should().Contain("Treasury");
        cut.Markup.Should().Contain("Operations");
    }

    [Fact]
    public void NoDelete_InFlight_RendersNoRowSpinner()
    {
        var cut = RenderPanel(wallets: TwoWallets());

        // No load, no delete → there should be no progress spinner anywhere.
        cut.FindAll(".mud-progress-circular").Should().BeEmpty();
    }

    [Fact]
    public void DeletingAddress_ShowsRowSpinnerAndDisablesOtherDeletes()
    {
        var cut = RenderPanel(wallets: TwoWallets(), deletingAddress: "0xAAA");

        // The deleting row swaps its delete button for a spinner …
        cut.FindAll(".mud-progress-circular").Should().ContainSingle();
        // … and the sibling delete button is disabled to block a double-submit.
        cut.FindAll("button[disabled]").Should().NotBeEmpty();
    }
}
