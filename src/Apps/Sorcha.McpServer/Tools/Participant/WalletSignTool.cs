// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Sorcha.McpServer.Infrastructure;
using Sorcha.McpServer.Services;

namespace Sorcha.McpServer.Tools.Participant;

/// <summary>
/// Participant tool for signing data with the wallet.
/// </summary>
[McpServerToolType]
public sealed class WalletSignTool
{
    private readonly IMcpSessionService _sessionService;
    private readonly IMcpAuthorizationService _authService;
    private readonly IMcpErrorHandler _errorHandler;
    private readonly IServiceAvailabilityTracker _availabilityTracker;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<WalletSignTool> _logger;
    private readonly string _walletServiceEndpoint;

    public WalletSignTool(
        IMcpSessionService sessionService,
        IMcpAuthorizationService authService,
        IMcpErrorHandler errorHandler,
        IServiceAvailabilityTracker availabilityTracker,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<WalletSignTool> logger)
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
    /// Signs data with the user's wallet.
    /// </summary>
    /// <param name="dataToSign">The data to sign (can be a message or JSON).</param>
    /// <param name="addressIndex">The address index to use for signing (default: 0 for primary).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The signature.</returns>
    [McpServerTool(Name = "sorcha_wallet_sign")]
    [Description("Sign data with your wallet. Creates a cryptographic signature that proves you authorized the data. Used for action submissions and identity verification.")]
    public async Task<WalletSignResult> SignDataAsync(
        [Description("The data to sign (message or JSON)")] string dataToSign,
        [Description("Address index to use for signing (default: 0 for primary)")] int addressIndex = 0,
        CancellationToken cancellationToken = default)
    {
        // Authorization check
        if (!_authService.CanInvokeTool("sorcha_wallet_sign"))
        {
            return new WalletSignResult
            {
                Status = "Unauthorized",
                Message = "Access denied. This tool requires the sorcha:participant role.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        // Validate input
        if (string.IsNullOrWhiteSpace(dataToSign))
        {
            return new WalletSignResult
            {
                Status = "Error",
                Message = "Data to sign is required.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        if (addressIndex < 0)
        {
            return new WalletSignResult
            {
                Status = "Error",
                Message = "Address index must be 0 or greater.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        // Check service availability
        if (!_availabilityTracker.IsServiceAvailable("Wallet"))
        {
            return new WalletSignResult
            {
                Status = "Unavailable",
                Message = "Wallet service is currently unavailable. Please try again later.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        _logger.LogInformation("Signing data with address index {AddressIndex}", addressIndex);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(30);

            var url = $"{_walletServiceEndpoint.TrimEnd('/')}/api/wallet/sign";

            var requestBody = JsonSerializer.Serialize(new
            {
                data = dataToSign,
                addressIndex
            });

            var content = new StringContent(requestBody, Encoding.UTF8, "application/json");
            var response = await client.PostAsync(url, content, cancellationToken);

            stopwatch.Stop();

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning("Wallet sign request failed: HTTP {StatusCode} - {Error}", response.StatusCode, errorContent);

                _availabilityTracker.RecordSuccess("Wallet");

                try
                {
                    var errorResponse = JsonSerializer.Deserialize<ErrorResponse>(errorContent, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    return new WalletSignResult
                    {
                        Status = "Error",
                        Message = errorResponse?.Error ?? "Signing failed.",
                        CheckedAt = DateTimeOffset.UtcNow,
                        ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
                    };
                }
                catch (JsonException)
                {
                    return new WalletSignResult
                    {
                        Status = "Error",
                        Message = $"Signing request failed with status {(int)response.StatusCode}.",
                        CheckedAt = DateTimeOffset.UtcNow,
                        ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
                    };
                }
            }

            // Record success
            _availabilityTracker.RecordSuccess("Wallet");

            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
            var result = JsonSerializer.Deserialize<SignResponse>(responseContent, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (result == null)
            {
                return new WalletSignResult
                {
                    Status = "Error",
                    Message = "Failed to parse sign response.",
                    CheckedAt = DateTimeOffset.UtcNow,
                    ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
                };
            }

            _logger.LogInformation(
                "Data signed successfully in {ElapsedMs}ms",
                stopwatch.ElapsedMilliseconds);

            return new WalletSignResult
            {
                Status = "Success",
                Message = "Data signed successfully.",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds,
                Signature = result.Signature ?? "",
                SignatureFormat = result.SignatureFormat ?? "Base64",
                Algorithm = result.Algorithm ?? "ED25519",
                SignerAddress = result.SignerAddress ?? ""
            };
        }
        catch (TaskCanceledException)
        {
            stopwatch.Stop();
            _availabilityTracker.RecordFailure("Wallet");

            return new WalletSignResult
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

            return new WalletSignResult
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

            _logger.LogError(ex, "Unexpected error signing data");

            return new WalletSignResult
            {
                Status = "Error",
                Message = "An unexpected error occurred while signing data.",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
            };
        }
    }

    // Internal response models
    private sealed class SignResponse
    {
        public string? Signature { get; set; }
        public string? SignatureFormat { get; set; }
        public string? Algorithm { get; set; }
        public string? SignerAddress { get; set; }
    }

    private sealed class ErrorResponse
    {
        public string? Error { get; set; }
    }
}

/// <summary>
/// Result of signing data.
/// </summary>
public sealed record WalletSignResult
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
    /// The signature (base64 encoded).
    /// </summary>
    public string Signature { get; init; } = "";

    /// <summary>
    /// The signature format (Base64, Hex).
    /// </summary>
    public string SignatureFormat { get; init; } = "Base64";

    /// <summary>
    /// The algorithm used for signing.
    /// </summary>
    public string Algorithm { get; init; } = "ED25519";

    /// <summary>
    /// The address that created the signature.
    /// </summary>
    public string SignerAddress { get; init; } = "";
}
