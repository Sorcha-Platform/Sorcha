// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Blueprint.Service.Models;

/// <summary>
/// Result of a recipient payload decryption operation.
/// </summary>
public sealed class DecryptionResult
{
    /// <summary>
    /// Whether the decryption succeeded.
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// The merged decrypted payload fields from all authorized disclosure groups.
    /// Null when <see cref="Success"/> is false.
    /// </summary>
    public Dictionary<string, object>? DecryptedPayload { get; init; }

    /// <summary>
    /// Error message when decryption failed. Null when <see cref="Success"/> is true.
    /// </summary>
    public string? Error { get; init; }

    /// <summary>
    /// Creates a successful decryption result with the merged payload.
    /// </summary>
    /// <param name="payload">The decrypted and merged payload dictionary.</param>
    /// <returns>A successful <see cref="DecryptionResult"/>.</returns>
    public static DecryptionResult Succeeded(Dictionary<string, object> payload) =>
        new() { Success = true, DecryptedPayload = payload };

    /// <summary>
    /// Creates a failed decryption result with an error message.
    /// </summary>
    /// <param name="error">The error message describing the failure.</param>
    /// <returns>A failed <see cref="DecryptionResult"/>.</returns>
    public static DecryptionResult Failed(string error) =>
        new() { Success = false, Error = error };
}
