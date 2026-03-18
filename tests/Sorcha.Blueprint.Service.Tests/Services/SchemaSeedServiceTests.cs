// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sorcha.Blueprint.Schemas.Models;
using Sorcha.Blueprint.Schemas.Repositories;
using Sorcha.Blueprint.Service.Services;

namespace Sorcha.Blueprint.Service.Tests.Services;

/// <summary>
/// Tests for SchemaSeedService — startup schema seeding from JSON files.
/// </summary>
public class SchemaSeedServiceTests : IDisposable
{
    private readonly Mock<ISchemaRepository> _mockRepository;
    private readonly Mock<ILogger<SchemaSeedService>> _mockLogger;
    private readonly Mock<IWebHostEnvironment> _mockEnv;
    private readonly Mock<IServiceScopeFactory> _mockScopeFactory;
    private readonly string _tempDir;

    public SchemaSeedServiceTests()
    {
        _mockRepository = new Mock<ISchemaRepository>();
        _mockLogger = new Mock<ILogger<SchemaSeedService>>();
        _mockEnv = new Mock<IWebHostEnvironment>();
        _mockScopeFactory = new Mock<IServiceScopeFactory>();
        _tempDir = Path.Combine(Path.GetTempPath(), $"sorcha-schema-test-{Guid.NewGuid():N}");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    private SchemaSeedService CreateService()
    {
        _mockEnv.Setup(e => e.ContentRootPath).Returns(_tempDir);

        var mockScope = new Mock<IServiceScope>();
        var mockServiceProvider = new Mock<IServiceProvider>();

        mockServiceProvider
            .Setup(sp => sp.GetService(typeof(ISchemaRepository)))
            .Returns(_mockRepository.Object);

        mockScope.Setup(s => s.ServiceProvider).Returns(mockServiceProvider.Object);
        _mockScopeFactory.Setup(f => f.CreateScope()).Returns(mockScope.Object);

        return new SchemaSeedService(_mockScopeFactory.Object, _mockLogger.Object, _mockEnv.Object);
    }

    private string CreateSchemasDir()
    {
        var dir = Path.Combine(_tempDir, "blueprints", "schemas");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string CreateSchemaJson(
        string identifier = "test-schema",
        string title = "Test Schema",
        string version = "1.0.0",
        string? category = "finance")
    {
        return JsonSerializer.Serialize(new
        {
            identifier,
            title,
            description = "A test schema for seeding",
            version,
            category,
            tags = new[] { "test" },
            keywords = new[] { "testing" },
            schema = new
            {
                type = "object",
                properties = new
                {
                    name = new { type = "string", title = "Name" },
                    amount = new { type = "number", title = "Amount" }
                },
                required = new[] { "name" }
            }
        });
    }

    [Fact]
    public async Task StartAsync_SeedsSchemaFromFiles()
    {
        // Arrange
        var dir = CreateSchemasDir();
        File.WriteAllText(Path.Combine(dir, "schema1.json"), CreateSchemaJson("s1", "Schema One"));

        _mockRepository.Setup(r => r.GetByIdentifierAsync(
                "s1", SchemaSeedService.SeedOrganizationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SchemaEntry?)null);

        _mockRepository.Setup(r => r.CreateAsync(It.IsAny<SchemaEntry>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SchemaEntry e, CancellationToken _) => e);

        var service = CreateService();

        // Act
        await service.StartAsync(CancellationToken.None);

        // Assert
        _mockRepository.Verify(r => r.CreateAsync(
            It.Is<SchemaEntry>(e =>
                e.Identifier == "s1" &&
                e.Title == "Schema One" &&
                e.OrganizationId == SchemaSeedService.SeedOrganizationId &&
                e.IsGloballyPublished),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StartAsync_SkipsExistingSameVersion()
    {
        // Arrange
        var dir = CreateSchemasDir();
        File.WriteAllText(Path.Combine(dir, "schema1.json"), CreateSchemaJson("s1", "Schema One", "1.0.0"));

        var existing = new SchemaEntry
        {
            Identifier = "s1",
            Title = "Schema One",
            Version = "1.0.0",
            Category = SchemaCategory.Custom,
            Source = SchemaSource.Internal(),
            Content = JsonDocument.Parse("{}")
        };

        _mockRepository.Setup(r => r.GetByIdentifierAsync(
                "s1", SchemaSeedService.SeedOrganizationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        var service = CreateService();

        // Act
        await service.StartAsync(CancellationToken.None);

        // Assert
        _mockRepository.Verify(r => r.CreateAsync(It.IsAny<SchemaEntry>(), It.IsAny<CancellationToken>()), Times.Never);
        _mockRepository.Verify(r => r.UpdateAsync(It.IsAny<SchemaEntry>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task StartAsync_UpdatesNewerVersion()
    {
        // Arrange
        var dir = CreateSchemasDir();
        File.WriteAllText(Path.Combine(dir, "schema1.json"), CreateSchemaJson("s1", "Schema One", "2.0.0"));

        var existing = new SchemaEntry
        {
            Identifier = "s1",
            Title = "Schema One",
            Version = "1.0.0",
            Category = SchemaCategory.Custom,
            Source = SchemaSource.Internal(),
            Content = JsonDocument.Parse("{}")
        };

        _mockRepository.Setup(r => r.GetByIdentifierAsync(
                "s1", SchemaSeedService.SeedOrganizationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        _mockRepository.Setup(r => r.UpdateAsync(It.IsAny<SchemaEntry>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SchemaEntry e, CancellationToken _) => e);

        var service = CreateService();

        // Act
        await service.StartAsync(CancellationToken.None);

        // Assert
        _mockRepository.Verify(r => r.UpdateAsync(
            It.Is<SchemaEntry>(e => e.Version == "2.0.0"),
            It.IsAny<CancellationToken>()), Times.Once);
        _mockRepository.Verify(r => r.CreateAsync(It.IsAny<SchemaEntry>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task StartAsync_SkipsInvalidJsonFiles()
    {
        // Arrange
        var dir = CreateSchemasDir();
        File.WriteAllText(Path.Combine(dir, "bad.json"), "{ this is not valid json }}}");
        File.WriteAllText(Path.Combine(dir, "good.json"), CreateSchemaJson("s2", "Good Schema"));

        _mockRepository.Setup(r => r.GetByIdentifierAsync(
                "s2", SchemaSeedService.SeedOrganizationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SchemaEntry?)null);

        _mockRepository.Setup(r => r.CreateAsync(It.IsAny<SchemaEntry>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SchemaEntry e, CancellationToken _) => e);

        var service = CreateService();

        // Act
        await service.StartAsync(CancellationToken.None);

        // Assert — good schema still seeded despite bad file
        _mockRepository.Verify(r => r.CreateAsync(
            It.Is<SchemaEntry>(e => e.Identifier == "s2"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StartAsync_HandlesEmptyDirectory()
    {
        // Arrange
        CreateSchemasDir();
        var service = CreateService();

        // Act
        await service.StartAsync(CancellationToken.None);

        // Assert
        _mockRepository.Verify(r => r.CreateAsync(It.IsAny<SchemaEntry>(), It.IsAny<CancellationToken>()), Times.Never);
        _mockRepository.Verify(r => r.UpdateAsync(It.IsAny<SchemaEntry>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task StartAsync_HandlesMissingDirectory()
    {
        // Arrange — don't create the schemas directory
        var service = CreateService();

        // Act
        await service.StartAsync(CancellationToken.None);

        // Assert
        _mockRepository.Verify(r => r.CreateAsync(It.IsAny<SchemaEntry>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("1.0.0", "1.0.0", 0)]
    [InlineData("1.0.0", "2.0.0", -1)]
    [InlineData("2.0.0", "1.0.0", 1)]
    [InlineData("1.0.0", "1.0.1", -1)]
    [InlineData("1.1.0", "1.0.9", 1)]
    [InlineData("1.0", "1.0.0", 0)]
    [InlineData("2", "1.9.9", 1)]
    public void CompareVersions_ComparesCorrectly(string a, string b, int expectedSign)
    {
        // Act
        var result = SchemaSeedService.CompareVersions(a, b);

        // Assert
        result.Should().Be(expectedSign == 0 ? 0 : expectedSign > 0 ? result : result,
            "sign should match");
        Math.Sign(result).Should().Be(expectedSign);
    }

    [Fact]
    public async Task StartAsync_SetsCorrectFieldCountAndFieldNames()
    {
        // Arrange
        var dir = CreateSchemasDir();
        File.WriteAllText(Path.Combine(dir, "schema1.json"), CreateSchemaJson("s1", "Schema One"));

        _mockRepository.Setup(r => r.GetByIdentifierAsync(
                "s1", SchemaSeedService.SeedOrganizationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SchemaEntry?)null);

        SchemaEntry? captured = null;
        _mockRepository.Setup(r => r.CreateAsync(It.IsAny<SchemaEntry>(), It.IsAny<CancellationToken>()))
            .Callback<SchemaEntry, CancellationToken>((e, _) => captured = e)
            .ReturnsAsync((SchemaEntry e, CancellationToken _) => e);

        var service = CreateService();

        // Act
        await service.StartAsync(CancellationToken.None);

        // Assert
        captured.Should().NotBeNull();
        captured!.FieldCount.Should().Be(2);
        captured.FieldNames.Should().BeEquivalentTo(new[] { "name", "amount" });
    }
}
