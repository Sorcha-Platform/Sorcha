// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Sorcha.Peer.Service.Replication;
using Sorcha.Register.Models;

namespace Sorcha.Peer.Service.Tests.Replication;

public class ValidatorKeyCacheTests
{
    private readonly ValidatorKeyCache _cache;

    public ValidatorKeyCacheTests()
    {
        _cache = new ValidatorKeyCache(new Mock<ILogger<ValidatorKeyCache>>().Object);
    }

    [Fact]
    public void TryGetKey_Empty_ReturnsFalse()
    {
        _cache.TryGetKey("reg-1", out var entry).Should().BeFalse();
        entry.Should().BeNull();
    }

    [Fact]
    public void HasKey_AfterExtractFromControlRecord_ReturnsTrue()
    {
        _cache.HasKey("reg-1").Should().BeFalse();
        CacheRosterEntry("reg-1", new byte[] { 1 });
        _cache.HasKey("reg-1").Should().BeTrue();
    }

    [Fact]
    public void TryGetKey_AfterExtract_ReturnsFirstEntry()
    {
        CacheRosterEntry("reg-1", new byte[] { 1, 2, 3 });
        _cache.TryGetKey("reg-1", out var entry).Should().BeTrue();
        entry!.PublicKey.Should().BeEquivalentTo(new byte[] { 1, 2, 3 });
        entry.Algorithm.Should().Be("ED25519");
    }

    [Fact]
    public void Count_ReflectsEntries()
    {
        _cache.Count.Should().Be(0);
        CacheRosterEntry("reg-1", new byte[] { 1 });
        CacheRosterEntry("reg-2", new byte[] { 2 });
        _cache.Count.Should().Be(2);
    }

    [Fact]
    public void ExtractFromControlRecord_SameRegister_OverwritesPrevious()
    {
        CacheRosterEntry("reg-1", new byte[] { 1 });
        CacheRosterEntry("reg-1", new byte[] { 99 });

        _cache.Count.Should().Be(1);
        _cache.TryGetKey("reg-1", out var entry).Should().BeTrue();
        entry!.PublicKey.Should().BeEquivalentTo(new byte[] { 99 });
    }

    [Fact]
    public void IsAuthorizedSigner_MatchingKey_ReturnsTrue()
    {
        var key = new byte[] { 10, 20, 30 };
        CacheRosterEntry("reg-1", key);
        _cache.IsAuthorizedSigner("reg-1", key).Should().BeTrue();
    }

    [Fact]
    public void IsAuthorizedSigner_NonMatchingKey_ReturnsFalse()
    {
        CacheRosterEntry("reg-1", new byte[] { 10, 20, 30 });
        _cache.IsAuthorizedSigner("reg-1", new byte[] { 99, 88, 77 }).Should().BeFalse();
    }

    [Fact]
    public void IsAuthorizedSigner_UnknownRegister_ReturnsFalse()
    {
        _cache.IsAuthorizedSigner("unknown", new byte[] { 1 }).Should().BeFalse();
    }

    [Fact]
    public void IsAuthorizedSigner_MultipleKeys_MatchesAny()
    {
        var key1 = new byte[] { 1, 2, 3 };
        var key2 = new byte[] { 4, 5, 6 };
        CacheRosterWithMultipleEntries("reg-1", key1, key2);

        _cache.IsAuthorizedSigner("reg-1", key1).Should().BeTrue();
        _cache.IsAuthorizedSigner("reg-1", key2).Should().BeTrue();
        _cache.IsAuthorizedSigner("reg-1", new byte[] { 7, 8, 9 }).Should().BeFalse();
    }

    [Fact]
    public void ExtractFromControlRecord_Bytes_ValidRecord_CachesRoster()
    {
        var key = new byte[] { 10, 20, 30 };
        var record = CreateControlRecord(key);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(record);

        _cache.ExtractFromControlRecord("reg-1", bytes).Should().BeTrue();
        _cache.IsAuthorizedSigner("reg-1", key).Should().BeTrue();
    }

    [Fact]
    public void ExtractFromControlRecord_Bytes_NullValidators_ReturnsFalse()
    {
        var record = new RegisterControlRecord
        {
            RegisterId = "reg-1",
            Name = "Test",
            CreatedAt = DateTimeOffset.UtcNow,
            Attestations = [],
            Validators = null
        };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(record);

        _cache.ExtractFromControlRecord("reg-1", bytes).Should().BeFalse();
    }

