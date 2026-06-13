// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
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
/// Feature 151 US1 — bUnit tests for the "Things to do" inbox page: renders the citizen's
/// outstanding actions, empty state, base-relative navigation on row tap, and graceful
/// degradation on a refresh failure (last-known list retained, non-blocking notice).
/// </summary>
public sealed class ActionsInboxTests : ComponentTestFixture
{
    private readonly Mock<IMyActionsClient> _client = new();
    private readonly Mock<IPendingApplicationClient> _pending = new();

    public ActionsInboxTests()
    {
        Services.AddSingleton(_client.Object);
        Services.AddSingleton(_pending.Object);
        _pending.Setup(p => p.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync((PendingApplicationView?)null);
    }

    private static PendingActionItem Item(string id, string title) =>
        new(id, 1, title, "Blue Badge Application", null, null, ActionUrgency.Normal, null, DateTimeOffset.UtcNow, null);

    [Fact]
    public void RendersRow_PerOutstandingAction()
    {
        _client.Setup(c => c.GetPendingAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PendingActionItem> { Item("a1", "Upload proof"), Item("a2", "Confirm details") });

        var cut = Render<ActionsPage>();

        cut.FindAll("[data-testid=actions-list]").Should().ContainSingle();
        cut.FindAll("[data-testid^=actions-item-]").Should().HaveCount(2);
        cut.FindAll("[data-testid=actions-empty]").Should().BeEmpty();
    }

    [Fact]
    public void NoActions_ShowsEmptyState()
    {
        _client.Setup(c => c.GetPendingAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<PendingActionItem>());

        var cut = Render<ActionsPage>();

        cut.FindAll("[data-testid=actions-empty]").Should().ContainSingle();
        cut.FindAll("[data-testid=actions-list]").Should().BeEmpty();
    }

    [Fact]
    public void RowTap_NavigatesBaseRelativeToApplicationInstance()
    {
        _client.Setup(c => c.GetPendingAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PendingActionItem> { Item("11111111-1111-1111-1111-111111111111", "Upload proof") });
        var nav = Services.GetRequiredService<NavigationManager>();

        var cut = Render<ActionsPage>();
        cut.Find("[data-testid^=actions-item-]").Click();

        nav.Uri.Should().EndWith("applications/11111111-1111-1111-1111-111111111111");
    }

    [Fact]
    public void InitialLoadFailure_ShowsNotice_NotBlankError()
    {
        _client.Setup(c => c.GetPendingAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("offline"));

        var cut = Render<ActionsPage>();

        cut.FindAll("[data-testid=actions-refresh-error]").Should().ContainSingle();
    }

    [Fact]
    public async Task RefreshFailureAfterLoad_RetainsLastKnownList_AndShowsNotice()
    {
        _client.SetupSequence(c => c.GetPendingAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PendingActionItem> { Item("a1", "Upload proof") })
            .ThrowsAsync(new HttpRequestException("offline"));

        var cut = Render<ActionsPage>();
        cut.FindAll("[data-testid^=actions-item-]").Should().ContainSingle();

        await cut.InvokeAsync(() => cut.Instance.RefreshAsync());

        cut.FindAll("[data-testid^=actions-item-]").Should().ContainSingle("the last-known list is retained on refresh failure");
        cut.FindAll("[data-testid=actions-refresh-error]").Should().ContainSingle();
    }
}
