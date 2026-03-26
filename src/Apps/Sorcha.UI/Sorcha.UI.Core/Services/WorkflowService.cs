// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Sorcha.UI.Core.Models.Common;
using Sorcha.UI.Core.Models.Workflows;

namespace Sorcha.UI.Core.Services;

/// <summary>
/// Implementation of <see cref="IWorkflowService"/> calling the Blueprint Service orchestration API.
/// </summary>
public class WorkflowService : IWorkflowService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<WorkflowService> _logger;
    // Scoped to the WASM session lifetime (singleton in Blazor WASM). Acceptable for typical
    // use because blueprints rarely change during a single user session.
    private readonly Dictionary<string, Sorcha.Blueprint.Models.Blueprint> _blueprintCache = new();

    public WorkflowService(HttpClient httpClient, ILogger<WorkflowService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<PaginatedList<WorkflowInstanceViewModel>> GetMyWorkflowsAsync(int page = 1, int pageSize = 20, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync(
                $"/api/instances?page={page}&pageSize={pageSize}", cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to fetch workflows: {StatusCode}", response.StatusCode);
                return new PaginatedList<WorkflowInstanceViewModel>();
            }

            var result = await response.Content.ReadFromJsonAsync<PaginatedList<WorkflowInstanceViewModel>>(cancellationToken: cancellationToken);
            return result ?? new PaginatedList<WorkflowInstanceViewModel>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching workflows");
            return new PaginatedList<WorkflowInstanceViewModel>();
        }
    }

    public async Task<WorkflowInstanceViewModel?> GetWorkflowAsync(string instanceId, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _httpClient.GetFromJsonAsync<WorkflowInstanceViewModel>(
                $"/api/instances/{instanceId}", cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching workflow {InstanceId}", instanceId);
            return null;
        }
    }

    public async Task<List<PendingActionViewModel>> GetPendingActionsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // Use the dedicated wallet-scoped pending actions endpoint.
            // The server reads the wallet_address JWT claim to find actions
            // assigned to this user across all active instances.
            var response = await _httpClient.GetAsync("/api/actions/pending", cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Pending actions endpoint returned {StatusCode}", response.StatusCode);
                return [];
            }

            var result = await response.Content.ReadFromJsonAsync<PendingActionsResponse>(cancellationToken: cancellationToken);
            if (result?.Items is null || result.Items.Count == 0) return [];

            return result.Items.Select(s => new PendingActionViewModel
            {
                ActionId = s.ActionId.ToString(),
                InstanceId = s.InstanceId,
                BlueprintId = s.BlueprintId,
                RegisterId = s.RegisterId,
                BlueprintName = s.BlueprintTitle ?? s.BlueprintId,
                ActionName = s.ActionTitle ?? $"Action {s.ActionId}",
                InstanceReference = s.InstanceReference ?? string.Empty,
                Description = s.Summary ?? string.Empty,
                Priority = s.Urgency ?? "normal",
                AssignedAt = s.ReceivedAt,
                DueAt = s.Deadline
            }).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching pending actions");
            return [];
        }
    }

    /// <summary>
    /// Response shape from GET /api/actions/pending
    /// </summary>
    private record PendingActionsResponse
    {
        public List<PendingActionSummaryDto> Items { get; init; } = [];
        public int TotalCount { get; init; }
        public int Page { get; init; }
        public int PageSize { get; init; }
    }

    private record PendingActionSummaryDto
    {
        public string InstanceId { get; init; } = string.Empty;
        public int ActionId { get; init; }
        public string? ActionTitle { get; init; }
        public string BlueprintId { get; init; } = string.Empty;
        public string? BlueprintTitle { get; init; }
        public string? InstanceReference { get; init; }
        public string? Summary { get; init; }
        public string? Urgency { get; init; }
        public DateTimeOffset? Deadline { get; init; }
        public string RegisterId { get; init; } = string.Empty;
        public DateTimeOffset ReceivedAt { get; init; }
    }

    public async Task<WorkflowInstanceViewModel?> CreateInstanceAsync(string blueprintId, string registerId, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync(
                "/api/instances",
                new { blueprintId, registerId },
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to create instance for blueprint {BlueprintId}: {StatusCode}", blueprintId, response.StatusCode);
                return null;
            }

            return await response.Content.ReadFromJsonAsync<WorkflowInstanceViewModel>(cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating instance for blueprint {BlueprintId}", blueprintId);
            return null;
        }
    }

    /// <inheritdoc/>
    public async Task<Sorcha.Blueprint.Models.Blueprint?> GetBlueprintAsync(string blueprintId, CancellationToken cancellationToken = default)
    {
        if (_blueprintCache.TryGetValue(blueprintId, out var cached))
            return cached;

        try
        {
            var blueprint = await _httpClient.GetFromJsonAsync<Sorcha.Blueprint.Models.Blueprint>(
                $"/api/blueprints/{blueprintId}", cancellationToken);

            if (blueprint != null)
                _blueprintCache[blueprintId] = blueprint;

            return blueprint;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching blueprint {BlueprintId}", blueprintId);
            return null;
        }
    }

    public async Task<ActionSubmissionResultViewModel?> SubmitActionExecuteAsync(ActionExecuteRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var body = new
            {
                blueprintId = request.BlueprintId,
                actionId = request.ActionId,
                instanceId = request.InstanceId,
                senderWallet = request.SenderWallet,
                registerAddress = request.RegisterAddress,
                payloadData = request.PayloadData
            };

            using var httpRequest = new HttpRequestMessage(HttpMethod.Post,
                $"/api/instances/{request.InstanceId}/actions/{request.ActionId}/execute");
            httpRequest.Content = JsonContent.Create(body);
            httpRequest.Headers.Add("X-Delegation-Token", "delegate");

            var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to execute action {ActionId} on instance {InstanceId}: {StatusCode}",
                    request.ActionId, request.InstanceId, response.StatusCode);
                return null;
            }

            return await response.Content.ReadFromJsonAsync<ActionSubmissionResultViewModel>(cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing action {ActionId} on instance {InstanceId}", request.ActionId, request.InstanceId);
            return null;
        }
    }

    public async Task<bool> SubmitActionAsync(ActionSubmissionViewModel submission, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync(
                $"/api/instances/{submission.InstanceId}/actions/{submission.ActionId}/execute",
                new { data = submission.Data },
                cancellationToken);

            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error submitting action {ActionId}", submission.ActionId);
            return false;
        }
    }

    public async Task<bool> RejectActionAsync(string instanceId, string actionId, string reason, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync(
                $"/api/instances/{instanceId}/actions/{actionId}/reject",
                new { reason },
                cancellationToken);

            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error rejecting action {ActionId}", actionId);
            return false;
        }
    }
}
