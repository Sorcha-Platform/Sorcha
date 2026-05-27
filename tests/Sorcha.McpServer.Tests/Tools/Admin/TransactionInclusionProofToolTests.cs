// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Logging;
using Sorcha.McpServer.Infrastructure;
using Sorcha.McpServer.Services;
using Sorcha.McpServer.Tools.Admin;
using Sorcha.Register.Models;
using Sorcha.ServiceClients.Register;

namespace Sorcha.McpServer.Tests.Tools.Admin;

/// <summary>
/// Feature 140 Wave 1: TransactionInclusionProofTool routes through the typed <see cref="IRegisterServiceClient"/>.
/// </summary>
public class TransactionInclusionProofToolTests
{
    private readonly Mock<IMcpAuthorizationService> _authServiceMock = new();
    private readonly Mock<IServiceAvailabilityTracker> _availabilityTrackerMock = new();
    private readonly Mock<IRegisterServiceClient> _registerClientMock = new();

    private TransactionInclusionProofTool CreateTool() => new(
        _authServiceMock.Object,
        _availabilityTrackerMock.Object,
        _registerClientMock.Object,
        Mock.Of<ILogger<TransactionInclusionProofTool>>());

    private void Allow()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_transaction_inclusion_proof")).Returns(true);
        _availabilityTrackerMock.Setup(a => a.IsServiceAvailable("Register")).Returns(true);
    }

    [Fact]
    public async Task GetProofAsync_Unauthorized_ReturnsUnauthorized()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_transaction_inclusion_proof")).Returns(false);

        var result = await CreateTool().GetProofAsync("reg-1", "tx-1");

        result.Status.Should().Be("Unauthorized");
        result.Proof.Should().BeNull();
    }

    [Fact]
    public async Task GetProofAsync_MissingArgs_ReturnsError()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_transaction_inclusion_proof")).Returns(true);

        var result = await CreateTool().GetProofAsync("", "tx-1");

        result.Status.Should().Be("Error");
    }

    [Fact]
    public async Task GetProofAsync_ClientReturnsNull_ReturnsNotFound()
    {
        Allow();
        _registerClientMock
            .Setup(c => c.GetInclusionProofAsync("reg-1", "tx-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((MerkleInclusionProof?)null);

        var result = await CreateTool().GetProofAsync("reg-1", "tx-1");

        result.Status.Should().Be("NotFound");
        _availabilityTrackerMock.Verify(a => a.RecordSuccess("Register"), Times.Once);
    }

    [Fact]
    public async Task GetProofAsync_Success_ReturnsProof()
    {
        Allow();
        var proof = new MerkleInclusionProof
        {
            TransactionHash = "abc",
            DocketNumber = 7,
            MerkleRoot = "root",
            ProofPath = [new MerkleProofStep { Hash = "sib", Position = ProofPosition.Left }],
            LeafIndex = 0,
            TreeSize = 2
        };
        _registerClientMock
            .Setup(c => c.GetInclusionProofAsync("reg-1", "tx-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(proof);

        var result = await CreateTool().GetProofAsync("reg-1", "tx-1");

        result.Status.Should().Be("Success");
        result.Proof.Should().NotBeNull();
        result.Proof!.DocketNumber.Should().Be(7);
        result.Proof.ProofPath.Should().HaveCount(1);
    }
}
