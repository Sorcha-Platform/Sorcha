// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using FluentAssertions;
using Sorcha.Register.Models;

namespace Sorcha.Register.Models.Tests;

public class RegisterPolicyTests
{
    // --- CreateDefault: Governance ---

    [Fact]
    public void CreateDefault_Version_Is1()
    {
        var policy = RegisterPolicy.CreateDefault();

        policy.Version.Should().Be(1);
    }

    [Fact]
    public void CreateDefault_Governance_QuorumFormula_IsStrictMajority()
    {
        var policy = RegisterPolicy.CreateDefault();

        policy.Governance.QuorumFormula.Should().Be(QuorumFormula.StrictMajority);
    }

    [Fact]
    public void CreateDefault_Governance_ProposalTtlDays_Is7()
    {
        var policy = RegisterPolicy.CreateDefault();

        policy.Governance.ProposalTtlDays.Should().Be(7);
    }

    [Fact]
    public void CreateDefault_Governance_OwnerCanBypassQuorum_IsTrue()
    {
        var policy = RegisterPolicy.CreateDefault();

        policy.Governance.OwnerCanBypassQuorum.Should().BeTrue();
    }

    [Fact]
    public void CreateDefault_Governance_BlueprintVersion_IsRegisterGovernanceV1()
    {
        var policy = RegisterPolicy.CreateDefault();

        policy.Governance.BlueprintVersion.Should().Be("register-governance-v1");
    }

    // --- CreateDefault: Validators ---

    [Fact]
    public void CreateDefault_Validators_RegistrationMode_IsPublic()
    {
        var policy = RegisterPolicy.CreateDefault();

        policy.Validators.RegistrationMode.Should().Be(RegistrationMode.Public);
    }

    [Fact]
    public void CreateDefault_Validators_ApprovedValidators_IsEmpty()
    {
        var policy = RegisterPolicy.CreateDefault();

        policy.Validators.ApprovedValidators.Should().BeEmpty();
    }

    [Fact]
    public void CreateDefault_Validators_MinValidators_Is1()
    {
        var policy = RegisterPolicy.CreateDefault();

        policy.Validators.MinValidators.Should().Be(1);
    }

    [Fact]
    public void CreateDefault_Validators_MaxValidators_Is100()
    {
        var policy = RegisterPolicy.CreateDefault();

        policy.Validators.MaxValidators.Should().Be(100);
    }

    [Fact]
    public void CreateDefault_Validators_RequireStake_IsFalse()
    {
        var policy = RegisterPolicy.CreateDefault();

        policy.Validators.RequireStake.Should().BeFalse();
    }

    [Fact]
    public void CreateDefault_Validators_StakeAmount_IsNull()
    {
        var policy = RegisterPolicy.CreateDefault();

        policy.Validators.StakeAmount.Should().BeNull();
    }

    [Fact]
    public void CreateDefault_Validators_OperationalTtlSeconds_Is60()
    {
        var policy = RegisterPolicy.CreateDefault();

        policy.Validators.OperationalTtlSeconds.Should().Be(60);
    }

    // --- CreateDefault: Consensus ---

    [Fact]
    public void CreateDefault_Consensus_SignatureThresholdMin_Is2()
    {
        var policy = RegisterPolicy.CreateDefault();

        policy.Consensus.SignatureThresholdMin.Should().Be(2);
    }

    [Fact]
    public void CreateDefault_Consensus_SignatureThresholdMax_Is10()
    {
        var policy = RegisterPolicy.CreateDefault();

        policy.Consensus.SignatureThresholdMax.Should().Be(10);
    }

    [Fact]
    public void CreateDefault_Consensus_MaxTransactionsPerDocket_Is1000()
    {
        var policy = RegisterPolicy.CreateDefault();

        policy.Consensus.MaxTransactionsPerDocket.Should().Be(1000);
    }

    [Fact]
    public void CreateDefault_Consensus_DocketBuildIntervalMs_Is100()
    {
        var policy = RegisterPolicy.CreateDefault();

        policy.Consensus.DocketBuildIntervalMs.Should().Be(100);
    }

    [Fact]
    public void CreateDefault_Consensus_DocketTimeoutSeconds_Is30()
    {
        var policy = RegisterPolicy.CreateDefault();

        policy.Consensus.DocketTimeoutSeconds.Should().Be(30);
    }

    // --- CreateDefault: LeaderElection ---

    [Fact]
    public void CreateDefault_LeaderElection_Mechanism_IsRotating()
    {
        var policy = RegisterPolicy.CreateDefault();

        policy.LeaderElection.Mechanism.Should().Be(ElectionMechanism.Rotating);
    }

