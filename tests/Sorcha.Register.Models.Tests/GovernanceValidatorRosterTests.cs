// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.Register.Models;
using Xunit;

namespace Sorcha.Register.Models.Tests;

/// <summary>
/// Tests for validator roster governance operations (add, remove, rotate).
/// Tests the roster mutation logic that the governance endpoint uses.
/// </summary>
public class GovernanceValidatorRosterTests
{
    [Fact]
    public void AddValidator_CreatesRosterWithTwoValidators()
    {
        var roster = CreateSingleValidatorRoster();
        var newEntry = CreateEntry("validator-2");

        roster.Validators.Add(newEntry);
        roster.Version++;

        roster.Validators.Should().HaveCount(2);
        roster.ActiveValidators.Count().Should().Be(2);
        roster.Validate().Should().BeEmpty();
    }

    [Fact]
    public void AddValidator_WithDuplicateId_FailsValidation()
    {
        var roster = CreateSingleValidatorRoster();
        var duplicate = CreateEntry("validator-1"); // Same ID as existing

        roster.Validators.Add(duplicate);

        roster.Validate().Should().Contain(e => e.Contains("Duplicate"));
    }

    [Fact]
    public void RemoveValidator_SetsStatusToRevoked()
    {
        var roster = CreateTwoValidatorRoster();

        var toRevoke = roster.Validators.First(v => v.ValidatorId == "validator-2");
        toRevoke.Status = ValidatorKeyStatus.Revoked;
        toRevoke.RevokedAt = DateTimeOffset.UtcNow;
        roster.Version++;

        roster.Validators.Should().HaveCount(2);
        roster.ActiveValidators.Count().Should().Be(1);
        roster.Validate().Should().BeEmpty();
    }

    [Fact]
    public void RemoveValidator_LastActive_FailsValidation()
    {
        var roster = CreateSingleValidatorRoster();

        roster.Validators[0].Status = ValidatorKeyStatus.Revoked;
        roster.Validators[0].RevokedAt = DateTimeOffset.UtcNow;

        var errors = roster.Validate();
        errors.Should().Contain(e => e.Contains("Active status"));
    }

    [Fact]
    public void RemoveValidator_WhenThresholdWouldBeViolated_FailsValidation()
    {
        var roster = CreateTwoValidatorRoster();
        roster.RequiredSignatures = 2;

        // Remove one — now only 1 active but requiredSignatures is 2
        roster.Validators[1].Status = ValidatorKeyStatus.Revoked;

        var errors = roster.Validate();
        errors.Should().Contain(e => e.Contains("RequiredSignatures"));
    }

    [Fact]
    public void RotateValidatorKey_MarksOldKeyRotatedAndAddsNew()
    {
        var roster = CreateSingleValidatorRoster();
        var originalKey = roster.Validators[0].PublicKey;

        // Rotate: mark old as Rotated, add new Active
        roster.Validators[0].Status = ValidatorKeyStatus.Rotated;
        roster.Validators[0].RevokedAt = DateTimeOffset.UtcNow;

        var newEntry = new ValidatorRosterEntry
        {
            ValidatorId = "validator-1-rotated",
            PublicKey = Convert.ToBase64String(new byte[] { 99, 88, 77 }),
            Algorithm = SignatureAlgorithm.ED25519,
            DerivationContext = "sorcha:docket-signing",
            Status = ValidatorKeyStatus.Active,
            AuthorizedAt = DateTimeOffset.UtcNow
        };
        roster.Validators.Add(newEntry);
        roster.Version++;

        roster.Validators.Should().HaveCount(2);
        roster.ActiveValidators.Count().Should().Be(1);
        roster.VerifiableValidators.Count().Should().Be(2); // Both Active + Rotated
        roster.Validate().Should().BeEmpty();
    }

