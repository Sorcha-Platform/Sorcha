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
/// Administrator tool exporting a portable offline verification bundle for a sealed transaction.
/// Routes through the typed <see cref="IRegisterServiceClient"/>.
/// </summary>
[McpServerToolType]
public sealed class TransactionVerificationBundleTool
{
    private const string ToolName = "sorcha_transaction_verification_bundle";
    private const string ServiceName = "Register";

    private readonly IMcpAuthorizationService _authService;
    private readonly IServiceAvailabilityTracker _availabilityTracker;
    private readonly IRegisterServiceClient _registerClient;
    private readonly ILogger<TransactionVerificationBundleTool> _logger;

    public TransactionVerificationBundleTool(
        IMcpAuthorizationService authService,
        IServiceAvailabilityTracker availabilityTracker,
        IRegisterServiceClient registerClient,
        ILogger<TransactionVerificationBundleTool> logger)
    {
        _authService = authService;
        _availabilityTracker = availabilityTracker;
        _registerClient = registerClient;
        _logger = logger;
    }

    /// <summary>
    /// Exports a portable offline verification bundle for a sealed transaction.
    /// </summary>
    /// <param name="registerId">The register containing the transaction.</param>
    /// <param name="transactionId">The transaction to export a bundle for.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The verification bundle, or a NotFound result if missing or not yet sealed.</returns>
    [McpServerTool(Name = ToolName)]
    [Description("Assembles a portable offline verification bundle for a sealed transaction, containing everything a third party needs to verify it without contacting the register: the credential/payload, the signed receipt with its embedded Merkle inclusion proof, a point-in-time revocation-status snapshot, and the validator public-key references. Call this when an operator needs to hand a credential's proof of authenticity, ledger inclusion, and current revocation state to an external verifier or archive it for later audit. This is the all-in-one superset of sorcha_transaction_inclusion_proof (proof only) and sorcha_transaction_status (lifecycle only); returns NotFound when the transaction does not exist or has not yet been sealed (a receipt is required to build the bundle).")]
    public async Task<TransactionVerificationBundleResult> GetBundleAsync(
        [Description("The register ID containing the transaction")] string registerId,
        [Description("The transaction ID to export a bundle for")] string transactionId,
        CancellationToken cancellationToken = default)
    {
        if (!_authService.CanInvokeTool(ToolName))
        {
            return new TransactionVerificationBundleResult
            {
                Status = "Unauthorized",
                Message = "Access denied. This tool requires the sorcha:admin role.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        if (string.IsNullOrWhiteSpace(registerId) || string.IsNullOrWhiteSpace(transactionId))
        {
            return new TransactionVerificationBundleResult
            {
                Status = "Error",
                Message = "Both register ID and transaction ID are required.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        if (!_availabilityTracker.IsServiceAvailable(ServiceName))
        {
            return new TransactionVerificationBundleResult
            {
                Status = "Unavailable",
                Message = "Register service is currently unavailable. Please try again later.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        _logger.LogInformation("Exporting verification bundle for transaction {TransactionId} on register {RegisterId}", transactionId, registerId);

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var bundle = await _registerClient.GetVerificationBundleAsync(registerId, transactionId, cancellationToken);
            stopwatch.Stop();
            _availabilityTracker.RecordSuccess(ServiceName);

            if (bundle is null)
            {
                return new TransactionVerificationBundleResult
                {
                    Status = "NotFound",
                    Message = $"No verification bundle available for transaction '{transactionId}' (not found or not yet sealed).",
                    CheckedAt = DateTimeOffset.UtcNow,
                    ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
                };
            }

            return new TransactionVerificationBundleResult
            {
                Status = "Success",
                Message = $"Verification bundle exported for transaction '{transactionId}'.",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds,
                Bundle = bundle
            };
        }
        catch (TaskCanceledException)
        {
            stopwatch.Stop();
            _availabilityTracker.RecordFailure(ServiceName);
            _logger.LogWarning("Verification-bundle query timed out for transaction {TransactionId}", transactionId);
            return new TransactionVerificationBundleResult
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
            _logger.LogError(ex, "Failed to export verification bundle for transaction {TransactionId}", transactionId);
            return new TransactionVerificationBundleResult
            {
                Status = "Error",
                Message = $"Failed to export verification bundle: {ex.Message}",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
            };
        }
    }
}

/// <summary>
/// Result of a verification-bundle export.
/// </summary>
public sealed record TransactionVerificationBundleResult
{
    /// <summary>Status: Success, NotFound, Error, Unavailable, Timeout, or Unauthorized.</summary>
    public required string Status { get; init; }

    /// <summary>Human-readable message about the result.</summary>
    public required string Message { get; init; }

    /// <summary>When the query was performed.</summary>
    public required DateTimeOffset CheckedAt { get; init; }

    /// <summary>Response time in milliseconds.</summary>
    public int ResponseTimeMs { get; init; }

    /// <summary>The portable verification bundle (on success).</summary>
    public VerificationBundle? Bundle { get; init; }
}
