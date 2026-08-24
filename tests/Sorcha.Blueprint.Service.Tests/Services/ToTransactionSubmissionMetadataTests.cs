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
        Id = Guid.NewGuid().ToString(), BlueprintId = "bp-1", BlueprintDefinitionTxId = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", BlueprintVersion = 1,
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

    [Fact]
    public async Task ToTransactionSubmission_CarriesRoutingDecision_AndNoLegacyNextActionId()
    {
        // Feature 145 US5: the submission whitelist must propagate `routingDecision` (the carried,
        // signed decision the validator validates + the seal carries) — and must NOT carry the
        // removed legacy `nextActionId` hint. This is the hop that was silently dropping the
        // decision end-to-end before the fix.
        var built = await _service.BuildPresentationInitiatedAsync(
            MakeBp(), MakeInst(), MakeAct(),
            presentationRequestId: Guid.NewGuid(),
            consumerName: "haip",
            requirementsDigest: new byte[] { 0x01 },
            validityWindowSeconds: 600,
            submitterWallet: "ws11qcitizen",
            previousTransactionId: null);
        built.SenderWallet = "ws11qcitizen";
        const string canonicalDecision = "{\"completedActionId\":1,\"nextActions\":[{\"actionId\":2}],\"attestation\":{\"kind\":\"SenderSigned\",\"signature\":\"sig\"}}";
        built.Metadata["routingDecision"] = canonicalDecision;
        // A producer that still wrote the legacy key must NOT leak it through the whitelist.
        built.Metadata["nextActionId"] = "2";

        var submission = built.ToTransactionSubmission(MakeSig(), sequenceNumber: 1);

        submission.Metadata!["routingDecision"].Should().Be(canonicalDecision);
        submission.Metadata!.ContainsKey("nextActionId").Should().BeFalse();
    }

    [Fact]
    public void ToTransactionSubmission_CredentialIssuance_CarriesTypeAndCredentialId()
    {
        // Feature 155: the public anchor endpoint
        // (GET /api/registers/{registerId}/credentials/{credentialId}/anchor) locates the issuance
        // tx by sealed TrackingData["type"]=="credential-issuance" AND ["credentialId"]==<id>.
        // The submission whitelist must propagate both keys from the BuiltTransaction.Metadata dict
        // exactly as RecordCredentialOnRegisterAsync populates them.
        const string credentialId = "urn:uuid:3fa85f64-5717-4562-b3fc-2c963f66afa6";
        var built = new BuiltTransaction
        {
            TransactionData = JsonSerializer.SerializeToUtf8Bytes(new { type = "credential-issuance", credentialId }),
            TxId = "tx-cred-1",
            PayloadHash = "tx-cred-1",
            TransactionType = "credential-issuance",
            RegisterId = "reg-1",
            SenderWallet = "ws11qissuer",
            Metadata = new Dictionary<string, object>
            {
                ["blueprintId"] = "inst-1",
                ["actionId"] = 0,
                ["instanceId"] = "inst-1",
                ["previousTxId"] = "prev-tx",
                ["type"] = "credential-issuance",
                ["credentialId"] = credentialId,
                ["credentialType"] = "MembershipCredential"
            }
        };

        var submission = built.ToTransactionSubmission(MakeSig(), sequenceNumber: 1);

        submission.Metadata!["type"].Should().Be("credential-issuance");
        submission.Metadata!["credentialId"].Should().Be(credentialId);
        // ["Type"] (mapped enum name) and the lowercase domain ["type"] coexist independently.
        submission.Metadata!["Type"].Should().Be("Action");
    }

    [Fact]
    public async Task ToTransactionSubmission_NonCredentialTransaction_OmitsTypeAndCredentialId()
    {
        // Additive guard: a transaction with no `type`/`credentialId` in its Metadata must not
        // gain those keys (they remain absent), so non-credential txs are unaffected.
        var built = await _service.BuildPresentationInitiatedAsync(
            MakeBp(), MakeInst(), MakeAct(),
            presentationRequestId: Guid.NewGuid(),
            consumerName: "haip",
            requirementsDigest: new byte[] { 0x01 },
            validityWindowSeconds: 600,
            submitterWallet: "ws11qcitizen",
            previousTransactionId: null);
        built.SenderWallet = "ws11qcitizen";

        var submission = built.ToTransactionSubmission(MakeSig(), sequenceNumber: 1);

        submission.Metadata!.ContainsKey("type").Should().BeFalse();
        submission.Metadata!.ContainsKey("credentialId").Should().BeFalse();
    }
}
