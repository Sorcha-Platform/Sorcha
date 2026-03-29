// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Sorcha.Peer.Service.Replication;

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
    public void CacheKey_ThenTryGetKey_ReturnsEntry()
    {
        var entry = new ValidatorKeyEntry
        {
            RegisterId = "reg-1",
            PublicKey = new byte[] { 1, 2, 3 },
            Algorithm = "ED25519",
            ResolvedFrom = "test",
            ResolvedAt = DateTimeOffset.UtcNow
        };

        _cache.CacheKey(entry);

        _cache.TryGetKey("reg-1", out var retrieved).Should().BeTrue();
        retrieved!.PublicKey.Should().BeEquivalentTo(new byte[] { 1, 2, 3 });
        retrieved.Algorithm.Should().Be("ED25519");
    }

    [Fact]
    public void HasKey_AfterCache_ReturnsTrue()
    {
        _cache.HasKey("reg-1").Should().BeFalse();

        _cache.CacheKey(new ValidatorKeyEntry
        {
            RegisterId = "reg-1",
            PublicKey = new byte[] { 1 },
            Algorithm = "ED25519",
            ResolvedFrom = "test",
            ResolvedAt = DateTimeOffset.UtcNow
        });

        _cache.HasKey("reg-1").Should().BeTrue();
    }

    [Fact]
    public void Count_ReflectsEntries()
    {
        _cache.Count.Should().Be(0);

        _cache.CacheKey(new ValidatorKeyEntry
        {
            RegisterId = "reg-1",
            PublicKey = new byte[] { 1 },
            Algorithm = "ED25519",
            ResolvedFrom = "test",
            ResolvedAt = DateTimeOffset.UtcNow
        });

        _cache.CacheKey(new ValidatorKeyEntry
        {
            RegisterId = "reg-2",
            PublicKey = new byte[] { 2 },
            Algorithm = "NISTP256",
            ResolvedFrom = "test",
            ResolvedAt = DateTimeOffset.UtcNow
        });

        _cache.Count.Should().Be(2);
    }

    [Fact]
    public void CacheKey_SameRegister_OverwritesPrevious()
    {
        _cache.CacheKey(new ValidatorKeyEntry
        {
            RegisterId = "reg-1",
            PublicKey = new byte[] { 1 },
            Algorithm = "ED25519",
            ResolvedFrom = "genesis",
            ResolvedAt = DateTimeOffset.UtcNow
        });

        _cache.CacheKey(new ValidatorKeyEntry
        {
            RegisterId = "reg-1",
            PublicKey = new byte[] { 99 },
            Algorithm = "NISTP256",
            ResolvedFrom = "wallet-service",
            ResolvedAt = DateTimeOffset.UtcNow
        });

        _cache.Count.Should().Be(1);
        _cache.TryGetKey("reg-1", out var entry).Should().BeTrue();
        entry!.Algorithm.Should().Be("NISTP256");
        entry.PublicKey.Should().BeEquivalentTo(new byte[] { 99 });
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
        entry.ResolvedFrom.Should().Be("genesis-docket");
    }

    [Fact]
    public void ExtractFromGenesisDocket_MissingSignature_ReturnsFalse()
    {
        var docketData = JsonSerializer.Serialize(new { DocketNumber = 0, RegisterId = "reg-1" });

        var result = _cache.ExtractFromGenesisDocket("reg-1", Encoding.UTF8.GetBytes(docketData));

        result.Should().BeFalse();
        _cache.HasKey("reg-1").Should().BeFalse();
    }

    [Fact]
    public void ExtractFromGenesisDocket_InvalidJson_ReturnsFalse()
    {
        var result = _cache.ExtractFromGenesisDocket("reg-1", Encoding.UTF8.GetBytes("not json"));

        result.Should().BeFalse();
        _cache.HasKey("reg-1").Should().BeFalse();
    }

    [Fact]
    public void ExtractFromGenesisDocket_IncompleteSignature_ReturnsFalse()
    {
        // Has ProposerSignature but missing Algorithm
        var docketData = JsonSerializer.Serialize(new
        {
            ProposerSignature = new
            {
                PublicKey = Convert.ToBase64String(new byte[] { 1, 2, 3 })
                // Missing Algorithm
            }
        });

        var result = _cache.ExtractFromGenesisDocket("reg-1", Encoding.UTF8.GetBytes(docketData));

        result.Should().BeFalse();
    }

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
