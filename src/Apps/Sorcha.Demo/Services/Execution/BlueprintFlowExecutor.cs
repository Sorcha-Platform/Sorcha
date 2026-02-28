// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Logging;
using Sorcha.Blueprint.Models;
using Sorcha.Demo.Models;
using Sorcha.Demo.Services.Api;
using System.Diagnostics;

namespace Sorcha.Demo.Services.Execution;

/// <summary>
/// Executes blueprint workflows using the Blueprint Service orchestration API
/// </summary>
public class BlueprintFlowExecutor
{
    private readonly BlueprintApiClient _blueprintClient;
    private readonly ParticipantManager _participantManager;
    private readonly ILogger<BlueprintFlowExecutor> _logger;

    public BlueprintFlowExecutor(
        BlueprintApiClient blueprintClient,
        ParticipantManager participantManager,
        ILogger<BlueprintFlowExecutor> logger)
    {
        _blueprintClient = blueprintClient;
        _participantManager = participantManager;
        _logger = logger;
    }

    /// <summary>
    /// Executes a complete blueprint workflow
    /// </summary>
    public async Task<bool> ExecuteWorkflowAsync(
        DemoContext context,
        Func<int, Sorcha.Blueprint.Models.Action, Task<Dictionary<string, object>>>? promptForInputAsync = null,
        Func<ActionExecutionResult, Task>? onActionCompleteAsync = null,
        CancellationToken ct = default)
    {
        if (context.CurrentBlueprint == null)
        {
            throw new InvalidOperationException("No blueprint loaded in context");
        }

        var blueprint = context.CurrentBlueprint;
        _logger.LogInformation("Starting workflow execution: {Title}", blueprint.Title);

        try
        {
            // Step 1: Register blueprint with Blueprint Service (if needed)
            // This returns the actual blueprint ID to use (may be different from the template ID)
            var blueprintId = await EnsureBlueprintRegisteredAsync(blueprint, ct);

            // Step 2: Create blueprint instance
            var instanceId = await CreateInstanceAsync(blueprintId, context, ct);
            context.CurrentInstanceId = instanceId;

            // Step 2: Execute actions sequentially
            var currentActionIndex = 0;
            var workflowComplete = false;

            while (!workflowComplete && currentActionIndex < blueprint.Actions.Count)
            {
                if (ct.IsCancellationRequested)
                {
                    _logger.LogWarning("Workflow execution cancelled");
                    return false;
                }

                var action = blueprint.Actions[currentActionIndex];
                var participantId = action.Sender ?? throw new InvalidOperationException($"Action {currentActionIndex} has no sender");

                // Get participant context
                var participant = context.Participants.GetValueOrDefault(participantId);
                if (participant == null)
                {
                    _logger.LogError("Participant {ParticipantId} not found in context", participantId);
                    return false;
                }

                // Prompt for input data (interactive mode) or use defaults (automated)
                Dictionary<string, object> inputData;
                if (promptForInputAsync != null)
                {
                    inputData = await promptForInputAsync(currentActionIndex, action);
                }
                else
                {
                    inputData = GenerateDefaultInputData(action);
                }

                // Execute the action
                var result = await ExecuteActionAsync(
                    instanceId,
                    currentActionIndex,
                    action,
                    participant,
                    inputData,
                    context,
                    ct);

                // Store result
                context.ExecutionHistory.Add(result);
                participant.ActionsExecuted++;

                // Notify completion (for UI updates)
                if (onActionCompleteAsync != null)
                {
                    await onActionCompleteAsync(result);
                }

                // Check if workflow is complete
                workflowComplete = result.WorkflowComplete;

                // Move to next action (or complete)
                if (!workflowComplete && result.NextActionIndex.HasValue)
                {
                    currentActionIndex = result.NextActionIndex.Value;
                }
                else
                {
                    break;
                }
            }

            context.IsWorkflowComplete = workflowComplete;
            _logger.LogInformation("Workflow execution completed. Success: {Success}", workflowComplete);
            return workflowComplete;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Workflow execution failed");
            return false;
        }
    }

