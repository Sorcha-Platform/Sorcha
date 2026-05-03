// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.ComponentModel;
using System.Reflection;
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
