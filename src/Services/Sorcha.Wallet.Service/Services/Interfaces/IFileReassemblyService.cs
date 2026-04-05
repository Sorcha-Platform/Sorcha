// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Wallet.Service.Services.Interfaces;

/// <summary>
/// Mediates decryption and reassembly of multi-chunk file attachments stored on a Sorcha register.
/// The service unwraps the recipient's master file key from the action transaction's encrypted payload,
/// derives per-chunk symmetric keys via HKDF-SHA256, decrypts each chunk with XChaCha20-Poly1305,
/// and returns a streaming result for direct HTTP response forwarding.
/// </summary>
public interface IFileReassemblyService
{
    /// <summary>
    /// Prepares a file for download by fetching, decrypting, and reassembling all encrypted chunks.
    /// </summary>
    /// <param name="walletAddress">
    /// The wallet address of the requester. Must be a recipient of the action transaction's
    /// encrypted payload group in order to unwrap the master file key.
    /// </param>
    /// <param name="registerId">
    /// The register ID that holds the action and chunk transactions.
    /// </param>
    /// <param name="actionTxId">
    /// The transaction ID of the action whose payload contains the <c>FileReference</c>.
    /// </param>
    /// <param name="fieldName">
    /// The JSON field name in the action payload that holds the file attachment(s).
    /// </param>
    /// <param name="fileIndex">
    /// Zero-based index into the file array when the field contains multiple files.
    /// Defaults to <c>0</c> for single-file fields.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A <see cref="FileDownloadResult"/> with metadata and a streaming callback,
    /// or <see langword="null"/> if the file cannot be found or the wallet is not authorised.
    /// </returns>
    Task<FileDownloadResult?> PrepareDownloadAsync(
        string walletAddress,
        string registerId,
        string actionTxId,
        string fieldName,
        int fileIndex,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents the result of a prepared file download: metadata and an async streaming callback.
/// </summary>
/// <param name="FileName">Original filename as recorded in the <c>FileReference</c>.</param>
/// <param name="ContentType">MIME type of the file.</param>
/// <param name="Size">Total plaintext size in bytes.</param>
/// <param name="WriteToStreamAsync">
/// Callback that writes decrypted file bytes to the provided <see cref="Stream"/>.
/// After all chunks have been written the SHA-256 hash is verified; a mismatch throws
/// <see cref="InvalidOperationException"/> to signal an integrity failure to the caller.
/// </param>
public record FileDownloadResult(
    string FileName,
    string ContentType,
    long Size,
    Func<Stream, CancellationToken, Task> WriteToStreamAsync);
