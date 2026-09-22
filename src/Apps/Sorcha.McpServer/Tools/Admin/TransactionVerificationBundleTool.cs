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
    /// <returns>The verification bundle, or NotFound/Refused carrying the Register Service's own status and reason.</returns>
    [McpServerTool(Name = ToolName)]
    [Description("Assembles a portable offline verification bundle for a sealed transaction, containing everything a third party needs to verify it without contacting the register: the credential/payload, the signed receipt with its embedded Merkle inclusion proof, a point-in-time revocation-status snapshot, and the validator public-key references. Call this when an operator needs to hand a credential's proof of authenticity, ledger inclusion, and current revocation state to an external verifier or archive it for later audit. This is the all-in-one superset of sorcha_transaction_inclusion_proof (proof only) and sorcha_transaction_status (lifecycle only); returns NotFound only when the transaction genuinely does not exist (HTTP 404), and Refused with the register's own explanation for any other refusal (e.g. HTTP 409 when the transaction is not yet sealed) — the two are different situations and are not collapsed together.")]
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
            var outcome = await _registerClient.GetVerificationBundleAsync(registerId, transactionId, cancellationToken);
            stopwatch.Stop();
            _availabilityTracker.RecordSuccess(ServiceName);

            if (outcome?.Bundle is { } bundle)
            {
                return new TransactionVerificationBundleResult
                {
                    Status = "Success",
                    Message = $"Verification bundle exported for transaction '{transactionId}'.",
                    CheckedAt = DateTimeOffset.UtcNow,
                    ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds,
                    Bundle = bundle
                };
            }

            // #1680: the Register Service refused (e.g. 404 no such transaction, or 409 not sealed
            // yet) and said why in its body. Surface the real status and reason verbatim rather than
            // reporting every non-success case as "NotFound … not yet sealed" — a fabricated
            // explanation that reads as transient and invites blind retries.
            if (outcome?.Refusal is { } refusal)
            {
                var status = refusal.StatusCode == 404 ? "NotFound" : "Refused";
                var reason = string.IsNullOrWhiteSpace(refusal.Reason)
                    ? "no reason was given"
                    : refusal.Reason;

                return new TransactionVerificationBundleResult
                {
                    Status = status,
                    Message = $"No verification bundle for transaction '{transactionId}': the Register "
                        + $"Service answered HTTP {refusal.StatusCode} — {reason}.",
                    CheckedAt = DateTimeOffset.UtcNow,
                    ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
                };
            }

            return new TransactionVerificationBundleResult
            {
                Status = "Error",
                Message = $"Failed to export verification bundle for transaction '{transactionId}': "
                    + "the Register Service's response could not be read.",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
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