    [Fact]
    public void ExtractFromControlRecord_RevokedKeys_Excluded()
    {
        var activeKey = new byte[] { 1, 2, 3 };
        var revokedKey = new byte[] { 4, 5, 6 };
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
                    CreateEntry("v1", activeKey, ValidatorKeyStatus.Active),
                    CreateEntry("v2", revokedKey, ValidatorKeyStatus.Revoked)
                ],
                RequiredSignatures = 1,
                Version = 1
            }
        };

        _cache.ExtractFromControlRecord("reg-1", record).Should().BeTrue();
        _cache.IsAuthorizedSigner("reg-1", activeKey).Should().BeTrue();
        _cache.IsAuthorizedSigner("reg-1", revokedKey).Should().BeFalse();
    }

    [Fact]
    public void ExtractFromControlRecord_RotatedKeys_Included()
    {
        var rotatedKey = new byte[] { 1, 2, 3 };
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
                    CreateEntry("v1", new byte[] { 4, 5, 6 }, ValidatorKeyStatus.Active),
                    CreateEntry("v2", rotatedKey, ValidatorKeyStatus.Rotated)
                ],
                RequiredSignatures = 1,
                Version = 1
            }
        };

        _cache.ExtractFromControlRecord("reg-1", record).Should().BeTrue();
        _cache.IsAuthorizedSigner("reg-1", rotatedKey).Should().BeTrue();
    }

    [Fact]
    public void GetRoster_ReturnsRosterMetadata()
    {
        CacheRosterEntry("reg-1", new byte[] { 1 });
        var roster = _cache.GetRoster("reg-1");
        roster.Should().NotBeNull();
        roster!.RequiredSignatures.Should().Be(1);
        roster.RosterVersion.Should().Be(1);
        roster.ResolvedFrom.Should().Be("genesis-control-record");
    }

    [Fact]
    public void ExtractFromGenesisDocket_ValidData_CachesKey()
    {
        var docketData = CreateGenesisDocketJson(
            publicKey: Convert.ToBase64String(new byte[] { 10, 20, 30 }),
            algorithm: "ED25519");

        var result = _cache.ExtractFromGenesisDocket("reg-1", Encoding.UTF8.GetBytes(docketData));

        result.Should().BeTrue();
        _cache.HasKey("reg-1").Should().BeTrue();
        _cache.TryGetKey("reg-1", out var entry).Should().BeTrue();
        entry!.Algorithm.Should().Be("ED25519");
        entry.PublicKey.Should().BeEquivalentTo(new byte[] { 10, 20, 30 });
        entry.ResolvedFrom.Should().Be("genesis-docket-legacy");
    }

    [Fact]
    public void ExtractFromGenesisDocket_MissingSignature_ReturnsFalse()
    {
        var docketData = JsonSerializer.Serialize(new { DocketNumber = 0, RegisterId = "reg-1" });
        _cache.ExtractFromGenesisDocket("reg-1", Encoding.UTF8.GetBytes(docketData)).Should().BeFalse();
    }

    [Fact]
    public void ExtractFromGenesisDocket_InvalidJson_ReturnsFalse()
    {
        _cache.ExtractFromGenesisDocket("reg-1", Encoding.UTF8.GetBytes("not json")).Should().BeFalse();
    }

    [Fact]
    public void ExtractFromGenesisDocket_IncompleteSignature_ReturnsFalse()
    {
        var docketData = JsonSerializer.Serialize(new
        {
            ProposerSignature = new
            {
                PublicKey = Convert.ToBase64String(new byte[] { 1, 2, 3 })
            }
        });
        _cache.ExtractFromGenesisDocket("reg-1", Encoding.UTF8.GetBytes(docketData)).Should().BeFalse();
    }

    // --- Helpers ---

    private void CacheRosterEntry(string registerId, byte[] publicKey)
    {
        var record = CreateControlRecord(publicKey);
        _cache.ExtractFromControlRecord(registerId, record);
    }

    private void CacheRosterWithMultipleEntries(string registerId, byte[] key1, byte[] key2)
    {
        var record = new RegisterControlRecord
        {
            RegisterId = registerId,
            Name = "Test",
            CreatedAt = DateTimeOffset.UtcNow,
            Attestations = [],
            Validators = new ValidatorRoster
            {
                Validators =
                [
                    CreateEntry("v1", key1, ValidatorKeyStatus.Active),
                    CreateEntry("v2", key2, ValidatorKeyStatus.Active)
                ],
                RequiredSignatures = 1,
                Version = 1
            }
        };
        _cache.ExtractFromControlRecord(registerId, record);
    }

    private static RegisterControlRecord CreateControlRecord(byte[] publicKey) => new()
    {
        RegisterId = "reg-1",
        Name = "Test",
        CreatedAt = DateTimeOffset.UtcNow,
        Attestations = [],
        Validators = new ValidatorRoster
        {
            Validators = [CreateEntry("v1", publicKey, ValidatorKeyStatus.Active)],
            RequiredSignatures = 1,
            Version = 1
        }
    };

    private static ValidatorRosterEntry CreateEntry(string id, byte[] key, ValidatorKeyStatus status) => new()
    {
        ValidatorId = id,
        PublicKey = Convert.ToBase64String(key),
        Algorithm = SignatureAlgorithm.ED25519,
        DerivationContext = "sorcha:docket-signing",
        Status = status,
        AuthorizedAt = DateTimeOffset.UtcNow
    };

    private static string CreateGenesisDocketJson(string publicKey, string algorithm)
    {
        return JsonSerializer.Serialize(new
        {
            DocketNumber = 0,
            RegisterId = "reg-1",
            ProposerSignature = new
            {
                PublicKey = publicKey,
                Algorithm = algorithm,
                SignatureValue = Convert.ToBase64String(new byte[] { 99, 88, 77 }),
                SignedAt = DateTimeOffset.UtcNow
            }
        });
    }
}
