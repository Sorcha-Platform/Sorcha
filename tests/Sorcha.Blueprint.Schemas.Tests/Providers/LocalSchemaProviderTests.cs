// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Sorcha.Blueprint.Schemas.Services;

namespace Sorcha.Blueprint.Schemas.Tests.Providers;

public class LocalSchemaProviderTests : IDisposable
{
    private readonly string _tempDir;
    private readonly Mock<ILogger<LocalSchemaProvider>> _logger = new();

    public LocalSchemaProviderTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"sorcha-schema-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public void ProviderName_IsSorchaLocal()
    {
        var provider = new LocalSchemaProvider(_tempDir, _logger.Object);
        provider.ProviderName.Should().Be("Sorcha Local");
    }

    [Fact]
    public async Task IsAvailableAsync_ExistingDirectory_ReturnsTrue()
    {
        var provider = new LocalSchemaProvider(_tempDir, _logger.Object);
        var result = await provider.IsAvailableAsync();
        result.Should().BeTrue();
    }

    [Fact]
    public async Task IsAvailableAsync_MissingDirectory_ReturnsFalse()
    {
        var provider = new LocalSchemaProvider("/nonexistent/path", _logger.Object);
        var result = await provider.IsAvailableAsync();
        result.Should().BeFalse();
    }

    [Fact]
    public async Task GetCatalogAsync_EmptyDirectory_ReturnsEmpty()
    {
        var provider = new LocalSchemaProvider(_tempDir, _logger.Object);
        var catalog = (await provider.GetCatalogAsync()).ToList();
        catalog.Should().BeEmpty();
    }

    [Fact]
    public async Task GetCatalogAsync_WithSchemaFiles_ReturnsCatalog()
    {
        WriteSchemaFile("people-identity", "uk-address", "UK Address",
            "Standard UK postal address", "people-identity",
            ["address", "uk", "postal"]);

        WriteSchemaFile("financial", "invoice-line-item", "Invoice Line Item",
            "Standard invoice line item", "financial",
            ["invoice", "finance"]);

        var provider = new LocalSchemaProvider(_tempDir, _logger.Object);
        var catalog = (await provider.GetCatalogAsync()).ToList();

        catalog.Should().HaveCount(2);
        catalog.Should().Contain(r => r.Name == "UK Address");
        catalog.Should().Contain(r => r.Name == "Invoice Line Item");
    }

    [Fact]
    public async Task GetCatalogAsync_SchemaHasCorrectUri()
    {
        WriteSchemaFile("people-identity", "uk-address", "UK Address",
            "Standard UK postal address", "people-identity", []);

        var provider = new LocalSchemaProvider(_tempDir, _logger.Object);
        var catalog = (await provider.GetCatalogAsync()).ToList();

        catalog.Single().Url.Should().Be("urn:sorcha:schema:uk-address");
    }

    [Fact]
    public async Task GetCatalogAsync_SchemaHasSectorTags()
    {
        WriteSchemaFile("people-identity", "uk-address", "UK Address",
            "Standard UK postal address", "people-identity",
            ["address", "uk", "postal"]);

        var provider = new LocalSchemaProvider(_tempDir, _logger.Object);
        var catalog = (await provider.GetCatalogAsync()).ToList();

        var entry = catalog.Single();
        entry.SectorTags.Should().Contain("people-identity");
        entry.SectorTags.Should().Contain("address");
        entry.SectorTags.Should().Contain("uk");
    }

    [Fact]
    public async Task GetCatalogAsync_SchemaHasContent()
    {
        WriteSchemaFile("people-identity", "uk-address", "UK Address",
            "Standard UK postal address", "people-identity", []);

        var provider = new LocalSchemaProvider(_tempDir, _logger.Object);
        var catalog = (await provider.GetCatalogAsync()).ToList();

        var entry = catalog.Single();
        entry.Content.Should().NotBeNull();
        entry.Content!.RootElement.TryGetProperty("properties", out var props).Should().BeTrue();
        props.TryGetProperty("addressLine1", out _).Should().BeTrue();
    }

