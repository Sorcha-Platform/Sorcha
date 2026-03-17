// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Sorcha.Blueprint.Schemas.Services;

namespace Sorcha.Blueprint.Schemas.Tests;

/// <summary>
/// Tests for DppSchemaProvider — Digital Product Passport schema provider.
/// </summary>
public class DppSchemaProviderTests
{
    private readonly DppSchemaProvider _provider;

    public DppSchemaProviderTests()
    {
        var logger = new Mock<ILogger<DppSchemaProvider>>();
        _provider = new DppSchemaProvider(logger.Object);
    }

    [Fact]
    public void ProviderName_ReturnsDigitalProductPassport()
    {
        _provider.ProviderName.Should().Be("Digital Product Passport");
    }

    [Fact]
    public void DefaultSectorTags_ContainsSustainability()
    {
        _provider.DefaultSectorTags.Should().Contain("sustainability");
    }

    [Fact]
    public async Task IsAvailableAsync_ReturnsTrue()
    {
        var result = await _provider.IsAvailableAsync();
        result.Should().BeTrue();
    }

    [Fact]
    public async Task GetCatalogAsync_ReturnsAllSchemas()
    {
        var catalog = (await _provider.GetCatalogAsync()).ToList();

        catalog.Should().HaveCountGreaterThanOrEqualTo(10);
        catalog.Should().OnlyContain(s => s.Provider == "Digital Product Passport");
    }

    [Fact]
    public async Task GetCatalogAsync_AllSchemasHaveContent()
    {
        var catalog = (await _provider.GetCatalogAsync()).ToList();

        foreach (var schema in catalog)
        {
            schema.Content.Should().NotBeNull($"schema '{schema.Name}' should have content");
            schema.Content!.RootElement.GetProperty("$schema").GetString()
                .Should().Be("https://json-schema.org/draft/2020-12/schema");
            schema.Content.RootElement.GetProperty("type").GetString()
                .Should().Be("object");
        }
    }

    [Fact]
    public async Task GetCatalogAsync_AllSchemasHavePerSchemaSectorTags()
    {
        var catalog = (await _provider.GetCatalogAsync()).ToList();

        foreach (var schema in catalog)
        {
            schema.SectorTags.Should().NotBeNullOrEmpty(
                $"schema '{schema.Name}' should have per-schema sector tags");
            schema.SectorTags.Should().Contain("sustainability",
                $"schema '{schema.Name}' should include sustainability tag");
        }
    }

    [Fact]
    public async Task GetCatalogAsync_ContainsUntpSchemas()
    {
        var catalog = (await _provider.GetCatalogAsync()).ToList();

        catalog.Should().Contain(s => s.Name.Contains("UNTP") && s.Name.Contains("Product Passport"));
        catalog.Should().Contain(s => s.Name.Contains("UNTP") && s.Name.Contains("Conformity"));
        catalog.Should().Contain(s => s.Name.Contains("UNTP") && s.Name.Contains("Traceability"));
        catalog.Should().Contain(s => s.Name.Contains("UNTP") && s.Name.Contains("Facility"));
    }

    [Fact]
    public async Task GetCatalogAsync_ContainsBatteryPassSchemas()
    {
        var catalog = (await _provider.GetCatalogAsync()).ToList();

        catalog.Should().Contain(s => s.Name.Contains("Battery Pass") && s.Name.Contains("Battery Passport"));
        catalog.Should().Contain(s => s.Name.Contains("Battery Pass") && s.Name.Contains("Carbon Footprint"));
    }

    [Fact]
    public async Task GetCatalogAsync_ContainsCatenaXSchemas()
    {
        var catalog = (await _provider.GetCatalogAsync()).ToList();

        catalog.Should().Contain(s => s.Name.Contains("Catena-X") && s.Name.Contains("Digital Twin"));
        catalog.Should().Contain(s => s.Name.Contains("Catena-X") && s.Name.Contains("Carbon Footprint"));
        catalog.Should().Contain(s => s.Name.Contains("Catena-X") && s.Name.Contains("Traceability"));
    }

    [Fact]
    public async Task GetCatalogAsync_ContainsEsprSchema()
    {
        var catalog = (await _provider.GetCatalogAsync()).ToList();

        catalog.Should().Contain(s => s.Name.Contains("ESPR"));
    }

    [Fact]
    public async Task GetCatalogAsync_BatteryPassHasCommerceCrossTag()
    {
        var catalog = (await _provider.GetCatalogAsync()).ToList();
        var batteryPassport = catalog.Single(s => s.Name.Contains("Battery Passport"));

        batteryPassport.SectorTags.Should().Contain("commerce");
        batteryPassport.SectorTags.Should().Contain("construction");
    }

