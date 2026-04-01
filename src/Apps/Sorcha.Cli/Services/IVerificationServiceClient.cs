// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Refit;

namespace Sorcha.Cli.Services;

/// <summary>
/// Refit client interface for verification endpoints on the Register Service.
/// </summary>
public interface IVerificationServiceClient
{
    /// <summary>
    /// Gets a transaction receipt.
    /// </summary>
    [Get("/api/registers/{registerId}/transactions/{txId}/receipt")]
    Task<TransactionReceiptResponse> GetReceiptAsync(
        string registerId,
        string txId,
        [Header("Authorization")] string authorization);

    /// <summary>
    /// Gets a verification bundle for a transaction.
    /// </summary>
    [Get("/api/registers/{registerId}/transactions/{txId}/verification-bundle")]
    Task<VerificationBundleResponse> GetVerificationBundleAsync(
        string registerId,
        string txId,
        [Header("Authorization")] string authorization);

    /// <summary>
    /// Verifies a transaction receipt.
    /// </summary>
    [Post("/api/registers/{registerId}/receipts/verify")]
    Task<VerificationResult> VerifyReceiptAsync(
        string registerId,
        [Body] TransactionReceiptResponse receipt,
        [Header("Authorization")] string authorization);

    /// <summary>
    /// Verifies a verification bundle.
    /// </summary>
    [Post("/api/registers/{registerId}/verification-bundles/verify")]
    Task<VerificationResult> VerifyBundleAsync(
        string registerId,
        [Body] VerificationBundleResponse bundle,
        [Header("Authorization")] string authorization);
}

// --- Request/Response DTOs ---

/// <summary>
/// Transaction receipt response.
/// </summary>
public class TransactionReceiptResponse
{
    public string TransactionId { get; set; } = string.Empty;
    public string RegisterId { get; set; } = string.Empty;
    public ulong DocketNumber { get; set; }
    public string MerkleRoot { get; set; } = string.Empty;
    public List<string> MerkleProof { get; set; } = new();
    public string ValidatorSignature { get; set; } = string.Empty;
    public string ValidatorAddress { get; set; } = string.Empty;
    public DateTimeOffset IssuedAt { get; set; }
}

/// <summary>
/// Verification bundle response.
/// </summary>
public class VerificationBundleResponse
{
    public string TransactionId { get; set; } = string.Empty;
    public string RegisterId { get; set; } = string.Empty;
    public TransactionReceiptResponse? Receipt { get; set; }
    public string? VerifiableCredential { get; set; }
    public string TransactionStatus { get; set; } = string.Empty;
    public DateTimeOffset GeneratedAt { get; set; }
}

/// <summary>
/// Result of a verification operation.
/// </summary>
public class VerificationResult
{
    public bool IsValid { get; set; }
    public string Status { get; set; } = string.Empty;
    public List<string> Errors { get; set; } = new();
    public DateTimeOffset VerifiedAt { get; set; }
}
