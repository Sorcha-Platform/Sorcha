// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Sorcha.ServiceClients.Blueprint;
using Sorcha.Validator.Service.Configuration;
using Sorcha.Validator.Service.Services;

namespace Sorcha.Validator.Service.Tests.Services;

public class BlueprintFetcherTests
{
    private readonly Mock<IBlueprintServiceClient> _blueprintClientMock;
    private readonly Mock<IOptions<BlueprintFetcherConfiguration>> _configMock;
    private readonly Mock<ILogger<BlueprintFetcher>> _loggerMock;
    private readonly BlueprintFetcherConfiguration _config;
    private readonly BlueprintFetcher _fetcher;

    public BlueprintFetcherTests()
    {
        _config = new BlueprintFetcherConfiguration
        {
            FetchTimeout = TimeSpan.FromSeconds(30),
            MaxRetries = 3
        };

        _blueprintClientMock = new Mock<IBlueprintServiceClient>();
        _configMock = new Mock<IOptions<BlueprintFetcherConfiguration>>();
        _configMock.Setup(x => x.Value).Returns(_config);
        _loggerMock = new Mock<ILogger<BlueprintFetcher>>();

        _fetcher = new BlueprintFetcher(
            _blueprintClientMock.Object,
            _configMock.Object,
            _loggerMock.Object);
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_NullBlueprintClient_ThrowsArgumentNullException()
    {
        var act = () => new BlueprintFetcher(
            null!,
            _configMock.Object,
            _loggerMock.Object);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("blueprintClient");
    }

    [Fact]
    public void Constructor_NullLogger_ThrowsArgumentNullException()
    {
        var act = () => new BlueprintFetcher(
            _blueprintClientMock.Object,
            _configMock.Object,
            null!);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("logger");
    }

    #endregion

    #region FetchBlueprintAsync Tests

    [Fact]
    public async Task FetchBlueprintAsync_ValidBlueprint_ReturnsBlueprint()
    {
        // Arrange
        const string blueprintId = "blueprint-1";
        const string blueprintJson = """
            {
                "id": "blueprint-1",
                "title": "Test Blueprint",
                "description": "A test blueprint",
                "version": 1,
                "participants": [],
                "actions": []
            }
            """;

        _blueprintClientMock.Setup(x => x.GetBlueprintAsync(blueprintId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(blueprintJson);

        // Act
        var result = await _fetcher.FetchBlueprintAsync(blueprintId);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(blueprintId);
        result.Title.Should().Be("Test Blueprint");
    }

    [Fact]
    public async Task FetchBlueprintByPublicationAsync_ReadsTheKebabCaseEnumsTheBlueprintServiceServes()
    {
        // #1700 — measured on n1: GET /api/blueprints/{id}/definitions/{hash} serves
        // "transform":"first-word" (SorchaJson), and the fetcher's plain options threw
        // "could not be converted to ReferenceTransform" on every cold-cache pinned fetch.
        const string servedJson = """
            {
                "id": "construction-permit",
                "title": "Construction permit",
                "participants": [],
                "actions": [],
                "instanceReference": {
                    "prefix": "CP",
                    "components": [ { "field": "/projectName", "transform": "first-word", "chars": 3 } ]
                }
            }
            """;
        _blueprintClientMock
            .Setup(x => x.GetBlueprintDefinitionAsync("construction-permit", "pin-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(servedJson);

        var result = await _fetcher.FetchBlueprintByPublicationAsync("construction-permit", "pin-1");

        result.Should().NotBeNull("the pinned definition must be readable on a cold cache");
        result!.InstanceReference!.Components[0].Transform.Should().Be(Sorcha.Blueprint.Models.ReferenceTransform.FirstWord);
    }

    [Fact]
    public async Task FetchBlueprintAsync_BlueprintNotFound_ReturnsNull()
    {
        // Arrange
        const string blueprintId = "blueprint-missing";
        _blueprintClientMock.Setup(x => x.GetBlueprintAsync(blueprintId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        // Act
        var result = await _fetcher.FetchBlueprintAsync(blueprintId);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task FetchBlueprintAsync_InvalidJson_ReturnsNull()
    {
        // Arrange
        const string blueprintId = "blueprint-1";
        _blueprintClientMock.Setup(x => x.GetBlueprintAsync(blueprintId, It.IsAny<CancellationToken>()))
            .ReturnsAsync("not valid json");

        // Act
        var result = await _fetcher.FetchBlueprintAsync(blueprintId);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task FetchBlueprintAsync_NullBlueprintId_ThrowsArgumentException()
    {
        // Act
        var act = () => _fetcher.FetchBlueprintAsync(null!);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task FetchBlueprintAsync_EmptyBlueprintId_ThrowsArgumentException()
    {
        // Act
        var act = () => _fetcher.FetchBlueprintAsync(string.Empty);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task FetchBlueprintAsync_ServiceException_ReturnsNull()
    {
        // Arrange
        const string blueprintId = "blueprint-1";
        _blueprintClientMock.Setup(x => x.GetBlueprintAsync(blueprintId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Service unavailable"));

        // Act
        var result = await _fetcher.FetchBlueprintAsync(blueprintId);

        // Assert
        result.Should().BeNull();
    }

    #endregion

    #region ValidatePayloadAsync Tests

    [Fact]
    public async Task ValidatePayloadAsync_ValidPayload_ReturnsValid()
    {
        // Arrange
        const string blueprintId = "blueprint-1";
        const string actionId = "action-1";
        const string payload = "{}";

        _blueprintClientMock.Setup(x => x.ValidatePayloadAsync(
                blueprintId, actionId, payload, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _fetcher.ValidatePayloadAsync(blueprintId, actionId, payload);

        // Assert
        result.IsValid.Should().BeTrue();
        result.BlueprintFound.Should().BeTrue();
        result.ActionFound.Should().BeTrue();
    }

    [Fact]
    public async Task ValidatePayloadAsync_InvalidPayload_ReturnsInvalid()
    {
        // Arrange
        const string blueprintId = "blueprint-1";
        const string actionId = "action-1";
        const string payload = "{}";

        _blueprintClientMock.Setup(x => x.ValidatePayloadAsync(
                blueprintId, actionId, payload, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        var result = await _fetcher.ValidatePayloadAsync(blueprintId, actionId, payload);

        // Assert
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task ValidatePayloadAsync_ServiceException_ReturnsInvalidWithError()
    {
        // Arrange
        const string blueprintId = "blueprint-1";
        const string actionId = "action-1";
        const string payload = "{}";

        _blueprintClientMock.Setup(x => x.ValidatePayloadAsync(
                blueprintId, actionId, payload, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Validation failed"));

        // Act
        var result = await _fetcher.ValidatePayloadAsync(blueprintId, actionId, payload);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().NotBeEmpty();
    }

    #endregion

    #region GetStats Tests

    [Fact]
    public void GetStats_InitialState_ReturnsZeroCounts()
    {
        // Act
        var stats = _fetcher.GetStats();

        // Assert
        stats.TotalFetched.Should().Be(0);
        stats.TotalFailures.Should().Be(0);
        stats.TotalValidations.Should().Be(0);
    }

    [Fact]
    public async Task GetStats_AfterOperations_TracksCorrectly()
    {
        // Arrange
        const string blueprintJson = """
            {
                "id": "blueprint-1",
                "title": "Test",
                "description": "A test blueprint",
                "version": 1,
                "participants": [],
                "actions": []
            }
            """;

        _blueprintClientMock.Setup(x => x.GetBlueprintAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(blueprintJson);

        _blueprintClientMock.Setup(x => x.ValidatePayloadAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        await _fetcher.FetchBlueprintAsync("bp-1");
        await _fetcher.FetchBlueprintAsync("bp-2");
        await _fetcher.ValidatePayloadAsync("bp-1", "action-1", "{}");

        var stats = _fetcher.GetStats();

        // Assert
        stats.TotalFetched.Should().Be(2);
        stats.TotalValidations.Should().Be(1);
    }

    #endregion
}
