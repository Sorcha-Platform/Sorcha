// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using System.Text.Json.Serialization;
using Sorcha.UI.Core.Models.Designer;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Sorcha.UI.Core.Services;

/// <summary>
/// Service for serializing and deserializing blueprints to/from JSON and YAML formats.
/// Uses JSON as an intermediary for YAML to correctly handle System.Text.Json types
/// (JsonNode, JsonDocument) that YamlDotNet cannot natively round-trip.
/// </summary>
public class BlueprintSerializationService
{
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly JsonSerializerOptions _yamlImportJsonOptions;
    private readonly ISerializer _yamlSerializer;
    private readonly IDeserializer _yamlDeserializer;

    public BlueprintSerializationService()
    {
        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
        };

        // Lenient options for YAML-to-JSON import path: YamlDotNet's JsonCompatible()
        // serializer emits all scalars as JSON strings (e.g. "1" instead of 1),
        // so we must allow reading numbers and booleans from strings.
        _yamlImportJsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            NumberHandling = JsonNumberHandling.AllowReadingFromString,
            Converters =
            {
                new JsonStringEnumConverter(JsonNamingPolicy.CamelCase),
                new LenientBooleanConverter()
            }
        };

        _yamlSerializer = new SerializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
            .Build();

        _yamlDeserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();
    }

    /// <summary>
    /// Serializes a blueprint to JSON format.
    /// </summary>
    public string ToJson(Blueprint.Models.Blueprint blueprint)
    {
        var exportModel = new BlueprintExportModel
        {
            Blueprint = blueprint,
            ExportedAt = DateTimeOffset.UtcNow
        };

        return JsonSerializer.Serialize(exportModel, _jsonOptions);
    }

    /// <summary>
    /// Serializes a blueprint to YAML format.
    /// Uses JSON as an intermediary to correctly handle System.Text.Json types
    /// (JsonNode, JsonDocument, etc.) that YamlDotNet cannot natively serialize.
    /// </summary>
    public string ToYaml(Blueprint.Models.Blueprint blueprint)
    {
        // Serialize to JSON first (handles JsonNode, JsonDocument, etc. correctly)
        var json = ToJson(blueprint);

        // Deserialize JSON into a generic object graph that YamlDotNet can handle
        var jsonDeserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .Build();
        var objectGraph = jsonDeserializer.Deserialize<object>(json);

        // Serialize the generic object graph to YAML
        return _yamlSerializer.Serialize(objectGraph!);
    }

    /// <summary>
    /// Validates and parses a blueprint from file content.
    /// Automatically detects JSON or YAML format.
    /// </summary>
    public ImportValidationResult ValidateAndParse(string content, string fileName)
    {
        var isYaml = fileName.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase) ||
                     fileName.EndsWith(".yml", StringComparison.OrdinalIgnoreCase);

        try
        {
            string jsonContent;

            if (isYaml)
            {
                // Convert YAML to JSON first, then deserialize via System.Text.Json
                // so that JsonNode/JsonDocument properties are correctly reconstituted.
                var yamlObject = _yamlDeserializer.Deserialize<object>(content);
                if (yamlObject is null)
                {
                    return ImportValidationResult.Failure(new ImportValidationError(
                        "/",
                        "File does not contain valid YAML content",
                        ImportErrorType.InvalidFormat));
                }

                var jsonSerializer = new SerializerBuilder()
                    .JsonCompatible()
                    .Build();
                jsonContent = jsonSerializer.Serialize(yamlObject);
            }
            else
            {
                jsonContent = content;
            }

            // Check raw JSON for missing blueprint.id before deserialization
            // (Blueprint.Id has a default Guid value, so it's never empty after deserialization)
            bool idMissingFromSource = false;
            using (var doc = JsonDocument.Parse(jsonContent))
            {
                if (doc.RootElement.TryGetProperty("blueprint", out var blueprintElement))
                {
                    idMissingFromSource = !blueprintElement.TryGetProperty("id", out var idElement)
                                          || idElement.ValueKind == JsonValueKind.Null
                                          || (idElement.ValueKind == JsonValueKind.String
                                              && string.IsNullOrWhiteSpace(idElement.GetString()));
                }
            }

            var deserializeOptions = isYaml ? _yamlImportJsonOptions : _jsonOptions;
            var exportModel = JsonSerializer.Deserialize<BlueprintExportModel>(jsonContent, deserializeOptions);

            if (exportModel?.Blueprint is null)
            {
                return ImportValidationResult.Failure(new ImportValidationError(
                    "/",
                    "File does not contain a valid blueprint",
                    ImportErrorType.InvalidFormat));
            }

            var errors = new List<ImportValidationError>();
            var warnings = new List<ImportValidationWarning>();

            // Validate required fields
            if (string.IsNullOrWhiteSpace(exportModel.Blueprint.Title))
            {
                errors.Add(new ImportValidationError(
                    "/blueprint/title",
                    "Blueprint title is required",
                    ImportErrorType.MissingRequiredField));
            }

            if (idMissingFromSource)
            {
                // Ensure an ID is present (the default value from the model is fine)
                if (string.IsNullOrWhiteSpace(exportModel.Blueprint.Id))
                {
                    exportModel.Blueprint.Id = Guid.NewGuid().ToString();
                }
                warnings.Add(new ImportValidationWarning(
                    "/blueprint/id",
                    "Blueprint ID was missing and has been auto-generated"));
            }

            // Validate participant references in actions
            var participantIds = exportModel.Blueprint.Participants?.Select(p => p.Id).ToHashSet() ?? [];
            if (exportModel.Blueprint.Actions is not null)
            {
                foreach (var action in exportModel.Blueprint.Actions)
                {
                    // Check Target participant reference
                    if (!string.IsNullOrEmpty(action.Target) && !participantIds.Contains(action.Target))
                    {
                        warnings.Add(new ImportValidationWarning(
                            $"/blueprint/actions/{action.Id}/target",
                            $"Action '{action.Title}' references non-existent target participant '{action.Target}'"));
                    }

                    // Check Sender participant reference
                    if (!string.IsNullOrEmpty(action.Sender) && !participantIds.Contains(action.Sender))
                    {
                        warnings.Add(new ImportValidationWarning(
                            $"/blueprint/actions/{action.Id}/sender",
                            $"Action '{action.Title}' references non-existent sender '{action.Sender}'"));
                    }
                }
            }

            // Check format version
            if (!string.IsNullOrEmpty(exportModel.FormatVersion) &&
                !exportModel.FormatVersion.StartsWith("1."))
            {
                warnings.Add(new ImportValidationWarning(
                    "/formatVersion",
                    $"Blueprint was exported with format version {exportModel.FormatVersion}, current version is 1.0"));
            }

            if (errors.Count > 0)
            {
                return new ImportValidationResult
                {
                    IsValid = false,
                    Errors = errors,
                    Warnings = warnings
                };
            }

            return new ImportValidationResult
            {
                IsValid = true,
                Blueprint = exportModel.Blueprint,
                Warnings = warnings
            };
        }
        catch (JsonException ex)
        {
            return ImportValidationResult.Failure(new ImportValidationError(
                ex.Path ?? "/",
                $"Invalid JSON: {ex.Message}",
                ImportErrorType.InvalidFormat));
        }
        catch (YamlDotNet.Core.YamlException ex)
        {
            return ImportValidationResult.Failure(new ImportValidationError(
                $"Line {ex.Start.Line}",
                $"Invalid YAML: {ex.Message}",
                ImportErrorType.InvalidFormat));
        }
        catch (Exception ex)
        {
            return ImportValidationResult.Failure(new ImportValidationError(
                "/",
                $"Failed to parse file: {ex.Message}",
                ImportErrorType.InvalidFormat));
        }
    }

    /// <summary>
    /// Gets the appropriate file extension for a format.
    /// </summary>
    public static string GetFileExtension(ExportFormat format) => format switch
    {
        ExportFormat.Json => ".json",
        ExportFormat.Yaml => ".yaml",
        _ => ".json"
    };

    /// <summary>
    /// Gets the appropriate MIME type for a format.
    /// </summary>
    public static string GetMimeType(ExportFormat format) => format switch
    {
        ExportFormat.Json => "application/json",
        ExportFormat.Yaml => "text/yaml",
        _ => "application/octet-stream"
    };
}

/// <summary>
/// Supported export formats.
/// </summary>
public enum ExportFormat
{
    Json,
    Yaml
}

/// <summary>
/// JSON converter that accepts boolean values encoded as strings (e.g. "true"/"false"),
/// which occurs when YAML scalars are serialized to JSON via YamlDotNet's JsonCompatible() mode.
/// </summary>
internal sealed class LenientBooleanConverter : JsonConverter<bool>
{
    public override bool Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.True) return true;
        if (reader.TokenType == JsonTokenType.False) return false;
        if (reader.TokenType == JsonTokenType.String)
        {
            var str = reader.GetString();
            if (bool.TryParse(str, out var result)) return result;
        }
        throw new JsonException($"Cannot convert {reader.TokenType} to Boolean.");
    }

    public override void Write(Utf8JsonWriter writer, bool value, JsonSerializerOptions options)
    {
        writer.WriteBooleanValue(value);
    }
}
