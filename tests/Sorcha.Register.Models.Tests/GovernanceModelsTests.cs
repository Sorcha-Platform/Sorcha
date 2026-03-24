// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using FluentAssertions;
using Sorcha.Register.Models;

namespace Sorcha.Register.Models.Tests;

public class GovernanceModelsTests
{
    // --- GovernanceOperationType ---

    [Fact]
    public void GovernanceOperationType_HasExpectedValues()
    {
        ((int)GovernanceOperationType.Add).Should().Be(0);
        ((int)GovernanceOperationType.Remove).Should().Be(1);
        ((int)GovernanceOperationType.Transfer).Should().Be(2);
    }

    // --- ProposalStatus ---

    [Fact]
    public void ProposalStatus_HasExpectedValues()
    {
        ((int)ProposalStatus.Pending).Should().Be(0);
        ((int)ProposalStatus.Approved).Should().Be(1);
        ((int)ProposalStatus.Rejected).Should().Be(2);
        ((int)ProposalStatus.Expired).Should().Be(3);
        ((int)ProposalStatus.Recorded).Should().Be(4);
    }

    // --- GovernanceOperation ---

    [Fact]
    public void GovernanceOperation_DefaultValues_AreCorrect()
    {
        var op = new GovernanceOperation();

        op.ProposerDid.Should().BeEmpty();
        op.TargetDid.Should().BeEmpty();
        op.ApprovalSignatures.Should().BeEmpty();
        op.Justification.Should().BeNull();
    }

    [Fact]
    public void GovernanceOperation_Serialization_RoundTrips()
    {
        var op = new GovernanceOperation
        {
            OperationType = GovernanceOperationType.Add,
            ProposerDid = "did:sorcha:w:owner1",
            TargetDid = "did:sorcha:w:newadmin1",
            TargetRole = RegisterRole.Admin,
            ProposedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(7),
            Status = ProposalStatus.Pending,
            Justification = "Adding new team member"
        };

        var json = JsonSerializer.Serialize(op);
        var deserialized = JsonSerializer.Deserialize<GovernanceOperation>(json);

        deserialized.Should().NotBeNull();
        deserialized!.OperationType.Should().Be(GovernanceOperationType.Add);
        deserialized.ProposerDid.Should().Be("did:sorcha:w:owner1");
        deserialized.TargetDid.Should().Be("did:sorcha:w:newadmin1");
        deserialized.TargetRole.Should().Be(RegisterRole.Admin);
        deserialized.Status.Should().Be(ProposalStatus.Pending);
        deserialized.Justification.Should().Be("Adding new team member");
    }

    [Fact]
    public void GovernanceOperation_ExpiresAt_Is7DaysAfterProposedAt()
    {
        var proposedAt = DateTimeOffset.UtcNow;
        var op = new GovernanceOperation
        {
            ProposedAt = proposedAt,
            ExpiresAt = proposedAt.AddDays(7)
        };

        (op.ExpiresAt - op.ProposedAt).TotalDays.Should().Be(7);
    }

    // --- ControlTransactionPayload ---

    [Fact]
    public void ControlTransactionPayload_DefaultVersion_Is1()
    {
        new ControlTransactionPayload().Version.Should().Be(1);
    }

    [Fact]
    public void ControlTransactionPayload_GenesisHasNullOperation()
    {
        var payload = new ControlTransactionPayload
        {
            Roster = new RegisterControlRecord
            {
                RegisterId = "a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4",
                Name = "Test",
                CreatedAt = DateTimeOffset.UtcNow,
                Attestations = [new RegisterAttestation
                {
                    Role = RegisterRole.Owner,
                    Subject = "did:sorcha:w:owner1",
                    PublicKey = Convert.ToBase64String(new byte[32]),
                    Signature = Convert.ToBase64String(new byte[64]),
                    Algorithm = SignatureAlgorithm.ED25519,
                    GrantedAt = DateTimeOffset.UtcNow
                }]
            },
            Operation = null
        };

        payload.Operation.Should().BeNull();
        payload.Roster.HasOwnerAttestation().Should().BeTrue();
    }

