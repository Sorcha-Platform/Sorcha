// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using Microsoft.Extensions.Logging;
using Sorcha.Blueprint.Schemas.DTOs;
using Sorcha.Blueprint.Schemas.Models;

namespace Sorcha.Blueprint.Schemas.Services;

/// <summary>
/// Provider that serves categorised schemas from local JSON files in the
/// blueprints/schemas/ directory. These are Sorcha's curated default schemas
/// (UK Address, Personal Identity, Invoice Line Item, etc.) and are always
/// available — no network dependency.
///
/// Each JSON file follows the Sorcha schema file format with embedded metadata:
/// identifier, title, description, version, category, tags, keywords, schema,
/// formLayout, and disclosure fields.
/// </summary>
public class LocalSchemaProvider : IExternalSchemaProvider, IDisposable
{
    private readonly ILogger<LocalSchemaProvider> _logger;
    private readonly string _schemasPath;
    private readonly object _cacheLock = new();
    private volatile List<ExternalSchemaResult>? _catalogCache;

    private static readonly JsonSerializerOptions CaseInsensitiveJson = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public LocalSchemaProvider(string schemasPath, ILogger<LocalSchemaProvider> logger)
    {
        _schemasPath = schemasPath ?? throw new ArgumentNullException(nameof(schemasPath));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public string ProviderName => "Sorcha Local";

    /// <inheritdoc />
    public ProviderType ProviderType => ProviderType.StaticFile;

    /// <inheritdoc />
    public string[] DefaultSectorTags => ["general"];

    /// <inheritdoc />
    public Task<ExternalSchemaSearchResponse> SearchAsync(string query, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return Task.FromResult(new ExternalSchemaSearchResponse([], 0, ProviderName, query));

        var catalog = BuildCatalog();
        var matches = catalog
            .Where(r => r.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                        r.Description.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                        (r.SectorTags?.Any(t => t.Contains(query, StringComparison.OrdinalIgnoreCase)) ?? false))
            .ToList();

        return Task.FromResult(new ExternalSchemaSearchResponse(matches, matches.Count, ProviderName, query));
    }

    /// <inheritdoc />
    public Task<ExternalSchemaResult?> GetSchemaAsync(string schemaUrl, CancellationToken cancellationToken = default)
    {
        var catalog = BuildCatalog();
        return Task.FromResult(catalog.FirstOrDefault(r =>
            string.Equals(r.Url, schemaUrl, StringComparison.OrdinalIgnoreCase)));
    }

    /// <inheritdoc />
    public Task<IEnumerable<ExternalSchemaResult>> GetCatalogAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IEnumerable<ExternalSchemaResult>>(BuildCatalog());
    }

