// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Sorcha.Blueprint.Models;
using Sorcha.Blueprint.Service.Services;
using Sorcha.Storage.Abstractions;

namespace Sorcha.Blueprint.Service.Tests.Services;

/// <summary>
/// Tests for TemplateSeedService — startup template seeding from JSON files.
/// </summary>
public class TemplateSeedServiceTests : IDisposable
{
    private readonly Mock<IDocumentStore<BlueprintTemplate, string>> _mockStore;
    private readonly Mock<ILogger<TemplateSeedService>> _mockLogger;
    private readonly Mock<IWebHostEnvironment> _mockEnv;
    private readonly string _tempDir;

    public TemplateSeedServiceTests()
    {
        _mockStore = new Mock<IDocumentStore<BlueprintTemplate, string>>();
        _mockLogger = new Mock<ILogger<TemplateSeedService>>();
        _mockEnv = new Mock<IWebHostEnvironment>();
        _tempDir = Path.Combine(Path.GetTempPath(), $"sorcha-test-{Guid.NewGuid():N}");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    private TemplateSeedService CreateService()
    {
        _mockEnv.Setup(e => e.ContentRootPath).Returns(_tempDir);
        return new TemplateSeedService(_mockStore.Object, _mockLogger.Object, _mockEnv.Object);
    }

    private string CreateTemplatesDir()
    {
        var dir = Path.Combine(_tempDir, "blueprints", "templates");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string CreateTemplateJson(string id = "test-1", string title = "Test Template", int version = 1)
    {
        var template = new BlueprintTemplate
        {
            Id = id,
            Title = title,
            Description = "A test template for seeding",
            Version = version,
            Published = true,
            Template = System.Text.Json.Nodes.JsonNode.Parse("{\"title\": \"test\"}")!
        };
        return JsonSerializer.Serialize(template, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
    }

    [Fact]
    public async Task StartAsync_SeedsTemplatesFromFiles()
    {
        var dir = CreateTemplatesDir();
        File.WriteAllText(Path.Combine(dir, "template1.json"), CreateTemplateJson("t1", "Template One"));

        _mockStore.Setup(s => s.GetAsync("t1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((BlueprintTemplate?)null);
        _mockStore.Setup(s => s.UpsertAsync("t1", It.IsAny<BlueprintTemplate>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, BlueprintTemplate t, CancellationToken _) => t);

        var service = CreateService();
        await service.StartAsync(CancellationToken.None);

        _mockStore.Verify(s => s.UpsertAsync("t1", It.Is<BlueprintTemplate>(t => t.Title == "Template One"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StartAsync_Idempotent_SkipsExistingVersion()
    {
        var dir = CreateTemplatesDir();
        File.WriteAllText(Path.Combine(dir, "template1.json"), CreateTemplateJson("t1", "Template One", version: 1));

        var existing = new BlueprintTemplate { Id = "t1", Title = "Template One", Version = 1 };
        _mockStore.Setup(s => s.GetAsync("t1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        var service = CreateService();
        await service.StartAsync(CancellationToken.None);

        _mockStore.Verify(s => s.UpsertAsync(It.IsAny<string>(), It.IsAny<BlueprintTemplate>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task StartAsync_SkipsInvalidJsonFiles()
    {
        var dir = CreateTemplatesDir();
        File.WriteAllText(Path.Combine(dir, "bad.json"), "{ this is not valid json }}}");
        File.WriteAllText(Path.Combine(dir, "good.json"), CreateTemplateJson("t2", "Good Template"));

        _mockStore.Setup(s => s.GetAsync("t2", It.IsAny<CancellationToken>()))
            .ReturnsAsync((BlueprintTemplate?)null);
        _mockStore.Setup(s => s.UpsertAsync("t2", It.IsAny<BlueprintTemplate>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, BlueprintTemplate t, CancellationToken _) => t);

        var service = CreateService();
        await service.StartAsync(CancellationToken.None);

        // Good template still seeded despite bad file
        _mockStore.Verify(s => s.UpsertAsync("t2", It.IsAny<BlueprintTemplate>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StartAsync_NoDirectory_DoesNotThrow()
    {
        // Don't create the templates directory
        var service = CreateService();
        await service.StartAsync(CancellationToken.None);

        _mockStore.Verify(s => s.UpsertAsync(It.IsAny<string>(), It.IsAny<BlueprintTemplate>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task StartAsync_UpsertsNewerVersion()
    {
        var dir = CreateTemplatesDir();
        File.WriteAllText(Path.Combine(dir, "template1.json"), CreateTemplateJson("t1", "Template One", version: 2));

        var existing = new BlueprintTemplate { Id = "t1", Title = "Template One", Version = 1 };
        _mockStore.Setup(s => s.GetAsync("t1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        _mockStore.Setup(s => s.UpsertAsync("t1", It.IsAny<BlueprintTemplate>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, BlueprintTemplate t, CancellationToken _) => t);

        var service = CreateService();
        await service.StartAsync(CancellationToken.None);

        _mockStore.Verify(s => s.UpsertAsync("t1", It.Is<BlueprintTemplate>(t => t.Version == 2), It.IsAny<CancellationToken>()), Times.Once);
    }
}
