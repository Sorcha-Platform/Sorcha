// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Sorcha.Blueprint.Engine.Implementation;
using Sorcha.Blueprint.Engine.Interfaces;
using Sorcha.Blueprint.Engine.Models;
using Sorcha.Blueprint.Models.Schemas;
using ActionModel = Sorcha.Blueprint.Models.Action;

namespace Sorcha.Blueprint.Engine.Testing;

/// <summary>
/// A single field the generator could not (fully) satisfy, and why.
/// </summary>
/// <param name="Path">JSON Pointer (RFC 6901) to the field within the generated payload.</param>
/// <param name="Reason">Human-readable explanation — an unsupported regex, an unresolved
/// <c>$ref</c>, a <c>file-reference</c> field, an <c>x-holder-key</c>/<c>x-file</c> field, or a
/// residual schema violation the bounded repair loop could not clear.</param>
public sealed record UnsatisfiedField(string Path, string Reason);

/// <summary>
/// Result of <see cref="SchemaSamplePayloadGenerator.Generate"/>.
/// </summary>
/// <param name="Payload">The generated payload — a plain <see cref="Dictionary{TKey,TValue}"/> tree
/// ready to submit exactly as an action's data (the same shape <c>ActionSchemaValidation</c> validates).</param>
/// <param name="IsValid">True when <see cref="Payload"/> passes every schema the action declares.</param>
/// <param name="Unsatisfied">Fields the generator could not satisfy — empty when <see cref="IsValid"/> is true.</param>
public sealed record SamplePayloadResult(
    Dictionary<string, object> Payload,
    bool IsValid,
    IReadOnlyList<UnsatisfiedField> Unsatisfied);

/// <summary>
/// Generates random-but-schema-valid sample data for a blueprint action (#1724), so the Designer's
/// Rehearse stage can submit real data through every step instead of an empty payload.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why generate rather than render step forms.</b> The product decision on #1724 is to exercise
/// schema validation and payload-reading routing conditions during rehearsal without building
/// step-by-step form UI. A generated payload is enough for that: it is schema-valid, so the Validator
/// rules a real submission would hit are actually exercised, and it varies per "Regenerate" so
/// different routing branches can be walked.
/// </para>
/// <para>
/// <b>Satisfies every declared schema.</b> Schemas are resolved via
/// <see cref="ActionSchemaValidation.ResolveSchemas"/> — the same <c>dataSchemas</c>-first,
/// <c>Form.Schema</c>-fallback resolution the engine itself validates against (CLAUDE.md pattern
/// 24). Generating against a different resolution would exercise a contract nothing enforces.
/// </para>
/// <para>
/// <b>WASM-safe.</b> No I/O and no native dependency, so this runs equally inside the in-browser
/// dry-run engine and the full-rehearsal server-side path.
/// </para>
/// <para>
/// <b>Deterministic per seed.</b> The entire walk — initial generation and every repair attempt —
/// pulls from one <see cref="Random"/> seeded with the caller's <c>seed</c>. The same seed always
/// produces the same payload; a different seed generally produces different data. That is what lets
/// the Rehearse stage's "Regenerate" button move to the next seed reproducibly.
/// </para>
/// <para>
/// <b>Self-checked, not just generated.</b> After the first pass, the result is validated against
/// every declared schema with the real <see cref="ISchemaValidator"/> — the same validator
/// <c>ActionSchemaValidation</c> uses. A failing field is regenerated (bounded retries); anything
/// still unsatisfied — an unsupported regex, an unresolved <c>$ref</c>, a <c>file-reference</c>
/// field, an <c>x-holder-key</c>/<c>x-file</c> field that needs a real key or upload — is reported in
/// <see cref="SamplePayloadResult.Unsatisfied"/> rather than shipped silently invalid or thrown.
/// </para>
/// </remarks>
public sealed class SchemaSamplePayloadGenerator
{
    private const int MaxRepairAttempts = 6;

    private static readonly string[] FirstNames =
        ["Alex", "Jamie", "Taylor", "Morgan", "Casey", "Priya", "Liam", "Nadia", "Sam", "Rowan"];

    private static readonly string[] LastNames =
        ["Smith", "Khan", "Nguyen", "Garcia", "Brown", "Patel", "Murphy", "Silva", "Chen", "Walsh"];

