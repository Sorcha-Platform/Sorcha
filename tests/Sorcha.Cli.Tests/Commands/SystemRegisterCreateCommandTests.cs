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

    /// <summary>
    /// The genesis ceremony's validator key is private material whose mnemonic controls EVERY key
    /// derived from the genesis wallet. With no explicit --output it used to land in the process's
    /// current directory, which for the documented workflow is the repo root — and a
    /// <c>genesis-validator-key.json.bak-pre471</c> duly sat there untracked for weeks, one
    /// <c>git add -A</c> away from being published.
    ///
    /// <para>It now resolves into the repo's gitignored <c>/temp</c>. The repo is located by walking
    /// up for the same marker the genesis-file default uses, so it does not depend on where the CLI
    /// was invoked from within the checkout.</para>
    /// </summary>
    [Fact]
    public void ResolveValidatorKeyPath_InsideARepo_WritesIntoGitignoredTemp()
    {
        // A checkout is identified by this marker directory, mirroring FindDefaultGenesisPath.
        var repoRoot = Path.Combine(_tempDir, "checkout");
        var invokedFrom = Path.Combine(repoRoot, "some", "nested", "dir");
        Directory.CreateDirectory(Path.Combine(repoRoot, "src", "Common", "Sorcha.Register.Models"));
        Directory.CreateDirectory(invokedFrom);

        var resolved = SystemRegisterCreateCommand.ResolveValidatorKeyPath(
            outputPath: null, workingDirectory: invokedFrom);

        resolved.Should().Be(
            Path.Combine(repoRoot, "temp", "genesis-validator-key.json"),
            "private key material belongs in the gitignored /temp, not the repo root");
    }

    /// <summary>
    /// The CLI ships as a global tool and can be run from anywhere. Outside a checkout there is no
    /// /temp to write to, so it must fall back to the working directory rather than inventing one.
    /// </summary>
    [Fact]
    public void ResolveValidatorKeyPath_OutsideARepo_FallsBackToWorkingDirectory()
    {
        var elsewhere = Path.Combine(_tempDir, "not-a-checkout");
        Directory.CreateDirectory(elsewhere);

        var resolved = SystemRegisterCreateCommand.ResolveValidatorKeyPath(
            outputPath: null, workingDirectory: elsewhere);

        resolved.Should().Be(Path.Combine(elsewhere, "genesis-validator-key.json"));
    }

    /// <summary>
    /// An explicit --output keeps the key adjacent to the genesis file — the behaviour the existing
    /// tests and any scripted ceremony depend on. Only the DEFAULT moved.
    /// </summary>
    [Fact]
    public void ResolveValidatorKeyPath_WithExplicitOutput_StaysAdjacentToGenesis()
    {
        var outputPath = Path.Combine(_tempDir, "custom", "genesis.json");

        var resolved = SystemRegisterCreateCommand.ResolveValidatorKeyPath(
            outputPath, workingDirectory: _tempDir);

        resolved.Should().Be(Path.Combine(_tempDir, "custom", "genesis-validator-key.json"));
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
