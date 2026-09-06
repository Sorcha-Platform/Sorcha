// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Blueprint.Engine.Interfaces;
using Sorcha.Blueprint.Engine.Models;
using Sorcha.Blueprint.Models;

namespace Sorcha.Blueprint.Engine.Implementation;

/// <summary>
/// Main execution engine facade that provides both full workflow execution
/// and access to individual processing components.
/// </summary>
/// <remarks>
/// This is the primary entry point for blueprint action execution.
/// It delegates to ActionProcessor for full workflow execution and provides
/// individual component methods for standalone use.
///
/// Thread-safe and can be used concurrently.
/// </remarks>
public class ExecutionEngine : IExecutionEngine
{
    private readonly IActionProcessor _actionProcessor;
    private readonly ISchemaValidator _schemaValidator;
    private readonly IJsonLogicEvaluator _jsonLogicEvaluator;
    private readonly IDisclosureProcessor _disclosureProcessor;
    private readonly IRoutingEngine _routingEngine;

    public ExecutionEngine(
        IActionProcessor actionProcessor,
        ISchemaValidator schemaValidator,
        IJsonLogicEvaluator jsonLogicEvaluator,
        IDisclosureProcessor disclosureProcessor,
        IRoutingEngine routingEngine)
    {
        _actionProcessor = actionProcessor ?? throw new ArgumentNullException(nameof(actionProcessor));
        _schemaValidator = schemaValidator ?? throw new ArgumentNullException(nameof(schemaValidator));
        _jsonLogicEvaluator = jsonLogicEvaluator ?? throw new ArgumentNullException(nameof(jsonLogicEvaluator));
        _disclosureProcessor = disclosureProcessor ?? throw new ArgumentNullException(nameof(disclosureProcessor));
        _routingEngine = routingEngine ?? throw new ArgumentNullException(nameof(routingEngine));
    }

    /// <summary>
    /// Execute a complete action workflow including validation, calculations,
    /// routing, and disclosure processing.
    /// </summary>
    public async Task<ActionExecutionResult> ExecuteActionAsync(
        Engine.Models.ExecutionContext context,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        // Delegate to ActionProcessor for full workflow
        return await _actionProcessor.ProcessAsync(context, ct);
    }

    /// <summary>
    /// Validate action data against the action's schema.
    /// </summary>
    public async Task<ValidationResult> ValidateAsync(
        Dictionary<string, object> data,
        Sorcha.Blueprint.Models.Action action,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(action);

        // Issue #1573: the schemas live on Action.DataSchemas — the property every published
        // blueprint populates and the one the Validator itself enforces. Reading Form.Schema here
        // meant this returned Valid for every payload ever submitted. See ActionSchemaValidation.
        return await ActionSchemaValidation.ValidateAsync(_schemaValidator, data, action, ct);
    }

    /// <summary>
    /// Apply calculations to data using the action's calculation definitions.
    /// </summary>
    public async Task<Dictionary<string, object>> ApplyCalculationsAsync(
        Dictionary<string, object> data,
        Sorcha.Blueprint.Models.Action action,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(action);

        if (action.Calculations?.Any() != true)
        {
            // No calculations defined - return original data
            return new Dictionary<string, object>(data);
        }

        // Convert calculations dictionary to Calculation objects
        var calculations = action.Calculations
            .Select(kvp => Calculation.Create(kvp.Key, kvp.Value))
            .ToList();

        return await _jsonLogicEvaluator.ApplyCalculationsAsync(data, calculations, ct);
    }

    /// <summary>
    /// Determine the next participant and action in the workflow.
    /// </summary>
    public async Task<RoutingResult> DetermineRoutingAsync(
        Sorcha.Blueprint.Models.Blueprint blueprint,
        Sorcha.Blueprint.Models.Action action,
        Dictionary<string, object> data,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(blueprint);
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(data);

        return await _routingEngine.DetermineNextAsync(blueprint, action, data, ct);
    }

    /// <inheritdoc />
    public async Task<RoutingResult> DetermineRoutingWithMappingAsync(
        Sorcha.Blueprint.Models.Blueprint blueprint,
        Sorcha.Blueprint.Models.Action action,
        Dictionary<string, object> data,
        System.Text.Json.Nodes.JsonObject? outputSource,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(blueprint);
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(data);

        return await _routingEngine.DetermineNextWithMappingAsync(blueprint, action, data, outputSource, ct);
    }

    /// <summary>
    /// Create disclosure results for all participants based on the action's
    /// disclosure definitions.
    /// </summary>
    public List<DisclosureResult> ApplyDisclosures(
        Dictionary<string, object> data,
        Sorcha.Blueprint.Models.Action action)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(action);

        if (action.Disclosures?.Any() != true)
        {
            // No disclosures defined - return empty list
            return new List<DisclosureResult>();
        }

        return _disclosureProcessor.CreateDisclosures(data, action.Disclosures);
    }
}
