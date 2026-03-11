// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.Cryptography.Core;
using Sorcha.Cryptography.Enums;
using Sorcha.Cryptography.Models;
using Sorcha.Cryptography.Utilities;
using Sorcha.Wallet.Core.Domain.ValueObjects;
using Xunit;

namespace Sorcha.Wallet.Service.Tests.Unit;

public class PqcWalletIntegrationTests
{
    private readonly CryptoModule _cryptoModule = new();
    private readonly WalletUtilities _walletUtilities = new();

    [Fact]
    public async Task CreatePqcWallet_ProducesWs2Address()
    {
        var keyResult = await _cryptoModule.GenerateKeySetAsync(WalletNetworks.ML_DSA_65);
        keyResult.IsSuccess.Should().BeTrue();

        var address = _walletUtilities.PublicKeyToWallet(
            keyResult.Value.PublicKey.Key!, (byte)WalletNetworks.ML_DSA_65);

        address.Should().NotBeNull();
        address.Should().StartWith("ws2");
        address!.Length.Should().BeLessThan(100);
    }

    [Fact]
    public async Task HybridSign_IncludesWitnessPublicKey()
    {
        // Generate classical + PQC key pairs
        var classicalKeys = await _cryptoModule.GenerateKeySetAsync(WalletNetworks.ED25519);
        var pqcKeys = await _cryptoModule.GenerateKeySetAsync(WalletNetworks.ML_DSA_65);
        classicalKeys.IsSuccess.Should().BeTrue();
        pqcKeys.IsSuccess.Should().BeTrue();

        var data = System.Text.Encoding.UTF8.GetBytes("hybrid test data");
        var hash = System.Security.Cryptography.SHA256.HashData(data);

        // Hybrid sign
        var signResult = await _cryptoModule.HybridSignAsync(
            hash,
            (byte)WalletNetworks.ED25519, classicalKeys.Value.PrivateKey.Key!,
            (byte)WalletNetworks.ML_DSA_65, pqcKeys.Value.PrivateKey.Key!,
            pqcKeys.Value.PublicKey.Key!);

        signResult.IsSuccess.Should().BeTrue();
        signResult.Value!.WitnessPublicKey.Should().NotBeNullOrEmpty(
            "HybridSignature must include the PQC public key as witness data");

        // Witness key should match the original PQC public key
        var witnessKey = Convert.FromBase64String(signResult.Value.WitnessPublicKey!);
        witnessKey.Should().BeEquivalentTo(pqcKeys.Value.PublicKey.Key);
    }

    [Fact]
    public async Task WitnessKey_HashMatchesWs2Address()
    {
        var pqcKeys = await _cryptoModule.GenerateKeySetAsync(WalletNetworks.ML_DSA_65);
        pqcKeys.IsSuccess.Should().BeTrue();

        // Generate ws2 address
        var ws2Address = _walletUtilities.PublicKeyToWallet(
            pqcKeys.Value.PublicKey.Key!, (byte)WalletNetworks.ML_DSA_65);

        // Decode address to get the hash
        var decoded = _walletUtilities.WalletToPublicKey(ws2Address!);
        decoded.Should().NotBeNull();
        var addressHash = decoded!.Value.PublicKey;

        // Compute hash of the witness key (the full PQC public key)
        byte[] toHash = new byte[pqcKeys.Value.PublicKey.Key!.Length + 1];
        toHash[0] = (byte)WalletNetworks.ML_DSA_65;
        Array.Copy(pqcKeys.Value.PublicKey.Key, 0, toHash, 1, pqcKeys.Value.PublicKey.Key.Length);
        var expectedHash = System.Security.Cryptography.SHA256.HashData(toHash);

        addressHash.Should().BeEquivalentTo(expectedHash,
            "hash of witness key must match the ws2 address hash");
    }

    [Fact]
    public async Task HybridVerify_WithWitnessKey_Succeeds()
    {
        var classicalKeys = await _cryptoModule.GenerateKeySetAsync(WalletNetworks.ED25519);
        var pqcKeys = await _cryptoModule.GenerateKeySetAsync(WalletNetworks.ML_DSA_65);

        var data = System.Text.Encoding.UTF8.GetBytes("verify with witness");
        var hash = System.Security.Cryptography.SHA256.HashData(data);

        var signResult = await _cryptoModule.HybridSignAsync(
            hash,
            (byte)WalletNetworks.ED25519, classicalKeys.Value.PrivateKey.Key!,
            (byte)WalletNetworks.ML_DSA_65, pqcKeys.Value.PrivateKey.Key!,
            pqcKeys.Value.PublicKey.Key!);

        // Verify using classical public key (PQC key comes from witness)
        var verifyResult = await _cryptoModule.HybridVerifyAsync(
            signResult.Value!, hash, classicalKeys.Value.PublicKey.Key!);

        verifyResult.Should().Be(CryptoStatus.Success);
    }

    [Fact]
    public void PqcDerivationPath_UsesCoinType1()
    {
        var path = DerivationPath.CreatePqcBip44(account: 0, change: 0, addressIndex: 0);

        path.Path.Should().Contain("1'", "PQC uses coin type 1'");
        path.Path.Should().Be("44'/1'/0'/0/0");
    }

    [Fact]
    public void PqcDerivationPath_DiffersFromClassical()
    {
        var classical = DerivationPath.CreateBip44(coinType: 0);
        var pqc = DerivationPath.CreatePqcBip44();

        classical.Path.Should().NotBe(pqc.Path, "PQC paths must differ from classical");
    }
}
