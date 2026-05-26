// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Sorcha.McpServer.Infrastructure;
using Sorcha.McpServer.Services;
using Sorcha.Register.Models;
using Sorcha.ServiceClients.Register;

namespace Sorcha.McpServer.Tools.Admin;

/// <summary>
/// Administrator tool returning a Merkle inclusion proof for a sealed transaction.
/// Routes through the typed <see cref="IRegisterServiceClient"/>.
/// </summary>
[McpServerToolType]
public sealed class TransactionInclusionProofTool
{
    private const string ToolName = "sorcha_transaction_inclusion_proof";
    private const string ServiceName = "Register";

    private readonly IMcpAuthorizationService _authService;
    private readonly IServiceAvailabilityTracker _availabilityTracker;
    private readonly IRegisterServiceClient _registerClient;
    private readonly ILogger<TransactionInclusionProofTool> _logger;

    public TransactionInclusionProofTool(
        IMcpAuthorizationService authService,
        IServiceAvailabilityTracker availabilityTracker,
        IRegisterServiceClient registerClient,
        ILogger<TransactionInclusionProofTool> logger)
    {
        _authService = authService;
        _availabilityTracker = availabilityTracker;
        _registerClient = registerClient;
        _logger = logger;
    }

    /// <summary>
    /// Gets a Merkle inclusion proof for a sealed transaction.
    /// </summary>
    /// <param name="registerId">The register containing the transaction.</param>
    /// <param name="transactionId">The transaction to prove inclusion of.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The Merkle inclusion proof, or a NotFound result if missing or not yet sealed.</returns>
    [McpServerTool(Name = ToolName)]
    [Description("Generates a compact Merkle inclusion proof that a transaction is a leaf in its docket's Merkle tree, returning the sibling hashes from leaf to root, the docket height, the expected Merkle root, the leaf index, and the tree size. Call this to cryptographically prove a transaction was sealed onto the ledger without trusting the register service — the proof can be re-verified offline against a known docket root. Returns NotFound when the transaction does not exist or has not yet been sealed into a docket (no proof is possible until sealing); call this when you need cryptographic proof of sealing rather than lifecycle state, and use sorcha_transaction_verification_bundle instead of this when you want the proof plus receipt and revocation status in one portable package.")]
    public async Task<TransactionInclusionProofResult> GetProofAsync(
        [Description("The register ID containing the transaction")] string registerId,
        [Description("The transaction ID to prove inclusion of")] string transactionId,
        CancellationToken cancellationToken = default)
    {
        if (!_authService.CanInvokeTool(ToolName))
        {
            return new TransactionInclusionProofResult
            {
                Status = "Unauthorized",
                Message = "Access denied. This tool requires the sorcha:admin role.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        if (string.IsNullOrWhiteSpace(registerId) || string.IsNullOrWhiteSpace(transactionId))
        {
            return new TransactionInclusionProofResult
            {
                Status = "Error",
                Message = "Both register ID and transaction ID are required.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        if (!_availabilityTracker.IsServiceAvailable(ServiceName))
        {
            return new TransactionInclusionProofResult
            {
                Status = "Unavailable",
                Message = "Register service is currently unavailable. Please try again later.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        _logger.LogInformation("Getting inclusion proof for transaction {TransactionId} on register {RegisterId}", transactionId, registerId);

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var proof = await _registerClient.GetInclusionProofAsync(registerId, transactionId, cancellationToken);
            stopwatch.Stop();
            _availabilityTracker.RecordSuccess(ServiceName);

            if (proof is null)
            {
                return new TransactionInclusionProofResult
                {
                    Status = "NotFound",
                    Message = $"No inclusion proof available for transaction '{transactionId}' (not found or not yet sealed).",
                    CheckedAt = DateTimeOffset.UtcNow,
                    ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
                };
            }

            return new TransactionInclusionProofResult
            {
                Status = "Success",
                Message = $"Inclusion proof generated for transaction '{transactionId}' (docket {proof.DocketNumber}).",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds,
                Proof = proof
            };
        }
        catch (TaskCanceledException)
        {
            stopwatch.Stop();
            _availabilityTracker.RecordFailure(ServiceName);
            _logger.LogWarning("Inclusion-proof query timed out for transaction {TransactionId}", transactionId);
            return new TransactionInclusionProofResult
            {
                Status = "Timeout",
                Message = "Request to register service timed out.",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _availabilityTracker.RecordFailure(ServiceName, ex);
            _logger.LogError(ex, "Failed to get inclusion proof for transaction {TransactionId}", transactionId);
            return new TransactionInclusionProofResult
            {
                Status = "Error",
                Message = $"Failed to get inclusion proof: {ex.Message}",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
            };
        }
    }
}

/// <summary>
/// Result of a Merkle inclusion-proof query.
/// </summary>
public sealed record TransactionInclusionProofResult
{
    /// <summary>Status: Success, NotFound, Error, Unavailable, Timeout, or Unauthorized.</summary>
    public required string Status { get; init; }

    /// <summary>Human-readable message about the result.</summary>
    public required string Message { get; init; }

    /// <summary>When the query was performed.</summary>
    public required DateTimeOffset CheckedAt { get; init; }

    /// <summary>Response time in milliseconds.</summary>
    public int ResponseTimeMs { get; init; }

    /// <summary>The Merkle inclusion proof (on success).</summary>
    public MerkleInclusionProof? Proof { get; init; }
}
