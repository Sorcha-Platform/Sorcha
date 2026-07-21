// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Sorcha.ServiceClients.Auth;
using Sorcha.ServiceClients.Helpers;

namespace Sorcha.ServiceClients.Validator;

/// <summary>
/// HTTP client for Validator Service operations
/// </summary>
public class ValidatorServiceClient : IValidatorServiceClient
{
    private readonly HttpClient _httpClient;
    private readonly IServiceAuthClient _serviceAuth;
    private readonly ILogger<ValidatorServiceClient> _logger;
    private readonly string _serviceAddress;

    public ValidatorServiceClient(
        HttpClient httpClient,
        IConfiguration configuration,
        IServiceAuthClient serviceAuth,
        ILogger<ValidatorServiceClient> logger)
    {
        _serviceAuth = serviceAuth ?? throw new ArgumentNullException(nameof(serviceAuth));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));

        _serviceAddress = configuration["ServiceClients:ValidatorService:Address"]
            ?? throw new InvalidOperationException("Validator Service address not configured");

        _httpClient.BaseAddress = new Uri(_serviceAddress);

        _logger.LogInformation(
            "ValidatorServiceClient initialized (Address: {Address})",
            _serviceAddress);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        // CRITICAL: Use relaxed encoding so characters like '+' in base64 and DateTimeOffset
        // are not escaped to \u002B during serialization. The Validator computes payload hashes
        // from GetRawText() — any escaping changes the bytes and breaks hash verification.
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>
    /// Submits an action transaction to the Validator Service for validation and mempool inclusion
    /// </summary>
    public async Task<TransactionSubmissionResult> SubmitTransactionAsync(
        TransactionSubmission request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation(
                "Submitting action transaction {TransactionId} for register {RegisterId} to Validator Service",
                request.TransactionId, request.RegisterId);

            await SetAuthHeaderAsync(cancellationToken);

            var response = await _httpClient.PostAsJsonAsync(
                "/api/v1/transactions/validate",
                request,
                JsonOptions,
                cancellationToken);

            if (response.StatusCode == HttpStatusCode.OK)
            {
                var result = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions, cancellationToken);

                _logger.LogInformation(
                    "Action transaction {TransactionId} validated and added to mempool",
                    request.TransactionId);

                return new TransactionSubmissionResult
                {
                    Success = true,
                    TransactionId = result.GetProperty("transactionId").GetString() ?? request.TransactionId,
                    RegisterId = result.GetProperty("registerId").GetString() ?? request.RegisterId,
                    AddedAt = result.TryGetProperty("addedAt", out var addedAt)
                        ? addedAt.GetDateTimeOffset()
                        : DateTimeOffset.UtcNow
                };
            }

            if (response.StatusCode == HttpStatusCode.BadRequest)
            {
                var content = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning(
                    "Action transaction {TransactionId} failed validation: {Content}",
                    request.TransactionId, content);

                return new TransactionSubmissionResult
                {
                    Success = false,
                    TransactionId = request.TransactionId,
                    RegisterId = request.RegisterId,
                    ErrorCode = "VALIDATION_FAILED",
                    ErrorMessage = content
                };
            }

            if (response.StatusCode == HttpStatusCode.Conflict)
            {
                var content = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning(
                    "Action transaction {TransactionId} rejected (conflict): {Content}",
                    request.TransactionId, content);

                return new TransactionSubmissionResult
                {
                    Success = false,
                    TransactionId = request.TransactionId,
                    RegisterId = request.RegisterId,
                    ErrorCode = "MEMPOOL_FULL",
                    ErrorMessage = content
                };
            }

            // Unexpected status code
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError(
                "Unexpected response {StatusCode} submitting transaction {TransactionId}: {Content}",
                response.StatusCode, request.TransactionId, body);

            return new TransactionSubmissionResult
            {
                Success = false,
                TransactionId = request.TransactionId,
                RegisterId = request.RegisterId,
                ErrorCode = "HTTP_ERROR",
                ErrorMessage = $"Unexpected status code: {response.StatusCode}"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error submitting action transaction {TransactionId} for register {RegisterId}",
                request.TransactionId, request.RegisterId);

            return new TransactionSubmissionResult
            {
                Success = false,
                TransactionId = request.TransactionId,
                RegisterId = request.RegisterId,
                ErrorCode = "HTTP_ERROR",
                ErrorMessage = ex.Message
            };
        }
    }

    /// <summary>
    /// Gets the next sequence number for a wallet on a register (replay protection)
    /// </summary>
    public async Task<long> GetNextSequenceNumberAsync(
        string registerId,
        string walletAddress,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug(
                "Fetching next sequence number for wallet {WalletAddress} on register {RegisterId}",
                walletAddress, registerId);

            await SetAuthHeaderAsync(cancellationToken);

            var response = await _httpClient.GetAsync(
                $"/api/validators/{Uri.EscapeDataString(registerId)}/sequence/{Uri.EscapeDataString(walletAddress)}",
                cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions, cancellationToken);
                var nextSeq = result.GetProperty("nextSequenceNumber").GetInt64();

                _logger.LogDebug(
                    "Next sequence number for wallet {WalletAddress} on register {RegisterId}: {SequenceNumber}",
                    walletAddress, registerId, nextSeq);

                return nextSeq;
            }

            _logger.LogWarning(
                "Failed to get sequence number for wallet {WalletAddress} on register {RegisterId} (status: {StatusCode}). Defaulting to 1.",
                walletAddress, registerId, response.StatusCode);
            return 1;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Error fetching sequence number for wallet {WalletAddress} on register {RegisterId}. Defaulting to 1.",
                walletAddress, registerId);
            return 1;
        }
    }

    /// <summary>
    /// Starts the validator for a register (Feature 140 Wave 4 — admin orchestration).
    /// </summary>
    public async Task<bool> StartValidatorAsync(
        string registerId,
        CancellationToken cancellationToken = default)
    {
        await SetAuthHeaderAsync(cancellationToken);

        var response = await _httpClient.PostAsJsonAsync(
            "/api/admin/validators/start",
            new { registerId },
            JsonOptions,
            cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            return true;
        }

        _logger.LogWarning(
            "Start validator for register {RegisterId} failed: {StatusCode}",
            registerId, response.StatusCode);
        return false;
    }

    /// <summary>
    /// Stops the validator for a register (Feature 140 Wave 4 — admin orchestration).
    /// </summary>
    public async Task<bool> StopValidatorAsync(
        string registerId,
        bool persistMemPool,
        CancellationToken cancellationToken = default)
    {
        await SetAuthHeaderAsync(cancellationToken);

        var response = await _httpClient.PostAsJsonAsync(
            "/api/admin/validators/stop",
            new { registerId, persistMemPool },
            JsonOptions,
            cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            return true;
        }

        _logger.LogWarning(
            "Stop validator for register {RegisterId} failed: {StatusCode}",
            registerId, response.StatusCode);
        return false;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<OperationalValidatorSummary>> GetOperationalValidatorsAsync(
        string registerId,
        CancellationToken cancellationToken = default)
    {
        await SetAuthHeaderAsync(cancellationToken);

        var response = await _httpClient.GetAsync(
            $"/api/validators/{Uri.EscapeDataString(registerId)}",
            cancellationToken);

        // Do NOT swallow a failure to an empty list — see IValidatorServiceClient remarks. An
        // unreachable Validator Service must surface as a fault so the caller can say "unknown",
        // not silently report "no validators online".
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions, cancellationToken);

        var result = new List<OperationalValidatorSummary>();
        if (payload.TryGetProperty("validators", out var validators)
            && validators.ValueKind == JsonValueKind.Array)
        {
            foreach (var v in validators.EnumerateArray())
            {
                result.Add(new OperationalValidatorSummary
                {
                    ValidatorId = v.TryGetProperty("validatorId", out var id) ? id.GetString() ?? string.Empty : string.Empty,
                    PublicKey = v.TryGetProperty("publicKey", out var pk) ? pk.GetString() : null,
                    GrpcEndpoint = v.TryGetProperty("grpcEndpoint", out var ep) ? ep.GetString() : null,
                    Status = v.TryGetProperty("status", out var st) ? st.GetString() : null,
                    RegisteredAt = v.TryGetProperty("registeredAt", out var ra) && ra.ValueKind != JsonValueKind.Null
                        ? ra.GetDateTimeOffset() : null,
                    OrderIndex = v.TryGetProperty("orderIndex", out var oi) && oi.ValueKind == JsonValueKind.Number
                        ? oi.GetInt32() : null,
                });
            }
        }

        return result;
    }

    private Task SetAuthHeaderAsync(CancellationToken cancellationToken) =>
        ServiceClientAuthHelper.SetAuthHeaderAsync(
            _httpClient, _serviceAuth, _logger, "Validator Service", cancellationToken);
}
