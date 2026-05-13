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
    /// Derives a seed from the mnemonic with optional passphrase.
    /// </summary>
    /// <remarks>
    /// Despite the name, this returns the 32-byte master ExtKey private key
    /// (the output of HMAC-SHA512 over the BIP39 PBKDF2 seed), NOT the raw
    /// BIP39 PBKDF2 seed itself. Wallet creation has always used this value
    /// and the address-deriving chain depends on it; renaming the method
    /// would break wallet-address stability. Prefer <see cref="DeriveBip39Seed"/>
    /// for new code that needs to round-trip an ExtKey via
    /// <c>ExtKey.CreateFromSeed(...)</c> without an extra HMAC round.
    /// </remarks>
    /// <param name="passphrase">Optional passphrase for additional security</param>
    /// <returns>The derived 32-byte master private key</returns>
    public byte[] DeriveSeed(string? passphrase = null)
    {
        var extKey = _mnemonic.DeriveExtKey(passphrase);
        return extKey.PrivateKey.ToBytes();
    }

    /// <summary>
    /// Derives the raw BIP39 PBKDF2 seed (64 bytes) from the mnemonic.
    /// This is the canonical input to <c>NBitcoin.ExtKey.CreateFromSeed</c>
    /// for BIP32 hierarchical derivation — feed it in once, derive purpose
    /// keys directly with the path (e.g. <c>m/44'/0'/0'/0/102</c>) and no
    /// additional HMAC round.
    /// </summary>
    /// <param name="passphrase">Optional BIP39 passphrase</param>
    /// <returns>The 64-byte BIP39 PBKDF2 seed</returns>
    public byte[] DeriveBip39Seed(string? passphrase = null)
    {
        return _mnemonic.DeriveSeed(passphrase ?? string.Empty);
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
