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
}
