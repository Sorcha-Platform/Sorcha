// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;

namespace Sorcha.Blueprint.Schemas;

/// <summary>
/// Repository for schemas from SchemaStore.org.
/// Thread-safe: uses SemaphoreSlim for async-safe initialization.
/// </summary>
public class SchemaStoreRepository : ISchemaRepository
{
    private readonly HttpClient _httpClient;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private readonly List<SchemaDocument> _cachedSchemas = [];
    private bool _initialized = false;
    private DateTimeOffset _lastRefresh = DateTimeOffset.MinValue;
    private static readonly TimeSpan CacheExpiration = TimeSpan.FromHours(24);

    private const string SchemaStoreCatalogUrl = "https://www.schemastore.org/api/json/catalog.json";

    public SchemaSource SourceType => SchemaSource.SchemaStore;

    public SchemaStoreRepository(HttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    public async Task<IEnumerable<SchemaDocument>> GetAllSchemasAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        return _cachedSchemas;
    }

    public async Task<SchemaDocument?> GetSchemaByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        return _cachedSchemas.FirstOrDefault(s => s.Metadata.Id == id);
    }

    public async Task<IEnumerable<SchemaDocument>> SearchSchemasAsync(string query, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        query = query.ToLowerInvariant();
        var results = _cachedSchemas.Where(s =>
            s.Metadata.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            s.Metadata.Description.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            s.Metadata.Tags.Any(t => t.Contains(query, StringComparison.OrdinalIgnoreCase))
        );

        return results;
    }

    public async Task<IEnumerable<SchemaDocument>> GetSchemasByCategoryAsync(string category, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        var results = _cachedSchemas.Where(s =>
            s.Metadata.Category.Equals(category, StringComparison.OrdinalIgnoreCase)
        );

        return results;
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        await _initLock.WaitAsync(cancellationToken);
        try
        {
            _initialized = false;
            _cachedSchemas.Clear();
        }
        finally
        {
            _initLock.Release();
        }
        await EnsureInitializedAsync(cancellationToken);
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        // Fast path: cache is still valid
        if (_initialized && (DateTimeOffset.UtcNow - _lastRefresh) < CacheExpiration)
        {
            return;
        }

        await _initLock.WaitAsync(cancellationToken);
        try
        {
            // Double-check after acquiring the lock
            if (_initialized && (DateTimeOffset.UtcNow - _lastRefresh) < CacheExpiration)
            {
                return;
            }

            try
            {
                // Fetch the SchemaStore catalog
                var catalogJson = await _httpClient.GetStringAsync(SchemaStoreCatalogUrl, cancellationToken);
                var catalogDoc = JsonDocument.Parse(catalogJson);

                _cachedSchemas.Clear();

                // Parse the catalog
                if (catalogDoc.RootElement.TryGetProperty("schemas", out var schemas))
                {
                    foreach (var schemaElement in schemas.EnumerateArray())
                    {
                        var schemaDoc = ParseSchemaStoreEntry(schemaElement);
                        if (schemaDoc != null)
                        {
                            _cachedSchemas.Add(schemaDoc);
                        }
                    }
                }

                _initialized = true;
                _lastRefresh = DateTimeOffset.UtcNow;
            }
            catch (HttpRequestException ex)
            {
                // Log error but don't fail - return empty results
                Console.WriteLine($"Failed to fetch SchemaStore catalog: {ex.Message}");
                _initialized = true; // Mark as initialized to prevent repeated failures
            }
        }
        finally
        {
            _initLock.Release();
        }
    }

    private SchemaDocument? ParseSchemaStoreEntry(JsonElement element)
    {
        try
        {
            var name = element.TryGetProperty("name", out var nameEl) ? nameEl.GetString() ?? "" : "";
            var description = element.TryGetProperty("description", out var descEl) ? descEl.GetString() ?? "" : "";
            var url = element.TryGetProperty("url", out var urlEl) ? urlEl.GetString() ?? "" : "";
            var fileMatch = element.TryGetProperty("fileMatch", out var fileMatchEl) ?
                fileMatchEl.EnumerateArray().Select(e => e.GetString() ?? "").ToList() : [];

            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(url))
            {
                return null;
            }

            // Extract category from file patterns if possible
            var category = InferCategory(name, fileMatch);

            return new SchemaDocument
            {
                Metadata = new SchemaMetadata
                {
                    Id = url,
                    Title = name,
                    Description = description,
                    Version = "1.0.0", // SchemaStore doesn't always provide versions
                    Category = category,
                    Tags = ExtractTags(name, description),
                    Source = SchemaSource.SchemaStore,
                    SchemaUrl = url,
                    Author = "SchemaStore.org",
                    License = "Apache-2.0"
                },
                // Don't fetch the full schema yet - only fetch on demand
                Schema = JsonDocument.Parse("{}"),
                PropertyNames = []
            };
        }
        catch (JsonException)
        {
            // Malformed JSON in SchemaStore catalog entry — skip this entry
            return null;
        }
    }

    private string InferCategory(string name, List<string> fileMatch)
    {
        var lowerName = name.ToLowerInvariant();

        if (lowerName.Contains("package") || lowerName.Contains("npm") || lowerName.Contains("composer"))
            return "Package Management";

        if (lowerName.Contains("config") || lowerName.Contains("settings"))
            return "Configuration";

        if (lowerName.Contains("docker") || lowerName.Contains("kubernetes"))
            return "DevOps";

        if (lowerName.Contains("github") || lowerName.Contains("azure") || lowerName.Contains("aws"))
            return "Cloud Services";

        if (lowerName.Contains("json") || lowerName.Contains("yaml") || lowerName.Contains("xml"))
            return "Data Format";

        return "General";
    }

    private List<string> ExtractTags(string name, string description)
    {
        var tags = new List<string>();
        var combined = $"{name} {description}".ToLowerInvariant();

        // Extract common keywords as tags
        var keywords = new[] { "config", "package", "schema", "api", "json", "yaml", "docker", "kubernetes", "github", "npm", "python" };

        foreach (var keyword in keywords)
        {
            if (combined.Contains(keyword))
            {
                tags.Add(keyword);
            }
        }

        return tags.Distinct().ToList();
    }

    /// <summary>
    /// Fetch the full schema document from URL (lazy loading)
    /// </summary>
    public async Task<SchemaDocument?> FetchFullSchemaAsync(string schemaId, CancellationToken cancellationToken = default)
    {
        var schemaDoc = await GetSchemaByIdAsync(schemaId, cancellationToken);
        if (schemaDoc == null || string.IsNullOrEmpty(schemaDoc.Metadata.SchemaUrl))
        {
            return null;
        }

        try
        {
            var schemaJson = await _httpClient.GetStringAsync(schemaDoc.Metadata.SchemaUrl, cancellationToken);
            schemaDoc.Schema = JsonDocument.Parse(schemaJson);
            schemaDoc.ExtractPropertyNames();
            return schemaDoc;
        }
        catch (HttpRequestException ex)
        {
            Console.WriteLine($"Failed to fetch schema from {schemaDoc.Metadata.SchemaUrl}: {ex.Message}");
            return null;
        }
    }
}
