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

public class QuorumCollectionTests
{
    private readonly Mock<IRegisterRepository> _repositoryMock;
    private readonly GovernanceRosterService _service;

    public QuorumCollectionTests()
    {
        _repositoryMock = new Mock<IRegisterRepository>();
        var logger = new Mock<ILogger<GovernanceRosterService>>();
        _service = new GovernanceRosterService(_repositoryMock.Object, logger.Object);
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

    // --- Owner Bypass ---

    [Fact]
    public void ValidateProposal_OwnerAddsAdmin_BypassesQuorum()
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

    // --- Quorum Calculation for m=1 ---

    [Fact]
    public void GetQuorumThreshold_SingleOwner_RequiresOne()
    {
        var record = new RegisterControlRecord
        {
            RegisterId = "test",
            Name = "Test",
            CreatedAt = DateTimeOffset.UtcNow,
            Attestations =
            [
                new RegisterAttestation
                {
                    Role = RegisterRole.Owner,
                    Subject = "did:sorcha:w:owner1",
                    PublicKey = "key",
                    Signature = "sig",
                    Algorithm = SignatureAlgorithm.ED25519,
                    GrantedAt = DateTimeOffset.UtcNow
                }
            ]
        };

        var threshold = record.GetQuorumThreshold();

        threshold.Should().Be(1); // floor(1/2) + 1 = 1
    }

    // --- Quorum Calculation for m=2 ---

    [Fact]
    public void GetQuorumThreshold_TwoVotingMembers_RequiresTwo()
    {
        var record = new RegisterControlRecord
        {
            RegisterId = "test",
            Name = "Test",
            CreatedAt = DateTimeOffset.UtcNow,
            Attestations =
            [
                new RegisterAttestation
                {
                    Role = RegisterRole.Owner,
                    Subject = "did:sorcha:w:owner1",
                    PublicKey = "key",
                    Signature = "sig",
                    Algorithm = SignatureAlgorithm.ED25519,
                    GrantedAt = DateTimeOffset.UtcNow
                },
                new RegisterAttestation
                {
                    Role = RegisterRole.Admin,
                    Subject = "did:sorcha:w:admin1",
                    PublicKey = "key2",
                    Signature = "sig2",
                    Algorithm = SignatureAlgorithm.ED25519,
                    GrantedAt = DateTimeOffset.UtcNow
                }
            ]
        };

        var threshold = record.GetQuorumThreshold();

        threshold.Should().Be(2); // floor(2/2) + 1 = 2
    }

    // --- Quorum Calculation for m=3 ---

    [Fact]
    public void GetQuorumThreshold_ThreeVotingMembers_RequiresTwo()
    {
        var record = new RegisterControlRecord
        {
            RegisterId = "test",
            Name = "Test",
            CreatedAt = DateTimeOffset.UtcNow,
            Attestations =
            [
                new RegisterAttestation
                {
                    Role = RegisterRole.Owner,
                    Subject = "did:sorcha:w:owner1",
                    PublicKey = "key",
                    Signature = "sig",
                    Algorithm = SignatureAlgorithm.ED25519,
                    GrantedAt = DateTimeOffset.UtcNow
                },
                new RegisterAttestation
                {
                    Role = RegisterRole.Admin,
                    Subject = "did:sorcha:w:admin1",
                    PublicKey = "key2",
                    Signature = "sig2",
                    Algorithm = SignatureAlgorithm.ED25519,
                    GrantedAt = DateTimeOffset.UtcNow
                },
                new RegisterAttestation
                {
                    Role = RegisterRole.Admin,
                    Subject = "did:sorcha:w:admin2",
                    PublicKey = "key3",
                    Signature = "sig3",
                    Algorithm = SignatureAlgorithm.ED25519,
                    GrantedAt = DateTimeOffset.UtcNow
                }
            ]
        };

        var threshold = record.GetQuorumThreshold();

        threshold.Should().Be(2); // floor(3/2) + 1 = 2
    }

    // --- Quorum with Removal Exclusion ---

    [Fact]
    public void GetQuorumThreshold_ExcludesTarget_ReducesPool()
    {
        var record = new RegisterControlRecord
        {
            RegisterId = "test",
            Name = "Test",
            CreatedAt = DateTimeOffset.UtcNow,
            Attestations =
            [
                new RegisterAttestation
                {
                    Role = RegisterRole.Owner,
                    Subject = "did:sorcha:w:owner1",
                    PublicKey = "key",
                    Signature = "sig",
                    Algorithm = SignatureAlgorithm.ED25519,
                    GrantedAt = DateTimeOffset.UtcNow
                },
                new RegisterAttestation
                {
                    Role = RegisterRole.Admin,
                    Subject = "did:sorcha:w:admin1",
                    PublicKey = "key2",
                    Signature = "sig2",
                    Algorithm = SignatureAlgorithm.ED25519,
                    GrantedAt = DateTimeOffset.UtcNow
                },
                new RegisterAttestation
                {
                    Role = RegisterRole.Admin,
                    Subject = "did:sorcha:w:admin2",
                    PublicKey = "key3",
                    Signature = "sig3",
                    Algorithm = SignatureAlgorithm.ED25519,
                    GrantedAt = DateTimeOffset.UtcNow
                }
            ]
        };

        // Without exclusion: threshold = 2 (floor(3/2)+1)
        // With exclusion: threshold = 2 (floor(2/2)+1)
        var threshold = record.GetQuorumThreshold("did:sorcha:w:admin2");

        threshold.Should().Be(2); // floor(2/2) + 1 = 2
    }

    // --- Non-voting Members Don't Count ---

    [Fact]
    public void GetVotingMembers_ExcludesAuditors()
    {
        var record = new RegisterControlRecord
        {
            RegisterId = "test",
            Name = "Test",
            CreatedAt = DateTimeOffset.UtcNow,
            Attestations =
            [
                new RegisterAttestation
                {
                    Role = RegisterRole.Owner,
                    Subject = "did:sorcha:w:owner1",
                    PublicKey = "key",
                    Signature = "sig",
                    Algorithm = SignatureAlgorithm.ED25519,
                    GrantedAt = DateTimeOffset.UtcNow
                },
                new RegisterAttestation
                {
                    Role = RegisterRole.Auditor,
                    Subject = "did:sorcha:w:auditor1",
                    PublicKey = "key2",
                    Signature = "sig2",
                    Algorithm = SignatureAlgorithm.ED25519,
                    GrantedAt = DateTimeOffset.UtcNow
                }
            ]
        };

        var voters = record.GetVotingMembers().ToList();

        voters.Should().HaveCount(1);
        voters[0].Subject.Should().Be("did:sorcha:w:owner1");
    }

    // --- Quorum for Large Rosters ---

    [Theory]
    [InlineData(4, 3)]  // floor(4/2)+1 = 3
    [InlineData(5, 3)]  // floor(5/2)+1 = 3
    [InlineData(6, 4)]  // floor(6/2)+1 = 4
    [InlineData(7, 4)]  // floor(7/2)+1 = 4
    [InlineData(10, 6)] // floor(10/2)+1 = 6
    public void GetQuorumThreshold_VariousPoolSizes_CorrectThreshold(int memberCount, int expectedThreshold)
    {
        var attestations = new List<RegisterAttestation>
        {
            new()
            {
                Role = RegisterRole.Owner,
                Subject = "did:sorcha:w:owner1",
                PublicKey = "key",
                Signature = "sig",
                Algorithm = SignatureAlgorithm.ED25519,
                GrantedAt = DateTimeOffset.UtcNow
            }
        };

        for (int i = 1; i < memberCount; i++)
        {
            attestations.Add(new RegisterAttestation
            {
                Role = RegisterRole.Admin,
                Subject = $"did:sorcha:w:admin{i}",
                PublicKey = $"key{i}",
                Signature = $"sig{i}",
                Algorithm = SignatureAlgorithm.ED25519,
                GrantedAt = DateTimeOffset.UtcNow
            });
        }

        var record = new RegisterControlRecord
        {
            RegisterId = "test",
            Name = "Test",
            CreatedAt = DateTimeOffset.UtcNow,
            Attestations = attestations
        };

        var threshold = record.GetQuorumThreshold();

        threshold.Should().Be(expectedThreshold);
    }
}
