// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Sorcha.Cryptography.Secp256k1;
using Sorcha.Cryptography.Secp256k1.Siwe;
using Sorcha.Wallet.Core.Domain.ValueObjects;
using Sorcha.Wallet.Core.Encryption.Providers;
using Sorcha.Wallet.Core.Repositories.Interfaces;
using Sorcha.Wallet.Core.Services.Implementation;
using Xunit;
using WalletEntity = Sorcha.Wallet.Core.Domain.Entities.Wallet;

namespace Sorcha.Wallet.Service.Tests.Services;

/// <summary>Feature 180 (part 2) — the wallet auxiliary Ethereum identity: derive, address, SIWE sign, tx-guard.</summary>
public class EthereumIdentityServiceTests
{
    private const string WalletAddress = "ws1qtestwallet0000000000000000000000000000";

    [Fact]
    public async Task DeriveSecp256k1KeyAtPathAsync_Deterministic_MatchesNBitcoin()
    {
        var km = NewKeyManagement();
        var seed = SampleSeed();
        var path = new DerivationPath("m/44'/60'/0'/0/0");

        var (priv, pub) = await km.DeriveSecp256k1KeyAtPathAsync(seed, path);

        priv.Should().HaveCount(32);
        pub.Should().HaveCount(65);

        var expected = NBitcoin.ExtKey.CreateFromSeed(seed).Derive(new NBitcoin.KeyPath("m/44'/60'/0'/0/0"));
        priv.Should().Equal(expected.PrivateKey.ToBytes());

        var (priv2, _) = await km.DeriveSecp256k1KeyAtPathAsync(seed, path);
        priv2.Should().Equal(priv); // deterministic
    }

    [Fact]
    public async Task GetAddress_IsDeterministic_And_SignSiwe_VerifiesToThatAddress()
    {
        var (service, _) = NewService();

        var address = await service.GetAddressAsync(WalletAddress);
        address.Should().MatchRegex("^0x[0-9a-fA-F]{40}$");
        (await service.GetAddressAsync(WalletAddress)).Should().Be(address); // deterministic

        var siwe = new SiweMessage
        {
            Domain = "app.test",
            Address = "0x0000000000000000000000000000000000000000", // overwritten with the wallet's own address
            Uri = "https://app.test/login",
            Version = "1",
            ChainId = 1,
            Nonce = "nonce-1",
            IssuedAt = "2026-07-10T12:00:00Z"
        };

        var result = await service.SignSiweAsync(WalletAddress, siwe);

        result.Address.Should().Be(address);
        result.Signature.Should().MatchRegex("^0x[0-9a-f]{130}$"); // 65 bytes
        var sig = Convert.FromHexString(result.Signature[2..]);
        SiweVerifier.Verify(result.Message, sig, new SiweValidationOptions(ExpectedNonce: "nonce-1", ExpectedDomain: "app.test"))
            .Valid.Should().BeTrue();
    }

    [Fact]
    public async Task SiweSignResult_ContainsNoPrivateKeyMaterial()
    {
        var (service, _) = NewService();
        var siwe = new SiweMessage
        {
            Domain = "app.test", Address = "0x0", Uri = "https://app.test", Version = "1",
            ChainId = 1, Nonce = "n", IssuedAt = "2026-07-10T12:00:00Z"
        };

        var result = await service.SignSiweAsync(WalletAddress, siwe);

        // Only message / signature / address — the record has no key field by construction.
        typeof(SiweSignResult).GetProperties().Select(p => p.Name)
            .Should().BeEquivalentTo(["Message", "Signature", "Address"]);
    }

    [Theory]
    [InlineData(new byte[] { 0xf8, 0x6c, 0x01, 0x02 })]       // legacy RLP list (tx-shaped)
    [InlineData(new byte[] { 0x02, 0xc9, 0x01, 0x02 })]       // typed-tx (0x02) envelope + RLP list
    public async Task SignPersonalMessage_TransactionShapedPayload_Refused(byte[] payload)
    {
        var (service, _) = NewService();

        var act = async () => await service.SignPersonalMessageAsync(WalletAddress, payload);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task SignPersonalMessage_PlainText_Signs()
    {
        var (service, _) = NewService();
        var address = await service.GetAddressAsync(WalletAddress);

        var sig = await service.SignPersonalMessageAsync(WalletAddress, "hello sorcha"u8.ToArray());

        sig.Should().HaveCount(65);
        // recovers to the wallet address
        var digest = Eip191.PersonalSignDigest("hello sorcha"u8.ToArray());
        var recovered = Secp256k1Recovery.RecoverFromDigest(digest,
            new Org.BouncyCastle.Math.BigInteger(1, sig[..32]),
            new Org.BouncyCastle.Math.BigInteger(1, sig[32..64]), sig[64] - 27);
        EthereumAddress.FromPublicKey(recovered!).Should().BeEquivalentTo(address);
    }

    // ── setup ────────────────────────────────────────────────────────────────

    private static byte[] SampleSeed()
    {
        var seed = new byte[64];
        for (var i = 0; i < 64; i++) seed[i] = (byte)(i + 1);
        return seed;
    }

    private static KeyManagementService NewKeyManagement()
    {
        var encryptionProvider = new LocalEncryptionProvider(Mock.Of<ILogger<LocalEncryptionProvider>>());
        return new KeyManagementService(
            (Sorcha.Wallet.Core.Encryption.Interfaces.IKeyProtectionProvider)encryptionProvider,
            Mock.Of<Sorcha.Cryptography.Interfaces.ICryptoModule>(),
            Mock.Of<Sorcha.Cryptography.Interfaces.IWalletUtilities>(),
            Mock.Of<ILogger<KeyManagementService>>());
    }

    private static (EthereumIdentityService Service, WalletEntity Wallet) NewService()
    {
        var km = NewKeyManagement();
        var seed = SampleSeed();
        var keyId = km.GetDefaultKeyId();
        var (blob, encKeyId) = km.EncryptPrivateKeyAsync(seed, keyId).GetAwaiter().GetResult();

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

        var service = new EthereumIdentityService(repo.Object, km, Mock.Of<ILogger<EthereumIdentityService>>());
        return (service, wallet);
    }
}
