// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

extern alias ServiceClients;

using Grpc.Core;
using Microsoft.Extensions.Logging;
using Moq;
using ServiceClients::Sorcha.Register.Service.Grpc;
using ServiceClients::Sorcha.ServiceClients.Grpc;
using Sorcha.Wallet.Service.Services.Implementation;
using Xunit;
using FluentAssertions;

namespace Sorcha.Wallet.Service.Tests.Services;

public class AddressRegistrationServiceTests
{
    private readonly Mock<IRegisterAddressClient> _mockClient;
    private readonly Mock<ILogger<AddressRegistrationService>> _mockLogger;
    private readonly AddressRegistrationService _service;

    private const string ValidAddress = "bc1qar0srrr7xfkvy5l643lydnw9re59gtzzwf5mdq";
    private const string ValidRegisterId = "reg-001";

    public AddressRegistrationServiceTests()
    {
        _mockClient = new Mock<IRegisterAddressClient>();
        _mockLogger = new Mock<ILogger<AddressRegistrationService>>();

        _service = new AddressRegistrationService(
            _mockClient.Object,
            _mockLogger.Object);
    }

    // ---------------------------------------------------------------------------
    // Constructor
    // ---------------------------------------------------------------------------

    [Fact]
    public void Constructor_NullClient_ThrowsArgumentNullException()
    {
        var act = () => new AddressRegistrationService(null!, _mockLogger.Object);
        act.Should().Throw<ArgumentNullException>().WithParameterName("registerAddressClient");
    }

