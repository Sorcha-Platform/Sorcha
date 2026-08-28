// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Sorcha.Cryptography.Enums;
using Sorcha.Cryptography.Interfaces;
using Sorcha.Register.Models;
using Sorcha.ServiceClients.Register;
using Sorcha.Validator.Service.Configuration;
using Sorcha.Validator.Service.Models;
using Sorcha.Validator.Service.Services;
using Sorcha.Validator.Service.Services.Interfaces;

namespace Sorcha.Validator.Service.Tests.Services;

/// <summary>
/// Feature 196 (FR-006 / US4): a transaction must not describe itself one way to the validation
/// rules and another way to its own signature.
/// </summary>
/// <remarks>
/// <c>blueprintId</c> and <c>actionId</c> exist twice — inside the signed payload, and again as
/// submission-level fields the signature does not cover. Every rule reads the unsigned copies. This
/// is the general form of #1591: the specific instances are closed by the exemption authority rules,
/// and this closes the shape so the next instance cannot appear.
/// </remarks>
public class SignedFieldAgreementTests
{
    private readonly ValidationEngine _engine;

    public SignedFieldAgreementTests()
    {
        var hash = new Mock<IHashProvider>();
        // Real-ish: return a distinct digest per input so a hash comparison can actually fail.
        // Never a fixed array — #1587's tests did that and every hash compared equal by construction.
        hash.Setup(h => h.ComputeHash(It.IsAny<byte[]>(), It.IsAny<HashType>()))
            .Returns((byte[] data, HashType _) => System.Security.Cryptography.SHA256.HashData(data));

        var config = new ValidationEngineConfiguration
        {
            EnableSchemaValidation = false,
            EnableSignatureVerification = false,
            EnableChainValidation = false,
            EnableBlueprintConformance = false,
            EnableParallelValidation = false,
            MaxClockSkew = TimeSpan.FromMinutes(5),
            MaxTransactionAge = TimeSpan.FromHours(1)
        };

        var rights = new Mock<IRightsEnforcementService>();
        rights.Setup(r => r.ValidateGovernanceRightsAsync(It.IsAny<Transaction>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync((Transaction tx, CancellationToken _) =>
                  ValidationEngineResult.Success(tx.TransactionId, tx.RegisterId, TimeSpan.Zero));

        _engine = new ValidationEngine(
            Options.Create(config),
            new Mock<IBlueprintCache>().Object,
            hash.Object,
            new Mock<ICryptoModule>().Object,
            new Mock<IWalletUtilities>().Object,
            new Mock<IRegisterServiceClient>().Object,
            rights.Object,
            new Mock<ILogger<ValidationEngine>>().Object);
    }

    private static Transaction Tx(string unsignedBlueprintId, string unsignedActionId, string payloadJson)
    {
        var payload = JsonSerializer.Deserialize<JsonElement>(payloadJson);
        var payloadHash = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(payload.GetRawText()))).ToLowerInvariant();

        return new Transaction
        {
            TransactionId = Guid.NewGuid().ToString("N"),
            RegisterId = "test-register",
            BlueprintId = unsignedBlueprintId,
            ActionId = unsignedActionId,
            Payload = payload,
            PayloadHash = payloadHash,
            CreatedAt = DateTimeOffset.UtcNow,
            SequenceNumber = 1,
            Signatures =
            [
                new RegisterSignature
                {
                    PublicKey = new byte[32],
                    SignatureValue = new byte[64],
                    Algorithm = "ED25519",
                    SignedAt = DateTimeOffset.UtcNow
                }
            ],
            Metadata = []
        };
    }

    private static bool HasDisagreement(ValidationEngineResult r) =>
        r.Errors.Any(e => e.Code == "VAL_STRUCT_011");

    [Fact]
    public async Task BlueprintIdDisagreeingWithTheSignedPayload_IsRefused()
    {
        var tx = Tx("attacker-chosen-blueprint", "1",
            """{"type":"action","blueprintId":"real-blueprint","actionId":"1"}""");

        var result = await _engine.ValidateTransactionAsync(tx);

        HasDisagreement(result).Should().BeTrue();
    }

    [Fact]
    public async Task ActionIdDisagreeingWithTheSignedPayload_IsRefused()
    {
        var tx = Tx("real-blueprint", "99",
            """{"type":"action","blueprintId":"real-blueprint","actionId":"1"}""");

        var result = await _engine.ValidateTransactionAsync(tx);

        HasDisagreement(result).Should().BeTrue();
    }

    /// <summary>
    /// The counterfactual: agreeing fields raise no disagreement. Without this the two tests above
    /// would pass against a check that flagged everything.
    /// </summary>
    [Fact]
    public async Task FieldsThatAgree_RaiseNoDisagreement()
    {
        var tx = Tx("real-blueprint", "1",
            """{"type":"action","blueprintId":"real-blueprint","actionId":"1"}""");

        var result = await _engine.ValidateTransactionAsync(tx);

        HasDisagreement(result).Should().BeFalse();
    }

    /// <summary>
    /// Administrative payloads carry no such counterparts — a publication's payload IS the blueprint
    /// definition. Treating an absent counterpart as a disagreement would refuse every genesis,
    /// governance and publication transaction on the network.
    /// </summary>
    [Fact]
    public async Task APayloadWithNoCounterpart_IsNotADisagreement()
    {
        var tx = Tx("some-blueprint", "blueprint-publish",
            """{"title":"A blueprint definition","participants":[]}""");

        var result = await _engine.ValidateTransactionAsync(tx);

        HasDisagreement(result).Should().BeFalse();
    }

    [Fact]
    public async Task ANonObjectPayload_IsNotADisagreement()
    {
        var tx = Tx("some-blueprint", "1", """ "just-a-string" """);

        var result = await _engine.ValidateTransactionAsync(tx);

        HasDisagreement(result).Should().BeFalse();
    }
}
