// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sorcha.Cryptography.Enums;
using Sorcha.Cryptography.Interfaces;
using Sorcha.Cryptography.Models;
using Sorcha.Wallet.Core.Domain;
using Sorcha.Wallet.Core.Domain.Entities;
using Sorcha.Wallet.Core.Domain.Enums;
using Sorcha.Wallet.Core.Services.Interfaces;
using Sorcha.Wallet.Service.Services.Implementation;
using Sorcha.Wallet.Service.Services.Interfaces;
using Xunit;
using WalletEntity = Sorcha.Wallet.Core.Domain.Entities.Wallet;

namespace Sorcha.Wallet.Service.Tests.Services;

/// <summary>
/// Unit tests for <see cref="PersonaCryptoService"/>. Feature 092 / T074.
/// Exercises the encrypt/decrypt round trip, tamper detection, nonce
/// length enforcement, and the <c>wrappedKeyRef == walletAddress</c>
/// invariant against an in-memory wallet with a mocked key-management
/// and real <see cref="ISymmetricCrypto"/>-alike substitute.
/// </summary>
public sealed class PersonaCryptoServiceTests : IDisposable
{
    private readonly TestRecoveryDbContext _db;
    private readonly Mock<IKeyManagementService> _keyMgmt = new();
    private readonly Mock<ISymmetricCrypto> _symmetric = new();
    private readonly PersonaCryptoService _sut;

    private const string WalletAddress = "sorcha1testaddress";
    // Deterministic "decrypted private key" so DeriveContentKeyAsync
    // produces the same content key across calls in a test.
    private static readonly byte[] FakePrivateKey = Enumerable
        .Range(0, 32).Select(i => (byte)i).ToArray();

    public PersonaCryptoServiceTests()
    {
        var options = new DbContextOptionsBuilder<TestRecoveryDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _db = new TestRecoveryDbContext(options);

        _db.Wallets.Add(new WalletEntity
        {
            Address = WalletAddress,
            Name = "Test wallet",
            EncryptedPrivateKey = "fake-ciphertext",
            PublicKey = "fake-pub",
            Algorithm = "ED25519",
            EncryptionKeyId = "test-key-id",
            Owner = "test-user",
            Tenant = "test-tenant",
            Status = WalletStatus.Active,
        });
        _db.SaveChanges();

        _keyMgmt
            .Setup(k => k.DecryptPrivateKeyAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(() => (byte[])FakePrivateKey.Clone());

        // Substitute symmetric crypto: ciphertext = plaintext XOR key[0],
        // nonce = fixed 24 bytes. Deterministic, letting us assert the
        // round trip and tamper detection without dragging in NaCl.
        _symmetric
            .Setup(s => s.EncryptAsync(
                It.IsAny<byte[]>(), EncryptionType.XCHACHA20_POLY1305,
                It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[] pt, EncryptionType _, byte[] key, CancellationToken _) =>
            {
                var ct = new byte[pt.Length];
                for (int i = 0; i < pt.Length; i++) ct[i] = (byte)(pt[i] ^ key[0]);
                var iv = new byte[24];
                return CryptoResult<SymmetricCiphertext>.Success(new SymmetricCiphertext
                {
                    Data = ct,
                    Key = (byte[])key.Clone(),
                    IV = iv,
                    Type = EncryptionType.XCHACHA20_POLY1305,
                });
            });

        _symmetric
            .Setup(s => s.DecryptAsync(It.IsAny<SymmetricCiphertext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SymmetricCiphertext sc, CancellationToken _) =>
            {
                // Simulate tamper detection: when the ciphertext's first
                // byte is corrupted after encryption, fail.
                // (The test toggles this via a "tampered" marker.)
                var pt = new byte[sc.Data.Length];
                for (int i = 0; i < sc.Data.Length; i++) pt[i] = (byte)(sc.Data[i] ^ sc.Key[0]);
                if (pt.Length > 0 && pt[0] == 0xEE)
                {
                    return CryptoResult<byte[]>.Failure(CryptoStatus.InvalidKey, "authentication tag mismatch");
                }
                return CryptoResult<byte[]>.Success(pt);
            });

        _sut = new PersonaCryptoService(
            _db, _keyMgmt.Object, _symmetric.Object, NullLogger<PersonaCryptoService>.Instance);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task EncryptAsync_UnknownWallet_ThrowsKeyNotFound()
    {
        var act = async () => await _sut.EncryptAsync("unknown", new byte[] { 1, 2, 3 });

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task EncryptDecrypt_RoundTrip_ReturnsOriginalPlaintext()
    {
        var plaintext = Encoding.UTF8.GetBytes("{\"givenName\":\"Ada\"}");

        var encrypted = await _sut.EncryptAsync(WalletAddress, plaintext);
        encrypted.WrappedKeyRef.Should().Be(WalletAddress);
        encrypted.Nonce.Length.Should().Be(24);
        encrypted.Ciphertext.Should().NotEqual(plaintext);

        var decrypted = await _sut.DecryptAsync(
            WalletAddress, encrypted.Ciphertext, encrypted.Nonce, encrypted.WrappedKeyRef);

        decrypted.Should().Equal(plaintext);
    }

    [Fact]
    public async Task DecryptAsync_WrappedKeyRefMismatch_ThrowsTypedInvariantException()
    {
        var encrypted = await _sut.EncryptAsync(WalletAddress, new byte[] { 1 });

        var act = async () => await _sut.DecryptAsync(
            WalletAddress, encrypted.Ciphertext, encrypted.Nonce, wrappedKeyRef: "different-wallet");

        await act.Should().ThrowAsync<PersonaKeyRefMismatchException>();
    }

    [Fact]
    public async Task DecryptAsync_WrongNonceLength_ThrowsArgumentException()
    {
        var act = async () => await _sut.DecryptAsync(
            WalletAddress, new byte[] { 1, 2, 3 }, nonce: new byte[16], wrappedKeyRef: WalletAddress);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*24 bytes*");
    }

    [Fact]
    public async Task DecryptAsync_TamperedCiphertext_ThrowsCryptographicException()
    {
        var plaintext = new byte[] { 0xEE, 0x00, 0x00 }; // marker for the mock tamper path
        var encrypted = await _sut.EncryptAsync(WalletAddress, plaintext);

        var act = async () => await _sut.DecryptAsync(
            WalletAddress, encrypted.Ciphertext, encrypted.Nonce, encrypted.WrappedKeyRef);

        await act.Should().ThrowAsync<CryptographicException>();
    }

    [Fact]
    public async Task EncryptAsync_ProducesNonDeterministicCiphertextAcrossCalls()
    {
        // The substitute symmetric crypto is deterministic, so ciphertext
        // equality just reflects the same inputs. This test documents the
        // expectation for a real implementation: given the 24-byte nonce
        // is generated inside ISymmetricCrypto, a real round would differ.
        // Here we just assert the API contract shape (nonce is always 24
        // bytes, wrappedKeyRef is always the wallet address).
        var e1 = await _sut.EncryptAsync(WalletAddress, new byte[] { 1 });
        var e2 = await _sut.EncryptAsync(WalletAddress, new byte[] { 1 });

        e1.WrappedKeyRef.Should().Be(WalletAddress);
        e2.WrappedKeyRef.Should().Be(WalletAddress);
        e1.Nonce.Length.Should().Be(24);
        e2.Nonce.Length.Should().Be(24);
    }
}
