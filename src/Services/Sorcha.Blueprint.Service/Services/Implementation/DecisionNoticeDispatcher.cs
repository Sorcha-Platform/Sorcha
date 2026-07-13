// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using System.Text.Json.Nodes;
using Sorcha.Blueprint.Models;

namespace Sorcha.Blueprint.Service.Services.Implementation;

/// <summary>
/// Feature 183 (US2) — resolves and dispatches <c>x-decision-notice</c> route annotations after
/// routing is decided. For each route that was TAKEN and carries a decision-notice, it resolves the
/// recipient participant's wallet (from the instance participant bindings) and the on-brand reason
/// (from the submitted payload), then writes a durable inbox entry via the supplied writer delegate.
/// Pure and fail-safe: the whole dispatch is wrapped so a notice failure never affects sealing or
/// routing. Kept free of service dependencies (evaluator + writer are delegates) so the
/// routing → notice resolution is unit-testable in isolation.
/// </summary>
internal static class DecisionNoticeDispatcher
{
    /// <summary>The inbox-write delegate: (recipientWallet, instanceId, actionId, title, reason, severity, ct).</summary>
    internal delegate Task WriteDecision(
        string recipientWallet, string instanceId, string actionId,
        string title, string reason, string severity, CancellationToken ct);

    /// <summary>
    /// Fires a decision notice for every taken route carrying an <c>x-decision-notice</c>.
    /// A route is "taken" when its next-action set matches <paramref name="routingResult"/> and its
    /// condition (if any) is truthy per <paramref name="conditionMatches"/>.
    /// </summary>
    internal static async Task DispatchAsync(
        Sorcha.Blueprint.Models.Action actionDef,
        string instanceId,
        IReadOnlyDictionary<string, string> participantWallets,
        IReadOnlyDictionary<string, object> mergedData,
        RoutingResult routingResult,
        Func<JsonNode, bool> conditionMatches,
        WriteDecision write,
        Microsoft.Extensions.Logging.ILogger logger,
        CancellationToken ct)
    {
        try
        {
            if (actionDef.Routes is null)
                return;

            // Which next-action set was actually taken (sorted for order-independent comparison).
            var takenActionIds = routingResult.NextActions.Select(n => n.ActionId).OrderBy(x => x).ToArray();

            foreach (var route in actionDef.Routes)
            {
                var notice = route.DecisionNotice;
                if (notice is null || string.IsNullOrWhiteSpace(notice.RecipientParticipantId))
                    continue;

                // A route was taken iff its next-action set matches the routing result AND its
                // condition (if any) is truthy — this disambiguates two terminal routes.
                var routeActionIds = (route.NextActionIds ?? Enumerable.Empty<int>()).OrderBy(x => x).ToArray();
                if (!routeActionIds.SequenceEqual(takenActionIds))
                    continue;
                if (route.Condition is not null && !conditionMatches(route.Condition))
                    continue;

                if (!participantWallets.TryGetValue(notice.RecipientParticipantId, out var wallet) ||
                    string.IsNullOrWhiteSpace(wallet))
                {
                    logger.LogDebug(
                        "Decision notice: recipient participant {ParticipantId} is not bound to a wallet on instance {InstanceId}; skipping.",
                        notice.RecipientParticipantId, instanceId);
                    continue;
                }

                var reason = ResolvePointerString(mergedData, notice.ReasonField) ?? string.Empty;
                var title = string.IsNullOrWhiteSpace(notice.Title) ? "Application decision" : notice.Title;
                var severity = string.IsNullOrWhiteSpace(notice.Severity) ? "Warning" : notice.Severity!;

                await write(wallet, instanceId, actionDef.Id.ToString(), title, reason, severity, ct)
                    .ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            // A decision-notice dispatch failure must never affect sealing or routing.
            logger.LogWarning(ex,
                "Decision-notice dispatch failed for action {ActionId} on instance {InstanceId} — decision unaffected.",
                actionDef.Id, instanceId);
        }
    }

    /// <summary>
    /// Resolves a JSON-Pointer path against the merged action payload to a string. Handles the
    /// top-level and nested cases the reason field needs (dictionary or <see cref="JsonElement"/>
    /// nodes). Returns null when the pointer is empty or unresolvable.
    /// </summary>
    private static string? ResolvePointerString(IReadOnlyDictionary<string, object> data, string? pointer)
    {
        if (string.IsNullOrWhiteSpace(pointer))
            return null;

        var segments = pointer.TrimStart('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        object? current = data;

        foreach (var segment in segments)
        {
            switch (current)
            {
                case IReadOnlyDictionary<string, object> dict when dict.TryGetValue(segment, out var next):
                    current = next;
                    break;
                case JsonElement je when je.ValueKind == JsonValueKind.Object && je.TryGetProperty(segment, out var prop):
                    current = prop;
                    break;
                default:
                    return null;
            }
        }

        return current switch
        {
            null => null,
            string s => s,
            JsonElement je => je.ValueKind == JsonValueKind.String ? je.GetString() : je.ToString(),
            _ => current.ToString(),
        };
    }
}
