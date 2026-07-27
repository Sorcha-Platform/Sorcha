// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
using Sorcha.Agent.Decision.Checks;

namespace Sorcha.Agent.Tests.Decision.Checks;

public class ScoredQuestionnaireCheckTests
{
    private static ScoredQuestionnaireCheck Build() => new(
        "cyberScore",
        new Dictionary<string, IReadOnlyDictionary<string, int>>
        {
            ["/passwordStorage"] = new Dictionary<string, int>
            {
                ["A password manager"] = 3,
                ["Saved in my browser"] = 2,
                ["A notebook by the desk"] = 1,
                ["The same one everywhere, and hope"] = 0
            }
        },
        new Dictionary<string, IReadOnlyList<ScoreRange>>
        {
            ["/sharedPasswordCount"] =
            [
                new ScoreRange(0, 3), new ScoreRange(2, 2), new ScoreRange(5, 1), new ScoreRange(null, 0)
            ]
        });

    [Fact]
    public async Task EvaluateAsync_TopAnswers_ScoresMaximum()
    {
        var result = await Build().EvaluateAsync(CheckTestSupport.Payload("""
            { "passwordStorage": "A password manager", "sharedPasswordCount": 0 }
            """), default);

        result.Numeric.Should().Be(6);
    }

    [Fact]
    public async Task EvaluateAsync_UnrecognisedAnswer_ScoresZeroForThatQuestion()
    {
        var result = await Build().EvaluateAsync(CheckTestSupport.Payload("""
            { "passwordStorage": "Tattooed on my arm", "sharedPasswordCount": 0 }
            """), default);

        result.Numeric.Should().Be(3);
    }

    [Fact]
    public async Task EvaluateAsync_MissingField_ScoresZeroForThatQuestion()
    {
        var result = await Build().EvaluateAsync(CheckTestSupport.Payload("""
            { "sharedPasswordCount": 0 }
            """), default);

        result.Numeric.Should().Be(3);
    }

    [Theory]
    [InlineData(0, 3)]
    [InlineData(1, 2)]
    [InlineData(2, 2)]
    [InlineData(3, 1)]
    [InlineData(5, 1)]
    [InlineData(6, 0)]
    [InlineData(99, 0)]
    public async Task EvaluateAsync_RangeBoundaries_ScoreInclusively(int submitted, int expected)
    {
        var result = await Build().EvaluateAsync(CheckTestSupport.Payload($$"""
            { "sharedPasswordCount": {{submitted}} }
            """), default);

        result.Numeric.Should().Be(expected);
    }

    [Fact]
    public async Task EvaluateAsync_Always_ReportsBreakdownInDetail()
    {
        var result = await Build().EvaluateAsync(CheckTestSupport.Payload("""
            { "passwordStorage": "Saved in my browser", "sharedPasswordCount": 4 }
            """), default);

        result.Numeric.Should().Be(3);
        result.Detail.Should().Contain("/passwordStorage=2");
        result.Detail.Should().Contain("/sharedPasswordCount=1");
    }

    // Catch-all here deliberately carries non-zero points (9), so a config that leaks a
    // "could not score" answer into the catch-all would score 9 instead of 0 — the old
    // implementation returned the catch-all's Points for exactly this shape and would fail here.
    private static ScoredQuestionnaireCheck BuildWithNonZeroCatchAll() => new(
        "cyberScore",
        new Dictionary<string, IReadOnlyDictionary<string, int>>(),
        new Dictionary<string, IReadOnlyList<ScoreRange>>
        {
            ["/sharedPasswordCount"] = [new ScoreRange(2, 1), new ScoreRange(null, 9)]
        });

    [Fact]
    public async Task EvaluateAsync_RangeFieldMissing_ScoresZero_NotNonZeroCatchAll()
    {
        var result = await BuildWithNonZeroCatchAll().EvaluateAsync(CheckTestSupport.Payload("""
            { }
            """), default);

        result.Numeric.Should().Be(0, "an absent range field must score 0, never the catch-all band");
    }

    [Fact]
    public async Task EvaluateAsync_RangeFieldNonNumeric_ScoresZero_NotNonZeroCatchAll()
    {
        var result = await BuildWithNonZeroCatchAll().EvaluateAsync(CheckTestSupport.Payload("""
            { "sharedPasswordCount": "lots" }
            """), default);

        result.Numeric.Should().Be(0, "a non-numeric range value must score 0, never the catch-all band");
    }

    [Fact]
    public async Task EvaluateAsync_RangeFieldWithinBands_StillUsesCatchAllForOutOfRange()
    {
        // Contrast case: a genuinely numeric answer beyond every declared band IS a real answer,
        // and must still land in the catch-all — only "no answer" is forced to 0.
        var result = await BuildWithNonZeroCatchAll().EvaluateAsync(CheckTestSupport.Payload("""
            { "sharedPasswordCount": 99 }
            """), default);

        result.Numeric.Should().Be(9);
    }

    [Fact]
    public async Task EvaluateAsync_FractionalValueRoundsDownWithinBand_ScoresThatBand()
    {
        // 2.4 rounds to 2, which stays within the <=2 band (points 2) rather than crossing into <=5.
        var result = await Build().EvaluateAsync(CheckTestSupport.Payload("""
            { "sharedPasswordCount": 2.4 }
            """), default);

        result.Numeric.Should().Be(2);
    }

    [Fact]
    public async Task EvaluateAsync_FractionalValueRoundsUpAcrossBoundary_ScoresNextBand()
    {
        // 2.6 rounds to 3, crossing the <=2 boundary into the <=5 band (points 1).
        var result = await Build().EvaluateAsync(CheckTestSupport.Payload("""
            { "sharedPasswordCount": 2.6 }
            """), default);

        result.Numeric.Should().Be(1);
    }

    [Fact]
    public async Task EvaluateAsync_NegativeValue_FoldsIntoLowestBand()
    {
        var result = await Build().EvaluateAsync(CheckTestSupport.Payload("""
            { "sharedPasswordCount": -1 }
            """), default);

        result.Numeric.Should().Be(3, "negative counts fold into the first (lowest-Max) band");
    }
}
