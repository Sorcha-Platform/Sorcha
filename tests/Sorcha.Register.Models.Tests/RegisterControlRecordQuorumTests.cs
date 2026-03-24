// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.Register.Models;

namespace Sorcha.Register.Models.Tests;

public class RegisterControlRecordQuorumTests
{
    private static RegisterAttestation CreateAttestation(RegisterRole role, string subject) => new()
    {
        Role = role,
        Subject = subject,
        PublicKey = Convert.ToBase64String(new byte[32]),
        Signature = Convert.ToBase64String(new byte[64]),
        Algorithm = SignatureAlgorithm.ED25519,
        GrantedAt = DateTimeOffset.UtcNow
    };

    private static RegisterControlRecord CreateRosterWithVoters(int adminCount)
    {
        var record = new RegisterControlRecord
        {
            RegisterId = "a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4",
            Name = "Test Register",
            CreatedAt = DateTimeOffset.UtcNow
        };

        // Always add Owner
        record.Attestations.Add(CreateAttestation(RegisterRole.Owner, "did:sorcha:w:owner"));

        // Add requested number of admins
        for (var i = 0; i < adminCount; i++)
        {
            record.Attestations.Add(CreateAttestation(RegisterRole.Admin, $"did:sorcha:w:admin{i}"));
        }

        return record;
    }

    // --- GetVotingMembers ---

    [Fact]
    public void GetVotingMembers_OwnerOnly_ReturnsSingleMember()
    {
        var record = CreateRosterWithVoters(0);

        record.GetVotingMembers().Should().HaveCount(1);
    }

    [Fact]
    public void GetVotingMembers_OwnerAndAdmins_ReturnsAll()
    {
        var record = CreateRosterWithVoters(3);

        record.GetVotingMembers().Should().HaveCount(4); // 1 owner + 3 admins
    }

    [Fact]
    public void GetVotingMembers_ExcludesAuditorsAndDesigners()
    {
        var record = CreateRosterWithVoters(1);
        record.Attestations.Add(CreateAttestation(RegisterRole.Auditor, "did:sorcha:w:auditor1"));
        record.Attestations.Add(CreateAttestation(RegisterRole.Designer, "did:sorcha:w:designer1"));

        record.GetVotingMembers().Should().HaveCount(2); // owner + 1 admin
    }

    // --- GetQuorumThreshold (no exclusion) ---

    [Theory]
    [InlineData(0, 1)]  // m=1 (owner only) → threshold=1
    [InlineData(1, 2)]  // m=2 (owner+1 admin) → threshold=2
    [InlineData(2, 2)]  // m=3 → threshold=2
    [InlineData(3, 3)]  // m=4 → threshold=3
    [InlineData(4, 3)]  // m=5 → threshold=3
    [InlineData(5, 4)]  // m=6 → threshold=4
    [InlineData(6, 4)]  // m=7 → threshold=4
    [InlineData(7, 5)]  // m=8 → threshold=5
    [InlineData(8, 5)]  // m=9 → threshold=5
    [InlineData(9, 6)]  // m=10 → threshold=6
    public void GetQuorumThreshold_VariousPoolSizes_ReturnsCorrectThreshold(int adminCount, int expectedThreshold)
    {
        var record = CreateRosterWithVoters(adminCount);

        record.GetQuorumThreshold().Should().Be(expectedThreshold);
    }

    // --- GetQuorumThreshold (with exclusion for removal) ---

    [Fact]
    public void GetQuorumThreshold_ExcludeTarget_ReducesPool()
    {
        var record = CreateRosterWithVoters(2); // m=3 → threshold=2
        // Exclude one admin → m=2 → threshold=2
        record.GetQuorumThreshold("did:sorcha:w:admin0").Should().Be(2);
    }

    [Fact]
    public void GetQuorumThreshold_ExcludeNonExistent_NoEffect()
    {
        var record = CreateRosterWithVoters(2); // m=3 → threshold=2
        record.GetQuorumThreshold("did:sorcha:w:nonexistent").Should().Be(2);
    }

    [Fact]
    public void GetQuorumThreshold_ExcludeOwner_ReducesPool()
    {
        var record = CreateRosterWithVoters(2); // m=3 → threshold=2
        // Exclude owner → m=2 → threshold=2
        record.GetQuorumThreshold("did:sorcha:w:owner").Should().Be(2);
    }

    // --- Quorum always requires strict majority ---

    [Fact]
    public void GetQuorumThreshold_TwoVoters_RequiresBoth()
    {
        var record = CreateRosterWithVoters(1); // m=2
        // floor(2/2)+1 = 2 — both must agree
        record.GetQuorumThreshold().Should().Be(2);
    }

    [Fact]
    public void GetQuorumThreshold_ThreeVoters_RequiresTwo()
    {
        var record = CreateRosterWithVoters(2); // m=3
        // floor(3/2)+1 = 2
        record.GetQuorumThreshold().Should().Be(2);
    }

    // --- Attestation cap ---

    [Fact]
    public void Attestations_MaxLength_Is25()
    {
        var record = new RegisterControlRecord();
        var prop = typeof(RegisterControlRecord).GetProperty(nameof(RegisterControlRecord.Attestations));
        var maxLengthAttr = prop!.GetCustomAttributes(typeof(System.ComponentModel.DataAnnotations.MaxLengthAttribute), false)
            .Cast<System.ComponentModel.DataAnnotations.MaxLengthAttribute>()
            .First();

        maxLengthAttr.Length.Should().Be(25);
    }
}
