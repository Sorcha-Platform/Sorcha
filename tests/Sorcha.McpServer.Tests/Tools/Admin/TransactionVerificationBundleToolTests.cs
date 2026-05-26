// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using Microsoft.Extensions.Logging;
using Sorcha.McpServer.Infrastructure;
using Sorcha.McpServer.Services;
using Sorcha.McpServer.Tools.Admin;
using Sorcha.Register.Models;
using Sorcha.ServiceClients.Register;

namespace Sorcha.McpServer.Tests.Tools.Admin;

/// <summary>
/// Feature 140 Wave 1: TransactionVerificationBundleTool routes through the typed <see cref="IRegisterServiceClient"/>.
/// </summary>
public class TransactionVerificationBundleToolTests
{
    private readonly Mock<IMcpAuthorizationService> _authServiceMock = new();
    private readonly Mock<IServiceAvailabilityTracker> _availabilityTrackerMock = new();
    private readonly Mock<IRegisterServiceClient> _registerClientMock = new();

    private TransactionVerificationBundleTool CreateTool() => new(
        _authServiceMock.Object,
        _availabilityTrackerMock.Object,
        _registerClientMock.Object,
        Mock.Of<ILogger<TransactionVerificationBundleTool>>());

    private void Allow()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_transaction_verification_bundle")).Returns(true);
        _availabilityTrackerMock.Setup(a => a.IsServiceAvailable("Register")).Returns(true);
    }

    private static VerificationBundle BuildBundle(string txId)
    {
        var proof = new MerkleInclusionProof
        {
            TransactionHash = "abc",
            DocketNumber = 3,
            MerkleRoot = "root",
            ProofPath = [],
            LeafIndex = 0,
            TreeSize = 1
        };
        var receipt = new TransactionReceipt
        {
            ReceiptId = "rcpt-1",
            TransactionId = txId,
            RegisterId = "reg-1",
            DocketNumber = 3,
            MerkleRoot = "root",
            InclusionProof = proof,
            Signatures = [],
            SealedAt = DateTimeOffset.UtcNow
        };
        return new VerificationBundle
        {
            TransactionId = txId,
            RegisterId = "reg-1",
            Credential = JsonSerializer.Deserialize<JsonElement>("[]"),
            Receipt = receipt,
            RevocationStatus = new TransactionStatusResponse
            {
                TransactionId = txId,
                Status = TransactionLifecycleStatus.Active
            },
            ExportedAt = DateTimeOffset.UtcNow,
            ValidatorPublicKeys = []
        };
    }

    [Fact]
    public async Task GetBundleAsync_Unauthorized_ReturnsUnauthorized()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_transaction_verification_bundle")).Returns(false);

        var result = await CreateTool().GetBundleAsync("reg-1", "tx-1");

        result.Status.Should().Be("Unauthorized");
        result.Bundle.Should().BeNull();
    }

    [Fact]
    public async Task GetBundleAsync_MissingArgs_ReturnsError()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_transaction_verification_bundle")).Returns(true);

        var result = await CreateTool().GetBundleAsync("", "");

        result.Status.Should().Be("Error");
    }

    [Fact]
    public async Task GetBundleAsync_ClientReturnsNull_ReturnsNotFound()
    {
        Allow();
        _registerClientMock
            .Setup(c => c.GetVerificationBundleAsync("reg-1", "tx-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((VerificationBundle?)null);

        var result = await CreateTool().GetBundleAsync("reg-1", "tx-1");

        result.Status.Should().Be("NotFound");
        _availabilityTrackerMock.Verify(a => a.RecordSuccess("Register"), Times.Once);
    }

    [Fact]
    public async Task GetBundleAsync_Success_ReturnsBundle()
    {
        Allow();
        _registerClientMock
            .Setup(c => c.GetVerificationBundleAsync("reg-1", "tx-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildBundle("tx-1"));

        var result = await CreateTool().GetBundleAsync("reg-1", "tx-1");

        result.Status.Should().Be("Success");
        result.Bundle.Should().NotBeNull();
        result.Bundle!.TransactionId.Should().Be("tx-1");
        result.Bundle.RevocationStatus.Status.Should().Be(TransactionLifecycleStatus.Active);
    }
}
