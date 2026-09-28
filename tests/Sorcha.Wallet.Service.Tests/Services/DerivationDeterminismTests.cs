// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Sorcha.Cryptography.Core;
using Sorcha.Cryptography.Enums;
using Sorcha.Cryptography.Utilities;
using Sorcha.Wallet.Core.Domain.ValueObjects;
using Sorcha.Wallet.Core.Encryption.Providers;
using Sorcha.Wallet.Core.Events.Publishers;
using Sorcha.Wallet.Core.Repositories.Implementation;
using Sorcha.Wallet.Core.Services.Implementation;
using Sorcha.Wallet.Core.Services.Interfaces;
using Xunit;

namespace Sorcha.Wallet.Service.Tests.Services;

/// <summary>
/// #1689 — deriving a key at a path from a master key must be a FUNCTION: the same inputs give the
/// same key, for every algorithm a wallet can be created with — and an algorithm that cannot do
/// that must be refused, never answered with a key nothing can reproduce.
/// </summary>
/// <remarks>
/// <para>
/// Recovery re-derives the wallet's primary key from its mnemonic, and derivation-path signing
/// (<c>sorcha:docket-signing</c>, <c>sorcha:blueprint-publish</c>, …) re-derives a purpose key on
/// every call. If derivation ignores the seed, recovery lands on a different address ("no such
/// wallet") and a validator's signing key stops matching its roster entry, so dockets silently stop
/// sealing (CLAUDE.md §15). Nothing throws in either case.
/// </para>
/// <para>
/// This survived because every existing wallet test either used ED25519 (where the seed was always
/// honoured) or MOCKED the crypto module with a deterministic fake — assuming the very property
/// that was broken. These tests use the real <see cref="CryptoModule"/>. When this was written, five
/// of the seven algorithms returned a fresh random key on every derivation.
/// </para>
/// </remarks>
public class DerivationDeterminismTests
{
    /// <summary>RSA has no standard deterministic key generation; derivation must refuse it.</summary>
    private static readonly WalletNetworks[] NotDerivable = [WalletNetworks.RSA4096];

    private static KeyManagementService RealKeyManagement() => new(
        new LocalEncryptionProvider(Mock.Of<ILogger<LocalEncryptionProvider>>()),
        new CryptoModule(),
        new WalletUtilities(),
        Mock.Of<ILogger<KeyManagementService>>());

    private static byte[] MasterKey(byte fill) => Enumerable.Repeat(fill, 64).ToArray();

    public static TheoryData<WalletNetworks> DerivableAlgorithms()
    {
        var data = new TheoryData<WalletNetworks>();
        foreach (var network in Enum.GetValues<WalletNetworks>().Except(NotDerivable))
        {
            data.Add(network);
        }
        return data;
    }

    public static TheoryData<WalletNetworks> SigningAlgorithms()
    {
        // ML-KEM is a key-encapsulation algorithm: it derives, but it does not sign.
        var data = new TheoryData<WalletNetworks>();
        foreach (var network in Enum.GetValues<WalletNetworks>().Except(NotDerivable).Except([WalletNetworks.ML_KEM_768]))
        {
            data.Add(network);
        }
        return data;
    }

    [Fact]
    public void TheAlgorithmListIsNotVacuous()
    {
        // If the enum were ever read as empty, every theory above would pass by running nothing.
        Enum.GetValues<WalletNetworks>().Should().HaveCountGreaterThanOrEqualTo(7);
    }

    [Theory]
    [MemberData(nameof(DerivableAlgorithms))]
    public async Task SameMasterKeyAndPath_GiveTheSameKey(WalletNetworks network)
    {
        var kms = RealKeyManagement();
        var algorithm = AlgorithmMapper.ToAlgorithmName(network);
        var path = DerivationPath.CreateBip44(0, 0, 0, 0);

        var first = await kms.DeriveKeyAtPathAsync(MasterKey(7), path, algorithm);
        var second = await kms.DeriveKeyAtPathAsync(MasterKey(7), path, algorithm);

        second.PublicKey.Should().Equal(first.PublicKey,
            $"{algorithm}: the same master key at the same path must derive the same key, or recovery and purpose-key signing break silently");
        second.PrivateKey.Should().Equal(first.PrivateKey);
    }

