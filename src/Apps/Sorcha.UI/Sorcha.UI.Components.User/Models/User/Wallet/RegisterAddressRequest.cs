// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.ComponentModel.DataAnnotations;

namespace Sorcha.UI.Core.Models.Wallet;

/// <summary>
/// Request model for registering a client-derived HD wallet address
/// </summary>
public class RegisterAddressRequest
{
    /// <summary>
    /// Derived public key (base64 encoded)
    /// </summary>
    [Required]
    public required string DerivedPublicKey { get; set; }

    /// <summary>
    /// The derived wallet address
    /// </summary>
    [Required]
    public required string DerivedAddress { get; set; }

    /// <summary>
    /// BIP44 derivation path (e.g., m/44'/0'/0'/0/1)
    /// </summary>
    [Required]
    [RegularExpression(@"^m/44'/\d+'/\d+'/[01]/\d+$",
        ErrorMessage = "Derivation path must be valid BIP44 format: m/44'/cointype'/account'/change/index")]
    public required string DerivationPath { get; set; }

    /// <summary>
    /// Optional label for this address
    /// </summary>
    public string? Label { get; set; }

    /// <summary>
    /// Optional notes about this address
    /// </summary>
    public string? Notes { get; set; }

    /// <summary>
    /// Optional tags for categorization (comma-separated or JSON array)
    /// </summary>
    public string? Tags { get; set; }

    /// <summary>
    /// Optional metadata dictionary
    /// </summary>
    public Dictionary<string, string>? Metadata { get; set; }
}
