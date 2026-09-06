// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Sorcha.McpServer.Infrastructure;
using Sorcha.McpServer.Services;
using Sorcha.ServiceClients.Wallet;

namespace Sorcha.McpServer.Tools.Participant;

/// <summary>
/// Participant tool for getting wallet information. Reads via the typed
/// <see cref="IWalletServiceClient"/> (spec 139 US4) so the caller's bearer is forwarded
/// and the route is contract-pinned, not hand-rolled.
/// </summary>
[McpServerToolType]
public sealed class WalletInfoTool
{
    private readonly IMcpAuthorizationService _authService;
    private readonly IServiceAvailabilityTracker _availabilityTracker;
    private readonly IWalletServiceClient _walletClient;
    private readonly ILogger<WalletInfoTool> _logger;

    public WalletInfoTool(
        IMcpAuthorizationService authService,
        IServiceAvailabilityTracker availabilityTracker,
        IWalletServiceClient walletClient,
        ILogger<WalletInfoTool> logger)
    {
        _authService = authService;
        _availabilityTracker = availabilityTracker;
        _walletClient = walletClient;
        _logger = logger;
    }

    /// <summary>
    /// Gets the public metadata for a wallet by its address.
    /// </summary>
    /// <param name="walletAddress">The wallet address to inspect (required).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Wallet information.</returns>
    [McpServerTool(Name = "sorcha_wallet_info")]
    [Description("Return the public metadata for a Sorcha wallet identified by its address: the wallet name, primary public address, key algorithm (ED25519, P-256, RSA-4096), public key, and status. No private key material or mnemonic is exposed — only the public surface needed to identify the signing wallet. A walletAddress is required. Call this when an agent needs to confirm which signing identity an address belongs to or inspect a wallet's algorithm and status before relying on it; use sorcha_transaction_history rather than this tool when you want the wallet's activity rather than its identity.")]
    public async Task<WalletInfoResult> GetWalletInfoAsync(
        [Description("The wallet address to inspect (required)")] string walletAddress,
        CancellationToken cancellationToken = default)
    {
        // Authorization check
        if (!_authService.CanInvokeTool("sorcha_wallet_info"))
        {
            return new WalletInfoResult
            {
                Status = "Unauthorized",
                Message = "Access denied. This tool requires an authenticated consumer- or platform-tier caller.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        // Validate input
        if (string.IsNullOrWhiteSpace(walletAddress))
        {
            return new WalletInfoResult
            {
                Status = "Error",
                Message = "Wallet address is required.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        // Check service availability
        if (!_availabilityTracker.IsServiceAvailable("Wallet"))
        {
            return new WalletInfoResult
            {
                Status = "Unavailable",
                Message = "Wallet service is currently unavailable. Please try again later.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        _logger.LogInformation("Getting wallet info for {WalletAddress}", walletAddress);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var wallet = await _walletClient.GetWalletAsync(walletAddress, cancellationToken);

            stopwatch.Stop();
            _availabilityTracker.RecordSuccess("Wallet");

            if (wallet == null)
            {
                return new WalletInfoResult
                {
                    Status = "NotFound",
                    Message = $"Wallet '{walletAddress}' was not found.",
                    CheckedAt = DateTimeOffset.UtcNow,
                    ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
                };
            }

            _logger.LogInformation(
                "Retrieved wallet info in {ElapsedMs}ms",
                stopwatch.ElapsedMilliseconds);

            return new WalletInfoResult
            {
                Status = "Success",
                Message = "Wallet information retrieved successfully.",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds,
                Wallet = new WalletInfo
                {
                    WalletId = wallet.Name,
                    PrimaryAddress = wallet.Address,
                    Algorithm = wallet.Algorithm,
                    PublicKey = wallet.PublicKey,
                    StatusValue = wallet.Status,
                    CreatedAt = wallet.CreatedAt,
                    Addresses =
                    [
                        new WalletAddress
                        {
                            Address = wallet.Address,
                            DerivationPath = null,
                            Algorithm = wallet.Algorithm,
                            IsDefault = true
                        }
                    ]
                }
            };
        }
        catch (TaskCanceledException)
        {
            stopwatch.Stop();
            _availabilityTracker.RecordFailure("Wallet");

            return new WalletInfoResult
            {
                Status = "Timeout",
                Message = "Request to wallet service timed out.",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
            };
        }
        catch (HttpRequestException ex)
        {
            stopwatch.Stop();
            _availabilityTracker.RecordFailure("Wallet", ex);

            return new WalletInfoResult
            {
                Status = "Error",
                Message = $"Failed to connect to wallet service: {ex.Message}",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _availabilityTracker.RecordFailure("Wallet", ex);

            _logger.LogError(ex, "Unexpected error getting wallet info");

            return new WalletInfoResult
            {
                Status = "Error",
                Message = "An unexpected error occurred while getting wallet info.",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
            };
        }
    }
}

/// <summary>
/// Result of getting wallet info.
/// </summary>
public sealed record WalletInfoResult
{
    /// <summary>
    /// Operation status: Success, Error, Unavailable, Timeout, or Unauthorized.
    /// </summary>
    public required string Status { get; init; }

    /// <summary>
    /// Human-readable message about the operation result.
    /// </summary>
    public required string Message { get; init; }

    /// <summary>
    /// When the operation was performed.
    /// </summary>
    public required DateTimeOffset CheckedAt { get; init; }

    /// <summary>
    /// Response time in milliseconds.
    /// </summary>
    public int ResponseTimeMs { get; init; }

    /// <summary>
    /// The wallet information.
    /// </summary>
    public WalletInfo? Wallet { get; init; }
}

/// <summary>
/// Wallet information.
/// </summary>
public sealed record WalletInfo
{
    /// <summary>
    /// The wallet ID.
    /// </summary>
    public required string WalletId { get; init; }

    /// <summary>
    /// The primary wallet address.
    /// </summary>
    public required string PrimaryAddress { get; init; }

    /// <summary>
    /// The cryptographic algorithm used (ED25519, P256, RSA4096).
    /// </summary>
    public required string Algorithm { get; init; }

    /// <summary>
    /// The public key (base64 encoded).
    /// </summary>
    public string? PublicKey { get; init; }

    /// <summary>
    /// Wallet status (Active, Revoked, etc.).
    /// </summary>
    public string? StatusValue { get; init; }

    /// <summary>
    /// When the wallet was created.
    /// </summary>
    public DateTimeOffset? CreatedAt { get; init; }

    /// <summary>
    /// List of wallet addresses.
    /// </summary>
    public IReadOnlyList<WalletAddress> Addresses { get; init; } = [];
}

/// <summary>
/// A wallet address.
/// </summary>
public sealed record WalletAddress
{
    /// <summary>
    /// The address string.
    /// </summary>
    public required string Address { get; init; }

    /// <summary>
    /// The BIP44 derivation path.
    /// </summary>
    public string? DerivationPath { get; init; }

    /// <summary>
    /// The cryptographic algorithm.
    /// </summary>
    public required string Algorithm { get; init; }

    /// <summary>
    /// Whether this is the default address.
    /// </summary>
    public bool IsDefault { get; init; }
}
