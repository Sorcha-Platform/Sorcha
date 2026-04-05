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

public class RemovalValidationTests
{
    private readonly GovernanceRosterService _service;

    public RemovalValidationTests()
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

    [Fact]
    public void ValidateProposal_RemoveExistingAdmin_Valid()
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
    public void ValidateProposal_RemoveOwnerViaRemove_MustUseTransfer()
    {
        var roster = CreateRoster(
            ("did:sorcha:w:owner1", RegisterRole.Owner),
            ("did:sorcha:w:admin1", RegisterRole.Admin));
        var operation = new GovernanceOperation
        {
            OperationType = GovernanceOperationType.Remove,
            ProposerDid = "did:sorcha:w:admin1",
            TargetDid = "did:sorcha:w:owner1",
            ProposedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(7)
        };

        var result = _service.ValidateProposal(roster, operation);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Transfer"));
    }

    [Fact]
    public void ValidateProposal_RemoveNonMember_Rejected()
    {
        var roster = CreateRoster(("did:sorcha:w:owner1", RegisterRole.Owner));
        var operation = new GovernanceOperation
        {
            OperationType = GovernanceOperationType.Remove,
            ProposerDid = "did:sorcha:w:owner1",
            TargetDid = "did:sorcha:w:notamember",
            ProposedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(7)
        };

        var result = _service.ValidateProposal(roster, operation);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("not in the roster"));
    }

    [Fact]
    public void ApplyOperation_Remove_LeavesValidRoster()
    {
        var roster = CreateRoster(
            ("did:sorcha:w:owner1", RegisterRole.Owner),
            ("did:sorcha:w:admin1", RegisterRole.Admin),
            ("did:sorcha:w:admin2", RegisterRole.Admin));
        var operation = new GovernanceOperation
        {
            OperationType = GovernanceOperationType.Remove,
            TargetDid = "did:sorcha:w:admin1"
        };

        var result = _service.ApplyOperation(roster.ControlRecord, operation);

        // Roster still has an Owner
        result.Attestations.Should().ContainSingle(a => a.Role == RegisterRole.Owner);
        // Roster metadata preserved
        result.RegisterId.Should().Be(roster.ControlRecord.RegisterId);
    }

    [Fact]
    public void ValidateProposal_AuditorCannotProposeRemoval_Rejected()
    {
        var roster = CreateRoster(
            ("did:sorcha:w:owner1", RegisterRole.Owner),
            ("did:sorcha:w:admin1", RegisterRole.Admin),
            ("did:sorcha:w:auditor1", RegisterRole.Auditor));
        var operation = new GovernanceOperation
        {
            OperationType = GovernanceOperationType.Remove,
            ProposerDid = "did:sorcha:w:auditor1",
            TargetDid = "did:sorcha:w:admin1",
            ProposedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(7)
        };

        var result = _service.ValidateProposal(roster, operation);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("cannot propose"));
    }
}
