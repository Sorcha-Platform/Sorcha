// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Sorcha.Blueprint.Schemas;

/// <summary>
/// Repository that fetches schemas from the Blueprint Service API.
/// Supports system schemas and custom organization schemas.
/// Thread-safe: uses ConcurrentDictionary for ETag cache and lock for schema cache access.
/// </summary>
public class BlueprintServiceRepository : ISchemaRepository
{
    private static readonly JsonDocument EmptyDocument = JsonDocument.Parse("{}");

    private readonly HttpClient _httpClient;
    private readonly ConcurrentDictionary<string, string> _etagCache = new();
    private readonly object _cacheLock = new();
    private readonly TimeSpan _cacheDuration;
    private List<SchemaDocument>? _cachedSchemas;
    private DateTimeOffset _cacheExpiry = DateTimeOffset.MinValue;

    /// <summary>
    /// Initializes a new BlueprintServiceRepository.
    /// </summary>
    /// <param name="httpClient">HTTP client for communicating with the Blueprint Service API.</param>
    /// <param name="cacheDuration">
    /// How long to cache fetched schemas before re-fetching. Default: 5 minutes.
    /// </param>
    public BlueprintServiceRepository(HttpClient httpClient, TimeSpan? cacheDuration = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _cacheDuration = cacheDuration ?? TimeSpan.FromMinutes(5);
    }

    /// <inheritdoc />
    public SchemaSource SourceType => SchemaSource.BlueprintService;

    /// <inheritdoc />
    public async Task<IEnumerable<SchemaDocument>> GetAllSchemasAsync(CancellationToken cancellationToken = default)
    {
        // Return cached if still valid (read under lock for consistency)
        lock (_cacheLock)
        {
            if (_cachedSchemas != null && DateTimeOffset.UtcNow < _cacheExpiry)
            {
                return _cachedSchemas;
            }
        }

        try
        {
            // Fetch system schemas
            var systemSchemas = await FetchSystemSchemasAsync(cancellationToken);

            // Fetch custom schemas (if authenticated)
            var customSchemas = await FetchCustomSchemasAsync(cancellationToken);

            var result = systemSchemas.Concat(customSchemas).ToList();

            lock (_cacheLock)
            {
                _cachedSchemas = result;
                _cacheExpiry = DateTimeOffset.UtcNow.Add(_cacheDuration);
            }

            return result;
        }
        catch (HttpRequestException)
        {
            // If offline, return cached schemas even if expired
            lock (_cacheLock)
            {
                if (_cachedSchemas != null)
                {
                    return _cachedSchemas;
                }
            }

            return [];
        }
    }

    /// <inheritdoc />
    public async Task<SchemaDocument?> GetSchemaByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/schemas/{Uri.EscapeDataString(id)}");

            // Add ETag for cache validation
            if (_etagCache.TryGetValue(id, out var etag))
            {
                request.Headers.TryAddWithoutValidation("If-None-Match", etag);
            }

            var response = await _httpClient.SendAsync(request, cancellationToken);

            // 304 Not Modified - schema hasn't changed
            if (response.StatusCode == HttpStatusCode.NotModified)
            {
                // Return from in-memory cache
                return _cachedSchemas?.FirstOrDefault(s => s.Metadata.Id == id);
            }

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            response.EnsureSuccessStatusCode();

            var dto = await response.Content.ReadFromJsonAsync<SchemaContentResponse>(cancellationToken: cancellationToken);
            if (dto == null)
            {
                return null;
            }

            // Store new ETag
            if (response.Headers.ETag != null)
            {
                _etagCache[id] = response.Headers.ETag.Tag;
            }

