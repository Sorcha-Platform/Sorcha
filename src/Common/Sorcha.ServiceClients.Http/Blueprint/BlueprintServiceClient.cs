// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Sorcha.ServiceClients.Auth;
using Sorcha.ServiceClients.Helpers;

namespace Sorcha.ServiceClients.Blueprint;

/// <summary>
/// HTTP client for Blueprint Service operations
/// </summary>
public class BlueprintServiceClient : IBlueprintServiceClient
{
    private readonly HttpClient _httpClient;
    private readonly IServiceAuthClient _serviceAuth;
    private readonly ILogger<BlueprintServiceClient> _logger;
    private readonly string _serviceAddress;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public BlueprintServiceClient(
        HttpClient httpClient,
        IServiceAuthClient serviceAuth,
        IConfiguration configuration,
        ILogger<BlueprintServiceClient> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _serviceAuth = serviceAuth ?? throw new ArgumentNullException(nameof(serviceAuth));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _serviceAddress = configuration["ServiceClients:BlueprintService:Address"]
            ?? configuration["GrpcClients:BlueprintService:Address"]
            ?? throw new InvalidOperationException("Blueprint Service address not configured");

        // Configure HttpClient base address
        if (_httpClient.BaseAddress == null)
        {
            _httpClient.BaseAddress = new Uri(_serviceAddress.TrimEnd('/') + "/");
        }

        _logger.LogInformation("BlueprintServiceClient initialized (Address: {Address})", _serviceAddress);
    }

    public async Task<string?> GetBlueprintAsync(
        string blueprintId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Getting blueprint {BlueprintId}", blueprintId);

            await SetAuthHeaderAsync(cancellationToken);
            var response = await _httpClient.GetAsync(
                $"api/blueprints/{Uri.EscapeDataString(blueprintId)}",
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    _logger.LogDebug("Blueprint {BlueprintId} not found", blueprintId);
                    return null;
                }

                _logger.LogWarning(
                    "Failed to get blueprint {BlueprintId}: {StatusCode}",
                    blueprintId, response.StatusCode);
                return null;
            }

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogDebug("Retrieved blueprint {BlueprintId}", blueprintId);
            return content;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP error getting blueprint {BlueprintId}", blueprintId);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get blueprint {BlueprintId}", blueprintId);
            return null;
        }
    }

    public async Task<bool> ValidatePayloadAsync(
        string blueprintId,
        string actionId,
        string payload,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug(
                "Validating payload for blueprint {BlueprintId} action {ActionId}",
                blueprintId, actionId);

            var request = new ValidateRequest
            {
                BlueprintId = blueprintId,
                ActionId = actionId,
                Data = JsonSerializer.Deserialize<JsonElement>(payload)
            };

            await SetAuthHeaderAsync(cancellationToken);
            var response = await _httpClient.PostAsJsonAsync(
                "api/execution/validate",
                request,
                JsonOptions,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Validation failed for blueprint {BlueprintId} action {ActionId}: {StatusCode}",
                    blueprintId, actionId, response.StatusCode);
                return false;
            }

            var result = await response.Content.ReadFromJsonAsync<ValidateResponse>(JsonOptions, cancellationToken);

            if (result?.IsValid == true)
            {
                _logger.LogDebug(
                    "Payload valid for blueprint {BlueprintId} action {ActionId}",
                    blueprintId, actionId);
                return true;
            }

            _logger.LogDebug(
                "Payload invalid for blueprint {BlueprintId} action {ActionId}: {Errors}",
                blueprintId, actionId, string.Join(", ", result?.Errors ?? []));
            return false;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(
                ex,
                "HTTP error validating payload for blueprint {BlueprintId} action {ActionId}",
                blueprintId, actionId);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to validate payload for blueprint {BlueprintId} action {ActionId}",
                blueprintId, actionId);
            return false;
        }
    }

    public async Task<FileChunkSubmissionResult?> SubmitFileChunkAsync(
        string senderWallet,
        string registerAddress,
        int chunkIndex,
        int totalChunks,
        string fileHash,
        string contentType,
        byte[] chunkContent,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug(
                "Submitting file chunk {ChunkIndex}/{TotalChunks} for register {RegisterAddress}",
                chunkIndex, totalChunks, registerAddress);

            var request = new FileChunkSubmissionRequest
            {
                SenderWallet = senderWallet,
                RegisterAddress = registerAddress,
                ChunkIndex = chunkIndex,
                TotalChunks = totalChunks,
                FileHash = fileHash,
                ContentType = contentType,
                ContentBase64 = Convert.ToBase64String(chunkContent)
            };

            await SetAuthHeaderAsync(cancellationToken);
            var response = await _httpClient.PostAsJsonAsync(
                "api/file-chunks",
                request,
                JsonOptions,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Failed to submit file chunk {ChunkIndex}/{TotalChunks} for register {RegisterAddress}: {StatusCode}",
                    chunkIndex, totalChunks, registerAddress, response.StatusCode);
                return null;
            }

            var result = await response.Content.ReadFromJsonAsync<FileChunkSubmissionResponse>(JsonOptions, cancellationToken);

            if (result is null)
            {
                _logger.LogWarning(
                    "Empty response submitting file chunk {ChunkIndex}/{TotalChunks} for register {RegisterAddress}",
                    chunkIndex, totalChunks, registerAddress);
                return null;
            }

            _logger.LogDebug(
                "Submitted file chunk {ChunkIndex}/{TotalChunks} for register {RegisterAddress}, txId={TransactionId}",
                chunkIndex, totalChunks, registerAddress, result.ChunkTransactionId);

            return new FileChunkSubmissionResult(result.ChunkTransactionId, result.ChunkIndex, result.Timestamp);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(
                ex,
                "HTTP error submitting file chunk {ChunkIndex}/{TotalChunks} for register {RegisterAddress}",
                chunkIndex, totalChunks, registerAddress);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to submit file chunk {ChunkIndex}/{TotalChunks} for register {RegisterAddress}",
                chunkIndex, totalChunks, registerAddress);
            return null;
        }
    }

    private Task SetAuthHeaderAsync(CancellationToken cancellationToken) =>
        ServiceClientAuthHelper.SetAuthHeaderAsync(
            _httpClient, _serviceAuth, _logger, "BlueprintService", cancellationToken);

    /// <summary>
    /// Request DTO for validation endpoint
    /// </summary>
    private record ValidateRequest
    {
        public required string BlueprintId { get; init; }
        public required string ActionId { get; init; }
        public required JsonElement Data { get; init; }
    }

    /// <summary>
    /// Response DTO from validation endpoint
    /// </summary>
    private record ValidateResponse
    {
        public bool IsValid { get; init; }
        public List<string> Errors { get; init; } = [];
    }

    /// <summary>
    /// Request DTO for file chunk submission endpoint
    /// </summary>
    private record FileChunkSubmissionRequest
    {
        public required string SenderWallet { get; init; }
        public required string RegisterAddress { get; init; }
        public required int ChunkIndex { get; init; }
        public required int TotalChunks { get; init; }
        public required string FileHash { get; init; }
        public required string ContentType { get; init; }
        public required string ContentBase64 { get; init; }
    }

    /// <summary>
    /// Response DTO from file chunk submission endpoint
    /// </summary>
    private record FileChunkSubmissionResponse
    {
        public required string ChunkTransactionId { get; init; }
        public required int ChunkIndex { get; init; }
        public required DateTimeOffset Timestamp { get; init; }
    }
}
