// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Sorcha.Register.Core.Services;
using Sorcha.Register.Core.Storage;
using Sorcha.Register.Models;
using Sorcha.Register.Models.Enums;
using Xunit;

namespace Sorcha.Register.Core.Tests.Services;

public class TransferOwnershipTests
{
    private readonly GovernanceRosterService _service;

    public TransferOwnershipTests()
    {
        var repository = new Mock<IRegisterRepository>();
        var logger = new Mock<ILogger<GovernanceRosterService>>();
        _service = new GovernanceRosterService(repository.Object, logger.Object);
    }

    private static AdminRoster CreateRoster(params (string did, RegisterRole role)[] members)
    {
        return new AdminRoster
        {
            RegisterId = "a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4",
            ControlRecord = new RegisterControlRecord
            {
                RegisterId = "a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4",
                Name = "Test Register",
                CreatedAt = DateTimeOffset.UtcNow,
                Attestations = members.Select(m => new RegisterAttestation
                {
                    Role = m.role,
                    Subject = m.did,
                    PublicKey = Convert.ToBase64String(new byte[32]),
                    Signature = Convert.ToBase64String(new byte[64]),
                    Algorithm = SignatureAlgorithm.ED25519,
                    GrantedAt = DateTimeOffset.UtcNow
                }).ToList()
            },
            ControlTransactionCount = 1
        };
    }

    // --- Transfer Validation ---

    [Fact]
    public void ValidateProposal_OwnerTransfersToAdmin_Valid()
    {
        var roster = CreateRoster(
            ("did:sorcha:w:owner1", RegisterRole.Owner),
            ("did:sorcha:w:admin1", RegisterRole.Admin));
        var operation = new GovernanceOperation
        {
            OperationType = GovernanceOperationType.Transfer,
            ProposerDid = "did:sorcha:w:owner1",
            TargetDid = "did:sorcha:w:admin1",
            TargetRole = RegisterRole.Owner,
            ProposedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(7)
        };

        var result = _service.ValidateProposal(roster, operation);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void ValidateProposal_NonOwnerTransfer_Rejected()
    {
        var roster = CreateRoster(
            ("did:sorcha:w:owner1", RegisterRole.Owner),
            ("did:sorcha:w:admin1", RegisterRole.Admin));
        var operation = new GovernanceOperation
        {
            OperationType = GovernanceOperationType.Transfer,
            ProposerDid = "did:sorcha:w:admin1",
            TargetDid = "did:sorcha:w:owner1",
            TargetRole = RegisterRole.Owner,
            ProposedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(7)
        };

        var result = _service.ValidateProposal(roster, operation);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Only the Owner"));
    }

    [Fact]
    public void ValidateProposal_TransferToAuditor_Rejected()
    {
        var roster = CreateRoster(
            ("did:sorcha:w:owner1", RegisterRole.Owner),
            ("did:sorcha:w:auditor1", RegisterRole.Auditor));
        var operation = new GovernanceOperation
        {
            OperationType = GovernanceOperationType.Transfer,
            ProposerDid = "did:sorcha:w:owner1",
            TargetDid = "did:sorcha:w:auditor1",
            TargetRole = RegisterRole.Owner,
            ProposedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(7)
        };

        var result = _service.ValidateProposal(roster, operation);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("existing Admin"));
    }