            return ConvertToSchemaDocument(dto);
        }
        catch (HttpRequestException)
        {
            // If offline, try to find in cache
            return _cachedSchemas?.FirstOrDefault(s => s.Metadata.Id == id);
        }
    }

    /// <inheritdoc />
    public async Task<IEnumerable<SchemaDocument>> SearchSchemasAsync(string query, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return await GetAllSchemasAsync(cancellationToken);
        }

        try
        {
            var response = await _httpClient.GetAsync(
                $"/api/v1/schemas/?search={Uri.EscapeDataString(query)}",
                cancellationToken);

            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<SchemaListApiResponse>(cancellationToken: cancellationToken);
            if (result?.Schemas == null)
            {
                return [];
            }

            return result.Schemas.Select(ConvertToSchemaDocument);
        }
        catch (HttpRequestException)
        {
            // If offline, search in cached schemas
            if (_cachedSchemas != null)
            {
                var lowerQuery = query.ToLowerInvariant();
                return _cachedSchemas.Where(s =>
                    s.Metadata.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    s.Metadata.Id.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    s.Metadata.Description.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    s.PropertyNames.Any(p => p.Contains(query, StringComparison.OrdinalIgnoreCase)));
            }

            return [];
        }
    }

    /// <inheritdoc />
    public async Task<IEnumerable<SchemaDocument>> GetSchemasByCategoryAsync(string category, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(category))
        {
            return await GetAllSchemasAsync(cancellationToken);
        }

        try
        {
            var response = await _httpClient.GetAsync(
                $"/api/v1/schemas/?category={Uri.EscapeDataString(category)}",
                cancellationToken);

            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<SchemaListApiResponse>(cancellationToken: cancellationToken);
            if (result?.Schemas == null)
            {
                return [];
            }

            return result.Schemas.Select(ConvertToSchemaDocument);
        }
        catch (HttpRequestException)
        {
            // If offline, filter cached schemas
            if (_cachedSchemas != null)
            {
                return _cachedSchemas.Where(s =>
                    s.Metadata.Category.Equals(category, StringComparison.OrdinalIgnoreCase));
            }

            return [];
        }
    }

    /// <inheritdoc />
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        lock (_cacheLock)
        {
            _cachedSchemas = null;
            _cacheExpiry = DateTimeOffset.MinValue;
        }
        _etagCache.Clear();

        await GetAllSchemasAsync(cancellationToken);
    }

    private async Task<IEnumerable<SchemaDocument>> FetchSystemSchemasAsync(CancellationToken cancellationToken)
    {
        try
        {
            var response = await _httpClient.GetAsync("/api/v1/schemas/system", cancellationToken);
            response.EnsureSuccessStatusCode();

            var schemas = await response.Content.ReadFromJsonAsync<List<SchemaEntryResponse>>(cancellationToken: cancellationToken);
            if (schemas == null)
            {
                return [];
            }

            // Fetch full content for each schema
            var documents = new List<SchemaDocument>();
            foreach (var entry in schemas)
            {
                var doc = await GetSchemaByIdAsync(entry.Identifier, cancellationToken);
                if (doc != null)
                {
                    documents.Add(doc);
                }
            }

            return documents;
        }
        catch (HttpRequestException)
        {
            // Network failure fetching system schemas — return empty to allow graceful degradation
            return [];
        }
        catch (JsonException)
        {
            // Malformed response from Blueprint Service — return empty
            return [];
        }
    }

    private async Task<IEnumerable<SchemaDocument>> FetchCustomSchemasAsync(CancellationToken cancellationToken)
    {
        try
        {
            var response = await _httpClient.GetAsync("/api/v1/schemas/?category=Custom", cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return [];
            }

            var result = await response.Content.ReadFromJsonAsync<SchemaListApiResponse>(cancellationToken: cancellationToken);
            if (result?.Schemas == null)
            {
                return [];
            }

            return result.Schemas.Select(ConvertToSchemaDocument);
        }
        catch (HttpRequestException)
        {
            // Network failure fetching custom schemas — return empty to allow graceful degradation
            return [];
        }
        catch (JsonException)
        {
            // Malformed response from Blueprint Service — return empty
            return [];
        }
    }

    private static SchemaDocument ConvertToSchemaDocument(SchemaContentResponse dto)
    {
        var document = new SchemaDocument
        {
            Metadata = new SchemaMetadata
            {
                Id = dto.Identifier,
                Title = dto.Title,
                Description = dto.Description ?? string.Empty,
                Version = dto.Version,
                Category = dto.Category == "System" ? "System" : dto.Category,
                Source = SchemaSource.BlueprintService,
                Author = "Sorcha",
                AddedAt = DateTimeOffset.UtcNow
            },
            Schema = dto.Content,
            IsValid = true
        };

        document.ExtractPropertyNames();
        return document;
    }

    private static SchemaDocument ConvertToSchemaDocument(SchemaEntryResponse dto)
    {
        var document = new SchemaDocument
        {
            Metadata = new SchemaMetadata
            {
                Id = dto.Identifier,
                Title = dto.Title,
                Description = dto.Description ?? string.Empty,
                Version = dto.Version,
                Category = dto.Category == "System" ? "System" : dto.Category,
                Source = SchemaSource.BlueprintService,
                Author = "Sorcha",
                AddedAt = DateTimeOffset.UtcNow
            },
            Schema = EmptyDocument, // Will be loaded on demand
            IsValid = true
        };

        return document;
    }

    // DTOs for API responses
    private record SchemaEntryResponse(
        string Identifier,
        string Title,
        string Version,
        string Category,
        string Status,
        string? Description,
        SchemaSourceResponse Source);

    private record SchemaContentResponse(
        string Identifier,
        string Title,
        string Version,
        string Category,
        string? Description,
        SchemaSourceResponse Source,
        JsonDocument Content);

    private record SchemaSourceResponse(
        string Type,
        string? Uri,
        string? Provider);

    private record SchemaListApiResponse(
        List<SchemaEntryResponse> Schemas,
        int TotalCount,
        string? NextCursor);
}
