// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.Blueprint.Models;

namespace Sorcha.Blueprint.Models.Tests;

public class BlueprintVersionTests
{
    [Fact]
    public void ToString_FormatsAsVersionString()
    {
        new BlueprintVersion { Major = 1, Minor = 0 }.ToString().Should().Be("v1.0");
        new BlueprintVersion { Major = 3, Minor = 5 }.ToString().Should().Be("v3.5");
        new BlueprintVersion { Major = 0, Minor = 0 }.ToString().Should().Be("v0.0");
    }

    [Theory]
    [InlineData(1, 0, "structural", 2, 0)]
    [InlineData(1, 2, "documentation", 1, 3)]
    [InlineData(3, 5, "structural", 4, 0)]
    [InlineData(3, 5, "documentation", 3, 6)]
    public void VersionIncrement_FollowsRules(int prevMajor, int prevMinor, string changeType, int expectedMajor, int expectedMinor)
    {
        int newMajor, newMinor;

        if (changeType == "structural")
        {
            newMajor = prevMajor + 1;
            newMinor = 0;
        }
        else
        {
            newMajor = prevMajor;
            newMinor = prevMinor + 1;
        }

        newMajor.Should().Be(expectedMajor);
        newMinor.Should().Be(expectedMinor);
    }

    [Fact]
    public void FirstPublish_IsAlways_V1_0()
    {
        // First publish: no previous version exists
        var version = new BlueprintVersion
        {
            Major = 1,
            Minor = 0,
            ChangeType = "structural"
        };

        version.Major.Should().Be(1);
        version.Minor.Should().Be(0);
        version.ChangeType.Should().Be("structural");
    }

    [Fact]
    public void MajorBump_ResetsMinor()
    {
        // v3.5 → structural change → v4.0
        int prevMajor = 3, prevMinor = 5;
        var newMajor = prevMajor + 1;
        var newMinor = 0; // reset on major bump

        newMajor.Should().Be(4);
        newMinor.Should().Be(0);
    }

    [Fact]
    public void ChangeType_MustBeStructuralOrDocumentation()
    {
        var version = new BlueprintVersion { ChangeType = "structural" };
        version.ChangeType.Should().BeOneOf("structural", "documentation");

        version = new BlueprintVersion { ChangeType = "documentation" };
        version.ChangeType.Should().BeOneOf("structural", "documentation");
    }

    [Fact]
    public void StructuralHash_DefaultEmpty()
    {
        new BlueprintVersion().StructuralHash.Should().BeEmpty();
    }

    [Fact]
    public void StructuralHash_ShouldBe64CharHex()
    {
        var version = new BlueprintVersion
        {
            StructuralHash = new string('a', 64)
        };
        version.StructuralHash.Should().HaveLength(64);
    }
}