    [Fact]
    public async Task GetCatalogAsync_EmbedsFomLayoutExtension()
    {
        WriteSchemaFile("people-identity", "uk-address", "UK Address",
            "Standard UK postal address", "people-identity", [],
            includeFormLayout: true);

        var provider = new LocalSchemaProvider(_tempDir, _logger.Object);
        var catalog = (await provider.GetCatalogAsync()).ToList();

        var content = catalog.Single().Content!.RootElement;
        content.TryGetProperty("x-sorcha-formLayout", out var layout).Should().BeTrue();
        layout.GetProperty("type").GetString().Should().Be("VerticalLayout");
    }

    [Fact]
    public async Task GetCatalogAsync_EmbedsDisclosureExtension()
    {
        WriteSchemaFile("people-identity", "uk-address", "UK Address",
            "Standard UK postal address", "people-identity", [],
            includeDisclosure: true);

        var provider = new LocalSchemaProvider(_tempDir, _logger.Object);
        var catalog = (await provider.GetCatalogAsync()).ToList();

        var content = catalog.Single().Content!.RootElement;
        content.TryGetProperty("x-sorcha-disclosure", out var disclosure).Should().BeTrue();
        disclosure.TryGetProperty("recommendation", out _).Should().BeTrue();
    }

    [Fact]
    public async Task SearchAsync_MatchingQuery_ReturnsResults()
    {
        WriteSchemaFile("people-identity", "uk-address", "UK Address",
            "Standard UK postal address", "people-identity",
            ["address", "uk"]);

        var provider = new LocalSchemaProvider(_tempDir, _logger.Object);
        var result = await provider.SearchAsync("address");

        result.Results.Should().HaveCount(1);
        result.Results.First().Name.Should().Be("UK Address");
    }

    [Fact]
    public async Task SearchAsync_NoMatch_ReturnsEmpty()
    {
        WriteSchemaFile("people-identity", "uk-address", "UK Address",
            "Standard UK postal address", "people-identity", []);

        var provider = new LocalSchemaProvider(_tempDir, _logger.Object);
        var result = await provider.SearchAsync("nonexistent");

        result.Results.Should().BeEmpty();
    }

    [Fact]
    public async Task SearchAsync_EmptyQuery_ReturnsEmpty()
    {
        var provider = new LocalSchemaProvider(_tempDir, _logger.Object);
        var result = await provider.SearchAsync("");

        result.Results.Should().BeEmpty();
    }

    [Fact]
    public async Task GetSchemaAsync_ExistingUri_ReturnsSchema()
    {
        WriteSchemaFile("people-identity", "uk-address", "UK Address",
            "Standard UK postal address", "people-identity", []);

        var provider = new LocalSchemaProvider(_tempDir, _logger.Object);
        var result = await provider.GetSchemaAsync("urn:sorcha:schema:uk-address");

        result.Should().NotBeNull();
        result!.Name.Should().Be("UK Address");
    }

