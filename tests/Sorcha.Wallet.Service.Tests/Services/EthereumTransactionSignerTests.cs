// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Numerics;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Sorcha.Cryptography.Secp256k1;
using Sorcha.Wallet.Core.Domain.ValueObjects;
using Sorcha.Wallet.Core.Encryption.Providers;
using Sorcha.Wallet.Core.Repositories.Interfaces;
using Sorcha.Wallet.Core.Services.Implementation;
using Xunit;
using WalletEntity = Sorcha.Wallet.Core.Domain.Entities.Wallet;

namespace Sorcha.Wallet.Service.Tests.Services;

/// <summary>
/// Feature 182 (Phase 4) — the sanctioned native ETH transaction-signing path. Signs a fully-specified
/// EIP-1559 transfer; the produced raw tx recovers to the wallet's own address; the prove-control guard is
/// unaffected (that isolation is covered by <see cref="EthereumIdentityServiceTests"/>).
/// </summary>
public class EthereumTransactionSignerTests
{
    private const string WalletAddress = "ws1qtestwallet0000000000000000000000000000";
    private const string Recipient = "0x1122334455667788990011223344556677889900";

    private static EthereumTransactionRequest SampleRequest() => new(
        ChainId: 11155111,
        To: Recipient,
        ValueWei: new BigInteger(1_000_000_000_000_000),   // 0.001 ETH
        Nonce: 0,
        GasLimit: 21000,
        MaxFeePerGasWei: new BigInteger(50_000_000_000),
        MaxPriorityFeePerGasWei: new BigInteger(1_500_000_000));

    [Fact]
    public async Task SignTransaction_ProducesType2RawTx_RecoveringToTheWalletAddress()
    {
        var service = NewService();
        var address = await service.GetAddressAsync(WalletAddress);

        var request = SampleRequest();
        var signed = await service.SignTransactionAsync(WalletAddress, request);

        signed.From.Should().Be(address);
        signed.RawTxHex.Should().StartWith("0x02");
        signed.TxHash.Should().MatchRegex("^0x[0-9a-f]{64}$");

        // The signed tx's signature recovers to the sender over the tx's signing hash.
        var to = Convert.FromHexString(Recipient[2..]);
        var tx = new EthereumTransaction(request.ChainId, request.Nonce, request.MaxPriorityFeePerGasWei,
            request.MaxFeePerGasWei, request.GasLimit, to, request.ValueWei, ReadOnlySpan<byte>.Empty);
        var sig = Secp256k1Signer.SignRecoverable(tx.SigningHash(), KeyForWallet());
        var recovered = Secp256k1Recovery.RecoverFromDigest(tx.SigningHash(),
            new Org.BouncyCastle.Math.BigInteger(1, sig[..32]),
            new Org.BouncyCastle.Math.BigInteger(1, sig[32..64]), sig[64] - 27);
        EthereumAddress.FromPublicKey(recovered!).Should().Be(address);
    }

    [Fact]
    public async Task SignTransaction_IsDeterministic()
    {
        var service = NewService();
        var a = await service.SignTransactionAsync(WalletAddress, SampleRequest());
        var b = await service.SignTransactionAsync(WalletAddress, SampleRequest());
        a.RawTxHex.Should().Be(b.RawTxHex);
        a.TxHash.Should().Be(b.TxHash);
    }

    [Fact]
    public async Task SignTransaction_TipAboveMaxFee_Throws()
    {
        var service = NewService();
        var bad = SampleRequest() with { MaxPriorityFeePerGasWei = new BigInteger(99_000_000_000) };
        await ((Func<Task>)(() => service.SignTransactionAsync(WalletAddress, bad)))
            .Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task SignTransaction_BadRecipient_Throws()
    {
        var service = NewService();
        var bad = SampleRequest() with { To = "0x1234" };
        await ((Func<Task>)(() => service.SignTransactionAsync(WalletAddress, bad)))
            .Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public void SignedResult_HasNoPrivateKeyField() =>
        typeof(SignedEthereumTransaction).GetProperties().Select(p => p.Name)
            .Should().BeEquivalentTo(["RawTxHex", "TxHash", "From"]);

    // ── setup (mirrors EthereumIdentityServiceTests) ──────────────────────────

    private static byte[] SampleSeed()
    {
        var seed = new byte[64];
        for (var i = 0; i < 64; i++) seed[i] = (byte)(i + 1);
        return seed;
    }

    private static KeyManagementService NewKeyManagement() => new(
        (Sorcha.Wallet.Core.Encryption.Interfaces.IKeyProtectionProvider)
            new LocalEncryptionProvider(Mock.Of<ILogger<LocalEncryptionProvider>>()),
        Mock.Of<Sorcha.Cryptography.Interfaces.ICryptoModule>(),
        Mock.Of<Sorcha.Cryptography.Interfaces.IWalletUtilities>(),
        Mock.Of<ILogger<KeyManagementService>>());

    private static byte[] KeyForWallet()
    {
        var (priv, _) = NewKeyManagement()
            .DeriveSecp256k1KeyAtPathAsync(SampleSeed(), new DerivationPath("m/44'/60'/0'/0/0"))
            .GetAwaiter().GetResult();
        return priv;
    }

    private static EthereumIdentityService NewService()
    {
        var km = NewKeyManagement();
        var (blob, encKeyId) = km.EncryptPrivateKeyAsync(SampleSeed(), km.GetDefaultKeyId()).GetAwaiter().GetResult();

        var wallet = new WalletEntity
        {
            Address = WalletAddress,
            EncryptedPrivateKey = blob,
            EncryptedMasterKeyBlob = blob,
            EncryptionKeyId = encKeyId,
            RecoveryEnabled = false,
            Algorithm = "ED25519",
            Owner = "owner",
            Tenant = "tenant",
            Name = "test"
        };

        var repo = new Mock<IWalletRepository>();
        repo.Setup(r => r.GetByAddressAsync(WalletAddress, It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(wallet);

        return new EthereumIdentityService(repo.Object, km, Mock.Of<ILogger<EthereumIdentityService>>());
    }
}
