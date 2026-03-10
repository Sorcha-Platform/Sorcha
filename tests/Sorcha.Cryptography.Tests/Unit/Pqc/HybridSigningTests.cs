// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Diagnostics;
using FluentAssertions;
using Sorcha.Cryptography.Core;
using Sorcha.Cryptography.Enums;
using Sorcha.Cryptography.Models;
using Xunit;

namespace Sorcha.Cryptography.Tests.Unit.Pqc;

/// <summary>
/// Tests for hybrid (classical + PQC) signing — concurrent signature production.
/// </summary>
public class HybridSigningTests
{
    private readonly CryptoModule _cryptoModule = new();

    [Fact]
    public async Task HybridSign_ShouldProduceBothSignatures()
    {
        // Arrange — generate classical + PQC keys
        var classicalKeySet = (await _cryptoModule.GenerateKeySetAsync(WalletNetworks.ED25519)).Value;
        var pqcKeySet = (await _cryptoModule.GenerateKeySetAsync(WalletNetworks.ML_DSA_65)).Value;
        var data = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes("hybrid signing test"));

        // Act — sign with both concurrently
        var classicalSignTask = _cryptoModule.SignAsync(data, (byte)WalletNetworks.ED25519, classicalKeySet.PrivateKey.Key!);
        var pqcSignTask = _cryptoModule.SignAsync(data, (byte)WalletNetworks.ML_DSA_65, pqcKeySet.PrivateKey.Key!);
        await Task.WhenAll(classicalSignTask, pqcSignTask);

        var classicalSig = await classicalSignTask;
        var pqcSig = await pqcSignTask;

        // Assert
        classicalSig.IsSuccess.Should().BeTrue();
        pqcSig.IsSuccess.Should().BeTrue();

        // Assemble HybridSignature
        var hybrid = new HybridSignature
        {
            Classical = Convert.ToBase64String(classicalSig.Value!),
            ClassicalAlgorithm = "ED25519",
            Pqc = Convert.ToBase64String(pqcSig.Value!),
            PqcAlgorithm = "ML-DSA-65",
            WitnessPublicKey = Convert.ToBase64String(pqcKeySet.PublicKey.Key!)
        };

        hybrid.IsValid().Should().BeTrue();
        hybrid.Classical.Should().NotBeNullOrEmpty();
        hybrid.Pqc.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task HybridSign_ShouldCompleteWithin500ms()
    {
        // Arrange
        var classicalKeySet = (await _cryptoModule.GenerateKeySetAsync(WalletNetworks.ED25519)).Value;
        var pqcKeySet = (await _cryptoModule.GenerateKeySetAsync(WalletNetworks.ML_DSA_65)).Value;
        var data = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes("performance test"));

        // Act — time the concurrent signing
        var sw = Stopwatch.StartNew();
        var classicalSignTask = _cryptoModule.SignAsync(data, (byte)WalletNetworks.ED25519, classicalKeySet.PrivateKey.Key!);
        var pqcSignTask = _cryptoModule.SignAsync(data, (byte)WalletNetworks.ML_DSA_65, pqcKeySet.PrivateKey.Key!);
        await Task.WhenAll(classicalSignTask, pqcSignTask);
        sw.Stop();

        // Assert — SC-006: per-operation under 500ms
        sw.ElapsedMilliseconds.Should().BeLessThan(500,
            "hybrid signing should complete within 500ms per SC-006");
    }

    [Fact]
    public async Task HybridSignAsync_ShouldProduceValidHybridSignature()
    {
        // Arrange
        var classicalKeySet = (await _cryptoModule.GenerateKeySetAsync(WalletNetworks.ED25519)).Value;
        var pqcKeySet = (await _cryptoModule.GenerateKeySetAsync(WalletNetworks.ML_DSA_65)).Value;
        var data = System.Security.Cryptography.SHA256.HashData("hybrid sign async test"u8.ToArray());

        // Act — use the convenience method
        var result = await _cryptoModule.HybridSignAsync(
            data,
            (byte)WalletNetworks.ED25519, classicalKeySet.PrivateKey.Key!,
            (byte)WalletNetworks.ML_DSA_65, pqcKeySet.PrivateKey.Key!, pqcKeySet.PublicKey.Key!);

        // Assert
        result.IsSuccess.Should().BeTrue();
        var hybrid = result.Value!;
        hybrid.IsValid().Should().BeTrue();
        hybrid.Classical.Should().NotBeNullOrEmpty();
        hybrid.ClassicalAlgorithm.Should().Be("ED25519");
        hybrid.Pqc.Should().NotBeNullOrEmpty();
        hybrid.PqcAlgorithm.Should().Be("ML-DSA-65");
        hybrid.WitnessPublicKey.Should().Be(Convert.ToBase64String(pqcKeySet.PublicKey.Key!));
    }

    [Fact]
    public async Task HybridSignature_JsonRoundTrip_WithRealSignatures()
    {
        // Arrange — real signatures
        var classicalKeySet = (await _cryptoModule.GenerateKeySetAsync(WalletNetworks.ED25519)).Value;
        var pqcKeySet = (await _cryptoModule.GenerateKeySetAsync(WalletNetworks.ML_DSA_65)).Value;
        var data = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes("json round-trip test"));

        var classicalSig = await _cryptoModule.SignAsync(data, (byte)WalletNetworks.ED25519, classicalKeySet.PrivateKey.Key!);
        var pqcSig = await _cryptoModule.SignAsync(data, (byte)WalletNetworks.ML_DSA_65, pqcKeySet.PrivateKey.Key!);

        var hybrid = new HybridSignature
        {
            Classical = Convert.ToBase64String(classicalSig.Value!),
            ClassicalAlgorithm = "ED25519",
            Pqc = Convert.ToBase64String(pqcSig.Value!),
            PqcAlgorithm = "ML-DSA-65",
            WitnessPublicKey = Convert.ToBase64String(pqcKeySet.PublicKey.Key!)
        };

        // Act — serialize and deserialize
        var json = hybrid.ToJson();
        var restored = HybridSignature.FromJson(json);

        // Assert — verify both signatures survive round-trip
        restored.Should().NotBeNull();
        var restoredClassicalSig = Convert.FromBase64String(restored!.Classical!);
        var restoredPqcSig = Convert.FromBase64String(restored.Pqc!);

        var classicalVerify = await _cryptoModule.VerifyAsync(restoredClassicalSig, data,
            (byte)WalletNetworks.ED25519, classicalKeySet.PublicKey.Key!);
        classicalVerify.Should().Be(CryptoStatus.Success);

        var pqcVerify = await _cryptoModule.VerifyAsync(restoredPqcSig, data,
            (byte)WalletNetworks.ML_DSA_65, pqcKeySet.PublicKey.Key!);
        pqcVerify.Should().Be(CryptoStatus.Success);
    }
}
