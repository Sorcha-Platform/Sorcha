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

        ValidateDataRecursive(schema.RootElement, data, parentScope: string.Empty, errors);
        return errors;
    }

    /// <summary>
    /// Recursively validates a schema node against the flat compound-scope
    /// data store. For each required field on the current node:
    ///   - if the field is scalar, checks <c>data[{parentScope}/{field}]</c>
    ///   - if the field is an object, recurses into the child schema with
    ///     the new parent scope so nested requireds (Feature 103 primitives)
    ///     are enforced correctly
    /// </summary>
    private void ValidateDataRecursive(
        JsonElement schemaNode,
        Dictionary<string, object?> data,
        string parentScope,
        Dictionary<string, List<string>> errors)
    {
        // Defensive: a schema can declare `required` without a `properties`
        // block (e.g. allOf-composed schemas where the properties live on
        // a sibling branch that this code path doesn't merge). When that
        // happens, propsForLabels is null and ResolveFieldLabel falls back
        // to a humanised field name rather than passing an Undefined
        // JsonElement around.
        JsonElement? propsForLabels = schemaNode.TryGetProperty("properties", out var propsEl) &&
                                      propsEl.ValueKind == JsonValueKind.Object
            ? propsEl
            : null;

        // Required-field presence check
        if (schemaNode.TryGetProperty("required", out var required) &&
            required.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in required.EnumerateArray())
            {
                var fieldName = item.GetString();
                if (string.IsNullOrEmpty(fieldName)) continue;

                var scope = parentScope + "/" + fieldName;

                // If the child is object-typed, "presence" means at least
                // one of its OWN required children has a value — we rely on
                // the recursive step below to surface specific missing
                // children.
                var childSchema = propsForLabels?.TryGetProperty(fieldName, out var cs) == true ? cs : (JsonElement?)null;
                if (childSchema is not null && childSchema.Value.TryGetProperty("type", out var typeEl) &&
                    typeEl.GetString() == "object")
                {
                    // Object field: check nested requireds via recursion below.
                    // Don't flag the parent as missing — the recursion will
                    // produce precise errors on the missing leaves.
                    continue;
                }

                if (!data.TryGetValue(scope, out var value) || IsEmptyValue(value))
                {
                    var label = ResolveFieldLabel(propsForLabels, fieldName);
                    errors[scope] = [$"{label} is required"];
                }
            }
        }

        // Recurse into object properties so nested schemas are validated too,
        // AND run scalar value validation on every leaf that has a value.
        if (propsForLabels is not null)
        {
            foreach (var prop in propsForLabels.Value.EnumerateObject())
            {
                var scope = parentScope + "/" + prop.Name;
                var childType = prop.Value.TryGetProperty("type", out var t) ? t.GetString() : null;

                if (childType == "object")
                {
                    ValidateDataRecursive(prop.Value, data, scope, errors);
                    continue;
                }

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
            JsonElement? propsForLabel = schema.RootElement.TryGetProperty("properties", out var propsEl) &&
                                         propsEl.ValueKind == JsonValueKind.Object
                ? propsEl
                : null;
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

        // Walk the JSON Pointer segment-by-segment so nested object
        // primitives (Feature 103: /name/givenName against a
        // PersonName/v1 reference) check the CORRECT required array
        // — the one on the parent object, not the root.
        var segments = NormalizeScope(scope).TrimStart('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
            return false;

        var current = schema.RootElement;
        for (var i = 0; i < segments.Length; i++)
        {
            var segment = segments[i];
            var isLast = i == segments.Length - 1;

            if (isLast)
            {
                if (current.TryGetProperty("required", out var required) &&
                    required.ValueKind == JsonValueKind.Array)
                {
                    if (required.EnumerateArray().Any(r => r.GetString() == segment))
                        return true;
                }
                return false;
            }

            if (!current.TryGetProperty("properties", out var properties) ||
                !properties.TryGetProperty(segment, out var nextSchema))
            {
                return false;
            }

            current = nextSchema;
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
            var control = InferControlFromSchema(prop.Name, prop.Value, parentScope: string.Empty);
            root.Elements.Add(control);
        }

        return root;
    }

    /// <summary>
    /// Builds a <see cref="Control"/> tree for one schema property.
    /// </summary>
    /// <remarks>
    /// <para>
    /// For scalar properties (string / number / boolean / date) this emits a
    /// single leaf Control with <c>Scope = {parentScope}/{propertyName}</c>.
    /// </para>
    /// <para>
    /// For object-typed properties (Feature 103 core primitives like
    /// <c>PersonName/v1</c>, <c>PostalAddress/v1</c>) this recurses into
    /// <c>properties</c> and emits a VerticalLayout control whose
    /// <c>Elements</c> are the nested leaves, each with a JSON-Pointer
    /// compound scope (e.g. <c>/name/givenName</c>). The parent container
    /// itself also carries the compound scope (<c>/name</c>) so
    /// <see cref="Forms.SorchaFormRenderer"/>'s field lookup by field name
    /// finds the whole subtree and the section renderer can dispatch it
    /// inline.
    /// </para>
    /// </remarks>
    private Control InferControlFromSchema(string propertyName, JsonElement schema, string parentScope)
    {
        var type = schema.TryGetProperty("type", out var typeEl) ? typeEl.GetString() : "string";
        var title = schema.TryGetProperty("title", out var titleEl) ? titleEl.GetString() : HumanizeName(propertyName);
        var scope = parentScope + "/" + propertyName;

        // Feature 137 (cross-node submission, C3): an object field carrying
        // `format: "sorcha-holder-key"` is captured read-only by HolderKeyRenderer, which
        // fetches the citizen's public delivery keys and writes the sibling pointers
        // (/holderJwk, /encryptionPublicKey, /algorithm). Checked before the object-recursion
        // branch so a holder-key field is never recursed into as a primitive group.
        if (schema.TryGetProperty("format", out var holderKeyFormatEl)
            && holderKeyFormatEl.GetString() == "sorcha-holder-key")
        {
            return new Control
            {
                ControlType = ControlTypes.HolderKey,
                Title = title ?? propertyName,
                Scope = scope,
                Rule = TryParseXRule(schema)
            };
        }

        // Object-typed properties recurse into their children. Feature 103
        // ships four core primitives as schema objects (PersonName,
        // DateOfBirth, EmailAddress, PostalAddress) — the form renderer
        // needs nested controls so primitive-based fields actually render
        // their child inputs.
        if (type == "object" && schema.TryGetProperty("properties", out var childProps))
        {
            var group = new Control
            {
                ControlType = ControlTypes.Layout,
                Layout = LayoutTypes.VerticalLayout,
                // Suppress the nested title when rendered inside a section —
                // the section already labels the grouping and a second header
                // at the child level is visual noise. VerticalLayoutRenderer
                // treats empty Title as "no heading" via IsNullOrEmpty.
                Title = string.Empty,
                Scope = scope,
                Rule = TryParseXRule(schema)
            };

            foreach (var childProp in childProps.EnumerateObject())
            {
                group.Elements.Add(InferControlFromSchema(childProp.Name, childProp.Value, scope));
            }

            return group;
        }

        var hasEnum = schema.TryGetProperty("enum", out _);
        var format = schema.TryGetProperty("format", out var formatEl) ? formatEl.GetString() : null;

        // Feature 103 US3: a string field carrying `x-address-lookup: true`
        // dispatches to the postcode lookup control regardless of format /
        // maxLength. The control falls back to plain text at runtime when no
        // provider is configured, so we can route unconditionally here.
        var hasAddressLookup =
            schema.TryGetProperty("x-address-lookup", out var lookupEl) &&
            lookupEl.ValueKind == JsonValueKind.True;

        ControlTypes controlType;

        if (hasEnum)
        {
            controlType = ControlTypes.Selection;
        }
        else if (hasAddressLookup && type == "string")
        {
            controlType = ControlTypes.PostcodeLookup;
        }
        else if (format is "date" or "date-time")
        {
            controlType = ControlTypes.DateTime;
        }
        else if (format is "file-reference" or "binary")
        {
            // File-upload primitives — Sorcha's file-attachment format (Feature 085,
            // "file-reference", carries the x-file extension for accept/capture/embedAs and the
            // camera-first PortraitCaptureControl) and the OpenAPI byte-content convention
            // ("binary", used by document-upload fields). Both route to FileRenderer; without this
            // they fall through to a plain text line and never reach the file/camera control.
            controlType = ControlTypes.File;
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
            Scope = scope,
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
    /// Pass <see langword="null"/> for <paramref name="properties"/> when the
    /// schema has no <c>properties</c> block (e.g. allOf-composed schemas) —
    /// the method will fall back to humanising the field name.
    /// </summary>
    private static string ResolveFieldLabel(JsonElement? properties, string fieldName)
    {
        if (properties is { ValueKind: JsonValueKind.Object } props &&
            props.TryGetProperty(fieldName, out var fieldSchema) &&
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
