// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Microsoft.Extensions.Logging;
using Sorcha.Serialization;

namespace Sorcha.ServiceClients.Trust;

/// <summary>
/// HTTP client for the Tenant Service's X.509 trust endpoints. Today only
/// implements <see cref="IOrgCertChainProvider"/>; future trust surfaces
/// (CRL fetch, trust-anchor download, revocation submission) compose into
/// this client as separate interfaces.
/// </summary>
/// <remarks>
/// Org certs rotate rarely (yearly at most in typical production). An in-
/// memory TTL cache of 5 minutes prevents a chain fetch per credential
/// issuance while still letting a freshly-rotated cert propagate quickly
/// after the cache expires. Cache is best-effort — service restarts drop
/// it; there is no cross-node consistency requirement.
/// </remarks>
public sealed class TrustServiceClient : IOrgCertChainProvider
{
    private static readonly TimeSpan DefaultCacheTtl = TimeSpan.FromMinutes(5);

    private readonly HttpClient _httpClient;
    private readonly ILogger<TrustServiceClient> _logger;
    private readonly TimeSpan _cacheTtl;
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new();

    public TrustServiceClient(
        HttpClient httpClient,
        ILogger<TrustServiceClient> logger,
        TimeSpan? cacheTtl = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _cacheTtl = cacheTtl ?? DefaultCacheTtl;
    }

    /// <inheritdoc />
    public async Task<OrgCertChain?> GetChainForAsync(
        string tenantId,
        string orgWalletAddress,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(orgWalletAddress);

        var cacheKey = $"{tenantId}:{orgWalletAddress}";
        if (_cache.TryGetValue(cacheKey, out var cached) && cached.ExpiresAt > DateTimeOffset.UtcNow)
        {
            return cached.Chain;
        }

        var path = $"/api/v1/trust/tenants/{Uri.EscapeDataString(tenantId)}/orgs/{Uri.EscapeDataString(orgWalletAddress)}/cert-chain";

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.GetAsync(path, ct);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex,
                "Cert chain fetch failed for tenant {TenantId} org {OrgWallet} — returning null",
                tenantId, orgWalletAddress);
            return null;
        }

        try
        {
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                _logger.LogDebug(
                    "No active cert chain for tenant {TenantId} org {OrgWallet} (404 from Tenant Service)",
                    tenantId, orgWalletAddress);
                // Cache the miss for a short window so a tight-loop caller
                // doesn't hammer the Tenant Service when enrolment is pending.
                _cache[cacheKey] = new CacheEntry(null, DateTimeOffset.UtcNow.AddSeconds(30));
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Cert chain fetch returned {StatusCode} for tenant {TenantId} org {OrgWallet}",
                    (int)response.StatusCode, tenantId, orgWalletAddress);
                return null;
            }

            JsonElement body;
            try
            {
                body = await response.Content.ReadFromJsonAsync<JsonElement>(SorchaJson.Options, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Malformed cert chain response for tenant {TenantId} org {OrgWallet}",
                    tenantId, orgWalletAddress);
                return null;
            }

            var orgB64 = body.TryGetProperty("orgCertBase64", out var orgEl) ? orgEl.GetString() : null;
            var rootB64 = body.TryGetProperty("rootCertBase64", out var rootEl) ? rootEl.GetString() : null;
            if (string.IsNullOrWhiteSpace(orgB64) || string.IsNullOrWhiteSpace(rootB64))
            {
                _logger.LogWarning(
                    "Cert chain response missing orgCertBase64 / rootCertBase64 for tenant {TenantId} org {OrgWallet}",
                    tenantId, orgWalletAddress);
                return null;
            }

            byte[] orgDer;
            byte[] rootDer;
            try
            {
                orgDer = Convert.FromBase64String(orgB64);
                rootDer = Convert.FromBase64String(rootB64);
            }
            catch (FormatException ex)
            {
                _logger.LogWarning(ex,
                    "Cert chain response contained non-base64 certificate bytes for tenant {TenantId} org {OrgWallet}",
                    tenantId, orgWalletAddress);
                return null;
            }

            var chain = new OrgCertChain(orgDer, rootDer);
            _cache[cacheKey] = new CacheEntry(chain, DateTimeOffset.UtcNow + _cacheTtl);
            return chain;
        }
        finally
        {
            response.Dispose();
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<byte[]>?> GetImportedChainForAsync(
        string tenantId,
        string orgWalletAddress,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(orgWalletAddress);

        // Not cached — issuance on the external anchor is a cold path and stale-imported-chain correctness
        // (fail-closed on absent/expired/rotated) matters more than shaving a lookup.
        var path = $"/api/v1/trust/tenants/{Uri.EscapeDataString(tenantId)}/orgs/{Uri.EscapeDataString(orgWalletAddress)}/imported-cert-chain";

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.GetAsync(path, ct);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex,
                "Imported cert chain fetch failed for tenant {TenantId} org {OrgWallet}",
                tenantId, orgWalletAddress);
            return null;
        }

        try
        {
            if (response.StatusCode == HttpStatusCode.NotFound || !response.IsSuccessStatusCode)
            {
                return null;
            }

            JsonElement body;
            try
            {
                body = await response.Content.ReadFromJsonAsync<JsonElement>(SorchaJson.Options, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Malformed imported cert chain response for tenant {TenantId} org {OrgWallet}",
                    tenantId, orgWalletAddress);
                return null;
            }

            if (!body.TryGetProperty("chainDerBase64", out var chainEl) || chainEl.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var chain = new List<byte[]>();
            foreach (var el in chainEl.EnumerateArray())
            {
                var b64 = el.GetString();
                if (string.IsNullOrWhiteSpace(b64))
                {
                    return null;
                }
                try
                {
                    chain.Add(Convert.FromBase64String(b64));
                }
                catch (FormatException)
                {
                    return null;
                }
            }
            return chain.Count > 0 ? chain : null;
        }
        finally
        {
            response.Dispose();
        }
    }

    private sealed record CacheEntry(OrgCertChain? Chain, DateTimeOffset ExpiresAt);
}
