// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Blueprint.Models.Forms;

namespace Sorcha.Blueprint.Engine.Tests;

/// <summary>
/// Unit tests for <see cref="FormKeywordClassifier"/> (Feature 142 / T005).
/// Verifies the presentational-vs-behavioural split and the fail-safe default.
/// </summary>
public class FormKeywordClassifierTests
{
    [Theory]
    [InlineData("x-pages")]
    [InlineData("x-sections")]
    [InlineData("x-width")]
    [InlineData("x-introduction")]
    [InlineData("x-review")]
    [InlineData("x-address-lookup")]
    [InlineData("x-persona")]
    public void IsPresentational_KnownPresentationalKeyword_ReturnsTrue(string keyword)
    {
        FormKeywordClassifier.IsPresentational(keyword).Should().BeTrue();
        FormKeywordClassifier.IsBehavioural(keyword).Should().BeFalse();
    }

    [Theory]
    [InlineData("x-file")]
    [InlineData("x-credential-offer")]
    // Issue #1302 — x-holder-key now has a consumer: it gates whether the carried delivery keys
    // must be captured before submission, so it is part of the executable definition rather than
    // an unknown keyword riding the fail-safe. The classification is unchanged (behavioural either
    // way), so this is a declaration, not a gate-behaviour change.
    [InlineData("x-holder-key")]
    [InlineData("x-device-key")]
    public void IsBehavioural_KnownBehaviouralKeyword_ReturnsTrue(string keyword)
    {
        FormKeywordClassifier.IsBehavioural(keyword).Should().BeTrue();
        FormKeywordClassifier.IsPresentational(keyword).Should().BeFalse();
    }

    [Theory]
    [InlineData("x-foo")]
    [InlineData("x-unknown-future-keyword")]
    public void IsBehavioural_UnknownExtensionKeyword_FailsSafeToBehavioural(string keyword)
    {
        FormKeywordClassifier.IsBehavioural(keyword).Should().BeTrue();
        FormKeywordClassifier.IsPresentational(keyword).Should().BeFalse();
    }

    [Theory]
    [InlineData("type")]
    [InlineData("properties")]
    [InlineData("required")]
    public void NonExtensionKey_IsNeitherPresentationalNorBehavioural(string key)
    {
        FormKeywordClassifier.IsPresentational(key).Should().BeFalse();
        FormKeywordClassifier.IsBehavioural(key).Should().BeFalse();
    }

    [Fact]
    public void KeywordSets_AreDisjoint()
    {
        FormKeywordClassifier.PresentationalKeywords
            .Should().NotIntersectWith(FormKeywordClassifier.BehaviouralKeywords);
    }

    [Fact]
    public void PresentationalKeywords_ContainsExactExpectedSet()
    {
        FormKeywordClassifier.PresentationalKeywords.Should().BeEquivalentTo(new[]
        {
            "x-pages", "x-sections", "x-width", "x-introduction",
            "x-review", "x-address-lookup", "x-persona",
        });
    }

    [Fact]
    public void BehaviouralKeywords_ContainsExactExpectedSet()
    {
        FormKeywordClassifier.BehaviouralKeywords.Should().BeEquivalentTo(new[]
        {
            // x-holder-key joined the set in #1302 when it gained a consumer. Its effective
            // classification is unchanged — it previously reached "behavioural" via the fail-safe —
            // so no executable-definition hash or rehearsal-gate behaviour changes here.
            "x-file", "x-credential-offer", "x-holder-key", "x-device-key",
        });
    }

    [Fact]
    public void IsExtensionKeyword_DistinguishesXPrefix()
    {
        FormKeywordClassifier.IsExtensionKeyword("x-file").Should().BeTrue();
        FormKeywordClassifier.IsExtensionKeyword("type").Should().BeFalse();
    }
}
