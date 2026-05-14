// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sorcha.UI.Components.User.Services.Signing;
using Sorcha.Wallet.Pwa.Services.Applications;
using Sorcha.Wallet.Pwa.Services.Context;
using Xunit;

namespace Sorcha.Wallet.Pwa.Tests.Services.Applications;

/// <summary>
/// Tests for <see cref="StubApplicationSubmissionService"/> (Feature 125, PR-D).
/// Verifies the IUserSigner-integration contract: signing operation is
/// <see cref="SigningOperation.ActionSubmission"/>, active context flows
/// through, and the four submission outcomes (Success / ValidationFailed /
/// SigningFailed / ServerError) map to the right result shape.
/// </summary>
public sealed class StubApplicationSubmissionServiceTests
{
    private static readonly byte[] Payload = Encoding.UTF8.GetBytes("{}");
    private static readonly Guid BlueprintId = Guid.NewGuid();

    private static (StubApplicationSubmissionService sut, Mock<IUserSigner> signer)
        NewSut(Guid? activeContextOrgId = null, SigningResult? signingResult = null)
    {
        var signer = new Mock<IUserSigner>();
        signer.SetupGet(s => s.CustodyMode).Returns(UserCustodyMode.Managed);
        signer.SetupGet(s => s.DisplayLabel).Returns("Sign with your Sorcha Wallet");
        signer.Setup(s => s.SignAsync(It.IsAny<SigningRequest>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(signingResult ?? SigningResult.Ok(new byte[] { 1, 2, 3 }, "ES256"));

        var ctx = new InMemoryUserContext();
        if (activeContextOrgId is not null)
            ctx.SetActiveContextAsync(activeContextOrgId).Wait();

        return (new StubApplicationSubmissionService(signer.Object, ctx, NullLogger<StubApplicationSubmissionService>.Instance),
                signer);
    }

    [Fact]
    public async Task SubmitAsync_HappyPath_ReturnsSuccess_WithSyntheticInstanceId()
    {
        var (sut, _) = NewSut();
        var result = await sut.SubmitAsync(new ApplicationSubmissionRequest(
            BlueprintId, "Driving Licence", ActionId: 1, Payload));

        result.Status.Should().Be(ApplicationSubmissionStatus.Success);
        result.InstanceId.Should().NotBeNull();
        result.InstanceId.Should().NotBe(Guid.Empty);
        result.ErrorCode.Should().BeNull();
    }

    [Fact]
    public async Task SubmitAsync_PassesActionSubmissionOperation_AndActiveContext_ToSigner()
    {
        var orgId = Guid.NewGuid();
        var (sut, signer) = NewSut(activeContextOrgId: orgId);

        await sut.SubmitAsync(new ApplicationSubmissionRequest(BlueprintId, "App", 2, Payload));

        signer.Verify(s => s.SignAsync(
            It.Is<SigningRequest>(r => r.Operation == SigningOperation.ActionSubmission
                                       && r.ActiveContextOrgId == orgId
                                       && r.PayloadToSign == Payload),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SubmitAsync_EmptyPayload_ReturnsValidationFailed_WithoutSigning()
    {
        var (sut, signer) = NewSut();
        var result = await sut.SubmitAsync(new ApplicationSubmissionRequest(
            BlueprintId, "App", 1, Array.Empty<byte>()));

        result.Status.Should().Be(ApplicationSubmissionStatus.ValidationFailed);
        result.ErrorCode.Should().Be("ERR_APPSUBMIT_EMPTY_PAYLOAD");
        signer.Verify(s => s.SignAsync(It.IsAny<SigningRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SubmitAsync_SignerFails_ReturnsSigningFailed_WithSurfacedDetail()
    {
        var (sut, _) = NewSut(signingResult: SigningResult.Fail("ERR_X", "WebCrypto unavailable"));
        var result = await sut.SubmitAsync(new ApplicationSubmissionRequest(
            BlueprintId, "App", 1, Payload));

        result.Status.Should().Be(ApplicationSubmissionStatus.SigningFailed);
        result.ErrorCode.Should().Be("ERR_X");
        result.ErrorDetail.Should().Be("WebCrypto unavailable");
    }

    [Fact]
    public async Task SubmitAsync_SamePayloadTwice_ReturnsSameInstanceId_StableForRetry()
    {
        var (sut, _) = NewSut();
        var r1 = await sut.SubmitAsync(new ApplicationSubmissionRequest(BlueprintId, "App", 1, Payload));
        var r2 = await sut.SubmitAsync(new ApplicationSubmissionRequest(BlueprintId, "App", 1, Payload));
        r1.InstanceId.Should().Be(r2.InstanceId, "deterministic id keeps UI continuity across retries.");
    }
}
