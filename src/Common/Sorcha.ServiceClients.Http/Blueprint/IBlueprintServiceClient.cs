// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.ServiceClients.Blueprint;

/// <summary>
/// Unified client interface for Blueprint Service operations
/// </summary>
public interface IBlueprintServiceClient
{
    /// <summary>
    /// Gets a blueprint by ID
    /// </summary>
    /// <param name="blueprintId">Blueprint ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Blueprint definition, or null if not found</returns>
    Task<string?> GetBlueprintAsync(
        string blueprintId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates a transaction payload against blueprint schema
    /// </summary>
    /// <param name="blueprintId">Blueprint ID</param>
    /// <param name="actionId">Action ID within blueprint</param>
    /// <param name="payload">Payload to validate (JSON)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if payload is valid</returns>
    Task<bool> ValidatePayloadAsync(
        string blueprintId,
        string actionId,
        string payload,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Submits a single chunk of a file upload to the Blueprint Service
    /// </summary>
    /// <param name="senderWallet">Wallet address of the sender</param>
    /// <param name="registerAddress">Target register address</param>
    /// <param name="chunkIndex">Zero-based index of this chunk</param>
    /// <param name="totalChunks">Total number of chunks in the file</param>
    /// <param name="fileHash">SHA-256 hash of the complete file (hex)</param>
    /// <param name="contentType">MIME type of the file</param>
    /// <param name="chunkContent">Raw bytes of this chunk</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Result containing the chunk transaction ID, chunk index, and timestamp; or null on failure</returns>
    Task<FileChunkSubmissionResult?> SubmitFileChunkAsync(
        string senderWallet,
        string registerAddress,
        int chunkIndex,
        int totalChunks,
        string fileHash,
        string contentType,
        byte[] chunkContent,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Result returned after a file chunk is successfully submitted
/// </summary>
/// <param name="ChunkTransactionId">Transaction ID assigned to this chunk on the register</param>
/// <param name="ChunkIndex">Zero-based index of the submitted chunk</param>
/// <param name="Timestamp">Server-side timestamp at which the chunk was accepted</param>
public record FileChunkSubmissionResult(string ChunkTransactionId, int ChunkIndex, DateTimeOffset Timestamp);
