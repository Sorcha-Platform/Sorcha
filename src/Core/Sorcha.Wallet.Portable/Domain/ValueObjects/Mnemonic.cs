// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
using NBitcoin;

namespace Sorcha.Wallet.Core.Domain.ValueObjects;

/// <summary>
/// Represents a BIP39 mnemonic phrase
/// </summary>
public record Mnemonic
{
    private readonly NBitcoin.Mnemonic _mnemonic;

    /// <summary>
    /// Creates a mnemonic from an existing phrase
    /// </summary>
    /// <param name="phrase">The mnemonic phrase (12 or 24 words)</param>
    /// <exception cref="ArgumentException">Thrown if phrase is invalid</exception>
    public Mnemonic(string phrase)
    {
        if (string.IsNullOrWhiteSpace(phrase))
            throw new ArgumentException("Mnemonic phrase cannot be empty", nameof(phrase));

        try
        {
            _mnemonic = new NBitcoin.Mnemonic(phrase);
        }
        catch (Exception ex)
        {
            throw new ArgumentException("Invalid mnemonic phrase", nameof(phrase), ex);
        }
    }

    /// <summary>
    /// Creates a mnemonic from NBitcoin.Mnemonic
    /// </summary>
    internal Mnemonic(NBitcoin.Mnemonic mnemonic)
    {
        _mnemonic = mnemonic ?? throw new ArgumentNullException(nameof(mnemonic));
    }

    /// <summary>
    /// Generates a new random mnemonic
    /// </summary>
    /// <param name="wordCount">Number of words (12 or 24)</param>
    /// <returns>A new mnemonic</returns>
    public static Mnemonic Generate(int wordCount = 12)
    {
        var entropy = wordCount == 24 ? NBitcoin.WordCount.TwentyFour : NBitcoin.WordCount.Twelve;
        return new Mnemonic(new NBitcoin.Mnemonic(Wordlist.English, entropy));
    }

    /// <summary>
    /// Gets the mnemonic phrase as a string
    /// </summary>
    public string Phrase => _mnemonic.ToString();

    /// <summary>
    /// Gets the word count
    /// </summary>
    public int WordCount => _mnemonic.Words.Length;

    /// <summary>
    /// Derives a seed from the mnemonic with optional passphrase
    /// </summary>
    /// <param name="passphrase">Optional passphrase for additional security</param>
    /// <returns>The derived seed bytes</returns>
    public byte[] DeriveSeed(string? passphrase = null)
    {
        var extKey = _mnemonic.DeriveExtKey(passphrase);
        return extKey.PrivateKey.ToBytes();
    }

    /// <summary>
    /// Validates a mnemonic phrase
    /// </summary>
    /// <param name="phrase">The phrase to validate</param>
    /// <returns>True if valid</returns>
    public static bool IsValid(string phrase)
    {
        try
        {
            _ = new NBitcoin.Mnemonic(phrase);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Returns a string representation of the mnemonic showing the word count for security.
    /// The actual mnemonic phrase is never exposed in ToString() to prevent accidental logging.
    /// </summary>
    /// <returns>A string indicating the number of words (e.g., "Mnemonic(12 words)").</returns>
    public override string ToString() => $"Mnemonic({WordCount} words)";
}
