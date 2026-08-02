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

    /// <summary>
    /// Issue #1303 — three keywords the code genuinely consumes were absent from both declared sets
    /// and reached "behavioural" only through the fail-safe, so a reader could not tell "deliberately
    /// behavioural" from "nobody thought about it".
    ///
    /// <para>This asserts DECLARED MEMBERSHIP rather than <c>IsBehavioural(...)</c> on purpose. Every
    /// <c>x-</c> keyword already returns true from <c>IsBehavioural</c> via the fail-safe, so an
    /// <c>IsBehavioural</c> assertion here would pass whether or not the keyword had ever been
    /// classified — a test that cannot fail for the reason it exists.</para>
    /// </summary>
    [Theory]
    [InlineData("x-rule")]
    [InlineData("x-claim-source")]
    [InlineData("x-sorcha-disclosure")]
    public void BehaviouralKeywords_DeclaresKeywordsTheCodeConsumes(string keyword)
    {
        FormKeywordClassifier.BehaviouralKeywords.Should().Contain(keyword,
            "a keyword the engine acts on must be classified by declaration, not by the fail-safe");
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
            // #1303 — same story for these three: each was already behavioural via the fail-safe, so
            // declaring them changes no hash and no gate behaviour. See the source comments for why
            // x-rule is behavioural despite governing *display* (it can hide a still-required field).
            "x-rule", "x-claim-source", "x-sorcha-disclosure",
        });
    }

    [Fact]
    public void IsExtensionKeyword_DistinguishesXPrefix()
    {
        FormKeywordClassifier.IsExtensionKeyword("x-file").Should().BeTrue();
        FormKeywordClassifier.IsExtensionKeyword("type").Should().BeFalse();
    }
}
