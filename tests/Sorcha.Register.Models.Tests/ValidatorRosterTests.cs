// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using FluentAssertions;
using Sorcha.Register.Models;

namespace Sorcha.Register.Models.Tests;

public class ValidatorRosterEntryTests
{
    [Fact]
    public void ValidEntry_HasAllRequiredFields()
    {
        var entry = CreateActiveEntry("validator-1");
        entry.ValidatorId.Should().Be("validator-1");
        entry.PublicKey.Should().NotBeNullOrEmpty();
        entry.Algorithm.Should().Be(SignatureAlgorithm.ED25519);
        entry.DerivationContext.Should().Be("sorcha:docket-signing");
        entry.Status.Should().Be(ValidatorKeyStatus.Active);
        entry.AuthorizedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
        entry.RevokedAt.Should().BeNull();
    }

    [Fact]
    public void DefaultStatus_IsActive()
    {
        var entry = new ValidatorRosterEntry();
        entry.Status.Should().Be(ValidatorKeyStatus.Active);
    }

    [Fact]
    public void SerializationRoundtrip_PreservesAllFields()
    {
        var entry = CreateActiveEntry("validator-1");
        entry.RevokedAt = DateTimeOffset.UtcNow;
        entry.Status = ValidatorKeyStatus.Rotated;

        var json = JsonSerializer.Serialize(entry);
        var deserialized = JsonSerializer.Deserialize<ValidatorRosterEntry>(json);

        deserialized.Should().NotBeNull();
        deserialized!.ValidatorId.Should().Be(entry.ValidatorId);
        deserialized.PublicKey.Should().Be(entry.PublicKey);
        deserialized.Algorithm.Should().Be(entry.Algorithm);
        deserialized.DerivationContext.Should().Be(entry.DerivationContext);
        deserialized.Status.Should().Be(ValidatorKeyStatus.Rotated);
        deserialized.RevokedAt.Should().NotBeNull();
    }

    [Fact]
    public void ActiveEntry_OmitsRevokedAt()
    {
        var entry = CreateActiveEntry("v1");
        var json = JsonSerializer.Serialize(entry);
        json.Should().NotContain("revokedAt");
    }

    private static ValidatorRosterEntry CreateActiveEntry(string validatorId) => new()
    {
        ValidatorId = validatorId,
        PublicKey = Convert.ToBase64String(new byte[32]),
        Algorithm = SignatureAlgorithm.ED25519,
        DerivationContext = "sorcha:docket-signing",
        Status = ValidatorKeyStatus.Active,
        AuthorizedAt = DateTimeOffset.UtcNow
    };
}

public class ValidatorRosterValidationTests
{
    [Fact]
    public void ValidRoster_WithOneActiveValidator_PassesValidation()
    {
        var roster = CreateRosterWithEntries(1);
        roster.Validate().Should().BeEmpty();
    }

    [Fact]
    public void EmptyValidators_FailsValidation()
    {
        var roster = new ValidatorRoster { Validators = [], RequiredSignatures = 1 };
        var errors = roster.Validate();
        errors.Should().Contain(e => e.Contains("At least one validator"));
    }

    [Fact]
    public void OverTenValidators_FailsValidation()
    {
        var roster = CreateRosterWithEntries(11);
        var errors = roster.Validate();
        errors.Should().Contain(e => e.Contains("Maximum 10"));
    }

    [Fact]
    public void NoActiveValidators_FailsValidation()
    {
        var roster = new ValidatorRoster
        {
            Validators =
            [
                new ValidatorRosterEntry
                {
                    ValidatorId = "v1",
                    PublicKey = Convert.ToBase64String(new byte[32]),
                    Algorithm = SignatureAlgorithm.ED25519,
                    DerivationContext = "sorcha:docket-signing",
                    Status = ValidatorKeyStatus.Revoked,
                    AuthorizedAt = DateTimeOffset.UtcNow
                }
            ]
        };
        var errors = roster.Validate();
        errors.Should().Contain(e => e.Contains("Active status"));
    }

    [Fact]
    public void RequiredSignatures_ExceedsActiveCount_FailsValidation()
    {
        var roster = CreateRosterWithEntries(1);
        roster.RequiredSignatures = 2;
        var errors = roster.Validate();
        errors.Should().Contain(e => e.Contains("RequiredSignatures"));
    }

    [Fact]
    public void RequiredSignatures_LessThanOne_FailsValidation()
    {
        var roster = CreateRosterWithEntries(1);
        roster.RequiredSignatures = 0;
        var errors = roster.Validate();
        errors.Should().Contain(e => e.Contains("at least 1"));
    }

    [Fact]
    public void DuplicateValidatorIds_ForTheSamePurpose_FailsValidation()
    {
        // Same node, same purpose key, twice — a genuine duplicate, still refused.
        var roster = new ValidatorRoster
        {
            Validators =
            [
                CreateEntry("v1"),
                CreateEntry("v1")
            ]
        };
        var errors = roster.Validate();
        errors.Should().Contain(e => e.Contains("Duplicate"));
    }

