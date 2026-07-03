// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
using System.ComponentModel.DataAnnotations;

namespace Sorcha.Wallet.Service.Models;

/// <summary>
/// Request model for generating a new address
/// </summary>
public class GenerateAddressRequest
{
    /// <summary>
    /// Optional derivation path (BIP44 format)
    /// </summary>
    [StringLength(120)]
    public string? DerivationPath { get; set; }
}
