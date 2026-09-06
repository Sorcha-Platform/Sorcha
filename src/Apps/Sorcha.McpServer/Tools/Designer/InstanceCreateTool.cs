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

namespace Sorcha.McpServer.Tools.Designer;

/// <summary>
/// Designer tool for starting a workflow instance from a published blueprint. Writes via the
/// typed <see cref="IBlueprintServiceClient"/> (spec 139 US4) so the caller's bearer is forwarded
/// and the route is contract-pinned, not hand-rolled.
/// </summary>
/// <remarks>
/// Closes the loop <c>sorcha_action_submit</c> has always had open: it has always required an
/// <c>instanceId</c> that nothing on the MCP surface produced. This is that producer.
/// </remarks>
[McpServerToolType]
public sealed class InstanceCreateTool
{
    private readonly IMcpAuthorizationService _authService;
    private readonly IServiceAvailabilityTracker _availabilityTracker;
    private readonly IBlueprintServiceClient _blueprintClient;
    private readonly ILogger<InstanceCreateTool> _logger;

    public InstanceCreateTool(
        IMcpAuthorizationService authService,
        IServiceAvailabilityTracker availabilityTracker,
        IBlueprintServiceClient blueprintClient,
        ILogger<InstanceCreateTool> logger)
    {
        _authService = authService;
        _availabilityTracker = availabilityTracker;
        _blueprintClient = blueprintClient;
        _logger = logger;
    }

