// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Sorcha.Register.Core.Services;
using Sorcha.Register.Models;
using Sorcha.Register.Models.Enums;
using Sorcha.Register.Service.Services;
using Sorcha.ServiceClients.Wallet;
using Sorcha.Wallet.Contracts.Constants;
using Xunit;

namespace Sorcha.Register.Service.Tests.Unit;

/// <summary>
/// Feature 189 (T020) — governance control transactions are signed by an ORGANISATION on the
/// register's roster, at slot 100, not by the node's system wallet.
/// </summary>
public class GovernanceSigningServiceTests
{
    private const string RegisterId = "abc123def456abc123def456abc123de";
    private const string OwnerWallet = "ws11qowner00000000000000000000000000000000000000000000000000";
    private const string AdminWallet = "ws11qadmin00000000000000000000000000000000000000000000000000";

    private readonly Mock<IGovernanceRosterService> _rosterMock = new();
    private readonly Mock<IWalletServiceClient> _walletMock = new();
    private readonly GovernanceSigningService _sut;

    private string? _capturedWallet;
    private string? _capturedDerivationPath;
    private bool? _capturedPreHashed;
    private byte[]? _capturedData;

    public GovernanceSigningServiceTests()
    {
        _walletMock
            .Setup(x => x.SignTransactionAsync(
                It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<string?>(),
                It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Callback((string addr, byte[] data, string? path, bool pre, CancellationToken _) =>
            {
                _capturedWallet = addr;
                _capturedData = data;
                _capturedDerivationPath = path;
                _capturedPreHashed = pre;
            })
            .ReturnsAsync(new WalletSignResult
            {
                Signature = [1, 2, 3, 4],
                PublicKey = [5, 6, 7, 8],
                Algorithm = "ED25519",
                SignedBy = OwnerWallet
            });

        _sut = new GovernanceSigningService(
            _rosterMock.Object, _walletMock.Object, Mock.Of<ILogger<GovernanceSigningService>>());
    }

    private void SetRoster(params (string subject, RegisterRole role)[] members)
    {
        _rosterMock.Setup(x => x.GetCurrentRosterAsync(RegisterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AdminRoster
            {
                RegisterId = RegisterId,
                ControlRecord = new RegisterControlRecord
                {
                    RegisterId = RegisterId,
                    Name = "Test",
                    CreatedAt = DateTimeOffset.UtcNow,
                    Attestations = members.Select(m => new RegisterAttestation
                    {
                        Role = m.role,
                        Subject = m.subject,
                        PublicKey = Convert.ToBase64String(new byte[32]),
                        Signature = Convert.ToBase64String(new byte[64]),
                        Algorithm = SignatureAlgorithm.ED25519,
                        GrantedAt = DateTimeOffset.UtcNow
                    }).ToList()
                },
                ControlTransactionCount = 1
            });
    }

    [Fact]
    public async Task SignAsync_SignsAtSlot100_NotThePrimaryKey()
    {
        SetRoster(($"did:sorcha:w:{OwnerWallet}", RegisterRole.Owner));

        await _sut.SignAsync(RegisterId, "tx-1", "hash-1");

        // The roster records the slot-100 key. Signing with the wallet's primary key (which is what
        // a null derivation path selects) reproduces the original "submitter not found in roster".
        _capturedDerivationPath.Should().Be(SorchaDerivationPaths.RegisterAttestation);
        _capturedPreHashed.Should().BeTrue();
    }

    [Fact]
    public async Task SignAsync_ResolvesWalletAddressFromRosterSubject()
    {
        SetRoster(($"did:sorcha:w:{OwnerWallet}", RegisterRole.Owner));

        var result = await _sut.SignAsync(RegisterId, "tx-1", "hash-1");

        _capturedWallet.Should().Be(OwnerWallet);
        result.WalletAddress.Should().Be(OwnerWallet);
        result.Subject.Should().Be($"did:sorcha:w:{OwnerWallet}");
    }

    [Fact]
    public async Task SignAsync_SignsTheBytesTheValidatorRecomputes()
    {
        SetRoster(($"did:sorcha:w:{OwnerWallet}", RegisterRole.Owner));

        await _sut.SignAsync(RegisterId, "tx-abc", "hash-def");

        // VerifySignaturesAsync recomputes SHA-256(UTF-8("{TxId}:{PayloadHash}")). Diverging here
        // yields a signature that verifies nowhere, and a rejection naming neither cause.
        var expected = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes("tx-abc:hash-def"));
        _capturedData.Should().Equal(expected);
    }

    [Fact]
    public async Task SignAsync_PrefersOwnerOverAdmin()
    {
        SetRoster(
            ($"did:sorcha:w:{AdminWallet}", RegisterRole.Admin),
            ($"did:sorcha:w:{OwnerWallet}", RegisterRole.Owner));

        var result = await _sut.SignAsync(RegisterId, "tx-1", "hash-1");

        result.WalletAddress.Should().Be(OwnerWallet);
    }

    [Fact]
    public async Task SignAsync_PreferredSubject_SignsAsThatOrganisation()
    {
        SetRoster(
            ($"did:sorcha:w:{OwnerWallet}", RegisterRole.Owner),
            ($"did:sorcha:w:{AdminWallet}", RegisterRole.Admin));

        // Consortium approvals: each organisation signs its own, so the caller names the signer.
        var result = await _sut.SignAsync(RegisterId, "tx-1", "hash-1",
            preferredSubject: $"did:sorcha:w:{AdminWallet}");

        result.WalletAddress.Should().Be(AdminWallet);
    }

    [Fact]
    public async Task SignAsync_NoRoster_ThrowsWithAnActionableReason()
    {
        _rosterMock.Setup(x => x.GetCurrentRosterAsync(RegisterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((AdminRoster?)null);

        var act = () => _sut.SignAsync(RegisterId, "tx-1", "hash-1");

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*no governance roster*");
    }

    [Fact]
    public async Task SignAsync_NoGovernanceRoleOnRoster_Throws()
    {
        SetRoster(($"did:sorcha:w:{OwnerWallet}", RegisterRole.Auditor));

        var act = () => _sut.SignAsync(RegisterId, "tx-1", "hash-1");

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*governance role*");
        _walletMock.Verify(x => x.SignTransactionAsync(
            It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<string?>(),
            It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SignAsync_GenesisStyleSubjectWithNoWalletAddress_ThrowsClearly()
    {
        // The system register's ceremony mints did:sorcha:genesis:{fingerprint}, not a wallet DID,
        // so there is no key this path can sign with. That is why transferring system-register
        // ownership is deferred to US4 rather than assumed to work.
        SetRoster(("did:sorcha:genesis:60d8be0f8846aa31142cd39e928910eb", RegisterRole.Owner));

        var act = () => _sut.SignAsync(RegisterId, "tx-1", "hash-1");

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*carries no wallet address*");
    }
}
