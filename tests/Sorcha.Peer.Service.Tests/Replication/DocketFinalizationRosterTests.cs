// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Sorcha.Peer.Service.Replication;
using Sorcha.Register.Models;

namespace Sorcha.Peer.Service.Tests.Replication;

/// <summary>
/// Tests for DocketFinalizationService roster-based verification logic.
/// Verifies that IsAuthorizedSigner correctly accepts/rejects signers.
/// </summary>
public class DocketFinalizationRosterTests
{
    private readonly ValidatorKeyCache _cache;

    public DocketFinalizationRosterTests()
    {
        _cache = new ValidatorKeyCache(new Mock<ILogger<ValidatorKeyCache>>().Object);
    }

    [Fact]
    public void AuthorizedSigner_AcceptedByRoster()
    {
        var key = new byte[] { 1, 2, 3, 4, 5 };
        var record = CreateControlRecord("validator-1", key, ValidatorKeyStatus.Active);
        _cache.ExtractFromControlRecord("reg-1", record);

        _cache.IsAuthorizedSigner("reg-1", key).Should().BeTrue();
    }

    [Fact]
    public void UnauthorizedSigner_RejectedByRoster()
    {
        var authorizedKey = new byte[] { 1, 2, 3, 4, 5 };
        var unauthorizedKey = new byte[] { 9, 8, 7, 6, 5 };
        var record = CreateControlRecord("validator-1", authorizedKey, ValidatorKeyStatus.Active);
        _cache.ExtractFromControlRecord("reg-1", record);

        _cache.IsAuthorizedSigner("reg-1", unauthorizedKey).Should().BeFalse();
    }

    [Fact]
    public void RevokedSigner_RejectedByRoster()
    {
        var key = new byte[] { 1, 2, 3 };
        var record = CreateControlRecord("validator-1", key, ValidatorKeyStatus.Revoked);
        // Need at least one Active for the roster to be valid
        record.Validators!.Validators.Add(new ValidatorRosterEntry
        {
            ValidatorId = "validator-2",
            PublicKey = Convert.ToBase64String(new byte[] { 4, 5, 6 }),
            Algorithm = SignatureAlgorithm.ED25519,
            DerivationContext = "sorcha:docket-signing",
            Status = ValidatorKeyStatus.Active,
            AuthorizedAt = DateTimeOffset.UtcNow
        });

        _cache.ExtractFromControlRecord("reg-1", record);

        _cache.IsAuthorizedSigner("reg-1", key).Should().BeFalse();
    }

    [Fact]
    public void RotatedSigner_AcceptedForHistoricalVerification()
    {
        var rotatedKey = new byte[] { 1, 2, 3 };
        var activeKey = new byte[] { 4, 5, 6 };

        var record = new RegisterControlRecord
        {
            RegisterId = "reg-1",
            Name = "Test",
            CreatedAt = DateTimeOffset.UtcNow,
            Attestations = [],
            Validators = new ValidatorRoster
            {
                Validators =
                [
                    new ValidatorRosterEntry
                    {
                        ValidatorId = "v1-old",
                        PublicKey = Convert.ToBase64String(rotatedKey),
                        Algorithm = SignatureAlgorithm.ED25519,
                        DerivationContext = "sorcha:docket-signing",
                        Status = ValidatorKeyStatus.Rotated,
                        AuthorizedAt = DateTimeOffset.UtcNow.AddDays(-30),
                        RevokedAt = DateTimeOffset.UtcNow
                    },
                    new ValidatorRosterEntry
                    {
                        ValidatorId = "v1-new",
                        PublicKey = Convert.ToBase64String(activeKey),
                        Algorithm = SignatureAlgorithm.ED25519,
                        DerivationContext = "sorcha:docket-signing",
                        Status = ValidatorKeyStatus.Active,
                        AuthorizedAt = DateTimeOffset.UtcNow
                    }
                ],
                RequiredSignatures = 1,
                Version = 2
            }
        };

        _cache.ExtractFromControlRecord("reg-1", record);

        _cache.IsAuthorizedSigner("reg-1", rotatedKey).Should().BeTrue("rotated keys verify historical dockets");
        _cache.IsAuthorizedSigner("reg-1", activeKey).Should().BeTrue();
    }

    [Fact]
    public void MultipleActiveValidators_AllAccepted()
    {
        var key1 = new byte[] { 1, 1, 1 };
        var key2 = new byte[] { 2, 2, 2 };
        var key3 = new byte[] { 3, 3, 3 };

        var record = new RegisterControlRecord
        {
            RegisterId = "reg-1",
            Name = "Test",
            CreatedAt = DateTimeOffset.UtcNow,
            Attestations = [],
            Validators = new ValidatorRoster
            {
                Validators =
                [
                    CreateEntry("v1", key1),
                    CreateEntry("v2", key2),
                    CreateEntry("v3", key3)
                ],
                RequiredSignatures = 1,
                Version = 1
            }
        };

        _cache.ExtractFromControlRecord("reg-1", record);

        _cache.IsAuthorizedSigner("reg-1", key1).Should().BeTrue();
        _cache.IsAuthorizedSigner("reg-1", key2).Should().BeTrue();
        _cache.IsAuthorizedSigner("reg-1", key3).Should().BeTrue();
        _cache.IsAuthorizedSigner("reg-1", new byte[] { 9, 9, 9 }).Should().BeFalse();
    }

    [Fact]
    public void RosterUpdate_ReplacesOldCache()
    {
        var key1 = new byte[] { 1, 2, 3 };
        var key2 = new byte[] { 4, 5, 6 };

        // Initial roster with key1
        _cache.ExtractFromControlRecord("reg-1", CreateControlRecord("v1", key1, ValidatorKeyStatus.Active));
        _cache.IsAuthorizedSigner("reg-1", key1).Should().BeTrue();
        _cache.IsAuthorizedSigner("reg-1", key2).Should().BeFalse();

        // Updated roster with key2 only
        _cache.ExtractFromControlRecord("reg-1", CreateControlRecord("v2", key2, ValidatorKeyStatus.Active));
        _cache.IsAuthorizedSigner("reg-1", key1).Should().BeFalse("old key should be replaced");
        _cache.IsAuthorizedSigner("reg-1", key2).Should().BeTrue();
    }

    // --- Helpers ---

    private static RegisterControlRecord CreateControlRecord(string validatorId, byte[] key, ValidatorKeyStatus status) => new()
    {
        RegisterId = "reg-1",
        Name = "Test",
        CreatedAt = DateTimeOffset.UtcNow,
        Attestations = [],
        Validators = new ValidatorRoster
        {
            Validators = [new ValidatorRosterEntry
            {
                ValidatorId = validatorId,
                PublicKey = Convert.ToBase64String(key),
                Algorithm = SignatureAlgorithm.ED25519,
                DerivationContext = "sorcha:docket-signing",
                Status = status,
                AuthorizedAt = DateTimeOffset.UtcNow
            }],
            RequiredSignatures = 1,
            Version = 1
        }
    };

    private static ValidatorRosterEntry CreateEntry(string id, byte[] key) => new()
    {
        ValidatorId = id,
        PublicKey = Convert.ToBase64String(key),
        Algorithm = SignatureAlgorithm.ED25519,
        DerivationContext = "sorcha:docket-signing",
        Status = ValidatorKeyStatus.Active,
        AuthorizedAt = DateTimeOffset.UtcNow
    };
}
