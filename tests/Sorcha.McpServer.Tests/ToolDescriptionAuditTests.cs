// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.ComponentModel;
using System.Reflection;
using System.Text.RegularExpressions;
using FluentAssertions;
using ModelContextProtocol.Server;

namespace Sorcha.McpServer.Tests;

/// <summary>
/// Spec 117 FR-017 — every MCP tool's <c>[Description("...")]</c> attribute MUST be at least
/// two sentences AND name at least one disambiguating situation. These tests reflect over
/// every tool class in the Sorcha.McpServer assembly and assert both.
/// </summary>
public class ToolDescriptionAuditTests
{
    /// <summary>
    /// Acceptable disambiguator phrases. Heuristic substring match (case-insensitive). The
    /// audit at T008 documented these forms; T034 codifies them. The list is permissive —
    /// reviewers may extend it when adding tools that introduce a new clear disambiguation
    /// idiom, but every entry must be a phrase that an AI agent would read as "use this when X".
    /// </summary>
    private static readonly string[] AcceptedDisambiguators =
    {
        "call this when",
        "use when",
        "use this when",
        "prefer this when",
        "prefer ... over",
        "prefer this over",
        "not when",
        "versus",
        "instead of",
        "instead when",
        "rather than",
        "as opposed to",
        "pick this for",
        "call after",
        "call before",
        "before ... rather than",
    };

    public static IEnumerable<object[]> EveryToolMethod()
    {
        var assembly = Assembly.Load("Sorcha.McpServer");
        foreach (var type in assembly.GetTypes())
        {
            if (type.GetCustomAttribute<McpServerToolTypeAttribute>() is null)
            {
                continue;
            }
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance))
            {
                if (method.GetCustomAttribute<McpServerToolAttribute>() is null)
                {
                    continue;
                }
                yield return new object[] { type.FullName!, method.Name };
            }
        }
    }

    [Theory]
    [MemberData(nameof(EveryToolMethod))]
    public void EveryTool_DescriptionIsAtLeastTwoSentences(string typeName, string methodName)
    {
        var description = ResolveToolDescription(typeName, methodName);
        description.Should().NotBeNullOrWhiteSpace($"{typeName}.{methodName} has no [Description] attribute");

        var sentenceCount = CountSentences(description!);
        sentenceCount.Should().BeGreaterThanOrEqualTo(2,
            $"FR-017 — {typeName}.{methodName} description must be ≥ 2 sentences (got {sentenceCount})");
    }

    [Theory]
    [MemberData(nameof(EveryToolMethod))]
    public void EveryTool_DescriptionMentionsDisambiguatingSituation(string typeName, string methodName)
    {
        var description = ResolveToolDescription(typeName, methodName);
        description.Should().NotBeNullOrWhiteSpace($"{typeName}.{methodName} has no [Description] attribute");

        var lower = description!.ToLowerInvariant();
        var hit = AcceptedDisambiguators.Any(phrase => lower.Contains(NormalisePhrase(phrase)));
        hit.Should().BeTrue(
            $"FR-017 — {typeName}.{methodName} description must name a disambiguating situation. " +
            $"Allowed phrases: {string.Join(", ", AcceptedDisambiguators)}");
    }

    private static string? ResolveToolDescription(string typeName, string methodName)
    {
        var type = Assembly.Load("Sorcha.McpServer").GetType(typeName);
        var method = type?.GetMethod(methodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance);
        return method?.GetCustomAttribute<DescriptionAttribute>()?.Description
            ?? type?.GetCustomAttribute<DescriptionAttribute>()?.Description;
    }

    /// <summary>
    /// The MCP name a tool method is actually registered under — from the compiled
    /// <c>[McpServerTool(Name = ...)]</c> attribute instance, NOT a textual match over source. Most
    /// tools spell the name as a string literal, but ~30 declare it via a
    /// <c>private const string ToolName = "sorcha_x";</c> and reference the constant instead. A
    /// regex over source text cannot see through that; reflection over the compiled attribute
    /// resolves the constant automatically because the compiler already folded it in.
    /// </summary>
    private static string? ResolveToolName(string typeName, string methodName)
    {
        var type = Assembly.Load("Sorcha.McpServer").GetType(typeName);
        var method = type?.GetMethod(methodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance);
        return method?.GetCustomAttribute<McpServerToolAttribute>()?.Name;
    }

    /// <summary>
    /// Every name actually registered on the MCP surface (mirrors
    /// <c>ManifestIntegrityTests.ServedToolNames</c> — reflection over <see cref="EveryToolMethod"/>
    /// rather than a duplicate type scan).
    /// </summary>
    private static IReadOnlySet<string> RegisteredToolNames()
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in EveryToolMethod())
        {
            if (ResolveToolName((string)entry[0], (string)entry[1]) is { Length: > 0 } name)
            {
                names.Add(name);
            }
        }
        return names;
    }

    // Tool names are always lowercase snake_case starting "sorcha_" — matches the convention every
    // [McpServerTool(Name = ...)] in this assembly follows. Bounded on both sides so a match can't
    // swallow trailing punctuation or bleed into an adjacent word.
    private static readonly Regex ToolNameReferencePattern =
        new(@"\bsorcha_[a-z0-9]+(?:_[a-z0-9]+)*\b", RegexOptions.Compiled);

    /// <summary>
    /// Spec 117 follow-up (Task 4 review) — a tool's <c>[Description]</c> must never point an agent
    /// at a tool name that does not exist on the registered surface. This is the exact shape of
    /// defect that shipped earlier in this workstream: <c>sorcha_action_submit</c>'s description
    /// told agents to call <c>sorcha_inbox_list</c> for the ids it needed, while that route had
    /// never actually been mapped. Nothing checked the cross-reference; this test is that check.
    /// </summary>
    [Fact]
    public void EveryToolNameReferencedInADescription_ExistsInTheRegisteredSurface()
    {
        var registered = RegisteredToolNames();
        registered.Should().NotBeEmpty(
            "the registered-tool-name resolver must find at least one tool, or this test is vacuous");

        var dangling = new List<string>();

        foreach (var entry in EveryToolMethod())
        {
            var typeName = (string)entry[0];
            var methodName = (string)entry[1];

            var description = ResolveToolDescription(typeName, methodName);
            if (string.IsNullOrWhiteSpace(description))
            {
                continue;
            }

            var selfName = ResolveToolName(typeName, methodName);

            foreach (var referenced in ToolNameReferencePattern.Matches(description)
                         .Select(m => m.Value).Distinct(StringComparer.Ordinal))
            {
                // A tool naming ITSELF (e.g. restating its own name for clarity) is not a
                // cross-reference and is not what this test guards against.
                if (string.Equals(referenced, selfName, StringComparison.Ordinal))
                {
                    continue;
                }

                if (!registered.Contains(referenced))
                {
                    dangling.Add($"{typeName}.{methodName} description references '{referenced}', which is not a registered tool");
                }
            }
        }

        dangling.Should().BeEmpty(
            "every sorcha_* name mentioned in a [Description] must exist on the registered tool " +
            $"surface. Dangling references found: {string.Join("; ", dangling)}");
    }

    private static int CountSentences(string text)
    {
        // Heuristic: count terminating punctuation followed by either a space or end-of-string.
        var count = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] is '.' or '!' or '?')
            {
                if (i == text.Length - 1 || char.IsWhiteSpace(text[i + 1]))
                {
                    count++;
                }
            }
        }
        return count;
    }

    private static string NormalisePhrase(string phrase) =>
        phrase.Replace(" ... ", " ", StringComparison.Ordinal);
}
