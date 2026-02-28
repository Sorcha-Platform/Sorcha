// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Sorcha.McpServer.Infrastructure;
using Sorcha.McpServer.Services;

namespace Sorcha.McpServer.Tools.Participant;

/// <summary>
/// Participant tool for getting wallet information.
/// </summary>
[McpServerToolType]
public sealed class WalletInfoTool
{
    private readonly IMcpSessionService _sessionService;
    private readonly IMcpAuthorizationService _authService;
    private readonly IMcpErrorHandler _errorHandler;
    private readonly IServiceAvailabilityTracker _availabilityTracker;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<WalletInfoTool> _logger;
    private readonly string _walletServiceEndpoint;

    public WalletInfoTool(
        IMcpSessionService sessionService,
        IMcpAuthorizationService authService,
        IMcpErrorHandler errorHandler,
        IServiceAvailabilityTracker availabilityTracker,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<WalletInfoTool> logger)
    {
        _sessionService = sessionService;
        _authService = authService;
        _errorHandler = errorHandler;
        _availabilityTracker = availabilityTracker;
        _httpClientFactory = httpClientFactory;
        _logger = logger;

        _walletServiceEndpoint = configuration["ServiceClients:WalletService:Address"] ?? "http://localhost:5001";
    }

    /// <summary>
    /// Gets information about the user's wallet.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Wallet information.</returns>
    [McpServerTool(Name = "sorcha_wallet_info")]
    [Description("Get information about your wallet including addresses and key types. Useful for verifying your signing identity.")]
    public async Task<WalletInfoResult> GetWalletInfoAsync(
        CancellationToken cancellationToken = default)
    {
        // Authorization check
        if (!_authService.CanInvokeTool("sorcha_wallet_info"))
        {
            return new WalletInfoResult
            {
                Status = "Unauthorized",
                Message = "Access denied. This tool requires the sorcha:participant role.",
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

        _logger.LogInformation("Getting wallet info");

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(30);

            var url = $"{_walletServiceEndpoint.TrimEnd('/')}/api/wallet/info";

            var response = await client.GetAsync(url, cancellationToken);

            stopwatch.Stop();

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning("Wallet info request failed: HTTP {StatusCode} - {Error}", response.StatusCode, errorContent);

                _availabilityTracker.RecordSuccess("Wallet");

                try
                {
                    var errorResponse = JsonSerializer.Deserialize<ErrorResponse>(errorContent, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    return new WalletInfoResult
                    {
                        Status = "Error",
                        Message = errorResponse?.Error ?? "Failed to retrieve wallet info.",
                        CheckedAt = DateTimeOffset.UtcNow,
                        ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
                    };
                }
                catch (JsonException)
                {
                    return new WalletInfoResult
                    {
                        Status = "Error",
                        Message = $"Request failed with status {(int)response.StatusCode}.",
                        CheckedAt = DateTimeOffset.UtcNow,
                        ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
                    };
                }
            }

            // Record success
            _availabilityTracker.RecordSuccess("Wallet");

            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
            var result = JsonSerializer.Deserialize<WalletInfoResponse>(responseContent, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (result == null)
            {
                return new WalletInfoResult
                {
                    Status = "Error",
                    Message = "Failed to parse wallet info response.",
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
                    WalletId = result.WalletId ?? "",
                    PrimaryAddress = result.PrimaryAddress ?? "",
                    Algorithm = result.Algorithm ?? "ED25519",
                    PublicKey = result.PublicKey,
                    CreatedAt = result.CreatedAt,
                    Addresses = result.Addresses?.Select(a => new WalletAddress
                    {
                        Address = a.Address ?? "",
                        DerivationPath = a.DerivationPath,
                        Algorithm = a.Algorithm ?? "ED25519",
                        IsDefault = a.IsDefault
                    }).ToList() ?? []
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

    // Internal response models
    private sealed class WalletInfoResponse
    {
        public string? WalletId { get; set; }
        public string? PrimaryAddress { get; set; }
        public string? Algorithm { get; set; }
        public string? PublicKey { get; set; }
        public DateTimeOffset? CreatedAt { get; set; }
        public List<WalletAddressDto>? Addresses { get; set; }
    }

    private sealed class WalletAddressDto
    {
        public string? Address { get; set; }
        public string? DerivationPath { get; set; }
        public string? Algorithm { get; set; }
        public bool IsDefault { get; set; }
    }

    private sealed class ErrorResponse
    {
        public string? Error { get; set; }
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
