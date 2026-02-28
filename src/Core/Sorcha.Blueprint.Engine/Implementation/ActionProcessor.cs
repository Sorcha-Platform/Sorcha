// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json.Nodes;
using Sorcha.Blueprint.Engine.Credentials;
using Sorcha.Blueprint.Engine.Interfaces;
using Sorcha.Blueprint.Engine.Models;

namespace Sorcha.Blueprint.Engine.Implementation;

/// <summary>
/// Action processor that orchestrates the complete action execution workflow.
/// </summary>
/// <remarks>
/// Coordinates credential verification, validation, calculation, routing,
/// and disclosure processing using specialized components.
///
/// Thread-safe and can be used concurrently.
/// </remarks>
public class ActionProcessor : IActionProcessor
{
    private readonly ISchemaValidator _schemaValidator;
    private readonly IJsonLogicEvaluator _jsonLogicEvaluator;
    private readonly IDisclosureProcessor _disclosureProcessor;
    private readonly IRoutingEngine _routingEngine;
    private readonly ICredentialVerifier? _credentialVerifier;
    private readonly ICredentialIssuer? _credentialIssuer;

    public ActionProcessor(
        ISchemaValidator schemaValidator,
        IJsonLogicEvaluator jsonLogicEvaluator,
        IDisclosureProcessor disclosureProcessor,
        IRoutingEngine routingEngine,
        ICredentialVerifier? credentialVerifier = null,
        ICredentialIssuer? credentialIssuer = null)
    {
        _schemaValidator = schemaValidator ?? throw new ArgumentNullException(nameof(schemaValidator));
        _jsonLogicEvaluator = jsonLogicEvaluator ?? throw new ArgumentNullException(nameof(jsonLogicEvaluator));
        _disclosureProcessor = disclosureProcessor ?? throw new ArgumentNullException(nameof(disclosureProcessor));
        _routingEngine = routingEngine ?? throw new ArgumentNullException(nameof(routingEngine));
        _credentialVerifier = credentialVerifier;
        _credentialIssuer = credentialIssuer;
    }

    /// <summary>
    /// Process an action through the complete execution workflow.
    /// </summary>
    public async Task<ActionExecutionResult> ProcessAsync(
        Engine.Models.ExecutionContext context,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var result = new ActionExecutionResult();

        try
        {
            // Step 0: Verify credential presentations against action requirements
            if (context.Action.CredentialRequirements?.Any() == true && _credentialVerifier != null)
            {
                result.CredentialValidation = await _credentialVerifier.VerifyAsync(
                    context.Action.CredentialRequirements,
                    context.CredentialPresentations,
                    ct);

                if (!result.CredentialValidation.IsValid)
                {
                    result.Success = false;
                    foreach (var error in result.CredentialValidation.Errors)
                    {
                        result.Errors.Add($"Credential verification failed: {error.Message}");
                    }
                    return result;
                }
            }

            // Step 1: Validate action data against schema
            if (context.Action.Form?.Schema != null)
            {
                result.Validation = await _schemaValidator.ValidateAsync(
                    context.ActionData,
                    context.Action.Form.Schema,
                    ct);

                if (!result.Validation.IsValid)
                {
                    // Validation failed - short circuit
                    result.Success = false;
                    result.Errors.Add("Action data failed schema validation");
                    return result;
                }
            }
            else
            {
                // No schema defined - validation passes
                result.Validation = ValidationResult.Valid();
            }

            // ValidationOnly mode: return after schema validation without executing
            // calculations, routing, or disclosure processing
            if (context.Mode == ExecutionMode.ValidationOnly)
            {
                result.Success = result.Validation.IsValid;
                return result;
            }

            // Step 2: Apply calculations using JSON Logic
            var processedData = new Dictionary<string, object>(context.ActionData);

            if (context.Action.Calculations?.Any() == true)
            {
                // Convert calculations dictionary to Calculation objects
                var calculations = context.Action.Calculations
                    .Select(kvp => Calculation.Create(kvp.Key, kvp.Value))
                    .ToList();

                processedData = await _jsonLogicEvaluator.ApplyCalculationsAsync(
                    processedData,
                    calculations,
                    ct);

                // Track calculated values separately
                foreach (var kvp in processedData)
                {
                    if (!context.ActionData.ContainsKey(kvp.Key))
                    {
                        result.CalculatedValues[kvp.Key] = kvp.Value;
                    }
                }
            }

            result.ProcessedData = processedData;

            // Step 3: Determine routing to next participant
            result.Routing = await _routingEngine.DetermineNextAsync(
                context.Blueprint,
                context.Action,
                processedData,
                ct);

            // Step 4: Create disclosure results for participants
            if (context.Action.Disclosures?.Any() == true)
            {
                result.Disclosures = _disclosureProcessor.CreateDisclosures(
                    processedData,
                    context.Action.Disclosures);
            }

            // Step 5: Issue credential if action has issuance configuration
            if (context.Action.CredentialIssuanceConfig != null
                && _credentialIssuer != null
                && context.IssuanceContext != null)
            {
                result.IssuedCredential = await _credentialIssuer.IssueAsync(
                    context.Action.CredentialIssuanceConfig,
                    processedData,
                    context.IssuanceContext.IssuerDid,
                    context.IssuanceContext.RecipientDid,
                    context.IssuanceContext.SigningKey,
                    context.IssuanceContext.Algorithm,
                    ct);
            }

            // If we got here, execution succeeded
            result.Success = true;

            // Add informational warnings
            if (result.Routing.IsWorkflowComplete)
            {
                result.Warnings.Add("Workflow complete - no next action");
            }

            if (!result.Disclosures.Any())
            {
                result.Warnings.Add("No disclosures defined for this action");
            }
        }
        catch (Exception ex)
        {
            // WARNING: All exceptions are caught here and converted to error messages in the result.
            // This includes unexpected failures (e.g., NullReferenceException, ObjectDisposedException)
            // that may indicate bugs rather than business logic errors. If an ILogger is added to this
            // class in the future, log at Error level with the full exception (ex) here.
            result.Success = false;
            result.Errors.Add($"Action processing error: {ex.Message}");
        }

        return result;
    }
}
