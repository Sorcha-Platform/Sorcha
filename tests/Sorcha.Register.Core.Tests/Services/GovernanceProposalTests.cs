// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Sorcha.Register.Core.Services;
using Sorcha.Register.Core.Storage;
using Sorcha.Register.Models;
using Xunit;

namespace Sorcha.Register.Core.Tests.Services;

public class GovernanceProposalTests
{
    private readonly GovernanceRosterService _service;

    public GovernanceProposalTests()
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

    // --- Add Proposal Validation ---

    [Fact]
    public void ValidateProposal_ValidAdd_ReturnsSuccess()
    {
        var roster = CreateRoster(("did:sorcha:w:owner1", RegisterRole.Owner));
        var operation = new GovernanceOperation
        {
            OperationType = GovernanceOperationType.Add,
            ProposerDid = "did:sorcha:w:owner1",
            TargetDid = "did:sorcha:w:newadmin",
            TargetRole = RegisterRole.Admin,
            ProposedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(7)
        };

        var result = _service.ValidateProposal(roster, operation);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void ValidateProposal_DuplicateTarget_ReturnsFailure()
    {
        var roster = CreateRoster(
            ("did:sorcha:w:owner1", RegisterRole.Owner),
            ("did:sorcha:w:admin1", RegisterRole.Admin));
        var operation = new GovernanceOperation
        {
            OperationType = GovernanceOperationType.Add,
            ProposerDid = "did:sorcha:w:owner1",
            TargetDid = "did:sorcha:w:admin1", // Already in roster
            TargetRole = RegisterRole.Admin,
            ProposedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(7)
        };

        var result = _service.ValidateProposal(roster, operation);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("already in the roster"));
    }

