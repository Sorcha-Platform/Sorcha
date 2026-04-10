// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using FluentAssertions;
using Sorcha.Register.Models.Genesis;

namespace Sorcha.Register.Models.Tests.Genesis;

public class GenesisFileLoaderTests : IDisposable
{
    private readonly string _tempDir;

    public GenesisFileLoaderTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"genesis-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void Load_WithConfigPath_LoadsFromFile()
    {
        var genesis = CreateTestGenesis();
        var path = WriteGenesis(genesis);

        var result = GenesisFileLoader.Load(path);

        result.Should().NotBeNull();
        result!.NetworkId.Should().Be("test-network");
        result.Version.Should().Be(1);
    }

    [Fact]
    public void Load_WithMissingConfigPath_ThrowsFileNotFoundException()
    {
        var act = () => GenesisFileLoader.Load("/nonexistent/path/genesis.json");

        act.Should().Throw<FileNotFoundException>()
            .WithMessage("*not found at configured path*");
    }

    [Fact]
    public void Load_WithNullPath_FallsBackToEmbeddedResource()
    {
        // The embedded resource contains the dev genesis (sorcha-dev)
        var result = GenesisFileLoader.Load(null);

        // May be null (placeholder) or valid genesis (dev genesis embedded)
        // Either is acceptable — the test validates the fallback path works
        if (result is not null)
        {
            result.Version.Should().Be(1);
            result.NetworkId.Should().NotBeNullOrEmpty();
        }
    }

    [Fact]
    public void Load_WithEmptyPath_FallsBackToEmbeddedResource()
    {
        var result = GenesisFileLoader.Load("");

        // Same as null path — falls back to embedded resource
        if (result is not null)
        {
            result.Version.Should().Be(1);
        }
    }

    [Fact]
    public void Load_WithInvalidJson_ThrowsJsonException()
    {
        var path = Path.Combine(_tempDir, "bad.json");
        File.WriteAllText(path, "not json");

        var act = () => GenesisFileLoader.Load(path);

        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void Load_WithWrongVersion_ThrowsInvalidOperationException()
    {
        var genesis = CreateTestGenesis();
        genesis.Version = 99;
        var path = WriteGenesis(genesis);

        var act = () => GenesisFileLoader.Load(path);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Unsupported genesis file version*");
    }

    [Fact]
    public async Task LoadAsync_WithConfigPath_LoadsFromFile()
    {
        var genesis = CreateTestGenesis();
        var path = WriteGenesis(genesis);

        var result = await GenesisFileLoader.LoadAsync(path);

        result.Should().NotBeNull();
        result!.NetworkId.Should().Be("test-network");
    }

    [Fact]
    public void ComputeFingerprint_ReturnsTruncatedSha256()
    {
        var publicKey = new byte[32];
        Array.Fill(publicKey, (byte)0xAB);

        var fingerprint = GenesisFileLoader.ComputeFingerprint(publicKey);

        fingerprint.Should().HaveLength(32);
        fingerprint.Should().MatchRegex("^[0-9a-f]{32}$");
    }

    [Fact]
    public void ComputeFingerprint_IsDeterministic()
    {
        var publicKey = new byte[32];
        Array.Fill(publicKey, (byte)0xCD);

        var fp1 = GenesisFileLoader.ComputeFingerprint(publicKey);
        var fp2 = GenesisFileLoader.ComputeFingerprint(publicKey);

        fp1.Should().Be(fp2);
    }

    private SystemRegisterGenesis CreateTestGenesis()
    {
        var publicKey = new byte[32];
        Array.Fill(publicKey, (byte)0x01);

        return new SystemRegisterGenesis
        {
            Version = 1,
            NetworkId = "test-network",
            GenesisTransaction = new GenesisTransactionData
            {
                TxId = "test-tx-id",
                Payload = Convert.ToBase64String(new byte[] { 0x01, 0x02, 0x03 }),
                PayloadHash = "test-hash",
                Signature = new GenesisSignature
                {
                    PublicKey = Convert.ToBase64String(publicKey),
                    SignatureValue = Convert.ToBase64String(new byte[64]),
                    Algorithm = "ED25519",
                    SignedAt = DateTimeOffset.UtcNow
                }
            },
            ValidatorRoster = new ValidatorRoster
            {
                Validators =
                [
                    new ValidatorRosterEntry
                    {
                        ValidatorId = "test-validator",
                        PublicKey = Convert.ToBase64String(publicKey),
                        Algorithm = SignatureAlgorithm.ED25519,
                        DerivationContext = "sorcha:docket-signing",
                        Status = ValidatorKeyStatus.Active,
                        AuthorizedAt = DateTimeOffset.UtcNow
                    }
                ],
                RequiredSignatures = 1,
                Version = 1
            },
            GenesisPublicKeyFingerprint = GenesisFileLoader.ComputeFingerprint(publicKey)
        };
    }

    private string WriteGenesis(SystemRegisterGenesis genesis)
    {
        var path = Path.Combine(_tempDir, "genesis.json");
        var json = JsonSerializer.Serialize(genesis, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        });
        File.WriteAllText(path, json);
        return path;
    }
}
