// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.UI.Core.Services.Designer;
using Xunit;

namespace Sorcha.UI.Core.Tests.Services.Designer;

public class TabRouteParserTests
{
    [Theory]
    [InlineData("ai", DesignerTab.Ai)]
    [InlineData("diagram", DesignerTab.Diagram)]
    [InlineData("preview", DesignerTab.Preview)]
    public void Parse_ValidLowercaseValue_ReturnsMatchingTab(string input, DesignerTab expected)
    {
        TabRouteParser.Parse(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("DIAGRAM")]
    [InlineData("Preview")]
    [InlineData("AI")]
    public void Parse_MixedCaseValue_ReturnsMatchingTabCaseInsensitively(string input)
    {
        TabRouteParser.Parse(input).Should().BeOneOf(DesignerTab.Ai, DesignerTab.Diagram, DesignerTab.Preview);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Parse_NullOrWhitespace_ReturnsAi(string? input)
    {
        TabRouteParser.Parse(input).Should().Be(DesignerTab.Ai);
    }

    [Fact]
    public void Parse_UnknownValue_FallsBackToAi()
    {
        TabRouteParser.Parse("foo").Should().Be(DesignerTab.Ai);
    }

    [Theory]
    [InlineData(DesignerTab.Ai, null)]
    [InlineData(DesignerTab.Diagram, "diagram")]
    [InlineData(DesignerTab.Preview, "preview")]
    public void ToQuery_Roundtrips_WithParse(DesignerTab tab, string? expectedQuery)
    {
        var q = TabRouteParser.ToQuery(tab);
        q.Should().Be(expectedQuery);
        TabRouteParser.Parse(q).Should().Be(tab);
    }
}
