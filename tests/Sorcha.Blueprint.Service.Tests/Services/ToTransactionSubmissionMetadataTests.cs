// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Sorcha.Blueprint.Service.Services.Implementation;
using Sorcha.Blueprint.Service.Services.Interfaces;
using Sorcha.Cryptography.Interfaces;
using Sorcha.ServiceClients.Wallet;
using Xunit;
using ActionModel = Sorcha.Blueprint.Models.Action;
using BlueprintModel = Sorcha.Blueprint.Models.Blueprint;
using Instance = Sorcha.Blueprint.Service.Models.Instance;

namespace Sorcha.Blueprint.Service.Tests.Services;

/// <summary>
/// Regression guard for the Feature 111 US3 retry-gate contract:
/// <c>BuiltTransaction.ToTransactionSubmission</c> must propagate
/// <c>outcomeKind</c>, <c>presentationRequestId</c>, and <c>consumerName</c>
/// from the in-memory Metadata dict onto <c>TransactionSubmission.Metadata</c>,
/// so downstream DocketSerializer copies them into
/// <c>TransactionMetaData.TrackingData</c>. Without this, the retry gate
/// cannot see which prior outcomes are successes.
/// </summary>
public class ToTransactionSubmissionMetadataTests
{
    private readonly TransactionBuilderService _service = new(
        new Mock<ICryptoModule>().Object,
        new Mock<IHashProvider>().Object,
        new Mock<ISymmetricCrypto>().Object,
        new Mock<ILogger<TransactionBuilderService>>().Object);

    private static BlueprintModel MakeBp() => new()
    {
        Id = "bp-1", Title = "t", Description = "d", Version = 1,
        Participants = [], Actions = []
    };
    private static Instance MakeInst() => new()
    {
        Id = Guid.NewGuid().ToString(), BlueprintId = "bp-1", BlueprintVersion = 1,
        RegisterId = "reg-1", TenantId = "t"
    };
    private static ActionModel MakeAct() => new() { Id = 3, BlueprintId = "bp-1" };

    private static WalletSignResult MakeSig() => new()
    {
        Signature = new byte[] { 0x1 },
        PublicKey = new byte[] { 0x2 },
        Algorithm = "ED25519",
        SignedBy = "w"
    };

    [Fact]
    public async Task ToTransactionSubmission_PresentationOutcomeSuccess_ExposesOutcomeKindOnMetadata()
    {
        var built = await _service.BuildPresentationOutcomeAsync(
            MakeBp(), MakeInst(), MakeAct(),
            presentationRequestId: Guid.NewGuid(),
            consumerName: "haip",
            submitterWallet: "ws11qcitizen",
            outcomeKind: "success",
            verifiedClaims: new Dictionary<string, object>(),
            declineReason: null,
            verifierDiagnostics: null,
            presentationSubmissionHash: "sha256:abc",
            actionPayload: null,
            previousTransactionId: null);

        built.SenderWallet = "ws11qcitizen";
        var submission = built.ToTransactionSubmission(MakeSig(), sequenceNumber: 1);

        submission.Metadata.Should().NotBeNull();
        submission.Metadata!["outcomeKind"].Should().Be("success");
        submission.Metadata!["consumerName"].Should().Be("haip");
        submission.Metadata!["presentationRequestId"].Should().NotBeNullOrWhiteSpace();
        submission.Metadata!["Type"].Should().Be("PresentationOutcome");
    }

    [Fact]
    public async Task ToTransactionSubmission_PresentationOutcomeDecline_ExposesDeclineKindOnMetadata()
    {
        var built = await _service.BuildPresentationOutcomeAsync(
            MakeBp(), MakeInst(), MakeAct(),
            presentationRequestId: Guid.NewGuid(),
            consumerName: "haip",
            submitterWallet: "ws11qcitizen",
            outcomeKind: "decline",
            verifiedClaims: null,
            declineReason: "ExpiredCredential",
            verifierDiagnostics: null,
            presentationSubmissionHash: null,
            actionPayload: null,
            previousTransactionId: null);

        built.SenderWallet = "ws11qcitizen";
        var submission = built.ToTransactionSubmission(MakeSig(), sequenceNumber: 1);

        submission.Metadata!["outcomeKind"].Should().Be("decline");
    }

    [Fact]
    public async Task ToTransactionSubmission_PresentationInitiated_ExposesConsumerAndRequestId()
    {
        var requestId = Guid.NewGuid();
        var built = await _service.BuildPresentationInitiatedAsync(
            MakeBp(), MakeInst(), MakeAct(),
            presentationRequestId: requestId,
            consumerName: "haip",
            requirementsDigest: new byte[] { 0xAB, 0xCD },
            validityWindowSeconds: 600,
            submitterWallet: "ws11qcitizen",
            previousTransactionId: null);

        built.SenderWallet = "ws11qcitizen";
        var submission = built.ToTransactionSubmission(MakeSig(), sequenceNumber: 1);

        submission.Metadata!["consumerName"].Should().Be("haip");
        submission.Metadata!["presentationRequestId"].Should().Be(requestId.ToString());
        submission.Metadata!["Type"].Should().Be("PresentationInitiated");
        // PresentationInitiated has no outcomeKind — ensure we don't leak anything stale.
        submission.Metadata!.ContainsKey("outcomeKind").Should().BeFalse();
    }
}
