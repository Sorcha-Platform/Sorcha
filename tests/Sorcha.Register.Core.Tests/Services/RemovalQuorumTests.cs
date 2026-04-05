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

public class RemovalQuorumTests
{
    private readonly GovernanceRosterService _service;

    public RemovalQuorumTests()
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

    // --- Target Excluded from Quorum Pool ---

    [Fact]
    public void GetQuorumThreshold_RemovalExcludesTarget_AdjustedPool()
    {
        // 3 voting members: Owner + Admin1 + Admin2
        // Removing Admin2 → pool becomes 2 → threshold = 2
        var record = CreateRoster(
            ("did:sorcha:w:owner1", RegisterRole.Owner),
            ("did:sorcha:w:admin1", RegisterRole.Admin),
            ("did:sorcha:w:admin2", RegisterRole.Admin)).ControlRecord;

        var threshold = record.GetQuorumThreshold("did:sorcha:w:admin2");

        threshold.Should().Be(2); // floor(2/2)+1 = 2
    }

    [Fact]
    public void GetQuorumThreshold_RemovalFromFourMembers_AdjustedPool()
    {
        // 4 voting members: remove 1 → pool 3 → threshold 2
        var record = CreateRoster(
            ("did:sorcha:w:owner1", RegisterRole.Owner),
            ("did:sorcha:w:admin1", RegisterRole.Admin),
            ("did:sorcha:w:admin2", RegisterRole.Admin),
            ("did:sorcha:w:admin3", RegisterRole.Admin)).ControlRecord;

        var threshold = record.GetQuorumThreshold("did:sorcha:w:admin3");

        threshold.Should().Be(2); // floor(3/2)+1 = 2
    }

    // --- Owner Bypass for Removal ---

    [Fact]
    public void ValidateProposal_OwnerRemovesAdmin_Valid()
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

    // --- Cannot Remove Owner ---

    [Fact]
    public void ValidateProposal_RemoveOwner_Rejected()
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

    // --- Apply Remove Operation ---

    [Fact]
    public void ApplyOperation_RemoveAdmin_DecreasesRoster()
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

        result.Attestations.Should().HaveCount(2);
        result.Attestations.Should().NotContain(a => a.Subject == "did:sorcha:w:admin1");
        result.Attestations.Should().Contain(a => a.Subject == "did:sorcha:w:owner1");
        result.Attestations.Should().Contain(a => a.Subject == "did:sorcha:w:admin2");
    }

    // --- Remove Last Admin Leaves Only Owner ---

    [Fact]
    public void ApplyOperation_RemoveLastAdmin_OnlyOwnerRemains()
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
        result.Attestations[0].Role.Should().Be(RegisterRole.Owner);
    }

    // --- Remove Non-existent Target ---

    [Fact]
    public void ValidateProposal_RemoveNonExistent_Rejected()
    {
        var roster = CreateRoster(("did:sorcha:w:owner1", RegisterRole.Owner));
        var operation = new GovernanceOperation
        {
            OperationType = GovernanceOperationType.Remove,
            ProposerDid = "did:sorcha:w:owner1",
            TargetDid = "did:sorcha:w:nobody",
            ProposedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(7)
        };

        var result = _service.ValidateProposal(roster, operation);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("not in the roster"));
    }
}