    [Fact]
    public void RotateValidatorKey_RotatedKeyStillVerifiable()
    {
        var roster = CreateSingleValidatorRoster();
        roster.Validators[0].Status = ValidatorKeyStatus.Rotated;
        roster.Validators.Add(CreateEntry("validator-new"));

        roster.VerifiableValidators.Should().HaveCount(2);
        roster.VerifiableValidators.Should().Contain(v => v.Status == ValidatorKeyStatus.Rotated);
    }

    [Fact]
    public void GovernanceOperation_AddValidator_CarriesValidatorEntry()
    {
        var operation = new GovernanceOperation
        {
            OperationType = GovernanceOperationType.AddValidator,
            ProposerDid = "did:sorcha:w:owner",
            TargetDid = "validator-2",
            ProposedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(7),
            Status = ProposalStatus.Pending,
            ValidatorEntry = CreateEntry("validator-2")
        };

        operation.OperationType.Should().Be(GovernanceOperationType.AddValidator);
        operation.ValidatorEntry.Should().NotBeNull();
        operation.ValidatorEntry!.ValidatorId.Should().Be("validator-2");
    }

    [Fact]
    public void GovernanceOperation_RemoveValidator_UsesTargetDid()
    {
        var operation = new GovernanceOperation
        {
            OperationType = GovernanceOperationType.RemoveValidator,
            ProposerDid = "did:sorcha:w:owner",
            TargetDid = "validator-2",
            ProposedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(7),
            Status = ProposalStatus.Pending
        };

        operation.OperationType.Should().Be(GovernanceOperationType.RemoveValidator);
        operation.TargetDid.Should().Be("validator-2");
        operation.ValidatorEntry.Should().BeNull();
    }

    [Fact]
    public void GovernanceOperation_RotateValidatorKey_CarriesNewEntry()
    {
        var newEntry = new ValidatorRosterEntry
        {
            ValidatorId = "validator-1-new",
            PublicKey = Convert.ToBase64String(new byte[] { 1, 2, 3 }),
            Algorithm = SignatureAlgorithm.ED25519,
            DerivationContext = "sorcha:docket-signing",
            Status = ValidatorKeyStatus.Active,
            AuthorizedAt = DateTimeOffset.UtcNow
        };

        var operation = new GovernanceOperation
        {
            OperationType = GovernanceOperationType.RotateValidatorKey,
            ProposerDid = "did:sorcha:w:owner",
            TargetDid = "validator-1",
            ProposedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(7),
            Status = ProposalStatus.Pending,
            ValidatorEntry = newEntry
        };

        operation.OperationType.Should().Be(GovernanceOperationType.RotateValidatorKey);
        operation.TargetDid.Should().Be("validator-1");
        operation.ValidatorEntry!.ValidatorId.Should().Be("validator-1-new");
    }

    [Fact]
    public void RosterVersionIncrements_OnEachOperation()
    {
        var roster = CreateSingleValidatorRoster();
        roster.Version.Should().Be(1);

        roster.Validators.Add(CreateEntry("v2"));
        roster.Version++;
        roster.Version.Should().Be(2);

        roster.Validators.Add(CreateEntry("v3"));
        roster.Version++;
        roster.Version.Should().Be(3);
    }

    // --- Helpers ---

    private static ValidatorRoster CreateSingleValidatorRoster() => new()
    {
        Validators = [CreateEntry("validator-1")],
        RequiredSignatures = 1,
        Version = 1
    };

    private static ValidatorRoster CreateTwoValidatorRoster() => new()
    {
        Validators = [CreateEntry("validator-1"), CreateEntry("validator-2")],
        RequiredSignatures = 1,
        Version = 1
    };

    private static ValidatorRosterEntry CreateEntry(string id) => new()
    {
        ValidatorId = id,
        PublicKey = Convert.ToBase64String(new byte[32]),
        Algorithm = SignatureAlgorithm.ED25519,
        DerivationContext = "sorcha:docket-signing",
        Status = ValidatorKeyStatus.Active,
        AuthorizedAt = DateTimeOffset.UtcNow
    };
}
