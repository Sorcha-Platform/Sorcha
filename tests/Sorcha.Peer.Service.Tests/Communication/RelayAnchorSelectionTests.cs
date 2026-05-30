// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.Peer.Service.Communication;

namespace Sorcha.Peer.Service.Tests.Communication;

/// <summary>
/// Unit tests for the anchor send-ordering policy (Feature 143 US3): a NAT'd node sending over its
/// reverse-stream anchors prefers the anchor that IS the target, then the lowest-latency connected
/// anchor, then the most recently active.
/// </summary>
public class RelayAnchorSelectionTests
{
    private static RelayCommunicationService.Anchor A(
        string id, int latencyMs = 0, long activityTicks = 0) => new()
    {
        AnchorId = id,
        Address = $"http://{id}:50051",
        Connected = true,
        LatencyMs = latencyMs,
        LastActivityTicks = activityTicks
    };

    [Fact]
    public void OrderAnchorsForSend_PrefersTargetMatch_OverLowerLatency()
    {
        var anchors = new[] { A("n2", latencyMs: 1), A("n1", latencyMs: 50) };

        var ordered = RelayCommunicationService.OrderAnchorsForSend(anchors, "n1");

        ordered[0].AnchorId.Should().Be("n1", "the anchor that IS the target is the zero-extra-hop path");
    }

    [Fact]
    public void OrderAnchorsForSend_NoTargetMatch_PrefersLowestLatency()
    {
        var anchors = new[] { A("n1", latencyMs: 50), A("n2", latencyMs: 10), A("n3", latencyMs: 30) };

        var ordered = RelayCommunicationService.OrderAnchorsForSend(anchors, "owner-not-an-anchor");

        ordered.Select(a => a.AnchorId).Should().ContainInOrder("n2", "n3", "n1");
    }

    [Fact]
    public void OrderAnchorsForSend_UnknownLatency_RanksAfterKnown()
    {
        var anchors = new[] { A("unknown", latencyMs: 0), A("known", latencyMs: 99) };

        var ordered = RelayCommunicationService.OrderAnchorsForSend(anchors, "x");

        ordered[0].AnchorId.Should().Be("known", "a known 99ms beats unknown (treated as max)");
    }

    [Fact]
    public void OrderAnchorsForSend_EqualLatency_PrefersMostRecentlyActive()
    {
        var anchors = new[] { A("stale", latencyMs: 10, activityTicks: 100), A("fresh", latencyMs: 10, activityTicks: 200) };

        var ordered = RelayCommunicationService.OrderAnchorsForSend(anchors, "x");

        ordered[0].AnchorId.Should().Be("fresh");
    }

    [Fact]
    public void OrderAnchorsForSend_Empty_ReturnsEmpty()
    {
        RelayCommunicationService.OrderAnchorsForSend(System.Array.Empty<RelayCommunicationService.Anchor>(), "x")
            .Should().BeEmpty();
    }
}
