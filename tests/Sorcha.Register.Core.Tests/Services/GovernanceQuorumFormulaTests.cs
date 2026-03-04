// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.Register.Models;
using Xunit;

namespace Sorcha.Register.Core.Tests.Services;

/// <summary>
/// Unit tests for parameterized quorum calculation via RegisterControlRecord.CalculateQuorum.
/// Covers all three formulas (StrictMajority, Supermajority, Unanimous) and edge cases.
/// </summary>
public class GovernanceQuorumFormulaTests
{
    #region StrictMajority Tests

    [Theory]
    [InlineData(1, 1)]  // m=1 → (1/2)+1 = 1
    [InlineData(2, 2)]  // m=2 → (2/2)+1 = 2
    [InlineData(3, 2)]  // m=3 → (3/2)+1 = 2
    [InlineData(4, 3)]  // m=4 → (4/2)+1 = 3
    [InlineData(5, 3)]  // m=5 → (5/2)+1 = 3
    [InlineData(10, 6)] // m=10 → (10/2)+1 = 6
    [InlineData(25, 13)]// m=25 → (25/2)+1 = 13
    public void CalculateQuorum_StrictMajority_ReturnsCorrectThreshold(int poolSize, int expected)
    {
        // Act
        var result = RegisterControlRecord.CalculateQuorum(poolSize, QuorumFormula.StrictMajority);

        // Assert
        result.Should().Be(expected);
    }

    #endregion

    #region Supermajority Tests

    [Theory]
    [InlineData(1, 1)]  // m=1 → (2*1/3)+1 = 1
    [InlineData(2, 2)]  // m=2 → (4/3)+1 = 2
    [InlineData(3, 3)]  // m=3 → (6/3)+1 = 3
    [InlineData(4, 3)]  // m=4 → (8/3)+1 = 3
    [InlineData(5, 4)]  // m=5 → (10/3)+1 = 4
    [InlineData(6, 5)]  // m=6 → (12/3)+1 = 5
    [InlineData(9, 7)]  // m=9 → (18/3)+1 = 7
    [InlineData(10, 7)] // m=10 → (20/3)+1 = 7
    [InlineData(25, 17)]// m=25 → (50/3)+1 = 17
    public void CalculateQuorum_Supermajority_ReturnsCorrectThreshold(int poolSize, int expected)
    {
        // Act
        var result = RegisterControlRecord.CalculateQuorum(poolSize, QuorumFormula.Supermajority);

        // Assert
        result.Should().Be(expected);
    }

    #endregion

    #region Unanimous Tests

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 3)]
    [InlineData(5, 5)]
    [InlineData(10, 10)]
    [InlineData(25, 25)]
    public void CalculateQuorum_Unanimous_ReturnsPoolSize(int poolSize, int expected)
    {
        // Act
        var result = RegisterControlRecord.CalculateQuorum(poolSize, QuorumFormula.Unanimous);

        // Assert
        result.Should().Be(expected);
    }

    #endregion

    #region Edge Cases

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public void CalculateQuorum_ZeroOrNegativePool_ReturnsMinimumOne(int poolSize)
    {
        // All formulas should return 1 for non-positive pool sizes
        RegisterControlRecord.CalculateQuorum(poolSize, QuorumFormula.StrictMajority).Should().Be(1);
        RegisterControlRecord.CalculateQuorum(poolSize, QuorumFormula.Supermajority).Should().Be(1);
        RegisterControlRecord.CalculateQuorum(poolSize, QuorumFormula.Unanimous).Should().Be(1);
    }

    [Fact]
    public void CalculateQuorum_UnknownFormula_DefaultsToStrictMajority()
    {
        // Arrange - cast an invalid enum value
        var unknownFormula = (QuorumFormula)999;

        // Act
        var result = RegisterControlRecord.CalculateQuorum(5, unknownFormula);

        // Assert — should use default branch which matches strict majority
        result.Should().Be(3); // same as StrictMajority for m=5
    }

    #endregion

    #region GetQuorumThreshold Integration Tests

    [Fact]
    public void GetQuorumThreshold_WithPolicyFormula_UsesCorrectFormula()
    {
        // Arrange
        var record = CreateControlRecordWithMembers(4);
        record.RegisterPolicy = new RegisterPolicy
        {
            Version = 1,
            Governance = new PolicyGovernanceConfig
            {
                QuorumFormula = QuorumFormula.Supermajority
            }
        };

        // Act — use the formula from policy
        var formula = record.RegisterPolicy.Governance.QuorumFormula;
        var threshold = record.GetQuorumThreshold(formula: formula);

        // Assert — Supermajority of 4: (8/3)+1 = 3
        threshold.Should().Be(3);
    }

    [Fact]
    public void GetQuorumThreshold_WithExcludeDid_ReducesPool()
    {
        // Arrange
        var record = CreateControlRecordWithMembers(4);
        var excludeDid = record.Attestations[1].Subject; // exclude one admin

        // Act
        var threshold = record.GetQuorumThreshold(excludeDid);

        // Assert — pool of 3 (4-1), strict majority: (3/2)+1 = 2
        threshold.Should().Be(2);
    }

    [Fact]
    public void GetQuorumThreshold_DefaultFormula_IsStrictMajority()
    {
        // Arrange
        var record = CreateControlRecordWithMembers(5);

        // Act
        var threshold = record.GetQuorumThreshold();

        // Assert — StrictMajority of 5: (5/2)+1 = 3
        threshold.Should().Be(3);
    }

    [Fact]
    public void GetQuorumThreshold_NullPolicy_DefaultsToStrictMajority()
    {
        // Arrange
        var record = CreateControlRecordWithMembers(3);
        record.RegisterPolicy = null;

        // Act
        var threshold = record.GetQuorumThreshold();

        // Assert — StrictMajority of 3: (3/2)+1 = 2
        threshold.Should().Be(2);
    }

    #endregion

    #region Helpers

    private static RegisterControlRecord CreateControlRecordWithMembers(int memberCount)
    {
        var attestations = new List<RegisterAttestation>();

        // First member is always Owner
        attestations.Add(new RegisterAttestation
        {
            Role = RegisterRole.Owner,
            Subject = "did:sorcha:owner",
            PublicKey = "owner-pubkey",
            Signature = "owner-sig",
            Algorithm = SignatureAlgorithm.ED25519,
            GrantedAt = DateTimeOffset.UtcNow
        });

        // Rest are Admins
        for (int i = 1; i < memberCount; i++)
        {
            attestations.Add(new RegisterAttestation
            {
                Role = RegisterRole.Admin,
                Subject = $"did:sorcha:admin-{i:D3}",
                PublicKey = $"admin-{i:D3}-pubkey",
                Signature = $"admin-{i:D3}-sig",
                Algorithm = SignatureAlgorithm.ED25519,
                GrantedAt = DateTimeOffset.UtcNow
            });
        }

        return new RegisterControlRecord
        {
            RegisterId = "a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4",
            Name = "Test Register",
            TenantId = "tenant-001",
            CreatedAt = DateTimeOffset.UtcNow,
            Attestations = attestations
        };
    }

    #endregion
}
