// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.AspNetCore.SignalR.Client;
using Xunit;

namespace Sorcha.Blueprint.Service.Tests.Integration;

/// <summary>
/// Feature 118 T034 — guards the EventsHub retirement (Phase 10). Once
/// the hub is decommissioned per spec FR-038, the legacy <c>/eventshub</c>
/// route must respond with HTTP 410 Gone carrying a deprecation message
/// pointing clients at TenantHub / WalletHub / BlueprintHub.
/// </summary>
/// <remarks>
/// <para>
/// Currently <c>[Fact(Skip = "awaiting decommission step")]</c> because
/// EventsHub still hosts InboundActionReceived / EventReceived /
/// CredentialStatusChanged / EncryptionOperationCompleted (DeferredExemptions
/// in <c>ThinSignalContractTests</c> reflect the same).
/// </para>
/// <para>
/// Un-skip in T121 alongside removing the EventsHub class, the four
/// IEventsHubClient methods, the bridge service, and the
/// AddSorchaHub&lt;EventsHub,IEventsHubClient&gt; registration. Update
/// HubTopologyTests.ExpectedHubFqns to drop EventsHub at the same time.
/// </para>
/// </remarks>
public class EventsHubRetirementTests
{
    [Fact(Skip = "awaiting decommission step (T121 / Phase 10)")]
    public async Task LegacyEventsHubRoute_ReturnsGoneWithDeprecationMessage()
    {
        // Arrange — caller hits the legacy /eventshub route after retirement.
        // The expected response shape is HTTP 410 Gone carrying a body that
        // names the destination hubs (TenantHub for inbox, WalletHub for
        // credential events, BlueprintHub for action lifecycle).
        var connection = new HubConnectionBuilder()
            .WithUrl("http://localhost/eventshub")
            .Build();

        // Act
        var act = async () => await connection.StartAsync();

        // Assert — connection refusal carrying 410 status and the deprecation
        // body. The exact body shape is TBD pending T121; this assertion
        // tightens at decommission time.
        var ex = await act.Should().ThrowAsync<Exception>();
        ex.Which.Message.Should().Contain("410");
    }
}
