// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;

using Sorcha.TransactionHandler.Encryption;

namespace Sorcha.TransactionHandler.Tests;

/// <summary>
/// Unit tests for <see cref="HkdfKeyDerivation"/>.
/// Verifies determinism, isolation, and input validation for HKDF-SHA256 chunk key derivation.
/// </summary>
public class HkdfKeyDerivationTests
{
    #region DeriveChunkKey (array overload)

    [Fact]
    public void DeriveChunkKey_ValidInputs_Returns32ByteKey()
    {
        // Arrange
        var masterKey = HkdfKeyDerivation.GenerateMasterFileKey();
        var salt = HkdfKeyDerivation.GenerateSalt();

        // Act
        var result = HkdfKeyDerivation.DeriveChunkKey(masterKey, salt, chunkIndex: 0);

        // Assert
        result.Should().HaveCount(HkdfKeyDerivation.KeySizeBytes,
            because: "XChaCha20-Poly1305 requires a 32-byte key");
    }

    [Fact]
    public void DeriveChunkKey_SameInputs_ReturnsDeterministicKey()
    {
        // Arrange
        var masterKey = HkdfKeyDerivation.GenerateMasterFileKey();
        var salt = HkdfKeyDerivation.GenerateSalt();
        const int chunkIndex = 3;

        // Act
        var first = HkdfKeyDerivation.DeriveChunkKey(masterKey, salt, chunkIndex);
        var second = HkdfKeyDerivation.DeriveChunkKey(masterKey, salt, chunkIndex);

        // Assert
        first.Should().Equal(second,
            because: "HKDF is a deterministic function — identical inputs must yield identical outputs");
    }

    [Fact]
    public void DeriveChunkKey_DifferentIndices_ReturnsDifferentKeys()
    {
        // Arrange
        var masterKey = HkdfKeyDerivation.GenerateMasterFileKey();
        var salt = HkdfKeyDerivation.GenerateSalt();

        // Act
        var chunkZero = HkdfKeyDerivation.DeriveChunkKey(masterKey, salt, chunkIndex: 0);
        var chunkOne = HkdfKeyDerivation.DeriveChunkKey(masterKey, salt, chunkIndex: 1);

        // Assert
        chunkZero.Should().NotEqual(chunkOne,
            because: "the chunk index is embedded in the HKDF info field, producing unique per-chunk keys");
    }

    [Fact]
    public void DeriveChunkKey_DifferentSalts_ReturnsDifferentKeys()
    {
        // Arrange
        var masterKey = HkdfKeyDerivation.GenerateMasterFileKey();
        var saltA = HkdfKeyDerivation.GenerateSalt();
        var saltB = HkdfKeyDerivation.GenerateSalt();
        const int chunkIndex = 0;

        // Act
        var keyA = HkdfKeyDerivation.DeriveChunkKey(masterKey, saltA, chunkIndex);
        var keyB = HkdfKeyDerivation.DeriveChunkKey(masterKey, saltB, chunkIndex);

        // Assert
        keyA.Should().NotEqual(keyB,
            because: "different salts must produce different derived keys to prevent cross-file key reuse");
    }

    [Fact]
    public void DeriveChunkKey_InvalidKeySize_ThrowsArgumentException()
    {
        // Arrange — 16-byte key is too short (AES-128 size, not 32-byte HKDF input)
        var shortKey = new byte[16];
        var salt = HkdfKeyDerivation.GenerateSalt();

        // Act
        var act = () => HkdfKeyDerivation.DeriveChunkKey(shortKey, salt, chunkIndex: 0);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithParameterName("masterFileKey");
    }

    [Fact]
    public void DeriveChunkKey_NegativeIndex_ThrowsArgumentException()
    {
        // Arrange
        var masterKey = HkdfKeyDerivation.GenerateMasterFileKey();
        var salt = HkdfKeyDerivation.GenerateSalt();

        // Act
        var act = () => HkdfKeyDerivation.DeriveChunkKey(masterKey, salt, chunkIndex: -1);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithParameterName("chunkIndex");
    }

    #endregion

    #region DeriveChunkKey (span overload)

    [Fact]
    public void DeriveChunkKey_SpanOverload_MatchesArrayOverload()
    {
        // Arrange
        var masterKey = HkdfKeyDerivation.GenerateMasterFileKey();
        var salt = HkdfKeyDerivation.GenerateSalt();
        const int chunkIndex = 7;

        var arrayResult = HkdfKeyDerivation.DeriveChunkKey(masterKey, salt, chunkIndex);

        // Act — span overload writes into a caller-supplied buffer
        var spanOutput = new byte[HkdfKeyDerivation.KeySizeBytes];
        HkdfKeyDerivation.DeriveChunkKey(
            masterKey.AsSpan(),
            salt.AsSpan(),
            chunkIndex,
            spanOutput.AsSpan());

        // Assert
        spanOutput.Should().Equal(arrayResult,
            because: "both overloads must produce the same HKDF output for identical inputs");
    }

    #endregion

    #region GenerateMasterFileKey

    [Fact]
    public void GenerateMasterFileKey_ReturnsUnique32ByteKeys()
    {
        // Act
        var keyA = HkdfKeyDerivation.GenerateMasterFileKey();
        var keyB = HkdfKeyDerivation.GenerateMasterFileKey();

        // Assert
        keyA.Should().HaveCount(HkdfKeyDerivation.KeySizeBytes,
            because: "master file keys must be exactly 32 bytes for XChaCha20-Poly1305 compatibility");

        keyB.Should().HaveCount(HkdfKeyDerivation.KeySizeBytes);

        keyA.Should().NotEqual(keyB,
            because: "each call must use a fresh cryptographic random source");
    }

    #endregion

    #region GenerateSalt

    [Fact]
    public void GenerateSalt_ReturnsUnique32ByteArrays()
    {
        // Act
        var saltA = HkdfKeyDerivation.GenerateSalt();
        var saltB = HkdfKeyDerivation.GenerateSalt();

        // Assert
        saltA.Should().HaveCount(HkdfKeyDerivation.KeySizeBytes,
            because: "salts must be exactly 32 bytes to match the master key size convention");

        saltB.Should().HaveCount(HkdfKeyDerivation.KeySizeBytes);

        saltA.Should().NotEqual(saltB,
            because: "each call must draw from the cryptographic RNG, producing statistically unique salts");
    }

    #endregion
}