    /// <summary>Starts a workflow instance from a published blueprint.</summary>
    /// <param name="blueprintId">The published blueprint to instantiate.</param>
    /// <param name="registerId">The register the instance's transactions are written to.</param>
    /// <param name="tenantId">Optional tenant id for isolation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The created instance's id and human-readable reference.</returns>
    [McpServerTool(Name = "sorcha_instance_create", Destructive = false, ReadOnly = false, Idempotent = false)]
    [Description("Starts a running workflow instance from a blueprint that has already been published to a register, returning the instanceId needed for every subsequent step. Call this when a blueprint has been published to the target register and you need to begin a fresh execution of it: pass the returned instanceId to sorcha_action_submit to perform the workflow's first action, then poll sorcha_workflow_status to observe the outcome, since submission in Sorcha is asynchronous and neither call reports workflow progress directly. Both blueprintId and registerId are required, and the blueprint must already be published to that register — publishing and instantiating are separate steps. The returned instanceReference is a human-readable label auto-generated from the first action's payload, so it is typically null immediately after creation and only appears once that first action has been submitted and folded.")]
    public async Task<InstanceCreateResult> CreateInstanceAsync(
        [Description("The published blueprint's ID")] string blueprintId,
        [Description("The register the instance writes its transactions to")] string registerId,
        [Description("Optional tenant ID for isolation; omit for the default tenant")] string? tenantId = null,
        CancellationToken cancellationToken = default)
    {
        // Authorization check
        if (!_authService.CanInvokeTool("sorcha_instance_create"))
        {
            return new InstanceCreateResult
            {
                Status = "Unauthorized",
                Message = "Access denied. This tool requires the sorcha:designer role.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        // Validate input
        if (string.IsNullOrWhiteSpace(blueprintId))
        {
            return new InstanceCreateResult
            {
                Status = "Error",
                Message = "blueprintId is required.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        if (string.IsNullOrWhiteSpace(registerId))
        {
            return new InstanceCreateResult
            {
                Status = "Error",
                Message = "registerId is required.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        // Check service availability
        if (!_availabilityTracker.IsServiceAvailable("Blueprint"))
        {
            return new InstanceCreateResult
            {
                Status = "Unavailable",
                Message = "Blueprint service is currently unavailable. Please try again later.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        _logger.LogInformation(
            "Creating instance for blueprint {BlueprintId} on register {RegisterId}", blueprintId, registerId);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            // Typed client forwards the caller's bearer and pins the route (POST api/instances/).
            var responseContent = await _blueprintClient.CreateInstanceAsync(
                blueprintId, registerId, tenantId, cancellationToken);

            stopwatch.Stop();

            if (string.IsNullOrWhiteSpace(responseContent))
            {
                _availabilityTracker.RecordFailure("Blueprint");

                // The endpoint's one DOCUMENTED non-success shape for this route is a 409
                // "blueprint_not_available" — the blueprint has not finished replicating to this
                // node yet (Feature 137 / C1 replicas resolve blueprints asynchronously). That is
                // transient, not a permanent refusal, so say so rather than handing back a bare
                // "Error" an agent has no reason to retry.
                return new InstanceCreateResult
                {
                    Status = "Error",
                    Message = "Failed to create the workflow instance. If the blueprint was only just " +
                        "published, or this register recently synced to this node, the blueprint may not " +
                        "have replicated here yet — that is transient, so retry in a few seconds. If it " +
                        "persists, verify the blueprint has actually been published to this register.",
                    CheckedAt = DateTimeOffset.UtcNow,
                    ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
                };
            }

            InstanceResponseDto? dto;
            try
            {
                dto = JsonSerializer.Deserialize<InstanceResponseDto>(responseContent, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Error parsing create instance response");

                return new InstanceCreateResult
                {
                    Status = "Error",
                    Message = $"Failed to parse the create-instance response: {ex.Message}",
                    CheckedAt = DateTimeOffset.UtcNow,
                    ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
                };
            }

            if (dto == null || string.IsNullOrWhiteSpace(dto.Id))
            {
                _availabilityTracker.RecordFailure("Blueprint");

                return new InstanceCreateResult
                {
                    Status = "Error",
                    Message = "Failed to create instance. The service returned an unexpected response.",
                    CheckedAt = DateTimeOffset.UtcNow,
                    ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
                };
            }

            _availabilityTracker.RecordSuccess("Blueprint");

            var state = InstanceStateResolver.Resolve(dto.State);
            var instanceReference = dto.Metadata != null && dto.Metadata.TryGetValue("instanceReference", out var reference)
                ? reference
                : null;

            _logger.LogInformation(
                "Instance '{InstanceId}' created for blueprint '{BlueprintId}' in {ElapsedMs}ms",
                dto.Id, blueprintId, stopwatch.ElapsedMilliseconds);

            return new InstanceCreateResult
            {
                Status = "Success",
                Message = $"Instance '{dto.Id}' created for blueprint '{blueprintId}' on register '{registerId}'. " +
                    "Submission is asynchronous: call sorcha_action_submit with this instanceId to perform the " +
                    "first action, then sorcha_workflow_status to observe the outcome once it has folded.",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds,
                InstanceId = dto.Id,
                InstanceReference = instanceReference,
                State = state
            };
        }
        catch (TaskCanceledException)
        {
            stopwatch.Stop();
            _availabilityTracker.RecordFailure("Blueprint");

            _logger.LogWarning("Instance create request timed out");

            return new InstanceCreateResult
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

            _logger.LogWarning(ex, "Failed to create instance");

            return new InstanceCreateResult
            {
                Status = "Error",
                Message = $"Failed to connect to blueprint service: {ex.Message}",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
            };
        }
        catch (JsonException ex)
        {
            stopwatch.Stop();
            _availabilityTracker.RecordFailure("Blueprint", ex);

            _logger.LogWarning(ex, "Error parsing create instance response");

            return new InstanceCreateResult
            {
                Status = "Error",
                Message = $"Failed to parse the create-instance response: {ex.Message}",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _availabilityTracker.RecordFailure("Blueprint", ex);

            _logger.LogError(ex, "Unexpected error creating instance");

            return new InstanceCreateResult
            {
                Status = "Error",
                Message = "An unexpected error occurred while creating the instance.",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
            };
        }
    }

    // Internal response model — mirrors the fields actually present on
    // Sorcha.Blueprint.Service.Models.Instance, the type POST /api/instances/ serializes via
    // Results.Created. State has no JsonStringEnumConverter registered (see
    // InstanceStateResolver), and instanceReference — when present at all — lives inside Metadata,
    // not as a top-level field: it is written by ActionExecutionService only after the instance's
    // first action is submitted and folded, never at creation time.
    private sealed class InstanceResponseDto
    {
        public string? Id { get; set; }
        public string? BlueprintId { get; set; }
        public string? RegisterId { get; set; }
        public JsonElement? State { get; set; }
        public Dictionary<string, string>? Metadata { get; set; }
    }
}

/// <summary>
/// Result of starting a workflow instance.
/// </summary>
public sealed record InstanceCreateResult
{
    /// <summary>Operation status: Success, Error, Unavailable, Timeout, or Unauthorized.</summary>
    public required string Status { get; init; }

    /// <summary>Human-readable message about the operation result.</summary>
    public required string Message { get; init; }

    /// <summary>When the operation was performed.</summary>
    public required DateTimeOffset CheckedAt { get; init; }

    /// <summary>Response time in milliseconds.</summary>
    public int ResponseTimeMs { get; init; }

    /// <summary>The created instance's ID. Pass this to sorcha_action_submit.</summary>
    public string? InstanceId { get; init; }

    /// <summary>The instance's human-readable reference (e.g. "CP-RIV-14-A7K3"), when configured.</summary>
    public string? InstanceReference { get; init; }

    /// <summary>The instance's initial state.</summary>
    public string? State { get; init; }
}
