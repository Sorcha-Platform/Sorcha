// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.UI.Core.Models.Registers;

/// <summary>
/// View model for displaying payload information in the UI.
/// </summary>
public record PayloadViewModel
{
    /// <summary>
    /// Payload index within the transaction
    /// </summary>
    public int Index { get; init; }

    /// <summary>
    /// SHA-256 hash of payload for integrity
    /// </summary>
    public required string Hash { get; init; }

    /// <summary>
    /// Size of payload in bytes
    /// </summary>
    public ulong PayloadSize { get; init; }

    /// <summary>
    /// Wallet addresses authorized to decrypt this payload
    /// </summary>
    public IReadOnlyList<string> WalletAccess { get; init; } = [];

    /// <summary>
    /// Encryption metadata flags
    /// </summary>
    public string? PayloadFlags { get; init; }

    /// <summary>
    /// Whether an initialization vector is present
    /// </summary>
    public bool HasIV { get; init; }

    /// <summary>
    /// Number of per-wallet encryption challenges
    /// </summary>
    public int ChallengeCount { get; init; }

    /// <summary>
    /// Base64-encoded payload data (if available)
    /// </summary>
    public string? Data { get; init; }

    /// <summary>
    /// UTF-8 decoded text content (null if decoding failed or data unavailable)
    /// </summary>
    public string? DecodedContent { get; init; }

    /// <summary>
    /// Whether the decoded content is valid JSON
    /// </summary>
    public bool IsJson { get; init; }

    /// <summary>
    /// Pretty-printed JSON content (null if not JSON)
    /// </summary>
    public string? PrettyJson { get; init; }

    /// <summary>
    /// Detected or declared MIME content type
    /// </summary>
    public string? ContentType { get; init; }

    /// <summary>
    /// Whether the payload is encrypted (has an initialization vector)
    /// </summary>
    public bool IsEncrypted => HasIV;

    /// <summary>
    /// Whether the current user can decrypt this payload
    /// </summary>
    public bool IsAccessible { get; init; }

    /// <summary>
    /// Computed: Human-readable payload size
    /// </summary>
    public string PayloadSizeFormatted => PayloadSize switch
    {
        >= 1_048_576 => $"{PayloadSize / 1_048_576.0:F1} MB",
        >= 1_024 => $"{PayloadSize / 1_024.0:F1} KB",
        _ => $"{PayloadSize} B"
    };
}