    [Fact]
    public async Task GetCatalogAsync_CarbonFootprintHasGovernmentCrossTag()
    {
        var catalog = (await _provider.GetCatalogAsync()).ToList();
        var carbonFootprint = catalog.First(s =>
            s.Name.Contains("Battery Pass") && s.Name.Contains("Carbon Footprint"));

        carbonFootprint.SectorTags.Should().Contain("government");
    }

    [Fact]
    public async Task GetCatalogAsync_FacilityRecordHasConstructionCrossTag()
    {
        var catalog = (await _provider.GetCatalogAsync()).ToList();
        var facility = catalog.Single(s => s.Name.Contains("Facility Record"));

        facility.SectorTags.Should().Contain("construction");
        facility.SectorTags.Should().Contain("government");
    }

    [Fact]
    public async Task SearchAsync_FindsByName()
    {
        var result = await _provider.SearchAsync("battery");

        result.Results.Should().HaveCountGreaterThanOrEqualTo(1);
        result.Results.Should().OnlyContain(s =>
            s.Name.Contains("battery", StringComparison.OrdinalIgnoreCase) ||
            s.Description.Contains("battery", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task SearchAsync_FindsByDescription()
    {
        var result = await _provider.SearchAsync("carbon footprint");

        result.Results.Should().HaveCountGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task SearchAsync_EmptyQuery_ReturnsEmpty()
    {
        var result = await _provider.SearchAsync("");

        result.Results.Should().BeEmpty();
        result.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task SearchAsync_NoMatch_ReturnsEmpty()
    {
        var result = await _provider.SearchAsync("xyznonexistent");

        result.Results.Should().BeEmpty();
    }

    [Fact]
    public async Task GetSchemaAsync_ValidUrl_ReturnsSchema()
    {
        var catalog = (await _provider.GetCatalogAsync()).ToList();
        var first = catalog.First();

        var result = await _provider.GetSchemaAsync(first.Url);

        result.Should().NotBeNull();
        result!.Name.Should().Be(first.Name);
        result.Content.Should().NotBeNull();
    }

    [Fact]
    public async Task GetSchemaAsync_InvalidUrl_ReturnsNull()
    {
        var result = await _provider.GetSchemaAsync("urn:nonexistent:schema");

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetCatalogAsync_SchemasHaveProperties()
    {
        var catalog = (await _provider.GetCatalogAsync()).ToList();

        foreach (var schema in catalog)
        {
            var root = schema.Content!.RootElement;
            root.TryGetProperty("properties", out var props).Should().BeTrue(
                $"schema '{schema.Name}' should have properties");
            props.EnumerateObject().Count().Should().BeGreaterThanOrEqualTo(3,
                $"schema '{schema.Name}' should have at least 3 properties");
        }
    }

    [Fact]
    public async Task GetCatalogAsync_SchemasHaveDescriptiveProperties()
    {
        var catalog = (await _provider.GetCatalogAsync()).ToList();

        foreach (var schema in catalog)
        {
            var properties = schema.Content!.RootElement.GetProperty("properties");
            foreach (var prop in properties.EnumerateObject())
            {
                prop.Value.TryGetProperty("description", out var desc).Should().BeTrue(
                    $"property '{prop.Name}' in schema '{schema.Name}' should have a description");
                desc.GetString().Should().NotBeNullOrWhiteSpace();
            }
        }
    }

    [Fact]
    public async Task GetCatalogAsync_SchemasHaveSourceInDescription()
    {
        var catalog = (await _provider.GetCatalogAsync()).ToList();

        foreach (var schema in catalog)
        {
            var desc = schema.Content!.RootElement.GetProperty("description").GetString();
            desc.Should().Contain("Source:",
                $"schema '{schema.Name}' content description should include source attribution");
        }
    }

    [Fact]
    public async Task GetCatalogAsync_CrossSectorCoverage_AtLeastThreeSectors()
    {
        var catalog = (await _provider.GetCatalogAsync()).ToList();

        var allSectors = catalog
            .Where(s => s.SectorTags is not null)
            .SelectMany(s => s.SectorTags!)
            .Distinct()
            .ToList();

        allSectors.Should().HaveCountGreaterThanOrEqualTo(3,
            "DPP schemas should span at least 3 different sector tags");
        allSectors.Should().Contain("sustainability");
        allSectors.Should().Contain("commerce");
        allSectors.Should().Contain("government");
    }
}
