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
    public void ChecksConfig_EveryAnswerKey_ExistsInTheBlueprintEnum()
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
                .Select(v => v!.GetValue<string>()).ToHashSet(StringComparer.Ordinal);

            foreach (var answer in table.Keys)
                enumValues.Should().Contain(answer,
                    $"scoring key '{answer}' for {pointer} must match a blueprint enum value verbatim");
        }
    }
}
