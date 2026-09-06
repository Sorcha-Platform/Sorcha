// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Sorcha.McpServer.Infrastructure;
using Sorcha.McpServer.Services;
using Sorcha.ServiceClients.Blueprint;

namespace Sorcha.McpServer.Tools.Participant;

/// <summary>
/// Participant tool for viewing data disclosed to the user. Reads via the typed
/// <see cref="IBlueprintServiceClient"/> (spec 139 US4) so the caller's bearer is forwarded
/// and the routes are contract-pinned, not hand-rolled.
/// </summary>
[McpServerToolType]
public sealed class DisclosedDataTool
{
    private readonly IMcpAuthorizationService _authService;
    private readonly IServiceAvailabilityTracker _availabilityTracker;
    private readonly IBlueprintServiceClient _blueprintClient;
    private readonly ILogger<DisclosedDataTool> _logger;

    public DisclosedDataTool(
        IMcpAuthorizationService authService,
        IServiceAvailabilityTracker availabilityTracker,
        IBlueprintServiceClient blueprintClient,
        ILogger<DisclosedDataTool> logger)
    {
        _authService = authService;
        _availabilityTracker = availabilityTracker;
        _blueprintClient = blueprintClient;
        _logger = logger;
    }

    /// <summary>
    /// Gets data disclosed to the current user for a workflow or action.
    /// </summary>
    /// <param name="workflowInstanceId">The workflow instance ID.</param>
    /// <param name="actionInstanceId">The action instance ID (optional - returns all workflow disclosures if not specified).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Data disclosed to the user.</returns>
    [McpServerTool(Name = "sorcha_disclosed_data")]
    [Description("Return the upstream payload fields a participant has been granted access to within a workflow instance, decrypted under that participant's per-recipient symmetric key wrap. Disclosure is cryptographically bounded by the blueprint's selective-disclosure rules — fields not intended for this participant are not retrievable, by construction. Call this when an agent needs the contextual data carried forward from earlier steps (for example, to reason about an invoice or attestation before responding); prefer sorcha_action_details when you only need disclosures attached to one specific action, and use sorcha_register_query rather than this tool when the data lives in a register rather than as inline workflow state.")]
    public async Task<DisclosedDataResult> GetDisclosedDataAsync(
        [Description("The workflow instance ID")] string workflowInstanceId,
        [Description("The action instance ID (optional - returns all workflow disclosures if not specified)")] string? actionInstanceId = null,
        CancellationToken cancellationToken = default)
    {
        // Authorization check
        if (!_authService.CanInvokeTool("sorcha_disclosed_data"))
        {
            return new DisclosedDataResult
            {
                Status = "Unauthorized",
                Message = "Access denied. This tool requires an authenticated consumer- or platform-tier caller.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        // Validate input
        if (string.IsNullOrWhiteSpace(workflowInstanceId))
        {
            return new DisclosedDataResult
            {
                Status = "Error",
                Message = "Workflow instance ID is required.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        // Check service availability
        if (!_availabilityTracker.IsServiceAvailable("Blueprint"))
        {
            return new DisclosedDataResult
            {
                Status = "Unavailable",
                Message = "Blueprint service is currently unavailable. Please try again later.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        _logger.LogInformation(
            "Getting disclosed data for workflow {WorkflowInstanceId}, action {ActionInstanceId}",
            workflowInstanceId, actionInstanceId ?? "all");

        var stopwatch = Stopwatch.StartNew();

        try
        {
            // Typed client forwards the caller's bearer and pins the route (GET api/workflows/{id}/disclosures).
            var responseContent = await _blueprintClient.GetDisclosedDataAsync(
                workflowInstanceId, actionInstanceId, cancellationToken);

            stopwatch.Stop();

            if (string.IsNullOrWhiteSpace(responseContent))
            {
                _availabilityTracker.RecordSuccess("Blueprint");

                return new DisclosedDataResult
                {
                    Status = "Error",
                    Message = "Failed to retrieve disclosed data.",
                    CheckedAt = DateTimeOffset.UtcNow,
                    ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
                };
            }

            // Record success
            _availabilityTracker.RecordSuccess("Blueprint");

            var result = JsonSerializer.Deserialize<DisclosedDataResponse>(responseContent, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (result == null)
            {
                return new DisclosedDataResult
                {
                    Status = "Error",
                    Message = "Failed to parse disclosed data response.",
                    CheckedAt = DateTimeOffset.UtcNow,
                    ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
                };
            }

            var disclosureCount = result.Disclosures?.Count ?? 0;

            _logger.LogInformation(
                "Retrieved {Count} disclosure(s) in {ElapsedMs}ms",
                disclosureCount, stopwatch.ElapsedMilliseconds);

            return new DisclosedDataResult
            {
                Status = "Success",
                Message = disclosureCount > 0
                    ? $"Retrieved {disclosureCount} disclosure(s)."
                    : "No data has been disclosed to you for this workflow.",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds,
                Disclosures = result.Disclosures?.Select(d => new DisclosureItem
                {
                    ActionId = d.ActionId,
                    ActionTitle = d.ActionTitle ?? "",
                    DisclosedAt = d.DisclosedAt,
                    Data = d.Data ?? new Dictionary<string, object>()
                }).ToList() ?? [],
                TotalFields = result.Disclosures?.Sum(d => d.Data?.Count ?? 0) ?? 0
            };
        }
        catch (TaskCanceledException)
        {
            stopwatch.Stop();
            _availabilityTracker.RecordFailure("Blueprint");

            return new DisclosedDataResult
            {
                Status = "Timeout",
                Message = "Request to blueprint service timed out.",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
            };
        }
        catch (HttpRequestException ex)
        {
            stopwatch.Stop();
            _availabilityTracker.RecordFailure("Blueprint", ex);

            return new DisclosedDataResult
            {
                Status = "Error",
                Message = $"Failed to connect to blueprint service: {ex.Message}",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _availabilityTracker.RecordFailure("Blueprint", ex);

            _logger.LogError(ex, "Unexpected error getting disclosed data");

            return new DisclosedDataResult
            {
                Status = "Error",
                Message = "An unexpected error occurred while getting disclosed data.",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
            };
        }
    }

    // Internal response models
    private sealed class DisclosedDataResponse
    {
        public List<DisclosureDto>? Disclosures { get; set; }
    }

    private sealed class DisclosureDto
    {
        public int ActionId { get; set; }
        public string? ActionTitle { get; set; }
        public DateTimeOffset? DisclosedAt { get; set; }
        public Dictionary<string, object>? Data { get; set; }
    }

}

/// <summary>
/// Result of getting disclosed data.
/// </summary>
public sealed record DisclosedDataResult
{
    /// <summary>
    /// Operation status: Success, Error, Unavailable, Timeout, or Unauthorized.
    /// </summary>
    public required string Status { get; init; }

    /// <summary>
    /// Human-readable message about the operation result.
    /// </summary>
    public required string Message { get; init; }

    /// <summary>
    /// When the operation was performed.
    /// </summary>
    public required DateTimeOffset CheckedAt { get; init; }

    /// <summary>
    /// Response time in milliseconds.
    /// </summary>
    public int ResponseTimeMs { get; init; }

    /// <summary>
    /// List of disclosures from different actions.
    /// </summary>
    public IReadOnlyList<DisclosureItem> Disclosures { get; init; } = [];

    /// <summary>
    /// Total number of fields disclosed across all disclosures.
    /// </summary>
    public int TotalFields { get; init; }
}

/// <summary>
/// Data disclosed from a specific action.
/// </summary>
public sealed record DisclosureItem
{
    /// <summary>
    /// The action ID that disclosed the data.
    /// </summary>
    public int ActionId { get; init; }

    /// <summary>
    /// The action title.
    /// </summary>
    public required string ActionTitle { get; init; }

    /// <summary>
    /// When the data was disclosed.
    /// </summary>
    public DateTimeOffset? DisclosedAt { get; init; }

    /// <summary>
    /// The disclosed data fields.
    /// </summary>
    public Dictionary<string, object> Data { get; init; } = new();
}