    /// <summary>
    /// Ensures blueprint is registered with Blueprint Service
    /// Returns the actual blueprint ID to use
    /// </summary>
    private async Task<string> EnsureBlueprintRegisteredAsync(
        Sorcha.Blueprint.Models.Blueprint blueprint,
        CancellationToken ct)
    {
        _logger.LogInformation("Checking if blueprint exists: {BlueprintId}", blueprint.Id);

        try
        {
            // Try to get the blueprint
            var existing = await _blueprintClient.GetBlueprintAsync(blueprint.Id ?? "", ct);

            if (existing != null)
            {
                _logger.LogInformation("Blueprint already registered: {BlueprintId}", existing.Id);
                return existing.Id ?? blueprint.Id ?? "";
            }
        }
        catch (Exception ex)
        {
            // Blueprint doesn't exist or retrieval failed, continue to create it
            Console.Error.WriteLine($"Blueprint lookup failed (will create new): {ex.Message}");
        }

        // Blueprint doesn't exist, create it
        _logger.LogInformation("Registering blueprint: {BlueprintId}", blueprint.Id);
        var created = await _blueprintClient.CreateBlueprintAsync(blueprint, ct);

        if (created == null || string.IsNullOrEmpty(created.Id))
        {
            throw new InvalidOperationException($"Failed to register blueprint: {blueprint.Id}");
        }

        _logger.LogInformation("Blueprint registered successfully: {BlueprintId}", created.Id);
        return created.Id;
    }

    /// <summary>
    /// Creates a blueprint instance via API
    /// </summary>
    private async Task<string> CreateInstanceAsync(
        string blueprintId,
        DemoContext context,
        CancellationToken ct)
    {
        _logger.LogInformation("Creating blueprint instance for: {BlueprintId}", blueprintId);

        var response = await _blueprintClient.CreateInstanceAsync(
            blueprintId: blueprintId,
            registerId: context.RegisterId,
            metadata: new Dictionary<string, object>
            {
                ["demoMode"] = true,
                ["startedAt"] = DateTime.UtcNow
            },
            tenantId: null,
            ct: ct);

        if (response == null || string.IsNullOrEmpty(response.Id))
        {
            throw new InvalidOperationException("Failed to create blueprint instance");
        }

        _logger.LogInformation("Created instance: {InstanceId}", response.Id);
        return response.Id;
    }

    /// <summary>
    /// Executes a single action using Blueprint Service orchestration endpoint
    /// </summary>
    private async Task<ActionExecutionResult> ExecuteActionAsync(
        string instanceId,
        int actionIndex,
        Sorcha.Blueprint.Models.Action action,
        ParticipantContext participant,
        Dictionary<string, object> inputData,
        DemoContext context,
        CancellationToken ct)
    {
        _logger.LogInformation("Executing action {Index}: {Title} as {Participant}",
            actionIndex, action.Title, participant.ParticipantId);

        var stopwatch = Stopwatch.StartNew();
        var result = new ActionExecutionResult
        {
            ActionIndex = actionIndex,
            ActionTitle = action.Title ?? $"Action {actionIndex}",
            ParticipantId = participant.ParticipantId,
            WalletAddress = participant.WalletAddress,
            InputData = inputData,
            ExecutedAt = DateTime.UtcNow
        };

        try
        {
            // Call Blueprint Service orchestration endpoint
            // This handles: validation, execution, transaction building, signing, and Register submission
            var response = await _blueprintClient.ExecuteActionAsync(
                instanceId: instanceId,
                actionId: actionIndex,
                blueprintId: context.CurrentBlueprint?.Id ?? "",
                actionData: inputData,
                senderWallet: participant.WalletAddress,
                registerAddress: context.RegisterId,
                previousTransactionHash: context.ExecutionHistory.LastOrDefault()?.TransactionHash,
                ct: ct);

            if (response == null)
            {
                throw new InvalidOperationException("Orchestration endpoint returned null response");
            }

            // Populate result from response
            result.Success = response.Success;
            result.TransactionHash = response.TransactionHash;
            result.RegisterId = response.RegisterId;
            result.OutputData = response.ActionResult;
            result.NextActionIndex = response.NextActionIndex;
            result.NextParticipant = response.NextParticipant;
            result.WorkflowComplete = response.WorkflowComplete;
            result.ErrorMessage = response.ErrorMessage;

            // Update context workflow state
            foreach (var (key, value) in response.WorkflowState)
            {
                context.WorkflowState[key] = value;
            }

            stopwatch.Stop();
            result.ExecutionTimeMs = stopwatch.ElapsedMilliseconds;

            _logger.LogInformation("Action {Index} executed successfully. TxHash: {TxHash}, Next: {Next}",
                actionIndex,
                response.TransactionHash != null && response.TransactionHash.Length > 16
                    ? response.TransactionHash[..16] + "..."
                    : response.TransactionHash ?? "N/A",
                response.NextParticipant ?? "Complete");

            return result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            result.Success = false;
            result.ErrorMessage = ex.Message;
            result.ExecutionTimeMs = stopwatch.ElapsedMilliseconds;

            _logger.LogError(ex, "Action {Index} execution failed", actionIndex);
            return result;
        }
    }