    [Fact]
    public async Task GetSchemaAsync_NonexistentUri_ReturnsNull()
    {
        var provider = new LocalSchemaProvider(_tempDir, _logger.Object);
        var result = await provider.GetSchemaAsync("urn:sorcha:schema:nonexistent");

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetCatalogAsync_InvalidJsonFile_SkipsAndContinues()
    {
        WriteSchemaFile("people-identity", "uk-address", "UK Address",
            "Standard UK postal address", "people-identity", []);

        // Write an invalid JSON file
        var invalidDir = Path.Combine(_tempDir, "invalid");
        Directory.CreateDirectory(invalidDir);
        File.WriteAllText(Path.Combine(invalidDir, "bad.json"), "not valid json");

        var provider = new LocalSchemaProvider(_tempDir, _logger.Object);
        var catalog = (await provider.GetCatalogAsync()).ToList();

        // Should still return the valid schema
        catalog.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetCatalogAsync_SubdirectoryStructure_FindsAllSchemas()
    {
        WriteSchemaFile("people-identity", "personal-id", "Personal Identity",
            "Personal ID schema", "people-identity", ["identity"]);
        WriteSchemaFile("financial", "bank-account", "Bank Account",
            "Bank account schema", "financial", ["finance", "banking"]);
        WriteSchemaFile("healthcare", "patient-ref", "Patient Reference",
            "Patient reference schema", "healthcare", ["healthcare"]);

        var provider = new LocalSchemaProvider(_tempDir, _logger.Object);
        var catalog = (await provider.GetCatalogAsync()).ToList();

        catalog.Should().HaveCount(3);
    }

    [Fact]
    public async Task InvalidateCache_CausesReReadOnNextAccess()
    {
        var provider = new LocalSchemaProvider(_tempDir, _logger.Object);

        // First call — empty directory
        var catalog1 = (await provider.GetCatalogAsync()).ToList();
        catalog1.Should().BeEmpty();

        // Add a file
        WriteSchemaFile("general", "test", "Test Schema",
            "A test schema", "general", []);

        // Still empty (cached)
        var catalog2 = (await provider.GetCatalogAsync()).ToList();
        catalog2.Should().BeEmpty();

        // Invalidate and re-read
        provider.InvalidateCache();
        var catalog3 = (await provider.GetCatalogAsync()).ToList();
        catalog3.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetCatalogAsync_LoadsRealBlueprintSchemas()
    {
        // Test against the actual blueprints/schemas directory if available
        var realPath = LocalSchemaProvider.ResolveSchemasPath(
            Path.Combine(Directory.GetCurrentDirectory()));

        if (!Directory.Exists(realPath))
        {
            Assert.Skip("blueprints/schemas directory not found — not running from solution root");
        }

        var provider = new LocalSchemaProvider(realPath, _logger.Object);
        var catalog = (await provider.GetCatalogAsync()).ToList();

        // Should have the known default schemas
        catalog.Should().NotBeEmpty();
        catalog.Should().Contain(r => r.Name == "UK Address");
        catalog.Should().Contain(r => r.Name == "Personal Identity");

        // Raw-JSON-Schema core primitives must also surface in the library so
        // the "Sorcha Core Primitives" sector chip is non-empty — see fix for
        // the two-pipelines-sharing-a-tree regression.
        catalog.Should().Contain(r => r.Name == "Personal Name");
        catalog.Should().Contain(r => r.Name == "Postal Address");
        catalog.Should().Contain(r => r.Name == "Date of Birth");

        catalog.Where(r => r.SectorTags is not null && r.SectorTags.Contains("core"))
               .Should().HaveCountGreaterThan(0,
                   "sorcha-core/*.json primitives must be tagged with the 'core' sector");

        // All entries should have content
        catalog.Should().OnlyContain(r => r.Content != null);

        // All entries should have the provider name
        catalog.Should().OnlyContain(r => r.Provider == "Sorcha Local");
    }

    [Fact]
    public async Task GetCatalogAsync_RawJsonSchemaFile_LoadsAsCorePrimitive()
    {
        WriteRawJsonSchemaFile(
            category: "sorcha-core",
            fileName: "PersonName.v1.json",
            id: "https://schemas.sorcha.dev/core/PersonName/v1",
            title: "Personal Name",
            description: "A person's name decomposed into given, middle, family, and full forms.");

        var provider = new LocalSchemaProvider(_tempDir, _logger.Object);
        var catalog = (await provider.GetCatalogAsync()).ToList();

        var entry = catalog.Single();
        entry.Name.Should().Be("Personal Name");
        entry.Url.Should().Be("https://schemas.sorcha.dev/core/PersonName/v1");
        entry.Description.Should().Contain("given, middle, family");
        entry.SectorTags.Should().Contain("core");
        entry.Provider.Should().Be("Sorcha Local");
    }

    [Fact]
    public async Task GetCatalogAsync_RawJsonSchemaMissingTitle_IsSkipped()
    {
        var dir = Path.Combine(_tempDir, "sorcha-core");
        Directory.CreateDirectory(dir);

        // Raw JSON Schema with $id but no title — should be skipped, not crash.
        File.WriteAllText(Path.Combine(dir, "bad.json"),
            """{ "$id": "https://example.com/x", "type": "object" }""");

        var provider = new LocalSchemaProvider(_tempDir, _logger.Object);
        var catalog = (await provider.GetCatalogAsync()).ToList();

        catalog.Should().BeEmpty();
    }

    [Fact]
    public async Task GetCatalogAsync_MixedFormats_BothSurfaceInCatalog()
    {
        // Envelope format (existing path)
        WriteSchemaFile("people-identity", "uk-address", "UK Address",
            "Standard UK postal address", "people-identity", ["address", "uk"]);

        // Raw JSON Schema format (new path)
        WriteRawJsonSchemaFile(
            category: "sorcha-core",
            fileName: "EmailAddress.v1.json",
            id: "https://schemas.sorcha.dev/core/EmailAddress/v1",
            title: "Email Address",
            description: "A single email address.");

        var provider = new LocalSchemaProvider(_tempDir, _logger.Object);
        var catalog = (await provider.GetCatalogAsync()).ToList();

        catalog.Should().HaveCount(2);
        catalog.Should().Contain(r => r.Name == "UK Address" && r.SectorTags!.Contains("people-identity"));
        catalog.Should().Contain(r => r.Name == "Email Address" && r.SectorTags!.Contains("core"));
    }

    private void WriteRawJsonSchemaFile(string category, string fileName, string id, string title, string description)
    {
        var dir = Path.Combine(_tempDir, category);
        Directory.CreateDirectory(dir);

        var schema = new Dictionary<string, object>
        {
            ["$id"] = id,
            ["$schema"] = "https://json-schema.org/draft/2020-12/schema",
            ["type"] = "object",
            ["title"] = title,
            ["description"] = description,
            ["properties"] = new Dictionary<string, object>
            {
                ["sample"] = new Dictionary<string, object> { ["type"] = "string" }
            }
        };

        var json = JsonSerializer.Serialize(schema, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(dir, fileName), json);
    }

    private void WriteSchemaFile(
        string category,
        string identifier,
        string title,
        string description,
        string categoryTag,
        string[] tags,
        bool includeFormLayout = false,
        bool includeDisclosure = false)
    {
        var dir = Path.Combine(_tempDir, category);
        Directory.CreateDirectory(dir);

        var schema = new Dictionary<string, object>
        {
            ["identifier"] = identifier,
            ["title"] = title,
            ["description"] = description,
            ["version"] = "1.0.0",
            ["category"] = categoryTag,
            ["tags"] = tags,
            ["schema"] = new Dictionary<string, object>
            {
                ["type"] = "object",
                ["properties"] = new Dictionary<string, object>
                {
                    ["addressLine1"] = new Dictionary<string, object>
                    {
                        ["type"] = "string",
                        ["title"] = "Address Line 1"
                    }
                }
            }
        };

        if (includeFormLayout)
        {
            schema["formLayout"] = new Dictionary<string, object>
            {
                ["type"] = "VerticalLayout",
                ["elements"] = new[] { new { type = "TextLine", scope = "addressLine1" } }
            };
        }

        if (includeDisclosure)
        {
            schema["disclosure"] = new Dictionary<string, object>
            {
                ["sensitive"] = Array.Empty<string>(),
                ["recommendation"] = "Full address is generally needed by all recipients."
            };
        }

        var json = JsonSerializer.Serialize(schema, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(dir, $"{identifier}.json"), json);
    }
}