    private static readonly string[] EmailDomains = ["example.com", "example.org", "mailtest.sorcha.dev"];
    private static readonly string[] Cities = ["Springfield", "Riverton", "Fairview", "Oakdale", "Milton Keynes"];
    private static readonly string[] Countries = ["United Kingdom", "Ireland", "Canada", "Australia"];
    private static readonly string[] StreetNames = ["High Street", "Church Lane", "Station Road", "Mill Lane"];

    private readonly ISchemaValidator _validator;

    /// <summary>Creates the generator over an <see cref="ISchemaValidator"/> — defaults to a fresh <see cref="SchemaValidator"/>.</summary>
    public SchemaSamplePayloadGenerator(ISchemaValidator? validator = null)
    {
        _validator = validator ?? new SchemaValidator();
    }

    /// <summary>
    /// Generates a sample payload for <paramref name="action"/>, deterministic for <paramref name="seed"/>.
    /// </summary>
    public SamplePayloadResult Generate(ActionModel action, int seed)
    {
        ArgumentNullException.ThrowIfNull(action);

        var schemas = ActionSchemaValidation.ResolveSchemas(action);
        if (schemas.Count == 0)
        {
            return new SamplePayloadResult(new Dictionary<string, object>(), true, []);
        }

        var ctx = new GenContext(new Random(seed), []);
        var payload = new Dictionary<string, object>();

        foreach (var schema in schemas)
        {
            if (GenerateValue(schema, ctx, "") is Dictionary<string, object> generated)
            {
                foreach (var (key, value) in generated)
                {
                    payload[key] = value;
                }
            }
        }

        var isValid = RepairUntilValid(schemas, payload, ctx);
        return new SamplePayloadResult(payload, isValid, ctx.Unsatisfied);
    }

    // ---- Self-check + bounded repair ----------------------------------------------------------

    private bool RepairUntilValid(IReadOnlyList<JsonNode> schemas, Dictionary<string, object> payload, GenContext ctx)
    {
        for (var attempt = 0; attempt < MaxRepairAttempts; attempt++)
        {
            var errors = ValidateAll(schemas, payload);
            if (errors.Count == 0)
            {
                return true;
            }

            var repairedAny = false;
            foreach (var pointer in errors.Select(e => e.InstanceLocation).Distinct())
            {
                if (TryRepair(schemas, payload, pointer, ctx))
                {
                    repairedAny = true;
                }
            }

            if (!repairedAny)
            {
                break;
            }
        }

        var finalErrors = ValidateAll(schemas, payload);
        if (finalErrors.Count == 0)
        {
            return true;
        }

        foreach (var group in finalErrors.GroupBy(e => e.InstanceLocation))
        {
            var pointer = group.Key;
            if (!ctx.Unsatisfied.Any(u => u.Path == pointer))
            {
                var reason = string.Join("; ", group.Select(e => e.Message).Distinct());
                ctx.Unsatisfied.Add(new UnsatisfiedField(pointer, reason));
            }

            // Best-effort: an optional leaf that is still invalid is better dropped than left in.
            if (!IsRequiredAnywhere(schemas, pointer))
            {
                TryRemoveAtPointer(payload, pointer);
            }
        }

        return ValidateAll(schemas, payload).Count == 0;
    }

    private List<ValidationError> ValidateAll(IReadOnlyList<JsonNode> schemas, Dictionary<string, object> payload)
    {
        var errors = new List<ValidationError>();
        foreach (var schema in schemas)
        {
            // SchemaValidator.ValidateAsync performs no actual I/O — the Task is always
            // synchronously complete, so this cannot deadlock (including under WASM's single
            // threaded SynchronizationContext).
            var result = _validator.ValidateAsync(payload, schema).GetAwaiter().GetResult();
            if (!result.IsValid)
            {
                errors.AddRange(result.Errors);
            }
        }
        return errors;
    }