    [Fact]
    public void ValidateProposal_TransferToNonMember_Rejected()
    {
        var roster = CreateRoster(("did:sorcha:w:owner1", RegisterRole.Owner));
        var operation = new GovernanceOperation
        {
            OperationType = GovernanceOperationType.Transfer,
            ProposerDid = "did:sorcha:w:owner1",
            TargetDid = "did:sorcha:w:nobody",
            TargetRole = RegisterRole.Owner,
            ProposedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(7)
        };

        var result = _service.ValidateProposal(roster, operation);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("not in the roster"));
    }

    // --- Transfer Execution ---

    [Fact]
    public void ApplyOperation_Transfer_SwapsOwnerAndAdmin()
    {
        var roster = CreateRoster(
            ("did:sorcha:w:owner1", RegisterRole.Owner),
            ("did:sorcha:w:admin1", RegisterRole.Admin));
        var operation = new GovernanceOperation
        {
            OperationType = GovernanceOperationType.Transfer,
            ProposerDid = "did:sorcha:w:owner1",
            TargetDid = "did:sorcha:w:admin1",
            TargetRole = RegisterRole.Owner
        };

        var result = _service.ApplyOperation(roster.ControlRecord, operation);

        result.Attestations.Should().ContainSingle(a => a.Role == RegisterRole.Owner)
            .Which.Subject.Should().Be("did:sorcha:w:admin1");
        result.Attestations.Should().ContainSingle(a => a.Role == RegisterRole.Admin)
            .Which.Subject.Should().Be("did:sorcha:w:owner1");
    }

    [Fact]
    public void ApplyOperation_Transfer_RosterSizeUnchanged()
    {
        var roster = CreateRoster(
            ("did:sorcha:w:owner1", RegisterRole.Owner),
            ("did:sorcha:w:admin1", RegisterRole.Admin),
            ("did:sorcha:w:admin2", RegisterRole.Admin));
        var operation = new GovernanceOperation
        {
            OperationType = GovernanceOperationType.Transfer,
            ProposerDid = "did:sorcha:w:owner1",
            TargetDid = "did:sorcha:w:admin2",
            TargetRole = RegisterRole.Owner
        };

        var result = _service.ApplyOperation(roster.ControlRecord, operation);

        result.Attestations.Should().HaveCount(3);
    }

    [Fact]
    public void ApplyOperation_Transfer_SingleOwnerInvariant()
    {
        var roster = CreateRoster(
            ("did:sorcha:w:owner1", RegisterRole.Owner),
            ("did:sorcha:w:admin1", RegisterRole.Admin));
        var operation = new GovernanceOperation
        {
            OperationType = GovernanceOperationType.Transfer,
            ProposerDid = "did:sorcha:w:owner1",
            TargetDid = "did:sorcha:w:admin1",
            TargetRole = RegisterRole.Owner
        };

        var result = _service.ApplyOperation(roster.ControlRecord, operation);

        result.Attestations.Count(a => a.Role == RegisterRole.Owner).Should().Be(1);
    }

    [Fact]
    public void ApplyOperation_Transfer_OldOwnerBecomesAdmin()
    {
        var roster = CreateRoster(
            ("did:sorcha:w:owner1", RegisterRole.Owner),
            ("did:sorcha:w:admin1", RegisterRole.Admin));
        var operation = new GovernanceOperation
        {
            OperationType = GovernanceOperationType.Transfer,
            ProposerDid = "did:sorcha:w:owner1",
            TargetDid = "did:sorcha:w:admin1",
            TargetRole = RegisterRole.Owner
        };

        var result = _service.ApplyOperation(roster.ControlRecord, operation);

        var oldOwner = result.Attestations.First(a => a.Subject == "did:sorcha:w:owner1");
        oldOwner.Role.Should().Be(RegisterRole.Admin);
    }

    [Fact]
    public void ApplyOperation_Transfer_PreservesOtherMembers()
    {
        var roster = CreateRoster(
            ("did:sorcha:w:owner1", RegisterRole.Owner),
            ("did:sorcha:w:admin1", RegisterRole.Admin),
            ("did:sorcha:w:admin2", RegisterRole.Admin),
            ("did:sorcha:w:auditor1", RegisterRole.Auditor));
        var operation = new GovernanceOperation
        {
            OperationType = GovernanceOperationType.Transfer,
            ProposerDid = "did:sorcha:w:owner1",
            TargetDid = "did:sorcha:w:admin1",
            TargetRole = RegisterRole.Owner
        };

        var result = _service.ApplyOperation(roster.ControlRecord, operation);

        result.Attestations.Should().HaveCount(4);
        result.Attestations.Should().Contain(a => a.Subject == "did:sorcha:w:admin2" && a.Role == RegisterRole.Admin);
        result.Attestations.Should().Contain(a => a.Subject == "did:sorcha:w:auditor1" && a.Role == RegisterRole.Auditor);
    }
}
