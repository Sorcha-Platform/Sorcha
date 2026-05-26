// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Sorcha.McpServer.Infrastructure;
using Sorcha.McpServer.Services;
using Sorcha.ServiceClients.Register;

namespace Sorcha.McpServer.Tools.Admin;

/// <summary>
/// Administrator tool submitting a transaction revocation with a reason.
/// Routes through the typed <see cref="IRegisterServiceClient"/>.
/// </summary>
[McpServerToolType]
public sealed class TransactionRevokeTool
{
    private const string ToolName = "sorcha_transaction_revoke";
    private const string ServiceName = "Register";

    private static readonly string[] ValidReasons =
        ["Superseded", "Erroneous", "Compromised", "Expired", "Withdrawn", "Regulatory"];

    private readonly IMcpAuthorizationService _authService;
    private readonly IServiceAvailabilityTracker _availabilityTracker;
    private readonly IRegisterServiceClient _registerClient;
    private readonly ILogger<TransactionRevokeTool> _logger;

    public TransactionRevokeTool(
        IMcpAuthorizationService authService,
        IServiceAvailabilityTracker availabilityTracker,
        IRegisterServiceClient registerClient,
        ILogger<TransactionRevokeTool> logger)
    {
        _authService = authService;
        _availabilityTracker = availabilityTracker;
        _registerClient = registerClient;
        _logger = logger;
    }

    /// <summary>
    /// Submits a revocation that marks a transaction as revoked or superseded.
    /// </summary>
    /// <param name="registerId">The register containing the target transaction.</param>
    /// <param name="originalTxId">The transaction to revoke.</param>
    /// <param name="reason">Revocation reason: Superseded, Erroneous, Compromised, Expired, Withdrawn, or Regulatory.</param>
    /// <param name="supersededByTxId">Replacement transaction ID (required when reason is Superseded).</param>
    /// <param name="signerWalletAddress">Wallet address of the signer submitting the revocation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The revocation result with the new revocation transaction ID.</returns>
    [McpServerTool(Name = ToolName)]
    [Description("Submits a revocation transaction that marks an existing ledger transaction as Revoked or, when a replacement is supplied, Superseded — recording the reason (Superseded, Erroneous, Compromised, Expired, Withdrawn, or Regulatory) immutably on the register. Call this to invalidate a previously-issued credential or workflow action; the revocation itself is a new sealed transaction, so the original is never deleted, only marked. When reason is Superseded you MUST pass supersededByTxId pointing at the replacement transaction. This is a state-changing, append-only operation requiring Owner/Admin authority on the register, so call after checking sorcha_register_relationship; to read current lifecycle without changing anything, use sorcha_transaction_status instead of this tool.")]
    public async Task<TransactionRevokeToolResult> RevokeAsync(
        [Description("The register ID containing the target transaction")] string registerId,
        [Description("The transaction ID to revoke")] string originalTxId,
        [Description("Revocation reason: Superseded, Erroneous, Compromised, Expired, Withdrawn, or Regulatory")] string reason,
        [Description("Replacement transaction ID (required only when reason is Superseded)")] string? supersededByTxId = null,
        [Description("Wallet address of the signer submitting the revocation")] string? signerWalletAddress = null,
        CancellationToken cancellationToken = default)
    {
        if (!_authService.CanInvokeTool(ToolName))
        {
            return new TransactionRevokeToolResult
            {
                Status = "Unauthorized",
                Message = "Access denied. This tool requires the sorcha:admin role.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        if (string.IsNullOrWhiteSpace(registerId) || string.IsNullOrWhiteSpace(originalTxId))
        {
            return new TransactionRevokeToolResult
            {
                Status = "Error",
                Message = "Both register ID and original transaction ID are required.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        if (string.IsNullOrWhiteSpace(reason) ||
            !ValidReasons.Contains(reason, StringComparer.OrdinalIgnoreCase))
        {
            return new TransactionRevokeToolResult
            {
                Status = "Error",
                Message = $"Reason must be one of: {string.Join(", ", ValidReasons)}.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        if (reason.Equals("Superseded", StringComparison.OrdinalIgnoreCase) &&
            string.IsNullOrWhiteSpace(supersededByTxId))
        {
            return new TransactionRevokeToolResult
            {
                Status = "Error",
                Message = "supersededByTxId is required when reason is 'Superseded'.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        if (!_availabilityTracker.IsServiceAvailable(ServiceName))
        {
            return new TransactionRevokeToolResult
            {
                Status = "Unavailable",
                Message = "Register service is currently unavailable. Please try again later.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        _logger.LogInformation(
            "Revoking transaction {TransactionId} on register {RegisterId} (reason {Reason})",
            originalTxId, registerId, reason);

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var result = await _registerClient.RevokeTransactionAsync(
                registerId,
                new RevokeTransactionClientRequest
                {
                    OriginalTxId = originalTxId,
                    Reason = reason,
                    SupersededByTxId = supersededByTxId,
                    SignerWalletAddress = signerWalletAddress
                },
                cancellationToken);

            stopwatch.Stop();
            _availabilityTracker.RecordSuccess(ServiceName);

            if (result is null)
            {
                return new TransactionRevokeToolResult
                {
                    Status = "Error",
                    Message = $"Revocation of transaction '{originalTxId}' was not accepted (it may not exist, may already be revoked, or the request was rejected).",
                    CheckedAt = DateTimeOffset.UtcNow,
                    ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
                };
            }

            return new TransactionRevokeToolResult
            {
                Status = "Success",
                Message = $"Revocation submitted for transaction '{originalTxId}' (revocation tx '{result.RevocationTxId}').",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds,
                RevocationTxId = result.RevocationTxId,
                OriginalTxId = result.OriginalTxId
            };
        }
        catch (TaskCanceledException)
        {
            stopwatch.Stop();
            _availabilityTracker.RecordFailure(ServiceName);
            _logger.LogWarning("Revocation timed out for transaction {TransactionId}", originalTxId);
            return new TransactionRevokeToolResult
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
            _logger.LogError(ex, "Failed to revoke transaction {TransactionId}", originalTxId);
            return new TransactionRevokeToolResult
            {
                Status = "Error",
                Message = $"Failed to revoke transaction: {ex.Message}",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
            };
        }
    }
}

/// <summary>
/// Result of a transaction revocation submission.
/// </summary>
public sealed record TransactionRevokeToolResult
{
    /// <summary>Status: Success, Error, Unavailable, Timeout, or Unauthorized.</summary>
    public required string Status { get; init; }

    /// <summary>Human-readable message about the result.</summary>
    public required string Message { get; init; }

    /// <summary>When the request was performed.</summary>
    public required DateTimeOffset CheckedAt { get; init; }

    /// <summary>Response time in milliseconds.</summary>
    public int ResponseTimeMs { get; init; }

    /// <summary>ID of the newly-created revocation transaction (on success).</summary>
    public string? RevocationTxId { get; init; }

    /// <summary>The original transaction ID that was revoked (on success).</summary>
    public string? OriginalTxId { get; init; }
}
