// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
using System.ComponentModel.DataAnnotations;

namespace Sorcha.Wallet.Service.Models;

/// <summary>
/// Request model for granting wallet access
/// </summary>
public class GrantAccessRequest
{
    /// <summary>
    /// Subject identifier (user/service principal)
    /// </summary>
    [Required]
    public required string Subject { get; set; }

    /// <summary>
    /// Access right to grant (Owner, ReadWrite, ReadOnly)
    /// </summary>
    [Required]
    public required string AccessRight { get; set; }

    /// <summary>
    /// Optional reason for granting access
    /// </summary>
    public string? Reason { get; set; }

    /// <summary>
    /// Optional expiration date/time
    /// </summary>
    public DateTime? ExpiresAt { get; set; }

    /// <summary>
    /// Sorcha derivation contexts (e.g. <c>sorcha:register-attestation</c>) the grant may sign at.
    /// Required when an organisation Administrator grants access to the organisation's wallet (#1643).
    /// </summary>
    public List<string>? AllowedDerivationContexts { get; set; }
}
