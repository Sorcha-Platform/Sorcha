// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Sorcha.Blueprint.Schemas.Models;
using Sorcha.Blueprint.Schemas.Repositories;
using Sorcha.Blueprint.Schemas.Services;
using Sorcha.Blueprint.Service.Models;
using Sorcha.Blueprint.Service.Services.Interfaces;

namespace Sorcha.Blueprint.Service.Services;

/// <summary>
/// Orchestrates schema index CRUD, full-text search with sector filtering,
/// content fetching from providers, and provider status tracking.
/// </summary>
public class SchemaIndexService : ISchemaIndexService
{
    private readonly ISchemaIndexRepository _indexRepository;
    private readonly IEnumerable<IExternalSchemaProvider> _providers;
    private readonly ILogger<SchemaIndexService> _logger;

    private readonly ConcurrentDictionary<string, SchemaProviderStatus> _providerStatuses = new();
    private readonly ConcurrentDictionary<string, bool> _loadingProviders = new();
    private readonly object _statusLock = new();

    public SchemaIndexService(
        ISchemaIndexRepository indexRepository,
        IEnumerable<IExternalSchemaProvider> providers,
        ILogger<SchemaIndexService> logger)
    {
        _indexRepository = indexRepository;
        _providers = providers;
        _logger = logger;

        // Initialize provider statuses with correct type detection
        foreach (var provider in _providers)
        {
            var providerType = provider switch
            {
                LocalSchemaProvider => ProviderType.StaticFile,
                StaticFileSchemaProvider => ProviderType.StaticFile,
                W3cVcProvider => ProviderType.StaticFile,
                UblSchemaProvider => ProviderType.StaticFile,
                Iso20022Provider => ProviderType.StaticFile,
                DppSchemaProvider => ProviderType.StaticFile,
                RegisterSchemaProvider => ProviderType.LiveApi,
                _ => ProviderType.LiveApi
            };

            _providerStatuses.TryAdd(provider.ProviderName, new SchemaProviderStatus
            {
                ProviderName = provider.ProviderName,
                ProviderType = providerType,
                HealthStatus = ProviderHealth.Unknown
            });
        }
    }

    /// <inheritdoc />
    public async Task<SchemaIndexSearchResponse> SearchAsync(
        string? search = null,
        string[]? sectors = null,
        string? provider = null,
        SchemaIndexStatus? status = null,
        int limit = 25,
        string? cursor = null,
        string? organizationId = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _indexRepository.SearchAsync(
            search, sectors, provider, status, limit, cursor, cancellationToken);

        var dtos = result.Results.Select(MapToDto).ToList();
        var loadingProviders = GetLoadingProviders();

        return new SchemaIndexSearchResponse(dtos, result.TotalCount, result.NextCursor, loadingProviders);
    }