    private bool TryRepair(IReadOnlyList<JsonNode> schemas, Dictionary<string, object> payload, string pointer, GenContext ctx)
    {
        var segments = SplitPointer(pointer);

        foreach (var schema in schemas)
        {
            if (!TryResolveSchemaAtPointer(schema, segments, out var resolved) || resolved is null)
            {
                continue;
            }

            // JsonSchema.Net reports a "required" violation at the OBJECT's own pointer, not the
            // missing property's — so a missing-required repair fills in whichever named
            // properties are absent, rather than regenerating the whole object.
            if (ResolveType(resolved) == "object"
                && resolved.TryGetPropertyValue("required", out var reqNode) && reqNode is JsonArray reqArr
                && resolved.TryGetPropertyValue("properties", out var propsNode) && propsNode is JsonObject propsObj)
            {
                var container = GetOrCreateContainer(payload, pointer);
                var addedAny = false;
                foreach (var reqName in reqArr.Select(n => n?.GetValue<string>()).Where(n => n is not null))
                {
                    if (container.ContainsKey(reqName!))
                    {
                        continue;
                    }
                    if (propsObj.TryGetPropertyValue(reqName!, out var propSchema) && propSchema is JsonObject)
                    {
                        var value = GenerateValue(propSchema, ctx, CombinePointer(pointer, reqName!));
                        if (value is not null)
                        {
                            container[reqName!] = value;
                            addedAny = true;
                        }
                    }
                }
                if (addedAny)
                {
                    return true;
                }
            }

            var regenerated = GenerateValue(resolved, ctx, pointer);
            if (regenerated is not null)
            {
                return SetAtPointer(payload, pointer, regenerated);
            }
        }

        return false;
    }

    private static bool IsRequiredAnywhere(IReadOnlyList<JsonNode> schemas, string pointer)
    {
        var segments = SplitPointer(pointer);
        if (segments.Length == 0)
        {
            return true; // never drop the payload root
        }

        var parentSegments = segments[..^1];
        var lastName = segments[^1];

        foreach (var schema in schemas)
        {
            if (TryResolveSchemaAtPointer(schema, parentSegments, out var parentSchema) && parentSchema is not null
                && parentSchema.TryGetPropertyValue("required", out var reqNode) && reqNode is JsonArray reqArr
                && reqArr.Any(n => string.Equals(n?.GetValue<string>(), lastName, StringComparison.Ordinal)))
            {
                return true;
            }
        }

        return false;
    }

    // ---- Recursive value generation ------------------------------------------------------------

    private object? GenerateValue(JsonNode? schemaNode, GenContext ctx, string path)
    {
        if (schemaNode is JsonValue boolValue && boolValue.TryGetValue<bool>(out var literalBool))
        {
            // JSON Schema boolean shorthand: `true` (anything) / `false` (nothing satisfies).
            return literalBool ? "value" : null;
        }

        if (schemaNode is not JsonObject schema)
        {
            return "value";
        }

        if (schema.ContainsKey("$ref"))
        {
            ctx.Unsatisfied.Add(new UnsatisfiedField(path, "$ref is not resolved by the sample generator"));
            return null;
        }

        if (schema.ContainsKey("x-holder-key") || schema.ContainsKey("x-file"))
        {
            ctx.Unsatisfied.Add(new UnsatisfiedField(path, "needs a real holder key or uploaded file, not sample data"));
            return null;
        }

        if (schema.TryGetPropertyValue("format", out var fmtNode) && fmtNode is JsonValue fmtVal
            && fmtVal.TryGetValue<string>(out var fmt) && fmt == "file-reference")
        {
            ctx.Unsatisfied.Add(new UnsatisfiedField(path, "format 'file-reference' needs a real uploaded file, not sample data"));
            return null;
        }

        if (schema.TryGetPropertyValue("const", out var constNode) && constNode is not null)
        {
            return JsonNodeToClr(constNode);
        }

        if (schema.TryGetPropertyValue("enum", out var enumNode) && enumNode is JsonArray enumArr && enumArr.Count > 0)
        {
            return JsonNodeToClr(enumArr[ctx.Rng.Next(enumArr.Count)]);
        }

        if (schema.TryGetPropertyValue("oneOf", out var oneOfNode) && oneOfNode is JsonArray oneOfArr && oneOfArr.Count > 0)
        {
            return GenerateFromBranches(oneOfArr, ctx, path);
        }

        if (schema.TryGetPropertyValue("anyOf", out var anyOfNode) && anyOfNode is JsonArray anyOfArr && anyOfArr.Count > 0)
        {
            return GenerateFromBranches(anyOfArr, ctx, path);
        }

        var effective = schema;
        if (schema.TryGetPropertyValue("allOf", out var allOfNode) && allOfNode is JsonArray allOfArr && allOfArr.Count > 0)
        {
            effective = MergeAllOf(schema, allOfArr);
        }

