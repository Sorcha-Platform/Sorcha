// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.Validator.Service.Services;

namespace Sorcha.Validator.Service.Tests.Services;

/// <summary>
/// Feature 137 (C5): the authoritative metadata projection in DocketBuildTriggerService must carry
/// the Blueprint Service's resolved next action (submission metadata key <c>nextActionId</c>) onto
/// <c>TransactionMetaData.NextActionId</c>, so a cross-node InstanceMirrorReconstructor can seed a
/// mirror's CurrentActionIds. These tests pin the parse/key contract.
/// </summary>
public class DocketBuildTriggerNextActionIdTests
{
    [Fact]
    public void ResolveNextActionId_WithValidValue_ReturnsParsedId()
    {
        var metadata = new Dictionary<string, string> { ["nextActionId"] = "2" };

        var result = DocketBuildTriggerService.ResolveNextActionId(metadata);

        result.Should().Be(2u);
    }

    [Fact]
    public void ResolveNextActionId_KeyAbsent_ReturnsNull()
    {
        var metadata = new Dictionary<string, string> { ["instanceId"] = "abc" };

        var result = DocketBuildTriggerService.ResolveNextActionId(metadata);

        result.Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-number")]
    [InlineData("-1")]
    public void ResolveNextActionId_Unparseable_ReturnsNull(string raw)
    {
        var metadata = new Dictionary<string, string> { ["nextActionId"] = raw };

        var result = DocketBuildTriggerService.ResolveNextActionId(metadata);

        result.Should().BeNull();
    }
}
