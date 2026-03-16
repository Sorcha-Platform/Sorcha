// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;

namespace Sorcha.Blueprint.Schemas.Client;

/// <summary>
/// Recursively extracts fields from a JSON Schema, producing a flattened hierarchy
/// (e.g., "address.street") with type, constraints, and required markers.
/// </summary>
public static class SchemaFieldExtractor
{
    /// <summary>
    /// Extracts all fields from a JSON Schema, including nested objects.
    /// </summary>
    public static IReadOnlyList<SchemaFieldInfo> ExtractFields(JsonDocument schema, int maxDepth = 5)
    {
        var fields = new List<SchemaFieldInfo>();
        ExtractFieldsFromElement(schema.RootElement, "", fields, new HashSet<string>(), 0, maxDepth);
        return fields;
    }

    /// <summary>
    /// Extracts all fields from a JSON Schema string.
    /// </summary>
    public static IReadOnlyList<SchemaFieldInfo> ExtractFields(string schemaJson, int maxDepth = 5)
    {
        using var doc = JsonDocument.Parse(schemaJson);
        return ExtractFields(doc, maxDepth);
    }

    private static void ExtractFieldsFromElement(
        JsonElement element,
        string prefix,
        List<SchemaFieldInfo> fields,
        HashSet<string> visited,
        int depth,
        int maxDepth)
    {
        if (depth > maxDepth) return;

        var requiredSet = new HashSet<string>(StringComparer.Ordinal);
        if (element.TryGetProperty("required", out var required) && required.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in required.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                    requiredSet.Add(item.GetString()!);
            }
        }

        var defs = new Dictionary<string, JsonElement>();
        if (element.TryGetProperty("$defs", out var defsElement) && defsElement.ValueKind == JsonValueKind.Object)
        {
            foreach (var def in defsElement.EnumerateObject())
                defs[$"#/$defs/{def.Name}"] = def.Value;
        }
        if (element.TryGetProperty("definitions", out var definitionsElement) && definitionsElement.ValueKind == JsonValueKind.Object)
        {
            foreach (var def in definitionsElement.EnumerateObject())
                defs[$"#/definitions/{def.Name}"] = def.Value;
        }

        if (element.TryGetProperty("properties", out var properties) && properties.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in properties.EnumerateObject())
            {
                var path = string.IsNullOrEmpty(prefix) ? prop.Name : $"{prefix}.{prop.Name}";
                var isRequired = requiredSet.Contains(prop.Name);
                var propElement = prop.Value;

                if (propElement.TryGetProperty("$ref", out var refElement) && refElement.ValueKind == JsonValueKind.String)
                {
                    var refPath = refElement.GetString()!;
                    if (!visited.Contains(refPath) && defs.TryGetValue(refPath, out var resolved))
                    {
                        visited.Add(refPath);
                        propElement = resolved;
                    }
                }

                var fieldInfo = BuildFieldInfo(path, propElement, isRequired);
                fields.Add(fieldInfo);

                var fieldType = GetFieldType(propElement);
                if (fieldType == "object")
                {
                    ExtractFieldsFromElement(propElement, path, fields, visited, depth + 1, maxDepth);
                }
                else if (fieldType == "array" &&
                         propElement.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Object)
                {
                    var itemsType = GetFieldType(items);
                    if (itemsType == "object" || items.TryGetProperty("properties", out _))
                    {
                        ExtractFieldsFromElement(items, $"{path}[]", fields, visited, depth + 1, maxDepth);
                    }
                }
            }
        }
    }

    private static SchemaFieldInfo BuildFieldInfo(string path, JsonElement element, bool isRequired)
    {
        var type = GetFieldType(element);
        var format = element.TryGetProperty("format", out var fmt) && fmt.ValueKind == JsonValueKind.String
            ? fmt.GetString() : null;
        var description = element.TryGetProperty("description", out var desc) && desc.ValueKind == JsonValueKind.String
            ? desc.GetString() : null;
        double? minimum = element.TryGetProperty("minimum", out var min) && min.ValueKind == JsonValueKind.Number
            ? min.GetDouble() : null;
        double? maximum = element.TryGetProperty("maximum", out var max) && max.ValueKind == JsonValueKind.Number
            ? max.GetDouble() : null;
        int? minLength = element.TryGetProperty("minLength", out var minLen) && minLen.ValueKind == JsonValueKind.Number
            ? minLen.GetInt32() : null;
        int? maxLength = element.TryGetProperty("maxLength", out var maxLen) && maxLen.ValueKind == JsonValueKind.Number
            ? maxLen.GetInt32() : null;
        string? pattern = element.TryGetProperty("pattern", out var pat) && pat.ValueKind == JsonValueKind.String
            ? pat.GetString() : null;

        string[]? enumValues = null;
        if (element.TryGetProperty("enum", out var enumProp) && enumProp.ValueKind == JsonValueKind.Array)
        {
            enumValues = enumProp.EnumerateArray()
                .Where(e => e.ValueKind == JsonValueKind.String)
                .Select(e => e.GetString()!)
                .ToArray();
        }

        string? example = null;
        if (element.TryGetProperty("examples", out var examples) && examples.ValueKind == JsonValueKind.Array)
        {
            var first = examples.EnumerateArray().FirstOrDefault();
            if (first.ValueKind != JsonValueKind.Undefined)
                example = first.ToString();
        }
        else if (element.TryGetProperty("default", out var defaultVal) && defaultVal.ValueKind != JsonValueKind.Undefined)
        {
            example = defaultVal.ToString();
        }

        var depth = path.Count(c => c == '.');

        return new SchemaFieldInfo
        {
            Path = path,
            Name = path.Contains('.') ? path[(path.LastIndexOf('.') + 1)..] : path,
            Type = type,
            Format = format,
            Description = description,
            IsRequired = isRequired,
            Depth = depth,
            Minimum = minimum,
            Maximum = maximum,
            MinLength = minLength,
            MaxLength = maxLength,
            Pattern = pattern,
            EnumValues = enumValues,
            Example = example
        };
    }

    private static string GetFieldType(JsonElement element)
    {
        if (element.TryGetProperty("type", out var type))
        {
            if (type.ValueKind == JsonValueKind.String)
                return type.GetString()!;
            if (type.ValueKind == JsonValueKind.Array)
                return string.Join("|", type.EnumerateArray()
                    .Where(t => t.ValueKind == JsonValueKind.String)
                    .Select(t => t.GetString()));
        }

        if (element.TryGetProperty("properties", out _)) return "object";
        if (element.TryGetProperty("items", out _)) return "array";

        return "unknown";
    }
}

/// <summary>
/// Extracted field information from a JSON Schema.
/// </summary>
public sealed class SchemaFieldInfo
{
    public required string Path { get; init; }
    public required string Name { get; init; }
    public required string Type { get; init; }
    public string? Format { get; init; }
    public string? Description { get; init; }
    public required bool IsRequired { get; init; }
    public required int Depth { get; init; }
    public double? Minimum { get; init; }
    public double? Maximum { get; init; }
    public int? MinLength { get; init; }
    public int? MaxLength { get; init; }
    public string? Pattern { get; init; }
    public string[]? EnumValues { get; init; }
    public string? Example { get; init; }
}
