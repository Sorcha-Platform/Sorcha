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
using Sorcha.Wallet.Pwa.Services.Context;
using Sorcha.Wallet.Pwa.Services.Drafts;
using Sorcha.Wallet.Pwa.Services.Drafts.Models;
using Xunit;
using ActionsPage = Sorcha.Wallet.Pwa.Pages.Actions;

namespace Sorcha.Wallet.Pwa.Tests.Pages;

/// <summary>
/// Feature 153 (D, US2) — the inbox frames org-role work: an "acting as &lt;Org&gt;" banner shows
/// when the active capacity is an organisation, and is absent when acting personally.
/// </summary>
public sealed class ActionsActingAsTests : ComponentTestFixture
{
    private readonly Mock<IUserContext> _userContext = new();

    public ActionsActingAsTests()
    {
        Services.AddSingleton(Mock.Of<IMyActionsClient>(c =>
            c.GetPendingAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>())
                == System.Threading.Tasks.Task.FromResult((IReadOnlyList<PendingActionItem>)Array.Empty<PendingActionItem>())));
        Services.AddSingleton(Mock.Of<IPendingApplicationClient>());
        Services.AddSingleton(Mock.Of<IActionContextCache>());
        var drafts = new Mock<IDraftStore>();
        drafts.Setup(d => d.ListAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<ActionDraft>());
        Services.AddSingleton(drafts.Object);
        var queue = new Mock<ISubmitQueue>();
        queue.Setup(q => q.ListAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<QueuedSubmission>());
        Services.AddSingleton(queue.Object);
        Services.AddSingleton(_userContext.Object);
    }

    [Fact]
    public void OrgContext_ShowsActingAsBanner()
    {
        _userContext.SetupGet(c => c.ActiveContextOrgId).Returns(Guid.NewGuid());

        var cut = Render<ActionsPage>();

        cut.FindAll("[data-testid=actions-acting-as]").Should().ContainSingle();
    }

    [Fact]
    public void PersonalContext_NoActingAsBanner()
    {
        _userContext.SetupGet(c => c.ActiveContextOrgId).Returns((Guid?)null);

        var cut = Render<ActionsPage>();

        cut.FindAll("[data-testid=actions-acting-as]").Should().BeEmpty();
    }
}