    /// <inheritdoc />
    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Directory.Exists(_schemasPath));
    }

    /// <summary>
    /// Invalidates the catalog cache, disposing JsonDocument instances and forcing
    /// a re-read from disk on next access.
    /// </summary>
    public void InvalidateCache()
    {
        List<ExternalSchemaResult>? old;
        lock (_cacheLock)
        {
            old = _catalogCache;
            _catalogCache = null;
        }

        DisposeDocuments(old);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        DisposeDocuments(_catalogCache);
        _catalogCache = null;
    }

    private List<ExternalSchemaResult> BuildCatalog()
    {
        var cache = _catalogCache;
        if (cache is not null) return cache;

        lock (_cacheLock)
        {
            // Double-check inside lock
            if (_catalogCache is not null) return _catalogCache;

            if (!Directory.Exists(_schemasPath))
            {
                _logger.LogWarning("Local schemas directory not found at {Path}", _schemasPath);
                _catalogCache = [];
                return _catalogCache;
            }

            var files = Directory.GetFiles(_schemasPath, "*.json", SearchOption.AllDirectories);
            var results = new List<ExternalSchemaResult>();

            foreach (var file in files)
            {
                try
                {
                    var json = File.ReadAllText(file);
                    var schemaFile = JsonSerializer.Deserialize<SchemaFileFormat>(json, CaseInsensitiveJson);

                    if (schemaFile is null || string.IsNullOrWhiteSpace(schemaFile.Identifier)
                                           || string.IsNullOrWhiteSpace(schemaFile.Title))
                    {
                        _logger.LogWarning("Skipping invalid schema file {File}", Path.GetFileName(file));
                        continue;
                    }

                    var content = BuildContent(schemaFile);
                    var sectorTags = BuildSectorTags(schemaFile);

                    results.Add(new ExternalSchemaResult(
                        Name: schemaFile.Title,
                        Description: schemaFile.Description ?? $"Sorcha standard schema: {schemaFile.Title}",
                        Url: $"urn:sorcha:schema:{schemaFile.Identifier}",
                        Provider: ProviderName,
                        Content: content,
                        SectorTags: sectorTags));
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex, "Failed to parse schema file {File}", Path.GetFileName(file));
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Error loading schema from {File}", Path.GetFileName(file));
                }
            }

            _catalogCache = results;
            _logger.LogInformation("Built catalog of {Count} local schemas from {Path}", results.Count, _schemasPath);
            return _catalogCache;
        }
    }

    /// <summary>
    /// Resolves the schemas directory, checking multiple locations.
    /// </summary>
    public static string ResolveSchemasPath(string contentRootPath)
    {
        // Check relative to content root (development)
        var contentRootCandidate = Path.Combine(contentRootPath, "blueprints", "schemas");
        if (Directory.Exists(contentRootCandidate))
            return contentRootCandidate;

        // Check relative to base directory (published/Docker)
        var baseDirCandidate = Path.Combine(AppContext.BaseDirectory, "blueprints", "schemas");
        if (Directory.Exists(baseDirCandidate))
            return baseDirCandidate;

        // Walk up from content root to find solution-level blueprints/schemas
        var current = contentRootPath;
        for (var i = 0; i < 6; i++)
        {
            var parent = Directory.GetParent(current);
            if (parent is null) break;
            current = parent.FullName;

            var candidate = Path.Combine(current, "blueprints", "schemas");
            if (Directory.Exists(candidate))
                return candidate;
        }

        // Fallback to base directory path
        return baseDirCandidate;
    }

    private static JsonDocument BuildContent(SchemaFileFormat file)
    {
        var contentObj = new Dictionary<string, object?>();

        if (file.Schema is not null)
        {
            var schemaJson = file.Schema.Value.GetRawText();
            var schemaDict = JsonSerializer.Deserialize<Dictionary<string, object?>>(schemaJson, CaseInsensitiveJson);
            if (schemaDict is not null)
            {
                foreach (var kvp in schemaDict)
                    contentObj[kvp.Key] = kvp.Value;
            }
        }

        // Standard JSON Schema envelope
        contentObj.TryAdd("$schema", "https://json-schema.org/draft/2020-12/schema");
        contentObj.TryAdd("$id", $"urn:sorcha:schema:{file.Identifier}");
        contentObj.TryAdd("title", file.Title);
        if (!string.IsNullOrEmpty(file.Description))
            contentObj.TryAdd("description", file.Description);

        // Embed form layout as x-sorcha-formLayout extension
        if (file.FormLayout is not null)
        {
            var layoutJson = file.FormLayout.Value.GetRawText();
            contentObj["x-sorcha-formLayout"] = JsonSerializer.Deserialize<object>(layoutJson, CaseInsensitiveJson);
        }

        // Embed disclosure as x-sorcha-disclosure extension
        if (file.Disclosure is not null)
        {
            var disclosureJson = file.Disclosure.Value.GetRawText();
            contentObj["x-sorcha-disclosure"] = JsonSerializer.Deserialize<object>(disclosureJson, CaseInsensitiveJson);
        }

        return JsonDocument.Parse(JsonSerializer.Serialize(contentObj));
    }

    private static string[] BuildSectorTags(SchemaFileFormat file)
    {
        var tags = new List<string>();
        if (!string.IsNullOrEmpty(file.Category))
            tags.Add(file.Category);
        if (file.Tags is not null)
            tags.AddRange(file.Tags);
        return tags.Distinct().ToArray();
    }

    private static void DisposeDocuments(List<ExternalSchemaResult>? entries)
    {
        if (entries is null) return;
        foreach (var entry in entries)
        {
            entry.Content?.Dispose();
        }
    }

    /// <summary>
    /// Deserialization target for Sorcha schema JSON files.
    /// </summary>
    internal sealed class SchemaFileFormat
    {
        public string? Identifier { get; set; }
        public string? Title { get; set; }
        public string? Description { get; set; }
        public string Version { get; set; } = "1.0.0";
        public string? Category { get; set; }
        public string[]? Tags { get; set; }
        public string[]? Keywords { get; set; }
        public JsonElement? Schema { get; set; }
        public JsonElement? FormLayout { get; set; }
        public JsonElement? Disclosure { get; set; }
    }
}
