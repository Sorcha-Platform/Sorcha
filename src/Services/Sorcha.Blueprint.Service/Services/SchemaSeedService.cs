// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using Sorcha.Blueprint.Schemas.Models;
using Sorcha.Blueprint.Schemas.Repositories;
using Sorcha.Blueprint.Schemas.Services;

namespace Sorcha.Blueprint.Service.Services;

/// <summary>
/// Hosted service that seeds standardised data schemas from JSON files on startup.
/// Scans the blueprints/schemas/ subdirectories for *.json files and upserts them
/// into the schema store. Idempotent — skips schemas that already exist with the
/// same or newer version.
/// </summary>
public class SchemaSeedService : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SchemaSeedService> _logger;
    private readonly IWebHostEnvironment _environment;

    /// <summary>
    /// Organization ID used for seeded standardised schemas.
    /// Uses the System Admin Org well-known ID.
    /// </summary>
    internal const string SeedOrganizationId = "00000000-0000-0000-0000-000000000001";

    private static readonly JsonSerializerOptions CaseInsensitiveJson = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public SchemaSeedService(
        IServiceScopeFactory scopeFactory,
        ILogger<SchemaSeedService> logger,
        IWebHostEnvironment environment)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _environment = environment;
    }

    /// <summary>
    /// Scans the schemas directory and seeds schemas into the store on startup.
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var schemasPath = ResolveSchemasPath();

        if (!Directory.Exists(schemasPath))
        {
            _logger.LogWarning("Schemas directory not found at {Path}, skipping seed", schemasPath);
            return;
        }

        // Collect all JSON files from subdirectories
        var files = Directory.GetFiles(schemasPath, "*.json", SearchOption.AllDirectories);
        _logger.LogInformation("Found {Count} schema files in {Path}", files.Length, schemasPath);

        if (files.Length == 0)
            return;

        using var scope = _scopeFactory.CreateScope();

        // Use ISchemaRepository directly because ISchemaStore.CreateAsync/UpdateAsync
        // are restricted to Custom category schemas. Seeded standardised schemas use
        // Custom category with IsGloballyPublished=true and a system organization ID
        // to make them globally visible while working within the existing API constraints.
        var repository = scope.ServiceProvider.GetService<ISchemaRepository>();

        if (repository is null)
        {
            _logger.LogWarning("No ISchemaRepository registered, cannot seed schemas");
            return;
        }

        var seeded = 0;
        var skipped = 0;

        foreach (var file in files)
        {
            try
            {
                var json = await File.ReadAllTextAsync(file, cancellationToken);
                var schemaFile = JsonSerializer.Deserialize<SchemaFileFormat>(json, CaseInsensitiveJson);

                if (schemaFile is null || string.IsNullOrWhiteSpace(schemaFile.Identifier)
                                      || string.IsNullOrWhiteSpace(schemaFile.Title))
                {
                    _logger.LogWarning("Skipping invalid schema file {File}", Path.GetFileName(file));
                    skipped++;
                    continue;
                }

                // Check if schema already exists
                var existing = await repository.GetByIdentifierAsync(
                    schemaFile.Identifier, SeedOrganizationId, cancellationToken);

                if (existing is not null)
                {
                    // Compare versions — skip if existing is same or newer
                    if (CompareVersions(existing.Version, schemaFile.Version) >= 0)
                    {
                        _logger.LogDebug("Schema {Id} already exists at v{Version}, skipping",
                            schemaFile.Identifier, existing.Version);
                        skipped++;
                        continue;
                    }

                    // Update existing schema with newer version
                    existing.Title = schemaFile.Title;
                    existing.Description = schemaFile.Description;
                    existing.Version = schemaFile.Version;
                    existing.Content = BuildContent(schemaFile);
                    existing.SectorTags = BuildSectorTags(schemaFile);
                    existing.Keywords = schemaFile.Keywords;
                    existing.FieldCount = CountFields(schemaFile);
                    existing.FieldNames = ExtractFieldNames(schemaFile);

                    await repository.UpdateAsync(existing, cancellationToken);
                    seeded++;
                    _logger.LogInformation("Updated schema {Id}: {Title} to v{Version}",
                        schemaFile.Identifier, schemaFile.Title, schemaFile.Version);
                }
                else
                {
                    // Create new schema entry as Custom + globally published
                    var entry = new SchemaEntry
                    {
                        Identifier = schemaFile.Identifier,
                        Title = schemaFile.Title,
                        Description = schemaFile.Description,
                        Version = schemaFile.Version,
                        Category = SchemaCategory.Custom,
                        Source = SchemaSource.Internal(),
                        Status = SchemaStatus.Active,
                        OrganizationId = SeedOrganizationId,
                        IsGloballyPublished = true,
                        Content = BuildContent(schemaFile),
                        SectorTags = BuildSectorTags(schemaFile),
                        Keywords = schemaFile.Keywords,
                        FieldCount = CountFields(schemaFile),
                        FieldNames = ExtractFieldNames(schemaFile)
                    };

                    await repository.CreateAsync(entry, cancellationToken);
                    seeded++;
                    _logger.LogInformation("Seeded schema {Id}: {Title} v{Version}",
                        schemaFile.Identifier, schemaFile.Title, schemaFile.Version);
                }
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to parse schema file {File}", Path.GetFileName(file));
                skipped++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Error seeding schema from {File}", Path.GetFileName(file));
                skipped++;
            }
        }

        _logger.LogInformation("Schema seeding complete: {Seeded} seeded, {Skipped} skipped",
            seeded, skipped);
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// Resolves the path to the schemas directory.
    /// Checks multiple locations: content root, base directory, and parent paths.
    /// </summary>
    internal string ResolveSchemasPath()
    {
        // First check relative to content root (typical for development)
        var contentRootPath = Path.Combine(_environment.ContentRootPath, "blueprints", "schemas");
        if (Directory.Exists(contentRootPath))
            return contentRootPath;

        // Check relative to base directory (typical for published/Docker)
        var baseDirPath = Path.Combine(AppContext.BaseDirectory, "blueprints", "schemas");
        if (Directory.Exists(baseDirPath))
            return baseDirPath;

        // Walk up from content root to find solution-level blueprints/schemas
        var current = _environment.ContentRootPath;
        for (var i = 0; i < 6; i++)
        {
            var parent = Directory.GetParent(current);
            if (parent is null) break;
            current = parent.FullName;

            var candidate = Path.Combine(current, "blueprints", "schemas");
            if (Directory.Exists(candidate))
                return candidate;
        }

        // Fallback to base directory path (even if it doesn't exist yet)
        return baseDirPath;
    }

    /// <summary>
    /// Builds the Content JsonDocument from the schema file, embedding formLayout
    /// and disclosure as extensions within the JSON Schema content.
    /// </summary>
    private static JsonDocument BuildContent(SchemaFileFormat file)
    {
        var contentObj = new Dictionary<string, object?>();

        // Copy the JSON Schema properties
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

        var json = JsonSerializer.Serialize(contentObj);
        return JsonDocument.Parse(json);
    }

    /// <summary>
    /// Builds sector tags from category and tags.
    /// </summary>
    private static string[] BuildSectorTags(SchemaFileFormat file)
    {
        var tags = new List<string>();
        if (!string.IsNullOrEmpty(file.Category))
            tags.Add(file.Category);
        if (file.Tags is not null)
            tags.AddRange(file.Tags);
        return tags.Distinct().ToArray();
    }

    /// <summary>
    /// Counts top-level properties in the schema.
    /// </summary>
    private static int? CountFields(SchemaFileFormat file)
    {
        if (file.Schema is null) return null;
        if (file.Schema.Value.TryGetProperty("properties", out var props))
            return props.EnumerateObject().Count();
        return 0;
    }

    /// <summary>
    /// Extracts top-level property names from the schema.
    /// </summary>
    private static string[]? ExtractFieldNames(SchemaFileFormat file)
    {
        if (file.Schema is null) return null;
        if (file.Schema.Value.TryGetProperty("properties", out var props))
            return props.EnumerateObject().Select(p => p.Name).ToArray();
        return [];
    }

    /// <summary>
    /// Compares two semantic version strings. Returns negative if a &lt; b,
    /// zero if equal, positive if a &gt; b.
    /// </summary>
    internal static int CompareVersions(string a, string b)
    {
        var partsA = a.Split('.').Select(p => int.TryParse(p, out var v) ? v : 0).ToArray();
        var partsB = b.Split('.').Select(p => int.TryParse(p, out var v) ? v : 0).ToArray();

        var maxLen = Math.Max(partsA.Length, partsB.Length);
        for (var i = 0; i < maxLen; i++)
        {
            var va = i < partsA.Length ? partsA[i] : 0;
            var vb = i < partsB.Length ? partsB[i] : 0;
            if (va != vb) return va.CompareTo(vb);
        }

        return 0;
    }

    /// <summary>
    /// Deserialization target for standardised schema JSON files.
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
