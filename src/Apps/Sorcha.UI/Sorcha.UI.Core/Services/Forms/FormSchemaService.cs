// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using System.Text.Json.Nodes;
using Sorcha.Blueprint.Models;

namespace Sorcha.UI.Core.Services.Forms;

/// <summary>
/// Handles JSON Schema operations for the form renderer.
/// </summary>
public class FormSchemaService : IFormSchemaService
{
    public JsonDocument? MergeSchemas(IEnumerable<JsonDocument>? schemas)
    {
        if (schemas is null)
            return null;

        var schemaList = schemas.ToList();
        if (schemaList.Count == 0)
            return null;

        if (schemaList.Count == 1)
            return schemaList[0];

        // Merge multiple schemas by combining their properties and required arrays
        var mergedProperties = new JsonObject();
        var mergedRequired = new JsonArray();

        foreach (var schema in schemaList)
        {
            var root = schema.RootElement;

            if (root.TryGetProperty("properties", out var props))
            {
                foreach (var prop in props.EnumerateObject())
                {
                    mergedProperties[prop.Name] = JsonNode.Parse(prop.Value.GetRawText());
                }
            }

            if (root.TryGetProperty("required", out var required))
            {
                foreach (var item in required.EnumerateArray())
                {
                    var val = item.GetString();
                    if (val is not null && !mergedRequired.Any(n => n?.GetValue<string>() == val))
                        mergedRequired.Add(val);
                }
            }
        }

        var merged = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = mergedProperties,
            ["required"] = mergedRequired
        };

