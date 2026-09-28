// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Reflection;
using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sorcha.Cryptography.Core;
using Sorcha.Cryptography.Utilities;
using Sorcha.Wallet.Contracts.Models;
using Sorcha.Wallet.Core.Encryption.Providers;
using Sorcha.Wallet.Core.Events.Publishers;
using Sorcha.Wallet.Core.Repositories.Implementation;
using Sorcha.Wallet.Core.Services.Implementation;
using Sorcha.Wallet.Core.Services.Interfaces;
using Sorcha.Wallet.Service.Endpoints;
using Xunit;

namespace Sorcha.Wallet.Service.Tests.Services;

/// <summary>
/// #1756 — a hybrid wallet's post-quantum half is a real, stored, recoverable key, and a wallet can
/// only ever be paired with its OWN post-quantum half when signing.
/// </summary>
/// <remarks>
/// Creation used to generate a random PQC key, return its address and discard the private key, so
/// the PQC half could never sign ("PQC wallet not found") and was not in the recovery phrase. And
/// the hybrid sign path checked ownership of the classical address only, so pairing your wallet
/// with someone else's PQC wallet would have produced a signature from theirs. Real crypto
/// throughout — the #1689 lesson is that a mocked crypto module assumes the property under test.
/// </remarks>
public class HybridWalletCompanionTests
{
    private readonly InMemoryWalletRepository _repository = new();
    private readonly WalletManager _manager;

    public HybridWalletCompanionTests() => _manager = RealWalletManager(_repository);

    private static WalletManager RealWalletManager(InMemoryWalletRepository repository)
    {
        var crypto = new CryptoModule();
        var kms = new KeyManagementService(
            new LocalEncryptionProvider(NullLogger<LocalEncryptionProvider>.Instance),
            crypto, new WalletUtilities(), NullLogger<KeyManagementService>.Instance);
        return new WalletManager(
            kms,
            new TransactionService(crypto, new HashProvider(), NullLogger<TransactionService>.Instance),
            new DelegationService(repository, NullLogger<DelegationService>.Instance),
            repository,
            new InMemoryEventPublisher(NullLogger<InMemoryEventPublisher>.Instance),
            NullLogger<WalletManager>.Instance,
            Mock.Of<IRecoveryKeyService>());
    }

    private async Task<(Sorcha.Wallet.Core.Domain.Entities.Wallet Classical, Sorcha.Wallet.Core.Domain.Entities.Wallet Pqc, Sorcha.Wallet.Core.Domain.ValueObjects.Mnemonic Mnemonic)>
        CreateHybridAsync(string owner, string pqcAlgorithm = "ML-DSA-65")
    {
        var (classical, mnemonic) = await _manager.CreateWalletAsync("Hybrid", "ED25519", owner, "tenant-1", wordCount: 12);
        var pqc = await _manager.CreateHybridCompanionAsync(classical, mnemonic, pqcAlgorithm);
        return (classical, pqc, mnemonic);
    }

    [Fact]
    public async Task TheCompanion_IsStored_Linked_AndDistinctFromTheClassicalKey()
    {
        var (classical, pqc, _) = await CreateHybridAsync("alice");

        (await _manager.GetWalletAsync(pqc.Address)).Should().NotBeNull("the PQC half must be a stored wallet, not just an address");
        pqc.Algorithm.Should().Be("ML-DSA-65");
        pqc.Owner.Should().Be("alice");
        pqc.Address.Should().NotBe(classical.Address);
        classical.Metadata[WalletManager.HybridPqcAddressKey].Should().Be(pqc.Address);
        pqc.Metadata[WalletManager.HybridClassicalAddressKey].Should().Be(classical.Address);
    }

    [Fact]
    public async Task TheCompanion_Signs_AndTheSignatureVerifies()
    {
        var (_, pqc, _) = await CreateHybridAsync("alice");
        var hash = System.Security.Cryptography.SHA256.HashData("#1756"u8.ToArray());

        var (signature, publicKey) = await _manager.SignTransactionAsync(pqc.Address, hash, isPreHashed: true);

        var verified = await new CryptoModule().VerifyAsync(
            signature, hash, (byte)Sorcha.Cryptography.Enums.WalletNetworks.ML_DSA_65, publicKey);
        verified.Should().Be(Sorcha.Cryptography.Enums.CryptoStatus.Success);
    }

