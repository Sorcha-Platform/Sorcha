// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
using System.ComponentModel.DataAnnotations;

namespace Sorcha.Wallet.Service.Models;

/// <summary>
/// Request model for updating wallet address metadata
/// </summary>
public class UpdateAddressRequest
{
    /// <summary>
    /// Updated label (null to leave unchanged)
    /// </summary>
    [StringLength(200)]
    public string? Label { get; set; }

    /// <summary>
    /// Updated notes (null to leave unchanged)
    /// </summary>
    [StringLength(2000)]
    public string? Notes { get; set; }

    /// <summary>
    /// Updated tags (null to leave unchanged)
    /// </summary>
    [StringLength(1000)]
    public string? Tags { get; set; }

    /// <summary>
    /// Additional metadata to merge (null to leave unchanged)
    /// </summary>
    public Dictionary<string, string>? Metadata { get; set; }
}
