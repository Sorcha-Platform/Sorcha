// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
using System.Runtime.CompilerServices;
using Sorcha.Agent.Inbox;
using Sorcha.Agent.Models;

namespace Sorcha.Agent.Tests.Inbox;

public class CompositeInboxListenerTests
{
    private static PendingAction CreateAction(string actionId) => new()
    {
        ActionId = actionId,
        ActionName = "TestAction",
        ActionIndex = 1,
        BlueprintId = "bp-1",
        InstanceId = "inst-1",
        RegisterId = "reg-1",
        TransactionId = $"tx-{actionId}"
    };

    private class FakeInboxListener(params PendingAction[] actions) : IInboxListener
    {
        public async IAsyncEnumerable<PendingAction> ListenAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (var action in actions)
            {
                if (cancellationToken.IsCancellationRequested) yield break;
                await Task.Yield();
                yield return action;
            }
        }
    }

    [Fact]
    public async Task ListenAsync_DeduplicatesByActionId()
    {
        var action1 = CreateAction("act-1");
        var action1Dup = CreateAction("act-1"); // Same ID
        var action2 = CreateAction("act-2");

        var listener1 = new FakeInboxListener(action1, action2);
        var listener2 = new FakeInboxListener(action1Dup);

        var composite = new CompositeInboxListener(listener1, listener2);

        var received = new List<PendingAction>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await foreach (var action in composite.ListenAsync(cts.Token))
        {
            received.Add(action);
            if (received.Count >= 2) break;
        }

        received.Should().HaveCount(2);
        received.Select(a => a.ActionId).Should().BeEquivalentTo(["act-1", "act-2"]);
    }

    [Fact]
    public async Task ListenAsync_MergesFromMultipleSources()
    {
        var listener1 = new FakeInboxListener(CreateAction("act-1"));
        var listener2 = new FakeInboxListener(CreateAction("act-2"));

        var composite = new CompositeInboxListener(listener1, listener2);

        var received = new List<PendingAction>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await foreach (var action in composite.ListenAsync(cts.Token))
        {
            received.Add(action);
            if (received.Count >= 2) break;
        }

        received.Should().HaveCount(2);
    }

    [Fact]
    public async Task ListenAsync_SkipsAlreadyProcessedActions()
    {
        var action = CreateAction("act-1");
        var listener = new FakeInboxListener(action, action, action);

        var composite = new CompositeInboxListener(listener);

        var received = new List<PendingAction>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        try
        {
            await foreach (var a in composite.ListenAsync(cts.Token))
            {
                received.Add(a);
            }
        }
        catch (OperationCanceledException) { }

        received.Should().HaveCount(1);
    }
}
