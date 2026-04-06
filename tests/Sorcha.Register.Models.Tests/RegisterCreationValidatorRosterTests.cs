// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.Register.Models;
using Xunit;

namespace Sorcha.Register.Models.Tests;

/// <summary>
/// Tests for validator roster population during register creation.
/// These test the model-level behavior since the orchestrator requires
/// complex service mocking (ISystemWalletSigningService, IValidatorServiceClient, etc.).
/// </summary>
public class RegisterCreationValidatorRosterTests
{
    [Fact]
    public void ControlRecord_WithValidatorRoster_SerializesToGenesisPayload()
    {
        // Simulates what RegisterCreationOrchestrator.FinalizeAsync produces
        var controlRecord = new RegisterControlRecord
        {
            RegisterId = "a1b2c3d4e5f6a7b8c9d0e1f2a3b4c5d6",
            Name = "Test Register",
            CreatedAt = DateTimeOffset.UtcNow,
            Attestations =
            [
                new RegisterAttestation
                {
                    Role = RegisterRole.Owner,
                    Subject = "did:sorcha:w:test-wallet",
                    PublicKey = Convert.ToBase64String(new byte[32]),
                    Signature = Convert.ToBase64String(new byte[64]),
                    Algorithm = SignatureAlgorithm.ED25519,
                    GrantedAt = DateTimeOffset.UtcNow
                }
            ],
            Validators = new ValidatorRoster
            {
                Validators =
                [
                    new ValidatorRosterEntry
                    {
                        ValidatorId = "system-wallet-address",
                        PublicKey = Convert.ToBase64String(new byte[32]),
                        Algorithm = SignatureAlgorithm.ED25519,
                        DerivationContext = "sorcha:docket-signing",
                        Status = ValidatorKeyStatus.Active,
                        AuthorizedAt = DateTimeOffset.UtcNow
                    }
                ],
                RequiredSignatures = 1,
                Version = 1
            }
        };

        // Serialize like the orchestrator does
        var json = System.Text.Json.JsonSerializer.Serialize(controlRecord);

        json.Should().Contain("\"validators\"");
        json.Should().Contain("\"sorcha:docket-signing\"");
        json.Should().Contain("\"requiredSignatures\":1");

        // Deserialize back
        var deserialized = System.Text.Json.JsonSerializer.Deserialize<RegisterControlRecord>(json);
        deserialized!.Validators.Should().NotBeNull();
        deserialized.Validators!.Validators.Should().HaveCount(1);
        deserialized.Validators.Validators[0].DerivationContext.Should().Be("sorcha:docket-signing");
    }

    [Fact]
    public void ControlRecord_WithExternalValidatorRoster_PreservesExternalEntries()
    {
        // FR-014: External roster should be used as-is
        var externalRoster = new ValidatorRoster
        {
            Validators =
            [
                new ValidatorRosterEntry
                {
                    ValidatorId = "external-validator-1",
                    PublicKey = Convert.ToBase64String(new byte[32]),
                    Algorithm = SignatureAlgorithm.ED25519,
                    DerivationContext = "sorcha:docket-signing",
                    Status = ValidatorKeyStatus.Active,
                    AuthorizedAt = DateTimeOffset.UtcNow
                },
                new ValidatorRosterEntry
                {
                    ValidatorId = "external-validator-2",
                    PublicKey = Convert.ToBase64String(new byte[32]),
                    Algorithm = SignatureAlgorithm.NISTP256,
                    DerivationContext = "sorcha:docket-signing",
                    Status = ValidatorKeyStatus.Active,
                    AuthorizedAt = DateTimeOffset.UtcNow
                }
            ],
            RequiredSignatures = 1,
            Version = 1
        };

        var controlRecord = new RegisterControlRecord
        {
            RegisterId = "a1b2c3d4e5f6a7b8c9d0e1f2a3b4c5d6",
            Name = "System Register",
            CreatedAt = DateTimeOffset.UtcNow,
            Attestations = [new RegisterAttestation
            {
                Role = RegisterRole.Owner,
                Subject = "did:sorcha:w:platform-admin",
                PublicKey = Convert.ToBase64String(new byte[32]),
                Signature = Convert.ToBase64String(new byte[64]),
                Algorithm = SignatureAlgorithm.ED25519,
                GrantedAt = DateTimeOffset.UtcNow
            }],
            Validators = externalRoster
        };

        controlRecord.Validators.Validators.Should().HaveCount(2);
        controlRecord.Validators.Validators[0].ValidatorId.Should().Be("external-validator-1");
        controlRecord.Validators.Validators[1].ValidatorId.Should().Be("external-validator-2");
    }

    [Fact]
    public void ControlRecord_WithEmptyValidatorRoster_FailsValidation()
    {
        // FR-010: Register creation must fail if zero validators
        var emptyRoster = new ValidatorRoster
        {
            Validators = [],
            RequiredSignatures = 1,
            Version = 1
        };

        var errors = emptyRoster.Validate();
        errors.Should().NotBeEmpty();
        errors.Should().Contain(e => e.Contains("At least one validator"));
    }
}