        return JsonDocument.Parse(merged.ToJsonString());
    }

    public JsonElement? GetSchemaForScope(JsonDocument? schema, string scope)
    {
        if (schema is null || string.IsNullOrEmpty(scope))
            return null;

        var normalizedScope = NormalizeScope(scope);
        var parts = normalizedScope.TrimStart('/').Split('/');

        var current = schema.RootElement;

        foreach (var part in parts)
        {
            if (current.TryGetProperty("properties", out var properties) &&
                properties.TryGetProperty(part, out var prop))
            {
                current = prop;
            }
            else
            {
                return null;
            }
        }

        return current;
    }

    public string NormalizeScope(string scope)
    {
        if (string.IsNullOrEmpty(scope))
            return scope;

        return scope.StartsWith('/') ? scope : "/" + scope;
    }

    public Dictionary<string, List<string>> ValidateData(JsonDocument? schema, Dictionary<string, object?> data)
    {
        var errors = new Dictionary<string, List<string>>();
        if (schema is null)
            return errors;

        var root = schema.RootElement;

        // Check required fields
        if (root.TryGetProperty("required", out var required))
        {
            root.TryGetProperty("properties", out var propsForLabels);
            foreach (var item in required.EnumerateArray())
            {
                var fieldName = item.GetString();
                if (fieldName is null) continue;

                var scope = "/" + fieldName;
                if (!data.TryGetValue(scope, out var value) || IsEmptyValue(value))
                {
                    var label = ResolveFieldLabel(propsForLabels, fieldName);
                    errors[scope] = [$"{label} is required"];
                }
            }
        }

        // Validate each field against its schema
        if (root.TryGetProperty("properties", out var properties))
        {
            foreach (var prop in properties.EnumerateObject())
            {
                var scope = "/" + prop.Name;
                if (data.TryGetValue(scope, out var value) && !IsEmptyValue(value))
                {
                    var fieldErrors = ValidateFieldValue(prop.Value, value);
                    if (fieldErrors.Count > 0)
                    {
                        if (errors.TryGetValue(scope, out var existing))
                            existing.AddRange(fieldErrors);
                        else
                            errors[scope] = fieldErrors;
                    }
                }
            }
        }

        return errors;
    }

    public List<string> ValidateField(JsonDocument? schema, string scope, object? value)
    {
        if (schema is null)
            return [];

        var fieldSchema = GetSchemaForScope(schema, scope);
        if (fieldSchema is null)
            return [];

        var errors = new List<string>();

        // Check required
        if (IsRequired(schema, scope) && IsEmptyValue(value))
        {
            var fieldName = scope.TrimStart('/').Split('/').Last();
            schema.RootElement.TryGetProperty("properties", out var propsForLabel);
            var label = ResolveFieldLabel(propsForLabel, fieldName);
            errors.Add($"{label} is required");
            return errors;
        }

        if (!IsEmptyValue(value))
        {
            errors.AddRange(ValidateFieldValue(fieldSchema.Value, value));
        }

        return errors;
    }

    public List<string> GetEnumValues(JsonDocument? schema, string scope)
    {
        var fieldSchema = GetSchemaForScope(schema, scope);
        if (fieldSchema is null)
            return [];

        if (fieldSchema.Value.TryGetProperty("enum", out var enumValues))
        {
            return enumValues.EnumerateArray()
                .Select(e => e.GetString() ?? e.GetRawText())
                .ToList();
        }

        return [];
    }

    public bool IsRequired(JsonDocument? schema, string scope)
    {
        if (schema is null)
            return false;

        var fieldName = NormalizeScope(scope).TrimStart('/').Split('/').First();

        if (schema.RootElement.TryGetProperty("required", out var required))
        {
            return required.EnumerateArray()
                .Any(r => r.GetString() == fieldName);
        }

        return false;
    }

    public Control AutoGenerateForm(IEnumerable<JsonDocument>? schemas)
    {
        var root = new Control
        {
            ControlType = ControlTypes.Layout,
            Layout = LayoutTypes.VerticalLayout,
            Title = "Form"
        };

        var merged = MergeSchemas(schemas);
        if (merged is null)
            return root;

        if (!merged.RootElement.TryGetProperty("properties", out var properties))
            return root;

        foreach (var prop in properties.EnumerateObject())
        {
            var control = InferControlFromSchema(prop.Name, prop.Value);
            root.Elements.Add(control);
        }

        return root;
    }

    private Control InferControlFromSchema(string propertyName, JsonElement schema)
    {
        var type = schema.TryGetProperty("type", out var typeEl) ? typeEl.GetString() : "string";
        var title = schema.TryGetProperty("title", out var titleEl) ? titleEl.GetString() : HumanizeName(propertyName);
        var hasEnum = schema.TryGetProperty("enum", out _);
        var format = schema.TryGetProperty("format", out var formatEl) ? formatEl.GetString() : null;

        ControlTypes controlType;

        if (hasEnum)
        {
            controlType = ControlTypes.Selection;
        }
        else if (format is "date" or "date-time")
        {
            controlType = ControlTypes.DateTime;
        }
        else
        {
            controlType = type switch
            {
                "number" or "integer" => ControlTypes.Numeric,
                "boolean" => ControlTypes.Checkbox,
                "string" when GetMaxLength(schema) > 500 => ControlTypes.TextArea,
                _ => ControlTypes.TextLine
            };
        }

        return new Control
        {
            ControlType = controlType,
            Title = title ?? propertyName,
            Scope = "/" + propertyName,
            Rule = TryParseXRule(schema)
        };
    }

    /// <summary>
    /// Parses an <c>x-rule</c> extension on a property schema into a
    /// <see cref="FormRule"/>. Follows the JSON Forms rule spec — the rule has
    /// an <c>effect</c> (SHOW / HIDE / ENABLE / DISABLE) and a schema-based
    /// <c>condition</c> with a JSON Pointer <c>scope</c> and a <c>schema</c>
    /// fragment to match against the scoped value.
    /// </summary>
    /// <remarks>
    /// Example blueprint JSON:
    /// <code>
    /// {
    ///   "medicalConditionsList": {
    ///     "type": "string",
    ///     "title": "Medical conditions",
    ///     "x-rule": {
    ///       "effect": "SHOW",
    ///       "condition": {
    ///         "scope": "/hasMedicalCondition",
    ///         "schema": { "const": true }
    ///       }
    ///     }
    ///   }
    /// }
    /// </code>
    /// Returns null on any parse failure so the form falls back to
    /// always-visible rendering rather than crashing.
    /// </remarks>
    private static FormRule? TryParseXRule(JsonElement propertySchema)
    {
        if (propertySchema.ValueKind != JsonValueKind.Object) return null;
        if (!propertySchema.TryGetProperty("x-rule", out var ruleElement)) return null;
        if (ruleElement.ValueKind != JsonValueKind.Object) return null;

        try
        {
            // Parse effect (default to SHOW)
            var effect = RuleEffect.SHOW;
            if (ruleElement.TryGetProperty("effect", out var effectEl) &&
                effectEl.ValueKind == JsonValueKind.String)
            {
                var effectStr = effectEl.GetString();
                if (Enum.TryParse<RuleEffect>(effectStr, ignoreCase: true, out var parsed))
                    effect = parsed;
            }

            // Parse condition
            if (!ruleElement.TryGetProperty("condition", out var conditionEl) ||
                conditionEl.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var scope = conditionEl.TryGetProperty("scope", out var scopeEl) &&
                        scopeEl.ValueKind == JsonValueKind.String
                ? scopeEl.GetString() ?? string.Empty
                : string.Empty;

            if (string.IsNullOrEmpty(scope)) return null;

            if (!conditionEl.TryGetProperty("schema", out var schemaEl) ||
                schemaEl.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var schemaNode = System.Text.Json.Nodes.JsonNode.Parse(schemaEl.GetRawText());
            if (schemaNode is null) return null;

            return new FormRule
            {
                Effect = effect,
                Condition = new SchemaBasedCondition
                {
                    Scope = scope,
                    Schema = schemaNode
                }
            };
        }
        catch
        {
            return null;
        }
    }

    private static int GetMaxLength(JsonElement schema)
    {
        return schema.TryGetProperty("maxLength", out var ml) ? ml.GetInt32() : 0;
    }

    private static string HumanizeName(string name)
    {
        // Convert camelCase/PascalCase to "Title Case"
        var result = new System.Text.StringBuilder();
        for (int i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (i > 0 && char.IsUpper(c))
                result.Append(' ');
            result.Append(i == 0 ? char.ToUpper(c) : c);
        }
        return result.ToString();
    }

    /// <summary>
    /// Returns the human-readable label for a field. Prefers the schema
    /// <c>title</c> property if defined; otherwise falls back to a humanised
    /// version of the field name (e.g., <c>firstName</c> → <c>First Name</c>).
    /// </summary>
    private static string ResolveFieldLabel(JsonElement properties, string fieldName)
    {
        if (properties.ValueKind == JsonValueKind.Object &&
            properties.TryGetProperty(fieldName, out var fieldSchema) &&
            fieldSchema.ValueKind == JsonValueKind.Object &&
            fieldSchema.TryGetProperty("title", out var titleEl) &&
            titleEl.ValueKind == JsonValueKind.String)
        {
            var title = titleEl.GetString();
            if (!string.IsNullOrWhiteSpace(title))
                return title;
        }
        return HumanizeName(fieldName);
    }

    private static List<string> ValidateFieldValue(JsonElement fieldSchema, object? value)
    {
        var errors = new List<string>();
        var type = fieldSchema.TryGetProperty("type", out var typeEl) ? typeEl.GetString() : null;

        var strValue = value?.ToString() ?? "";

        switch (type)
        {
            case "string":
                if (fieldSchema.TryGetProperty("minLength", out var minLen) && strValue.Length < minLen.GetInt32())
                    errors.Add($"Must be at least {minLen.GetInt32()} characters");
                if (fieldSchema.TryGetProperty("maxLength", out var maxLen) && strValue.Length > maxLen.GetInt32())
                    errors.Add($"Must be at most {maxLen.GetInt32()} characters");
                if (fieldSchema.TryGetProperty("pattern", out var pattern))
                {
                    try
                    {
                        if (!System.Text.RegularExpressions.Regex.IsMatch(strValue, pattern.GetString()!))
                            errors.Add("Value does not match the required pattern");
                    }
                    catch (ArgumentException) { /* Invalid regex in schema -- skip pattern validation */ }
                }
                if (fieldSchema.TryGetProperty("enum", out var enumValues))
                {
                    var allowed = enumValues.EnumerateArray().Select(e => e.GetString()).ToList();
                    if (!allowed.Contains(strValue))
                        errors.Add($"Must be one of: {string.Join(", ", allowed)}");
                }
                break;

            case "number":
            case "integer":
                if (decimal.TryParse(strValue, out var numValue))
                {
                    if (fieldSchema.TryGetProperty("minimum", out var min) && numValue < min.GetDecimal())
                        errors.Add($"Must be at least {min.GetDecimal()}");
                    if (fieldSchema.TryGetProperty("maximum", out var max) && numValue > max.GetDecimal())
                        errors.Add($"Must be at most {max.GetDecimal()}");
                }
                break;
        }

        return errors;
    }

    private static bool IsEmptyValue(object? value)
    {
        return value is null ||
               (value is string s && string.IsNullOrWhiteSpace(s)) ||
               (value is JsonElement je && je.ValueKind == JsonValueKind.Null);
    }
}