    [Fact]
    public async Task TheCompanion_IsRecoverable_FromTheSameRecoveryPhrase()
    {
        var (_, pqc, mnemonic) = await CreateHybridAsync("alice");

        var recovered = await RealWalletManager(new InMemoryWalletRepository())
            .RecoverWalletAsync(mnemonic, "restored", "ML-DSA-65", "alice", "tenant-1");

        recovered.Address.Should().Be(pqc.Address, "the phrase the owner wrote down must bring back the PQC half too");
    }

    [Theory]
    [InlineData("ML-KEM-768")]   // encapsulation only — a companion that cannot sign
    [InlineData("ED25519")]       // not post-quantum
    [InlineData("RSA4096")]
    public async Task AnAlgorithmThatCannotBeTheSigningPqcHalf_IsRefused(string algorithm)
    {
        var (classical, mnemonic) = await _manager.CreateWalletAsync("Hybrid", "ED25519", "alice", "tenant-1", wordCount: 12);

        var act = () => _manager.CreateHybridCompanionAsync(classical, mnemonic, algorithm);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    // ── The sign path: a wallet can only be paired with its OWN post-quantum half. ─────────────

    private static HttpContext UserContext(string platformUserId) => new DefaultHttpContext
    {
        User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("platform_user_id", platformUserId)], "Test"))
    };

    private static HttpContext ServiceContext() => new DefaultHttpContext
    {
        User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("token_type", "service"), new Claim("client_id", "service-blueprint")], "Test"))
    };

    private async Task<IResult> SignHybridAsync(string address, string pqcAddress, HttpContext context)
    {
        var method = typeof(WalletEndpoints).GetMethod("SignTransaction", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();
        var request = new SignTransactionRequest
        {
            TransactionData = Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData("tx"u8.ToArray())),
            IsPreHashed = true,
            HybridMode = true,
            PqcWalletAddress = pqcAddress,
        };
        var task = (Task<IResult>)method!.Invoke(null, [
            address, request, _manager, null,
            new DelegationService(_repository, NullLogger<DelegationService>.Instance),
            context, NullLogger<Program>.Instance, CancellationToken.None,
        ])!;
        return await task;
    }

    private static int StatusOf(IResult result) =>
        result is IStatusCodeHttpResult withStatus ? withStatus.StatusCode ?? 200 : 200;

    [Fact]
    public async Task HybridSigning_WithTheWalletsOwnPqcHalf_Succeeds()
    {
        var (classical, pqc, _) = await CreateHybridAsync("alice");

        var result = await SignHybridAsync(classical.Address, pqc.Address, UserContext("alice"));

        StatusOf(result).Should().Be(200);
    }

    [Fact]
    public async Task HybridSigning_WithSomeoneElsesPqcWallet_IsRefused()
    {
        var (aliceClassical, _, _) = await CreateHybridAsync("alice");
        var (_, bobPqc, _) = await CreateHybridAsync("bob");

        // Alice owns her classical wallet, so the ownership check passes — the pairing must not.
        var result = await SignHybridAsync(aliceClassical.Address, bobPqc.Address, UserContext("alice"));

        StatusOf(result).Should().Be(StatusCodes.Status403Forbidden);
    }

    [Fact]
    public async Task HybridSigning_ByAService_IsHeldToThePairingToo()
    {
        // Service tokens skip the ownership check entirely; the pairing rule must still hold.
        var (aliceClassical, _, _) = await CreateHybridAsync("alice");
        var (_, bobPqc, _) = await CreateHybridAsync("bob");

        var result = await SignHybridAsync(aliceClassical.Address, bobPqc.Address, ServiceContext());

        StatusOf(result).Should().Be(StatusCodes.Status403Forbidden);
    }
}
