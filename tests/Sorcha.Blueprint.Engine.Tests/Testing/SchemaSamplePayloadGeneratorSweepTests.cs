// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Sorcha.Blueprint.Engine.Testing;
using BlueprintModel = Sorcha.Blueprint.Models.Blueprint;
using ActionModel = Sorcha.Blueprint.Models.Action;

namespace Sorcha.Blueprint.Engine.Tests.Testing;

/// <summary>
/// #1724 acceptance test: runs <see cref="SchemaSamplePayloadGenerator"/> over every action of
/// every blueprint the walkthrough suite executes, plus the shipped templates/examples corpus, and
/// asserts every action whose schemas contain no file / holder-key / <c>$ref</c> construct gets a
/// valid generated payload. This is the real check that the generator holds up against the actual
/// blueprint corpus, not just hand-written fixtures.
/// </summary>
public class SchemaSamplePayloadGeneratorSweepTests
{
    /// <summary>Directories the sweep reads from — mirrors the walkthrough suite and the shipped corpus.</summary>
    private static readonly string[] SearchDirs = ["walkthroughs", "blueprints"];

    private const int MinimumActionsWithSchemas = 10;

    private sealed record ActionUnderTest(string RelFile, string ActionTitle, int ActionId, ActionModel Action);

    [Fact]
    public void TheSweepActuallyFindsActionsWithSchemas()
    {
        var repoRoot = FindRepoRoot(AppContext.BaseDirectory);
        var actions = ActionsWithDeclaredSchemas(repoRoot).ToList();

        actions.Should().HaveCountGreaterThanOrEqualTo(MinimumActionsWithSchemas,
            "the rest of this class is only meaningful if it actually opens real action schemas");
    }

    [Fact]
    public void EveryActionWithoutFileOrRefConstructs_GetsAValidGeneratedPayload()
    {
        var repoRoot = FindRepoRoot(AppContext.BaseDirectory);
        var generator = new SchemaSamplePayloadGenerator();

        var failures = new List<string>();
        var excludedForFileOrRef = 0;
        var checked_ = 0;

        foreach (var (relFile, actionTitle, actionId, action) in ActionsWithDeclaredSchemas(repoRoot))
        {
            var schemaText = string.Join("\n", (action.DataSchemas ?? [])
                .Select(d => d.RootElement.GetRawText()));

            if (ContainsFileOrRefConstructs(schemaText))
            {
                excludedForFileOrRef++;
                continue;
            }

            checked_++;
            var result = generator.Generate(action, seed: 1);
            if (!result.IsValid)
            {
                var reasons = string.Join("; ", result.Unsatisfied.Select(u => $"{u.Path}: {u.Reason}"));
                failures.Add($"{relFile} :: action {actionId} \"{actionTitle}\" -> {reasons}");
            }
        }

        checked_.Should().BeGreaterThan(0, "the exclusion filter must not have excluded everything");

        failures.Should().BeEmpty(
            $"{failures.Count} of {checked_} eligible actions (excluding {excludedForFileOrRef} with "
            + "file/holder-key/$ref constructs) did not get a valid generated payload:\n"
            + string.Join("\n", failures));
    }

    private static bool ContainsFileOrRefConstructs(string schemaText) =>
        schemaText.Contains("\"$ref\"", StringComparison.Ordinal)
        || schemaText.Contains("\"x-holder-key\"", StringComparison.Ordinal)
        || schemaText.Contains("\"x-file\"", StringComparison.Ordinal)
        || schemaText.Contains("file-reference", StringComparison.Ordinal);

    /// <summary>Every action, across the corpus, that declares at least one entry on <c>dataSchemas</c>.</summary>
    private static IEnumerable<(string RelFile, string ActionTitle, int ActionId, ActionModel Action)>
        ActionsWithDeclaredSchemas(string repoRoot)
    {
        foreach (var file in BlueprintFiles(repoRoot))
        {
            var relFile = Path.GetRelativePath(repoRoot, file).Replace('\\', '/');

            foreach (var definitionText in BlueprintDefinitionTexts(file))
            {
                BlueprintModel? blueprint;
                try
                {
                    blueprint = JsonSerializer.Deserialize<BlueprintModel>(definitionText);
                }
                catch
                {
                    continue; // not a parseable blueprint shape — irrelevant to this sweep.
                }

                if (blueprint is null)
                {
                    continue;
                }

                foreach (var action in blueprint.Actions)
                {
                    if (action.DataSchemas is not null && action.DataSchemas.Any())
                    {
                        yield return (relFile, action.Title ?? string.Empty, action.Id, action);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Both shapes a blueprint file can carry: a root-level blueprint, or one nested under
    /// <c>template</c> (<c>BlueprintTemplate</c>) — mirrors <c>BlueprintCorpusFreshnessTests</c>.
    /// </summary>
    private static IEnumerable<string> BlueprintDefinitionTexts(string file)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(File.ReadAllText(file));
        }
        catch
        {
            yield break;
        }

        if (root is not JsonObject obj)
        {
            yield break;
        }

        if (LooksLikeBlueprint(obj))
        {
            yield return obj.ToJsonString();
        }
        else if (obj["template"] is JsonObject template && LooksLikeBlueprint(template))
        {
            yield return template.ToJsonString();
        }
    }

    private static bool LooksLikeBlueprint(JsonObject obj) =>
        obj.ContainsKey("participants") && obj.ContainsKey("actions");

    private static IEnumerable<string> BlueprintFiles(string repoRoot)
    {
        foreach (var relDir in SearchDirs)
        {
            var dir = Path.Combine(repoRoot, relDir);
            if (!Directory.Exists(dir))
            {
                continue;
            }
            foreach (var file in Directory.EnumerateFiles(dir, "*.json", SearchOption.AllDirectories))
            {
                yield return file;
            }
        }
    }

    private static string FindRepoRoot(string start)
    {
        var dir = new DirectoryInfo(start);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Sorcha.sln")))
        {
            dir = dir.Parent;
        }
        dir.Should().NotBeNull("the tests must be able to locate the repo root");
        return dir!.FullName;
    }
}