        var type = ResolveType(effective);
        return type switch
        {
            "object" => GenerateObject(effective, ctx, path),
            "array" => GenerateArray(effective, ctx, path),
            "string" => GenerateString(effective, ctx, path),
            "integer" => GenerateInteger(effective, ctx.Rng),
            "number" => GenerateNumber(effective, ctx.Rng),
            "boolean" => ctx.Rng.Next(2) == 1,
            _ => effective.ContainsKey("properties")
                ? GenerateObject(effective, ctx, path)
                : GenerateString(effective, ctx, path)
        };
    }

    private object? GenerateFromBranches(JsonArray branches, GenContext ctx, string path)
    {
        var order = Enumerable.Range(0, branches.Count).OrderBy(_ => ctx.Rng.Next()).ToList();
        foreach (var i in order)
        {
            if (branches[i] is JsonObject branchSchema)
            {
                var value = GenerateValue(branchSchema, ctx, path);
                if (value is not null)
                {
                    return value;
                }
            }
        }

        ctx.Unsatisfied.Add(new UnsatisfiedField(path, "no oneOf/anyOf branch could be generated"));
        return null;
    }

    private Dictionary<string, object> GenerateObject(JsonObject schema, GenContext ctx, string path)
    {
        var result = new Dictionary<string, object>();
        if (schema["properties"] is not JsonObject props)
        {
            return result;
        }

        foreach (var (propName, propSchemaNode) in props)
        {
            if (propSchemaNode is null)
            {
                continue;
            }
            var childPath = CombinePointer(path, propName);
            var value = GenerateValue(propSchemaNode, ctx, childPath);
            if (value is not null)
            {
                result[propName] = value;
            }
        }

        return result;
    }

    private List<object> GenerateArray(JsonObject schema, GenContext ctx, string path)
    {
        var minItems = GetInt(schema, "minItems") ?? 1;
        var maxItems = GetInt(schema, "maxItems") ?? Math.Max(minItems, minItems + 2);
        if (maxItems < minItems)
        {
            maxItems = minItems;
        }
        var count = minItems == maxItems ? minItems : ctx.Rng.Next(minItems, maxItems + 1);
        var uniqueItems = GetBool(schema, "uniqueItems") ?? false;

        var items = new List<object>();
        if (schema["items"] is not JsonObject itemsSchema)
        {
            for (var i = 0; i < count; i++)
            {
                items.Add($"item{i + 1}");
            }
            return items;
        }

        var guard = 0;
        while (items.Count < count && guard < count * 6 + 12)
        {
            guard++;
            var value = GenerateValue(itemsSchema, ctx, CombinePointer(path, items.Count.ToString(CultureInfo.InvariantCulture)));
            if (value is null)
            {
                break; // unsatisfiable item schema — already recorded; stop rather than loop forever.
            }
            if (uniqueItems && items.Any(existing => JsonPayloadEquals(existing, value)))
            {
                continue;
            }
            items.Add(value);
        }

        return items;
    }

    private static string GenerateString(JsonObject schema, GenContext ctx, string path)
    {
        var minLength = GetInt(schema, "minLength");
        var maxLength = GetInt(schema, "maxLength");
        var pattern = GetString(schema, "pattern");
        var format = GetString(schema, "format");

        if (pattern is not null)
        {
            var sampled = RegexSampler.TrySample(pattern, ctx.Rng);
            if (sampled is not null)
            {
                return sampled;
            }
            ctx.Unsatisfied.Add(new UnsatisfiedField(path, $"pattern '{pattern}' is not supported by the sample generator"));
            return ClampLength(LastSegmentHeuristic(path, ctx.Rng), minLength, maxLength, ctx.Rng);
        }

        if (format is not null)
        {
            var formatted = GenerateForFormat(format, schema, ctx.Rng);
            if (formatted is not null)
            {
                return formatted;
            }
        }

        return ClampLength(LastSegmentHeuristic(path, ctx.Rng), minLength, maxLength, ctx.Rng);
    }

    private static string? GenerateForFormat(string format, JsonObject schema, Random rng) => format switch
    {
        "date" => GenerateDate(schema, rng),
        "date-time" => GenerateDateTime(rng),
        "time" => GenerateTime(rng),
        "email" => GenerateEmail(rng),
        "uri" => GenerateUri(rng),
        "uuid" => GenerateUuid(rng),
        _ => null
    };

    private static object GenerateInteger(JsonObject schema, Random rng)
    {
        var min = GetLong(schema, "minimum");
        var max = GetLong(schema, "maximum");
        var exMin = GetLong(schema, "exclusiveMinimum");
        var exMax = GetLong(schema, "exclusiveMaximum");

