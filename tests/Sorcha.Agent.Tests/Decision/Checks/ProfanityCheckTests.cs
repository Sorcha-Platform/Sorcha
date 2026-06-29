// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
using Sorcha.Agent.Decision.Checks;

namespace Sorcha.Agent.Tests.Decision.Checks;

public class ProfanityCheckTests
{
    private static readonly string[] Wordlist = ["bugger", "arse"];

    private static ProfanityCheck Check(params string[] fields) => new("profane", fields, Wordlist);

    [Fact]
    public async Task EvaluateAsync_CleanDetails_ReturnsFalse()
    {
        var result = await Check("/name/fullName", "/address").EvaluateAsync(
            CheckTestSupport.Payload("""{ "name": { "fullName": "Alice Smith" }, "address": { "town": "Bristol" } }"""), default);

        result.Value.Should().BeFalse();
    }

    [Fact]
    public async Task EvaluateAsync_ProfaneName_ReturnsTrueWithMatchedTerm()
    {
        var result = await Check("/name/fullName", "/address").EvaluateAsync(
            CheckTestSupport.Payload("""{ "name": { "fullName": "Bugger Off" }, "address": { "town": "Bristol" } }"""), default);

        result.Value.Should().BeTrue();
        result.Detail.Should().Be("bugger");
    }

    [Fact]
    public async Task EvaluateAsync_ProfaneInNestedAddress_FlattensAndMatches()
    {
        var result = await Check("/address").EvaluateAsync(
            CheckTestSupport.Payload("""{ "address": { "line1": "12 Arse Lane", "town": "Bristol" } }"""), default);

        result.Value.Should().BeTrue();
    }

    [Fact]
    public async Task EvaluateAsync_SubstringOnly_DoesNotFalsePositive()
    {
        // "Scunthorpe problem": a banned term embedded in a legitimate word must not match.
        var result = new ProfanityCheck("profane", ["/name/fullName"], ["scun"]).EvaluateAsync(
            CheckTestSupport.Payload("""{ "name": { "fullName": "Resident of Scunthorpe" } }"""), default);

        (await result).Value.Should().BeFalse();
    }
}
