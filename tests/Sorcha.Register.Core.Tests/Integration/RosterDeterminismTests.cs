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

namespace Sorcha.Register.Core.Tests.Integration;

/// <summary>
/// Roster determinism tests: replaying the same Control transaction chain
/// must always produce the identical roster snapshot.
/// </summary>
public class RosterDeterminismTests
{
    private const string RegisterId = "test-register";

    private static RegisterControlRecord MakeRoster(params (string did, RegisterRole role)[] members)
    {
        return new RegisterControlRecord
        {
            RegisterId = RegisterId,
            Name = "Test",
            CreatedAt = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero),
            Attestations = members.Select(m => new RegisterAttestation
            {
                Role = m.role,
                Subject = m.did,
                PublicKey = Convert.ToBase64String(new byte[32]),
                Signature = Convert.ToBase64String(new byte[64]),
                Algorithm = SignatureAlgorithm.ED25519,
                GrantedAt = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero)
            }).ToList()
        };
    }

    private static GovernanceRosterService CreateService(List<RegisterControlRecord> rosterChain)
    {
        var mockRepository = new Mock<IRegisterRepository>();
        var transactions = rosterChain.Select((roster, i) =>
        {
            var payload = new ControlTransactionPayload
            {
                Version = 1,
                Roster = roster
            };
            var payloadBytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(payload);

            return new TransactionModel
            {
                TxId = $"tx{i:D4}",
                RegisterId = RegisterId,
                MetaData = new TransactionMetaData
                {
                    RegisterId = RegisterId,
                    TransactionType = TransactionType.Control,
                    BlueprintId = "register-governance-v1"
                },
                Payloads = new[]
                {
                    new PayloadModel { Data = Convert.ToBase64String(payloadBytes) }
                }
            };
        }).ToList();

        mockRepository
            .Setup(r => r.GetTransactionsAsync(RegisterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(transactions.AsQueryable());

        var logger = new Mock<ILogger<GovernanceRosterService>>();
        return new GovernanceRosterService(mockRepository.Object, logger.Object);
    }

    [Fact]
    public async Task ReplayControlChain_TwoReconstructions_ProduceIdenticalRosters()
    {
        var rosterChain = new List<RegisterControlRecord>
        {
            MakeRoster(("did:sorcha:w:owner1", RegisterRole.Owner)),
            MakeRoster(
                ("did:sorcha:w:owner1", RegisterRole.Owner),
                ("did:sorcha:w:admin1", RegisterRole.Admin)),
            MakeRoster(
                ("did:sorcha:w:owner1", RegisterRole.Owner),
                ("did:sorcha:w:admin1", RegisterRole.Admin),
                ("did:sorcha:w:admin2", RegisterRole.Admin))
        };

        var service1 = CreateService(rosterChain);
        var service2 = CreateService(rosterChain);

        var roster1 = await service1.GetCurrentRosterAsync(RegisterId);
        var roster2 = await service2.GetCurrentRosterAsync(RegisterId);

        roster1.Should().NotBeNull();
        roster2.Should().NotBeNull();

        roster1!.ControlTransactionCount.Should().Be(roster2!.ControlTransactionCount);
        roster1.ControlRecord.Attestations.Should().HaveCount(roster2.ControlRecord.Attestations.Count);

        for (var i = 0; i < roster1.ControlRecord.Attestations.Count; i++)
        {
            var a1 = roster1.ControlRecord.Attestations[i];
            var a2 = roster2.ControlRecord.Attestations[i];
            a1.Subject.Should().Be(a2.Subject);
            a1.Role.Should().Be(a2.Role);
            a1.PublicKey.Should().Be(a2.PublicKey);
        }
    }

    [Fact]
    public async Task ReplayControlChain_OrderMatters_LastTxWins()
    {
        // If chain has conflicting snapshots, last one wins
        var rosterChain = new List<RegisterControlRecord>
        {
            MakeRoster(
                ("did:sorcha:w:owner1", RegisterRole.Owner),
                ("did:sorcha:w:admin1", RegisterRole.Admin),
                ("did:sorcha:w:admin2", RegisterRole.Admin)),
            MakeRoster(
                ("did:sorcha:w:owner1", RegisterRole.Owner),
                ("did:sorcha:w:admin1", RegisterRole.Admin))  // admin2 was removed
        };

        var service = CreateService(rosterChain);
        var roster = await service.GetCurrentRosterAsync(RegisterId);

        roster.Should().NotBeNull();
        roster!.ControlRecord.Attestations.Should().HaveCount(2, "latest snapshot should be used");
        roster.ControlRecord.Attestations.Should().NotContain(a => a.Subject == "did:sorcha:w:admin2");
    }

    [Fact]
    public async Task ReplayControlChain_EmptyChain_ReturnsNull()
    {
        var mockRepository = new Mock<IRegisterRepository>();
        mockRepository
            .Setup(r => r.GetTransactionsAsync(RegisterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<TransactionModel>().AsQueryable());

        var logger = new Mock<ILogger<GovernanceRosterService>>();
        var service = new GovernanceRosterService(mockRepository.Object, logger.Object);

        var roster = await service.GetCurrentRosterAsync(RegisterId);
        roster.Should().BeNull();
    }

    [Fact]
    public async Task ReplayControlChain_SingleGenesis_ReturnsOwnerOnly()
    {
        var rosterChain = new List<RegisterControlRecord>
        {
            MakeRoster(("did:sorcha:w:owner1", RegisterRole.Owner))
        };

        var service = CreateService(rosterChain);
        var roster = await service.GetCurrentRosterAsync(RegisterId);

        roster.Should().NotBeNull();
        roster!.ControlTransactionCount.Should().Be(1);
        roster.ControlRecord.Attestations.Should().HaveCount(1);
        roster.ControlRecord.Attestations[0].Role.Should().Be(RegisterRole.Owner);
    }

    [Fact]
    public async Task ReplayControlChain_LargeRoster_DeterminsticReconstruction()
    {
        // Build a chain that grows to 10 members
        var rosters = new List<RegisterControlRecord>();
        var currentMembers = new List<(string did, RegisterRole role)>
        {
            ("did:sorcha:w:owner1", RegisterRole.Owner)
        };

        rosters.Add(MakeRoster(currentMembers.ToArray()));

        for (var i = 1; i <= 9; i++)
        {
            currentMembers.Add(($"did:sorcha:w:admin{i}", RegisterRole.Admin));
            rosters.Add(MakeRoster(currentMembers.ToArray()));
        }

        var service = CreateService(rosters);
        var roster = await service.GetCurrentRosterAsync(RegisterId);

        roster.Should().NotBeNull();
        roster!.ControlTransactionCount.Should().Be(10);
        roster.ControlRecord.Attestations.Should().HaveCount(10);
        roster.ControlRecord.Attestations.Count(a => a.Role == RegisterRole.Owner).Should().Be(1);
        roster.ControlRecord.Attestations.Count(a => a.Role == RegisterRole.Admin).Should().Be(9);
    }

    [Fact]
    public void ApplyOperationChain_Deterministic_SameInputSameOutput()
    {
        var logger = new Mock<ILogger<GovernanceRosterService>>();
        var mockRepository = new Mock<IRegisterRepository>();
        var service = new GovernanceRosterService(mockRepository.Object, logger.Object);

        var genesis = MakeRoster(("did:sorcha:w:owner1", RegisterRole.Owner));

        var addOp = new GovernanceOperation
        {
            OperationType = GovernanceOperationType.Add,
            ProposerDid = "did:sorcha:w:owner1",
            TargetDid = "did:sorcha:w:admin1",
            TargetRole = RegisterRole.Admin,
            ProposedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(7)
        };

        var attestation = new RegisterAttestation
        {
            Role = RegisterRole.Admin,
            Subject = "did:sorcha:w:admin1",
            PublicKey = Convert.ToBase64String(new byte[32]),
            Signature = Convert.ToBase64String(new byte[64]),
            Algorithm = SignatureAlgorithm.ED25519,
            GrantedAt = DateTimeOffset.UtcNow
        };

        // Apply same operation twice on same input
        var result1 = service.ApplyOperation(genesis, addOp, attestation);
        var result2 = service.ApplyOperation(genesis, addOp, attestation);

        result1.Attestations.Should().HaveCount(result2.Attestations.Count);
        for (var i = 0; i < result1.Attestations.Count; i++)
        {
            result1.Attestations[i].Subject.Should().Be(result2.Attestations[i].Subject);
            result1.Attestations[i].Role.Should().Be(result2.Attestations[i].Role);
        }
    }
}
