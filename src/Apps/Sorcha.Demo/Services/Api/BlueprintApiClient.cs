// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Logging;
using Sorcha.Blueprint.Models;
using System.Text.Json;

namespace Sorcha.Demo.Services.Api;

/// <summary>
/// Client for Blueprint Service API including orchestration endpoints
/// </summary>
public class BlueprintApiClient : ApiClientBase
{
    private readonly string _baseUrl;

    public BlueprintApiClient(HttpClient httpClient, ILogger<BlueprintApiClient> logger, string baseUrl)
        : base(httpClient, logger)
    {
        _baseUrl = baseUrl.TrimEnd('/');
    }

    /// <summary>
    /// Creates a new blueprint
    /// </summary>
    public async Task<Sorcha.Blueprint.Models.Blueprint?> CreateBlueprintAsync(
        Sorcha.Blueprint.Models.Blueprint blueprint,
        CancellationToken ct = default)
    {
        return await PostAsync<Sorcha.Blueprint.Models.Blueprint, Sorcha.Blueprint.Models.Blueprint>($"{_baseUrl}/blueprints", blueprint, ct);
    }

    /// <summary>
    /// Gets a blueprint by ID
    /// </summary>
    public async Task<Sorcha.Blueprint.Models.Blueprint?> GetBlueprintAsync(string blueprintId, CancellationToken ct = default)
    {
        return await GetAsync<Sorcha.Blueprint.Models.Blueprint>($"{_baseUrl}/blueprints/{blueprintId}", ct);
    }

    /// <summary>
    /// Publishes a blueprint
    /// </summary>
    public async Task<Sorcha.Blueprint.Models.Blueprint?> PublishBlueprintAsync(string blueprintId, CancellationToken ct = default)
    {
        return await PostAsync<object, Sorcha.Blueprint.Models.Blueprint>($"{_baseUrl}/blueprints/{blueprintId}/publish", new { }, ct);
    }

    /// <summary>
    /// Creates a new blueprint instance for execution
    /// </summary>
    public async Task<BlueprintInstanceResponse?> CreateInstanceAsync(
        string blueprintId,
        string registerId,
        Dictionary<string, object>? metadata = null,
        string? tenantId = null,
        CancellationToken ct = default)
    {
        var request = new
        {
            blueprintId,
            registerId,
            tenantId,
            metadata = metadata ?? new Dictionary<string, object>()
        };

        return await PostAsync<object, BlueprintInstanceResponse>($"{_baseUrl}/instances", request, ct);
    }

    /// <summary>
    /// Executes an action in a blueprint instance (KEY ORCHESTRATION ENDPOINT)
    /// This handles: validation, execution, transaction building, signing, and Register submission
    /// </summary>
    public async Task<ActionExecutionResponse?> ExecuteActionAsync(
        string instanceId,
        int actionId,
        string blueprintId,
        Dictionary<string, object> actionData,
        string senderWallet,
        string registerAddress,
        string? previousTransactionHash = null,
        CancellationToken ct = default)
    {
        var request = new ActionSubmissionRequest
        {
            BlueprintId = blueprintId,
            ActionId = actionId.ToString(),
            InstanceId = instanceId,
            SenderWallet = senderWallet,
            RegisterAddress = registerAddress,
            PayloadData = actionData,
            PreviousTransactionHash = previousTransactionHash
        };

        return await PostAsync<ActionSubmissionRequest, ActionExecutionResponse>(
            $"{_baseUrl}/instances/{instanceId}/actions/{actionId}/execute",
            request,
            ct);
    }

    /// <summary>
    /// Gets the current state of a blueprint instance
    /// </summary>
    public async Task<BlueprintInstanceResponse?> GetInstanceAsync(
        string instanceId,
        CancellationToken ct = default)
    {
        return await GetAsync<BlueprintInstanceResponse>($"{_baseUrl}/instances/{instanceId}", ct);
    }

    /// <summary>
    /// Lists all instances for a blueprint
    /// </summary>
    public async Task<List<BlueprintInstanceResponse>?> ListInstancesAsync(
        string? blueprintId = null,
        CancellationToken ct = default)
    {
        var url = blueprintId != null
            ? $"{_baseUrl}/instances?blueprintId={blueprintId}"
            : $"{_baseUrl}/instances";

        return await GetAsync<List<BlueprintInstanceResponse>>(url, ct);
    }

    /// <summary>
    /// Checks Blueprint Service health
    /// </summary>
    public async Task<bool> CheckHealthAsync(CancellationToken ct = default)
    {
        return await base.CheckHealthAsync(_baseUrl, ct);
    }
}

/// <summary>
/// Response from blueprint instance creation/retrieval
/// </summary>
public class BlueprintInstanceResponse
{
    public string Id { get; set; } = string.Empty;  // Blueprint Service returns "id", not "instanceId"
    public string BlueprintId { get; set; } = string.Empty;
    public int BlueprintVersion { get; set; }
    public string RegisterId { get; set; } = string.Empty;
    public int State { get; set; }  // 0 = Active, 1 = Completed, 2 = Failed
    public List<int> CurrentActionIds { get; set; } = new();
    public Dictionary<string, string> ParticipantWallets { get; set; } = new();
    public List<object> ActiveBranches { get; set; } = new();
    public string? FirstTransactionId { get; set; }
    public string? LastTransactionId { get; set; }
    public int CompletedActionCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public Dictionary<string, string> Metadata { get; set; } = new();
}

/// <summary>
/// Response from action execution (orchestration endpoint)
/// </summary>
public class ActionExecutionResponse
{
    // Core fields from Blueprint Service
    public string? TransactionId { get; set; }
    public string? TransactionHash => TransactionId;
    public string? InstanceId { get; set; }
    public List<NextActionResponse> NextActions { get; set; } = [];
    public bool IsComplete { get; set; }
    public Dictionary<string, object>? Calculations { get; set; }
    public List<string>? Warnings { get; set; }

    // Computed properties for Demo compatibility
    public bool Success => !string.IsNullOrEmpty(TransactionId);
    public string? RegisterId { get; set; }
    public int? NextActionIndex => NextActions.FirstOrDefault()?.ActionId;
    public string? NextParticipant => NextActions.FirstOrDefault()?.ParticipantId;
    public Dictionary<string, object> ActionResult { get; set; } = new();
    public Dictionary<string, object> WorkflowState { get; set; } = new();
    public bool WorkflowComplete => IsComplete;
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Information about the next action from Blueprint Service
/// </summary>
public class NextActionResponse
{
    public int ActionId { get; set; }
    public string ActionTitle { get; set; } = string.Empty;
    public string ParticipantId { get; set; } = string.Empty;
    public string? BranchId { get; set; }
}

/// <summary>
/// Record of an action execution in instance history
/// </summary>
public class ActionExecutionRecord
{
    public int ActionIndex { get; set; }
    public string ActionTitle { get; set; } = string.Empty;
    public string Participant { get; set; } = string.Empty;
    public string? TransactionHash { get; set; }
    public DateTime ExecutedAt { get; set; }
    public Dictionary<string, object> ActionData { get; set; } = new();
}

/// <summary>
/// Request to submit an action for execution (matches Blueprint.Service model)
/// </summary>
public class ActionSubmissionRequest
{
    public required string BlueprintId { get; init; }
    public required string ActionId { get; init; }
    public string? InstanceId { get; init; }
    public string? PreviousTransactionHash { get; init; }
    public required string SenderWallet { get; init; }
    public required string RegisterAddress { get; init; }
    public required Dictionary<string, object> PayloadData { get; init; }
}
