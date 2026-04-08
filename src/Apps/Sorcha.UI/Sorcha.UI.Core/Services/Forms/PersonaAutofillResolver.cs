// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using Microsoft.Extensions.Logging;
using Sorcha.Tenant.Models.Persona;
using Sorcha.UI.Core.Models.Forms;

namespace Sorcha.UI.Core.Services.Forms;

/// <summary>
/// Pure function: given a merged JSON Schema describing a form and the
/// signed-in user's persona, produces the set of autofill decisions that
/// <c>SorchaFormRenderer</c> should apply. Feature 092.
/// </summary>
/// <remarks>
/// Matching rules are defined in plan.md §6.2 and research.md Decision 5.
/// <para>Priority order per field:</para>
/// <list type="number">
/// <item>Schema <c>default</c> keyword — treated as an author-supplied value
/// and is left to the existing form renderer's default handling. The
/// resolver <b>skips</b> fields that carry a schema default so persona never
/// overwrites an authored default.</item>
/// <item>Explicit <c>x-persona</c> extension — the attribute name it contains
/// wins unconditionally when the persona has that attribute. A literal
/// <c>false</c> (or the string <c>"false"</c>) explicitly blocks persona
/// autofill on the field.</item>
/// <item>Inference allowlist — conservative rules only:
/// <list type="bullet">
/// <item><c>format: "email"</c> → default email</item>
/// <item><c>format: "tel"</c> → default phone</item>
/// <item>field name exactly one of <c>dateOfBirth</c>, <c>dob</c>, <c>birthDate</c>
/// (case-insensitive) → date of birth</item>
/// <item>postal-address schema shape (object with recognised child properties)
/// → default address</item>
/// </list></item>
/// </list>
/// <para>Array-valued fields and nested objects other than recognised postal
/// addresses are <b>not</b> autofilled by v1.</para>
/// <para>All pointers returned are relative to the form data root (<c>"/"</c>).</para>
/// </remarks>
public sealed class PersonaAutofillResolver
{
    private readonly ILogger<PersonaAutofillResolver>? _logger;

    /// <summary>
    /// Creates a new resolver. Logging is optional — when unavailable
    /// (e.g. unit tests), warnings from malformed schema extensions are
    /// silently dropped.
    /// </summary>
    public PersonaAutofillResolver(ILogger<PersonaAutofillResolver>? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// Resolves the set of autofill decisions for a form.
    /// </summary>
    /// <param name="schema">
    /// The merged JSON Schema document for the form (<see cref="FormContext.DataSchema"/>).
    /// May be null — returns an empty dictionary.
    /// </param>
    /// <param name="persona">
    /// The signed-in user's persona read model. May be null — returns an empty dictionary.
    /// </param>
    /// <returns>
    /// A dictionary keyed by JSON Pointer (e.g. <c>"/email"</c>) describing
    /// which persona attribute should fill each field and its provenance.
    /// Fields not in the dictionary are not filled by persona.
    /// </returns>
    public IReadOnlyDictionary<string, PersonaFillResult> Resolve(
        JsonDocument? schema,
        PersonaReadModelV1? persona)
    {
        var results = new Dictionary<string, PersonaFillResult>(StringComparer.Ordinal);

        if (schema is null || persona is null)
        {
            return results;
        }

        var root = schema.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            return results;
        }

        WalkProperties(root, basePointer: string.Empty, persona, results);
        return results;
    }