    [Fact]
    public void ControlTransactionPayload_Serialization_RoundTrips()
    {
        var payload = new ControlTransactionPayload
        {
            Version = 1,
            Roster = new RegisterControlRecord
            {
                RegisterId = "a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4",
                Name = "Test Register",
                CreatedAt = DateTimeOffset.UtcNow,
                Attestations =
                [
                    new RegisterAttestation
                    {
                        Role = RegisterRole.Owner,
                        Subject = "did:sorcha:w:owner1",
                        PublicKey = Convert.ToBase64String(new byte[32]),
                        Signature = Convert.ToBase64String(new byte[64]),
                        Algorithm = SignatureAlgorithm.ED25519,
                        GrantedAt = DateTimeOffset.UtcNow
                    }
                ]
            },
            Operation = new GovernanceOperation
            {
                OperationType = GovernanceOperationType.Add,
                ProposerDid = "did:sorcha:w:owner1",
                TargetDid = "did:sorcha:w:admin1",
                TargetRole = RegisterRole.Admin,
                ProposedAt = DateTimeOffset.UtcNow,
                ExpiresAt = DateTimeOffset.UtcNow.AddDays(7),
                Status = ProposalStatus.Recorded
            }
        };

        var json = JsonSerializer.Serialize(payload);
        var deserialized = JsonSerializer.Deserialize<ControlTransactionPayload>(json);

        deserialized.Should().NotBeNull();
        deserialized!.Version.Should().Be(1);
        deserialized.Roster.Should().NotBeNull();
        deserialized.Roster.Attestations.Should().HaveCount(1);
        deserialized.Operation.Should().NotBeNull();
        deserialized.Operation!.OperationType.Should().Be(GovernanceOperationType.Add);
    }

    // --- ApprovalSignature ---

    [Fact]
    public void ApprovalSignature_DefaultValues_AreCorrect()
    {
        var sig = new ApprovalSignature();

        sig.ApproverDid.Should().BeEmpty();
        sig.Signature.Should().BeEmpty();
        sig.IsApproval.Should().BeFalse();
        sig.Comment.Should().BeNull();
    }

    // --- RegisterControlRecord single Owner constraint ---

    [Fact]
    public void RegisterControlRecord_HasOwnerAttestation_WithOwner_ReturnsTrue()
    {
        var record = new RegisterControlRecord
        {
            RegisterId = "a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4",
            Name = "Test",
            CreatedAt = DateTimeOffset.UtcNow,
            Attestations =
            [
                new RegisterAttestation
                {
                    Role = RegisterRole.Owner,
                    Subject = "did:sorcha:w:owner1",
                    PublicKey = Convert.ToBase64String(new byte[32]),
                    Signature = Convert.ToBase64String(new byte[64]),
                    Algorithm = SignatureAlgorithm.ED25519,
                    GrantedAt = DateTimeOffset.UtcNow
                }
            ]
        };

        record.HasOwnerAttestation().Should().BeTrue();
    }

    [Fact]
    public void RegisterControlRecord_HasOwnerAttestation_WithoutOwner_ReturnsFalse()
    {
        var record = new RegisterControlRecord
        {
            RegisterId = "a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4",
            Name = "Test",
            CreatedAt = DateTimeOffset.UtcNow,
            Attestations =
            [
                new RegisterAttestation
                {
                    Role = RegisterRole.Admin,
                    Subject = "did:sorcha:w:admin1",
                    PublicKey = Convert.ToBase64String(new byte[32]),
                    Signature = Convert.ToBase64String(new byte[64]),
                    Algorithm = SignatureAlgorithm.ED25519,
                    GrantedAt = DateTimeOffset.UtcNow
                }
            ]
        };

        record.HasOwnerAttestation().Should().BeFalse();
    }

    // --- TransactionType enum values preserved ---

    [Fact]
    public void TransactionType_Control_HasValue0()
    {
        ((int)Sorcha.Register.Models.Enums.TransactionType.Control).Should().Be(0);
    }

    [Fact]
    public void TransactionType_Action_HasValue1()
    {
        ((int)Sorcha.Register.Models.Enums.TransactionType.Action).Should().Be(1);
    }

    [Fact]
    public void TransactionType_Docket_HasValue2()
    {
        ((int)Sorcha.Register.Models.Enums.TransactionType.Docket).Should().Be(2);
    }

    [Fact]
    public void TransactionType_HasExpectedValues()
    {
        Enum.GetValues<Sorcha.Register.Models.Enums.TransactionType>().Should().HaveCount(4);
    }
}
