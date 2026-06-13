// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.Threading;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Sorcha.UI.Testing;
using Sorcha.Wallet.Pwa.Services;
using Sorcha.Wallet.Pwa.Services.Actions;
using Sorcha.Wallet.Pwa.Services.Actions.Models;
using Xunit;
using ActionsPage = Sorcha.Wallet.Pwa.Pages.Actions;

namespace Sorcha.Wallet.Pwa.Tests.Pages;

/// <summary>
/// Feature 151 US3 — the "In review" banner reuses the existing Feature-124 pending-application
/// notice: shown (distinct from the "needs you" rows) when a notice exists, hidden when absent.
/// </summary>
public sealed class ActionsInReviewBannerTests : ComponentTestFixture
{
    private readonly Mock<IMyActionsClient> _client = new();
    private readonly Mock<IPendingApplicationClient> _pending = new();

    public ActionsInReviewBannerTests()
    {
        Services.AddSingleton(_client.Object);
        Services.AddSingleton(_pending.Object);
        Services.AddSingleton(Mock.Of<Sorcha.Wallet.Pwa.Services.Drafts.IActionContextCache>());
        _client.Setup(c => c.GetPendingAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<PendingActionItem>());
    }

    [Fact]
    public void NoticePresent_ShowsInReviewBanner()
    {
        _pending.Setup(p => p.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PendingApplicationView("Blue Badge Application", DateTimeOffset.UtcNow));

        var cut = Render<ActionsPage>();

        var banner = cut.Find("[data-testid=actions-in-review]");
        banner.TextContent.Should().Contain("Blue Badge Application");
    }

    [Fact]
    public void NoNotice_HidesInReviewBanner()
    {
        _pending.Setup(p => p.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((PendingApplicationView?)null);

        var cut = Render<ActionsPage>();

        cut.FindAll("[data-testid=actions-in-review]").Should().BeEmpty();
    }
}
