// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;
using Sorcha.Blueprint.Engine.Caching;
using Sorcha.Blueprint.Engine.Interfaces;
using Sorcha.Blueprint.Engine.Models;
using Sorcha.Blueprint.Models.Schemas;

namespace Sorcha.Blueprint.Engine.Implementation;

/// <summary>
/// JSON Schema validator implementing JSON Schema Draft 2020-12.
/// </summary>
/// <remarks>
/// This implementation uses JsonSchema.Net to provide comprehensive
/// validation against JSON Schema specifications.
/// Parsed schemas are cached to avoid re-parsing on repeated validations.
///
/// Thread-safe and can be used concurrently.
/// </remarks>
public class SchemaValidator : ISchemaValidator
{
    private readonly JsonSchemaCache _schemaCache;

    public SchemaValidator(JsonSchemaCache schemaCache)
    {
        _schemaCache = schemaCache ?? throw new ArgumentNullException(nameof(schemaCache));
    }

    public SchemaValidator() : this(new JsonSchemaCache())
    {
    }

    /// <summary>
    /// Validate data against a JSON Schema.
    /// </summary>
    public async Task<ValidationResult> ValidateAsync(
        Dictionary<string, object> data,
        JsonNode schema,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(schema);

        try
        {
            // Convert data dictionary to JsonNode
            var dataJson = ConvertToJsonNode(data);

            // Strip x-* custom keywords (x-pages, x-sections, x-file, x-review, x-persona,
            // x-holder-key, …) before parsing. These are UI-renderer extensions consumed by
            // Sorcha.Blueprint.Models.SchemaLayoutParser and friends, not JSON Schema vocabulary;
            // Json.Schema is strict about unknown keywords and throws "Unknown keywords (x-…) are
            // disallowed for this dialect". Mirrors ValidationEngine.StripCustomExtensionKeywords
            // on the validator side so both validation paths tolerate the same blueprint schemas.
            var strippedSchema = StripExtensionKeywords(schema);

            // The stripped text names the Sorcha dialect, so it must be registered before
            // FromText resolves it. Idempotent.
            SorchaSchemaDialect.EnsureRegistered();

            // Parse the JSON Schema (cached — avoids re-parsing same schema)
            var jsonSchema = _schemaCache.GetOrAdd(strippedSchema, text => JsonSchema.FromText(text));

            // Convert JsonNode to JsonElement for evaluation
            var jsonString = dataJson.ToJsonString();
            using var jsonDocument = JsonDocument.Parse(jsonString);
            var dataElement = jsonDocument.RootElement;

            // Perform validation
            var validationResults = jsonSchema.Evaluate(dataElement, new EvaluationOptions
            {
                OutputFormat = OutputFormat.List,
                RequireFormatValidation = true
            });

            // Check if validation succeeded
            if (validationResults.IsValid)
            {
                return ValidationResult.Valid();
            }

            // Convert validation errors to our model
            var errors = ConvertErrors(validationResults);

            return ValidationResult.Invalid(errors);
        }
        catch (Exception ex)
        {
            // If schema parsing or validation throws, create an error
            return ValidationResult.Invalid(
                ValidationError.Create(
                    "",
                    $"Schema validation error: {ex.Message}",
                    null,
                    "schema",
                    new Dictionary<string, object>
                    {
                        ["exceptionType"] = ex.GetType().Name,
                        ["exceptionMessage"] = ex.Message
                    }
                )
            );
        }
    }

    /// <summary>
    /// Returns a copy of <paramref name="schema"/> with every property whose name starts with
    /// <c>x-</c> recursively removed. The original node is not mutated (it is typically a shared
    /// <c>Action.Form.Schema</c>). Mirrors <c>ValidationEngine.StripCustomExtensionKeywords</c>.
    /// </summary>
    private static JsonNode StripExtensionKeywords(JsonNode schema)
    {
        // Deep-clone via round-trip so the caller's schema node is never mutated.
        var clone = JsonNode.Parse(schema.ToJsonString());
        StripInPlace(clone);
        EnsureDialectDeclared(clone);
        return clone ?? new JsonObject();
    }

