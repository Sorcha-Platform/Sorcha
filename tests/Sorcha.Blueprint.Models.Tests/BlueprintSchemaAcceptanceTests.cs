// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;

using FluentAssertions;

using Json.Schema;

namespace Sorcha.Blueprint.Models.Tests;

/// <summary>
/// Acceptance evidence for issue #1609: the updated <c>blueprint.schema.json</c> validates the three
/// worked examples the walkthrough suite actually executes, and causes no regression on any blueprint
/// under <c>blueprints/templates</c> or <c>blueprints/examples</c>.
/// </summary>
/// <remarks>
/// JsonSchema.Net's <see cref="JsonSchema.Evaluate(JsonElement, EvaluationOptions?)"/> requires a
/// <see cref="JsonElement"/>, not a <see cref="System.Text.Json.Nodes.JsonNode"/> (CLAUDE.md pattern 4).
/// </remarks>
public sealed class BlueprintSchemaAcceptanceTests
{
    private static readonly JsonSchema Schema = JsonSchema.FromText(ReadSchemaFile());

    /// <summary>
    /// Marker keys used by the pre-render blueprint-TEMPLATE format (distinct from the "template"
    /// wrapper key — see <see cref="UnwrapTemplate"/>): <c>approval-workflow-template.json</c>,
    /// <c>loan-application-template.json</c> and <c>supply-chain-order-template.json</c> embed
    /// <c>{"$eval": "paramName"}</c> / <c>{"$if": ..., "then": ..., "else": ...}</c> /
    /// <c>{"$flattenDeep": [...]}</c> placeholders in place of real string/array values — e.g.
    /// <c>"id": {"$eval": "blueprintId"}</c> where the schema requires <c>"id"</c> to be a string.
    /// These are pre-render documents for a template-evaluation engine
    /// (<see cref="TemplateEvaluationRequest"/>), not blueprints, and were never valid against
    /// blueprint.schema.json (old or new) — confirmed by re-running them against the pre-#1609 schema
    /// text. Detected structurally (does the raw JSON contain one of these keys?) rather than by
    /// filename, so the exclusion is self-documenting and does not silently widen if another
    /// unrelated file happens to share a name pattern.
    /// </summary>
    private static readonly string[] TemplateEngineMarkers = ["\"$eval\"", "\"$if\"", "\"$flattenDeep\""];

    public static IEnumerable<object[]> WalkthroughExamples()
    {
        yield return ["assured-identity", @"walkthroughs\AssuredIdentity\blueprints\assured-identity.json"];
        yield return ["encryption-at-rest", @"walkthroughs\EncryptionAtRest\blueprints\encryption-at-rest.json"];
        yield return ["ping-pong", @"walkthroughs\PingPongN1\blueprints\ping-pong.json"];
    }

    [Theory]
    [MemberData(nameof(WalkthroughExamples))]
    public void WalkthroughExample_ValidatesAgainstSchema(string name, string relativePath)
    {
        var raw = File.ReadAllText(Path.Combine(RepoRoot(), relativePath));
        var blueprintJson = UnwrapTemplate(raw);
        blueprintJson.Should().NotBeNull($"{name} must be a real blueprint document, wrapped or flat");

        var element = JsonSerializer.Deserialize<JsonElement>(blueprintJson!);
        var result = Schema.Evaluate(element, new EvaluationOptions { OutputFormat = OutputFormat.List });

        result.IsValid.Should().BeTrue(
            $"{name} is one of the three blueprints the walkthrough suite actually executes and MUST " +
            $"validate against the schema an agent is told to author against; errors: {Describe(result)}");
    }

    public static IEnumerable<object[]> RepositoryBlueprintFiles()
    {
        var root = RepoRoot();

        foreach (var file in Directory.GetFiles(Path.Combine(root, "blueprints", "templates"), "*.json"))
        {
            yield return [Path.GetRelativePath(root, file)];
        }

        foreach (var file in Directory.GetFiles(
            Path.Combine(root, "blueprints", "examples"), "*.json", SearchOption.AllDirectories))
        {
            yield return [Path.GetRelativePath(root, file)];
        }
    }

    /// <summary>
    /// Regression guard: every blueprint file under <c>blueprints/templates</c> and
    /// <c>blueprints/examples</c> that validated against the OLD (pre-#1609) schema must still
    /// validate against the updated one. The update only ADDS optional properties, $defs, and enum
    /// values, and widens the handful of newly-typed optional properties to also accept an explicit
    /// JSON <c>null</c> (found authored that way in <c>blueprint-publishing-v1.json</c>'s
    /// <c>rejectionConfig</c>) — it narrows nothing that existed before, so a prior pass must still be
    /// a pass. Any failure here on a non-excluded file is a real regression to fix.
    /// </summary>
    [Theory]
    [MemberData(nameof(RepositoryBlueprintFiles))]
    public void RepositoryBlueprint_StillValidatesAgainstUpdatedSchema(string relativePath)
    {
        var fullPath = Path.Combine(RepoRoot(), relativePath);
        var raw = File.ReadAllText(fullPath);

        if (TemplateEngineMarkers.Any(raw.Contains))
        {
            // A pre-render template-engine document (see TemplateEngineMarkers) — never a blueprint,
            // never valid against this schema, old or new. Not this gate's concern.
            return;
        }

        var blueprintJson = UnwrapTemplate(raw);
        if (blueprintJson is null)
        {
            // "template" key present but not an object — not a blueprint-carrying file.
            return;
        }

        var element = JsonSerializer.Deserialize<JsonElement>(blueprintJson);
        var result = Schema.Evaluate(element, new EvaluationOptions { OutputFormat = OutputFormat.List });

        result.IsValid.Should().BeTrue(
            $"{relativePath} must not regress against the updated schema; errors: {Describe(result)}");
    }

    private static string Describe(EvaluationResults result) =>
        string.Join(" | ", result.Details
            .Where(d => !d.IsValid && d.Errors is { Count: > 0 })
            .SelectMany(d => d.Errors!.Select(e => $"{d.InstanceLocation}: {e.Key}={e.Value}")));

    /// <summary>
    /// Mirrors <c>SorchaResources.UnwrapTemplate</c> (Sorcha.McpServer) exactly, but detects wrapping
    /// dynamically rather than assuming it from directory (<c>blueprints/examples/healthcare/
    /// medical-equipment-refurb.json</c> is wrapped despite living under <c>examples/</c>):
    /// walkthrough-catalog and blueprint-catalog files come wrapped
    /// (<c>{"template": {...}, "category": ..., "tags": [...], ...}</c>) or flat (the blueprint
    /// object at the top level). Returns null when a "template" key is present but not an object.
    /// </summary>
    private static string? UnwrapTemplate(string raw)
    {
        using var doc = JsonDocument.Parse(raw);
        if (doc.RootElement.ValueKind != JsonValueKind.Object)
        {
            return raw;
        }

        if (!doc.RootElement.TryGetProperty("template", out var template))
        {
            return raw;
        }

        return template.ValueKind == JsonValueKind.Object
            ? JsonSerializer.Serialize(template)
            : null;
    }

    private static string ReadSchemaFile() =>
        File.ReadAllText(Path.Combine(RepoRoot(), "src", "Common", "blueprint.schema.json"));

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Sorcha.sln")))
        {
            dir = dir.Parent;
        }

        dir.Should().NotBeNull("the tests must be able to locate the repo root");
        return dir!.FullName;
    }
}
