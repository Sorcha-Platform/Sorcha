// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using Sorcha.Cli.Commands;
using Sorcha.Register.Models.Constants;
using Sorcha.Register.Models.Genesis;

namespace Sorcha.Cli.Tests.Commands;

public class SystemRegisterCreateCommandTests : IDisposable
{
    private readonly string _tempDir;

    public SystemRegisterCreateCommandTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"genesis-create-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }
        catch { /* best effort cleanup */ }
    }

    [Fact]
    public async Task ExecuteCreate_ProducesValidGenesisFile()
    {
        var outputPath = Path.Combine(_tempDir, "genesis.json");

        var exitCode = await SystemRegisterCreateCommand.ExecuteCreateAsync(
            "test-network", outputPath, "ED25519", CancellationToken.None);

        exitCode.Should().Be(ExitCodes.Success);
        File.Exists(outputPath).Should().BeTrue();

        var genesis = JsonSerializer.Deserialize<SystemRegisterGenesis>(
            File.ReadAllText(outputPath),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        genesis.Should().NotBeNull();
        genesis!.Version.Should().Be(1);
        genesis.NetworkId.Should().Be("test-network");
        genesis.GenesisPublicKeyFingerprint.Should().HaveLength(32);
    }

    [Fact]
    public async Task ExecuteCreate_ProducesValidatorKeyFile()
    {
        var outputPath = Path.Combine(_tempDir, "genesis.json");

        await SystemRegisterCreateCommand.ExecuteCreateAsync(
            "test-network", outputPath, "ED25519", CancellationToken.None);

        // Key file is written adjacent to genesis when --output is specified
        var keyFilePath = Path.Combine(_tempDir, "genesis-validator-key.json");

        var keyFile = JsonSerializer.Deserialize<GenesisValidatorKeyFile>(
            File.ReadAllText(keyFilePath),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        keyFile.Should().NotBeNull();
        keyFile!.Version.Should().Be(1);
        keyFile.NetworkId.Should().Be("test-network");
        keyFile.Algorithm.Should().Be("ED25519");

        // Key file cleaned up by Dispose
    }

    [Fact]
    public async Task ExecuteCreate_UsesDeterministicRegisterId()
    {
        var outputPath = Path.Combine(_tempDir, "genesis.json");

        await SystemRegisterCreateCommand.ExecuteCreateAsync(
            "test-network", outputPath, "ED25519", CancellationToken.None);

        var genesis = JsonSerializer.Deserialize<SystemRegisterGenesis>(
            File.ReadAllText(outputPath),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        genesis!.GenesisTransaction.TxId.Should().Be(
            GenesisSignatureVerifier.ComputeGenesisTxId());
    }

    [Fact]
    public async Task ExecuteCreate_UniqueKeysPerCeremony()
    {
        var path1 = Path.Combine(_tempDir, "genesis1.json");
        var path2 = Path.Combine(_tempDir, "genesis2.json");

        await SystemRegisterCreateCommand.ExecuteCreateAsync(
            "net1", path1, "ED25519", CancellationToken.None);
        await SystemRegisterCreateCommand.ExecuteCreateAsync(
            "net2", path2, "ED25519", CancellationToken.None);

        var g1 = JsonSerializer.Deserialize<SystemRegisterGenesis>(
            File.ReadAllText(path1),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        var g2 = JsonSerializer.Deserialize<SystemRegisterGenesis>(
            File.ReadAllText(path2),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        // Same deterministic TxId
        g1!.GenesisTransaction.TxId.Should().Be(g2!.GenesisTransaction.TxId);

        // Different keys and signatures
        g1.GenesisPublicKeyFingerprint.Should().NotBe(g2.GenesisPublicKeyFingerprint);
    }

    [Fact]
    public async Task ExecuteCreate_InvalidAlgorithm_ReturnsValidationError()
    {
        var outputPath = Path.Combine(_tempDir, "genesis.json");

        var exitCode = await SystemRegisterCreateCommand.ExecuteCreateAsync(
            "test-network", outputPath, "INVALID", CancellationToken.None);

        exitCode.Should().Be(ExitCodes.ValidationError);
    }

    [Fact]
    public async Task ExecuteCreate_GenesisPassesStructuralValidation()
    {
        var outputPath = Path.Combine(_tempDir, "genesis.json");

        await SystemRegisterCreateCommand.ExecuteCreateAsync(
            "test-network", outputPath, "ED25519", CancellationToken.None);

        var genesis = GenesisFileLoader.Load(outputPath);
        genesis.Should().NotBeNull();

        var errors = GenesisSignatureVerifier.ValidateStructure(genesis!);
        errors.Should().BeEmpty();
    }
}
