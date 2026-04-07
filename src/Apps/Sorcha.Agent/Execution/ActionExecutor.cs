// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Sorcha.Agent.Auth;
using Sorcha.Agent.Models;

namespace Sorcha.Agent.Execution;

/// <summary>
/// Executes actions by submitting payloads to the Blueprint Service.
/// </summary>
public class ActionExecutor : IActionExecutor
{
    private readonly HttpClient _httpClient;
    private readonly AgentAuthService _authService;
    private readonly string _walletAddress;
    private readonly string _registerId;
    private readonly ILogger<ActionExecutor> _logger;
    private readonly AuditLogger _auditLogger;

    public ActionExecutor(
        HttpClient httpClient,
        AgentAuthService authService,
        string walletAddress,
        string registerId,
        ILogger<ActionExecutor> logger,
        AuditLogger auditLogger)
    {
        _httpClient = httpClient;
        _authService = authService;
        _walletAddress = walletAddress;
        _registerId = registerId;
        _logger = logger;
        _auditLogger = auditLogger;
    }

    public async Task<bool> ExecuteAsync(PendingAction action, ActionDecision decision, CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var entry = new ActionAuditEntry
        {
            Timestamp = DateTimeOffset.UtcNow,
            ActionId = action.ActionId,
            ActionName = action.ActionName,
            Decision = decision.Decision,
            Success = false,
            DurationMs = 0
        };

        try
        {
            if (decision.Decision == "skip")
            {
                entry = entry with { Success = true, DurationMs = sw.ElapsedMilliseconds };
                await _auditLogger.LogAsync(entry, cancellationToken);
                return true;
            }

            var token = await _authService.GetTokenAsync(cancellationToken);

            var endpoint = decision.Decision == "reject"
                ? $"/api/instances/{action.InstanceId}/actions/{action.ActionIndex}/reject"
                : $"/api/instances/{action.InstanceId}/actions/{action.ActionIndex}/execute";

            var body = decision.Decision == "reject"
                ? (object)new
                {
                    reason = decision.Payload?.GetValueOrDefault("reason")?.ToString() ?? "Rejected by agent rule",
                    senderWallet = _walletAddress,
                    registerAddress = _registerId
                }
                : new
                {
                    blueprintId = action.BlueprintId,
                    actionId = action.ActionIndex.ToString(),
                    instanceId = action.InstanceId,
                    senderWallet = _walletAddress,
                    registerAddress = _registerId,
                    payloadData = decision.Payload
                };

            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = JsonContent.Create(body)
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Add("X-Delegation-Token", token);

            var response = await _httpClient.SendAsync(request, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Action {ActionName} {Decision} submitted successfully",
                    action.ActionName, decision.Decision);
                entry = entry with { Success = true, DurationMs = sw.ElapsedMilliseconds };
                await _auditLogger.LogAsync(entry, cancellationToken);
                return true;
            }

            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError("Action submission failed: {StatusCode} {Error}",
                response.StatusCode, errorBody);
            entry = entry with { Error = $"{response.StatusCode}: {errorBody}", DurationMs = sw.ElapsedMilliseconds };
            await _auditLogger.LogAsync(entry, cancellationToken);
            return false;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Action execution failed for {ActionName}", action.ActionName);
            entry = entry with { Error = ex.Message, DurationMs = sw.ElapsedMilliseconds };
            await _auditLogger.LogAsync(entry, cancellationToken);
            return false;
        }
    }
}
