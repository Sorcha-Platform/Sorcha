// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
using System;
using Sorcha.Cryptography.Enums;

namespace Sorcha.Cryptography.Models;

/// <summary>
/// Represents a complete key ring with mnemonic recovery phrase.
/// </summary>
public class KeyRing : IDisposable
{
    private bool _disposed;

    /// <summary>
    /// Gets or sets the network type for this key ring.
    /// </summary>
    public required WalletNetworks Network { get; init; }

    /// <summary>
    /// Gets or sets the mnemonic recovery phrase (12, 15, 18, 21, or 24 words; new wallets default to 24).
    /// </summary>
    public string? Mnemonic { get; init; }

    /// <summary>
    /// Gets or sets the master key set.
    /// </summary>
    public required KeySet MasterKeySet { get; init; }

    /// <summary>
    /// Gets or sets the optional password hint.
    /// </summary>
    public string? PasswordHint { get; init; }

    /// <summary>
    /// Gets or sets the creation timestamp.
    /// </summary>
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Zeroes out sensitive key material.
    /// </summary>
    public void Zeroize()
    {
        MasterKeySet.Zeroize();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (!_disposed)
        {
            Zeroize();
            _disposed = true;
        }
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Destructor to ensure key material is zeroized.
    /// </summary>
    ~KeyRing() => Dispose();
}
