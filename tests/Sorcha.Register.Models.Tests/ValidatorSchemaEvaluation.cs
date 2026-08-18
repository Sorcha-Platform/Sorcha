// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;

namespace Sorcha.Register.Models.Tests;

/// <summary>
/// Parses and evaluates a shipped action schema <b>the way the Validator does</b>, so a contract
/// test's verdict means what a live seal would mean.
/// </summary>
/// <remarks>
/// <para>
/// This mirrors <c>ValidationEngine.StripCustomExtensionKeywords</c> plus the engine's
/// <see cref="EvaluationOptions"/>. It is deliberately one copy shared by every governance contract
/// test rather than a helper per file: a hand-maintained mirror of production behaviour that exists
/// twice is a drift risk of exactly the kind these tests are here to catch.
/// </para>
/// <para>
/// <b>The strip is not optional.</b> JsonSchema.Net refuses to parse a schema carrying an unknown
/// keyword at all — <i>"Unknown keywords (x-enumNames) are disallowed for this dialect"</i> — so a
/// test that skipped it would fail on the shipped schema for a reason production never encounters.
/// </para>
/// <para>
/// The engine's other relaxation — dropping <c>type</c> from <c>format: "file-reference"</c> fields —
/// is <b>not</b> mirrored. No governance schema uses it, and the callers assert that rather than
/// assuming it.
/// </para>
/// </remarks>
internal static class ValidatorSchemaEvaluation
{
    internal static readonly EvaluationOptions Options = new()
    {
        OutputFormat = OutputFormat.List,
        RequireFormatValidation = true,
    };

    internal static JsonSchema Parse(JsonElement schema)
    {
        var node = JsonNode.Parse(schema.GetRawText());
        StripXPrefixed(node);
        DeclareDialect(node);

        Sorcha.Blueprint.Models.Schemas.SorchaSchemaDialect.EnsureRegistered();
        return JsonSchema.FromText(node!.ToJsonString());
    }

    /// <summary>
    /// Mirrors <c>ValidationEngine.EnsureDialectDeclared</c>: a document declaring nothing, or plain
    /// 2020-12, is read under the Sorcha dialect (2020-12 plus <c>formatMaximum</c> /
    /// <c>formatMinimum</c>).
    /// </summary>
    /// <remarks>
    /// Without this the mirror diverged from production in two ways that would both surface as a
    /// contract test disagreeing with a live seal: an undeclared dialect parses under a strict
    /// default where an unknown keyword is FATAL, and a declared 2020-12 silently ignores the date
    /// bounds that production now enforces. No governance schema uses those keywords today, so
    /// this is drift insurance rather than a fix — which is precisely when it is cheapest to add.
    /// </remarks>
    private static void DeclareDialect(JsonNode? node)
    {
        if (node is not JsonObject obj) return;

        var declared = obj.TryGetPropertyValue("$schema", out var existing)
            ? existing?.GetValue<string>()
            : null;

        obj["$schema"] = Sorcha.Blueprint.Models.Schemas.SorchaSchemaDialect.ResolveReadDialect(declared);
    }

    internal static EvaluationResults Evaluate(JsonElement schema, JsonElement instance) =>
        Parse(schema).Evaluate(instance, Options);

    /// <summary>Flattens a result tree into readable failure reasons for an assertion message.</summary>
    internal static IEnumerable<string> Failures(EvaluationResults results)
    {
        if (results.Errors is not null)
        {
            foreach (var error in results.Errors)
            {
                yield return $"{results.InstanceLocation}: {error.Key} {error.Value}";
            }
        }

        if (results.Details is null) yield break;

        foreach (var detail in results.Details)
        {
            foreach (var nested in Failures(detail))
            {
                yield return nested;
            }
        }
    }

    private static void StripXPrefixed(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var key in obj.Where(kvp => kvp.Key.StartsWith("x-", StringComparison.Ordinal))
                             .Select(kvp => kvp.Key).ToList())
                {
                    obj.Remove(key);
                }

                foreach (var kvp in obj)
                {
                    StripXPrefixed(kvp.Value);
                }

                break;

            case JsonArray arr:
                foreach (var item in arr)
                {
                    StripXPrefixed(item);
                }

                break;
        }
    }
}
