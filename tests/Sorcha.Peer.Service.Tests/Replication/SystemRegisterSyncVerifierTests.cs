// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Sorcha.Cryptography.Interfaces;
using Sorcha.Peer.Service.Replication;
using Sorcha.Register.Models.Constants;
using Sorcha.ServiceDefaults;
using Xunit;

namespace Sorcha.Peer.Service.Tests.Replication;

public class SystemRegisterSyncVerifierTests : IDisposable
{
    private readonly Mock<ICryptoModule> _cryptoModule = new();
    private readonly Mock<ILogger<SystemRegisterSyncVerifier>> _logger = new();
    private readonly List<string> _tempDirs = new();

    public void Dispose()
    {
        foreach (var dir in _tempDirs)
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
            catch { /* best effort */ }
        }
    }

    [Fact]
    public void IsSystemRegister_WithSystemRegisterId_ReturnsTrue()
    {
        var verifier = CreateVerifier();
        verifier.IsSystemRegister(SystemRegisterConstants.SystemRegisterId).Should().BeTrue();
    }

    [Fact]
    public void IsSystemRegister_WithOtherRegisterId_ReturnsFalse()
    {
        var verifier = CreateVerifier();
        verifier.IsSystemRegister("some-other-register-id").Should().BeFalse();
    }

    [Fact]
    public async Task VerifyGenesis_NonSystemRegister_ReturnsTrue()
    {
        var verifier = CreateVerifier();
        var docket = CreateEmptyDocket("other-register");

        var result = await verifier.VerifySystemRegisterGenesisAsync(
            "other-register", docket, CancellationToken.None);

        result.Should().BeTrue(); // Non-system registers bypass verification
    }

    [Fact]
    public async Task VerifyGenesis_NoTrustedGenesis_ReturnsFalse()
    {
        // No genesis file configured, embedded is placeholder
        var verifier = CreateVerifier(genesisFile: null);
        var docket = CreateEmptyDocket(SystemRegisterConstants.SystemRegisterId);

        var result = await verifier.VerifySystemRegisterGenesisAsync(
            SystemRegisterConstants.SystemRegisterId, docket, CancellationToken.None);

        result.Should().BeFalse(); // Can't verify without trusted genesis
    }

    [Fact]
    public async Task VerifyGenesis_EmptyDocket_ReturnsFalse()
    {
        var verifier = CreateVerifierWithGenesis();
        var docket = CreateEmptyDocket(SystemRegisterConstants.SystemRegisterId);

        var result = await verifier.VerifySystemRegisterGenesisAsync(
            SystemRegisterConstants.SystemRegisterId, docket, CancellationToken.None);

        result.Should().BeFalse(); // Can't extract control transaction from empty docket
    }

    [Fact]
    public async Task VerifyGenesis_DocketWithCachedControlTx_ReturnsTrue()
    {
        // Replicates the production wire shape: docket carries TransactionIds only,
        // full TransactionModel JSON lives in the per-transaction cache. Verifier
        // must look up the Control transaction by ID, decode the payload, and verify
        // the signature against the trusted genesis pubkey.
        var registerId = SystemRegisterConstants.SystemRegisterId;
        var (verifier, cache) = CreateVerifierWithGenesisAndCache();

        // Stage a Control transaction in the cache that references the same payload
        // and signature the trusted genesis was built from.
        var txId = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
        var controlTx = new Sorcha.Register.Models.TransactionModel
        {
            RegisterId = registerId,
            TxId = txId,
            SenderWallet = "system",
            Signature = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
            MetaData = new Sorcha.Register.Models.TransactionMetaData
            {
                RegisterId = registerId,
                TransactionType = Sorcha.Register.Models.Enums.TransactionType.Control
            },
            Payloads = new[]
            {
                new Sorcha.Register.Models.PayloadModel
                {
                    Data = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("{\"registerId\":\"test\"}")),
                    Hash = "deadbeef",
                    ContentEncoding = "base64url"
                }
            }
        };
        var txJson = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(
            controlTx, Sorcha.Register.Models.RegisterSerializationOptions.Canonical);

        cache.GetOrCreate(registerId).AddOrUpdateTransaction(new CachedTransaction
        {
            RegisterId = registerId,
            TransactionId = txId,
            Data = txJson
        });

        var docket = new CachedDocket
        {
            RegisterId = registerId,
            Version = 0,
            Data = Array.Empty<byte>(),
            DocketHash = "h",
            TransactionIds = new List<string> { txId }
        };

        // Crypto module just rubber-stamps — we're testing the lookup + plumbing,
        // not the signature math (covered by GenesisSignatureVerifierTests).
        _cryptoModule
            .Setup(c => c.VerifyAsync(
                It.IsAny<byte[]>(), It.IsAny<byte[]>(), It.IsAny<byte>(),
                It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Sorcha.Cryptography.Enums.CryptoStatus.Success);

        var result = await verifier.VerifySystemRegisterGenesisAsync(
            registerId, docket, CancellationToken.None);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task VerifyGenesis_DocketReferencesUncachedTx_ReturnsFalse()
    {
        // Sync race: docket arrived before transactions did. Don't crash, don't
        // accept — log and reject so the next iteration can try again.
        var registerId = SystemRegisterConstants.SystemRegisterId;
        var (verifier, _) = CreateVerifierWithGenesisAndCache();

        var docket = new CachedDocket
        {
            RegisterId = registerId,
            Version = 0,
            Data = Array.Empty<byte>(),
            DocketHash = "h",
            TransactionIds = new List<string> { "deadbeef" }
        };

        var result = await verifier.VerifySystemRegisterGenesisAsync(
            registerId, docket, CancellationToken.None);

        result.Should().BeFalse();
    }

    private SystemRegisterSyncVerifier CreateVerifier(string? genesisFile = null)
    {
        var options = Options.Create(new SystemRegisterOptions { GenesisFile = genesisFile });
        var cache = new RegisterCache(new Mock<ILogger<RegisterCache>>().Object);
        return new SystemRegisterSyncVerifier(options, _cryptoModule.Object, cache, _logger.Object);
    }

    private SystemRegisterSyncVerifier CreateVerifierWithGenesis()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"sync-verifier-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        _tempDirs.Add(tempDir);
        var genesisPath = Path.Combine(tempDir, "genesis.json");

        var publicKey = new byte[32];
        Array.Fill(publicKey, (byte)0x01);
        var fingerprint = Register.Models.Genesis.GenesisFileLoader.ComputeFingerprint(publicKey);

        var payload = System.Text.Encoding.UTF8.GetBytes("{\"registerId\":\"test\"}");
        var payloadHash = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(payload)).ToLowerInvariant();

        var genesis = new Register.Models.Genesis.SystemRegisterGenesis
        {
            Version = 1,
            NetworkId = "test",
            GenesisTransaction = new Register.Models.Genesis.GenesisTransactionData
            {
                TxId = Register.Models.Genesis.GenesisSignatureVerifier.ComputeGenesisTxId(),
                Payload = Convert.ToBase64String(payload),
                PayloadHash = payloadHash,
                Signature = new Register.Models.Genesis.GenesisSignature
                {
                    PublicKey = Convert.ToBase64String(publicKey),
                    SignatureValue = Convert.ToBase64String(new byte[64]),
                    Algorithm = "ED25519",
                    SignedAt = DateTimeOffset.UtcNow
                }
            },
            ValidatorRoster = new Register.Models.ValidatorRoster
            {
                Validators =
                [
                    new Register.Models.ValidatorRosterEntry
                    {
                        ValidatorId = "test",
                        PublicKey = Convert.ToBase64String(publicKey),
                        Algorithm = Register.Models.SignatureAlgorithm.ED25519,
                        DerivationContext = "sorcha:docket-signing",
                        Status = Register.Models.ValidatorKeyStatus.Active,
                        AuthorizedAt = DateTimeOffset.UtcNow
                    }
                ],
                RequiredSignatures = 1,
                Version = 1
            },
            GenesisPublicKeyFingerprint = fingerprint
        };

        var json = System.Text.Json.JsonSerializer.Serialize(genesis,
            new System.Text.Json.JsonSerializerOptions
            {
                PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
                WriteIndented = true
            });
        File.WriteAllText(genesisPath, json);

        var options = Options.Create(new SystemRegisterOptions { GenesisFile = genesisPath });
        var cache = new RegisterCache(new Mock<ILogger<RegisterCache>>().Object);
        return new SystemRegisterSyncVerifier(options, _cryptoModule.Object, cache, _logger.Object);
    }

    private (SystemRegisterSyncVerifier Verifier, RegisterCache Cache) CreateVerifierWithGenesisAndCache()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"sync-verifier-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        _tempDirs.Add(tempDir);
        var genesisPath = Path.Combine(tempDir, "genesis.json");

        var publicKey = new byte[32];
        Array.Fill(publicKey, (byte)0x01);
        var fingerprint = Register.Models.Genesis.GenesisFileLoader.ComputeFingerprint(publicKey);

        var payload = System.Text.Encoding.UTF8.GetBytes("{\"registerId\":\"test\"}");
        var payloadHash = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(payload)).ToLowerInvariant();

        var genesis = new Register.Models.Genesis.SystemRegisterGenesis
        {
            Version = 1,
            NetworkId = "test",
            GenesisTransaction = new Register.Models.Genesis.GenesisTransactionData
            {
                TxId = Register.Models.Genesis.GenesisSignatureVerifier.ComputeGenesisTxId(),
                Payload = Convert.ToBase64String(payload),
                PayloadHash = payloadHash,
                Signature = new Register.Models.Genesis.GenesisSignature
                {
                    PublicKey = Convert.ToBase64String(publicKey),
                    SignatureValue = Convert.ToBase64String(new byte[64]),
                    Algorithm = "ED25519",
                    SignedAt = DateTimeOffset.UtcNow
                }
            },
            ValidatorRoster = new Register.Models.ValidatorRoster
            {
                Validators =
                [
                    new Register.Models.ValidatorRosterEntry
                    {
                        ValidatorId = "test",
                        PublicKey = Convert.ToBase64String(publicKey),
                        Algorithm = Register.Models.SignatureAlgorithm.ED25519,
                        DerivationContext = "sorcha:docket-signing",
                        Status = Register.Models.ValidatorKeyStatus.Active,
                        AuthorizedAt = DateTimeOffset.UtcNow
                    }
                ],
                RequiredSignatures = 1,
                Version = 1
            },
            GenesisPublicKeyFingerprint = fingerprint
        };
        File.WriteAllText(genesisPath, System.Text.Json.JsonSerializer.Serialize(genesis,
            new System.Text.Json.JsonSerializerOptions
            {
                PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
                WriteIndented = true
            }));

        var options = Options.Create(new SystemRegisterOptions { GenesisFile = genesisPath });
        var cache = new RegisterCache(new Mock<ILogger<RegisterCache>>().Object);
        var verifier = new SystemRegisterSyncVerifier(options, _cryptoModule.Object, cache, _logger.Object);
        return (verifier, cache);
    }

    private static CachedDocket CreateEmptyDocket(string registerId)
    {
        return new CachedDocket
        {
            RegisterId = registerId,
            Version = 0,
            Data = Array.Empty<byte>(),
            DocketHash = "empty",
            TransactionIds = new List<string>()
        };
    }
}
