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
/// Administrator tool returning a transaction's lifecycle status (active / revoked / superseded).
/// Routes through the typed <see cref="IRegisterServiceClient"/>.
/// </summary>
[McpServerToolType]
public sealed class TransactionStatusTool
{
    private const string ToolName = "sorcha_transaction_status";
    private const string ServiceName = "Register";

    private readonly IMcpAuthorizationService _authService;
    private readonly IServiceAvailabilityTracker _availabilityTracker;
    private readonly IRegisterServiceClient _registerClient;
    private readonly ILogger<TransactionStatusTool> _logger;

    public TransactionStatusTool(
        IMcpAuthorizationService authService,
        IServiceAvailabilityTracker availabilityTracker,
        IRegisterServiceClient registerClient,
        ILogger<TransactionStatusTool> logger)
    {
        _authService = authService;
        _availabilityTracker = availabilityTracker;
        _registerClient = registerClient;
        _logger = logger;
    }

    /// <summary>
    /// Gets the lifecycle status of a transaction (active, revoked, or superseded).
    /// </summary>
    /// <param name="registerId">The register containing the transaction.</param>
    /// <param name="transactionId">The transaction to query.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The transaction's lifecycle status, or a NotFound result if it does not exist.</returns>
    [McpServerTool(Name = ToolName)]
    [Description("Returns the lifecycle status of a single ledger transaction — Active, Revoked, or Superseded — by checking for any revocation transaction that references it, and surfaces the revocation transaction id, superseding transaction id, revocation reason, and revoked-at timestamp when applicable. Call this whenever a credential or workflow action's continued validity matters: before honouring it, before issuing a verification bundle, or when auditing why a transaction is no longer in force. This reports only the active/revoked/superseded lifecycle; use sorcha_transaction_inclusion_proof to prove the transaction was sealed into a docket, and sorcha_transaction_verification_bundle to export an offline-verifiable package.")]
    public async Task<TransactionStatusResult> GetStatusAsync(
        [Description("The register ID containing the transaction")] string registerId,
        [Description("The transaction ID to query")] string transactionId,
        CancellationToken cancellationToken = default)
    {
        if (!_authService.CanInvokeTool(ToolName))
        {
            return new TransactionStatusResult
            {
                Status = "Unauthorized",
                Message = "Access denied. This tool requires the sorcha:admin role.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        if (string.IsNullOrWhiteSpace(registerId) || string.IsNullOrWhiteSpace(transactionId))
        {
            return new TransactionStatusResult
            {
                Status = "Error",
                Message = "Both register ID and transaction ID are required.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        if (!_availabilityTracker.IsServiceAvailable(ServiceName))
        {
            return new TransactionStatusResult
            {
                Status = "Unavailable",
                Message = "Register service is currently unavailable. Please try again later.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        _logger.LogInformation("Getting status for transaction {TransactionId} on register {RegisterId}", transactionId, registerId);

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var status = await _registerClient.GetTransactionStatusAsync(registerId, transactionId, cancellationToken);
            stopwatch.Stop();
            _availabilityTracker.RecordSuccess(ServiceName);

            if (status is null)
            {
                return new TransactionStatusResult
                {
                    Status = "NotFound",
                    Message = $"Transaction '{transactionId}' was not found in register '{registerId}'.",
                    CheckedAt = DateTimeOffset.UtcNow,
                    ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
                };
            }

            return new TransactionStatusResult
            {
                Status = "Success",
                Message = $"Transaction '{transactionId}' is {status.Status}.",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds,
                Lifecycle = status
            };
        }
        catch (TaskCanceledException)
        {
            stopwatch.Stop();
            _availabilityTracker.RecordFailure(ServiceName);
            _logger.LogWarning("Status query timed out for transaction {TransactionId}", transactionId);
            return new TransactionStatusResult
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
            _logger.LogError(ex, "Failed to get status for transaction {TransactionId}", transactionId);
            return new TransactionStatusResult
            {
                Status = "Error",
                Message = $"Failed to get transaction status: {ex.Message}",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
            };
        }
    }
}

/// <summary>
/// Result of a transaction lifecycle-status query.
/// </summary>
public sealed record TransactionStatusResult
{
    /// <summary>Status: Success, NotFound, Error, Unavailable, Timeout, or Unauthorized.</summary>
    public required string Status { get; init; }

    /// <summary>Human-readable message about the result.</summary>
    public required string Message { get; init; }

    /// <summary>When the query was performed.</summary>
    public required DateTimeOffset CheckedAt { get; init; }

    /// <summary>Response time in milliseconds.</summary>
    public int ResponseTimeMs { get; init; }

    /// <summary>The transaction's lifecycle status detail (on success).</summary>
    public TransactionStatusResponse? Lifecycle { get; init; }
}
