// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.ComponentModel.DataAnnotations;

namespace Sorcha.ServiceClients.Wallet.Models;

/// <summary>
/// Validates that a word count is a valid BIP-39 mnemonic length (12, 15, 18, 21, or 24).
/// These correspond to entropy sizes of 128, 160, 192, 224, and 256 bits respectively.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class Bip39WordCountAttribute : ValidationAttribute
{
    private static readonly int[] ValidCounts = [12, 15, 18, 21, 24];

    public Bip39WordCountAttribute()
        : base("The field {0} must be a valid BIP-39 word count (12, 15, 18, 21, or 24).")
    {
    }

    public override bool IsValid(object? value)
    {
        if (value is int count)
            return Array.IndexOf(ValidCounts, count) >= 0;
        return false;
    }
}
