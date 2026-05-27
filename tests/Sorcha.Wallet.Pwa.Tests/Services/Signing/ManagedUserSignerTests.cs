// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sorcha.UI.Components.User.Services.Signing;
using Sorcha.Wallet.Pwa.Services;
using Sorcha.Wallet.Pwa.Services.Signing;
using Xunit;

namespace Sorcha.Wallet.Pwa.Tests.Services.Signing;

/// <summary>
/// Contract tests for <see cref="ManagedUserSigner"/> (Feature 125, T026).
/// Verifies the v1 managed-mode signer honours the <see cref="IUserSigner"/>
/// contract: announces <see cref="UserCustodyMode.Managed"/>, signs payloads
/// via the underlying <see cref="IDeviceKeyService"/>, and propagates errors
/// through <see cref="SigningResult"/> without leaking custody-mode awareness.
/// </summary>
public sealed class ManagedUserSignerTests
{
    private static readonly byte[] Payload = [1, 2, 3, 4, 5];
    private static readonly byte[] Signature = [9, 8, 7, 6];

    [Fact]
    public void CustodyMode_IsManaged()
    {
        var signer = new ManagedUserSigner(Mock.Of<IDeviceKeyService>(), NullLogger<ManagedUserSigner>.Instance);
        signer.CustodyMode.Should().Be(UserCustodyMode.Managed);
    }

    [Fact]
    public void DisplayLabel_IsRoleNeutral_NeverMentionsCustodyMode()
    {
        var signer = new ManagedUserSigner(Mock.Of<IDeviceKeyService>(), NullLogger<ManagedUserSigner>.Instance);
        signer.DisplayLabel.Should().NotBeNullOrEmpty();
        signer.DisplayLabel.Should().NotContain("custody", "Display label must not leak custody-mode detail to UI.");
        signer.DisplayLabel.Should().NotContain("device key");
    }

    [Fact]
    public async Task SignAsync_ValidPayload_DelegatesToDeviceKey_ReturnsEs256Success()
    {
        var device = new Mock<IDeviceKeyService>();
        device.Setup(d => d.SignAsync(Payload, It.IsAny<CancellationToken>())).ReturnsAsync(Signature);

        var signer = new ManagedUserSigner(device.Object, NullLogger<ManagedUserSigner>.Instance);
        var result = await signer.SignAsync(new SigningRequest(SigningOperation.Presentation, Payload));

        result.Success.Should().BeTrue();
        result.Signature.Should().Equal(Signature);
        result.Algorithm.Should().Be("ES256");
        result.ErrorCode.Should().BeNull();
        device.Verify(d => d.SignAsync(Payload, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SignAsync_NullPayload_ReturnsFailure_WithoutCallingDeviceKey()
    {
        var device = new Mock<IDeviceKeyService>(MockBehavior.Strict);
        var signer = new ManagedUserSigner(device.Object, NullLogger<ManagedUserSigner>.Instance);

        var result = await signer.SignAsync(new SigningRequest(SigningOperation.Generic, PayloadToSign: null!));

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("ERR_USERSIGNER_NO_PAYLOAD");
        device.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task SignAsync_EmptyPayload_ReturnsFailure()
    {
        var signer = new ManagedUserSigner(Mock.Of<IDeviceKeyService>(), NullLogger<ManagedUserSigner>.Instance);
        var result = await signer.SignAsync(new SigningRequest(SigningOperation.Generic, Array.Empty<byte>()));

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("ERR_USERSIGNER_NO_PAYLOAD");
    }

    [Fact]
    public async Task SignAsync_DeviceKeyThrows_ReturnsFailure_WithUserSafeMessage()
    {
        var device = new Mock<IDeviceKeyService>();
        device.Setup(d => d.SignAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
              .ThrowsAsync(new InvalidOperationException("WebCrypto unavailable"));

        var signer = new ManagedUserSigner(device.Object, NullLogger<ManagedUserSigner>.Instance);
        var result = await signer.SignAsync(new SigningRequest(SigningOperation.ActionSubmission, Payload));

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("ERR_USERSIGNER_DEVICE_KEY_FAILED");
        result.ErrorDetail.Should().NotContain("WebCrypto", "Error detail surfaced to UI must be user-safe, not raw exception text.");
    }

    [Fact]
    public async Task SignAsync_CancellationPropagates()
    {
        var device = new Mock<IDeviceKeyService>();
        device.Setup(d => d.SignAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
              .ThrowsAsync(new OperationCanceledException());

        var signer = new ManagedUserSigner(device.Object, NullLogger<ManagedUserSigner>.Instance);
        var act = async () => await signer.SignAsync(new SigningRequest(SigningOperation.Generic, Payload));

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
