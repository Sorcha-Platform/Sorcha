// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.UI.Core.Services.Forms;

/// <summary>
/// Service for hashing and signing form submission data with the participant's wallet.
/// </summary>
public interface IFormSigningService
{
    /// <summary>
    /// Serializes form data to canonical JSON (sorted keys)
    /// </summary>
    string SerializeFormData(Dictionary<string, object?> data);

    /// <summary>
    /// Computes SHA-256 hash of data and returns as base64
    /// </summary>
    string HashData(string canonicalJson);

    /// <summary>
    /// Signs form data with the participant's wallet.
    /// Returns the base64 signature or null if signing fails.
    /// </summary>
    Task<string?> SignWithWallet(string walletAddress, Dictionary<string, object?> data, CancellationToken ct = default);
}