    /// <summary>
    /// Feature 196 (#1591): one node holding several PURPOSE keys is the roster's intended shape —
    /// that is what <c>DerivationContext</c> is for. Uniqueness is on the pair, not the id alone.
    /// </summary>
    /// <remarks>
    /// Keying uniqueness on <c>ValidatorId</c> alone made a second purpose key unrepresentable, which
    /// is why blueprint-publication authority had nowhere to live: a node needs
    /// <c>sorcha:docket-signing</c> to seal dockets AND <c>sorcha:blueprint-publish</c> to publish
    /// definitions, and they are deliberately different keys so a compromise of one is not a
    /// compromise of the other.
    /// </remarks>
    [Fact]
    public void SameValidator_WithTwoDifferentPurposeKeys_IsValid()
    {
        var docketEntry = CreateEntry("node-1");
        var publishEntry = CreateEntry("node-1");
        publishEntry.DerivationContext = "sorcha:blueprint-publish";
        publishEntry.PublicKey = Convert.ToBase64String(Enumerable.Repeat((byte)9, 32).ToArray());

        var roster = new ValidatorRoster { Validators = [docketEntry, publishEntry] };

        roster.Validate().Should().NotContain(e => e.Contains("Duplicate"),
            "the roster is a purpose-keyed registry — one node, several keys, is its whole point");
    }

    [Fact]
    public void ActiveValidators_ReturnsOnlyActive()
    {
        var roster = new ValidatorRoster
        {
            Validators =
            [
                CreateEntry("v1", ValidatorKeyStatus.Active),
                CreateEntry("v2", ValidatorKeyStatus.Rotated),
                CreateEntry("v3", ValidatorKeyStatus.Revoked)
            ]
        };
        roster.ActiveValidators.Should().HaveCount(1);
        roster.ActiveValidators.First().ValidatorId.Should().Be("v1");
    }

    [Fact]
    public void VerifiableValidators_ReturnsActiveAndRotated()
    {
        var roster = new ValidatorRoster
        {
            Validators =
            [
                CreateEntry("v1", ValidatorKeyStatus.Active),
                CreateEntry("v2", ValidatorKeyStatus.Rotated),
                CreateEntry("v3", ValidatorKeyStatus.Revoked)
            ]
        };
        roster.VerifiableValidators.Should().HaveCount(2);
    }

    [Fact]
    public void SerializationRoundtrip_PreservesRoster()
    {
        var roster = CreateRosterWithEntries(2);
        roster.RequiredSignatures = 1;
        roster.Version = 3;

        var json = JsonSerializer.Serialize(roster);
        var deserialized = JsonSerializer.Deserialize<ValidatorRoster>(json);

        deserialized.Should().NotBeNull();
        deserialized!.Validators.Should().HaveCount(2);
        deserialized.RequiredSignatures.Should().Be(1);
        deserialized.Version.Should().Be(3);
    }

    [Fact]
    public void RegisterControlRecord_WithValidators_SerializesCorrectly()
    {
        var record = new RegisterControlRecord
        {
            RegisterId = "a1b2c3d4e5f6a7b8c9d0e1f2a3b4c5d6",
            Name = "Test Register",
            CreatedAt = DateTimeOffset.UtcNow,
            Attestations = [new RegisterAttestation
            {
                Role = RegisterRole.Owner,
                Subject = "did:sorcha:w:test",
                PublicKey = Convert.ToBase64String(new byte[32]),
                Signature = Convert.ToBase64String(new byte[64]),
                Algorithm = SignatureAlgorithm.ED25519,
                GrantedAt = DateTimeOffset.UtcNow
            }],
            Validators = CreateRosterWithEntries(1)
        };

        var json = JsonSerializer.Serialize(record);
        json.Should().Contain("\"validators\"");

        var deserialized = JsonSerializer.Deserialize<RegisterControlRecord>(json);
        deserialized.Should().NotBeNull();
        deserialized!.Validators.Should().NotBeNull();
        deserialized.Validators!.Validators.Should().HaveCount(1);
    }

    [Fact]
    public void RegisterControlRecord_WithoutValidators_OmitsField()
    {
        var record = new RegisterControlRecord
        {
            RegisterId = "a1b2c3d4e5f6a7b8c9d0e1f2a3b4c5d6",
            Name = "Test Register",
            CreatedAt = DateTimeOffset.UtcNow,
            Attestations = [new RegisterAttestation
            {
                Role = RegisterRole.Owner,
                Subject = "did:sorcha:w:test",
                PublicKey = Convert.ToBase64String(new byte[32]),
                Signature = Convert.ToBase64String(new byte[64]),
                Algorithm = SignatureAlgorithm.ED25519,
                GrantedAt = DateTimeOffset.UtcNow
            }]
        };

        var json = JsonSerializer.Serialize(record);
        json.Should().NotContain("\"validators\"");
    }

    private static ValidatorRoster CreateRosterWithEntries(int count) => new()
    {
        Validators = Enumerable.Range(1, count)
            .Select(i => CreateEntry($"validator-{i}"))
            .ToList(),
        RequiredSignatures = 1,
        Version = 1
    };

    private static ValidatorRosterEntry CreateEntry(string id, ValidatorKeyStatus status = ValidatorKeyStatus.Active) => new()
    {
        ValidatorId = id,
        PublicKey = Convert.ToBase64String(new byte[32]),
        Algorithm = SignatureAlgorithm.ED25519,
        DerivationContext = "sorcha:docket-signing",
        Status = status,
        AuthorizedAt = DateTimeOffset.UtcNow
    };
}