    [Fact]
    public void Constructor_NullLogger_ThrowsArgumentNullException()
    {
        var act = () => new AddressRegistrationService(_mockClient.Object, null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    // ---------------------------------------------------------------------------
    // RegisterAddressAsync
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task RegisterAddressAsync_ValidAddress_CallsGrpcClientAndReturnsTrue()
    {
        // Arrange
        _mockClient
            .Setup(c => c.RegisterLocalAddressAsync(
                ValidAddress, ValidRegisterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RegisterLocalAddressResponse { Success = true, Message = "Added" });

        // Act
        var result = await _service.RegisterAddressAsync(ValidAddress, ValidRegisterId);

        // Assert
        result.Should().BeTrue();
        _mockClient.Verify(
            c => c.RegisterLocalAddressAsync(ValidAddress, ValidRegisterId, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RegisterAddressAsync_GrpcFailure_ReturnsFalse()
    {
        // Arrange
        _mockClient
            .Setup(c => c.RegisterLocalAddressAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RpcException(new Status(StatusCode.Unavailable, "Connection refused")));

        // Act
        var result = await _service.RegisterAddressAsync(ValidAddress, ValidRegisterId);

        // Assert — must not throw, must return false
        result.Should().BeFalse();
    }

    [Fact]
    public async Task RegisterAddressAsync_ClientReturnsFailure_ReturnsFalse()
    {
        // Arrange
        _mockClient
            .Setup(c => c.RegisterLocalAddressAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RegisterLocalAddressResponse { Success = false, Message = "Duplicate address" });

        // Act
        var result = await _service.RegisterAddressAsync(ValidAddress, ValidRegisterId);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task RegisterAddressAsync_NullAddress_ThrowsArgumentException()
    {
        // Act
        var act = async () => await _service.RegisterAddressAsync(null!, ValidRegisterId);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task RegisterAddressAsync_EmptyAddress_ThrowsArgumentException()
    {
        // Act
        var act = async () => await _service.RegisterAddressAsync(string.Empty, ValidRegisterId);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task RegisterAddressAsync_EmptyRegisterId_ThrowsArgumentException()
    {
        // Act
        var act = async () => await _service.RegisterAddressAsync(ValidAddress, string.Empty);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task RegisterAddressAsync_NullRegisterId_ThrowsArgumentException()
    {
        // Act
        var act = async () => await _service.RegisterAddressAsync(ValidAddress, null!);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task RegisterAddressAsync_CancellationTokenForwarded_PassedToClient()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        var token = cts.Token;

        _mockClient
            .Setup(c => c.RegisterLocalAddressAsync(ValidAddress, ValidRegisterId, token))
            .ReturnsAsync(new RegisterLocalAddressResponse { Success = true, Message = "Added" });

        // Act
        await _service.RegisterAddressAsync(ValidAddress, ValidRegisterId, token);

        // Assert
        _mockClient.Verify(
            c => c.RegisterLocalAddressAsync(ValidAddress, ValidRegisterId, token),
            Times.Once);
    }

    [Fact]
    public async Task RegisterAddressAsync_GrpcNotFound_ReturnsFalse()
    {
        // Arrange
        _mockClient
            .Setup(c => c.RegisterLocalAddressAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RpcException(new Status(StatusCode.NotFound, "Register not found")));

        // Act
        var result = await _service.RegisterAddressAsync(ValidAddress, ValidRegisterId);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task RegisterAddressAsync_GrpcDeadlineExceeded_ReturnsFalse()
    {
        // Arrange
        _mockClient
            .Setup(c => c.RegisterLocalAddressAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RpcException(new Status(StatusCode.DeadlineExceeded, "Timeout")));

        // Act
        var result = await _service.RegisterAddressAsync(ValidAddress, ValidRegisterId);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task RegisterAddressAsync_MultipleAddressesSameRegister_EachCallsClient()
    {
        // Arrange
        var address1 = "bc1qaddress1";
        var address2 = "bc1qaddress2";

        _mockClient
            .Setup(c => c.RegisterLocalAddressAsync(
                It.IsAny<string>(), ValidRegisterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RegisterLocalAddressResponse { Success = true, Message = "Added" });

        // Act
        var result1 = await _service.RegisterAddressAsync(address1, ValidRegisterId);
        var result2 = await _service.RegisterAddressAsync(address2, ValidRegisterId);

        // Assert
        result1.Should().BeTrue();
        result2.Should().BeTrue();
        _mockClient.Verify(
            c => c.RegisterLocalAddressAsync(It.IsAny<string>(), ValidRegisterId, It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    // ---------------------------------------------------------------------------
    // RemoveAddressAsync
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task RemoveAddressAsync_ValidAddress_CallsGrpcClientAndReturnsTrue()
    {
        // Arrange
        _mockClient
            .Setup(c => c.RemoveLocalAddressAsync(
                ValidAddress, ValidRegisterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RemoveLocalAddressResponse
            {
                Success = true,
                RebuildTriggered = true,
                Message = "Removed and rebuild triggered"
            });

        // Act
        var result = await _service.RemoveAddressAsync(ValidAddress, ValidRegisterId);

        // Assert
        result.Should().BeTrue();
        _mockClient.Verify(
            c => c.RemoveLocalAddressAsync(ValidAddress, ValidRegisterId, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RemoveAddressAsync_GrpcFailure_ReturnsFalse()
    {
        // Arrange
        _mockClient
            .Setup(c => c.RemoveLocalAddressAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RpcException(new Status(StatusCode.Internal, "Server error")));

        // Act
        var result = await _service.RemoveAddressAsync(ValidAddress, ValidRegisterId);

        // Assert — must not throw, must return false
        result.Should().BeFalse();
    }

    [Fact]
    public async Task RemoveAddressAsync_ClientReturnsFailure_ReturnsFalse()
    {
        // Arrange
        _mockClient
            .Setup(c => c.RemoveLocalAddressAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RemoveLocalAddressResponse
            {
                Success = false,
                RebuildTriggered = false,
                Message = "Address not found"
            });

        // Act
        var result = await _service.RemoveAddressAsync(ValidAddress, ValidRegisterId);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task RemoveAddressAsync_NullAddress_ThrowsArgumentException()
    {
        // Act
        var act = async () => await _service.RemoveAddressAsync(null!, ValidRegisterId);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task RemoveAddressAsync_EmptyAddress_ThrowsArgumentException()
    {
        // Act
        var act = async () => await _service.RemoveAddressAsync(string.Empty, ValidRegisterId);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task RemoveAddressAsync_EmptyRegisterId_ThrowsArgumentException()
    {
        // Act
        var act = async () => await _service.RemoveAddressAsync(ValidAddress, string.Empty);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task RemoveAddressAsync_NullRegisterId_ThrowsArgumentException()
    {
        // Act
        var act = async () => await _service.RemoveAddressAsync(ValidAddress, null!);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task RemoveAddressAsync_CancellationTokenForwarded_PassedToClient()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        var token = cts.Token;

        _mockClient
            .Setup(c => c.RemoveLocalAddressAsync(ValidAddress, ValidRegisterId, token))
            .ReturnsAsync(new RemoveLocalAddressResponse
            {
                Success = true,
                RebuildTriggered = true,
                Message = "Removed"
            });

        // Act
        await _service.RemoveAddressAsync(ValidAddress, ValidRegisterId, token);

        // Assert
        _mockClient.Verify(
            c => c.RemoveLocalAddressAsync(ValidAddress, ValidRegisterId, token),
            Times.Once);
    }

    [Fact]
    public async Task RemoveAddressAsync_GrpcPermissionDenied_ReturnsFalse()
    {
        // Arrange
        _mockClient
            .Setup(c => c.RemoveLocalAddressAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RpcException(new Status(StatusCode.PermissionDenied, "Not authorized")));

        // Act
        var result = await _service.RemoveAddressAsync(ValidAddress, ValidRegisterId);

        // Assert
        result.Should().BeFalse();
    }
}