    [Fact]
    public void ValidateProposal_RosterCap_ReturnsFailure()
    {
        var members = new List<(string did, RegisterRole role)>
        {
            ("did:sorcha:w:owner1", RegisterRole.Owner)
        };
        for (int i = 0; i < 24; i++)
        {
            members.Add(($"did:sorcha:w:admin{i}", RegisterRole.Admin));
        }
        var roster = CreateRoster(members.ToArray());

        var operation = new GovernanceOperation
        {
            OperationType = GovernanceOperationType.Add,
            ProposerDid = "did:sorcha:w:owner1",
            TargetDid = "did:sorcha:w:newadmin25",
            TargetRole = RegisterRole.Admin,
            ProposedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(7)
        };

        var result = _service.ValidateProposal(roster, operation);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("maximum capacity"));
    }

    [Fact]
    public void ValidateProposal_NonAdminProposer_ReturnsFailure()
    {
        var roster = CreateRoster(
            ("did:sorcha:w:owner1", RegisterRole.Owner),
            ("did:sorcha:w:auditor1", RegisterRole.Auditor));
        var operation = new GovernanceOperation
        {
            OperationType = GovernanceOperationType.Add,
            ProposerDid = "did:sorcha:w:auditor1", // Auditor can't propose
            TargetDid = "did:sorcha:w:newadmin",
            TargetRole = RegisterRole.Admin,
            ProposedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(7)
        };

        var result = _service.ValidateProposal(roster, operation);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("cannot propose"));
    }

    [Fact]
    public void ValidateProposal_ExpiredProposal_ReturnsFailure()
    {
        var roster = CreateRoster(("did:sorcha:w:owner1", RegisterRole.Owner));
        var operation = new GovernanceOperation
        {
            OperationType = GovernanceOperationType.Add,
            ProposerDid = "did:sorcha:w:owner1",
            TargetDid = "did:sorcha:w:newadmin",
            TargetRole = RegisterRole.Admin,
            ProposedAt = DateTimeOffset.UtcNow.AddDays(-8),
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(-1) // Expired
        };

        var result = _service.ValidateProposal(roster, operation);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("expired"));
    }

    // --- Remove Proposal Validation ---

    [Fact]
    public void ValidateProposal_ValidRemove_ReturnsSuccess()
    {
        var roster = CreateRoster(
            ("did:sorcha:w:owner1", RegisterRole.Owner),
            ("did:sorcha:w:admin1", RegisterRole.Admin));
        var operation = new GovernanceOperation
        {
            OperationType = GovernanceOperationType.Remove,
            ProposerDid = "did:sorcha:w:owner1",
            TargetDid = "did:sorcha:w:admin1",
            ProposedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(7)
        };

        var result = _service.ValidateProposal(roster, operation);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void ValidateProposal_RemoveNonExistent_ReturnsFailure()
    {
        var roster = CreateRoster(("did:sorcha:w:owner1", RegisterRole.Owner));
        var operation = new GovernanceOperation
        {
            OperationType = GovernanceOperationType.Remove,
            ProposerDid = "did:sorcha:w:owner1",
            TargetDid = "did:sorcha:w:nonexistent",
            ProposedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(7)
        };

        var result = _service.ValidateProposal(roster, operation);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("not in the roster"));
    }

    [Fact]
    public void ValidateProposal_RemoveOwner_ReturnsFailure()
    {
        var roster = CreateRoster(
            ("did:sorcha:w:owner1", RegisterRole.Owner),
            ("did:sorcha:w:admin1", RegisterRole.Admin));
        var operation = new GovernanceOperation
        {
            OperationType = GovernanceOperationType.Remove,
            ProposerDid = "did:sorcha:w:admin1",
            TargetDid = "did:sorcha:w:owner1", // Can't remove Owner
            ProposedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(7)
        };

        var result = _service.ValidateProposal(roster, operation);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Transfer"));
    }

    // --- Transfer Proposal Validation ---

    [Fact]
    public void ValidateProposal_ValidTransfer_ReturnsSuccess()
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
    public void ValidateProposal_TransferByNonOwner_ReturnsFailure()
    {
        var roster = CreateRoster(
            ("did:sorcha:w:owner1", RegisterRole.Owner),
            ("did:sorcha:w:admin1", RegisterRole.Admin));
        var operation = new GovernanceOperation
        {
            OperationType = GovernanceOperationType.Transfer,
            ProposerDid = "did:sorcha:w:admin1", // Not owner
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
    public void ValidateProposal_TransferToNonAdmin_ReturnsFailure()
    {
        var roster = CreateRoster(
            ("did:sorcha:w:owner1", RegisterRole.Owner),
            ("did:sorcha:w:auditor1", RegisterRole.Auditor));
        var operation = new GovernanceOperation
        {
            OperationType = GovernanceOperationType.Transfer,
            ProposerDid = "did:sorcha:w:owner1",
            TargetDid = "did:sorcha:w:auditor1", // Auditor, not Admin
            TargetRole = RegisterRole.Owner,
            ProposedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(7)
        };

        var result = _service.ValidateProposal(roster, operation);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("existing Admin"));
    }

    // --- ApplyOperation ---

    [Fact]
    public void ApplyOperation_Add_IncreasesRoster()
    {
        var roster = CreateRoster(("did:sorcha:w:owner1", RegisterRole.Owner));
        var operation = new GovernanceOperation
        {
            OperationType = GovernanceOperationType.Add,
            TargetDid = "did:sorcha:w:admin1",
            TargetRole = RegisterRole.Admin
        };
        var newAttestation = new RegisterAttestation
        {
            Role = RegisterRole.Admin,
            Subject = "did:sorcha:w:admin1",
            PublicKey = Convert.ToBase64String(new byte[32]),
            Signature = Convert.ToBase64String(new byte[64]),
            Algorithm = SignatureAlgorithm.ED25519,
            GrantedAt = DateTimeOffset.UtcNow
        };

        var result = _service.ApplyOperation(roster.ControlRecord, operation, newAttestation);

        result.Attestations.Should().HaveCount(2);
        result.Attestations.Should().Contain(a => a.Subject == "did:sorcha:w:admin1");
    }

    [Fact]
    public void ApplyOperation_Remove_DecreasesRoster()
    {
        var roster = CreateRoster(
            ("did:sorcha:w:owner1", RegisterRole.Owner),
            ("did:sorcha:w:admin1", RegisterRole.Admin));
        var operation = new GovernanceOperation
        {
            OperationType = GovernanceOperationType.Remove,
            TargetDid = "did:sorcha:w:admin1"
        };

        var result = _service.ApplyOperation(roster.ControlRecord, operation);

        result.Attestations.Should().HaveCount(1);
        result.Attestations.Should().NotContain(a => a.Subject == "did:sorcha:w:admin1");
    }

    [Fact]
    public void ApplyOperation_Transfer_SwapsRoles()
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

        result.Attestations.Should().HaveCount(2);
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
            TargetDid = "did:sorcha:w:admin1",
            TargetRole = RegisterRole.Owner
        };

        var result = _service.ApplyOperation(roster.ControlRecord, operation);

        result.Attestations.Should().HaveCount(3); // Size unchanged
    }

    [Fact]
    public void ApplyOperation_AddWithoutAttestation_ThrowsArgumentException()
    {
        var roster = CreateRoster(("did:sorcha:w:owner1", RegisterRole.Owner));
        var operation = new GovernanceOperation
        {
            OperationType = GovernanceOperationType.Add,
            TargetDid = "did:sorcha:w:newadmin"
        };

        var act = () => _service.ApplyOperation(roster.ControlRecord, operation);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ValidateProposal_ProposerNotInRoster_ReturnsFailure()
    {
        var roster = CreateRoster(("did:sorcha:w:owner1", RegisterRole.Owner));
        var operation = new GovernanceOperation
        {
            OperationType = GovernanceOperationType.Add,
            ProposerDid = "did:sorcha:w:outsider",
            TargetDid = "did:sorcha:w:newadmin",
            ProposedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(7)
        };

        var result = _service.ValidateProposal(roster, operation);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("not in the roster"));
    }
}
