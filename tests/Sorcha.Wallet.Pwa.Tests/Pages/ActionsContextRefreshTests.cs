// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
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
/// Feature 153 (D, US3) — the inbox re-loads when the active capacity changes, so it never shows the
/// previous capacity's work.
/// </summary>
public sealed class ActionsContextRefreshTests : ComponentTestFixture
{
    private readonly Mock<IMyActionsClient> _client = new();
    private readonly FakeUserContext _userContext = new();

    public ActionsContextRefreshTests()
    {
        _client.Setup(c => c.GetPendingAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<PendingActionItem>());
        Services.AddSingleton(_client.Object);
        Services.AddSingleton(Mock.Of<IPendingApplicationClient>());
        Services.AddSingleton(Mock.Of<IActionContextCache>());
        var drafts = new Mock<IDraftStore>();
        drafts.Setup(d => d.ListAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<ActionDraft>());
        Services.AddSingleton(drafts.Object);
        var queue = new Mock<ISubmitQueue>();
        queue.Setup(q => q.ListAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<QueuedSubmission>());
        Services.AddSingleton(queue.Object);
        Services.AddSingleton<IUserContext>(_userContext);
    }

    [Fact]
    public async Task ContextChange_ReloadsInbox()
    {
        var cut = Render<ActionsPage>();
        _client.Invocations.Clear();

        await cut.InvokeAsync(() => _userContext.RaiseContextChanged(null, Guid.NewGuid()));

        _client.Verify(c => c.GetPendingAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.AtLeastOnce());
    }

    private sealed class FakeUserContext : IUserContext
    {
        public Guid? ActiveContextOrgId { get; private set; }
        public event Func<UserContextChangedEventArgs, Task>? OnContextChanged;
        public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task<bool> SetActiveContextAsync(Guid? orgId, CancellationToken ct = default)
        { ActiveContextOrgId = orgId; return Task.FromResult(true); }
        public Task RaiseContextChanged(Guid? from, Guid? to)
        {
            ActiveContextOrgId = to;
            return OnContextChanged?.Invoke(new UserContextChangedEventArgs(from, to)) ?? Task.CompletedTask;
        }
    }
}
