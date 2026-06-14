// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.Collections.Generic;
using System.Threading;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Sorcha.UI.Testing;
using Sorcha.Wallet.Pwa.Services;
using Sorcha.Wallet.Pwa.Services.Actions;
using Sorcha.Wallet.Pwa.Services.Actions.Models;
using Sorcha.Wallet.Pwa.Services.Drafts;
using Sorcha.Wallet.Pwa.Services.Drafts.Models;
using Xunit;
using ActionsPage = Sorcha.Wallet.Pwa.Pages.Actions;

namespace Sorcha.Wallet.Pwa.Tests.Pages;

/// <summary>
/// Feature 152 US3/US4 — the inbox surfaces queued submissions and, for a held (NeedsAttention)
/// item, shows the reason with discard / re-open choices; discard removes it (no silent loss).
/// </summary>
public sealed class ActionsQueueTests : ComponentTestFixture
{
    private readonly Mock<IMyActionsClient> _client = new();
    private readonly Mock<IPendingApplicationClient> _pending = new();
    private readonly Mock<IDraftStore> _drafts = new();
    private readonly Mock<ISubmitQueue> _queue = new();

    public ActionsQueueTests()
    {
        Services.AddSingleton(_client.Object);
        Services.AddSingleton(_pending.Object);
        Services.AddSingleton(Mock.Of<IActionContextCache>());
        Services.AddSingleton(_drafts.Object);
        Services.AddSingleton(_queue.Object);
        Services.AddSingleton(Mock.Of<Sorcha.Wallet.Pwa.Services.Context.IUserContext>());
        _client.Setup(c => c.GetPendingAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<PendingActionItem>());
        _pending.Setup(p => p.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync((PendingApplicationView?)null);
        _drafts.Setup(d => d.ListAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<ActionDraft>());
    }

    private QueuedSubmission Held() => new()
    {
        QueuedKey = "q1", InstanceId = "inst-1", ActionId = 1, BlueprintId = "bp-1",
        State = QueuedSubmissionState.NeedsAttention, ConflictReason = ConflictReason.StepMovedOn,
    };

    [Fact]
    public void NeedsAttentionItem_ShowsReason_AndDiscardReopen()
    {
        _queue.Setup(q => q.ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<QueuedSubmission> { Held() });

        var cut = Render<ActionsPage>();

        cut.Find("[data-testid=actions-queue-reason-q1]").TextContent.Should().Contain("moved on");
        cut.FindAll("[data-testid=actions-queue-discard-q1]").Should().ContainSingle();
        cut.FindAll("[data-testid=actions-queue-reopen-q1]").Should().ContainSingle();
    }

    [Fact]
    public void Discard_RemovesQueuedItem()
    {
        _queue.Setup(q => q.ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<QueuedSubmission> { Held() });

        var cut = Render<ActionsPage>();
        cut.Find("[data-testid=actions-queue-discard-q1]").Click();

        _queue.Verify(q => q.RemoveAsync("q1", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void QueuedItem_ShowsQueuedStatus()
    {
        _queue.Setup(q => q.ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<QueuedSubmission>
            {
                new() { QueuedKey = "q2", InstanceId = "i", ActionId = 1, BlueprintId = "b", State = QueuedSubmissionState.Queued },
            });

        var cut = Render<ActionsPage>();

        cut.FindAll("[data-testid=actions-queue-q2]").Should().ContainSingle();
    }
}
