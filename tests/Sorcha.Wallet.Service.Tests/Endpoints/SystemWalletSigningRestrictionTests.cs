// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Reflection;
using System.Security.Claims;

using FluentAssertions;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using Sorcha.Cryptography.Enums;
using Sorcha.Cryptography.Interfaces;
using Sorcha.Cryptography.Models;
using Sorcha.Wallet.Contracts.Models;
using Sorcha.Wallet.Core.Encryption.Providers;
using Sorcha.Wallet.Core.Events.Publishers;
using Sorcha.Wallet.Core.Repositories.Implementation;
using Sorcha.Wallet.Core.Services.Implementation;
using Sorcha.Wallet.Core.Services.Interfaces;
using Sorcha.Wallet.Service.Endpoints;

using Xunit;

namespace Sorcha.Wallet.Service.Tests.Endpoints;

/// <summary>
/// #1397 — narrows the service-token ownership bypass in <c>WalletEndpoints.SignTransaction</c>
/// so that a <c>validator:*</c>-owned system wallet (the docket-signing / SSR-owner key) may only
/// be signed by the Validator service principal (<c>client_id == "validator-service"</c>, confirmed
/// against <see cref="Sorcha.Tenant.Service.Data.DatabaseInitializer.ValidatorServicePrincipalId"/>'s
/// seeded <c>ClientId</c> — NOT "service-validator" as a naive guess would assume).
///
/// Service tokens legitimately bypass wallet ownership for ordinary (non-<c>validator:*</c>) wallets —
/// e.g. Blueprint Service signs the issuing org's wallet during credential issuance for a different
/// citizen — so that bypass must keep working unchanged; only <c>validator:*</c>-owned wallets are
/// narrowed to the Validator principal.
///
/// Uses a real <see cref="WalletManager"/> backed by <see cref="InMemoryWalletRepository"/> (the same
/// construction pattern as <c>WalletManagerTests</c>) rather than mocking <c>WalletManager</c> itself,
/// because its members are not virtual and cannot be intercepted by Moq. The endpoint handler is
/// invoked via the existing reflection-based static-handler pattern used by
/// <c>CitizenWalletEnrolEndpointTests</c>.
/// </summary>
public sealed class SystemWalletSigningRestrictionTests
{
    private const string ValidatorOwner = "validator:local-validator";
    private const string OrdinaryOwner = "some-org-wallet-owner";

    private readonly Mock<ICryptoModule> _mockCryptoModule = new();
    private readonly Mock<IHashProvider> _mockHashProvider = new();
    private readonly Mock<IWalletUtilities> _mockWalletUtilities = new();
    private readonly InMemoryWalletRepository _repository = new();
    private readonly WalletManager _walletManager;

