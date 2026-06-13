// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sorcha.Wallet.Pwa.Services.Actions;
using Sorcha.Wallet.Pwa.Services.Actions.Models;
using Sorcha.Wallet.Pwa.Services.Applications;
using Sorcha.Wallet.Pwa.Services.Drafts;
using Sorcha.Wallet.Pwa.Services.Drafts.Models;
using Xunit;

namespace Sorcha.Wallet.Pwa.Tests.Drafts;

/// <summary>
/// Feature 152 US2 — `ActionContextCache` pre-caches each pending action's form context for offline
/// open, reads cached contexts back, and tolerates per-item load failures.
/// </summary>
public sealed class ActionContextCacheTests
{
    private readonly Mock<IEncryptedObjectStore> _store = new();
    private readonly Mock<IMyActionsClient> _actions = new();
    private readonly Mock<IApplicationActionClient> _actionClient = new();

    private ActionContextCache Create() => new(
        _store.Object, _actions.Object, _actionClient.Object, TimeProvider.System,
        NullLogger<ActionContextCache>.Instance);

    private static PendingActionItem Pending(string instanceId) =>
        new(instanceId, 1, "t", "wf", null, null, ActionUrgency.Normal, null, DateTimeOffset.UtcNow, null);

    private static ApplicationFormContext FormCtx(Guid instanceId) =>
        new(instanceId, new Sorcha.Blueprint.Models.Action(), "bp-1", "reg-1", "ws1qcitizen", 1, "Apply");

    [Fact]
    public async Task RefreshFromPendingAsync_CachesEachPendingContext()
    {
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        _actions.Setup(a => a.GetPendingAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PendingActionItem> { Pending(id1.ToString()), Pending(id2.ToString()) });
        _actionClient.Setup(c => c.LoadFormAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid g, CancellationToken _) => FormCtx(g));

        var cached = await Create().RefreshFromPendingAsync();

        cached.Should().Be(2);
        _store.Verify(s => s.PutAsync("actionContext", It.IsAny<string>(), It.IsAny<CachedActionContext>(),
            It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task RefreshFromPendingAsync_OneLoadFailure_DoesNotAbortOthers()
    {
        var ok = Guid.NewGuid();
        var bad = Guid.NewGuid();
        _actions.Setup(a => a.GetPendingAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PendingActionItem> { Pending(bad.ToString()), Pending(ok.ToString()) });
        _actionClient.Setup(c => c.LoadFormAsync(bad, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));
        _actionClient.Setup(c => c.LoadFormAsync(ok, It.IsAny<CancellationToken>()))
            .ReturnsAsync(FormCtx(ok));

        var cached = await Create().RefreshFromPendingAsync();

        cached.Should().Be(1);
    }

    [Fact]
    public async Task RefreshFromPendingAsync_PendingUnavailable_ReturnsZero()
    {
        _actions.Setup(a => a.GetPendingAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new System.Net.Http.HttpRequestException("offline"));

        var cached = await Create().RefreshFromPendingAsync();

        cached.Should().Be(0);
    }

    [Fact]
    public async Task GetAsync_ReturnsCachedContext()
    {
        var ctx = new CachedActionContext { InstanceId = "i", ActionId = 2, BlueprintId = "bp", ActionJson = "{}" };
        _store.Setup(s => s.GetAsync<CachedActionContext>("actionContext", "i:2", It.IsAny<CancellationToken>()))
            .ReturnsAsync(ctx);

        var result = await Create().GetAsync("i", 2);

        result.Should().BeSameAs(ctx);
    }

    [Fact]
    public async Task GetAsync_UnknownAction_ReturnsNull()
    {
        _store.Setup(s => s.GetAsync<CachedActionContext>("actionContext", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CachedActionContext?)null);

        var result = await Create().GetAsync("i", 99);

        result.Should().BeNull();
    }
}
