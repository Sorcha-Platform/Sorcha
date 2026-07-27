// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Logic;
using Sorcha.Agent.Decision.Checks;

namespace Sorcha.Agent.Tests.Decision.Checks;

/// <summary>
/// Pins the four band transitions against the REAL demos/AIAS/agent/cyber.rules.json, so
/// retuning the scoring table cannot silently move a boundary. Feature AIAS M2.
/// </summary>
public class CyberBandBoundaryTests
{
    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !Directory.Exists(Path.Combine(dir, "demos")))
            dir = Path.GetDirectoryName(dir);
        return dir ?? throw new DirectoryNotFoundException("Could not locate repo root from test output directory");
    }

    private static readonly string RulesPath =
        Path.Combine(RepoRoot(), "demos", "AIAS", "agent", "cyber.rules.json");

    private static readonly string ChecksPath =
        Path.Combine(RepoRoot(), "demos", "AIAS", "agent", "cyber.checks.json");

    private static string? LevelFor(int score, bool portraitPresent = true)
    {
        var rules = JsonNode.Parse(File.ReadAllText(RulesPath))!.AsArray();
        var data = new JsonObject
        {
            ["checks"] = new JsonObject
            {
                ["portraitPresent"] = portraitPresent,
                ["cyberScore"] = score
            }
        };

        foreach (var entry in rules)
        {
            var condition = entry!["condition"]!;
            var rule = JsonSerializer.Deserialize<Rule>(condition.ToJsonString())!;
            var result = rule.Apply(data);
            if (result is JsonValue v && v.TryGetValue(out bool b) && b)
                return entry["payload"]!["level"]?.GetValue<string>() ?? "REJECTED";
        }

        return null;
    }

    [Theory]
    [InlineData(0, "REJECTED")]
    [InlineData(11, "REJECTED")]
    [InlineData(12, "Bronze")]
    [InlineData(15, "Bronze")]
    [InlineData(16, "Silver")]
    [InlineData(20, "Silver")]
    [InlineData(21, "Gold")]
    [InlineData(23, "Gold")]
    [InlineData(24, "Platinum")]
    public void Rules_AtEveryBandBoundary_AwardTheLockedLevel(int score, string expected)
    {
        LevelFor(score).Should().Be(expected);
    }

    [Fact]
    public void Rules_NoPortrait_RejectBeforeScoringEvenOnAPerfectCard()
    {
        LevelFor(24, portraitPresent: false).Should().Be("REJECTED");
    }

    [Fact]
    public void ChecksConfig_TotalAvailablePoints_Is24()
    {
        var config = ChecksConfig.Load(ChecksPath);
        var scored = config.Checks.Single(c => c.Type == "scored-questionnaire");

        var answerMax = (scored.Answers ?? []).Sum(q => q.Value.Values.Max());
        var rangeMax = (scored.Ranges ?? []).Sum(q => q.Value.Max(r => r.Points));

        (answerMax + rangeMax).Should().Be(24);
    }

    [Fact]
    public void ChecksConfig_AnswerKeysForEachField_ExactlyMatchTheBlueprintEnum()
    {
        var config = ChecksConfig.Load(ChecksPath);
        var scored = config.Checks.Single(c => c.Type == "scored-questionnaire");

        var blueprint = JsonNode.Parse(File.ReadAllText(
            Path.Combine(RepoRoot(), "demos", "AIAS", "blueprints", "aias-cyber-level.template.json")))!;
        var properties = blueprint["template"]!["actions"]![0]!["dataSchemas"]![0]!["properties"]!.AsObject();

        foreach (var (pointer, table) in scored.Answers ?? [])
        {
            var field = pointer.TrimStart('/');
            var enumValues = properties[field]!["enum"]!.AsArray()
                .Select(v => v!.GetValue<string>()).ToArray();

            // Bidirectional by design: a config key missing from the blueprint enum is dead scoring,
            // but a blueprint enum value missing from the config is worse — it silently scores 0 for
            // any citizen who picks it, permanently capping their achievable total below 24.
            table.Keys.Should().BeEquivalentTo(enumValues,
                $"the '{pointer}' scoring table and the blueprint's '{field}' enum must be the exact same " +
                "set of strings — any enum value absent from the scoring table scores 0 silently, with no " +
                "error and no log, for every citizen who picks it");
        }
    }

    [Fact]
    public void ChecksConfig_EveryScoringPointer_NamesARealPropertyInTheBlueprintActionOneSchema()
    {
        var config = ChecksConfig.Load(ChecksPath);
        var scored = config.Checks.Single(c => c.Type == "scored-questionnaire");

        var blueprint = JsonNode.Parse(File.ReadAllText(
            Path.Combine(RepoRoot(), "demos", "AIAS", "blueprints", "aias-cyber-level.template.json")))!;
        var propertyNames = blueprint["template"]!["actions"]![0]!["dataSchemas"]![0]!["properties"]!
            .AsObject().Select(kv => kv.Key).ToHashSet(StringComparer.Ordinal);

        // Both halves of the scoring table (answers AND ranges) are pointers into the blueprint's
        // action-1 schema. A renamed or removed property on either side scores 0 forever for
        // everyone, with no error and no log — for `ranges` this silently caps the maximum below
        // 24, making Platinum unreachable and under-grading every citizen.
        var allPointers = (scored.Answers ?? []).Keys.Concat((scored.Ranges ?? []).Keys);

        foreach (var pointer in allPointers)
        {
            var field = pointer.TrimStart('/');
            propertyNames.Should().Contain(field,
                $"the scoring config pointer '{pointer}' must name a real property in the blueprint's " +
                "action-1 schema — a pointer to a renamed or missing property silently scores 0 for " +
                "every citizen, forever, with no error anywhere");
        }
    }

    [Fact]
    public void Rules_EveryActionName_MatchesTheBlueprintAction2TitleVerbatim()
    {
        var rules = JsonNode.Parse(File.ReadAllText(RulesPath))!.AsArray();

        var blueprint = JsonNode.Parse(File.ReadAllText(
            Path.Combine(RepoRoot(), "demos", "AIAS", "blueprints", "aias-cyber-level.template.json")))!;
        var action2 = blueprint["template"]!["actions"]!.AsArray()
            .Single(a => a!["id"]!.GetValue<int>() == 2);
        var expectedActionName = action2!["title"]!.GetValue<string>();

        foreach (var entry in rules)
        {
            var actionName = entry!["actionName"]!.GetValue<string>();
            var ruleIdentifier = entry["payload"]?["level"]?.GetValue<string>()
                ?? entry["payload"]?["reasonCode"]?.GetValue<string>()
                ?? "(unidentified rule)";

            actionName.Should().Be(expectedActionName,
                $"rule '{ruleIdentifier}' has actionName '{actionName}', which the agent matches against " +
                "action 2's blueprint title — a mismatch means this rule silently never fires");
        }
    }

    [Fact]
    public void Rules_EveryReasonCode_ExistsAsAKeyInTheBlueprintDecisionNoticeReasons()
    {
        var rules = JsonNode.Parse(File.ReadAllText(RulesPath))!.AsArray();

        var blueprint = JsonNode.Parse(File.ReadAllText(
            Path.Combine(RepoRoot(), "demos", "AIAS", "blueprints", "aias-cyber-level.template.json")))!;
        var action2 = blueprint["template"]!["actions"]!.AsArray()
            .Single(a => a!["id"]!.GetValue<int>() == 2);
        var noticeRoute = action2!["routes"]!.AsArray()
            .Single(r => r!["x-decision-notice"] is not null);
        var reasonKeys = noticeRoute!["x-decision-notice"]!["reasons"]!.AsObject()
            .Select(kv => kv.Key).ToHashSet(StringComparer.Ordinal);

        foreach (var entry in rules)
        {
            var reasonCode = entry!["payload"]!["reasonCode"]?.GetValue<string>();
            if (reasonCode is null)
                continue; // approved rules carry no reasonCode — nothing to check

            reasonKeys.Should().Contain(reasonCode,
                $"rule reasonCode '{reasonCode}' must be a key in the blueprint's x-decision-notice.reasons " +
                "map, or a rejected citizen silently gets the generic fallback message instead of the " +
                "on-brand one");
        }
    }
}