        // A one-sided bound must anchor the default range AT the bound, never independently of
        // it — a fixed absolute fallback (e.g. "1000") silently loses to a declared minimum above
        // it, producing values BELOW the declared minimum once the two get reconciled.
        long lo, hi;
        if (min.HasValue && max.HasValue)
        {
            (lo, hi) = (min.Value, max.Value);
        }
        else if (min.HasValue)
        {
            (lo, hi) = (min.Value, min.Value + 1000);
        }
        else if (max.HasValue)
        {
            (lo, hi) = (max.Value - 1000, max.Value);
        }
        else if (exMin.HasValue)
        {
            (lo, hi) = (exMin.Value + 1, exMin.Value + 1001);
        }
        else if (exMax.HasValue)
        {
            (lo, hi) = (exMax.Value - 1001, exMax.Value - 1);
        }
        else
        {
            (lo, hi) = (-1000, 1000);
        }

        if (exMin.HasValue)
        {
            lo = Math.Max(lo, exMin.Value + 1);
        }
        if (exMax.HasValue)
        {
            hi = Math.Min(hi, exMax.Value - 1);
        }
        if (lo > hi)
        {
            (lo, hi) = (hi, lo);
        }
        if (lo == hi)
        {
            return lo;
        }

        var multipleOf = GetLong(schema, "multipleOf");
        if (multipleOf is > 0)
        {
            var m = multipleOf.Value;
            var startMultiple = (long)Math.Ceiling(lo / (double)m) * m;
            var count = (hi - startMultiple) / m;
            if (count < 0)
            {
                return startMultiple;
            }
            var k = rng.NextInt64(0, count + 1);
            return startMultiple + k * m;
        }

        return rng.NextInt64(lo, hi + 1);
    }

    private static object GenerateNumber(JsonObject schema, Random rng)
    {
        var declaredMin = GetDouble(schema, "minimum");
        var declaredMax = GetDouble(schema, "maximum");
        var exMin = GetDouble(schema, "exclusiveMinimum");
        var exMax = GetDouble(schema, "exclusiveMaximum");

        // Same reconciliation as GenerateInteger: a one-sided bound anchors the default range AT
        // the bound rather than being independently defaulted and then reconciled — see that
        // method's remark for why a fixed absolute fallback silently loses to a declared bound.
        double min, max;
        if (declaredMin.HasValue && declaredMax.HasValue)
        {
            (min, max) = (declaredMin.Value, declaredMax.Value);
        }
        else if (declaredMin.HasValue)
        {
            (min, max) = (declaredMin.Value, declaredMin.Value + 1000);
        }
        else if (declaredMax.HasValue)
        {
            (min, max) = (declaredMax.Value - 1000, declaredMax.Value);
        }
        else if (exMin.HasValue)
        {
            (min, max) = (exMin.Value, exMin.Value + 1000);
        }
        else if (exMax.HasValue)
        {
            (min, max) = (exMax.Value - 1000, exMax.Value);
        }
        else
        {
            (min, max) = (-1000, 1000);
        }

        if (exMin.HasValue)
        {
            min = Math.Max(min, exMin.Value + 0.01);
        }
        if (exMax.HasValue)
        {
            max = Math.Min(max, exMax.Value - 0.01);
        }
        if (min > max)
        {
            (min, max) = (max, min);
        }

        var multipleOf = GetDouble(schema, "multipleOf");
        if (multipleOf is > 0)
        {
            var m = multipleOf.Value;
            var startMultiple = Math.Ceiling(min / m) * m;
            var count = (int)Math.Max(0, (max - startMultiple) / m);
            var k = rng.Next(0, count + 1);
            return Math.Round(startMultiple + k * m, 6);
        }

        var value = min + rng.NextDouble() * (max - min);
        return Math.Round(value, 2);
    }

    // ---- Format generators -----------------------------------------------------------------

    private static string GenerateDate(JsonObject schema, Random rng)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        DateOnly ResolveBound(string key, DateOnly fallback)
        {
            var token = GetString(schema, key);
            if (token is null)
            {
                return fallback;
            }
            try
            {
                return SorchaDateTokenResolver.Resolve(token, today);
            }
            catch (FormatException)
            {
                return fallback;
            }
        }

        var min = ResolveBound("formatMinimum", today.AddYears(-60));
        var max = ResolveBound("formatMaximum", today.AddYears(1));
        if (min > max)
        {
            (min, max) = (max, min);
        }

