// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Sorcha.Wallet.Pwa.Services.Actions;
using Sorcha.Wallet.Pwa.Services.Actions.Models;
using Xunit;

namespace Sorcha.Wallet.Pwa.Tests.Actions;

/// <summary>
/// Feature 151 — ordering rule for the inbox: Urgent → Warning → Normal, then earliest
/// <c>Deadline</c> first (nulls last), then earliest <c>ReceivedAt</c>. Single source of truth for
/// "most pressing first" (SC: ordering).
/// </summary>
public sealed class PendingActionOrderingTests
{
    private static PendingActionItem Item(
        string id, ActionUrgency urgency, DateTimeOffset? deadline, DateTimeOffset receivedAt) =>
        new(id, 1, $"t-{id}", "wf", null, null, urgency, deadline, receivedAt, null);

    private static readonly DateTimeOffset T0 = new(2026, 6, 13, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Order_SortsByUrgencyDescending_First()
    {
        var items = new[]
        {
            Item("normal", ActionUrgency.Normal, null, T0),
            Item("urgent", ActionUrgency.Urgent, null, T0),
            Item("warning", ActionUrgency.Warning, null, T0),
        };

        var ordered = PendingActionOrdering.Order(items).Select(i => i.InstanceId).ToList();

        ordered.Should().Equal("urgent", "warning", "normal");
    }

    [Fact]
    public void Order_WithinSameUrgency_SortsByDeadlineAscending_NullsLast()
    {
        var items = new[]
        {
            Item("no-deadline", ActionUrgency.Normal, null, T0),
            Item("later", ActionUrgency.Normal, T0.AddDays(5), T0),
            Item("sooner", ActionUrgency.Normal, T0.AddDays(1), T0),
        };

        var ordered = PendingActionOrdering.Order(items).Select(i => i.InstanceId).ToList();

        ordered.Should().Equal("sooner", "later", "no-deadline");
    }

    [Fact]
    public void Order_FinalTiebreak_IsReceivedAtAscending()
    {
        var items = new[]
        {
            Item("newer", ActionUrgency.Normal, null, T0.AddHours(2)),
            Item("older", ActionUrgency.Normal, null, T0),
        };

        var ordered = PendingActionOrdering.Order(items).Select(i => i.InstanceId).ToList();

        ordered.Should().Equal("older", "newer");
    }

    [Fact]
    public void Order_DoesNotMutateInput_AndHandlesEmpty()
    {
        PendingActionOrdering.Order(Array.Empty<PendingActionItem>()).Should().BeEmpty();
    }
}
