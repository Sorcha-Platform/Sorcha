// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Sorcha.Blueprint.Schemas;

/// <summary>
/// Repository for built-in JSON Schemas shipped with Sorcha
/// </summary>
public class BuiltInSchemaRepository : ISchemaRepository
{
    private readonly List<SchemaDocument> _schemas = [];
    private readonly ILogger<BuiltInSchemaRepository> _logger;
    private volatile bool _initialized = false;

    public SchemaSource SourceType => SchemaSource.BuiltIn;

    public BuiltInSchemaRepository(ILogger<BuiltInSchemaRepository> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        InitializeBuiltInSchemas();
    }

    public Task<IEnumerable<SchemaDocument>> GetAllSchemasAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IEnumerable<SchemaDocument>>(_schemas);
    }

    public Task<SchemaDocument?> GetSchemaByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        var schema = _schemas.FirstOrDefault(s => s.Metadata.Id == id);
        return Task.FromResult(schema);
    }

    public Task<IEnumerable<SchemaDocument>> SearchSchemasAsync(string query, CancellationToken cancellationToken = default)
    {
        query = query.ToLowerInvariant();
        var results = _schemas.Where(s =>
            s.Metadata.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            s.Metadata.Description.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            s.Metadata.Tags.Any(t => t.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
            s.PropertyNames.Any(p => p.Contains(query, StringComparison.OrdinalIgnoreCase))
        );

        return Task.FromResult(results);
    }

    public Task<IEnumerable<SchemaDocument>> GetSchemasByCategoryAsync(string category, CancellationToken cancellationToken = default)
    {
        var results = _schemas.Where(s =>
            s.Metadata.Category.Equals(category, StringComparison.OrdinalIgnoreCase)
        );

        return Task.FromResult(results);
    }

    public Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        // Built-in schemas don't need refreshing
        return Task.CompletedTask;
    }

    private void InitializeBuiltInSchemas()
    {
        if (_initialized) return;

        // Load schemas from embedded JSON files
        var assembly = Assembly.GetExecutingAssembly();
        var resourceNames = assembly.GetManifestResourceNames()
            .Where(name => name.Contains("BuiltInSchemas") && name.EndsWith(".json"))
            .OrderBy(name => name);

        foreach (var resourceName in resourceNames)
        {
            try
            {
                using var stream = assembly.GetManifestResourceStream(resourceName);
                if (stream == null) continue;

                using var reader = new StreamReader(stream);
                var jsonContent = reader.ReadToEnd();

                // Parse JSON once and reuse for both metadata extraction and schema storage
                var jsonDoc = JsonDocument.Parse(jsonContent);

                // Extract metadata from the schema
                var root = jsonDoc.RootElement;
                var schemaId = root.GetProperty("$id").GetString() ?? string.Empty;
                var title = root.GetProperty("title").GetString() ?? "Unknown";
                var description = root.GetProperty("description").GetString() ?? string.Empty;

                // Extract property names from the schema
                var propertyNames = new List<string>();
                if (root.TryGetProperty("properties", out var properties))
                {
                    foreach (var prop in properties.EnumerateObject())
                    {
                        propertyNames.Add(prop.Name);
                    }
                }

                // Determine category and tags based on the schema
                var (category, tags) = DetermineMetadata(title, description, propertyNames);

                // Reuse the already-parsed jsonDoc (do NOT dispose — SchemaDocument takes ownership)
                _schemas.Add(new SchemaDocument
                {
                    Metadata = new SchemaMetadata
                    {
                        Id = schemaId,
                        Title = title,
                        Description = description,
                        Version = "1.0.0",
                        Category = category,
                        Tags = tags,
                        Source = SchemaSource.BuiltIn,
                        Author = "Sorcha Contributors",
                        License = "MIT"
                    },
                    Schema = jsonDoc,
                    PropertyNames = propertyNames
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load schema from {ResourceName}", resourceName);
            }
        }

        _initialized = true;
    }

    private static (string category, List<string> tags) DetermineMetadata(string title, string description, List<string> propertyNames)
    {
        var titleLower = title.ToLowerInvariant();
        var descriptionLower = description.ToLowerInvariant();
        var allText = $"{titleLower} {descriptionLower} {string.Join(" ", propertyNames)}".ToLowerInvariant();

        // Determine category
        string category = "General";
        List<string> tags = [titleLower];

        if (allText.Contains("person") || allText.Contains("firstname") || allText.Contains("lastname"))
        {
            category = "Identity";
            tags.AddRange(["person", "contact", "identity", "name"]);
        }
        else if (allText.Contains("address") || allText.Contains("street") || allText.Contains("postal"))
        {
            category = "Location";
            tags.AddRange(["address", "location", "postal", "street"]);
        }
        else if (allText.Contains("loan") || allText.Contains("credit") || allText.Contains("mortgage") ||
                 allText.Contains("interest") || allText.Contains("debt"))
        {
            category = "Finance";
            tags.AddRange(["loan", "credit", "finance", "banking", "lending"]);

            if (allText.Contains("request") || allText.Contains("application"))
                tags.Add("loan-application");
            if (allText.Contains("evaluation") || allText.Contains("approval") || allText.Contains("granted"))
                tags.Add("loan-evaluation");
        }
        else if (allText.Contains("payment") || allText.Contains("currency") || allText.Contains("amount"))
        {
            category = "Finance";
            tags.AddRange(["payment", "money", "transaction", "currency"]);
        }
        else if (allText.Contains("document") || allText.Contains("file") || allText.Contains("filename"))
        {
            category = "Files";
            tags.AddRange(["document", "file", "attachment"]);
        }

        // Add property-based tags
        if (propertyNames.Contains("email"))
            tags.Add("email");
        if (propertyNames.Contains("phone"))
            tags.Add("phone");
        if (propertyNames.Contains("loanAmount"))
            tags.Add("loan");
        if (propertyNames.Contains("creditScore"))
            tags.Add("credit");
        if (propertyNames.Contains("annualIncome") || propertyNames.Contains("income"))
            tags.Add("income");

        return (category, tags.Distinct().ToList());
    }
}
