// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Sorcha.Blueprint.Schemas.DTOs;

namespace Sorcha.Blueprint.Schemas.Services;

/// <summary>
/// Provider for Digital Product Passport (DPP) schemas from UNTP, Battery Pass,
/// and Catena-X/Tractus-X standards. Each schema carries per-schema sector tags
/// for cross-sector discoverability.
/// </summary>
public class DppSchemaProvider : IExternalSchemaProvider
{
    private readonly ILogger<DppSchemaProvider> _logger;
    private List<ExternalSchemaResult>? _catalogCache;

    private const string ProviderDisplayName = "Digital Product Passport";

    public DppSchemaProvider(ILogger<DppSchemaProvider> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public string ProviderName => ProviderDisplayName;

    /// <inheritdoc />
    public string[] DefaultSectorTags => ["sustainability"];

    /// <inheritdoc />
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
        return Task.FromResult(true); // Static schemas — always available
    }

    private List<ExternalSchemaResult> BuildCatalog()
    {
        if (_catalogCache is not null) return _catalogCache;

        _catalogCache =
        [
            // === UNTP (UN/CEFACT) Schemas ===
            BuildSchema(
                name: "UNTP Digital Product Passport",
                description: "UN Trade Facilitation (UNTP) Digital Product Passport — core product identity, provenance, and sustainability claims for cross-border trade",
                url: "urn:untp:dpp:digital-product-passport:0.5.0",
                sectorTags: ["sustainability", "commerce"],
                version: "0.5.0",
                source: "UNTP/UN CEFACT",
                properties: new Dictionary<string, (string Type, string Description)>
                {
                    ["id"] = ("string", "Unique product passport identifier (URI)"),
                    ["issuedBy"] = ("object", "The organisation that issued this passport"),
                    ["product"] = ("object", "Product identification including GTIN, batch, and serial numbers"),
                    ["sustainabilityClaims"] = ("array", "Verified sustainability and compliance claims"),
                    ["circularity"] = ("object", "Circularity information including recyclability and material composition"),
                    ["traceabilityEvents"] = ("array", "Supply chain traceability events linked to this product"),
                    ["conformityCertificates"] = ("array", "References to conformity assessment certificates"),
                    ["issuedAt"] = ("string", "ISO 8601 date-time when the passport was issued"),
                }),

            BuildSchema(
                name: "UNTP Digital Conformity Credential",
                description: "UN Trade Facilitation (UNTP) Digital Conformity Credential — verifiable proof of regulatory or standards compliance",
                url: "urn:untp:dcc:digital-conformity-credential:0.5.0",
                sectorTags: ["sustainability", "government"],
                version: "0.5.0",
                source: "UNTP/UN CEFACT",
                properties: new Dictionary<string, (string Type, string Description)>
                {
                    ["id"] = ("string", "Unique credential identifier (URI)"),
                    ["issuer"] = ("object", "Conformity assessment body that issued the credential"),
                    ["subject"] = ("object", "Entity or product assessed for conformity"),
                    ["assessment"] = ("object", "Assessment details including standard, scope, and outcome"),
                    ["evidence"] = ("array", "Supporting evidence for the conformity assessment"),
                    ["validFrom"] = ("string", "Date from which the credential is valid"),
                    ["validUntil"] = ("string", "Expiration date of the credential"),
                }),

            BuildSchema(
                name: "UNTP Digital Traceability Event",
                description: "UN Trade Facilitation (UNTP) Digital Traceability Event — supply chain event recording transformation, aggregation, or transaction of goods",
                url: "urn:untp:dte:digital-traceability-event:0.5.0",
                sectorTags: ["sustainability", "commerce"],
                version: "0.5.0",
                source: "UNTP/UN CEFACT",
                properties: new Dictionary<string, (string Type, string Description)>
                {
                    ["id"] = ("string", "Unique event identifier"),
                    ["eventType"] = ("string", "Type of event: transformation, aggregation, transaction, or observation"),
                    ["eventTime"] = ("string", "ISO 8601 date-time when the event occurred"),
                    ["actor"] = ("object", "Organisation or entity that performed the event"),
                    ["location"] = ("object", "Geographic location where the event took place"),
                    ["inputProducts"] = ("array", "Products consumed or used in this event"),
                    ["outputProducts"] = ("array", "Products created or modified by this event"),
                    ["certifications"] = ("array", "Conformity credentials associated with this event"),
                }),

            BuildSchema(
                name: "UNTP Digital Facility Record",
                description: "UN Trade Facilitation (UNTP) Digital Facility Record — sustainability and compliance data for a manufacturing or processing facility",
                url: "urn:untp:dfr:digital-facility-record:0.5.0",
                sectorTags: ["sustainability", "construction", "government"],
                version: "0.5.0",
                source: "UNTP/UN CEFACT",
                properties: new Dictionary<string, (string Type, string Description)>
                {
                    ["id"] = ("string", "Unique facility identifier"),
                    ["name"] = ("string", "Facility name"),
                    ["operatedBy"] = ("object", "Organisation operating the facility"),
                    ["location"] = ("object", "Geographic coordinates and address"),
                    ["processes"] = ("array", "Manufacturing or processing activities at the facility"),
                    ["environmentalFootprint"] = ("object", "Emissions, water usage, and waste metrics"),
                    ["certifications"] = ("array", "Facility-level conformity credentials"),
                }),

            // === Battery Pass Schemas ===
            BuildSchema(
                name: "Battery Pass — Battery Passport",
                description: "EU Battery Regulation (2023/1542) compliant battery passport — full lifecycle data including chemistry, capacity, carbon footprint, and recycled content",
                url: "urn:battery-pass:battery-passport:1.0.0",
                sectorTags: ["sustainability", "commerce", "construction"],
                version: "1.0.0",
                source: "Battery Pass Consortium",
                properties: new Dictionary<string, (string Type, string Description)>
                {
                    ["batteryId"] = ("string", "Unique battery identifier per EU regulation"),
                    ["manufacturer"] = ("object", "Battery manufacturer identity and location"),
                    ["chemistry"] = ("object", "Battery chemistry type and cathode/anode materials"),
                    ["capacity"] = ("object", "Rated capacity, energy density, and voltage characteristics"),
                    ["stateOfHealth"] = ("object", "Current state of health, cycle count, and degradation metrics"),
                    ["carbonFootprint"] = ("object", "Lifecycle carbon footprint (kg CO2e) per EU calculation methodology"),
                    ["recycledContent"] = ("object", "Percentage of recycled cobalt, lithium, nickel, and lead"),
                    ["dueDiligence"] = ("object", "Supply chain due diligence information per EU requirements"),
                    ["materialComposition"] = ("object", "Hazardous substances and critical raw materials declaration"),
                }),

            BuildSchema(
                name: "Battery Pass — Carbon Footprint Declaration",
                description: "Battery lifecycle carbon footprint per EU Battery Regulation methodology — cradle-to-gate emissions by lifecycle stage",
                url: "urn:battery-pass:carbon-footprint:1.0.0",
                sectorTags: ["sustainability", "government"],
                version: "1.0.0",
                source: "Battery Pass Consortium",
                properties: new Dictionary<string, (string Type, string Description)>
                {
                    ["batteryId"] = ("string", "Reference to the battery this footprint applies to"),
                    ["totalCarbonFootprint"] = ("number", "Total lifecycle carbon footprint in kg CO2 equivalent"),
                    ["rawMaterialAcquisition"] = ("number", "Emissions from raw material extraction and processing"),
                    ["mainProcessing"] = ("number", "Emissions from battery cell and module manufacturing"),
                    ["distribution"] = ("number", "Emissions from transport and distribution"),
                    ["endOfLife"] = ("number", "Emissions or credits from recycling and disposal"),
                    ["calculationMethodology"] = ("string", "Reference to EU delegated act calculation rules"),
                    ["performanceClass"] = ("string", "Carbon footprint performance class (A-E)"),
                }),

            // === Catena-X / Tractus-X Schemas ===
            BuildSchema(
                name: "Catena-X Digital Twin — Aspect Model",
                description: "Eclipse Tractus-X digital twin aspect model — standardised product data exchange across the automotive supply chain",
                url: "urn:catena-x:aspect-model:digital-twin:3.0.0",
                sectorTags: ["sustainability", "commerce"],
                version: "3.0.0",
                source: "Eclipse Tractus-X / Catena-X",
                properties: new Dictionary<string, (string Type, string Description)>
                {
                    ["catenaXId"] = ("string", "Catena-X unique identifier for the digital twin"),
                    ["manufacturerPartId"] = ("string", "Part identifier assigned by the manufacturer"),
                    ["nameAtManufacturer"] = ("string", "Part name as used by the manufacturer"),
                    ["classification"] = ("object", "Part classification per industry standards"),
                    ["aspectModels"] = ("array", "References to bound aspect model submodels"),
                    ["specificAssetIds"] = ("array", "Asset identifiers like serial number, batch ID, or VAN"),
                }),

            BuildSchema(
                name: "Catena-X Product Carbon Footprint",
                description: "Eclipse Tractus-X PCF (Product Carbon Footprint) — Catena-X standardised carbon footprint exchange per Pathfinder framework",
                url: "urn:catena-x:pcf:product-carbon-footprint:3.0.0",
                sectorTags: ["sustainability", "commerce", "government"],
                version: "3.0.0",
                source: "Eclipse Tractus-X / Catena-X",
                properties: new Dictionary<string, (string Type, string Description)>
                {
                    ["id"] = ("string", "Unique PCF dataset identifier"),
                    ["specVersion"] = ("string", "Pathfinder framework specification version"),
                    ["productId"] = ("string", "Identifier of the product this footprint covers"),
                    ["pcfExcludingBiogenic"] = ("number", "Product carbon footprint excluding biogenic CO2 (kg CO2e)"),
                    ["pcfIncludingBiogenic"] = ("number", "Product carbon footprint including biogenic CO2 (kg CO2e)"),
                    ["fossilGhgEmissions"] = ("number", "Fossil greenhouse gas emissions (kg CO2e)"),
                    ["biogenicCarbonContent"] = ("number", "Biogenic carbon content of the product (kg C)"),
                    ["reportingPeriodStart"] = ("string", "Start of the emissions reporting period"),
                    ["reportingPeriodEnd"] = ("string", "End of the emissions reporting period"),
                    ["geographyCountry"] = ("string", "ISO 3166-1 country code for production geography"),
                }),

            BuildSchema(
                name: "Catena-X Material Traceability",
                description: "Eclipse Tractus-X material traceability — batch and serialised part relationships for supply chain transparency in automotive and manufacturing",
                url: "urn:catena-x:traceability:material:2.0.0",
                sectorTags: ["sustainability", "commerce"],
                version: "2.0.0",
                source: "Eclipse Tractus-X / Catena-X",
                properties: new Dictionary<string, (string Type, string Description)>
                {
                    ["catenaXId"] = ("string", "Catena-X identifier for this material batch or part"),
                    ["batchId"] = ("string", "Manufacturer batch identifier"),
                    ["partTypeInformation"] = ("object", "Classification of the part including name and category"),
                    ["manufacturingInformation"] = ("object", "Manufacturing date, country, and site details"),
                    ["childParts"] = ("array", "Component parts assembled into this part (bill of materials)"),
                    ["parentParts"] = ("array", "Assemblies that include this part"),
                }),

            // === Cross-standard: EU ESPR ===
            BuildSchema(
                name: "EU ESPR Product Data Sheet",
                description: "EU Ecodesign for Sustainable Products Regulation (ESPR) product data sheet — minimum sustainability data requirements for products sold in the EU single market",
                url: "urn:eu:espr:product-data-sheet:1.0.0",
                sectorTags: ["sustainability", "commerce", "government"],
                version: "1.0.0",
                source: "EU ESPR",
                properties: new Dictionary<string, (string Type, string Description)>
                {
                    ["productIdentifier"] = ("string", "Unique product model identifier"),
                    ["manufacturer"] = ("object", "Manufacturer identity, address, and registration"),
                    ["energyEfficiency"] = ("object", "Energy label class and consumption data"),
                    ["durability"] = ("object", "Expected product lifetime, repairability score, and spare parts availability"),
                    ["recycledContent"] = ("object", "Percentage of recycled materials by type"),
                    ["substancesOfConcern"] = ("array", "REACH and RoHS regulated substance declarations"),
                    ["carbonFootprint"] = ("object", "Product lifecycle carbon footprint"),
                    ["digitalProductPassportUrl"] = ("string", "URI to the full digital product passport"),
                }),
        ];

        _logger.LogInformation("Built {Count} DPP schemas from UNTP, Battery Pass, and Catena-X sources",
            _catalogCache.Count);
        return _catalogCache;
    }

    private static ExternalSchemaResult BuildSchema(
        string name,
        string description,
        string url,
        string[] sectorTags,
        string version,
        string source,
        Dictionary<string, (string Type, string Description)> properties)
    {
        var propsNode = new JsonObject();
        var required = new JsonArray();

        foreach (var (propName, (type, desc)) in properties)
        {
            propsNode[propName] = new JsonObject
            {
                ["type"] = type,
                ["description"] = desc
            };

            // First two properties are required by convention (id/key fields)
            if (propsNode.Count <= 2)
                required.Add(propName);
        }

        var schema = new JsonObject
        {
            ["$schema"] = "https://json-schema.org/draft/2020-12/schema",
            ["$id"] = url,
            ["title"] = name,
            ["description"] = $"{description} (Source: {source}, v{version})",
            ["type"] = "object",
            ["properties"] = propsNode,
            ["required"] = required,
        };

        return new ExternalSchemaResult(
            Name: name,
            Description: description,
            Url: url,
            Provider: ProviderDisplayName,
            Content: JsonDocument.Parse(schema.ToJsonString()),
            SectorTags: sectorTags);
    }
}
