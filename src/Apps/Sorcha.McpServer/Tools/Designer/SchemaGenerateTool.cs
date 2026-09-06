// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Sorcha.McpServer.Infrastructure;
using Sorcha.McpServer.Services;

namespace Sorcha.McpServer.Tools.Designer;

/// <summary>
/// Designer tool for generating JSON Schema from sample data.
/// </summary>
[McpServerToolType]
public sealed class SchemaGenerateTool
{
    private readonly IMcpAuthorizationService _authService;
    private readonly IMcpErrorHandler _errorHandler;
    private readonly ILogger<SchemaGenerateTool> _logger;

    public SchemaGenerateTool(
        IMcpAuthorizationService authService,
        IMcpErrorHandler errorHandler,
        ILogger<SchemaGenerateTool> logger)
    {
        _authService = authService;
        _errorHandler = errorHandler;
        _logger = logger;
    }

    /// <summary>
    /// Generates a JSON Schema from sample JSON data.
    /// </summary>
    /// <param name="sampleJson">Sample JSON data to generate schema from.</param>
    /// <param name="makeRequired">Whether to mark all properties as required (default: false).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Generated JSON Schema.</returns>
    [McpServerTool(Name = "sorcha_schema_generate")]
    [Description("Infers a JSON Schema definition from a sample JSON document by analysing its structure and value types, optionally marking every property as required. Call this when bootstrapping a new blueprint action schema from an example payload, rather than authoring the schema by hand; use sorcha_schema_validate after this to confirm the generated schema is well-formed, and prefer hand-edited schemas over generated ones when you need constraints (enum values, formats, ranges) that cannot be inferred from a single sample.")]
    public Task<SchemaGenerateResult> GenerateSchemaAsync(
        [Description("Sample JSON data to generate schema from")] string sampleJson,
        [Description("Mark all properties as required (default: false)")] bool makeRequired = false,
        CancellationToken cancellationToken = default)
    {
        // Authorization check
        if (!_authService.CanInvokeTool("sorcha_schema_generate"))
        {
            return Task.FromResult(new SchemaGenerateResult
            {
                Status = "Unauthorized",
                Message = "Access denied. This tool requires the sorcha:designer role.",
                CheckedAt = DateTimeOffset.UtcNow
            });
        }

        // Validate input
        if (string.IsNullOrWhiteSpace(sampleJson))
        {
            return Task.FromResult(new SchemaGenerateResult
            {
                Status = "Error",
                Message = "Sample JSON is required.",
                CheckedAt = DateTimeOffset.UtcNow
            });
        }

        _logger.LogInformation("Generating JSON Schema from sample data");

        var stopwatch = Stopwatch.StartNew();

        try
        {
            // Parse the sample JSON
            JsonDocument doc;
            try
            {
                doc = JsonDocument.Parse(sampleJson);
            }
            catch (JsonException ex)
            {
                stopwatch.Stop();
                return Task.FromResult(new SchemaGenerateResult
                {
                    Status = "Error",
                    Message = $"Invalid JSON format: {ex.Message}",
                    CheckedAt = DateTimeOffset.UtcNow,
                    ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
                });
            }

            using (doc)
            {
                // Generate schema from the document
                var schema = GenerateSchemaFromElement(doc.RootElement, makeRequired);

                stopwatch.Stop();

                var schemaJson = JsonSerializer.Serialize(schema, new JsonSerializerOptions
                {
                    WriteIndented = true
                });

                _logger.LogInformation(
                    "Schema generation completed in {ElapsedMs}ms",
                    stopwatch.ElapsedMilliseconds);

                return Task.FromResult(new SchemaGenerateResult
                {
                    Status = "Success",
                    Message = "JSON Schema generated successfully from sample data.",
                    CheckedAt = DateTimeOffset.UtcNow,
                    ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds,
                    Schema = schemaJson,
                    PropertyCount = CountProperties(schema)
                });
            }
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Unexpected error generating schema");

            return Task.FromResult(new SchemaGenerateResult
            {
                Status = "Error",
                Message = $"Failed to generate schema: {ex.Message}",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
            });
        }
    }

    private static Dictionary<string, object> GenerateSchemaFromElement(JsonElement element, bool makeRequired)
    {
        var schema = new Dictionary<string, object>
        {
            ["$schema"] = "https://json-schema.org/draft/2020-12/schema"
        };

        AddSchemaForElement(schema, element, makeRequired);

        return schema;
    }

    private static void AddSchemaForElement(Dictionary<string, object> schema, JsonElement element, bool makeRequired)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                schema["type"] = "object";
                var properties = new Dictionary<string, object>();
                var required = new List<string>();

                foreach (var prop in element.EnumerateObject())
                {
                    var propSchema = new Dictionary<string, object>();
                    AddSchemaForElement(propSchema, prop.Value, makeRequired);
                    properties[prop.Name] = propSchema;

                    if (makeRequired)
                    {
                        required.Add(prop.Name);
                    }
                }

                if (properties.Count > 0)
                {
                    schema["properties"] = properties;
                }

                if (required.Count > 0)
                {
                    schema["required"] = required;
                }
                break;

            case JsonValueKind.Array:
                schema["type"] = "array";
                if (element.GetArrayLength() > 0)
                {
                    var itemSchema = new Dictionary<string, object>();
                    AddSchemaForElement(itemSchema, element[0], makeRequired);
                    schema["items"] = itemSchema;
                }
                break;

            case JsonValueKind.String:
                schema["type"] = "string";
                var stringValue = element.GetString();
                // Try to detect common formats
                if (stringValue != null)
                {
                    if (DateTime.TryParse(stringValue, out _))
                    {
                        schema["format"] = "date-time";
                    }
                    else if (stringValue.Contains('@') && stringValue.Contains('.'))
                    {
                        schema["format"] = "email";
                    }
                    else if (Uri.TryCreate(stringValue, UriKind.Absolute, out _))
                    {
                        schema["format"] = "uri";
                    }
                }
                break;

            case JsonValueKind.Number:
                if (element.TryGetInt64(out _))
                {
                    schema["type"] = "integer";
                }
                else
                {
                    schema["type"] = "number";
                }
                break;

            case JsonValueKind.True:
            case JsonValueKind.False:
                schema["type"] = "boolean";
                break;

            case JsonValueKind.Null:
                schema["type"] = "null";
                break;
        }
    }

    private static int CountProperties(Dictionary<string, object> schema)
    {
        if (schema.TryGetValue("properties", out var props) && props is Dictionary<string, object> properties)
        {
            return properties.Count;
        }
        return 0;
    }
}

/// <summary>
/// Result of a schema generation operation.
/// </summary>
public sealed record SchemaGenerateResult
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
    /// The generated JSON Schema.
    /// </summary>
    public string? Schema { get; init; }

    /// <summary>
    /// Number of properties in the generated schema.
    /// </summary>
    public int PropertyCount { get; init; }
}
