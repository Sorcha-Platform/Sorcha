// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Linq;
using System.Text.Json;
using FluentAssertions;
using Sorcha.UI.Core.Models.Forms;
using Xunit;

namespace Sorcha.UI.Components.User.Tests.Forms;

/// <summary>
/// A selection stores one string and shows another. Before this, they were the same string, so an
/// author had to pick between an identifier the citizen cannot read (<c>GB</c>) and prose that is
/// not a stable identifier ("United Kingdom").
/// </summary>
public sealed class SelectionOptionTests
{
    private static JsonElement Schema(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [Fact]
    public void EnumWithoutLabels_ShowsTheValue()
    {
        var options = SelectionOption.FromSchema(Schema("""{"type":"string","enum":["gb","fr"]}"""));

        options.Should().HaveCount(2);
        options[0].Value.Should().Be("gb");
        options[0].Label.Should().Be("gb", "with no labels declared, the value is all there is to show");
    }

    [Fact]
    public void EnumWithLabels_PairsThemInOrder()
    {
        var options = SelectionOption.FromSchema(Schema("""
            {"type":"string","enum":["GB","FR"],"x-enumNames":["United Kingdom","France"]}
            """));

        options.Select(o => o.Value).Should().Equal("GB", "FR");
        options.Select(o => o.Label).Should().Equal("United Kingdom", "France");
    }

    [Fact]
    public void MismatchedLabelCount_FallsBackToValues_RatherThanMispairing()
    {
        // THE case worth failing safe on. Zipping as far as the shorter array goes would leave the
        // citizen reading "France" while the payload stores "DE" — and it is the stored value that
        // gets signed and acted on. An ugly dropdown is recoverable; a silently wrong signed value
        // is not.
        var options = SelectionOption.FromSchema(Schema("""
            {"type":"string","enum":["GB","FR","DE"],"x-enumNames":["United Kingdom","France"]}
            """));

        options.Should().HaveCount(3);
        // Explicit collection overload: Equal(params) would treat the reason as a 4th expected item.
        options.Select(o => o.Label).Should().Equal(
            new[] { "GB", "FR", "DE" },
            "a partial label list must be ignored wholesale, never zipped");
    }

    [Fact]
    public void BlankLabel_FallsBackToValues()
    {
        // A blank entry is a mispairing waiting to happen, and an unreadable option either way.
        var options = SelectionOption.FromSchema(Schema("""
            {"type":"string","enum":["GB","FR"],"x-enumNames":["United Kingdom","   "]}
            """));

        options.Select(o => o.Label).Should().Equal("GB", "FR");
    }

    [Fact]
    public void NonArrayLabels_AreIgnored()
    {
        var options = SelectionOption.FromSchema(Schema("""
            {"type":"string","enum":["GB"],"x-enumNames":"United Kingdom"}
            """));

        options.Should().ContainSingle().Which.Label.Should().Be("GB");
    }

    [Fact]
    public void NoEnum_YieldsNoOptions()
    {
        SelectionOption.FromSchema(Schema("""{"type":"string"}""")).Should().BeEmpty();
        SelectionOption.FromSchema(null).Should().BeEmpty();
    }

    [Fact]
    public void LabelsNeverChangeTheStoredValue()
    {
        // The signed payload must be identical whether or not labels are present.
        var bare = SelectionOption.FromSchema(Schema("""{"type":"string","enum":["GB","FR"]}"""));
        var labelled = SelectionOption.FromSchema(Schema("""
            {"type":"string","enum":["GB","FR"],"x-enumNames":["United Kingdom","France"]}
            """));

        labelled.Select(o => o.Value).Should().Equal(bare.Select(o => o.Value));
    }
}
