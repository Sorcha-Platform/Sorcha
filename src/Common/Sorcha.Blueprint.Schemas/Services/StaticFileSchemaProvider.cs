// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Sorcha.Blueprint.Schemas.DTOs;

namespace Sorcha.Blueprint.Schemas.Services;

/// <summary>
/// Provider that serves schemas from static, curated definitions.
/// Covers NIEM and IFC domains with pre-built JSON Schema representations.
/// </summary>
public class StaticFileSchemaProvider : IExternalSchemaProvider
{
    private readonly ILogger<StaticFileSchemaProvider> _logger;
    private readonly string _providerName;
    private readonly string[] _sectorTags;
    private readonly Dictionary<string, (string Description, JsonDocument Schema)> _schemas;
    private List<ExternalSchemaResult>? _catalogCache;

    public StaticFileSchemaProvider(
        string providerName,
        string[] sectorTags,
        Dictionary<string, (string Description, JsonDocument Schema)> schemas,
        ILogger<StaticFileSchemaProvider> logger)
    {
        _providerName = providerName;
        _sectorTags = sectorTags;
        _schemas = schemas;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string ProviderName => _providerName;
    public Models.ProviderType ProviderType => Models.ProviderType.StaticFile;
    public string[] DefaultSectorTags => _sectorTags;

    public Task<ExternalSchemaSearchResponse> SearchAsync(string query, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return Task.FromResult(new ExternalSchemaSearchResponse([], 0, ProviderName, query));

        var catalog = BuildCatalog();
        var matches = catalog
            .Where(r => r.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                        r.Description.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return Task.FromResult(new ExternalSchemaSearchResponse(matches, matches.Count, ProviderName, query));
    }

    public Task<ExternalSchemaResult?> GetSchemaAsync(string schemaUrl, CancellationToken cancellationToken = default)
    {
        var catalog = BuildCatalog();
        return Task.FromResult(catalog.FirstOrDefault(r =>
            string.Equals(r.Url, schemaUrl, StringComparison.OrdinalIgnoreCase)));
    }

    public Task<IEnumerable<ExternalSchemaResult>> GetCatalogAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IEnumerable<ExternalSchemaResult>>(BuildCatalog());
    }

    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(true); // Static — always available
    }

    private List<ExternalSchemaResult> BuildCatalog()
    {
        if (_catalogCache is not null) return _catalogCache;

        _catalogCache = _schemas.Select(kvp => new ExternalSchemaResult(
            Name: kvp.Key,
            Description: kvp.Value.Description,
            Url: $"urn:static:{_providerName.ToLowerInvariant()}:{kvp.Key.ToLowerInvariant().Replace(' ', '-')}",
            Provider: _providerName,
            Content: kvp.Value.Schema)).ToList();

        _logger.LogInformation("Built {Count} {Provider} schemas", _catalogCache.Count, _providerName);
        return _catalogCache;
    }

    /// <summary>
    /// Creates a NIEM provider with curated schemas.
    /// </summary>
    public static StaticFileSchemaProvider CreateNiemProvider(ILogger<StaticFileSchemaProvider> logger)
    {
        var schemas = new Dictionary<string, (string, JsonDocument)>
        {
            ["NIEM Person"] = ("NIEM Person type — name, SSN, demographics for justice and government exchanges",
                BuildSchema("NIEM Person", "niem:Person", ["name", "birthDate", "ssn", "sex", "race"])),
            ["NIEM Activity"] = ("NIEM Activity type — an occurrence or event with participants and dates",
                BuildSchema("NIEM Activity", "niem:Activity", ["activityDate", "activityDescription", "activityStatus"])),
            ["NIEM Location"] = ("NIEM Location type — physical location with address and coordinates",
                BuildSchema("NIEM Location", "niem:Location", ["locationAddress", "locationName", "locationCoordinates"])),
            ["NIEM Organization"] = ("NIEM Organization type — formal group with identification and structure",
                BuildSchema("NIEM Organization", "niem:Organization", ["organizationName", "organizationId", "organizationType"])),
            ["NIEM Case"] = ("NIEM Case type — legal or administrative case record",
                BuildSchema("NIEM Case", "niem:Case", ["caseId", "caseTitle", "caseStatus", "caseFilingDate"])),
            ["NIEM Document"] = ("NIEM Document type — formal record with metadata and content",
                BuildSchema("NIEM Document", "niem:Document", ["documentId", "documentTitle", "documentDate", "documentType"])),
        };

        return new StaticFileSchemaProvider("NIEM", ["government", "identity"], schemas, logger);
    }

    /// <summary>
    /// Creates an IFC provider with curated schemas.
    /// </summary>
    public static StaticFileSchemaProvider CreateIfcProvider(ILogger<StaticFileSchemaProvider> logger)
    {
        var schemas = new Dictionary<string, (string, JsonDocument)>
        {
            ["IFC Building"] = ("IFC Building — a building structure with storeys and spatial elements",
                BuildSchema("IFC Building", "ifc:IfcBuilding", ["name", "description", "elevation", "storeyCount"])),
            ["IFC Site"] = ("IFC Site — a defined area of land on which construction takes place",
                BuildSchema("IFC Site", "ifc:IfcSite", ["name", "refLatitude", "refLongitude", "refElevation"])),
            ["IFC Space"] = ("IFC Space — an area or volume within a building that has a particular use",
                BuildSchema("IFC Space", "ifc:IfcSpace", ["name", "description", "area", "volume"])),
            ["IFC Wall"] = ("IFC Wall — a vertical construction element that bounds or subdivides spaces",
                BuildSchema("IFC Wall", "ifc:IfcWall", ["name", "height", "width", "material"])),
            ["IFC Project"] = ("IFC Project — the root element representing the construction project",
                BuildSchema("IFC Project", "ifc:IfcProject", ["name", "description", "phase", "units"])),
        };

        return new StaticFileSchemaProvider("IFC", ["construction"], schemas, logger);
    }

    private static JsonDocument BuildSchema(string title, string id, string[] fields)
    {
        var properties = new JsonObject();
        foreach (var field in fields)
        {
            properties[field] = new JsonObject { ["type"] = "string" };
        }

        var schema = new JsonObject
        {
            ["$schema"] = "https://json-schema.org/draft/2020-12/schema",
            ["$id"] = id,
            ["title"] = title,
            ["type"] = "object",
            ["properties"] = properties,
        };

        return JsonDocument.Parse(schema.ToJsonString());
    }
}
