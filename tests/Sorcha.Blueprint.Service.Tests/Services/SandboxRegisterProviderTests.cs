// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sorcha.Blueprint.Service.Services.Implementation;
using Sorcha.Register.Models;
using Sorcha.Register.Models.Enums;
using Sorcha.ServiceClients.Register;
using Sorcha.ServiceClients.Wallet;

namespace Sorcha.Blueprint.Service.Tests.Services;

/// <summary>
/// Feature 142 (T026 / T022) — tests for <see cref="SandboxRegisterProvider"/>: lazy creation via
/// the two-phase initiate/finalize ceremony (devMode + sandbox tag + not advertised), per-org
/// reuse (ceremony runs at most once), and isolation between organisations.
/// </summary>
public class SandboxRegisterProviderTests
{
    private readonly Mock<IRegisterServiceClient> _registerClient = new();
    private readonly Mock<IWalletServiceClient> _walletClient = new();

    private int _registerCounter;
    private int _walletCounter;

    private SandboxRegisterProvider CreateProvider() =>
        new(_registerClient.Object, _walletClient.Object, NullLogger<SandboxRegisterProvider>.Instance);

    public SandboxRegisterProviderTests()
    {
        // Owner-wallet mint — one stand-in signer wallet per org.
        _walletClient
            .Setup(w => w.CreateWalletAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new WalletInfo
            {
                Address = $"ws11q-owner-{Interlocked.Increment(ref _walletCounter)}",
                Name = "sandbox-owner",
                PublicKey = "pk",
                Algorithm = "ED25519",
                Status = "Active",
                Tenant = "org",
                Owner = "sandbox-owner",
            });

        // Pre-hashed attestation signing — echoes a deterministic signature/public key.
        _walletClient
            .Setup(w => w.SignTransactionAsync(
                It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<string?>(),
                It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string addr, byte[] _, string? _, bool _, CancellationToken _) =>
                new WalletSignResult
                {
                    Signature = [1, 2, 3, 4],
                    PublicKey = [5, 6, 7, 8],
                    SignedBy = addr,
                    Algorithm = "ED25519",
                });

        // Phase 1 — initiate: return one attestation per supplied owner, with a deterministic
        // register id per call so the cache key is stable.
        _registerClient
            .Setup(c => c.InitiateRegisterCreationAsync(
                It.IsAny<InitiateRegisterCreationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((InitiateRegisterCreationRequest req, CancellationToken _) =>
            {
                var registerId = $"sandbox-reg-{Interlocked.Increment(ref _registerCounter)}";
                return new InitiateRegisterCreationResponse
                {
                    RegisterId = registerId,
                    Nonce = "nonce",
                    ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5),
                    AttestationsToSign = req.Owners.Select(o => new AttestationToSign
                    {
                        UserId = o.UserId,
                        WalletId = o.WalletId,
                        Role = RegisterRole.Owner,
                        AttestationData = new AttestationSigningData
                        {
                            Role = RegisterRole.Owner,
                            Subject = $"did:sorcha:w:{o.WalletId}",
                            RegisterId = registerId,
                            RegisterName = req.Name,
                            GrantedAt = DateTimeOffset.UtcNow,
                        },
                        // Hex-encoded SHA-256-shaped hash (32 bytes).
                        DataToSign = new string('a', 64),
                    }).ToList(),
                };
            });

        // Phase 3 — finalize: echo the register id back.
        _registerClient
            .Setup(c => c.FinalizeRegisterCreationAsync(
                It.IsAny<FinalizeRegisterCreationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((FinalizeRegisterCreationRequest req, CancellationToken _) =>
                new FinalizeRegisterCreationResponse { RegisterId = req.RegisterId, Status = "created" });
    }

    [Fact]
    public async Task GetOrCreate_FirstCall_RunsCeremonyWithDevModeSandboxTagAndNotAdvertised()
    {
        var provider = CreateProvider();

        var registerId = await provider.GetOrCreateSandboxRegisterAsync("org-1");

        registerId.Should().NotBeNullOrWhiteSpace();

        // Initiated with devMode=true, sandbox metadata tag, not advertised, owned by the org.
        _registerClient.Verify(c => c.InitiateRegisterCreationAsync(
            It.Is<InitiateRegisterCreationRequest>(r =>
                r.DevMode
                && r.Advertise == false
                && r.Metadata != null
                && r.Metadata[SandboxRegisterProvider.SandboxMetadataKey] == "true"
                && r.Owners.Count == 1
                && r.Owners[0].UserId == "org-1"),
            It.IsAny<CancellationToken>()), Times.Once);

        // Owner wallet was minted for the org (tenant == org).
        _walletClient.Verify(w => w.CreateWalletAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            "org-1", It.IsAny<CancellationToken>()), Times.Once);

        // Each attestation was signed pre-hashed and finalize was called once.
        _walletClient.Verify(w => w.SignTransactionAsync(
            It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<string?>(),
            true, It.IsAny<CancellationToken>()), Times.Once);
        _registerClient.Verify(c => c.FinalizeRegisterCreationAsync(
            It.IsAny<FinalizeRegisterCreationRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetOrCreate_SameOrgTwice_ReusesAndRunsCeremonyOnlyOnce()
    {
        var provider = CreateProvider();

        var first = await provider.GetOrCreateSandboxRegisterAsync("org-1");
        var second = await provider.GetOrCreateSandboxRegisterAsync("org-1");

        second.Should().Be(first);
        _registerClient.Verify(c => c.InitiateRegisterCreationAsync(
            It.IsAny<InitiateRegisterCreationRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        _registerClient.Verify(c => c.FinalizeRegisterCreationAsync(
            It.IsAny<FinalizeRegisterCreationRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        // Owner wallet minted exactly once (reused on the second call).
        _walletClient.Verify(w => w.CreateWalletAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetOrCreate_DifferentOrgs_CreatesSeparateRegisters()
    {
        var provider = CreateProvider();

        var orgA = await provider.GetOrCreateSandboxRegisterAsync("org-A");
        var orgB = await provider.GetOrCreateSandboxRegisterAsync("org-B");

        orgA.Should().NotBe(orgB);
        _registerClient.Verify(c => c.InitiateRegisterCreationAsync(
            It.IsAny<InitiateRegisterCreationRequest>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        _registerClient.Verify(c => c.FinalizeRegisterCreationAsync(
            It.IsAny<FinalizeRegisterCreationRequest>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task GetOrCreate_ConcurrentSameOrg_RunsCeremonyOnlyOnce()
    {
        var provider = CreateProvider();

        var tasks = Enumerable.Range(0, 16)
            .Select(_ => provider.GetOrCreateSandboxRegisterAsync("org-race"))
            .ToArray();
        var ids = await Task.WhenAll(tasks);

        ids.Distinct().Should().HaveCount(1);
        _registerClient.Verify(c => c.InitiateRegisterCreationAsync(
            It.IsAny<InitiateRegisterCreationRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        _registerClient.Verify(c => c.FinalizeRegisterCreationAsync(
            It.IsAny<FinalizeRegisterCreationRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        _walletClient.Verify(w => w.CreateWalletAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetOrCreate_BlankOrg_Throws()
    {
        var provider = CreateProvider();

        var act = () => provider.GetOrCreateSandboxRegisterAsync("  ");

        await act.Should().ThrowAsync<ArgumentException>();
    }
}
