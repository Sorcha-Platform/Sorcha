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
/// Participant tool for getting action details. Reads via the typed
/// <see cref="IBlueprintServiceClient"/> (spec 139 US4) so the caller's bearer is forwarded
/// and the route is contract-pinned, not hand-rolled.
/// </summary>
[McpServerToolType]
public sealed class ActionDetailsTool
{
    private readonly IMcpAuthorizationService _authService;
    private readonly IServiceAvailabilityTracker _availabilityTracker;
    private readonly IBlueprintServiceClient _blueprintClient;
    private readonly ILogger<ActionDetailsTool> _logger;

    public ActionDetailsTool(
        IMcpAuthorizationService authService,
        IServiceAvailabilityTracker availabilityTracker,
        IBlueprintServiceClient blueprintClient,
        ILogger<ActionDetailsTool> logger)
    {
        _authService = authService;
        _availabilityTracker = availabilityTracker;
        _blueprintClient = blueprintClient;
        _logger = logger;
    }

    /// <summary>
    /// Gets details of a specific action within a workflow instance.
    /// </summary>
    /// <param name="instanceId">The workflow instance ID the action belongs to.</param>
    /// <param name="actionId">The action's sequence number within the blueprint.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Action details including its renderable schema.</returns>
    [McpServerTool(Name = "sorcha_action_details")]
    [Description("Fetch the renderable configuration of a single action on a workflow instance — its title, JSON input schema(s), UI form layout, calculated-field definitions, and its own credential gate — for an instance the caller's wallet participates in. Returns enough detail for an agent to construct a schema-valid submission payload. Deliberately narrow: it does not return routing rules, other participants, or any other action's content. Call this when the agent has an instanceId and actionId (from sorcha_inbox_list or sorcha_workflow_status) and needs the schema before drafting input; prefer sorcha_inbox_list rather than this tool when only enumerating pending work, and use sorcha_disclosed_data instead when you want disclosures across an entire workflow rather than a single action's schema.")]
    public async Task<ActionDetailsResult> GetActionDetailsAsync(
        [Description("The workflow instance ID the action belongs to")] string instanceId,
        [Description("The action's sequence number within the blueprint")] string actionId,
        CancellationToken cancellationToken = default)
    {
        // Authorization check
        if (!_authService.CanInvokeTool("sorcha_action_details"))
        {
            return new ActionDetailsResult
            {
                Status = "Unauthorized",
                Message = "Access denied. This tool requires the sorcha:participant role.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        // Validate input
        if (string.IsNullOrWhiteSpace(instanceId))
        {
            return new ActionDetailsResult
            {
                Status = "Error",
                Message = "Instance ID is required.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        if (string.IsNullOrWhiteSpace(actionId))
        {
            return new ActionDetailsResult
            {
                Status = "Error",
                Message = "Action ID is required.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        // Check service availability
        if (!_availabilityTracker.IsServiceAvailable("Blueprint"))
        {
            return new ActionDetailsResult
            {
                Status = "Unavailable",
                Message = "Blueprint service is currently unavailable. Please try again later.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        _logger.LogInformation("Getting action details for instance {InstanceId} action {ActionId}", instanceId, actionId);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            // Typed client forwards the caller's bearer and pins the route
            // (GET api/instances/{instanceId}/actions/{actionId}).
            var responseContent = await _blueprintClient.GetActionDetailsAsync(instanceId, actionId, cancellationToken);

            stopwatch.Stop();

            if (string.IsNullOrWhiteSpace(responseContent))
            {
                _availabilityTracker.RecordSuccess("Blueprint");

                return new ActionDetailsResult
                {
                    Status = "Error",
                    Message = "Action not found.",
                    CheckedAt = DateTimeOffset.UtcNow,
                    ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
                };
            }

            // Record success
            _availabilityTracker.RecordSuccess("Blueprint");

            var result = JsonSerializer.Deserialize<ActionDetailsResponse>(responseContent, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (result == null)
            {
                return new ActionDetailsResult
                {
                    Status = "Error",
                    Message = "Failed to parse action details response.",
                    CheckedAt = DateTimeOffset.UtcNow,
                    ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
                };
            }

            _logger.LogInformation(
                "Retrieved action details in {ElapsedMs}ms",
                stopwatch.ElapsedMilliseconds);

            return new ActionDetailsResult
            {
                Status = "Success",
                Message = $"Retrieved details for action '{result.Title ?? actionId}'.",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds,
                Action = new ActionDetail
                {
                    InstanceId = instanceId,
                    ActionId = result.ActionId,
                    Title = result.Title ?? "",
                    InputSchemas = result.DataSchemas?.Select(d => d.GetRawText()).ToList() ?? [],
                    FormLayout = result.Form?.GetRawText(),
                    Calculations = result.Calculations?.GetRawText(),
                    HasCredentialRequirements = result.CredentialRequirements is { Count: > 0 },
                    CredentialIssuanceConfig = result.CredentialIssuanceConfig?.GetRawText()
                }
            };
        }
        catch (TaskCanceledException)
        {
            stopwatch.Stop();
            _availabilityTracker.RecordFailure("Blueprint");

            return new ActionDetailsResult
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

            return new ActionDetailsResult
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

            _logger.LogError(ex, "Unexpected error getting action details");

            return new ActionDetailsResult
            {
                Status = "Error",
                Message = "An unexpected error occurred while getting action details.",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
            };
        }
    }

    // Internal response model — mirrors Sorcha.Blueprint.Service.Models.Responses.InstanceActionSchemaResponse
    // (GET /api/instances/{instanceId}/actions/{actionId}). Typed loosely (JsonElement) rather than
    // referencing the service's own model types, which McpServer does not depend on.
    private sealed class ActionDetailsResponse
    {
        public int ActionId { get; set; }
        public string? Title { get; set; }
        public JsonElement? Form { get; set; }
        public List<JsonElement>? DataSchemas { get; set; }
        public JsonElement? Calculations { get; set; }
        public List<JsonElement>? CredentialRequirements { get; set; }
        public JsonElement? CredentialIssuanceConfig { get; set; }
    }

}

/// <summary>
/// Result of getting action details.
/// </summary>
public sealed record ActionDetailsResult
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
    /// The action details.
    /// </summary>
    public ActionDetail? Action { get; init; }
}

/// <summary>
/// Detailed information about an action, mirroring the deliberately narrow shape of
/// <c>GET /api/instances/{instanceId}/actions/{actionId}</c> (routing rules, other participants,
/// and other actions' content are excluded by that endpoint, not just by this record).
/// </summary>
public sealed record ActionDetail
{
    /// <summary>
    /// The workflow instance ID this action belongs to.
    /// </summary>
    public required string InstanceId { get; init; }

    /// <summary>
    /// The action ID (sequence number) within the blueprint.
    /// </summary>
    public int ActionId { get; init; }

    /// <summary>
    /// The action title.
    /// </summary>
    public required string Title { get; init; }

    /// <summary>
    /// The JSON Schema(s) describing the data this action collects, each as a raw JSON string.
    /// </summary>
    public IReadOnlyList<string> InputSchemas { get; init; } = [];

    /// <summary>
    /// The UI form layout (JSON Forms-style control tree) as a raw JSON string, or null if the
    /// action has none (falls back to schema auto-generation).
    /// </summary>
    public string? FormLayout { get; init; }

    /// <summary>
    /// User-defined calculations (JSON Logic) performed on submitted data, as a raw JSON string.
    /// </summary>
    public string? Calculations { get; init; }

    /// <summary>
    /// Whether this action has credential requirements that must be satisfied before it can be
    /// executed. Requirement detail is intentionally not surfaced here — use sorcha_action_details
    /// output only to detect the gate's presence, not to evaluate it.
    /// </summary>
    public bool HasCredentialRequirements { get; init; }

    /// <summary>
    /// Configuration for a credential minted when this action executes, as a raw JSON string, or
    /// null if this action mints none.
    /// </summary>
    public string? CredentialIssuanceConfig { get; init; }
}
