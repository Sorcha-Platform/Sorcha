// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
using System.Text.Json;
using Sorcha.Agent.Inbox;
using Sorcha.Agent.Models;

namespace Sorcha.Agent.Tests.Inbox;

/// <summary>
/// Feature 176 US1/US2 — the disclosed-data enrichment step. A successful fetch populates the pending
/// action's previous payload from the fields disclosed to the agent's participant; a fetch failure or a
/// non-recipient response yields a fail-closed hold (no payload, no decision).
/// </summary>
public class DisclosedPayloadFetchTests
{
    private static PendingAction Action() => new()
    {
        ActionId = "inst-1-2",
        ActionName = "Verify Assured Identity Application",
        ActionIndex = 2,
        BlueprintId = "bp-1",
        InstanceId = "inst-1",
        RegisterId = "reg-1",
        TransactionId = "tx-1",
    };

    /// <summary>Scriptable <see cref="IDisclosedDataClient"/> that records the arguments it was called with.</summary>
    private sealed class StubClient(DisclosedDataResult? result) : IDisclosedDataClient
    {
        public string? LastInstanceId { get; private set; }
        public uint LastActionId { get; private set; }

        public Task<DisclosedDataResult?> GetDisclosedDataAsync(string instanceId, uint actionId, CancellationToken ct)
        {
            LastInstanceId = instanceId;
            LastActionId = actionId;
            return Task.FromResult(result);
        }
    }

    [Fact]
    public async Task EnrichAsync_DisclosedDataAvailable_FetchesByInstanceAndAction_SetsPreviousPayload()
    {
        var fields = JsonSerializer.Deserialize<JsonElement>(
            """{ "name": { "fullName": "Ada Lovelace" }, "address": { "postcode": "SW1A 1AA" } }""");
        var client = new StubClient(new DisclosedDataResult(RecipientResolved: true, fields));

        var outcome = await new DisclosedPayloadEnricher(client).EnrichAsync(Action(), CancellationToken.None);

        // Fetched keyed by (instanceId, actionId).
        client.LastInstanceId.Should().Be("inst-1");
        client.LastActionId.Should().Be(2u);

        outcome.ShouldHold.Should().BeFalse();
        outcome.Action.PreviousPayload.Should().NotBeNull();
        outcome.Action.PreviousPayload!.Value.GetProperty("address").GetProperty("postcode").GetString()
            .Should().Be("SW1A 1AA");
    }

    [Fact]
    public async Task EnrichAsync_FetchFails_ReturnsHold_NoPayloadSubmitted()
    {
        // Transport failure — the client returns null. Must hold, not decide on a blank view.
        var outcome = await new DisclosedPayloadEnricher(new StubClient(null)).EnrichAsync(Action(), CancellationToken.None);

        outcome.ShouldHold.Should().BeTrue();
        outcome.HoldReason.Should().Contain("unavailable");
        outcome.Action.PreviousPayload.Should().BeNull();
    }

    [Fact]
    public async Task EnrichAsync_CallerNotADisclosureRecipient_ReturnsHold()
    {
        var client = new StubClient(new DisclosedDataResult(RecipientResolved: false, DisclosedFields: null));

        var outcome = await new DisclosedPayloadEnricher(client).EnrichAsync(Action(), CancellationToken.None);

        outcome.ShouldHold.Should().BeTrue();
        outcome.Action.PreviousPayload.Should().BeNull();
    }
}