    /// <summary>
    /// Declares the dialect a schema is READ under. Mirrors
    /// <c>ValidationEngine.EnsureDialectDeclared</c> — read the rationale there.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Without <c>$schema</c>, Json.Schema parses under a strict default where an unknown keyword is
    /// fatal rather than an annotation. Since #1445 stopped inlined core primitives carrying their
    /// own <c>$schema</c>, nothing else in a Sorcha action schema declares one — so a primitive with
    /// a non-core keyword (<c>DateOfBirth.v1</c>'s <c>formatMaximum</c>) made the whole schema
    /// unparseable.
    /// </para>
    /// <para>
    /// The declared dialect is now Sorcha's 2020-12 superset, so <c>formatMaximum</c> /
    /// <c>formatMinimum</c> are ENFORCED rather than ignored as annotations. The two
    /// implementations must stay in lockstep or the submit path and the validate path disagree
    /// about which payloads are valid — a bound enforced on one side only is worse than neither.
    /// </para>
    /// </remarks>
    private static void EnsureDialectDeclared(JsonNode? node)
    {
        if (node is not JsonObject obj) return;

        var declared = obj.TryGetPropertyValue("$schema", out var existing)
            ? existing?.GetValue<string>()
            : null;

        obj["$schema"] = SorchaSchemaDialect.ResolveReadDialect(declared);
    }

    private static void StripInPlace(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                var toRemove = obj
                    .Where(kvp => kvp.Key.StartsWith("x-", StringComparison.Ordinal))
                    .Select(kvp => kvp.Key)
                    .ToList();
                foreach (var key in toRemove)
                {
                    obj.Remove(key);
                }

                // Relax the "type" constraint on file-reference fields — their runtime value is
                // a FileReference OBJECT (plus, for F107 portrait capture, a tokenImageBase64
                // sibling), never the declared "string". Structural validation lives in
                // FileReferenceValidator. Mirrors ValidationEngine.StripXPrefixedKeysRecursive.
                if (obj.TryGetPropertyValue("format", out var formatNode) &&
                    formatNode is JsonValue formatValue &&
                    formatValue.TryGetValue<string>(out var formatStr) &&
                    string.Equals(formatStr, "file-reference", StringComparison.Ordinal))
                {
                    obj.Remove("type");
                }

                foreach (var kvp in obj)
                {
                    StripInPlace(kvp.Value);
                }
                break;

            case JsonArray arr:
                foreach (var item in arr)
                {
                    StripInPlace(item);
                }
                break;
        }
    }

    /// <summary>
    /// Converts a dictionary to JsonNode for validation.
    /// </summary>
    private static JsonNode ConvertToJsonNode(Dictionary<string, object> data)
    {
        // Serialize dictionary to JSON string and parse back to JsonNode
        var json = JsonSerializer.Serialize(data);
        return JsonNode.Parse(json) ?? new JsonObject();
    }

    /// <summary>
    /// Converts JsonSchema.Net validation results to our ValidationError model.
    /// </summary>
    private static List<ValidationError> ConvertErrors(EvaluationResults results)
    {
        var errors = new List<ValidationError>();

        // JsonSchema.Net returns nested results, we need to flatten them
        CollectErrors(results, errors);

        // If no errors were collected but validation failed, add a generic error
        if (errors.Count == 0)
        {
            errors.Add(ValidationError.Create(
                "",
                "Validation failed but no specific errors were reported"
            ));
        }

        return errors;
    }

    /// <summary>
    /// Recursively collects validation errors from the evaluation results.
    /// </summary>
    private static void CollectErrors(EvaluationResults results, List<ValidationError> errors)
    {
        // If this result has errors, add them
        if (!results.IsValid && results.Errors != null)
        {
            foreach (var (key, value) in results.Errors)
            {
                var error = ValidationError.Create(
                    instanceLocation: results.InstanceLocation.ToString(),
                    message: value,
                    schemaLocation: results.SchemaLocation.ToString(),
                    keyword: key
                );
                errors.Add(error);
            }
        }

        // Recursively process child results
        if (results.Details != null)
        {
            foreach (var detail in results.Details)
            {
                CollectErrors(detail, errors);
            }
        }
    }
}
