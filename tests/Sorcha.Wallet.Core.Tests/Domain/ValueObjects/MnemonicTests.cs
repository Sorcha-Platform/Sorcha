// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
using FluentAssertions;
using Sorcha.Wallet.Core.Domain.ValueObjects;
using Xunit;

namespace Sorcha.Wallet.Core.Tests.Domain.ValueObjects;

public class MnemonicTests
{
    [Fact]
    public void Generate_Default_Produces12WordMnemonic()
    {
        var mnemonic = Mnemonic.Generate();

        mnemonic.WordCount.Should().Be(12);
        mnemonic.Phrase.Split(' ').Should().HaveCount(12);
    }

    [Fact]
    public void Generate_24Words_Produces24WordMnemonic()
    {
        var mnemonic = Mnemonic.Generate(24);

        mnemonic.WordCount.Should().Be(24);
        mnemonic.Phrase.Split(' ').Should().HaveCount(24);
    }

    [Fact]
    public void Generate_TwoCalls_ProducesDifferentMnemonics()
    {
        var first = Mnemonic.Generate();
        var second = Mnemonic.Generate();

        first.Phrase.Should().NotBe(second.Phrase);
    }

    [Fact]
    public void Constructor_ValidPhrase_CreatesMnemonic()
    {
        var generated = Mnemonic.Generate();
        var phrase = generated.Phrase;

        var restored = new Mnemonic(phrase);

        restored.Phrase.Should().Be(phrase);
        restored.WordCount.Should().Be(generated.WordCount);
    }

    [Fact]
    public void Constructor_EmptyPhrase_ThrowsArgumentException()
    {
        var act = () => new Mnemonic("");

        act.Should().Throw<ArgumentException>()
            .WithParameterName("phrase");
    }

    [Fact]
    public void Constructor_NullPhrase_ThrowsArgumentException()
    {
        var act = () => new Mnemonic(null!);

        act.Should().Throw<ArgumentException>()
            .WithParameterName("phrase");
    }

    [Fact]
    public void Constructor_InvalidPhrase_ThrowsArgumentException()
    {
        var act = () => new Mnemonic("these are not valid bip39 words at all sorry about that");

        act.Should().Throw<ArgumentException>()
            .WithParameterName("phrase");
    }

    [Fact]
    public void IsValid_ValidPhrase_ReturnsTrue()
    {
        var generated = Mnemonic.Generate();

        Mnemonic.IsValid(generated.Phrase).Should().BeTrue();
    }

    [Fact]
    public void IsValid_InvalidPhrase_ReturnsFalse()
    {
        Mnemonic.IsValid("not a valid mnemonic phrase").Should().BeFalse();
    }

    [Fact]
    public void DeriveSeed_SameMnemonic_ReturnsConsistentOutput()
    {
        var mnemonic = Mnemonic.Generate();

        var seed1 = mnemonic.DeriveSeed();
        var seed2 = mnemonic.DeriveSeed();

        seed1.Should().BeEquivalentTo(seed2);
        seed1.Should().HaveCount(32);
    }

    [Fact]
    public void DeriveSeed_DifferentPassphrase_ReturnsDifferentSeed()
    {
        var mnemonic = Mnemonic.Generate();

        var seedNoPassphrase = mnemonic.DeriveSeed();
        var seedWithPassphrase = mnemonic.DeriveSeed("my-secret-passphrase");

        seedNoPassphrase.Should().NotBeEquivalentTo(seedWithPassphrase);
    }

    [Fact]
    public void ToString_NeverExposesPhrase()
    {
        var mnemonic = Mnemonic.Generate();

        var result = mnemonic.ToString();

        result.Should().Be("Mnemonic(12 words)");
        result.Should().NotContain(mnemonic.Phrase.Split(' ')[0]);
    }

    [Fact]
    public void ToString_24Words_ShowsCorrectCount()
    {
        var mnemonic = Mnemonic.Generate(24);

        mnemonic.ToString().Should().Be("Mnemonic(24 words)");
    }
}
