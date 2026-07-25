// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.UI.Core.Models.Forms;
using Xunit;

namespace Sorcha.UI.Components.User.Tests.Forms;

/// <summary>
/// Issue #1278 (UT-015), write-path half. Keyboard hints stop a phone corrupting the value as it is
/// typed, but they cannot fix a value pasted, dictated, or typed on a keyboard that ignores the hint.
/// Normalising before the field is validated is what actually guarantees what gets signed.
/// </summary>
public sealed class PostalValueNormaliserTests
{
    [Theory]
    [InlineData("Eh91ja", "EH91JA")]          // exactly the corruption seen on n1
    [InlineData("eh9 1ja", "EH9 1JA")]
    [InlineData("  EH9   1JA  ", "EH9 1JA")]
    [InlineData("EH91JA", "EH91JA")]
    public void PostcodesNormaliseToUpperCaseWithSingleSpacing(string raw, string expected)
        => PostalValueNormaliser.Postcode(raw).Should().Be(expected);

    [Fact]
    public void AnEmptyPostcodeStaysEmpty_SoNormalisationNeverInventsAValue()
    {
        PostalValueNormaliser.Postcode(null).Should().BeEmpty();
        PostalValueNormaliser.Postcode("   ").Should().BeEmpty();
    }

    [Theory]
    [InlineData("  United   Kingdom ", "United Kingdom")]
    [InlineData("France", "France")]
    public void CountriesNormaliseWhitespaceOnly(string raw, string expected)
        => PostalValueNormaliser.Country(raw).Should().Be(expected);

    [Fact]
    public void CountryCaseIsLeftAlone()
    {
        // A country name is prose: sentence capitalisation is CORRECT for it, which is precisely why
        // it does not get the postcode treatment. Upper-casing "United Kingdom" would be a
        // regression dressed up as consistency.
        PostalValueNormaliser.Country("United Kingdom").Should().Be("United Kingdom");
        PostalValueNormaliser.Country("côte d'Ivoire").Should().Be("côte d'Ivoire");
    }
}
