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
/// Polls the pending actions endpoint on a configurable timer.
/// </summary>
public class PollingInboxListener : IInboxListener
{
    private readonly HttpClient _httpClient;
    private readonly AgentAuthService _authService;
    private readonly int _intervalSeconds;
    private readonly ILogger<PollingInboxListener> _logger;

    public PollingInboxListener(
        HttpClient httpClient,
        AgentAuthService authService,
        int intervalSeconds,
        ILogger<PollingInboxListener> logger)
    {
        _httpClient = httpClient;
        _authService = authService;
        _intervalSeconds = intervalSeconds;
        _logger = logger;
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
                _logger.LogDebug("Poll: {Count} pending actions", actions.Count);
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

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/actions/pending?page=1&pageSize=50");
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
            ActionId = item.GetProperty("actionId").GetString() ?? item.GetProperty("id").GetString() ?? "",
            ActionName = item.TryGetProperty("actionName", out var name) ? name.GetString() ?? "" : "",
            ActionIndex = item.TryGetProperty("actionIndex", out var idx) ? idx.GetUInt32() : 0,
            BlueprintId = item.TryGetProperty("blueprintId", out var bp) ? bp.GetString() ?? "" : "",
            InstanceId = item.TryGetProperty("instanceId", out var inst) ? inst.GetString() ?? "" : "",
            RegisterId = item.TryGetProperty("registerId", out var reg) ? reg.GetString() ?? "" : "",
            TransactionId = item.TryGetProperty("transactionId", out var tx) ? tx.GetString() ?? "" : "",
            SenderAddress = item.TryGetProperty("senderAddress", out var sender) ? sender.GetString() : null,
            PreviousPayload = item.TryGetProperty("payload", out var payload) ? payload.Clone() : null,
            Schema = item.TryGetProperty("schema", out var schema) ? schema.Clone() : null
        };
    }
}
