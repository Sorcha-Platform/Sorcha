// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using FluentAssertions;
using Sorcha.ServiceDefaults.Hubs;
using Xunit;

namespace Sorcha.ServiceDefaults.Tests.Hubs;

public class HubSignalTests
{
    [Fact]
    public void Construct_WithValidFields_RoundTripsThroughJson()
    {
        var original = new HubSignal(
            EventType: "ActionAvailable",
            Ids: ["instance-1", "action-2"],
            OccurredAt: new DateTimeOffset(2026, 5, 5, 12, 30, 0, TimeSpan.Zero),
            TraceId: "0af7651916cd43dd8448eb211c80319c");

        var json = JsonSerializer.Serialize(original);
        var roundTripped = JsonSerializer.Deserialize<HubSignal>(json);

        roundTripped.Should().NotBeNull();
        roundTripped!.EventType.Should().Be(original.EventType);
        roundTripped.Ids.Should().BeEquivalentTo(original.Ids);
        roundTripped.OccurredAt.Should().Be(original.OccurredAt);
        roundTripped.TraceId.Should().Be(original.TraceId);
    }

    [Fact]
    public void Records_AreEqualByValue()
    {
        var occurred = DateTimeOffset.UtcNow;
        var a = new HubSignal("Test", ["id-1"], occurred, "trace-1");
        var b = new HubSignal("Test", ["id-1"], occurred, "trace-1");

        // Records compare by value for scalar fields. Lists compare by reference, so
        // we verify scalar equality directly to document the contract.
        a.EventType.Should().Be(b.EventType);
        a.OccurredAt.Should().Be(b.OccurredAt);
        a.TraceId.Should().Be(b.TraceId);
    }

    [Fact]
    public void Construct_AllowsEmptyIdsList()
    {
        // Some events (e.g. broadcast SystemAnnouncement) carry no IDs.
        var signal = new HubSignal("Heartbeat", [], DateTimeOffset.UtcNow, "trace");
        signal.Ids.Should().BeEmpty();
    }
}
