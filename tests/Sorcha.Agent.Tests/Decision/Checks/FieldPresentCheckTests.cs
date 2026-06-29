// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
using Sorcha.Agent.Decision.Checks;

namespace Sorcha.Agent.Tests.Decision.Checks;

public class FieldPresentCheckTests
{
    private static readonly FieldPresentCheck PhotoCheck = new("photoPresent", "/portrait/tokenImageBase64");

    [Fact]
    public async Task EvaluateAsync_NestedFieldPresent_ReturnsTrue()
    {
        var result = await PhotoCheck.EvaluateAsync(
            CheckTestSupport.Payload("""{ "portrait": { "tokenImageBase64": "/9j/4AAQSkZJRg==" } }"""), default);

        result.Name.Should().Be("photoPresent");
        result.Value.Should().BeTrue();
    }

    [Fact]
    public async Task EvaluateAsync_FieldMissing_ReturnsFalse()
    {
        var result = await PhotoCheck.EvaluateAsync(CheckTestSupport.Payload("""{ "name": "Alice" }"""), default);
        result.Value.Should().BeFalse();
    }

    [Fact]
    public async Task EvaluateAsync_FieldPresentButEmptyString_ReturnsFalse()
    {
        var result = await PhotoCheck.EvaluateAsync(
            CheckTestSupport.Payload("""{ "portrait": { "tokenImageBase64": "" } }"""), default);

        result.Value.Should().BeFalse();
    }
}
