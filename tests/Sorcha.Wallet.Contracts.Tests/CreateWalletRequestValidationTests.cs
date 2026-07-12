// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.ComponentModel.DataAnnotations;
using FluentAssertions;
using Sorcha.Wallet.Contracts.Models;
using Sorcha.Wallet.Contracts.Validation;

namespace Sorcha.Wallet.Contracts.Tests;

/// <summary>
/// The single canonical validation ruleset for <see cref="CreateWalletRequest"/> — replaces the three
/// divergent rules (attribute / inline regex / none) that had drifted across the copied DTOs. Validation
/// is DataAnnotations so it fires identically under ASP.NET minimal-API model binding and Blazor
/// EditForm's DataAnnotationsValidator.
/// </summary>
public class CreateWalletRequestValidationTests
{
    private static IReadOnlyList<ValidationResult> Validate(object model)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(model, new ValidationContext(model), results, validateAllProperties: true);
        return results;
    }

    [Fact]
    public void ValidRequest_ProducesNoErrors()
    {
        var results = Validate(new CreateWalletRequest { Name = "Primary", Algorithm = "ED25519", WordCount = 12 });

        results.Should().BeEmpty();
    }

    [Theory]
    [InlineData(12)]
    [InlineData(15)]
    [InlineData(18)]
    [InlineData(21)]
    [InlineData(24)]
    public void ValidWordCounts_Pass(int wordCount)
    {
        var results = Validate(new CreateWalletRequest { Name = "Primary", Algorithm = "ED25519", WordCount = wordCount });

        results.Should().BeEmpty();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(11)]
    [InlineData(13)]
    [InlineData(25)]
    public void InvalidWordCount_FailsValidation(int wordCount)
    {
        var results = Validate(new CreateWalletRequest { Name = "Primary", Algorithm = "ED25519", WordCount = wordCount });

        results.Should().ContainSingle().Which.MemberNames.Should().Contain(nameof(CreateWalletRequest.WordCount));
    }

    [Fact]
    public void EmptyName_FailsValidation()
    {
        var results = Validate(new CreateWalletRequest { Name = "", Algorithm = "ED25519", WordCount = 12 });

        results.Should().Contain(r => r.MemberNames.Contains(nameof(CreateWalletRequest.Name)));
    }

    [Fact]
    public void Bip39WordCountAttribute_RejectsNonIntAndInvalidCounts()
    {
        var attribute = new Bip39WordCountAttribute();

        attribute.IsValid(12).Should().BeTrue();
        attribute.IsValid(13).Should().BeFalse();
        attribute.IsValid("12").Should().BeFalse();
        attribute.IsValid(null).Should().BeFalse();
    }
}
