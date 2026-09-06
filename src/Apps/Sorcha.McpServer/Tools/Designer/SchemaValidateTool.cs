// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using Json.Schema;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Sorcha.McpServer.Infrastructure;
using Sorcha.McpServer.Services;

namespace Sorcha.McpServer.Tools.Designer;

/// <summary>
/// Designer tool for validating JSON Schema definitions.
/// </summary>
[McpServerToolType]
public sealed class SchemaValidateTool
{
    private readonly IMcpAuthorizationService _authService;
    private readonly IMcpErrorHandler _errorHandler;
    private readonly ILogger<SchemaValidateTool> _logger;

    public SchemaValidateTool(
        IMcpAuthorizationService authService,
        IMcpErrorHandler errorHandler,
        ILogger<SchemaValidateTool> logger)
    {
        _authService = authService;
        _errorHandler = errorHandler;
        _logger = logger;
    }

    /// <summary>
    /// Validates a JSON Schema definition for correctness.
    /// </summary>
    /// <param name="schemaJson">The JSON Schema to validate.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Validation result indicating if the schema is valid.</returns>
    [McpServerTool(Name = "sorcha_schema_validate")]
    [Description("Parses a JSON Schema document and returns whether it is well-formed under the JSON Schema specification, along with structural metadata (root type, declared properties, required fields, and definition counts). Call this on any schema before embedding it in a blueprint action so authoring errors surface early; use sorcha_blueprint_validate instead when you want to validate a data payload against an already-deployed action schema, and prefer this over sorcha_schema_generate when you have a hand-authored schema and need to confirm it parses rather than build one from sample data.")]
    public Task<SchemaValidateResult> ValidateSchemaAsync(
        [Description("The JSON Schema to validate")] string schemaJson,
        CancellationToken cancellationToken = default)
    {
        // Authorization check
        if (!_authService.CanInvokeTool("sorcha_schema_validate"))
        {
            return Task.FromResult(new SchemaValidateResult
            {
                Status = "Unauthorized",
                Message = "Access denied. This tool requires the sorcha:designer role.",
                CheckedAt = DateTimeOffset.UtcNow
            });
        }

        // Validate input
        if (string.IsNullOrWhiteSpace(schemaJson))
        {
            return Task.FromResult(new SchemaValidateResult
            {
                Status = "Error",
                Message = "Schema JSON is required.",
                CheckedAt = DateTimeOffset.UtcNow
            });
        }

        _logger.LogInformation("Validating JSON Schema");

        var stopwatch = Stopwatch.StartNew();

        try
        {
            // Parse the schema
            JsonSchema schema;
            try
            {
                schema = JsonSchema.FromText(schemaJson);
            }
            catch (JsonException ex)
            {
                stopwatch.Stop();
                return Task.FromResult(new SchemaValidateResult
                {
                    Status = "Invalid",
                    Message = $"Invalid JSON format: {ex.Message}",
                    IsValid = false,
                    CheckedAt = DateTimeOffset.UtcNow,
                    ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds,
                    Errors = [new SchemaError { Message = ex.Message, Location = "root" }]
                });
            }

            // Collect schema info
            var schemaInfo = AnalyzeSchema(schema, schemaJson);

            stopwatch.Stop();

            _logger.LogInformation(
                "Schema validation completed in {ElapsedMs}ms. Valid: {IsValid}",
                stopwatch.ElapsedMilliseconds, true);

            return Task.FromResult(new SchemaValidateResult
            {
                Status = "Valid",
                Message = "JSON Schema is valid and well-formed.",
                IsValid = true,
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds,
                SchemaInfo = schemaInfo
            });
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Unexpected error validating schema");

            return Task.FromResult(new SchemaValidateResult
            {
                Status = "Error",
                Message = $"Failed to validate schema: {ex.Message}",
                IsValid = false,
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds,
                Errors = [new SchemaError { Message = ex.Message, Location = "root" }]
            });
        }
    }

