// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using Sorcha.Cli.Commands;
using Sorcha.Register.Models.Genesis;

namespace Sorcha.Cli.Tests.Commands;

public class SystemRegisterVerifyCommandTests : IDisposable
{
    private readonly string _tempDir;

    public SystemRegisterVerifyCommandTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"genesis-verify-test-{Guid.NewGuid():N}");
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
    public async Task Verify_ValidGenesis_ReturnsSuccess()
    {
        var genesisPath = await CreateValidGenesis();

        var exitCode = await SystemRegisterVerifyCommand.ExecuteVerifyAsync(
            genesisPath, CancellationToken.None);

        exitCode.Should().Be(ExitCodes.Success);
    }

    [Fact]
    public async Task Verify_TamperedPayload_ReturnsFailure()
    {
        var genesisPath = await CreateValidGenesis();

        // Tamper with the payload hash (so structural validation catches it)
        var json = File.ReadAllText(genesisPath);
        var genesis = JsonSerializer.Deserialize<SystemRegisterGenesis>(
            json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        genesis.GenesisTransaction.PayloadHash = "0000000000000000000000000000000000000000000000000000000000000000";

        File.WriteAllText(genesisPath, JsonSerializer.Serialize(genesis,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true }));

        var exitCode = await SystemRegisterVerifyCommand.ExecuteVerifyAsync(
            genesisPath, CancellationToken.None);

        exitCode.Should().NotBe(ExitCodes.Success);
    }

    [Fact]
    public async Task Verify_TamperedSignature_ReturnsFailure()
    {
        var genesisPath = await CreateValidGenesis();

        // Tamper with the signature value (structural validation passes, crypto fails)
        var json = File.ReadAllText(genesisPath);
        var genesis = JsonSerializer.Deserialize<SystemRegisterGenesis>(
            json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        var sigBytes = Convert.FromBase64String(genesis.GenesisTransaction.Signature.SignatureValue);
        sigBytes[0] ^= 0xFF;
        genesis.GenesisTransaction.Signature.SignatureValue = Convert.ToBase64String(sigBytes);

        File.WriteAllText(genesisPath, JsonSerializer.Serialize(genesis,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true }));

        var exitCode = await SystemRegisterVerifyCommand.ExecuteVerifyAsync(
            genesisPath, CancellationToken.None);

        exitCode.Should().NotBe(ExitCodes.Success);
    }

    [Fact]
    public async Task Verify_MissingFile_ReturnsValidationError()
    {
        var exitCode = await SystemRegisterVerifyCommand.ExecuteVerifyAsync(
            Path.Combine(_tempDir, "nonexistent.json"), CancellationToken.None);

        exitCode.Should().Be(ExitCodes.ValidationError);
    }

    [Fact]
    public async Task Verify_InvalidJson_ReturnsValidationError()
    {
        var path = Path.Combine(_tempDir, "bad.json");
        File.WriteAllText(path, "not valid json");

        var exitCode = await SystemRegisterVerifyCommand.ExecuteVerifyAsync(
            path, CancellationToken.None);

        exitCode.Should().Be(ExitCodes.ValidationError);
    }

    [Fact]
    public async Task Verify_EmptyPlaceholder_ReturnsValidationError()
    {
        var path = Path.Combine(_tempDir, "empty.json");
        File.WriteAllText(path, "{}");

        var exitCode = await SystemRegisterVerifyCommand.ExecuteVerifyAsync(
            path, CancellationToken.None);

        exitCode.Should().Be(ExitCodes.ValidationError);
    }

    private async Task<string> CreateValidGenesis()
    {
        var outputPath = Path.Combine(_tempDir, $"genesis-{Guid.NewGuid():N}.json");
        await SystemRegisterCreateCommand.ExecuteCreateAsync(
            "test-verify", outputPath, "ED25519", CancellationToken.None);
        return outputPath;
    }
}