    private void WalkProperties(
        JsonElement schemaNode,
        string basePointer,
        PersonaReadModelV1 persona,
        Dictionary<string, PersonaFillResult> results)
    {
        if (!schemaNode.TryGetProperty("properties", out var props) ||
            props.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (var prop in props.EnumerateObject())
        {
            var name = prop.Name;
            var propSchema = prop.Value;
            if (propSchema.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var pointer = basePointer + "/" + name;

            // (1) Skip fields with an authored default — the existing form
            // renderer applies those; persona must not compete.
            if (propSchema.TryGetProperty("default", out _))
            {
                continue;
            }

            // (2) Explicit x-persona extension
            if (propSchema.TryGetProperty("x-persona", out var xPersona))
            {
                if (IsExplicitBlock(xPersona))
                {
                    // x-persona: false → author explicitly blocks inference.
                    continue;
                }

                var attributeName = ReadExplicitAttributeName(xPersona, pointer);
                if (attributeName is not null)
                {
                    var fill = TryBuildFill(pointer, attributeName, PersonaMatchMode.ExplicitExtension, persona);
                    if (fill is not null)
                    {
                        results[pointer] = fill;
                    }
                    continue;
                }

                // x-persona present but malformed: fall through to inference.
            }

            // Recurse into nested objects before inference so deeply nested
            // fields with x-persona still resolve. The postal-address
            // inference short-circuits the recursion when it succeeds.
            if (IsObjectType(propSchema))
            {
                if (TryMatchAddressObject(propSchema, pointer, persona, out var addressFill))
                {
                    results[pointer] = addressFill;
                    continue;
                }

                WalkProperties(propSchema, pointer, persona, results);
                continue;
            }

            // Array-of-objects and other array fields are not autofilled in v1.
            if (IsArrayType(propSchema))
            {
                continue;
            }

            // (3) Inference allowlist on scalar leaf fields.
            var inferred = InferScalarAttribute(name, propSchema);
            if (inferred is not null)
            {
                var fill = TryBuildFill(pointer, inferred, PersonaMatchMode.Inference, persona);
                if (fill is not null)
                {
                    results[pointer] = fill;
                }
            }
        }
    }

    private static bool IsExplicitBlock(JsonElement xPersona)
    {
        if (xPersona.ValueKind == JsonValueKind.False)
        {
            return true;
        }
        if (xPersona.ValueKind == JsonValueKind.String &&
            string.Equals(xPersona.GetString(), "false", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        return false;
    }

    private string? ReadExplicitAttributeName(JsonElement xPersona, string pointer)
    {
        if (xPersona.ValueKind == JsonValueKind.String)
        {
            var raw = xPersona.GetString();
            if (!string.IsNullOrWhiteSpace(raw))
            {
                return raw.Trim();
            }
        }

        if (xPersona.ValueKind == JsonValueKind.Object &&
            xPersona.TryGetProperty("attribute", out var attr) &&
            attr.ValueKind == JsonValueKind.String)
        {
            var raw = attr.GetString();
            if (!string.IsNullOrWhiteSpace(raw))
            {
                return raw.Trim();
            }
        }

        _logger?.LogWarning(
            "Ignoring malformed x-persona extension at {Pointer}: expected string or object with 'attribute'",
            pointer);
        return null;
    }

    private static bool IsObjectType(JsonElement propSchema)
    {
        if (!propSchema.TryGetProperty("type", out var typeEl))
        {
            // No type — if it has a "properties" bag, treat as object.
            return propSchema.TryGetProperty("properties", out _);
        }
        return typeEl.ValueKind == JsonValueKind.String && typeEl.GetString() == "object";
    }

    private static bool IsArrayType(JsonElement propSchema)
    {
        return propSchema.TryGetProperty("type", out var typeEl) &&
               typeEl.ValueKind == JsonValueKind.String &&
               typeEl.GetString() == "array";
    }

    private static string? InferScalarAttribute(string fieldName, JsonElement propSchema)
    {
        // format: "email" → defaultEmail
        if (propSchema.TryGetProperty("format", out var formatEl) &&
            formatEl.ValueKind == JsonValueKind.String)
        {
            var fmt = formatEl.GetString();
            if (string.Equals(fmt, "email", StringComparison.OrdinalIgnoreCase))
            {
                return "defaultEmail";
            }
            if (string.Equals(fmt, "tel", StringComparison.OrdinalIgnoreCase))
            {
                return "defaultPhone";
            }
        }

        // Field name aliases for date of birth (case-insensitive exact match).
        if (string.Equals(fieldName, "dateOfBirth", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(fieldName, "dob", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(fieldName, "birthDate", StringComparison.OrdinalIgnoreCase))
        {
            return "dateOfBirth";
        }

        return null;
    }

    /// <summary>
    /// Detects a postal-address shaped object schema and, if found, returns a
    /// <see cref="PersonaFillResult"/> that carries a map of child pointers to
    /// their values packed into a <see cref="PersonaAddress"/> value.
    /// The form renderer unpacks this into per-field writes.
    /// </summary>
    /// <remarks>
    /// For v1 simplicity, we flag the address as a single fill whose
    /// <c>Value</c> is the default <see cref="PersonaAddress"/>. The renderer
    /// is responsible for distributing the sub-fields into the form data bag.
    /// </remarks>
    private static bool TryMatchAddressObject(
        JsonElement propSchema,
        string pointer,
        PersonaReadModelV1 persona,
        out PersonaFillResult addressFill)
    {
        addressFill = null!;

        if (!propSchema.TryGetProperty("properties", out var childProps) ||
            childProps.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        // Recognise a postal-address shape by the presence of at least three
        // of: line1/street/address1, city/locality, postalCode/postcode/zip,
        // country. This is deliberately conservative — deeply nested custom
        // objects with incidental property names won't match.
        int hits = 0;
        foreach (var child in childProps.EnumerateObject())
        {
            var lower = child.Name.ToLowerInvariant();
            if (lower is "line1" or "street" or "address1" or "address") hits++;
            else if (lower is "city" or "locality" or "town") hits++;
            else if (lower is "postalcode" or "postcode" or "zip") hits++;
            else if (lower is "country" or "countrycode") hits++;
        }

        if (hits < 3 || persona.DefaultAddress is null)
        {
            return false;
        }

        addressFill = new PersonaFillResult(
            FieldPath: pointer,
            AttributeName: "defaultAddress",
            Value: persona.DefaultAddress.Value,
            Source: persona.DefaultAddress.Source,
            MatchedBy: PersonaMatchMode.Inference);
        return true;
    }

    private static PersonaFillResult? TryBuildFill(
        string pointer,
        string attributeName,
        PersonaMatchMode mode,
        PersonaReadModelV1 persona)
    {
        // Canonicalise to lowerCamelCase for matching.
        var key = attributeName.Length == 0
            ? attributeName
            : char.ToLowerInvariant(attributeName[0]) + attributeName[1..];

        return key switch
        {
            "givenName" when persona.GivenName is { } gn =>
                new PersonaFillResult(pointer, "givenName", gn.Value, gn.Source, mode),

            "familyName" when persona.FamilyName is { } fn =>
                new PersonaFillResult(pointer, "familyName", fn.Value, fn.Source, mode),

            "fullName" when persona.FullName is { } full =>
                new PersonaFillResult(pointer, "fullName", full.Value, full.Source, mode),

            "dateOfBirth" when persona.DateOfBirth is { } dob =>
                new PersonaFillResult(pointer, "dateOfBirth", dob.Value, dob.Source, mode),

            "defaultEmail" when persona.DefaultEmail is { } de =>
                new PersonaFillResult(pointer, "defaultEmail", de.Value.Value, de.Source, mode),

            "defaultPhone" when persona.DefaultPhone is { } dp =>
                new PersonaFillResult(pointer, "defaultPhone", dp.Value.Value, dp.Source, mode),

            "defaultAddress" when persona.DefaultAddress is { } da =>
                new PersonaFillResult(pointer, "defaultAddress", da.Value, da.Source, mode),

            "defaultNationality" when persona.DefaultNationality is { } dn =>
                new PersonaFillResult(pointer, "defaultNationality", dn.Value, dn.Source, mode),

            _ => null,
        };
    }
}
