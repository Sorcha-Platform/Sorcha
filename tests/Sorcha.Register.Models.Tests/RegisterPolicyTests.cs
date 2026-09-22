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
    public void CreateDefault_Validators_RegistrationMode_IsConsent()
    {
        // Feature 138 US3 / FR-011 — new registers default to Consent (explicit roster approval),
        // not open self-registration.
        var policy = RegisterPolicy.CreateDefault();

        policy.Validators.RegistrationMode.Should().Be(RegistrationMode.Consent);
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

    // --- CreateDefault: DisclosureMetadata (issue #1684) ---

    [Fact]
    public void CreateDefault_DisclosureMetadata_IsPublic()
    {
        var policy = RegisterPolicy.CreateDefault();

        policy.DisclosureMetadata.Should().Be(DisclosureMetadataPolicy.Public);
    }

    [Fact]
    public void DisclosureMetadata_DefaultValueOnANewInstance_IsPublic()
    {
        // Absent/null on a pre-feature control record must default to Public so every existing
        // register keeps working unchanged — this is the C# property default that makes that true
        // (Public == 0 == default(DisclosureMetadataPolicy)), pinned directly rather than only via
        // CreateDefault().
        var policy = new RegisterPolicy();

        policy.DisclosureMetadata.Should().Be(DisclosureMetadataPolicy.Public);
    }

    [Fact]
    public void Deserialization_JsonWithNoDisclosureMetadataField_DefaultsToPublic()
    {
        // Every control record sealed before this feature existed has no "disclosureMetadata" key
        // at all. Deserializing that legacy shape must yield Public, not throw and not leave some
        // other value.
        const string legacyPolicyJson = """
            {"version":1,"governance":{},"validators":{},"consensus":{},"leaderElection":{}}
            """;

        var deserialized = JsonSerializer.Deserialize<RegisterPolicy>(legacyPolicyJson);

        deserialized.Should().NotBeNull();
        deserialized!.DisclosureMetadata.Should().Be(DisclosureMetadataPolicy.Public);
    }

    [Fact]
    public void EnumSerialization_DisclosureMetadata_SerializesAsString()
    {
        var policy = RegisterPolicy.CreateDefault();

        var json = JsonSerializer.Serialize(policy);

        // Pins the wire form: a plain property-level JsonStringEnumConverter (PascalCase), matching
        // the sibling enums in this file (QuorumFormula, RegistrationMode, ElectionMechanism) — not
        // SorchaJson's kebab-case global converter, which a property-level attribute always outranks.
        json.Should().Contain("\"disclosureMetadata\":\"Public\"");
    }

    [Fact]
    public void EnumSerialization_DisclosureMetadataMinimal_SerializesAsString()
    {
        var policy = RegisterPolicy.CreateDefault();
        policy.DisclosureMetadata = DisclosureMetadataPolicy.Minimal;

        var json = JsonSerializer.Serialize(policy);

        json.Should().Contain("\"disclosureMetadata\":\"Minimal\"");
        json.Should().NotContain("\"disclosureMetadata\":1");
    }

    [Fact]
    public void EnumSerialization_DisclosureMetadata_DeserializesFromString()
    {
        var policy = RegisterPolicy.CreateDefault();
        policy.DisclosureMetadata = DisclosureMetadataPolicy.Minimal;
        var json = JsonSerializer.Serialize(policy);

        var deserialized = JsonSerializer.Deserialize<RegisterPolicy>(json);

        deserialized!.DisclosureMetadata.Should().Be(DisclosureMetadataPolicy.Minimal);
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
        deserialized.DisclosureMetadata.Should().Be(policy.DisclosureMetadata);
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

        // Default is Consent (FR-011); the point of this test is that the enum serializes as a
        // string, not its numeric value.
        json.Should().Contain("\"Consent\"");
        json.Should().NotContain("\"registrationMode\":1");
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

        deserialized!.Validators.RegistrationMode.Should().Be(RegistrationMode.Consent);
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
