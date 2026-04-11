// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Sorcha.Haip.Service.Models;

namespace Sorcha.Haip.Service.Services;

/// <summary>
/// Redis-backed store for HAIP presentation requests. Requests have TTL-based
/// expiry and transition through Pending → Submitted → Verified/Denied states.
/// </summary>
public class PresentationRequestStore
{
    private readonly IDistributedCache? _cache;
    private readonly ILogger<PresentationRequestStore> _logger;
    private readonly int _ttlSeconds;

    private readonly ConcurrentDictionary<Guid, PresentationRequest> _memoryStore = new();

    public PresentationRequestStore(
        ILogger<PresentationRequestStore> logger,
        IConfiguration configuration,
        IDistributedCache? cache = null)
    {
        _logger = logger;
        _cache = cache;
        _ttlSeconds = configuration.GetValue<int>("Haip:PresentationRequestTtlSeconds", 600);
    }

    /// <summary>
    /// Creates and stores a new presentation request.
    /// </summary>
    // TODO(098): client_id should be the verifier's DID or x509_san_uri, not the base URL
    public async Task<PresentationRequest> CreateAsync(
        string clientId,
        string credentialType,
        List<string>? requiredClaims,
        List<string>? acceptedIssuers,
        string baseUrl,
        CancellationToken ct = default)
    {
        var nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

        var id = Guid.NewGuid();
        var request = new PresentationRequest
        {
            Id = id,
            Nonce = nonce,
            ClientId = clientId,
            CredentialType = credentialType,
            RequiredClaims = requiredClaims,
            AcceptedIssuers = acceptedIssuers,
            ResponseUri = $"{baseUrl}/api/v1/verifier/requests/{id}/direct-post",
            ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(_ttlSeconds),
            StateToken = id.ToString()
        };

        if (_cache != null)
        {
            var key = $"haip:vp-request:{request.Id}";
            var json = JsonSerializer.Serialize(request);
            await _cache.SetStringAsync(key, json,
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(_ttlSeconds) },
                ct);
        }
        else
        {
            _memoryStore[request.Id] = request;
        }

        _logger.LogInformation(
            "Created presentation request {RequestId} for credential type {Type}",
            request.Id, credentialType);

        return request;
    }

    /// <summary>
    /// Gets a presentation request by ID. Returns null if not found or expired.
    /// </summary>
    public async Task<PresentationRequest?> GetAsync(Guid requestId, CancellationToken ct = default)
    {
        if (_cache != null)
        {
            var key = $"haip:vp-request:{requestId}";
            var json = await _cache.GetStringAsync(key, ct);
            return json != null ? JsonSerializer.Deserialize<PresentationRequest>(json) : null;
        }

        _memoryStore.TryGetValue(requestId, out var request);
        if (request != null && request.ExpiresAt < DateTimeOffset.UtcNow)
        {
            _memoryStore.TryRemove(requestId, out _);
            return null;
        }
        return request;
    }

    /// <summary>
    /// Updates a request with the verification result and marks it as completed.
    /// </summary>
    public async Task MarkCompletedAsync(
        Guid requestId,
        VerificationResult result,
        CancellationToken ct = default)
    {
        if (_cache != null)
        {
            var key = $"haip:vp-request:{requestId}";
            var json = await _cache.GetStringAsync(key, ct);
            if (json != null)
            {
                var request = JsonSerializer.Deserialize<PresentationRequest>(json)!;
                request.State = result.IsValid ? PresentationRequestState.Verified : PresentationRequestState.Denied;
                request.Result = result;
                await _cache.SetStringAsync(key, JsonSerializer.Serialize(request),
                    new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(_ttlSeconds) },
                    ct);
            }
        }
        else if (_memoryStore.TryGetValue(requestId, out var request))
        {
            request.State = result.IsValid ? PresentationRequestState.Verified : PresentationRequestState.Denied;
            request.Result = result;
        }
    }
}
