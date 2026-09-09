// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Sorcha.Blueprint.Models;
using Sorcha.Blueprint.Models.Credentials;

namespace Sorcha.Blueprint.Models.Tests;

/// <summary>
/// Pins the wire values of the blueprint AUTHORING VOCABULARY — the enum values that appear in
/// authored blueprint JSON and in the schema served as <c>sorcha://schema/blueprint</c>.
/// </summary>
/// <remarks>
/// <para>
/// These are a published contract in three directions at once: shipped blueprints under
/// <c>walkthroughs/</c> are authored with them, the served schema advertises them to agents as the
/// legal values, and they are serialised into the canonical blueprint definition whose digest is a
/// publication id (CLAUDE.md pattern 22). Changing one is not a rename — it re-identifies
/// definitions and invalidates what the schema tells every agent to write.
/// </para>
/// <para>
/// They are therefore pinned with <c>[JsonStringEnumMemberName]</c>, which overrides any naming
/// policy — the same mechanism <see cref="CredentialFormat"/> uses to hold <c>"sd-jwt-vc"</c> and
/// <c>"mso_mdoc"</c> (underscore, per ISO 18013-5) against a kebab-case converter. Without the pins
/// these would have silently become <c>"text-line"</c> / <c>"vertical-layout"</c> when the
/// Blueprint Service adopted the shared SorchaJson wire format, while the schema went on
/// advertising the old spelling.
/// </para>
/// <para>
/// The expected values below are the values the platform emitted BEFORE that change. If a test here
/// fails, the wire has moved: fix the enum, do not update the expectation.
/// </para>
/// </remarks>
public class BlueprintVocabularyWireValueTests
{
    // Deliberately built here rather than taken from Sorcha.Serialization: this project must pin the
    // values against a kebab-case policy WITHOUT depending on the serialisation library, so the test
    // keeps working if that library's defaults are ever retuned.
    private static readonly JsonSerializerOptions KebabPolicy = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.KebabCaseLower) }
    };

    private static string Write<T>(T value) => JsonSerializer.Serialize(value, KebabPolicy).Trim('"');

    [Theory]
    [InlineData(ControlTypes.TextLine, "TextLine")]
    [InlineData(ControlTypes.TextArea, "TextArea")]
    [InlineData(ControlTypes.DateTime, "DateTime")]
    [InlineData(ControlTypes.PostcodeLookup, "PostcodeLookup")]
    public void ControlTypes_HoldTheirAuthoredSpelling(ControlTypes value, string expected)
        => Write(value).Should().Be(expected,
            "walkthroughs author these literally and blueprint.schema.json advertises them");

    [Theory]
    [InlineData(LayoutTypes.VerticalLayout, "VerticalLayout")]
    [InlineData(LayoutTypes.HorizontalLayout, "HorizontalLayout")]
    public void LayoutTypes_HoldTheirAuthoredSpelling(LayoutTypes value, string expected)
        => Write(value).Should().Be(expected);

    [Theory]
    [InlineData(RuleEffect.SHOW, "SHOW")]
    [InlineData(RuleEffect.HIDE, "HIDE")]
    [InlineData(RuleEffect.ENABLE, "ENABLE")]
    [InlineData(RuleEffect.DISABLE, "DISABLE")]
    public void RuleEffect_StaysUpperCase(RuleEffect value, string expected)
        => Write(value).Should().Be(expected, "a kebab policy would have lower-cased these to 'show'");

    [Theory]
    [InlineData(RevocationCheckPolicy.FailClosed, "FailClosed")]
    [InlineData(RevocationCheckPolicy.FailOpen, "FailOpen")]
    public void RevocationCheckPolicy_HoldsItsAuthoredSpelling(RevocationCheckPolicy value, string expected)
        => Write(value).Should().Be(expected);

    [Fact]
    public void CredentialFormat_KeepsItsStandardsValues_IncludingTheUnderscoreOne()
    {
        Write(CredentialFormat.SdJwtVc).Should().Be("sd-jwt-vc", "SD-JWT VC profile");
        Write(CredentialFormat.MsoMdoc).Should().Be("mso_mdoc",
            "ISO 18013-5 / OpenID4VP spell this with an UNDERSCORE — proof that kebab is not "
            + "universally correct and that per-member pinning, not the naming policy, is what "
            + "holds a standards value in place");
    }

    [Fact]
    public void EveryVocabularyEnumMember_IsPinned()
    {
        // Reflective, so a member added later cannot quietly inherit the naming policy. Both
        // defaults are wrong in different directions: unpinned silently moves the wire, and a wrong
        // pin is a rename.
        var vocabulary = new[]
        {
            typeof(ControlTypes), typeof(LayoutTypes), typeof(RuleEffect),
            typeof(RevocationCheckPolicy), typeof(UsagePolicy),
            typeof(TargetAudience), typeof(PresentationSource), typeof(CredentialFormat),
        };

        var unpinned = new List<string>();
        foreach (var t in vocabulary)
        {
            foreach (var f in t.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
            {
                if (f.GetCustomAttributes(typeof(JsonStringEnumMemberNameAttribute), false).Length == 0)
                {
                    unpinned.Add($"{t.Name}.{f.Name}");
                }
            }
        }

        unpinned.Should().BeEmpty(
            "every member of the authored blueprint vocabulary must pin its wire value — an unpinned "
            + "member takes whatever the ambient naming policy says, which is how a published "
            + "contract moves without anyone deciding to move it");
    }
}
