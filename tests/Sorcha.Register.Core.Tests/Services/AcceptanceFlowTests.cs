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

public class AcceptanceFlowTests
{
    private readonly GovernanceRosterService _service;

    public AcceptanceFlowTests()
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

    // --- Target Accepts Add ---

    [Fact]
    public void ApplyOperation_TargetAcceptsAdd_RosterUpdated()
    {
        var roster = CreateRoster(("did:sorcha:w:owner1", RegisterRole.Owner));
        var operation = new GovernanceOperation
        {
            OperationType = GovernanceOperationType.Add,
            TargetDid = "did:sorcha:w:newadmin",
            TargetRole = RegisterRole.Admin
        };
        var attestation = new RegisterAttestation
        {
            Role = RegisterRole.Admin,
            Subject = "did:sorcha:w:newadmin",
            PublicKey = Convert.ToBase64String(new byte[32]),
            Signature = Convert.ToBase64String(new byte[64]),
            Algorithm = SignatureAlgorithm.ED25519,
            GrantedAt = DateTimeOffset.UtcNow
        };

        var result = _service.ApplyOperation(roster.ControlRecord, operation, attestation);

        result.Attestations.Should().HaveCount(2);
        result.Attestations.Should().Contain(a => a.Subject == "did:sorcha:w:newadmin" && a.Role == RegisterRole.Admin);
    }

    // --- Target Accepts Transfer ---

    [Fact]
    public void ApplyOperation_TargetAcceptsTransfer_OwnershipSwapped()
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

    // --- 7-day Expiry ---

    [Fact]
    public void ValidateProposal_ExpiredProposal_Rejected()
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

    [Fact]
    public void ValidateProposal_NotYetExpired_Accepted()
    {
        var roster = CreateRoster(("did:sorcha:w:owner1", RegisterRole.Owner));
        var operation = new GovernanceOperation
        {
            OperationType = GovernanceOperationType.Add,
            ProposerDid = "did:sorcha:w:owner1",
            TargetDid = "did:sorcha:w:newadmin",
            TargetRole = RegisterRole.Admin,
            ProposedAt = DateTimeOffset.UtcNow.AddDays(-6),
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(1) // Still active
        };

        var result = _service.ValidateProposal(roster, operation);

        result.IsValid.Should().BeTrue();
    }

    // --- Preserved Roster Metadata After Apply ---

    [Fact]
    public void ApplyOperation_Add_PreservesRosterMetadata()
    {
        var roster = CreateRoster(("did:sorcha:w:owner1", RegisterRole.Owner));
        var operation = new GovernanceOperation
        {
            OperationType = GovernanceOperationType.Add,
            TargetDid = "did:sorcha:w:newadmin",
            TargetRole = RegisterRole.Admin
        };
        var attestation = new RegisterAttestation
        {
            Role = RegisterRole.Admin,
            Subject = "did:sorcha:w:newadmin",
            PublicKey = Convert.ToBase64String(new byte[32]),
            Signature = Convert.ToBase64String(new byte[64]),
            Algorithm = SignatureAlgorithm.ED25519,
            GrantedAt = DateTimeOffset.UtcNow
        };

        var result = _service.ApplyOperation(roster.ControlRecord, operation, attestation);

        result.RegisterId.Should().Be(roster.ControlRecord.RegisterId);
        result.Name.Should().Be(roster.ControlRecord.Name);
        result.CreatedAt.Should().Be(roster.ControlRecord.CreatedAt);
    }

    // --- Sequential Add Operations ---

    [Fact]
    public void ApplyOperation_SequentialAdds_RosterGrows()
    {
        var roster = CreateRoster(("did:sorcha:w:owner1", RegisterRole.Owner));

        // First add
        var result = _service.ApplyOperation(roster.ControlRecord, new GovernanceOperation
        {
            OperationType = GovernanceOperationType.Add,
            TargetDid = "did:sorcha:w:admin1",
            TargetRole = RegisterRole.Admin
        }, new RegisterAttestation
        {
            Role = RegisterRole.Admin,
            Subject = "did:sorcha:w:admin1",
            PublicKey = "key1",
            Signature = "sig1",
            Algorithm = SignatureAlgorithm.ED25519,
            GrantedAt = DateTimeOffset.UtcNow
        });

        // Second add using updated roster
        result = _service.ApplyOperation(result, new GovernanceOperation
        {
            OperationType = GovernanceOperationType.Add,
            TargetDid = "did:sorcha:w:admin2",
            TargetRole = RegisterRole.Admin
        }, new RegisterAttestation
        {
            Role = RegisterRole.Admin,
            Subject = "did:sorcha:w:admin2",
            PublicKey = "key2",
            Signature = "sig2",
            Algorithm = SignatureAlgorithm.ED25519,
            GrantedAt = DateTimeOffset.UtcNow
        });

        result.Attestations.Should().HaveCount(3);
    }
}
