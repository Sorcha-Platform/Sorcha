// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using FluentAssertions;
using Sorcha.UI.Core.Models.Forms;
using Xunit;

namespace Sorcha.UI.Components.User.Tests.Forms;

/// <summary>
/// Issue #1278 (UT-015) — no form text input set <c>autocapitalize</c>, so a mobile keyboard
/// auto-capitalised what the citizen typed. The n1 agent log shows <c>EH91JA</c> on submission 1 and
/// <c>Eh91ja</c> on submission 2: the value ARRIVED corrupted. It is a write-path defect, not a
/// render transform (there is no <c>text-transform: capitalize</c> anywhere in src/, and capitalize
/// cannot lowercase a tail anyway).
/// <para>
/// The rule these tests pin: a field whose value is MACHINE-CHECKED must not be auto-capitalised,
/// auto-corrected or spell-checked, because the keyboard's guess breaks a value the server will then
/// reject — or, worse, silently store wrong. A prose field keeps the browser defaults, since
/// capitalising the first letter of a sentence is what a citizen wants there.
/// </para>
/// </summary>
public sealed class TextInputHintsTests
{
    private static JsonElement Schema(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [Fact]
    public void APlainStringFieldKeepsBrowserDefaults()
    {
        var hints = TextInputHints.ForField(Schema("""{"type":"string","title":"Your name"}"""));

        hints.Should().Be(TextInputHints.Prose);
        hints.ToAttributes().Should().BeEmpty(
            "a prose field must not emit attributes at all — sentence capitalisation is correct there");
    }

    [Fact]
    public void AFieldWithAPatternIsTreatedAsExact()
    {
        // The AIAS postcode is exactly this shape: a machine-checked string.
        var hints = TextInputHints.ForField(
            Schema("""{"type":"string","pattern":"^[A-Z]{1,2}[0-9][A-Z0-9]? ?[0-9][A-Z]{2}$"}"""));

        hints.AutoCapitalize.Should().Be("none");
        hints.AutoCorrect.Should().Be("off");
        hints.SpellCheck.Should().Be("false");
    }

    [Fact]
    public void AnAddressLookupFieldIsTreatedAsExact()
    {
        var hints = TextInputHints.ForField(Schema("""{"type":"string","x-address-lookup":true}"""));

        hints.Should().Be(TextInputHints.ExactValue);
    }

    [Fact]
    public void AnEmailFieldAsksForTheEmailKeyboard_AndNoCapitalisation()
    {
        var hints = TextInputHints.ForField(Schema("""{"type":"string","format":"email"}"""));

        hints.AutoCapitalize.Should().Be("none");
        hints.InputMode.Should().Be("email");
    }

    [Theory]
    [InlineData("uri")]
    [InlineData("uuid")]
    [InlineData("hostname")]
    [InlineData("date")]
    [InlineData("date-time")]
    public void MachineFormatsAreTreatedAsExact(string format)
    {
        var hints = TextInputHints.ForField(Schema($$"""{"type":"string","format":"{{format}}"}"""));

        hints.AutoCapitalize.Should().Be("none",
            $"a '{format}' value is parsed by a machine, so a keyboard guess can only break it");
    }

    [Fact]
    public void AnEnumFieldIsTreatedAsExact()
    {
        var hints = TextInputHints.ForField(Schema("""{"type":"string","enum":["gb","fr"]}"""));

        hints.Should().Be(TextInputHints.ExactValue);
    }

    [Fact]
    public void AnUnknownSchemaFallsBackToProse_RatherThanGuessing()
        => TextInputHints.ForField(null).Should().Be(TextInputHints.Prose);

    [Fact]
    public void AttributesAreSplattableAndOmitNulls()
    {
        var attrs = TextInputHints.ExactValue.ToAttributes();

        attrs.Should().ContainKey("autocapitalize").WhoseValue.Should().Be("none");
        attrs.Should().ContainKey("autocorrect").WhoseValue.Should().Be("off");
        attrs.Should().ContainKey("spellcheck").WhoseValue.Should().Be("false");
        attrs.Should().NotContainKey("inputmode", "ExactValue makes no keyboard claim");
    }
}