    /// <summary>
    /// Generates default input data for automated mode (based on schema)
    /// </summary>
    private Dictionary<string, object> GenerateDefaultInputData(Sorcha.Blueprint.Models.Action action)
    {
        var inputData = new Dictionary<string, object>();

        // Simple default value generation based on schema
        if (action.DataSchemas != null && action.DataSchemas.Any())
        {
            var schema = action.DataSchemas.First();
            var schemaJson = System.Text.Json.JsonSerializer.Serialize(schema);
            var schemaObj = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(schemaJson);

            if (schemaObj != null && schemaObj.TryGetValue("properties", out var propsObj))
            {
                var propsJson = System.Text.Json.JsonSerializer.Serialize(propsObj);
                var properties = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(propsJson);

                if (properties != null)
                {
                    foreach (var prop in properties.Keys)
                    {
                        var propJson = System.Text.Json.JsonSerializer.Serialize(properties[prop]);
                        var propDef = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(propJson);

                        if (propDef != null)
                        {
                            inputData[prop] = GenerateDefaultValue(propDef);
                        }
                    }
                }
            }
        }

        return inputData;
    }

    /// <summary>
    /// Generates a default value based on JSON Schema property
    /// </summary>
    private object GenerateDefaultValue(Dictionary<string, object> propertyDef)
    {
        var propType = propertyDef.ContainsKey("type") ? propertyDef["type"].ToString() : "string";
        var format = propertyDef.ContainsKey("format") ? propertyDef["format"].ToString() : null;

        // Handle enum values
        if (propertyDef.ContainsKey("enum"))
        {
            var enumJson = System.Text.Json.JsonSerializer.Serialize(propertyDef["enum"]);
            var enumValues = System.Text.Json.JsonSerializer.Deserialize<List<string>>(enumJson);
            if (enumValues != null && enumValues.Any())
            {
                return enumValues.First();
            }
        }

        // Get minimum value if present
        double? minimum = null;
        if (propertyDef.ContainsKey("minimum"))
        {
            var minJson = System.Text.Json.JsonSerializer.Serialize(propertyDef["minimum"]);
            minimum = System.Text.Json.JsonSerializer.Deserialize<double>(minJson);
        }

        // Get title if present
        var title = propertyDef.ContainsKey("title") ? propertyDef["title"].ToString() : null;

        return propType?.ToLower() switch
        {
            "string" when format == "date" => DateTime.UtcNow.ToString("yyyy-MM-dd"),
            "string" => title ?? "Default Value",
            "number" => minimum ?? 100.0,
            "integer" => (int)(minimum ?? 1),
            "boolean" => true,
            _ => "unknown"
        };
    }
}
