// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Sorcha.Serialization;
using Sorcha.ServiceClients.Auth;
using Sorcha.ServiceClients.Blueprint.Models;
using Sorcha.ServiceClients.Configuration;
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

        _serviceAddress = SorchaServiceAddresses.TryResolve(configuration, SorchaService.Blueprint)
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

    /// <inheritdoc/>
    public async Task<string?> GetBlueprintDefinitionAsync(
        string blueprintId,
        string execDefHash,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(blueprintId);
        ArgumentException.ThrowIfNullOrWhiteSpace(execDefHash);

        try
        {
            _logger.LogDebug(
                "Getting pinned definition {ExecDefHash} of blueprint {BlueprintId}",
                execDefHash, blueprintId);

            await SetAuthHeaderAsync(cancellationToken);
            var response = await _httpClient.GetAsync(
                $"api/blueprints/{Uri.EscapeDataString(blueprintId)}/definitions/{Uri.EscapeDataString(execDefHash)}",
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    // Not an error condition worth shouting about here — the caller decides what an
                    // unresolvable pin means, and for the validator it is a typed refusal.
                    _logger.LogDebug(
                        "Pinned definition {ExecDefHash} of blueprint {BlueprintId} not found",
                        execDefHash, blueprintId);
                    return null;
                }

                _logger.LogWarning(
                    "Failed to get pinned definition {ExecDefHash} of blueprint {BlueprintId}: {StatusCode}",
                    execDefHash, blueprintId, response.StatusCode);
                return null;
            }

            return await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex,
                "HTTP error getting pinned definition {ExecDefHash} of blueprint {BlueprintId}",
                execDefHash, blueprintId);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to get pinned definition {ExecDefHash} of blueprint {BlueprintId}",
                execDefHash, blueprintId);
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
                Data = JsonSerializer.Deserialize<JsonElement>(payload, SorchaJson.Options)
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

            var result = await response.Content.ReadFromJsonAsync<ValidateResponse>(SorchaJson.Options, cancellationToken);

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

    public async Task<FileChunkRetrievalResult?> GetFileChunkAsync(
        string chunkId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Retrieving file chunk {ChunkId} from Blueprint Service", chunkId);

            await SetAuthHeaderAsync(cancellationToken);
            var response = await _httpClient.GetAsync(
                $"api/file-chunks/{Uri.EscapeDataString(chunkId)}",
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    _logger.LogDebug("File chunk {ChunkId} not found in Blueprint Service", chunkId);
                    return null;
                }

                _logger.LogWarning(
                    "Failed to retrieve file chunk {ChunkId}: {StatusCode}",
                    chunkId, response.StatusCode);
                return null;
            }

            var result = await response.Content.ReadFromJsonAsync<FileChunkRetrievalResponse>(SorchaJson.Options, cancellationToken);
            if (result is null)
            {
                _logger.LogWarning("Empty response retrieving file chunk {ChunkId}", chunkId);
                return null;
            }

            var encryptedContent = Convert.FromBase64String(result.ContentBase64);
            _logger.LogDebug(
                "Retrieved file chunk {ChunkId} ({Bytes} bytes encrypted)",
                chunkId, encryptedContent.Length);

            return new FileChunkRetrievalResult(chunkId, encryptedContent, result.ContentType);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP error retrieving file chunk {ChunkId}", chunkId);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve file chunk {ChunkId}", chunkId);
            return null;
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

            var result = await response.Content.ReadFromJsonAsync<FileChunkSubmissionResponse>(SorchaJson.Options, cancellationToken);

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

    // =========================================================================
    // Spec 139 US4 — MCP reconciliation reads/writes.
    // =========================================================================

    /// <inheritdoc />
    public Task<string?> ListBlueprintsAsync(
        int page = 1,
        int pageSize = 20,
        string? search = null,
        string? status = null,
        CancellationToken cancellationToken = default)
    {
        var query = new List<string> { $"page={page}", $"pageSize={pageSize}" };
        if (!string.IsNullOrWhiteSpace(search)) query.Add($"search={Uri.EscapeDataString(search)}");
        if (!string.IsNullOrWhiteSpace(status)) query.Add($"status={Uri.EscapeDataString(status)}");
        return GetRawAsync($"api/blueprints/?{string.Join("&", query)}", "list blueprints", cancellationToken);
    }

    /// <inheritdoc />
    public Task<string?> CreateBlueprintAsync(string blueprintJson, CancellationToken cancellationToken = default) =>
        SendRawAsync(HttpMethod.Post, "api/blueprints/", blueprintJson, "create blueprint", cancellationToken);

    /// <inheritdoc />
    public Task<string?> UpdateBlueprintAsync(string blueprintId, string blueprintJson, CancellationToken cancellationToken = default) =>
        SendRawAsync(HttpMethod.Put, $"api/blueprints/{Uri.EscapeDataString(blueprintId)}", blueprintJson, "update blueprint", cancellationToken);

    /// <inheritdoc />
    public Task<string?> GetBlueprintDiffAsync(string blueprintId, int fromVersion, int? toVersion = null, CancellationToken cancellationToken = default)
    {
        var url = $"api/blueprints/{Uri.EscapeDataString(blueprintId)}/diff?from={fromVersion}";
        if (toVersion is { } to && to > 0) url += $"&to={to}";
        return GetRawAsync(url, "blueprint diff", cancellationToken);
    }

    /// <inheritdoc />
    public Task<string?> SimulateRouteAsync(string requestJson, CancellationToken cancellationToken = default) =>
        SendRawAsync(HttpMethod.Post, "api/execution/route", requestJson, "simulate route", cancellationToken);

    /// <inheritdoc />
    public Task<string?> SimulateCalculateAsync(string requestJson, CancellationToken cancellationToken = default) =>
        SendRawAsync(HttpMethod.Post, "api/execution/calculate", requestJson, "simulate calculate", cancellationToken);

    /// <inheritdoc />
    public Task<string?> GetWorkflowInstancesAsync(string? queryString = null, CancellationToken cancellationToken = default)
    {
        var url = string.IsNullOrWhiteSpace(queryString) ? "api/workflows" : $"api/workflows?{queryString}";
        return GetRawAsync(url, "workflow instances", cancellationToken);
    }

    /// <inheritdoc />
    public Task<string?> GetWorkflowStatusAsync(string workflowInstanceId, CancellationToken cancellationToken = default) =>
        GetRawAsync($"api/workflows/{Uri.EscapeDataString(workflowInstanceId)}", "workflow status", cancellationToken);

    /// <inheritdoc />
    public Task<string?> GetActionDetailsAsync(string actionInstanceId, CancellationToken cancellationToken = default) =>
        GetRawAsync($"api/actions/{Uri.EscapeDataString(actionInstanceId)}", "action details", cancellationToken);

    /// <inheritdoc />
    public Task<string?> GetInboxAsync(string? queryString = null, CancellationToken cancellationToken = default)
    {
        var url = string.IsNullOrWhiteSpace(queryString) ? "api/inbox" : $"api/inbox?{queryString}";
        return GetRawAsync(url, "inbox", cancellationToken);
    }

    /// <inheritdoc />
    public Task<string?> GetDisclosedDataAsync(string workflowInstanceId, string? actionInstanceId = null, CancellationToken cancellationToken = default)
    {
        var url = string.IsNullOrWhiteSpace(actionInstanceId)
            ? $"api/workflows/{Uri.EscapeDataString(workflowInstanceId)}/disclosures"
            : $"api/workflows/{Uri.EscapeDataString(workflowInstanceId)}/actions/{Uri.EscapeDataString(actionInstanceId)}/disclosures";
        return GetRawAsync(url, "disclosed data", cancellationToken);
    }

    /// <inheritdoc />
    public Task<string?> ExecuteActionAsync(string instanceId, string actionId, string payloadJson, CancellationToken cancellationToken = default) =>
        SendRawAsync(
            HttpMethod.Post,
            $"api/instances/{Uri.EscapeDataString(instanceId)}/actions/{Uri.EscapeDataString(actionId)}/execute",
            payloadJson,
            "execute action",
            cancellationToken);

    // =========================================================================
    // Feature 140 Wave 2 — credential & presentation lifecycle MCP surface.
    // =========================================================================

    /// <inheritdoc />
    public Task<string?> GetPresentationStatusAsync(string presentationRequestId, CancellationToken cancellationToken = default) =>
        GetRawAsync($"api/presentations/{Uri.EscapeDataString(presentationRequestId)}/status", "presentation status", cancellationToken);

    /// <inheritdoc />
    public Task<string?> RevokeCredentialAsync(string credentialId, string issuerWallet, string? reason = null, CancellationToken cancellationToken = default) =>
        SendRawAsync(
            HttpMethod.Post,
            $"api/v1/credentials/{Uri.EscapeDataString(credentialId)}/revoke",
            JsonSerializer.Serialize(new { issuerWallet, reason }, JsonOptions),
            "revoke credential",
            cancellationToken);

    /// <inheritdoc />
    public Task<string?> SuspendCredentialAsync(string credentialId, string issuerWallet, string? reason = null, CancellationToken cancellationToken = default) =>
        SendRawAsync(
            HttpMethod.Post,
            $"api/v1/credentials/{Uri.EscapeDataString(credentialId)}/suspend",
            JsonSerializer.Serialize(new { issuerWallet, reason }, JsonOptions),
            "suspend credential",
            cancellationToken);

    /// <inheritdoc />
    public Task<string?> ReinstateCredentialAsync(string credentialId, string issuerWallet, string? reason = null, CancellationToken cancellationToken = default) =>
        SendRawAsync(
            HttpMethod.Post,
            $"api/v1/credentials/{Uri.EscapeDataString(credentialId)}/reinstate",
            JsonSerializer.Serialize(new { issuerWallet, reason }, JsonOptions),
            "reinstate credential",
            cancellationToken);

    /// <inheritdoc />
    public Task<string?> RefreshCredentialAsync(string credentialId, string issuerWallet, string? newExpiryDuration = null, CancellationToken cancellationToken = default) =>
        SendRawAsync(
            HttpMethod.Post,
            $"api/v1/credentials/{Uri.EscapeDataString(credentialId)}/refresh",
            JsonSerializer.Serialize(new { issuerWallet, newExpiryDuration }, JsonOptions),
            "refresh credential",
            cancellationToken);

    // =========================================================================
    // Feature 142 — Blueprint Design Lifecycle Overhaul.
    // Signatures wired here for compile; the rehearsal / publish-override / amend
    // behaviour is implemented in US2/US3. Thin throwing stubs until then.
    // =========================================================================

    /// <inheritdoc />
    public async Task<Rehearsal?> StartRehearsalAsync(string blueprintId, StartRehearsalRequest request, CancellationToken cancellationToken = default)
    {
        // The endpoint accepts { mode: "full" }; the wire contract expects the lowercase enum value.
        var body = new { mode = "full" };
        return await PostRehearsalAsync(
            $"api/blueprints/{Uri.EscapeDataString(blueprintId)}/rehearsals",
            body,
            "start rehearsal",
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Rehearsal?> GetRehearsalAsync(string blueprintId, Guid rehearsalId, CancellationToken cancellationToken = default)
    {
        try
        {
            await SetAuthHeaderAsync(cancellationToken);
            var response = await _httpClient.GetAsync(
                $"api/blueprints/{Uri.EscapeDataString(blueprintId)}/rehearsals/{rehearsalId}",
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                // 404 (unknown rehearsal) and any other non-success surface as null.
                _logger.LogDebug("Get rehearsal {RehearsalId} returned {StatusCode}", rehearsalId, response.StatusCode);
                return null;
            }

            return await response.Content.ReadFromJsonAsync<Rehearsal>(SorchaJson.Options, cancellationToken);
        }
        catch (HttpRequestException)
        {
            throw;
        }
        catch (TaskCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get rehearsal {RehearsalId}", rehearsalId);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<bool> ResetRehearsalAsync(string blueprintId, Guid rehearsalId, CancellationToken cancellationToken = default)
    {
        try
        {
            await SetAuthHeaderAsync(cancellationToken);
            var response = await _httpClient.DeleteAsync(
                $"api/blueprints/{Uri.EscapeDataString(blueprintId)}/rehearsals/{rehearsalId}",
                cancellationToken);

            // Idempotent: the endpoint returns 204 whether or not the rehearsal existed; tolerate 404 too.
            if (response.IsSuccessStatusCode || response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return true;
            }

            _logger.LogWarning("Reset rehearsal {RehearsalId} failed: {StatusCode}", rehearsalId, response.StatusCode);
            return false;
        }
        catch (HttpRequestException)
        {
            throw;
        }
        catch (TaskCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reset rehearsal {RehearsalId}", rehearsalId);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<Rehearsal?> SwitchRehearsalRoleAsync(string blueprintId, Guid rehearsalId, SwitchRehearsalRoleRequest request, CancellationToken cancellationToken = default) =>
        await PostRehearsalAsync(
            $"api/blueprints/{Uri.EscapeDataString(blueprintId)}/rehearsals/{rehearsalId}/role",
            new { role = request.Role },
            "switch rehearsal role",
            cancellationToken);

    /// <inheritdoc />
    public async Task<Rehearsal?> SubmitRehearsalStepAsync(string blueprintId, Guid rehearsalId, SubmitRehearsalStepRequest request, CancellationToken cancellationToken = default)
    {
        // The wire contract carries payload as a JSON object; the DTO holds it as a raw JSON string.
        JsonElement payload;
        try
        {
            payload = string.IsNullOrWhiteSpace(request.PayloadJson)
                ? JsonSerializer.Deserialize<JsonElement>("{}", SorchaJson.Options)
                : JsonSerializer.Deserialize<JsonElement>(request.PayloadJson, SorchaJson.Options);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Submit rehearsal step {RehearsalId}: payload is not valid JSON", rehearsalId);
            return null;
        }

        return await PostRehearsalAsync(
            $"api/blueprints/{Uri.EscapeDataString(blueprintId)}/rehearsals/{rehearsalId}/steps",
            new { actionId = request.ActionId, payload },
            "submit rehearsal step",
            cancellationToken);
    }

    /// <summary>
    /// Shared helper: POSTs a JSON body to a rehearsal endpoint and deserializes the <see cref="Rehearsal"/>
    /// response. Returns null on any non-success status (e.g. 409 blocking validation, 403 unauthorised,
    /// 404 unknown, 422 payload/step error) — matching the documented method contracts.
    /// </summary>
    private async Task<Rehearsal?> PostRehearsalAsync(string url, object body, string operation, CancellationToken cancellationToken)
    {
        try
        {
            await SetAuthHeaderAsync(cancellationToken);
            var response = await _httpClient.PostAsJsonAsync(url, body, JsonOptions, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Blueprint {Operation} failed: {StatusCode}", operation, response.StatusCode);
                return null;
            }

            return await response.Content.ReadFromJsonAsync<Rehearsal>(SorchaJson.Options, cancellationToken);
        }
        catch (HttpRequestException)
        {
            throw;
        }
        catch (TaskCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed Blueprint {Operation}", operation);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<PublishBlueprintOutcome?> PublishBlueprintAsync(string blueprintId, PublishBlueprintRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(blueprintId);
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            await SetAuthHeaderAsync(cancellationToken);
            var response = await _httpClient.PostAsJsonAsync(
                $"/api/blueprints/{Uri.EscapeDataString(blueprintId)}/publish", request, JsonOptions, cancellationToken);

            // 409 = rehearsal soft-gate (Feature 142): the executable-definition hash was not rehearsed.
            if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
            {
                var rehearsal = await response.Content
                    .ReadFromJsonAsync<RehearsalRequiredError>(SorchaJson.Options, cancellationToken);
                return new PublishBlueprintOutcome { RehearsalRequired = rehearsal ?? new RehearsalRequiredError() };
            }

            // 403 = governance-hard gate (caller lacks Owner/Admin/Designer on the register) and any
            // other non-success: surface as null (the outcome type models success vs rehearsal-required only).
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Blueprint publish failed: {StatusCode} (blueprint {BlueprintId}, register {RegisterId})",
                    response.StatusCode, blueprintId, request.RegisterId);
                return null;
            }

            var result = await response.Content
                .ReadFromJsonAsync<PublishBlueprintResult>(SorchaJson.Options, cancellationToken);
            return result is null ? null : new PublishBlueprintOutcome { Result = result };
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Blueprint publish request failed for {BlueprintId}", blueprintId);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<CloneFromPublishedResult?> CloneFromPublishedAsync(CloneFromPublishedRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            await SetAuthHeaderAsync(cancellationToken);
            var response = await _httpClient.PostAsJsonAsync(
                "/api/blueprints/from-published", request, JsonOptions, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Blueprint CloneFromPublished failed: {StatusCode} (register {RegisterId}, source {BlueprintId} v{Version})",
                    response.StatusCode, request.RegisterId, request.BlueprintId, request.Version);
                return null;
            }

            return await response.Content.ReadFromJsonAsync<CloneFromPublishedResult>(SorchaJson.Options, cancellationToken);
        }
        catch (HttpRequestException)
        {
            throw;
        }
        catch (TaskCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed Blueprint CloneFromPublished");
            return null;
        }
    }

    private async Task<string?> GetRawAsync(string url, string operation, CancellationToken cancellationToken)
    {
        try
        {
            await SetAuthHeaderAsync(cancellationToken);
            var response = await _httpClient.GetAsync(url, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Blueprint {Operation} failed: {StatusCode}", operation, response.StatusCode);
                return null;
            }

            return await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (HttpRequestException)
        {
            throw;
        }
        catch (TaskCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed Blueprint {Operation}", operation);
            return null;
        }
    }

    private async Task<string?> SendRawAsync(HttpMethod method, string url, string bodyJson, string operation, CancellationToken cancellationToken)
    {
        try
        {
            await SetAuthHeaderAsync(cancellationToken);

            using var request = new HttpRequestMessage(method, url)
            {
                Content = new StringContent(bodyJson, System.Text.Encoding.UTF8, "application/json")
            };
            var response = await _httpClient.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Blueprint {Operation} failed: {StatusCode}", operation, response.StatusCode);
                return null;
            }

            return await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (HttpRequestException)
        {
            throw;
        }
        catch (TaskCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed Blueprint {Operation}", operation);
            return null;
        }
    }

    /// <summary>
    /// Request DTO for validation endpoint
    /// </summary>
    private record ValidateRequest
    {
        /// <summary>Identifier of the blueprint.</summary>
        public required string BlueprintId { get; init; }
        /// <summary>Identifier of the action.</summary>
        public required string ActionId { get; init; }
        /// <summary>Payload data for this response.</summary>
        public required JsonElement Data { get; init; }
    }

    /// <summary>
    /// Response DTO from validation endpoint
    /// </summary>
    private record ValidateResponse
    {
        /// <summary>Indicates whether validation passed.</summary>
        public bool IsValid { get; init; }
        /// <summary>Collection of error details when the operation did not succeed.</summary>
        public List<string> Errors { get; init; } = [];
    }

    /// <summary>
    /// Request DTO for file chunk submission endpoint
    /// </summary>
    private record FileChunkSubmissionRequest
    {
        /// <summary>The sender wallet.</summary>
        public required string SenderWallet { get; init; }
        /// <summary>The register address.</summary>
        public required string RegisterAddress { get; init; }
        /// <summary>Numeric value for chunk index.</summary>
        public required int ChunkIndex { get; init; }
        /// <summary>Numeric value for total chunks.</summary>
        public required int TotalChunks { get; init; }
        /// <summary>The file hash.</summary>
        public required string FileHash { get; init; }
        /// <summary>The content type.</summary>
        public required string ContentType { get; init; }
        /// <summary>The content base64.</summary>
        public required string ContentBase64 { get; init; }
    }

    /// <summary>
    /// Response DTO from file chunk submission endpoint
    /// </summary>
    private record FileChunkSubmissionResponse
    {
        /// <summary>Identifier of the chunk transaction.</summary>
        public required string ChunkTransactionId { get; init; }
        /// <summary>Numeric value for chunk index.</summary>
        public required int ChunkIndex { get; init; }
        /// <summary>Timestamp associated with this record (UTC).</summary>
        public required DateTimeOffset Timestamp { get; init; }
    }

    /// <summary>
    /// Response DTO from file chunk retrieval endpoint
    /// </summary>
    private record FileChunkRetrievalResponse
    {
        /// <summary>Identifier of the chunk.</summary>
        public required string ChunkId { get; init; }
        /// <summary>The content base64.</summary>
        public required string ContentBase64 { get; init; }
        /// <summary>The content type.</summary>
        public required string ContentType { get; init; }
        /// <summary>Numeric value for size.</summary>
        public required int Size { get; init; }
    }
}
