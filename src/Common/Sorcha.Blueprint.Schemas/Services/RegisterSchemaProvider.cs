// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using Microsoft.Extensions.Logging;
using Sorcha.Blueprint.Schemas.DTOs;
using Sorcha.Blueprint.Schemas.Models;

namespace Sorcha.Blueprint.Schemas.Services;

/// <summary>
/// Provider that fetches certified schemas published to a Sorcha register.
/// Organisations can publish schemas to a register, making them discoverable
/// and verifiable by other participants in the network.
///
/// This provider enables a federated schema ecosystem where schemas carry
/// cryptographic provenance — each published schema is a signed transaction
/// on the register, traceable to the publishing organisation's wallet.
/// </summary>
public class RegisterSchemaProvider : IExternalSchemaProvider
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<RegisterSchemaProvider> _logger;
    private readonly string _registerId;
    private volatile List<ExternalSchemaResult>? _catalogCache;
    private DateTimeOffset? _catalogCacheExpiry;
    private readonly SemaphoreSlim _cacheLock = new(1, 1);
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(30);

    public RegisterSchemaProvider(
        HttpClient httpClient,
        string registerId,
        ILogger<RegisterSchemaProvider> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _registerId = registerId ?? throw new ArgumentNullException(nameof(registerId));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public string ProviderName => $"Register ({_registerId[..Math.Min(8, _registerId.Length)]}...)";

    /// <inheritdoc />
    public ProviderType ProviderType => ProviderType.LiveApi;

    /// <inheritdoc />
    public string[] DefaultSectorTags => ["certified"];

    /// <inheritdoc />
    public async Task<ExternalSchemaSearchResponse> SearchAsync(string query, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return new ExternalSchemaSearchResponse([], 0, ProviderName, query);

        var catalog = await GetOrFetchCatalogAsync(cancellationToken);
        var matches = catalog
            .Where(r => r.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                        r.Description.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return new ExternalSchemaSearchResponse(matches, matches.Count, ProviderName, query);
    }

    /// <inheritdoc />
    public async Task<ExternalSchemaResult?> GetSchemaAsync(string schemaUrl, CancellationToken cancellationToken = default)
    {
        var catalog = await GetOrFetchCatalogAsync(cancellationToken);
        return catalog.FirstOrDefault(r =>
            string.Equals(r.Url, schemaUrl, StringComparison.OrdinalIgnoreCase));
    }

    /// <inheritdoc />
    public async Task<IEnumerable<ExternalSchemaResult>> GetCatalogAsync(CancellationToken cancellationToken = default)
    {
        return await GetOrFetchCatalogAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // Check if the register service is reachable
            var response = await _httpClient.GetAsync(
                $"/api/v1/registers/{_registerId}/schemas?limit=1", cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Register schema provider availability check failed for {RegisterId}", _registerId);
            return false;
        }
    }

    private async Task<List<ExternalSchemaResult>> GetOrFetchCatalogAsync(CancellationToken cancellationToken)
    {
        var cache = _catalogCache;
        var expiry = _catalogCacheExpiry;
        if (cache is not null && expiry > DateTimeOffset.UtcNow)
            return cache;

        await _cacheLock.WaitAsync(cancellationToken);
        try
        {
            cache = _catalogCache;
            expiry = _catalogCacheExpiry;
            if (cache is not null && expiry > DateTimeOffset.UtcNow)
                return cache;

            _logger.LogDebug("RegisterSchemaProvider not yet connected — returning empty catalog for register {RegisterId}", _registerId);

            // TODO: Implement when Register Service exposes schema publication endpoints.
            // Expected endpoint: GET /api/v1/registers/{registerId}/schemas
            // Each schema transaction contains: identifier, title, description, JSON Schema content,
            // signing organisation, certification status, and version history.
            //
            // The response will include cryptographic provenance:
            // - Publisher wallet address
            // - Transaction ID on the register
            // - Attestation signatures from validators

            _catalogCache = [];
            _catalogCacheExpiry = DateTimeOffset.UtcNow.Add(CacheDuration);
            return _catalogCache;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to fetch schemas from register {RegisterId}", _registerId);

            // Return stale cache if available
            if (_catalogCache is not null)
                return _catalogCache;

            return [];
        }
        finally
        {
            _cacheLock.Release();
        }
    }
}