    private static SchemaInfo AnalyzeSchema(JsonSchema schema, string schemaJson)
    {
        var doc = JsonDocument.Parse(schemaJson);
        var root = doc.RootElement;

        var info = new SchemaInfo();

        // Get type
        if (root.TryGetProperty("type", out var typeProp))
        {
            info.Type = typeProp.GetString();
        }

        // Get title
        if (root.TryGetProperty("title", out var titleProp))
        {
            info.Title = titleProp.GetString();
        }

        // Get description
        if (root.TryGetProperty("description", out var descProp))
        {
            info.Description = descProp.GetString();
        }

        // Count properties
        if (root.TryGetProperty("properties", out var propsProp) && propsProp.ValueKind == JsonValueKind.Object)
        {
            var propertyNames = new List<string>();
            foreach (var prop in propsProp.EnumerateObject())
            {
                propertyNames.Add(prop.Name);
            }
            info.PropertyCount = propertyNames.Count;
            info.PropertyNames = propertyNames;
        }

        // Get required fields
        if (root.TryGetProperty("required", out var requiredProp) && requiredProp.ValueKind == JsonValueKind.Array)
        {
            var requiredFields = new List<string>();
            foreach (var item in requiredProp.EnumerateArray())
            {
                var value = item.GetString();
                if (value != null)
                {
                    requiredFields.Add(value);
                }
            }
            info.RequiredFields = requiredFields;
        }

        // Check for definitions
        if (root.TryGetProperty("$defs", out var defsProp) && defsProp.ValueKind == JsonValueKind.Object)
        {
            info.HasDefinitions = true;
            info.DefinitionCount = defsProp.EnumerateObject().Count();
        }
        else if (root.TryGetProperty("definitions", out var oldDefsProp) && oldDefsProp.ValueKind == JsonValueKind.Object)
        {
            info.HasDefinitions = true;
            info.DefinitionCount = oldDefsProp.EnumerateObject().Count();
        }

        doc.Dispose();

        return info;
    }
}

/// <summary>
/// Result of a schema validation operation.
/// </summary>
public sealed record SchemaValidateResult
{
    /// <summary>
    /// Operation status: Valid, Invalid, Error, or Unauthorized.
    /// </summary>
    public required string Status { get; init; }

    /// <summary>
    /// Human-readable message about the validation result.
    /// </summary>
    public required string Message { get; init; }

    /// <summary>
    /// Whether the schema is valid.
    /// </summary>
    public bool IsValid { get; init; }

    /// <summary>
    /// When the validation was performed.
    /// </summary>
    public required DateTimeOffset CheckedAt { get; init; }

    /// <summary>
    /// Response time in milliseconds.
    /// </summary>
    public int ResponseTimeMs { get; init; }

    /// <summary>
    /// Information about the validated schema.
    /// </summary>
    public SchemaInfo? SchemaInfo { get; init; }

    /// <summary>
    /// List of validation errors (if any).
    /// </summary>
    public IReadOnlyList<SchemaError> Errors { get; init; } = [];
}

/// <summary>
/// Information about a validated schema.
/// </summary>
public sealed record SchemaInfo
{
    /// <summary>
    /// The schema type (e.g., object, array).
    /// </summary>
    public string? Type { get; set; }

    /// <summary>
    /// The schema title.
    /// </summary>
    public string? Title { get; set; }

    /// <summary>
    /// The schema description.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Number of properties defined.
    /// </summary>
    public int PropertyCount { get; set; }

    /// <summary>
    /// Names of properties defined.
    /// </summary>
    public IReadOnlyList<string> PropertyNames { get; set; } = [];

    /// <summary>
    /// Names of required fields.
    /// </summary>
    public IReadOnlyList<string> RequiredFields { get; set; } = [];

    /// <summary>
    /// Whether the schema has definitions/$defs.
    /// </summary>
    public bool HasDefinitions { get; set; }

    /// <summary>
    /// Number of definitions.
    /// </summary>
    public int DefinitionCount { get; set; }
}

/// <summary>
/// A schema validation error.
/// </summary>
public sealed record SchemaError
{
    /// <summary>
    /// The error message.
    /// </summary>
    public required string Message { get; init; }

    /// <summary>
    /// Location in the schema where the error occurred.
    /// </summary>
    public string? Location { get; init; }
}
