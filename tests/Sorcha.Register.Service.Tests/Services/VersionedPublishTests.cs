// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.Blueprint.Models;
using Sorcha.Register.Service.Services;
using Xunit;

namespace Sorcha.Register.Service.Tests.Services;

public class VersionedPublishTests
{
    private readonly StructuralDiffService _diffService = new();

    [Fact]
    public void FirstPublish_ShouldBeVersion1_0()
    {
        var result = VersionCalculator.ComputeNextVersion(
            previousMajor: null,
            previousMinor: null,
            changeType: "structural");

        result.Major.Should().Be(1);
        result.Minor.Should().Be(0);
    }

    [Fact]
    public void DocumentationChange_ShouldIncrementMinor()
    {
        var result = VersionCalculator.ComputeNextVersion(
            previousMajor: 1,
            previousMinor: 2,
            changeType: "documentation");

        result.Major.Should().Be(1);
        result.Minor.Should().Be(3);
    }

    [Fact]
    public void StructuralChange_ShouldIncrementMajor_ResetMinor()
    {
        var result = VersionCalculator.ComputeNextVersion(
            previousMajor: 3,
            previousMinor: 5,
            changeType: "structural");

        result.Major.Should().Be(4);
        result.Minor.Should().Be(0);
    }

    [Fact]
    public void VersionMetadata_ShouldIncludeChangeType()
    {
        var metadata = VersionCalculator.BuildVersionMetadata(
            changeType: "structural",
            structuralHash: "abc123def456",
            previousMajor: 1,
            previousMinor: 2);

        metadata.Should().ContainKey("changeType");
        metadata["changeType"].Should().Be("structural");
        metadata.Should().ContainKey("structuralHash");
        metadata.Should().ContainKey("versionMajor");
        metadata["versionMajor"].Should().Be("2");
        metadata.Should().ContainKey("versionMinor");
        metadata["versionMinor"].Should().Be("0");
    }

    [Fact]
    public void VersionMetadata_FirstPublish_ShouldBeV1_0()
    {
        var metadata = VersionCalculator.BuildVersionMetadata(
            changeType: "structural",
            structuralHash: "firsthash",
            previousMajor: null,
            previousMinor: null);

        metadata["versionMajor"].Should().Be("1");
        metadata["versionMinor"].Should().Be("0");
    }

    [Fact]
    public void ClassifyChange_InstructionOnlyDiff_IsDocumentation()
    {
        var blueprintBase = """{"id":"bp1","title":"Test","actions":[],"participants":[]}""";
        var blueprintWithInstructions = """{"id":"bp1","title":"Test","instructions":{"overview":"Help"},"actions":[],"participants":[]}""";

        var hashBase = _diffService.ComputeStructuralHash(blueprintBase);
        var hashNew = _diffService.ComputeStructuralHash(blueprintWithInstructions);

        StructuralDiffService.ClassifyChange(hashBase, hashNew).Should().Be("documentation");
    }

    [Fact]
    public void ClassifyChange_TitleChange_IsStructural()
    {
        var blueprintBase = """{"id":"bp1","title":"Test","actions":[],"participants":[]}""";
        var blueprintChanged = """{"id":"bp1","title":"Changed","actions":[],"participants":[]}""";

        var hashBase = _diffService.ComputeStructuralHash(blueprintBase);
        var hashNew = _diffService.ComputeStructuralHash(blueprintChanged);

        StructuralDiffService.ClassifyChange(hashBase, hashNew).Should().Be("structural");
    }
}