    public SystemWalletSigningRestrictionTests()
    {
        var encryptionProvider = new LocalEncryptionProvider(NullLogger<LocalEncryptionProvider>.Instance);
        var eventPublisher = new InMemoryEventPublisher(NullLogger<InMemoryEventPublisher>.Instance);

        var keyManagement = new KeyManagementService(
            (Sorcha.Wallet.Core.Encryption.Interfaces.IKeyProtectionProvider)encryptionProvider,
            _mockCryptoModule.Object,
            _mockWalletUtilities.Object,
            NullLogger<KeyManagementService>.Instance);

        var transactionService = new TransactionService(
            _mockCryptoModule.Object,
            _mockHashProvider.Object,
            NullLogger<TransactionService>.Instance);

        var delegationService = new DelegationService(_repository, NullLogger<DelegationService>.Instance);

        _walletManager = new WalletManager(
            keyManagement,
            transactionService,
            delegationService,
            _repository,
            eventPublisher,
            NullLogger<WalletManager>.Instance,
            Mock.Of<IRecoveryKeyService>());

        // Deterministic key generation, mirroring WalletManagerTests.
        _mockCryptoModule
            .Setup(x => x.GenerateKeySetAsync(
                It.IsAny<WalletNetworks>(),
                It.IsAny<byte[]>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((WalletNetworks network, byte[] seed, CancellationToken _) =>
            {
                var privateKey = new byte[32];
                var publicKey = new byte[32];
                using var sha256 = System.Security.Cryptography.SHA256.Create();
                var hash = sha256.ComputeHash(seed);
                Array.Copy(hash, privateKey, 32);
                var pubHash = sha256.ComputeHash(privateKey);
                Array.Copy(pubHash, publicKey, 32);

                var keySet = new KeySet
                {
                    PrivateKey = new CryptoKey(network, privateKey),
                    PublicKey = new CryptoKey(network, publicKey)
                };
                return CryptoResult<KeySet>.Success(keySet);
            });

        _mockWalletUtilities
            .Setup(x => x.PublicKeyToWallet(It.IsAny<byte[]>(), It.IsAny<byte>()))
            .Returns((byte[] pk, byte _) => $"ws1{Convert.ToBase64String(pk)[..20]}");

        // Sign path — deterministic signature so a proceeding sign returns 200 Ok.
        _mockCryptoModule
            .Setup(x => x.SignAsync(
                It.IsAny<byte[]>(),
                It.IsAny<byte>(),
                It.IsAny<byte[]>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CryptoResult<byte[]>.Success(new byte[64]));

        _mockHashProvider
            .Setup(x => x.ComputeHash(It.IsAny<byte[]>(), It.IsAny<HashType>()))
            .Returns(new byte[32]);
    }

    private static HttpContext BuildServiceHttpContext(string clientId)
    {
        var claims = new List<Claim>
        {
            new("token_type", "service"),
            new("client_id", clientId)
        };
        return new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test")) };
    }

    private static SignTransactionRequest ValidSignRequest() => new()
    {
        TransactionData = Convert.ToBase64String(new byte[] { 1, 2, 3, 4 })
    };

    private async Task<IResult> InvokeSignTransactionAsync(string address, SignTransactionRequest request, HttpContext context)
    {
        var method = typeof(WalletEndpoints).GetMethod(
            "SignTransaction",
            BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull("the SignTransaction handler must exist as a private static method");

        var result = method!.Invoke(null, [
            address,
            request,
            _walletManager,
            null, // WalletDbContext? — in-memory storage path
            context,
            NullLogger<Program>.Instance,
            CancellationToken.None
        ]);

        return await (Task<IResult>)result!;
    }

    [Fact]
    public async Task SignTransaction_NonValidatorServiceToken_SystemWallet_Returns403()
    {
        var (wallet, _) = await _walletManager.CreateWalletAsync("System", "ED25519", ValidatorOwner, "system");
        var context = BuildServiceHttpContext(clientId: "service-blueprint");

        var result = await InvokeSignTransactionAsync(wallet.Address, ValidSignRequest(), context);

        result.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.ForbidHttpResult>();
    }

    [Fact]
    public async Task SignTransaction_ValidatorServiceToken_SystemWallet_Proceeds()
    {
        var (wallet, _) = await _walletManager.CreateWalletAsync("System", "ED25519", ValidatorOwner, "system");
        var context = BuildServiceHttpContext(clientId: "validator-service");

        var result = await InvokeSignTransactionAsync(wallet.Address, ValidSignRequest(), context);

        result.Should().NotBeOfType<Microsoft.AspNetCore.Http.HttpResults.ForbidHttpResult>();
        result.GetType().Name.Should().Contain("Ok", "the validator principal must be able to sign its own system wallet");
    }

    [Fact]
    public async Task SignTransaction_NonValidatorServiceToken_OrdinaryWallet_StillProceeds()
    {
        // Regression guard: the service-token bypass for NON-system wallets (e.g. Blueprint Service
        // signing an issuing org's wallet during credential issuance for a different citizen) must be
        // completely unaffected by the #1397 narrowing.
        var (wallet, _) = await _walletManager.CreateWalletAsync("Org", "ED25519", OrdinaryOwner, "tenant1");
        var context = BuildServiceHttpContext(clientId: "service-blueprint");

        var result = await InvokeSignTransactionAsync(wallet.Address, ValidSignRequest(), context);

        result.Should().NotBeOfType<Microsoft.AspNetCore.Http.HttpResults.ForbidHttpResult>();
        result.GetType().Name.Should().Contain("Ok");
    }
}