        var span = max.DayNumber - min.DayNumber;
        var offset = span <= 0 ? 0 : rng.Next(0, span + 1);
        return min.AddDays(offset).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    private static string GenerateDateTime(Random rng)
    {
        var days = rng.Next(-3650, 366);
        var seconds = rng.Next(0, 86_400);
        var dt = DateTimeOffset.UtcNow.Date.AddDays(days).AddSeconds(seconds);
        return dt.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
    }

    private static string GenerateTime(Random rng) =>
        // RFC 3339 full-time requires a zone offset — a bare "HH:mm:ss" is not a valid "time"
        // per JsonSchema.Net's format check even though it looks like one.
        new TimeOnly(rng.Next(0, 24), rng.Next(0, 60), rng.Next(0, 60)).ToString("HH:mm:ss", CultureInfo.InvariantCulture) + "Z";

    private static string GenerateEmail(Random rng) =>
        $"{Pick(FirstNames, rng).ToLowerInvariant()}." +
        $"{Pick(LastNames, rng).ToLowerInvariant()}{rng.Next(1, 999)}@{Pick(EmailDomains, rng)}";

    private static string GenerateUri(Random rng) => $"https://example.com/{GenerateHex(rng, 12)}";

    private static string GenerateUuid(Random rng)
    {
        var bytes = new byte[16];
        rng.NextBytes(bytes);
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x40); // version 4
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80); // variant 10xx
        return new Guid(bytes).ToString();
    }

    private static string GenerateHex(Random rng, int length)
    {
        const string chars = "0123456789abcdef";
        var sb = new StringBuilder(length);
        for (var i = 0; i < length; i++)
        {
            sb.Append(chars[rng.Next(chars.Length)]);
        }
        return sb.ToString();
    }

    // ---- Name heuristics (unformatted strings) -----------------------------------------------

    private static string LastSegmentHeuristic(string path, Random rng)
    {
        var segments = SplitPointer(path);
        var name = segments.Length > 0 ? segments[^1] : "value";
        return GenerateHeuristicString(name, rng);
    }

    private static string GenerateHeuristicString(string propertyName, Random rng)
    {
        var key = propertyName.ToLowerInvariant();

        if (key.Contains("email"))
        {
            return GenerateEmail(rng);
        }
        if (key.Contains("phone") || key.Contains("mobile") || key.Contains("tel"))
        {
            return $"+44 7{rng.Next(100, 999)} {rng.Next(100000, 999999)}";
        }
        if (key.Contains("postcode") || key.Contains("postalcode") || key.Contains("zip"))
        {
            return $"{(char)('A' + rng.Next(26))}{(char)('A' + rng.Next(26))}{rng.Next(1, 9)} " +
                   $"{rng.Next(1, 9)}{(char)('A' + rng.Next(26))}{(char)('A' + rng.Next(26))}";
        }
        if (key is "firstname" or "givenname")
        {
            return Pick(FirstNames, rng);
        }
        if (key is "lastname" or "surname" or "familyname")
        {
            return Pick(LastNames, rng);
        }
        if (key.Contains("fullname") || key == "name")
        {
            return $"{Pick(FirstNames, rng)} {Pick(LastNames, rng)}";
        }
        if (key.Contains("address"))
        {
            return $"{rng.Next(1, 200)} {Pick(StreetNames, rng)}";
        }
        if (key.Contains("city") || key.Contains("town"))
        {
            return Pick(Cities, rng);
        }
        if (key.Contains("country"))
        {
            return Pick(Countries, rng);
        }
        if (key.Contains("url") || key.Contains("website") || key.Contains("link"))
        {
            return GenerateUri(rng);
        }
        if (key.Contains("reference") || key == "id" || key.EndsWith("id", StringComparison.Ordinal))
        {
            return $"REF-{GenerateHex(rng, 8).ToUpperInvariant()}";
        }

        return $"Sample {CultureInfo.InvariantCulture.TextInfo.ToTitleCase(propertyName)} {rng.Next(1, 999)}";
    }

    private static string Pick(string[] pool, Random rng) => pool[rng.Next(pool.Length)];

    private static string ClampLength(string value, int? minLength, int? maxLength, Random rng)
    {
        if (maxLength is { } max && value.Length > max)
        {
            value = value[..max];
        }
        if (minLength is { } min && value.Length < min)
        {
            var sb = new StringBuilder(value);
            while (sb.Length < min)
            {
                sb.Append((char)('a' + rng.Next(26)));
            }
            value = sb.ToString();
        }
        return value;
    }

    // ---- Schema helpers -----------------------------------------------------------------------

    private static string? ResolveType(JsonObject schema)
    {
        if (!schema.TryGetPropertyValue("type", out var typeNode) || typeNode is null)
        {
            return null;
        }
        if (typeNode is JsonValue v && v.TryGetValue<string>(out var s))
        {
            return s;
        }
        if (typeNode is JsonArray arr)
        {
            foreach (var t in arr)
            {
                if (t is JsonValue tv && tv.TryGetValue<string>(out var ts) && ts != "null")
                {
                    return ts;
                }
            }
        }
        return null;
    }

    private static JsonObject MergeAllOf(JsonObject baseSchema, JsonArray allOf)
    {
        var merged = JsonNode.Parse(baseSchema.ToJsonString()) as JsonObject ?? new JsonObject();
        merged.Remove("allOf");

        var mergedProps = merged["properties"] as JsonObject ?? new JsonObject();
        var mergedRequired = merged["required"] is JsonArray reqArr0
            ? reqArr0.Select(n => n?.GetValue<string>()).Where(n => n is not null).Select(n => n!).ToList()
            : new List<string>();

        foreach (var branch in allOf)
        {
            if (branch is not JsonObject branchObj)
            {
                continue;
            }
            if (branchObj["properties"] is JsonObject branchProps)
            {
                foreach (var (k, v) in branchProps)
                {
                    mergedProps[k] = v?.DeepClone();
                }
            }
            if (branchObj["required"] is JsonArray branchReq)
            {
                foreach (var r in branchReq)
                {
                    var name = r?.GetValue<string>();
                    if (name is not null && !mergedRequired.Contains(name))
                    {
                        mergedRequired.Add(name);
                    }
                }
            }
            if (branchObj["type"] is not null && merged["type"] is null)
            {
                merged["type"] = branchObj["type"]?.DeepClone();
            }
        }

        merged["properties"] = mergedProps;
        if (mergedRequired.Count > 0)
        {
            merged["required"] = new JsonArray(mergedRequired.Select(r => (JsonNode)r).ToArray());
        }

        return merged;
    }

    private static bool TryResolveSchemaAtPointer(JsonNode schemaNode, IReadOnlyList<string> segments, out JsonObject? resolved)
    {
        resolved = null;
        if (schemaNode is not JsonObject obj)
        {
            return false;
        }

        if (obj.TryGetPropertyValue("allOf", out var allOfNode) && allOfNode is JsonArray allOfArr && allOfArr.Count > 0)
        {
            obj = MergeAllOf(obj, allOfArr);
        }

        if (segments.Count == 0)
        {
            resolved = obj;
            return true;
        }

        var seg = segments[0];
        var rest = segments.Skip(1).ToArray();

        if (int.TryParse(seg, out _))
        {
            if (obj.TryGetPropertyValue("items", out var itemsNode) && itemsNode is JsonObject itemsObj)
            {
                return TryResolveSchemaAtPointer(itemsObj, rest, out resolved);
            }
            return false;
        }

        if (obj.TryGetPropertyValue("properties", out var propsNode) && propsNode is JsonObject props
            && props.TryGetPropertyValue(seg, out var propSchemaNode) && propSchemaNode is JsonObject propSchema)
        {
            return TryResolveSchemaAtPointer(propSchema, rest, out resolved);
        }

        foreach (var branchKey in new[] { "oneOf", "anyOf" })
        {
            if (obj.TryGetPropertyValue(branchKey, out var branchesNode) && branchesNode is JsonArray branches)
            {
                foreach (var branch in branches)
                {
                    if (branch is JsonObject branchObj && TryResolveSchemaAtPointer(branchObj, segments, out var r) && r is not null)
                    {
                        resolved = r;
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private static object JsonNodeToClr(JsonNode? node) => node switch
    {
        null => "value",
        JsonArray arr => arr.Select(JsonNodeToClr).ToList(),
        JsonObject obj => obj.ToDictionary(kv => kv.Key, kv => JsonNodeToClr(kv.Value)),
        JsonValue v when v.TryGetValue<bool>(out var b) => b,
        JsonValue v when v.TryGetValue<long>(out var l) => l,
        JsonValue v when v.TryGetValue<double>(out var d) => d,
        JsonValue v when v.TryGetValue<string>(out var s) => s,
        _ => node.ToString()
    };

    private static bool JsonPayloadEquals(object a, object b) =>
        System.Text.Json.JsonSerializer.Serialize(a) == System.Text.Json.JsonSerializer.Serialize(b);

    // ---- JSON Pointer helpers over Dictionary<string, object> ---------------------------------

    private static string[] SplitPointer(string pointer) =>
        string.IsNullOrEmpty(pointer)
            ? []
            : pointer.TrimStart('/').Split('/').Select(s => s.Replace("~1", "/").Replace("~0", "~")).ToArray();

    private static string CombinePointer(string basePointer, string segment)
    {
        var escaped = segment.Replace("~", "~0").Replace("/", "~1");
        return basePointer + "/" + escaped;
    }

    private static Dictionary<string, object> GetOrCreateContainer(Dictionary<string, object> root, string pointer)
    {
        var segments = SplitPointer(pointer);
        var current = root;
        foreach (var seg in segments)
        {
            if (current.TryGetValue(seg, out var next) && next is Dictionary<string, object> nextDict)
            {
                current = nextDict;
            }
            else
            {
                var created = new Dictionary<string, object>();
                current[seg] = created;
                current = created;
            }
        }
        return current;
    }

    private static bool SetAtPointer(Dictionary<string, object> root, string pointer, object value)
    {
        var segments = SplitPointer(pointer);
        if (segments.Length == 0)
        {
            return false;
        }

        object? current = root;
        for (var i = 0; i < segments.Length - 1; i++)
        {
            var seg = segments[i];
            current = current switch
            {
                Dictionary<string, object> d => d.TryGetValue(seg, out var next) ? next : CreateAndInsert(d, seg),
                List<object> l when int.TryParse(seg, out var idx) && idx < l.Count => l[idx],
                _ => null
            };
            if (current is null)
            {
                return false;
            }
        }

        var last = segments[^1];
        switch (current)
        {
            case Dictionary<string, object> d:
                d[last] = value;
                return true;
            case List<object> l when int.TryParse(last, out var idx2):
                if (idx2 < l.Count)
                {
                    l[idx2] = value;
                }
                else if (idx2 == l.Count)
                {
                    l.Add(value);
                }
                else
                {
                    return false;
                }
                return true;
            default:
                return false;
        }
    }

    private static object CreateAndInsert(Dictionary<string, object> parent, string key)
    {
        var created = new Dictionary<string, object>();
        parent[key] = created;
        return created;
    }

    private static bool TryRemoveAtPointer(Dictionary<string, object> root, string pointer)
    {
        var segments = SplitPointer(pointer);
        if (segments.Length == 0)
        {
            return false;
        }

        object? current = root;
        for (var i = 0; i < segments.Length - 1; i++)
        {
            current = current switch
            {
                Dictionary<string, object> d when d.TryGetValue(segments[i], out var next) => next,
                List<object> l when int.TryParse(segments[i], out var idx) && idx < l.Count => l[idx],
                _ => null
            };
            if (current is null)
            {
                return false;
            }
        }

        var last = segments[^1];
        switch (current)
        {
            case Dictionary<string, object> d:
                return d.Remove(last);
            case List<object> l when int.TryParse(last, out var idx2) && idx2 < l.Count:
                l.RemoveAt(idx2);
                return true;
            default:
                return false;
        }
    }

    // ---- Small scalar readers over JsonObject --------------------------------------------------

    private static string? GetString(JsonObject schema, string key) =>
        schema.TryGetPropertyValue(key, out var node) && node is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    private static int? GetInt(JsonObject schema, string key) =>
        schema.TryGetPropertyValue(key, out var node) && node is JsonValue v && v.TryGetValue<double>(out var d) ? (int)d : null;

    private static long? GetLong(JsonObject schema, string key) =>
        schema.TryGetPropertyValue(key, out var node) && node is JsonValue v && v.TryGetValue<double>(out var d) ? (long)d : null;

    private static double? GetDouble(JsonObject schema, string key) =>
        schema.TryGetPropertyValue(key, out var node) && node is JsonValue v && v.TryGetValue<double>(out var d) ? d : null;

    private static bool? GetBool(JsonObject schema, string key) =>
        schema.TryGetPropertyValue(key, out var node) && node is JsonValue v && v.TryGetValue<bool>(out var b) ? b : null;

    /// <summary>Threads the seeded RNG and the accumulating unsatisfied-field list through recursion.</summary>
    private sealed record GenContext(Random Rng, List<UnsatisfiedField> Unsatisfied);
}
