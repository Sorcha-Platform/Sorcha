// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Logic;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Sorcha.McpServer.Infrastructure;
using Sorcha.McpServer.Services;

namespace Sorcha.McpServer.Tools.Designer;

/// <summary>
/// Designer tool for testing JSON Logic expressions.
/// </summary>
/// <remarks>
/// Backed by the same json-everything <c>Json.Logic</c> engine that
/// <c>Sorcha.Blueprint.Engine.Implementation.JsonLogicEvaluator</c> uses at
/// runtime, so test results in the designer match what a deployed blueprint
/// would compute. Prior implementation used the unrelated <c>JsonLogic.Net</c>
/// package; consolidated on the json-everything family to remove a duplicate
/// dependency and a Newtonsoft.Json bridge layer.
/// </remarks>
[McpServerToolType]
public sealed class JsonLogicTestTool
{
    private readonly IMcpAuthorizationService _authService;
    private readonly IMcpErrorHandler _errorHandler;
    private readonly ILogger<JsonLogicTestTool> _logger;

    public JsonLogicTestTool(
        IMcpAuthorizationService authService,
        IMcpErrorHandler errorHandler,
        ILogger<JsonLogicTestTool> logger)
    {
        _authService = authService;
        _errorHandler = errorHandler;
        _logger = logger;
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
        if (!_authService.CanInvokeTool("sorcha_jsonlogic_test"))
        {
            return Task.FromResult(new JsonLogicTestResult
            {
                Status = "Unauthorized",
                Message = "Access denied. This tool requires the sorcha:designer role.",
                CheckedAt = DateTimeOffset.UtcNow
            });
        }

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
            Rule? rule;
            try
            {
                rule = JsonSerializer.Deserialize<Rule>(ruleJson);
            }
            catch (JsonException ex)
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

            if (rule is null)
            {
                stopwatch.Stop();
                return Task.FromResult(new JsonLogicTestResult
                {
                    Status = "Error",
                    Message = "Rule JSON deserialised to null.",
                    CheckedAt = DateTimeOffset.UtcNow,
                    ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
                });
            }

            JsonNode? dataNode;
            try
            {
                dataNode = JsonNode.Parse(dataJson);
            }
            catch (JsonException ex)
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

            var resultNode = rule.Apply(dataNode);

            stopwatch.Stop();

            var resultJson = resultNode is null
                ? "null"
                : resultNode.ToJsonString(new JsonSerializerOptions { WriteIndented = true });

            var (resultType, isTruthy) = ClassifyResult(resultNode);

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

    /// <summary>
    /// Maps a <see cref="JsonNode"/> result to (resultType, isTruthy) using
    /// the same truthiness rules JSON Logic itself applies: null/false/0/""
    /// are falsy, empty arrays are falsy, everything else is truthy.
    /// </summary>
    /// <remarks>
    /// Uses <see cref="JsonNode.GetValueKind"/> rather than a chain of
    /// <c>TryGetValue&lt;T&gt;</c> calls because Json.Logic's arithmetic
    /// operations can wrap results as <see cref="decimal"/> internally, and
    /// the typed gets don't cross numeric-CLR-type boundaries — e.g. a
    /// <c>JsonValue&lt;decimal&gt;(15m)</c> returns false for
    /// <c>TryGetValue&lt;long&gt;</c>. <c>GetValueKind</c> classifies via the
    /// underlying JSON shape and is stable across all numeric wrappings.
    /// </remarks>
    private static (string resultType, bool isTruthy) ClassifyResult(JsonNode? node)
    {
        if (node is null)
        {
            return ("null", false);
        }

        var kind = node.GetValueKind();
        switch (kind)
        {
            case JsonValueKind.Null:
                return ("null", false);
            case JsonValueKind.True:
                return ("boolean", true);
            case JsonValueKind.False:
                return ("boolean", false);
            case JsonValueKind.String:
                var s = node.GetValue<string>();
                return ("string", !string.IsNullOrEmpty(s));
            case JsonValueKind.Number:
                // Json.Logic wraps numeric results as JsonValue<decimal/long/int/double>
                // depending on the operator. Try each in turn — typed GetValue<T> on
                // a JsonValue only succeeds when the requested T matches the underlying
                // CLR type exactly, so a chain is the simplest robust path.
                var value = node.AsValue();
                if (value.TryGetValue<decimal>(out var dec))
                {
                    return ("number", dec != 0m);
                }
                if (value.TryGetValue<long>(out var lng))
                {
                    return ("number", lng != 0L);
                }
                if (value.TryGetValue<double>(out var dbl))
                {
                    return ("number", dbl != 0.0);
                }
                if (value.TryGetValue<int>(out var intv))
                {
                    return ("number", intv != 0);
                }
                // Last-resort round-trip via JSON text — handles any future numeric
                // wrapping (BigInteger, etc.) without code change.
                return ("number", node.ToJsonString() is not ("0" or "0.0"));
            case JsonValueKind.Array:
                return ("array", ((JsonArray)node).Count > 0);
            case JsonValueKind.Object:
                return ("object", true);
            default:
                return ("object", true);
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
