// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
using Microsoft.Extensions.Logging;
using Sorcha.Agent.Models;

namespace Sorcha.Agent.Inbox;

/// <summary>
/// The outcome of enriching a pending action with its disclosed prior-action data (Feature 176).
/// </summary>
/// <param name="Action">
/// The action, with <see cref="PendingAction.PreviousPayload"/> populated from the disclosed data when
/// available. Unchanged when <see cref="ShouldHold"/> is true.
/// </param>
/// <param name="ShouldHold">
/// True when the disclosed data could not be obtained or the agent is not a disclosure recipient — the
/// host must hold the action (no approve/reject, no submission) and retry on the next poll.
/// </param>
/// <param name="HoldReason">The actionable reason logged/recorded when <see cref="ShouldHold"/> is true.</param>
public sealed record EnrichedAction(PendingAction Action, bool ShouldHold, string? HoldReason);

/// <summary>
/// Populates a pending action's previous payload from the register's disclosed prior-action data, and
/// signals a fail-closed hold when that data is unavailable (Feature 176, US1/US2).
/// </summary>
public interface IDisclosedPayloadEnricher
{
    /// <summary>Fetches the disclosed data for <paramref name="action"/> and returns the enrichment outcome.</summary>
    Task<EnrichedAction> EnrichAsync(PendingAction action, CancellationToken cancellationToken);
}

/// <summary>
/// Default enricher over an <see cref="IDisclosedDataClient"/>. On a fetch failure or a non-recipient /
/// empty response it returns a hold outcome (the agent never decides on a blank view); otherwise it sets
/// <see cref="PendingAction.PreviousPayload"/> to the disclosed fields so the external checks evaluate the
/// applicant's real submitted data.
/// </summary>
public sealed class DisclosedPayloadEnricher : IDisclosedPayloadEnricher
{
    private const string FetchFailedReason = "Disclosed application data unavailable; held for manual review";
    private const string NotDisclosedReason =
        "No application data disclosed to the agent's participant; held for manual review";

    private readonly IDisclosedDataClient _client;
    private readonly ILogger<DisclosedPayloadEnricher>? _logger;

    public DisclosedPayloadEnricher(IDisclosedDataClient client, ILogger<DisclosedPayloadEnricher>? logger = null)
    {
        _client = client;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<EnrichedAction> EnrichAsync(PendingAction action, CancellationToken cancellationToken)
    {
        var result = await _client.GetDisclosedDataAsync(action.InstanceId, action.ActionIndex, cancellationToken);

        if (result is null)
        {
            _logger?.LogError(
                "Disclosed-data fetch failed for {ActionName} ({InstanceId}/{ActionId}); holding (fail-closed).",
                action.ActionName, action.InstanceId, action.ActionIndex);
            return new EnrichedAction(action, ShouldHold: true, FetchFailedReason);
        }

        if (!result.RecipientResolved || result.DisclosedFields is null)
        {
            _logger?.LogError(
                "No disclosed data for {ActionName} ({InstanceId}/{ActionId}) — caller is not a disclosure recipient; holding (fail-closed).",
                action.ActionName, action.InstanceId, action.ActionIndex);
            return new EnrichedAction(action, ShouldHold: true, NotDisclosedReason);
        }

        _logger?.LogInformation(
            "Disclosed data fetched for {ActionName} ({InstanceId}/{ActionId})",
            action.ActionName, action.InstanceId, action.ActionIndex);

        return new EnrichedAction(
            action with { PreviousPayload = result.DisclosedFields }, ShouldHold: false, HoldReason: null);
    }
}