    /// <inheritdoc />
    public async Task<SchemaIndexEntryDetail?> GetByProviderAndUriAsync(
        string sourceProvider,
        string sourceUri,
        CancellationToken cancellationToken = default)
    {
        var doc = await _indexRepository.GetByProviderAndUriAsync(sourceProvider, sourceUri, cancellationToken);
        return doc is null ? null : await BuildDetailAsync(doc, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<SchemaIndexEntryDetail?> GetByShortCodeAsync(
        string shortCode,
        CancellationToken cancellationToken = default)
    {
        var doc = await _indexRepository.GetByShortCodeAsync(shortCode, cancellationToken);
        return doc is null ? null : await BuildDetailAsync(doc, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<JsonDocument?> GetContentByShortCodeAsync(
        string shortCode,
        CancellationToken cancellationToken = default)
    {
        var doc = await _indexRepository.GetByShortCodeAsync(shortCode, cancellationToken);
        if (doc is null) return null;
        return await FetchContentFromProvider(doc.SourceProvider, doc.SourceUri, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<JsonDocument?> GetContentAsync(
        string sourceProvider,
        string sourceUri,
        CancellationToken cancellationToken = default)
    {
        return await FetchContentFromProvider(sourceProvider, sourceUri, cancellationToken);
    }

    private async Task<SchemaIndexEntryDetail> BuildDetailAsync(
        SchemaIndexEntryDocument doc,
        CancellationToken cancellationToken)
    {
        var content = await FetchContentFromProvider(doc.SourceProvider, doc.SourceUri, cancellationToken);

        return new SchemaIndexEntryDetail(
            doc.ShortCode,
            doc.SourceProvider,
            doc.SourceUri,
            doc.Title,
            doc.Description,
            doc.SectorTags,
            doc.FieldCount,
            doc.RequiredFields?.Length ?? 0,
            doc.SchemaVersion,
            doc.Status,
            doc.LastFetchedAt,
            doc.FieldNames,
            doc.RequiredFields,
            doc.Keywords,
            content);
    }

    /// <inheritdoc />
    public async Task<int> UpsertFromProviderAsync(
        string providerName,
        IEnumerable<ProviderSchemaEntry> entries,
        CancellationToken cancellationToken = default)
    {
        var documents = entries.Select(e =>
        {
            var normalised = JsonSchemaNormaliser.Normalise(e.Content);
            var metadata = JsonSchemaNormaliser.ExtractMetadata(normalised);
            var hash = JsonSchemaNormaliser.ComputeContentHash(normalised);

            return new SchemaIndexEntryDocument
            {
                SourceProvider = providerName,
                SourceUri = e.SourceUri,
                Title = e.Title,
                Description = e.Description,
                SectorTags = e.SectorTags,
                Keywords = e.Keywords ?? metadata.Keywords,
                FieldCount = metadata.FieldCount,
                FieldNames = metadata.FieldNames,
                RequiredFields = metadata.RequiredFields,
                SchemaVersion = e.SchemaVersion,
                ContentHash = hash,
                Status = nameof(SchemaIndexStatus.Active),
                LastFetchedAt = DateTimeOffset.UtcNow
            };
        }).ToList();

        var count = await _indexRepository.BatchUpsertAsync(documents, cancellationToken);

        // Update provider status (thread-safe — multiple providers refresh concurrently)
        if (_providerStatuses.TryGetValue(providerName, out var status))
        {
            var schemaCount = await _indexRepository.GetCountByProviderAsync(providerName, cancellationToken);
            lock (_statusLock)
            {
                status.SchemaCount = schemaCount;
                status.LastSuccessfulFetch = DateTimeOffset.UtcNow;
                status.HealthStatus = ProviderHealth.Healthy;
                status.LastError = null;
                status.LastErrorAt = null;
                status.ConsecutiveFailures = 0;
                status.BackoffUntil = null;
            }
        }

        _logger.LogInformation("Upserted {Count} schemas from {Provider} ({Total} total for provider)",
            count, providerName, documents.Count);

        return count;
    }

    /// <inheritdoc />
    public IReadOnlyList<SchemaProviderStatusDto> GetProviderStatuses()
    {
        lock (_statusLock)
        {
            return _providerStatuses.Values.Select(s => new SchemaProviderStatusDto(
                s.ProviderName,
                s.IsEnabled,
                s.ProviderType.ToString(),
                s.RateLimitPerSecond,
                s.RefreshIntervalHours,
                s.LastSuccessfulFetch,
                s.LastError,
                s.LastErrorAt,
                s.SchemaCount,
                s.HealthStatus.ToString(),
                s.BackoffUntil)).ToList();
        }
    }

    /// <inheritdoc />
    public string[] GetLoadingProviders()
    {
        return _loadingProviders.Where(kv => kv.Value).Select(kv => kv.Key).ToArray();
    }

    /// <summary>
    /// Marks a provider as loading (used by refresh service during cold start).
    /// </summary>
    public void SetProviderLoading(string providerName, bool isLoading)
    {
        _loadingProviders.AddOrUpdate(providerName, isLoading, (_, _) => isLoading);
    }

    /// <summary>
    /// Records a provider failure with backoff.
    /// </summary>
    public void RecordProviderFailure(string providerName, string error)
    {
        if (_providerStatuses.TryGetValue(providerName, out var status))
        {
            lock (_statusLock)
            {
                status.ConsecutiveFailures++;
                status.LastError = error;
                status.LastErrorAt = DateTimeOffset.UtcNow;
                status.HealthStatus = status.ConsecutiveFailures >= 3
                    ? ProviderHealth.Unavailable
                    : ProviderHealth.Degraded;

                // Exponential backoff: 2^n seconds, max 1 hour
                var backoffSeconds = Math.Min(Math.Pow(2, status.ConsecutiveFailures), 3600);
                status.BackoffUntil = DateTimeOffset.UtcNow.AddSeconds(backoffSeconds);
            }
        }
    }

    /// <summary>
    /// Checks if a provider is in backoff period.
    /// </summary>
    public bool IsProviderInBackoff(string providerName)
    {
        if (_providerStatuses.TryGetValue(providerName, out var status))
        {
            lock (_statusLock)
            {
                return status.BackoffUntil.HasValue && status.BackoffUntil > DateTimeOffset.UtcNow;
            }
        }
        return false;
    }

    /// <summary>
    /// Registers or updates a provider's configuration.
    /// </summary>
    public void RegisterProvider(string providerName, ProviderType providerType, double rateLimitPerSecond = 2.0)
    {
        _providerStatuses.AddOrUpdate(providerName,
            new SchemaProviderStatus
            {
                ProviderName = providerName,
                ProviderType = providerType,
                RateLimitPerSecond = rateLimitPerSecond,
                HealthStatus = ProviderHealth.Unknown
            },
            (_, existing) =>
            {
                existing.ProviderType = providerType;
                existing.RateLimitPerSecond = rateLimitPerSecond;
                return existing;
            });
    }

    private async Task<JsonDocument?> FetchContentFromProvider(
        string sourceProvider,
        string sourceUri,
        CancellationToken cancellationToken)
    {
        var provider = _providers.FirstOrDefault(p =>
            string.Equals(p.ProviderName, sourceProvider, StringComparison.OrdinalIgnoreCase));

        if (provider is null)
        {
            _logger.LogWarning("Provider {Provider} not found for content fetch", sourceProvider);
            return null;
        }

        try
        {
            var schema = await provider.GetSchemaAsync(sourceUri, cancellationToken);
            if (schema?.Content is not null)
            {
                return JsonDocument.Parse(schema.Content.RootElement.GetRawText());
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch content from {Provider} for {Uri}", sourceProvider, sourceUri);
        }

        return null;
    }

    private static SchemaIndexEntryDto MapToDto(SchemaIndexEntryDocument doc)
    {
        return new SchemaIndexEntryDto(
            doc.ShortCode,
            doc.SourceProvider,
            doc.SourceUri,
            doc.Title,
            doc.Description,
            doc.SectorTags,
            doc.FieldCount,
            doc.RequiredFields?.Length ?? 0,
            doc.SchemaVersion,
            doc.Status,
            doc.LastFetchedAt);
    }
}
