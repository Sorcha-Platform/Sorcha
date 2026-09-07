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

    /// <summary>
    /// The canonical lifecycle order, matching <c>ServerInstructions</c>, all three
    /// <c>SorchaPrompts</c>, and every lifecycle tool's own [Description].
    /// </summary>
    private static readonly string[] LifecycleOrder =
    {
        "sorcha_blueprint_create",
        "sorcha_register_create",
        "sorcha_blueprint_publish",
        "sorcha_instance_create",
        "sorcha_action_submit",
    };

    /// <summary>
    /// Task-8 review finding (Important 3): <c>RegisterCreateTool</c>'s own [Description] once
    /// read "create the register first, then design a blueprint... and only then start work on
    /// it with sorcha_instance_create" — a sequence that both omitted
    /// <c>sorcha_blueprint_publish</c> entirely and put register creation before blueprint
    /// authoring, disagreeing with <c>ServerInstructions</c>, all three <c>SorchaPrompts</c>,
    /// <c>BlueprintPublishTool</c> and <c>InstanceCreateTool</c>. Nothing checked that a
    /// description's own prose agreed with the lifecycle it was implicitly describing.
    /// <para>
    /// This is a deliberately narrow, generic guard, not a re-run of that specific bug: wherever
    /// two or more of the five canonical lifecycle tool names are named (other than
    /// self-referentially) inside one tool's own description, their first-occurrence order must
    /// not reverse relative to each other. It does NOT require every surface to name every step
    /// — a tool legitimately describing only its own immediate neighbours should not be forced
    /// to enumerate the whole lifecycle — and it cannot catch an implicit claim that names no
    /// tool at all (the original defect's "create the register first" clause never named
    /// <c>sorcha_register_create</c> literally). A test that also caught THAT would need to
    /// assert something like "any description mentioning the lifecycle's first and last tools
    /// must also mention the middle two", which is fragile to legitimate rewording and was
    /// judged to be over-fitting to today's prose rather than a durable contract.
    /// </para>
    /// </summary>
    [Fact]
    public void LifecycleToolNamesMentionedInADescription_AppearInCanonicalOrder()
    {
        var offenders = new List<string>();

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

            var mentioned = LifecycleOrder
                .Select((name, canonicalIndex) => (
                    name,
                    canonicalIndex,
                    firstIndex: description!.IndexOf(name, StringComparison.Ordinal)))
                .Where(t => t.firstIndex >= 0 && !string.Equals(t.name, selfName, StringComparison.Ordinal))
                .OrderBy(t => t.firstIndex)
                .ToList();

            for (var i = 1; i < mentioned.Count; i++)
            {
                if (mentioned[i].canonicalIndex < mentioned[i - 1].canonicalIndex)
                {
                    offenders.Add(
                        $"{typeName}.{methodName}: '{mentioned[i].name}' is mentioned before "
                        + $"'{mentioned[i - 1].name}' in its own description, reversing the "
                        + "canonical lifecycle order (blueprint_create -> register_create -> "
                        + "blueprint_publish -> instance_create -> action_submit)");
                }
            }
        }

        offenders.Should().BeEmpty(
            "a tool description must not claim a lifecycle order that disagrees with itself. "
            + string.Join("; ", offenders));
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