    [Theory]
    [MemberData(nameof(DerivableAlgorithms))]
    public async Task DifferentMasterKeys_Or_Paths_GiveDifferentKeys(WalletNetworks network)
    {
        // The counterpart: a "deterministic" derivation that ignored its input entirely (a constant
        // key) would pass the test above. It must depend on both the master key and the path.
        var kms = RealKeyManagement();
        var algorithm = AlgorithmMapper.ToAlgorithmName(network);

        var baseline = await kms.DeriveKeyAtPathAsync(MasterKey(7), DerivationPath.CreateBip44(0, 0, 0, 0), algorithm);
        var otherMaster = await kms.DeriveKeyAtPathAsync(MasterKey(8), DerivationPath.CreateBip44(0, 0, 0, 0), algorithm);
        var otherPath = await kms.DeriveKeyAtPathAsync(MasterKey(7), DerivationPath.CreateBip44(0, 0, 0, 1), algorithm);

        otherMaster.PublicKey.Should().NotEqual(baseline.PublicKey);
        otherPath.PublicKey.Should().NotEqual(baseline.PublicKey);
    }

    [Theory]
    [MemberData(nameof(SigningAlgorithms))]
    public async Task ADerivedKey_Signs_AndItsSignatureVerifies(WalletNetworks network)
    {
        // Determinism is worthless if the derived key is not a usable key of that algorithm — e.g.
        // an encoding the signing path's FromEncoding cannot read.
        var kms = RealKeyManagement();
        var crypto = new CryptoModule();
        var (privateKey, publicKey) = await kms.DeriveKeyAtPathAsync(
            MasterKey(7), DerivationPath.CreateBip44(0, 0, 0, 0), AlgorithmMapper.ToAlgorithmName(network));
        var hash = System.Security.Cryptography.SHA256.HashData("#1689"u8.ToArray());

        var signature = await crypto.SignAsync(hash, (byte)network, privateKey);
        signature.IsSuccess.Should().BeTrue(signature.ErrorMessage);

        var verified = await crypto.VerifyAsync(signature.Value!, hash, (byte)network, publicKey);
        verified.Should().Be(Sorcha.Cryptography.Enums.CryptoStatus.Success);
    }

    [Fact]
    public async Task Rsa_IsRefused_NotAnsweredWithAnUnreproducibleKey()
    {
        var act = () => RealKeyManagement().DeriveKeyAtPathAsync(
            MasterKey(7), DerivationPath.CreateBip44(0, 0, 0, 0), "RSA4096");

        (await act.Should().ThrowAsync<NotSupportedException>()).WithMessage("*cannot be derived*");
    }

    // ── The property users actually depend on: a wallet recovers to the same address. ──────────

    private static WalletManager RealWalletManager()
    {
        var crypto = new CryptoModule();
        var encryption = new LocalEncryptionProvider(Mock.Of<ILogger<LocalEncryptionProvider>>());
        var repository = new InMemoryWalletRepository();
        var kms = new KeyManagementService(encryption, crypto, new WalletUtilities(), Mock.Of<ILogger<KeyManagementService>>());
        return new WalletManager(
            kms,
            new TransactionService(crypto, new HashProvider(), Mock.Of<ILogger<TransactionService>>()),
            new DelegationService(repository, Mock.Of<ILogger<DelegationService>>()),
            repository,
            new InMemoryEventPublisher(Mock.Of<ILogger<InMemoryEventPublisher>>()),
            Mock.Of<ILogger<WalletManager>>(),
            Mock.Of<IRecoveryKeyService>());
    }

    [Theory]
    [MemberData(nameof(DerivableAlgorithms))]
    public async Task AWalletCreatedFromAMnemonic_RecoversToTheSameAddress(WalletNetworks network)
    {
        var algorithm = AlgorithmMapper.ToAlgorithmName(network);

        var (created, mnemonic) = await RealWalletManager().CreateWalletAsync(
            "original", algorithm, owner: "owner-1", tenant: "tenant-1", wordCount: 12);

        // A different node, a different process: nothing shared but the recovery phrase.
        var recovered = await RealWalletManager().RecoverWalletAsync(
            mnemonic, "recovered", algorithm, "owner-1", "tenant-1");

        recovered.Address.Should().Be(created.Address,
            $"{algorithm}: the recovery phrase must bring back the same wallet, not a new one");
    }
}