    [Fact]
    public void CreateDefault_LeaderElection_HeartbeatIntervalMs_Is1000()
    {
        var policy = RegisterPolicy.CreateDefault();

        policy.LeaderElection.HeartbeatIntervalMs.Should().Be(1000);
    }

    [Fact]
    public void CreateDefault_LeaderElection_LeaderTimeoutMs_Is5000()
    {
        var policy = RegisterPolicy.CreateDefault();

        policy.LeaderElection.LeaderTimeoutMs.Should().Be(5000);
    }

    [Fact]
    public void CreateDefault_LeaderElection_TermDurationSeconds_Is60()
    {
        var policy = RegisterPolicy.CreateDefault();

        policy.LeaderElection.TermDurationSeconds.Should().Be(60);
    }

    // --- CreateDefault: Timestamps ---

    [Fact]
    public void CreateDefault_UpdatedBy_IsNull()
    {
        var policy = RegisterPolicy.CreateDefault();

        policy.UpdatedBy.Should().BeNull();
    }

    [Fact]
    public void CreateDefault_UpdatedAt_IsRecentUtc()
    {
        var before = DateTimeOffset.UtcNow;
        var policy = RegisterPolicy.CreateDefault();
        var after = DateTimeOffset.UtcNow;

        policy.UpdatedAt.Should().BeOnOrAfter(before);
        policy.UpdatedAt.Should().BeOnOrBefore(after);
    }

    // --- JSON Serialization Round-Trip ---

    [Fact]
    public void Serialization_DefaultPolicy_RoundTrips()
    {
        var policy = RegisterPolicy.CreateDefault();

        var json = JsonSerializer.Serialize(policy);
        var deserialized = JsonSerializer.Deserialize<RegisterPolicy>(json);

        deserialized.Should().NotBeNull();
        deserialized!.Version.Should().Be(policy.Version);
        deserialized.Governance.QuorumFormula.Should().Be(policy.Governance.QuorumFormula);
        deserialized.Governance.ProposalTtlDays.Should().Be(policy.Governance.ProposalTtlDays);
        deserialized.Governance.OwnerCanBypassQuorum.Should().Be(policy.Governance.OwnerCanBypassQuorum);
        deserialized.Governance.BlueprintVersion.Should().Be(policy.Governance.BlueprintVersion);
        deserialized.Validators.RegistrationMode.Should().Be(policy.Validators.RegistrationMode);
        deserialized.Validators.ApprovedValidators.Should().BeEmpty();
        deserialized.Validators.MinValidators.Should().Be(policy.Validators.MinValidators);
        deserialized.Validators.MaxValidators.Should().Be(policy.Validators.MaxValidators);
        deserialized.Validators.RequireStake.Should().Be(policy.Validators.RequireStake);
        deserialized.Validators.StakeAmount.Should().BeNull();
        deserialized.Validators.OperationalTtlSeconds.Should().Be(policy.Validators.OperationalTtlSeconds);
        deserialized.Consensus.SignatureThresholdMin.Should().Be(policy.Consensus.SignatureThresholdMin);
        deserialized.Consensus.SignatureThresholdMax.Should().Be(policy.Consensus.SignatureThresholdMax);
        deserialized.Consensus.MaxTransactionsPerDocket.Should().Be(policy.Consensus.MaxTransactionsPerDocket);
        deserialized.Consensus.DocketBuildIntervalMs.Should().Be(policy.Consensus.DocketBuildIntervalMs);
        deserialized.Consensus.DocketTimeoutSeconds.Should().Be(policy.Consensus.DocketTimeoutSeconds);
        deserialized.LeaderElection.Mechanism.Should().Be(policy.LeaderElection.Mechanism);
        deserialized.LeaderElection.HeartbeatIntervalMs.Should().Be(policy.LeaderElection.HeartbeatIntervalMs);
        deserialized.LeaderElection.LeaderTimeoutMs.Should().Be(policy.LeaderElection.LeaderTimeoutMs);
        deserialized.LeaderElection.TermDurationSeconds.Should().Be(policy.LeaderElection.TermDurationSeconds);
        deserialized.UpdatedBy.Should().BeNull();
    }

    [Fact]
    public void Serialization_WithNullableFieldsSet_RoundTrips()
    {
        var policy = RegisterPolicy.CreateDefault();
        policy.UpdatedBy = "did:sorcha:w:admin1";
        policy.Validators.StakeAmount = 500.0m;
        policy.Validators.RequireStake = true;

        var json = JsonSerializer.Serialize(policy);
        var deserialized = JsonSerializer.Deserialize<RegisterPolicy>(json);

        deserialized.Should().NotBeNull();
        deserialized!.UpdatedBy.Should().Be("did:sorcha:w:admin1");
        deserialized.Validators.StakeAmount.Should().Be(500.0m);
        deserialized.Validators.RequireStake.Should().BeTrue();
    }

