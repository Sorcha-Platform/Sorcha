// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Sorcha.Register.Core.Services;
using Sorcha.Register.Core.Storage;
using Xunit;

namespace Sorcha.Register.Core.Tests.Services;

/// <summary>
/// Unit tests for blueprint version validation in RegisterPolicyService.
/// Verifies that governance proposals referencing non-existent blueprint versions are rejected.
/// </summary>
public class BlueprintVersionValidationTests
{
    private readonly Mock<IReadOnlyRegisterRepository> _mockRepository;
    private readonly Mock<ISystemBlueprintValidator> _mockBlueprintValidator;
    private readonly Mock<ILogger<RegisterPolicyService>> _mockLogger;
    private readonly RegisterPolicyService _service;

    public BlueprintVersionValidationTests()
    {
        _mockRepository = new Mock<IReadOnlyRegisterRepository>();
        _mockBlueprintValidator = new Mock<ISystemBlueprintValidator>();
        _mockLogger = new Mock<ILogger<RegisterPolicyService>>();
        _service = new RegisterPolicyService(
            _mockRepository.Object, _mockBlueprintValidator.Object, _mockLogger.Object);
    }

    [Fact]
    public async Task ValidateBlueprintVersionExistsAsync_ExistingBlueprint_ReturnsTrue()
    {
        // Arrange
        _mockBlueprintValidator
            .Setup(c => c.ExistsAsync("register-creation-v1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _service.ValidateBlueprintVersionExistsAsync("register-creation-v1");

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateBlueprintVersionExistsAsync_NonExistentBlueprint_ReturnsFalse()
    {
        // Arrange
        _mockBlueprintValidator
            .Setup(c => c.ExistsAsync("nonexistent-blueprint-v99", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        var result = await _service.ValidateBlueprintVersionExistsAsync("nonexistent-blueprint-v99");

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateBlueprintVersionExistsAsync_EmptyBlueprintId_ThrowsArgumentException()
    {
        // Act
        var act = () => _service.ValidateBlueprintVersionExistsAsync("");

        // Assert
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task ValidateBlueprintVersionExistsAsync_NullBlueprintId_ThrowsArgumentException()
    {
        // Act
        var act = () => _service.ValidateBlueprintVersionExistsAsync(null!);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>();
    }
}
