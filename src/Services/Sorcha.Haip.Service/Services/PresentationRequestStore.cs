// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Cryptography;
using System.Text.Json;
using Sorcha.AtomicCache;
using Sorcha.Haip.Service.Models;

namespace Sorcha.Haip.Service.Services;

/// <summary>
/// Store for HAIP presentation requests. Requests have TTL-based expiry and transition through
/// Pending → Submitted → Verified/Denied states. Backed by <see cref="IAtomicDistributedCache"/>
/// (Redis in production, in-memory in tests) so the completion transition can be a compare-and-set
/// — closing the prior Get-then-Set TOCTOU (review M4) where two concurrent verifier callbacks
/// raced and the last writer won.
/// </summary>
public class PresentationRequestStore
{
    private readonly IAtomicDistributedCache _cache;
    private readonly ILogger<PresentationRequestStore> _logger;
    private readonly int _ttlSeconds;

    public PresentationRequestStore(
        ILogger<PresentationRequestStore> logger,
        IConfiguration configuration,
        IAtomicDistributedCache cache)
    {
        _logger = logger;
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
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
        Sorcha.Verifier.Engine.Dcql.DcqlQuery? declaredQuery = null,
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
            DeclaredQuery = declaredQuery,
            ResponseUri = $"{baseUrl}/api/v1/verifier/requests/{id}/direct-post",
            ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(_ttlSeconds),
            StateToken = id.ToString()
        };

        var key = $"haip:vp-request:{request.Id}";
        await _cache.SetAsync(key, JsonSerializer.Serialize(request), TimeSpan.FromSeconds(_ttlSeconds), ct);

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
        var key = $"haip:vp-request:{requestId}";
        var json = await _cache.GetAsync(key, ct);
        return json != null ? JsonSerializer.Deserialize<PresentationRequest>(json) : null;
    }

    /// <summary>
    /// Updates a request with the verification result and marks it as completed.
    /// </summary>
    /// <remarks>
    /// Review M4: the completion transition is a compare-and-set
    /// (<see cref="IAtomicDistributedCache.TryUpdateIfMatchAsync"/>) so two concurrent
    /// verifier callbacks for the same request cannot both transition it — the first writer
    /// wins, and a callback that loses the race observes the already-terminal state and is an
    /// idempotent no-op (rather than the prior last-writer-wins Get-then-Set).
    /// </remarks>
    public async Task MarkCompletedAsync(
        Guid requestId,
        VerificationResult result,
        string? vpToken = null,
        string? presentationSubmission = null,
        CancellationToken ct = default)
    {
        var key = $"haip:vp-request:{requestId}";
        var ttl = TimeSpan.FromSeconds(_ttlSeconds);
        var newState = result.IsValid ? PresentationRequestState.Verified : PresentationRequestState.Denied;

        // Bounded CAS retry: re-read on a lost race so we re-evaluate against the latest state.
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var json = await _cache.GetAsync(key, ct);
            if (json is null)
            {
                return; // expired or never created — nothing to complete
            }

            var request = JsonSerializer.Deserialize<PresentationRequest>(json)!;
            if (request.State is PresentationRequestState.Verified or PresentationRequestState.Denied)
            {
                // Already completed by a concurrent callback (first-writer-wins) — idempotent no-op.
                return;
            }

            request.State = newState;
            request.Result = result;
            // PR B1: retain the raw submitted presentation so a verifier client can re-validate
            // locally and build its own rich verdict. Persisted atomically with the result; same TTL.
            request.SubmittedVpToken = vpToken;
            request.PresentationSubmission = presentationSubmission;
            var newJson = JsonSerializer.Serialize(request);

            if (await _cache.TryUpdateIfMatchAsync(key, json, newJson, ttl, ct))
            {
                return; // CAS succeeded — this callback completed the request
            }
            // CAS lost the race (value changed since the read); loop to re-read and re-evaluate.
        }

        _logger.LogWarning(
            "MarkCompletedAsync for request {RequestId} could not complete the compare-and-set after retries",
            requestId);
    }
}
