// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Sorcha.Agent.Auth;
using Sorcha.Agent.Models;

namespace Sorcha.Agent.Inbox;

/// <summary>
/// Polls an action-listing endpoint on a configurable timer.
/// </summary>
/// <remarks>
/// Two endpoints answer in the same wire shape and are mapped by the same code here, on purpose:
/// <c>/api/actions/pending</c> (work assigned to this agent) and, when the actor opts in,
/// <c>/api/actions/open-starting</c> (Feature 103 workflows waiting for somebody to start them —
/// issue #1446). A second listener class would have meant a second copy of
/// <see cref="MapToPendingAction"/>, and a field added to one summary and not the other is exactly
/// the drift that class of duplication produces.
/// </remarks>
public class PollingInboxListener : IInboxListener
{
    /// <summary>Work already assigned to this agent's wallet.</summary>
    public const string PendingPath = "/api/actions/pending?page=1&pageSize=50";

    private readonly HttpClient _httpClient;
    private readonly AgentAuthService _authService;
    private readonly int _intervalSeconds;
    private readonly string _requestPath;
    private readonly ILogger<PollingInboxListener> _logger;

    public PollingInboxListener(
        HttpClient httpClient,
        AgentAuthService authService,
        int intervalSeconds,
        ILogger<PollingInboxListener> logger,
        string? requestPath = null)
    {
        _httpClient = httpClient;
        _authService = authService;
        _intervalSeconds = intervalSeconds;
        _requestPath = requestPath ?? PendingPath;
        _logger = logger;
    }

    /// <summary>
    /// Path for the open-starting query. <paramref name="blueprintId"/> is required by the endpoint;
    /// an unscoped query is refused rather than answered with every open instance on the node.
    /// </summary>
    public static string OpenStartingPath(string blueprintId, string? registerId)
    {
        var path = $"/api/actions/open-starting?blueprintId={Uri.EscapeDataString(blueprintId)}&page=1&pageSize=50";
        return string.IsNullOrWhiteSpace(registerId)
            ? path
            : $"{path}&registerId={Uri.EscapeDataString(registerId)}";
    }

    public async IAsyncEnumerable<PendingAction> ListenAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            List<PendingAction>? actions = null;
            try
            {
                actions = await PollAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                yield break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Poll failed, will retry at next interval");
            }

            if (actions is not null)
            {
                _logger.LogDebug("Poll {Path}: {Count} actions", _requestPath, actions.Count);
                foreach (var action in actions)
                {
                    yield return action;
                }
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(_intervalSeconds), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                yield break;
            }
        }
    }

    private async Task<List<PendingAction>> PollAsync(CancellationToken cancellationToken)
    {
        var token = await _authService.GetTokenAsync(cancellationToken);

        using var request = new HttpRequestMessage(HttpMethod.Get, _requestPath);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var doc = JsonDocument.Parse(json);

        var actions = new List<PendingAction>();
        if (doc.RootElement.TryGetProperty("items", out var items))
        {
            foreach (var item in items.EnumerateArray())
            {
                actions.Add(MapToPendingAction(item));
            }
        }

        return actions;
    }

    private static PendingAction MapToPendingAction(JsonElement item)
    {
        return new PendingAction
        {
            ActionId = $"{GetStringOrToString(item, "instanceId") ?? ""}-{GetStringOrToString(item, "actionId") ?? GetStringOrToString(item, "id") ?? ""}",
            ActionName = GetStringOrToString(item, "actionTitle") ?? GetStringOrToString(item, "actionName") ?? "",
            ActionIndex = item.TryGetProperty("actionIndex", out var idx) && idx.ValueKind == JsonValueKind.Number
                ? idx.GetUInt32()
                : item.TryGetProperty("actionId", out var aid) && aid.ValueKind == JsonValueKind.Number
                    ? aid.GetUInt32()
                    : 0,
            BlueprintId = GetStringOrToString(item, "blueprintId") ?? "",
            InstanceId = GetStringOrToString(item, "instanceId") ?? "",
            RegisterId = GetStringOrToString(item, "registerId") ?? "",
            TransactionId = GetStringOrToString(item, "transactionId") ?? "",
            SenderAddress = GetStringOrToString(item, "senderAddress"),
            // Feature 176: PreviousPayload is NOT sourced from the pending summary. The summary's
            // "prepopulatedPayload" is a Feature-104 form-prefill seed (empty for the AIAS verify action),
            // NOT the disclosed prior-action application data — reading it here is what left the agent
            // deciding on an empty payload (AIAS bad-postcode approved). PreviousPayload is populated
            // per-action from the disclosed-data endpoint by DisclosedPayloadEnricher before the decision.
            // The "dataSchema" correction stays (the API serialises the action schema as "dataSchema").
            PreviousPayload = null,
            Schema = item.TryGetProperty("dataSchema", out var schema) ? schema.Clone() : null
        };
    }

    private static string? GetStringOrToString(JsonElement item, string propertyName)
    {
        if (!item.TryGetProperty(propertyName, out var prop)) return null;
        return prop.ValueKind == JsonValueKind.String ? prop.GetString() : prop.ToString();
    }
}