    [Fact]
    public void Serialization_NullUpdatedBy_OmittedFromJson()
    {
        var policy = RegisterPolicy.CreateDefault();

        var json = JsonSerializer.Serialize(policy);

        json.Should().NotContain("\"updatedBy\"");
    }

    [Fact]
    public void Serialization_NullStakeAmount_OmittedFromJson()
    {
        var policy = RegisterPolicy.CreateDefault();

        var json = JsonSerializer.Serialize(policy);

        json.Should().NotContain("\"stakeAmount\"");
    }

    [Fact]
    public void Serialization_SetUpdatedBy_IncludedInJson()
    {
        var policy = RegisterPolicy.CreateDefault();
        policy.UpdatedBy = "did:sorcha:w:admin1";

        var json = JsonSerializer.Serialize(policy);

        json.Should().Contain("\"updatedBy\"");
        json.Should().Contain("did:sorcha:w:admin1");
    }

    // --- RegisterControlRecord.RegisterPolicy ---

    [Fact]
    public void RegisterControlRecord_RegisterPolicy_IsNullByDefault()
    {
        var record = new RegisterControlRecord();

        record.RegisterPolicy.Should().BeNull();
    }

    [Fact]
    public void RegisterControlRecord_NullRegisterPolicy_OmittedFromJson()
    {
        var record = new RegisterControlRecord
        {
            RegisterId = "a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4",
            Name = "Test",
            TenantId = "t1",
            CreatedAt = DateTimeOffset.UtcNow,
            Attestations = []
        };

        var json = JsonSerializer.Serialize(record);

        json.Should().NotContain("\"registerPolicy\"");
    }

    [Fact]
    public void RegisterControlRecord_WithRegisterPolicy_IncludedInJson()
    {
        var record = new RegisterControlRecord
        {
            RegisterId = "a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4",
            Name = "Test",
            TenantId = "t1",
            CreatedAt = DateTimeOffset.UtcNow,
            Attestations = [],
            RegisterPolicy = RegisterPolicy.CreateDefault()
        };

        var json = JsonSerializer.Serialize(record);

        json.Should().Contain("\"registerPolicy\"");
    }

    // --- Enum Serialization as Strings ---

    [Fact]
    public void EnumSerialization_QuorumFormula_SerializesAsString()
    {
        var policy = RegisterPolicy.CreateDefault();

        var json = JsonSerializer.Serialize(policy);

        json.Should().Contain("\"StrictMajority\"");
        json.Should().NotContain("\"quorumFormula\":0");
    }

    [Fact]
    public void EnumSerialization_RegistrationMode_SerializesAsString()
    {
        var policy = RegisterPolicy.CreateDefault();

        var json = JsonSerializer.Serialize(policy);

        json.Should().Contain("\"Public\"");
        json.Should().NotContain("\"registrationMode\":0");
    }

    [Fact]
    public void EnumSerialization_ElectionMechanism_SerializesAsString()
    {
        var policy = RegisterPolicy.CreateDefault();

        var json = JsonSerializer.Serialize(policy);

        json.Should().Contain("\"Rotating\"");
        json.Should().NotContain("\"mechanism\":0");
    }

    [Fact]
    public void EnumSerialization_QuorumFormula_DeserializesFromString()
    {
        var policy = RegisterPolicy.CreateDefault();
        var json = JsonSerializer.Serialize(policy);

        var deserialized = JsonSerializer.Deserialize<RegisterPolicy>(json);

        deserialized!.Governance.QuorumFormula.Should().Be(QuorumFormula.StrictMajority);
    }

    [Fact]
    public void EnumSerialization_RegistrationMode_DeserializesFromString()
    {
        var policy = RegisterPolicy.CreateDefault();
        var json = JsonSerializer.Serialize(policy);

        var deserialized = JsonSerializer.Deserialize<RegisterPolicy>(json);

        deserialized!.Validators.RegistrationMode.Should().Be(RegistrationMode.Public);
    }

    [Fact]
    public void EnumSerialization_ElectionMechanism_DeserializesFromString()
    {
        var policy = RegisterPolicy.CreateDefault();
        var json = JsonSerializer.Serialize(policy);

        var deserialized = JsonSerializer.Deserialize<RegisterPolicy>(json);

        deserialized!.LeaderElection.Mechanism.Should().Be(ElectionMechanism.Rotating);
    }
}
