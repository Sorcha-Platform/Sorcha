// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using JsonLogic.Net;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Newtonsoft.Json.Linq;
using Sorcha.McpServer.Infrastructure;
using Sorcha.McpServer.Services;

namespace Sorcha.McpServer.Tools.Designer;

/// <summary>
/// Designer tool for testing JSON Logic expressions.
/// </summary>
[McpServerToolType]
public sealed class JsonLogicTestTool
{
    private readonly IMcpSessionService _sessionService;
    private readonly IMcpAuthorizationService _authService;
    private readonly IMcpErrorHandler _errorHandler;
    private readonly ILogger<JsonLogicTestTool> _logger;
    private readonly JsonLogicEvaluator _evaluator;

    public JsonLogicTestTool(
        IMcpSessionService sessionService,
        IMcpAuthorizationService authService,
        IMcpErrorHandler errorHandler,
        ILogger<JsonLogicTestTool> logger)
    {
        _sessionService = sessionService;
        _authService = authService;
        _errorHandler = errorHandler;
        _logger = logger;
        _evaluator = new JsonLogicEvaluator(EvaluateOperators.Default);
    }

    /// <summary>
    /// Tests a JSON Logic expression against sample data.
    /// </summary>
    /// <param name="ruleJson">The JSON Logic rule to test.</param>
    /// <param name="dataJson">The data to evaluate the rule against.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The evaluation result.</returns>
    [McpServerTool(Name = "sorcha_jsonlogic_test")]
    [Description("Evaluates a standalone JSON Logic expression against a sample data document and returns the computed result, letting you verify routing predicates, calculation formulas, or disclosure conditions in isolation. Call this when iterating on a rule before pasting it into a blueprint, or when debugging why a rule produced an unexpected value; use sorcha_blueprint_simulate instead when you need the rule evaluated in the context of an actual action with its schema and routing, and prefer this over sorcha_blueprint_validate when the question is about logic rather than schema conformance.")]
    public Task<JsonLogicTestResult> TestJsonLogicAsync(
        [Description("The JSON Logic rule to test")] string ruleJson,
        [Description("The data to evaluate the rule against")] string dataJson,
        CancellationToken cancellationToken = default)
    {
        // Authorization check
        if (!_authService.CanInvokeTool("sorcha_jsonlogic_test"))
        {
            return Task.FromResult(new JsonLogicTestResult
            {
                Status = "Unauthorized",
                Message = "Access denied. This tool requires the sorcha:designer role.",
                CheckedAt = DateTimeOffset.UtcNow
            });
        }

        // Validate inputs
        if (string.IsNullOrWhiteSpace(ruleJson))
        {
            return Task.FromResult(new JsonLogicTestResult
            {
                Status = "Error",
                Message = "Rule JSON is required.",
                CheckedAt = DateTimeOffset.UtcNow
            });
        }

        if (string.IsNullOrWhiteSpace(dataJson))
        {
            return Task.FromResult(new JsonLogicTestResult
            {
                Status = "Error",
                Message = "Data JSON is required.",
                CheckedAt = DateTimeOffset.UtcNow
            });
        }

        _logger.LogInformation("Testing JSON Logic expression");

        var stopwatch = Stopwatch.StartNew();

        try
        {
            // Parse rule using Newtonsoft.Json (required by JsonLogic.Net)
            JToken rule;
            try
            {
                rule = JToken.Parse(ruleJson);
            }
            catch (Newtonsoft.Json.JsonReaderException ex)
            {
                stopwatch.Stop();
                return Task.FromResult(new JsonLogicTestResult
                {
                    Status = "Error",
                    Message = $"Invalid rule JSON format: {ex.Message}",
                    CheckedAt = DateTimeOffset.UtcNow,
                    ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
                });
            }

            // Parse data using Newtonsoft.Json
            object? data;
            try
            {
                data = JToken.Parse(dataJson);
            }
            catch (Newtonsoft.Json.JsonReaderException ex)
            {
                stopwatch.Stop();
                return Task.FromResult(new JsonLogicTestResult
                {
                    Status = "Error",
                    Message = $"Invalid data JSON format: {ex.Message}",
                    CheckedAt = DateTimeOffset.UtcNow,
                    ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
                });
            }

            // Evaluate the rule
            var result = _evaluator.Apply(rule, data);

            stopwatch.Stop();

            // Serialize result
            var resultJson = result != null
                ? Newtonsoft.Json.JsonConvert.SerializeObject(result, Newtonsoft.Json.Formatting.Indented)
                : "null";

            // Determine result type - handle both native types and JToken types
            string resultType;
            bool isTruthy;

            if (result == null)
            {
                resultType = "null";
                isTruthy = false;
            }
            else if (result is JValue jValue)
            {
                resultType = jValue.Type switch
                {
                    JTokenType.Null => "null",
                    JTokenType.Boolean => "boolean",
                    JTokenType.String => "string",
                    JTokenType.Integer or JTokenType.Float => "number",
                    _ => "object"
                };
                isTruthy = jValue.Type switch
                {
                    JTokenType.Null => false,
                    JTokenType.Boolean => jValue.Value<bool>(),
                    JTokenType.Integer => jValue.Value<long>() != 0,
                    JTokenType.Float => jValue.Value<double>() != 0,
                    JTokenType.String => !string.IsNullOrEmpty(jValue.Value<string>()),
                    _ => true
                };
            }
            else if (result is JArray)
            {
                resultType = "array";
                isTruthy = true;
            }
            else if (result is JObject)
            {
                resultType = "object";
                isTruthy = true;
            }
            else
            {
                resultType = result switch
                {
                    bool => "boolean",
                    string => "string",
                    int or long or float or double or decimal => "number",
                    System.Collections.IEnumerable => "array",
                    _ => "object"
                };
                isTruthy = result switch
                {
                    false => false,
                    0 => false,
                    "" => false,
                    _ => true
                };
            }

            _logger.LogInformation(
                "JSON Logic evaluation completed in {ElapsedMs}ms. Result type: {ResultType}",
                stopwatch.ElapsedMilliseconds, resultType);

            return Task.FromResult(new JsonLogicTestResult
            {
                Status = "Success",
                Message = $"JSON Logic expression evaluated successfully. Result is {resultType}.",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds,
                Result = resultJson,
                ResultType = resultType,
                IsTruthy = isTruthy
            });
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Error evaluating JSON Logic expression");

            return Task.FromResult(new JsonLogicTestResult
            {
                Status = "Error",
                Message = $"Failed to evaluate JSON Logic expression: {ex.Message}",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
            });
        }
    }
}

/// <summary>
/// Result of a JSON Logic test operation.
/// </summary>
public sealed record JsonLogicTestResult
{
    /// <summary>
    /// Operation status: Success, Error, or Unauthorized.
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
    /// The evaluation result as JSON.
    /// </summary>
    public string? Result { get; init; }

    /// <summary>
    /// The type of the result (boolean, string, number, array, object, null).
    /// </summary>
    public string? ResultType { get; init; }

    /// <summary>
    /// Whether the result is truthy in a boolean context.
    /// </summary>
    public bool IsTruthy { get; init; }
}
