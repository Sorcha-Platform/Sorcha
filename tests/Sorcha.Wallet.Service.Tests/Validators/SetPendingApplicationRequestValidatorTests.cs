// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Wallet.Service.Models;
using Sorcha.Wallet.Service.Validators;

namespace Sorcha.Wallet.Service.Tests.Validators;

/// <summary>
/// Tests for <see cref="SetPendingApplicationRequestValidator"/> (Feature 124, T016).
/// </summary>
public sealed class SetPendingApplicationRequestValidatorTests
{
    private static readonly SetPendingApplicationRequestValidator Validator = new();

    [Fact]
    public void EmptyLabel_Fails()
    {
        var result = Validator.Validate(new SetPendingApplicationRequest { Label = string.Empty });
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(SetPendingApplicationRequest.Label));
    }

    [Fact]
    public void WhitespaceLabel_Fails()
    {
        var result = Validator.Validate(new SetPendingApplicationRequest { Label = "   " });
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void LabelOver80Chars_Fails()
    {
        var result = Validator.Validate(new SetPendingApplicationRequest { Label = new string('a', 81) });
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(SetPendingApplicationRequest.Label));
    }

    [Theory]
    [InlineData("Assured Identity")]
    [InlineData("X")]
    public void ValidLabel_Passes(string label)
    {
        var result = Validator.Validate(new SetPendingApplicationRequest { Label = label });
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Label80CharsExactly_Passes()
    {
        var result = Validator.Validate(new SetPendingApplicationRequest { Label = new string('a', 80) });
        result.IsValid.Should().BeTrue();
    }
}
