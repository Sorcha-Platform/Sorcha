// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using Moq;
using Sorcha.Register.Service.Repositories;
using Sorcha.Register.Service.Services;
using Xunit;

namespace Sorcha.Register.Service.Tests.Unit;

/// <summary>
/// Unit tests for blueprint version publishing in SystemRegisterService.
/// Covers versioned publishing, previous-version validation, and existence checks.
/// </summary>
public class SystemRegisterBlueprintTests
{
    private readonly Mock<ISystemRegisterRepository> _mockRepository;
    private readonly Mock<ILogger<SystemRegisterService>> _mockLogger;
    private readonly SystemRegisterService _service;
    private long _versionCounter;

    public SystemRegisterBlueprintTests()
    {
        _mockRepository = new Mock<ISystemRegisterRepository>();
        _mockLogger = new Mock<ILogger<SystemRegisterService>>();
        _versionCounter = 0;

        _mockRepository
            .Setup(r => r.PublishBlueprintAsync(
                It.IsAny<string>(), It.IsAny<BsonDocument>(),
                It.IsAny<string>(), It.IsAny<Dictionary<string, string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string id, BsonDocument _, string _, Dictionary<string, string>? _, CancellationToken _) =>
                new SystemRegisterEntry
                {
                    BlueprintId = id,
                    Version = ++_versionCounter,
                    PublishedAt = DateTime.UtcNow,
                    PublishedBy = "test-publisher",
                    IsActive = true
                });

        _service = new SystemRegisterService(_mockRepository.Object, _mockLogger.Object);
    }

    #region PublishBlueprintVersionAsync Tests

    [Fact]
    public async Task PublishBlueprintVersionAsync_NewBlueprint_PublishesSuccessfully()
    {
        // Arrange
        var blueprintDoc = new BsonDocument { { "title", "Test Blueprint" } };

        // Act
        var result = await _service.PublishBlueprintVersionAsync(
            "test-blueprint-v1", blueprintDoc, "admin-001");

        // Assert
        result.BlueprintId.Should().Be("test-blueprint-v1");
        result.Version.Should().BeGreaterThan(0);
        _mockRepository.Verify(r => r.PublishBlueprintAsync(
            "test-blueprint-v1", blueprintDoc, "admin-001",
            It.IsAny<Dictionary<string, string>?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PublishBlueprintVersionAsync_WithPreviousVersion_ValidatesAndPublishes()
    {
        // Arrange — previous version exists
        _mockRepository
            .Setup(r => r.GetBlueprintByIdAsync("test-blueprint-v1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SystemRegisterEntry
            {
                BlueprintId = "test-blueprint-v1",
                Version = 1,
                IsActive = true,
                PublishedBy = "admin-001"
            });

        var blueprintDoc = new BsonDocument { { "title", "Updated Blueprint" } };

        // Act
        var result = await _service.PublishBlueprintVersionAsync(
            "test-blueprint-v2", blueprintDoc, "admin-001",
            previousBlueprintId: "test-blueprint-v1");

        // Assert
        result.BlueprintId.Should().Be("test-blueprint-v2");
        _mockRepository.Verify(r => r.GetBlueprintByIdAsync(
            "test-blueprint-v1", It.IsAny<CancellationToken>()), Times.Once);

        // Verify previousVersionId is in metadata
        _mockRepository.Verify(r => r.PublishBlueprintAsync(
            "test-blueprint-v2", blueprintDoc, "admin-001",
            It.Is<Dictionary<string, string>>(m =>
                m.ContainsKey("previousVersionId") && m["previousVersionId"] == "test-blueprint-v1"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PublishBlueprintVersionAsync_PreviousVersionNotFound_ThrowsInvalidOperation()
    {
        // Arrange — previous version does NOT exist
        _mockRepository
            .Setup(r => r.GetBlueprintByIdAsync("nonexistent-v1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((SystemRegisterEntry?)null);

        var blueprintDoc = new BsonDocument { { "title", "Updated Blueprint" } };

        // Act
        var act = () => _service.PublishBlueprintVersionAsync(
            "test-blueprint-v2", blueprintDoc, "admin-001",
            previousBlueprintId: "nonexistent-v1");

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Previous blueprint version*not found*");
    }

    [Fact]
    public async Task PublishBlueprintVersionAsync_PreviousVersionDeprecated_PublishesWithWarning()
    {
        // Arrange — previous version exists but is deprecated
        _mockRepository
            .Setup(r => r.GetBlueprintByIdAsync("deprecated-v1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SystemRegisterEntry
            {
                BlueprintId = "deprecated-v1",
                Version = 1,
                IsActive = false, // deprecated
                PublishedBy = "admin-001"
            });

        var blueprintDoc = new BsonDocument { { "title", "Successor Blueprint" } };

        // Act — should succeed despite deprecated previous version
        var result = await _service.PublishBlueprintVersionAsync(
            "successor-v2", blueprintDoc, "admin-001",
            previousBlueprintId: "deprecated-v1");

        // Assert
        result.BlueprintId.Should().Be("successor-v2");
    }

    [Fact]
    public async Task PublishBlueprintVersionAsync_NullBlueprintId_ThrowsArgumentException()
    {
        var blueprintDoc = new BsonDocument { { "title", "Test" } };

        var act = () => _service.PublishBlueprintVersionAsync(
            null!, blueprintDoc, "admin-001");

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task PublishBlueprintVersionAsync_NullPublishedBy_ThrowsArgumentException()
    {
        var blueprintDoc = new BsonDocument { { "title", "Test" } };

        var act = () => _service.PublishBlueprintVersionAsync(
            "test-bp", blueprintDoc, null!);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    #endregion

    #region BlueprintExistsAsync Tests

    [Fact]
    public async Task BlueprintExistsAsync_ExistingActiveBlueprint_ReturnsTrue()
    {
        // Arrange
        _mockRepository
            .Setup(r => r.GetBlueprintByIdAsync("active-bp", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SystemRegisterEntry
            {
                BlueprintId = "active-bp",
                Version = 1,
                IsActive = true,
                PublishedBy = "system"
            });

        // Act
        var result = await _service.BlueprintExistsAsync("active-bp");

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task BlueprintExistsAsync_DeprecatedBlueprint_ReturnsFalse()
    {
        // Arrange
        _mockRepository
            .Setup(r => r.GetBlueprintByIdAsync("deprecated-bp", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SystemRegisterEntry
            {
                BlueprintId = "deprecated-bp",
                Version = 1,
                IsActive = false,
                PublishedBy = "system"
            });

        // Act
        var result = await _service.BlueprintExistsAsync("deprecated-bp");

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task BlueprintExistsAsync_NonExistentBlueprint_ReturnsFalse()
    {
        // Arrange
        _mockRepository
            .Setup(r => r.GetBlueprintByIdAsync("nonexistent", It.IsAny<CancellationToken>()))
            .ReturnsAsync((SystemRegisterEntry?)null);

        // Act
        var result = await _service.BlueprintExistsAsync("nonexistent");

        // Assert
        result.Should().BeFalse();
    }

    #endregion
}
