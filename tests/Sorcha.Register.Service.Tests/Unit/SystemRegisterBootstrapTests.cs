// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Sorcha.Register.Service.Configuration;
using Sorcha.Register.Service.Repositories;
using Sorcha.Register.Service.Services;
using Xunit;

namespace Sorcha.Register.Service.Tests.Unit;

public class SystemRegisterBootstrapTests
{
    private readonly Mock<ISystemRegisterRepository> _mockRepository;
    private readonly Mock<ILogger<SystemRegisterService>> _mockServiceLogger;
    private readonly Mock<ILogger<SystemRegisterBootstrapper>> _mockBootstrapLogger;

    public SystemRegisterBootstrapTests()
    {
        _mockRepository = new Mock<ISystemRegisterRepository>();
        _mockServiceLogger = new Mock<ILogger<SystemRegisterService>>();
        _mockBootstrapLogger = new Mock<ILogger<SystemRegisterBootstrapper>>();

        // Default: not initialized, version 0
        _mockRepository.Setup(r => r.IsSystemRegisterInitializedAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _mockRepository.Setup(r => r.GetLatestVersionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(0L);
        _mockRepository.Setup(r => r.GetAllBlueprintsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SystemRegisterEntry>());
        _mockRepository.Setup(r => r.GetBlueprintCountAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _mockRepository.Setup(r => r.PublishBlueprintAsync(
                It.IsAny<string>(), It.IsAny<MongoDB.Bson.BsonDocument>(),
                It.IsAny<string>(), It.IsAny<Dictionary<string, string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SystemRegisterEntry { BlueprintId = "test", Version = 1 });
    }

    private SystemRegisterService CreateService() =>
        new(_mockRepository.Object, _mockServiceLogger.Object);

    private SystemRegisterBootstrapper CreateBootstrapper(bool seedEnabled)
    {
        var config = Options.Create(new SystemRegisterConfiguration
        {
            SeedSystemRegister = seedEnabled
        });

        return new SystemRegisterBootstrapper(
            config,
            CreateService(),
            _mockBootstrapLogger.Object);
    }

    [Fact]
    public async Task ExecuteAsync_WhenSeedFalse_DoesNotInitialize()
    {
        // Arrange
        var bootstrapper = CreateBootstrapper(seedEnabled: false);
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromSeconds(5));

        // Act
        await bootstrapper.StartAsync(cts.Token);
        await bootstrapper.StopAsync(cts.Token);

        // Assert — InitializeSystemRegisterAsync should NOT have been called
        _mockRepository.Verify(r => r.PublishBlueprintAsync(
            It.IsAny<string>(), It.IsAny<MongoDB.Bson.BsonDocument>(),
            It.IsAny<string>(), It.IsAny<Dictionary<string, string>?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_WhenAlreadyInitialized_SkipsBootstrap()
    {
        // Arrange — version > 0 means already initialized
        _mockRepository.Setup(r => r.GetLatestVersionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(2L);

        var bootstrapper = CreateBootstrapper(seedEnabled: true);
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromSeconds(5));

        // Act
        await bootstrapper.StartAsync(cts.Token);
        await bootstrapper.StopAsync(cts.Token);

        // Assert — no blueprint publish because already initialized
        _mockRepository.Verify(r => r.PublishBlueprintAsync(
            It.IsAny<string>(), It.IsAny<MongoDB.Bson.BsonDocument>(),
            It.IsAny<string>(), It.IsAny<Dictionary<string, string>?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_WhenSeedTrue_InitializesSystemRegister()
    {
        // Arrange — version 0 = not initialized
        _mockRepository.Setup(r => r.GetLatestVersionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(0L);
        _mockRepository.Setup(r => r.IsSystemRegisterInitializedAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var bootstrapper = CreateBootstrapper(seedEnabled: true);
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromSeconds(5));

        // Act — StartAsync fires ExecuteAsync but doesn't await it; give it time to complete
        await bootstrapper.StartAsync(cts.Token);
        await Task.Delay(500, cts.Token);
        await bootstrapper.StopAsync(cts.Token);

        // Assert — blueprints should have been published (at least register-creation-v1)
        _mockRepository.Verify(r => r.PublishBlueprintAsync(
            It.IsAny<string>(), It.IsAny<MongoDB.Bson.BsonDocument>(),
            It.IsAny<string>(), It.IsAny<Dictionary<string, string>?>(),
            It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task ExecuteAsync_OnException_DoesNotCrashHost()
    {
        // Arrange — GetLatestVersionAsync throws on every call
        _mockRepository.Setup(r => r.GetLatestVersionAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("MongoDB connection failed"));

        var bootstrapper = CreateBootstrapper(seedEnabled: true);
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromSeconds(30));

        // Act — should NOT throw
        Func<Task> act = async () =>
        {
            await bootstrapper.StartAsync(cts.Token);
            await bootstrapper.StopAsync(cts.Token);
        };

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task GetSystemRegisterInfoAsync_ReturnsCorrectInfo()
    {
        // Arrange
        _mockRepository.Setup(r => r.IsSystemRegisterInitializedAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _mockRepository.Setup(r => r.GetBlueprintCountAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(2);
        _mockRepository.Setup(r => r.GetLatestVersionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(5L);
        _mockRepository.Setup(r => r.GetAllBlueprintsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SystemRegisterEntry>
            {
                new() { BlueprintId = "bp-1", Version = 1, PublishedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) },
                new() { BlueprintId = "bp-2", Version = 5, PublishedAt = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc) }
            });

        var service = CreateService();

        // Act
        var info = await service.GetSystemRegisterInfoAsync();

        // Assert
        info.RegisterId.Should().Be("aebf26362e079087571ac0932d4db973");
        info.Name.Should().Be("Sorcha System Register");
        info.Status.Should().Be("initialized");
        info.BlueprintCount.Should().Be(2);
        info.CurrentVersion.Should().Be(5);
        info.CreatedAt.Should().Be(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public async Task GetSystemRegisterInfoAsync_WhenNotInitialized_ReturnsNotInitializedStatus()
    {
        // Arrange
        _mockRepository.Setup(r => r.IsSystemRegisterInitializedAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var service = CreateService();

        // Act
        var info = await service.GetSystemRegisterInfoAsync();

        // Assert
        info.Status.Should().Be("not_initialized");
        info.BlueprintCount.Should().Be(0);
        info.CurrentVersion.Should().Be(0);
        info.CreatedAt.Should().BeNull();
    }
}
